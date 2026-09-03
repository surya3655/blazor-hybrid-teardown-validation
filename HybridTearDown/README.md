# Blazor Hybrid teardown validation

A .NET MAUI Blazor Hybrid test harness for
[dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813) —
*Blazor Hybrid teardown while JavaScript calls are in flight*.

The scenario validates that disposing an `IJSObjectReference` after the WebView
has gone no longer throws `JSDisconnectedException`, and that a Hybrid app tears
down promptly however busy the page was.

**Results:** [TEST-REPORT.md](TEST-REPORT.md) ·
**Evidence:** [`evidence/log/`](evidence/log) ·
[`evidence/video/`](evidence/video)

## What the app contains

Four stress pages, each busy in a way that makes teardown awkward:

| Page | Route | Behaviour |
| --- | --- | --- |
| Module loop | `/module-loop` | Holds a JS module reference and calls into it every 100 ms |
| Slow call | `/slow-call` | Starts a 15-second JavaScript promise and does not await it |
| Timer | `/timer` | A `PeriodicTimer` invoking JavaScript four times a second |
| JS callback | `/callback` | Registers a `DotNetObjectReference` that JavaScript calls every 200 ms |
| JS callback (uncaught) | `/callback-uncaught` | Identical to the above, with the disposal-time catch removed. Used by one case only. |

Alongside them, a **native MAUI page** replaces the entire `BlazorWebView` host,
so teardown can be triggered by destroying the host rather than by route
navigation.

## Running it

```bash
# Android
dotnet run -f net11.0-android -c Debug

# Windows
dotnet run -f net11.0-windows10.0.19041.0 -c Debug
```

Requires .NET 11 Preview 7 or later and the MAUI workloads.

Reading the evidence log while the app runs:

```bash
# Android
adb logcat -s DOTNET:I

# Windows
Get-Content "$env:LOCALAPPDATA\Packages\HybridTearDown_ph1m9x8skttmg\LocalState\evidence.log" -Wait -Tail 40
```

## Instrumentation

`Services/TeardownDiagnostics.cs` records the evidence the test cases are scored
against.

| Record | Meaning |
| --- | --- |
| `[Module] X acquired JS module reference.` | The component holds a live module |
| `[Dispose] X entered. … WebView destroyed first: True/False.` | Disposal started, and whether the WebView had already gone |
| `[Cleanup] X completed after N …` | The cleanup body ran to completion |
| `[Dispose] X returned after N ms. … Module disposed cleanly: True/False.` | Disposal finished, and whether module disposal returned normally |
| `[WebView] BlazorWebView handler disconnected. WebView destroyed.` | The WebView is genuinely gone |
| `[WebView] BlazorWebView host created. Cycle N.` | A fresh host was built |
| `[Rotation] Orientation changed to … Activity recreated: True/False.` | A device rotation, and whether Android rebuilt the activity |
| `[Lifecycle] Window destroying / destroyed. Shutdown took N ms.` | App close |
| `[Memory] Sample (periodic) …` | Working set and GC heap, every 5 seconds |
| `[Memory] Retained after collection …` | Live object counts after a forced collection and finalizer drain |
| `[Error] UnhandledException` / `UnobservedTaskException` | Any occurrence is a finding |

Two flags read together decide a result:

| Combination | Meaning |
| --- | --- |
| `WebView destroyed first: True` + `Module disposed cleanly: True` | The fix working. The WebView had gone and disposal still returned normally. |
| `WebView destroyed first: False` + `Module disposed cleanly: True` | Ordinary route navigation. A pass, but it does not exercise the fix. |
| Either, **plus** an `[Error]` record | Failure — something escaped. |

## Disposal guarding

The stress pages **do** catch `JSDisconnectedException` around
`_module.DisposeAsync()`, and each records the outcome:

```csharp
try
{
    await _module.DisposeAsync().AsTask().WaitAsync(TeardownDiagnostics.DisposeTimeout);
    moduleDisposed = true;          // set ONLY on a normal return
}
catch (JSDisconnectedException)
{
    // leaves moduleDisposed false
}
```

The catch does not weaken the test. The pass criterion is the
`Module disposed cleanly` flag, which is set **only** when `DisposeAsync()`
returns without throwing. `True` is therefore positive evidence that no
exception occurred; had the fix been absent the flag would read `False` and the
catch block would have run.

`CallbackStressUncaught.razor` is the control: identical code with the catch
removed. It does throw, confirming the flag distinguishes the two states rather
than masking them.

Every `await` inside a `DisposeAsync` is bounded by `DisposeTimeout` (2 seconds),
so a call that never returns surfaces as a timeout rather than an indefinite
hang.

### Build warnings are intentional

An Android Debug build reports **nine `BL0016` warnings**:

```
BL0016: JS interop call 'InvokeAsync' is not guarded with a try/catch block.
```

These refer to the interop calls made during *active work* — inside the loops,
timers and callbacks. They are deliberately unguarded: guarding them would stop
teardown from occurring while calls are genuinely in flight, which is the entire
scenario under test. Only the disposal paths are guarded, as described above.

## Repository layout

```
Components/Pages/        the four stress pages, plus the uncaught control
Services/                TeardownDiagnostics, NativePageNavigator, EvidenceLog
Platforms/Android/       MainActivity, including the rotation instrumentation
wwwroot/js/teardown.js   the JavaScript side of every scenario
evidence/log/            one evidence log per test case
evidence/video/          screen recordings, zipped
TEST-REPORT.md           results, findings and open items
```

## Test cases

Eight cases, TC01–TC08, run on Windows/WebView2 and Android/Android WebView in
Debug with Hot Reload disabled. TC03 is the case the fix exists for: disposing a
held `IJSObjectReference` after the WebView has already been destroyed.

Full results, per-case evidence links and open items are in
[TEST-REPORT.md](TEST-REPORT.md).

## Rotation configuration

`Platforms/Android/MainActivity.cs` ships with `ConfigChanges.Orientation |
ScreenSize` set, so Android hands the app a configuration change and the activity
survives a rotation.

To force Android to destroy and rebuild the activity on every rotation —
destroying the WebView under a live component — reduce the attribute to:

```csharp
ConfigurationChanges = ConfigChanges.UiMode | ConfigChanges.Density
```

Restore the original attribute afterwards; that is a test configuration, not how
the app ships.
