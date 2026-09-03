**Test Plan: Blazor Hybrid Teardown With In-Flight JavaScript**

Based on [dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813).

**Objective**

Verify that a .NET MAUI Blazor Hybrid application tears down promptly and cleanly while JavaScript interop calls, timers, and JavaScript-to-.NET callbacks are active.

In particular:

- Component cleanup always runs.
- Teardown never hangs.
- In-flight calls terminate quietly.
- Disposing an `IJSObjectReference` after the WebView disconnects does not surface `JSDisconnectedException`.
- Repeated Blazor/native navigation does not cause sustained memory growth.
- The application remains functional after suspension and restoration.

**Prerequisites**

- .NET 11 Preview 7 or later.
- MAUI workloads installed.
- WebView2 runtime on Windows.
- Android device or emulator for cross-platform coverage.
- Debug output or IDE Debug Console visible.
- Release publishing tools for Windows and Android.
- Optional memory profiler or OS process monitor.

Record before testing:

- .NET SDK version.
- MAUI workload version.
- Operating system and version.
- Device or emulator model.
- Android WebView or WebView2 version.
- Debug or Release configuration.
- IDE or command-line launch method.
- Hot Reload enabled or disabled.
- Trim/AOT settings.

**Build Validation**

Run:

```powershell
dotnet restore

dotnet build .\HybridTearDown.csproj `
  -f net11.0-windows10.0.19041.0

dotnet build .\HybridTearDown.csproj `
  -f net11.0-android
```

Pass conditions:

- Restore succeeds.
- Both available targets compile.
- No project errors.
- No unexpected MAUI or package-version warnings.

**Baseline Check**

1. Start the application.
2. Confirm the overview page renders.
3. Open every stress page.
4. Confirm counters or status indicators update.
5. Open the native screen.
6. Return to the Blazor screen.
7. Confirm Blazor remains interactive.
8. Watch debug output for `[Cleanup]` and `[NativeNavigation]` records.

Expected:

- All pages load and operate.
- Native/Blazor transitions work.
- No exceptions occur during ordinary navigation.

**Scenario 1: Loaded JavaScript Module Loop**

Page: **Module loop**

1. Open the page.
2. Confirm the completed-call counter continuously increases.
3. Navigate to another Blazor page while calls are active.
4. Repeat using each other navigation tab.
5. Re-enter the page and immediately select **Native screen**.
6. Return to Blazor and repeat rapidly.
7. Close the application while the loop is active.
8. Background the application while the loop is active.
9. Restore it and confirm the application still works.
10. Background it again and terminate it from the OS.

Expected:

- `ModuleLoop` cleanup is logged promptly.
- The loop is cancelled.
- Its module reference is disposed.
- Navigation and shutdown do not hang.
- No `JSDisconnectedException` or unobserved task exception appears.

**Scenario 2: Unawaited Slow JavaScript Call**

Page: **Slow call**

1. Select **Start 15-second call**.
2. Immediately navigate to another Blazor page.
3. Repeat and immediately switch to the native screen.
4. Repeat and use the operating-system back action.
5. Repeat and close the application.
6. Repeat, background the application, and restore it before 15 seconds.
7. Repeat, background the application, and terminate it.
8. Allow one call to finish normally as a control.
9. Repeat with several rapid enter/start/leave cycles.

Expected:

- The event handler returns without waiting 15 seconds.
- Teardown does not wait for the JavaScript promise.
- `SlowCall` cleanup runs promptly.
- Disposing the module during the active call does not surface an exception.
- No hang, crash, or `JSDisconnectedException` appears.

This is the highest-priority regression test.

**Scenario 3: Persistent Timer**

Page: **Timer**

1. Open the page.
2. Confirm the timer counter updates approximately four times per second.
3. Navigate away during active updates.
4. Rapidly alternate between the timer and another Blazor page.
5. Switch to the native screen while the timer is active.
6. Return to Blazor and repeat.
7. Rotate the device repeatedly while the timer runs.
8. Background and restore the application.
9. Close the application while timer and JS activity are active.

Expected:

- `TimerStress` cleanup is logged.
- The cancellation token and timer stop promptly.
- No state update occurs against a disposed component.
- Rotation does not break the page.
- Restore produces a functional app.
- No teardown exception appears.

**Scenario 4: JavaScript-to-.NET Callbacks**

Page: **JS callback**

1. Open the page.
2. Confirm the callback counter increases.
3. Navigate away while callbacks are arriving.
4. Re-enter and immediately switch to the native screen.
5. Repeat native/Blazor navigation rapidly.
6. Use the OS back action while callbacks are active.
7. Rotate the device during callbacks.
8. Background and restore the application.
9. Close the application while callbacks are arriving.
10. Background and terminate the application.

Expected:

- The JavaScript interval is stopped.
- The JS module and `DotNetObjectReference` are disposed.
- `CallbackStress` cleanup is logged.
- JavaScript does not continue invoking the disposed .NET target.
- No callback, disconnection, or unobserved-promise error appears.

**Cross-Page Navigation Matrix**

Exercise every transition while the source page is busy:

| From | To test |
|---|---|
| Module loop | Slow call, Timer, Callback, Overview, Native |
| Slow call | Module loop, Timer, Callback, Overview, Native |
| Timer | Module loop, Slow call, Callback, Overview, Native |
| Callback | Module loop, Slow call, Timer, Overview, Native |
| Native | Blazor overview, then every busy page |

Repeat transitions using:

- In-app navigation.
- Native-screen buttons.
- Operating-system back button or gesture.
- Window close button.
- Task switcher or OS application termination.

**Lifecycle Tests**

Run each lifecycle action once from every busy page:

1. Background and restore.
2. Background and close.
3. Close directly while active.
4. Use OS back.
5. Rotate while active.
6. Resize the Windows window while active.
7. Minimize and restore on Windows.
8. Lock and unlock the device when practical.

Expected:

- Teardown is prompt.
- Restored pages are operational.
- No stale callback, timer, or JS module remains.
- No disconnection exception reaches debug output.

**Repeated-Cycle and Memory Test**

1. Record baseline process memory after startup stabilizes.
2. Open a busy page.
3. Allow it to run for 3–5 seconds.
4. Switch to the native page.
5. Return to Blazor.
6. Repeat for at least 25 cycles per stress page.
7. Run a combined 100-cycle test across all four pages.
8. Force or wait for normal garbage collections where appropriate.
9. Record memory after 10, 25, 50, and 100 cycles.

Expected:

- Temporary peaks are acceptable.
- Memory should stabilize rather than grow continuously.
- Old `BlazorWebView`, page, JS module, timer, and callback objects should become collectible.
- Navigation speed should not degrade.
- Debug output should continue showing cleanup for every cycle.

**Platform Matrix**

Run the complete plan on at least:

| Platform | Embedded browser |
|---|---|
| Windows | WebView2 |
| Android | Android System WebView |

Additional platforms, when available:

- iOS with WKWebView.
- macOS/Mac Catalyst with WKWebView.

A pass on one platform and failure on another is a valid finding and must be reported separately.

**Execution Configuration Matrix**

Run representative high-risk scenarios under each applicable configuration:

| Configuration | Required coverage |
|---|---|
| Debug | Complete test plan |
| Release | All four pages plus close/background tests |
| IDE launch | Complete or representative plan |
| Command-line launch | All four teardown paths |
| Hot Reload enabled | Navigation, native transition, close during slow call |
| Published output | All four pages and application shutdown |
| Trimming enabled | All four pages, especially `[JSInvokable]` callback |
| Ahead-of-time compilation | Slow call, callback, shutdown, native cycles |
| Existing .NET 10 app upgraded to .NET 11 | Repeat core teardown suite |

Publish examples:

```powershell
dotnet publish .\HybridTearDown.csproj `
  -f net11.0-windows10.0.19041.0 `
  -c Release

dotnet publish .\HybridTearDown.csproj `
  -f net11.0-android `
  -c Release
```

The issue also lists multiple server instances, proxies, and containers. These apply to hosted Blazor Server/Web App configurations, not this standalone MAUI Hybrid process. Mark them **not applicable for the Hybrid sample**, rather than silently omitting them.

**Other Blazor Configurations Listed by the Issue**

Issue #68813 requests broader validation across:

- Blazor Web App with Static SSR.
- Interactive Server.
- Interactive WebAssembly.
- Interactive Auto.
- Standalone WebAssembly.
- MAUI Blazor Hybrid.

This repository directly validates **MAUI Blazor Hybrid**. The web configurations require separate host projects because their teardown and transport mechanisms differ.

**Debug Output Review**

Search output for:

```text
[Cleanup]
[NativeNavigation]
JSDisconnectedException
ObjectDisposedException
TaskCanceledException
unobserved
Unhandled
WebView
JavaScript
```

Expected cleanup records:

```text
[Cleanup] ModuleLoop completed
[Cleanup] SlowCall completed
[Cleanup] TimerStress completed
[Cleanup] CallbackStress completed
```

`TaskCanceledException` or `OperationCanceledException` is acceptable only when intentionally handled and not surfaced as an unhandled error.

**Must-Pass Criteria**

- Leaving every busy page runs its cleanup without hanging.
- Closing during an in-flight slow JS call terminates promptly.
- No in-flight exception appears in debug output.
- `IJSObjectReference.DisposeAsync` never surfaces `JSDisconnectedException` after WebView disconnection.
- Repeated Blazor/native transitions do not produce sustained memory growth.
- Returning from the background leaves the app usable.
- Rotation and window resizing do not break active pages.
- OS back navigation tears down active work cleanly.
- Results are checked on more than one platform when available.

**Result Report Template**

```markdown
## Environment

- SDK:
- MAUI workload:
- OS/device:
- Embedded browser:
- Configuration:
- Launch method:
- Hot Reload:
- Trimming/AOT:
- Published build:

## Results

| Scenario | Navigation | Native transition | Background/restore | Close in flight | Cleanup logged | Exceptions | Result |
|---|---|---|---|---|---|---|---|
| Module loop | | | | | | | |
| Slow call | | | | | | | |
| Timer | | | | | | | |
| JS callback | | | | | | | |

## Repeated-cycle results

- Number of cycles:
- Initial memory:
- Peak memory:
- Stabilized memory:
- Cleanup count:
- Observed degradation:

## Findings

- Teardown duration:
- Exceptions/warnings:
- Platform-specific differences:
- Reproduction steps for failures:
- Logs/screenshots/dumps:
```