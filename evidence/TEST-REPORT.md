# Blazor Hybrid teardown validation report

- **Issue:** [dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813)
- **Test dates:** 2026-09-02 to 2026-09-03
- **Build:** .NET `11.0.0-preview.7.26381.103`, Debug configuration
- **Platforms:** Windows / WebView2 and Android / Android WebView
- **Harness:** [blazor-hybrid-teardown-validation](https://github.com/surya3655/blazor-hybrid-teardown-validation)
- **Scope:** Hybrid (MAUI), Debug

## Verdict

**The fix holds.** Across 41 disposals that ran after the WebView had already been
destroyed — 20 on Windows, 21 on Android — every `IJSObjectReference` disposal
returned normally in 1–178 ms. No `JSDisconnectedException`, no unhandled or
unobserved exception, and no hang appeared in any case on either platform.

Closing the app during an unawaited 15-second JavaScript promise terminated in
under two seconds on both platforms and never waited for the promise.

Two behaviours outside the scope of the fix were observed and are reported below:
component cleanup does not run on normal app close, and Android activity
recreation abandons components rather than disposing them.

## Results

All evidence logs are in [`evidence/log/`](https://github.com/surya3655/blazor-hybrid-teardown-validation/tree/main/evidence/log).

| ID | Case | Windows | Android |
| --- | --- | --- | --- |
| TC01 | Route navigation while module calls are in flight | **PASS** — 11 iterations, max disposal 565 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc01-windows.txt) | **PASS** — 10 iterations, max 367 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc01-android.txt) |
| TC02 | Replace the Blazor host with a native MAUI page | **PASS** — 20 cycles, 22/22 cleanup, max 32 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc02-windows.txt) | **PASS** — 24 cycles, 24/24 cleanup, max 37 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc02-android.txt) |
| TC03 | `IJSObjectReference` disposal after the WebView is gone | **PASS** — 20 disposals, all `destroyed first: True`, max 18 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc03-windows.txt) | **PASS** — 21 disposals, all `destroyed first: True`, max 178 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc03-android.txt) |
| TC04 | Close during the long JavaScript promise | **PASS** — 5/5 closed with the call in flight · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc04-windows.txt) | **PASS** — 5/5 in flight, teardown 759–1875 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc04-android.txt) |
| TC05 | Interop call initiated inside `DisposeAsync` | **PASS** — guarded 10 teardowns / 0 errors; unguarded 9 / 3 errors · [A](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-a-windows.txt) · [B](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-b-windows.txt) | **PASS** — guarded 10 / 0 errors; unguarded 10 / 6 errors · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-android.txt) |
| TC06 | Background 60 s and resume during active work | **PASS** — 4 pages, app responsive after each resume · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc06-windows.txt) | **PASS** — 4 pages, app responsive after each resume · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc06-android.txt) |
| TC07 | Background, then normal close | **PASS** — 4 pages, closed from the taskbar while backgrounded · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc07-windows.txt) | **PASS** — 4 pages, close 667–1019 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc07-android.txt) |
| TC08 | Rotation during active work | N/A — desktop windows do not rotate | **PASS** — see below · [A](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc08-a-android.txt) · [B](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc08-b-android.txt) |

No `JSDisconnectedException`, `UnhandledException`, `Error` or `Critical` record
attributable to teardown appears in any passing case. No stale
`DotNetObjectReference` callback (`There is no tracked object with id …`) appears
in any case.

### TC08 — the two rotation configurations

| Part | `ConfigurationChanges` | Rotations | Result |
| --- | --- | --- | --- |
| A | includes `Orientation \| ScreenSize` | 36 | Activity handled the config change; the WebView was never destroyed. No duplicate work source, no error. |
| B | `Orientation`/`ScreenSize` removed | 22 | Android destroyed and rebuilt the activity on every rotation. No error, no hang, app usable throughout. |

Part B is the only scenario in this report where the operating system destroyed
the WebView on its own timing rather than in response to a deliberate action.

## Findings

### 1. Component cleanup does not run on normal app close

Observed in TC04 (both platforms) and TC07 (both platforms): a component holding
a live JS module produced no `[Cleanup]` record when the app was closed normally.
Cleanup runs correctly on route navigation and on host replacement.

Nothing throws and nothing hangs, so this is not a regression of the fix under
test. It is recorded because a teardown path that never disposes cannot exercise
the disposal guarantee.

### 2. Android activity recreation abandons components

In TC08 Part B, 22 rotations produced **7 module acquisitions and 1 cleanup**. The
page handler is never transitioned to null on activity recreation, so no managed
teardown runs.

After a forced collection and finalizer drain, retained objects were
`MainPage=22/23`, `BlazorWebView=22/23`, `Components=16/16`, `Modules=16/16`;
working set rose from 342 MB to 409 MB across the run
([log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc08-b-android.txt)).

Again outside the fix's scope — disposal never runs on this path — but it means
the "teardown is clean" guarantee has a gap on Android rotation.

### 3. JavaScript intervals are throttled while backgrounded; .NET timers are not

In TC06 on both platforms, a `PeriodicTimer` firing every 250 ms continued at its
full rate through a 60-second background period (Windows 283 and 376 ticks,
Android 365 ticks — each matching elapsed time exactly). A JavaScript
`setInterval` at 200 ms dropped to roughly 1 Hz over the same window (Windows 142
callbacks, Android 153).

This is expected embedded-browser behaviour, recorded for completeness.

### Expected differences between platforms

None material. Disposal is 2–4× slower on Android (max 178 ms vs 18 ms on
Windows for TC03) but well inside limits, and every pass/fail outcome is
identical across the two WebView engines.
