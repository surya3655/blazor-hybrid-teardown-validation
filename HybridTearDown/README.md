# Blazor Hybrid teardown validation

Reproduction app for [dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813). It targets .NET 11 Preview 7 and keeps the issue's behaviors separate so each teardown path can be identified in debug output.

## Scenarios

| Page | Work kept active | Cleanup |
| --- | --- | --- |
| Module loop | Loaded `IJSObjectReference` called every 100 ms | Cancels loop, disposes module |
| Slow call | Unawaited 15-second JavaScript promise | Disposes module while call is in flight |
| Timer | `PeriodicTimer` invokes JavaScript every 250 ms | Cancels and disposes timer |
| JS callback | JavaScript interval calls a `[JSInvokable]` method | Stops interval, disposes module and .NET reference |

The native-screen button replaces the window's Blazor page with a native MAUI page. Returning creates a fresh `BlazorWebView`, making repeated host teardown measurable without retaining the old page.

## Prerequisites

- .NET SDK `11.0.100-preview.7.26381.103` or a compatible later Preview 7 build
- MAUI workload for at least one target: `dotnet workload install maui`
- Windows developer mode for an unpackaged Windows run, or an Android device/emulator

This machine reported a pending reboot while installing the Preview 7 MAUI workload. Restart Windows, then run:

```powershell
dotnet workload restore
dotnet restore
dotnet build -f net11.0-windows10.0.19041.0
```

Run the app through the VS Code MAUI debugger and watch the Debug Console. Successful component cleanup is marked with `[Cleanup]`; native host replacement is marked with `[NativeNavigation]`.

## Evidence without the VS Code debugger

The app writes the same validation evidence to a persistent UTF-8 file, so an attached debugger is not required. For the packaged Windows app the file is:

```text
%LOCALAPPDATA%\Packages\HybridTearDown_ph1m9x8skttmg\LocalState\evidence.log
```

The native screen also displays the resolved evidence path. Start a fresh run, monitor it live, and copy the final evidence with:

```powershell
$log = Join-Path $env:LOCALAPPDATA 'Packages\HybridTearDown_ph1m9x8skttmg\LocalState\evidence.log'
Remove-Item $log -ErrorAction SilentlyContinue
dotnet run -c Debug --project .\HybridTearDown.csproj -f net11.0-windows10.0.19041.0
```

In a second terminal:

```powershell
$log = Join-Path $env:LOCALAPPDATA 'Packages\HybridTearDown_ph1m9x8skttmg\LocalState\evidence.log'
Get-Content $log -Wait
```

After completing the test cycle:

```powershell
$log = Join-Path $env:LOCALAPPDATA 'Packages\HybridTearDown_ph1m9x8skttmg\LocalState\evidence.log'
Select-String -Path $log -Pattern '\[Cleanup\]|NativeNavigation|Lifecycle|JSDisconnectedException|Unhandled|Error|Critical'
Copy-Item $log ".\evidence-$((Get-Date).ToString('yyyyMMdd-HHmmss')).log"
```

The log records session startup, window activation/background/resume/destruction, native/Blazor transitions, component cleanup, framework messages at Information or higher, unhandled exceptions, and unobserved task exceptions. Logging does not catch or suppress `JSDisconnectedException`, so the validation behavior remains unchanged.

## Validation passes

For each of the four pages:

1. Navigate away while its indicator shows active work.
2. Re-enter it, then switch to the native page and back several times.
3. Use the operating-system back gesture or button.
4. Rotate the device while work is active.
5. Background and restore the app; repeat, then background and close it.
6. Close the app during the slow 15-second call.
7. Repeat the native/Blazor cycle at least 25 times while watching process memory.

Repeat on Windows and Android when both are available because WebView2 and Android WebView have different teardown behavior. Also repeat a representative pass with Hot Reload enabled and from published output.

## Pass criteria

- Every page writes its `[Cleanup]` record promptly when left.
- App shutdown does not wait for the 15-second promise.
- Debug output contains no in-flight exception, especially no `JSDisconnectedException` from `IJSObjectReference.DisposeAsync`.
- Process memory stabilizes across repeated native/Blazor cycles.
- The app remains functional after returning from the background.

The components intentionally do not catch `JSDisconnectedException`. Catching it in the sample would hide the runtime regression this app validates.