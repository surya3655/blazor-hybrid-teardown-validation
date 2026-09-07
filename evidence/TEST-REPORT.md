# Blazor Hybrid teardown validation report

- **Issue:** [dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813)
- **Test dates:** 2026-09-02 to 2026-09-07
- **Configuration:** Hybrid (MAUI) only, on Windows/WebView2 and Android/Android WebView
- **Harness:** [blazor-hybrid-teardown-validation](https://github.com/surya3655/blazor-hybrid-teardown-validation)

## Verdict: core fix passed, two retention findings open

**The disposal fix is substantiated on both platforms.** Across 30 disposals that
ran after the WebView had already been destroyed — 15 on Windows, 15 on Android —
every `IJSObjectReference` disposal returned normally in 1–18 ms with no
`JSDisconnectedException`, no unhandled or unobserved exception, and no hang
(TC03). Closing the app during an unawaited 15-second promise terminated promptly
on both platforms and never waited for the promise (TC04). Teardown triggered by
the operating system's back control behaves the same way: TC10 exercised system
Back on all four busy pages and across seven native-page returns, with no
exception and no failed host rebuild.

**Two retention findings remain unresolved.** Both fall outside what the fix
governs — the fix determines what happens when disposal runs while a call is in
flight, not whether the retained graph is released afterwards — but neither has an
identified cause, and they are reported as findings rather than observations:

1. **Component retention after in-flight teardown**, on both platforms and in the
   shipping configuration. Every component disposed while a JavaScript call was in
   flight was retained, with its module: 8 of 36 on Windows, 8 of 34 on Android.
2. **Host retention under Android activity recreation**, in the forced-recreation
   configuration only. 22 of 23 `MainPage` and `BlazorWebView` instances retained,
   along with all components, modules and timers.

Both are detailed under Findings.

**No `JSDisconnectedException`, unhandled exception, unobserved task exception or
hang occurred in any case**, with one deliberate exception: the TC05 unguarded
control, where three such exceptions are the pass condition rather than a defect.

This report does not claim that every condition passed or that no defect was
found. The core scenarios the issue lists as "must hold" are carried by this
evidence; the two findings above are not.

## Environment

| Field | Windows | Android |
| --- | --- | --- |
| Runtime | .NET 11.0.0-preview.7.26381.103 | .NET 11.0.0-preview.7.26381.103 |
| SDK | 11.0.100-preview.7.26410.2 | 11.0.100-preview.7.26410.2 |
| MAUI workload | supplied by the SDK | maui-android 11.0.0-preview.7.26406.9 |
| OS | Windows 11 24H2, build 26100.9106 | Android 13 |
| Device | Desktop | Emulator, sdk_gphone64_x86_64 |
| WebView | WebView2, Chromium 152.0.7977.65 | Android System WebView 109.0.5414.123 |
| Configuration | Debug, Hot Reload disabled | Debug, Hot Reload disabled |

Build and launch commands are in [README.md](README.md#running-it).

**The two WebView engines are far apart in age** — Chromium 152 on Windows
against Chromium 109 in the emulator, roughly three years between them. The issue
notes that the embedded browser differs between platforms and that covering more
than one is worthwhile; here the gap is unusually wide and every result was
identical across both. A device carrying a current Android System WebView would
strengthen the Android side.

## Build cleanliness

| Platform | Command | Result | Warnings | Errors |
| --- | --- | --- | --- | --- |
| Android | `dotnet run -f net11.0-android -c Debug` | succeeded | **9 × BL0016** | 0 |
| Windows | `dotnet build -f net11.0-windows10.0.19041.0 -c Debug` | succeeded | **0** | 0 |

All nine Android warnings are the same analyzer rule, raised against the four
stress components:

```
Components\Pages\ModuleLoop.razor(29,25): warning BL0016:
  JS interop call 'InvokeAsync' is not guarded with a try/catch block.
```

**They are intentional and load-bearing.** The warnings refer to the interop
calls made during *active work* — inside the loops, timers and callbacks. Those
calls must stay unguarded, because guarding them would prevent teardown from
occurring while a call is genuinely in flight, which is the entire scenario under
test. The disposal paths are guarded separately, as described in
[Disposal guarding](#disposal-guarding-and-what-tc03-actually-measures).

The Windows build reports the same components with no warnings. `BL0016` is
contributed by the Razor analyzer package that the Android head references and
the Windows head does not, so the difference is in analyzer coverage rather than
in the compiled source. The identical unguarded calls are present in both builds.

Both builds emit `NETSDK1057` (preview SDK in use), which is informational.

**Mac Catalyst project configuration.** The project declared Mac Catalyst support
with `SupportedOSPlatformVersion=15.0`, below the 17.0 minimum the .NET 11 workload
requires, so the checked-in configuration did not build for that target. The value
has been raised to 17.0. Mac Catalyst is outside the test matrix and no Windows or
Android result is affected.

## Results

Evidence logs are in [`evidence/log/`](https://github.com/surya3655/blazor-hybrid-teardown-validation/tree/main/evidence/log).

| ID | Case | Windows | Android |
| --- | --- | --- | --- |
| TC01 | Route navigation while module calls are in flight | **PASS** — 11 iterations, max disposal 565 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc01-windows.txt) | **PASS** — 10 iterations, max 367 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc01-android.txt) |
| TC02 | Replace the Blazor host with a native MAUI page | **PASS** — 20 cycles, 22/22 cleanup, max 32 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc02-windows.txt) | **PASS** — 24 cycles, 24/24 cleanup, max 37 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc02-android.txt) |
| TC03 | `IJSObjectReference` disposal after the WebView is gone | **PASS** — 15 disposals, all `destroyed first: True`, max 7 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc03-windows.txt) | **PASS** — 15 disposals, all `destroyed first: True`, max 18 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc03-android.txt) |
| TC04 | Close during the long JavaScript promise | **PASS** — 5/5 closed with the call in flight · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc04-windows.txt) | **PASS** — 5/5 in flight, teardown 759–1875 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc04-android.txt) |
| TC05 | Interop call initiated inside `DisposeAsync` | **PASS** — guarded 10 teardowns / 0 errors; unguarded control 9 / 3 errors (errors expected) · [A](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-a-windows.txt) · [B](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-b-windows.txt) | **PASS** — guarded 10 / 0 errors; unguarded 10 / 6 errors · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc05-android.txt) |
| TC06 | Background 60 s and resume during active work | **PASS** — 4 pages, app responsive after each resume · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc06-windows.txt) | **PASS** — 4 pages, app responsive after each resume · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc06-android.txt) |
| TC07 | Background, then normal close | **PASS** — 4 pages, closed from the taskbar while backgrounded · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc07-windows.txt) | **PASS** — 4 pages, close 667–1019 ms · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc07-android.txt) |
| TC08 | Rotation during active work | N/A — desktop windows do not rotate | **PASS** — see below · [A](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc08-a-android.txt) · [B](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc08-b-android.txt) |
| TC09 | Memory stability under repeated host replacement | **PASS with finding** — 36 cycles, 1/36 hosts retained, 8/36 components retained · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-windows.txt) | **PASS with finding** — 36 cycles, 0/34 hosts retained (at final checkpoint), 8/34 components retained · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-android.txt) |
| TC10 | System Back during active work | N/A — no system Back control on desktop | **PASS** — 4 busy pages plus 7 native-page returns, 0 errors · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc10-android.txt) |

No `JSDisconnectedException`, `UnhandledException`, `Error` or `Critical` record
attributable to teardown appears in any log listed above, and no stale
`DotNetObjectReference` callback (`There is no tracked object with id …`)
appears.

Each log is a single session captured for that case, verified by the count of
`Session started.` records in the file. `tc02-android.txt` previously held 27
concatenated sessions spanning two days, including error-level records from
earlier runs; it has been trimmed to the single TC02 session of 2026-09-02
10:23:47, which carries 24 host-replacement cycles, six cleanups on each of the
four stress pages and zero error-level records.

The TC05 unguarded variant is the one deliberate exception. It is a negative
control: `CallbackStressUncaught.razor` omits the `catch (JSDisconnectedException)`
that the other components carry, so the three `JSDisconnectedException` records it
produces — each traced to `CallbackStressUncaught.DisposeAsync()` in the captured
stack — are the pass condition. A session total of `Unobserved: 0` on that case
would mean the harness cannot detect the exception, which would leave every
"no exception" claim elsewhere in this report unverifiable. The Windows Part B log
was captured on the 2026-09-02 build, before the instrumentation correction
described below; the exception it records originates in `JSRuntime` and is
unaffected by that correction.

### TC08 — the two rotation configurations

| Part | `ConfigurationChanges` | Rotations | Result |
| --- | --- | --- | --- |
| A | includes `Orientation \| ScreenSize` | 36 | Activity handled the config change; the WebView was never destroyed. No duplicate work source, no error. |
| B | `Orientation`/`ScreenSize` removed | 22 | Android destroyed and rebuilt the activity on every rotation. No error, no hang, app usable throughout. |

Rotation is additional coverage, not a "must hold" condition of the issue.

### TC09 — memory stability under repeated host replacement

A single session per platform of repeated `stress page → native page → return`
cycles, rotating through all four stress pages, with a forced collection and
finalizer drain at each checkpoint. Both sessions ran on the corrected
instrumentation described under Instrumentation correction.

**Windows — 36 cycles**
([tc09-windows.txt](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-windows.txt))

| Checkpoint | Cycle | MainPage | BlazorWebView | Components | Modules | Timers | DotNetRefs | Working set |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 8 | 1/8 | 1/8 | 0/8 | 0/8 | 0/0 | 0/0 | 271 MB |
| 2 | 17 | 1/17 | 1/17 | 0/17 | 0/17 | 0/9 | 0/0 | 284 MB |
| Final | 36 | 1/36 | 1/36 | **8/36** | **8/36** | 0/9 | 0/11 | 235 MB |

Per-page disposals: ModuleLoop 8, SlowCall 8, TimerStress 9, CallbackStress 11.
Host-created, handler-disconnected and page-detached records all number 36, a 1:1:1
ratio across the run. Maximum disposal 52 ms. Working set peaked at 306 MB and
ended at 232 MB, below the mid-run reading.

**Android — 34 cycles**
([tc09-android.txt](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-android.txt))

| Checkpoint | Cycle | MainPage | BlazorWebView | Components | Modules | Timers | DotNetRefs | Working set |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 8 | 1/8 | 1/8 | 0/8 | 0/8 | 0/0 | 0/0 | 379 MB |
| 2 | 24 | 0/24 | 0/24 | **8/24** | **8/24** | 0/8 | 0/0 | 400 MB |
| Final | 34 | 0/34 | 0/34 | **8/34** | **8/34** | 0/8 | 0/10 | 405 MB |

Per-page disposals: ModuleLoop 8, SlowCall 8, TimerStress 8, CallbackStress 11.
36 hosts created against 35 disconnects and 35 page detachments — the 36th host
was live when the log was captured. Maximum disposal 177 ms. Working set rose from
379 MB to a band of 405–421 MB and was flat across the final third.

The Android final checkpoint was taken three times in succession. The first read
`MainPage=1/34`, the two that followed read `0/34`; the single instance in the
first reading was awaiting collection, not retained. Component and module counts
were identical in all three.

**Hosts are stable. Components are not.**

`MainPage` and `BlazorWebView` hold at 1 alive of 36 on Windows and 0 of 34 on
Android while the created count climbs — the shape of a stable graph, not a leak.
Timers and `DotNetObjectReference` instances release completely: 0 of 9 and 0 of 11
on Windows, 0 of 8 and 0 of 10 on Android. Both denominators are non-zero, so the
timer and callback paths were genuinely exercised.

Components and modules do not release. Both climb from 0 at the first checkpoint to
8 on each platform, and 8 is the SlowCall disposal count on both. This is the
in-flight retention finding; see Findings below.

**Coverage note.** An earlier TC09 run reported `Timers=0/0` and `DotNetRefs=0/0`,
which meant those objects were never created and the case proved nothing about the
timer and callback paths. `CallbackStress.razor` now calls `TrackComponent`,
`TrackModule` and `TrackDotNetReference`, and the runs above confirm the calls are
reached. That run is superseded, as are the earlier 64-cycle and 62-cycle runs,
which used the synthetic marker.

**Record reconciliation.** Windows records 36 cleanups but 25 carry a
`WebView destroyed first` flag; the 11 without it are `CallbackStress`, whose
dispose record does not emit that field.

### TC10 — system Back during active work

Teardown triggered by the operating system's back control rather than in-app
navigation. Android only; a single session from 09:47:44 to 09:58:19
([tc10-android.txt](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc10-android.txt)).

**Back from each busy page.** System Back was pressed with work active on all
four pages. In every case it was handled as in-app navigation — the app did not
exit — and the departed component recorded cleanup:

| Page | Cleanup on Back |
| --- | --- |
| Module loop | `[Cleanup] ModuleLoop completed after 36 calls.` |
| Slow call | `[Cleanup] SlowCall completed. Call state: in flight.` |
| Timer | `[Cleanup] TimerStress completed after 29 ticks.` |
| JS callback | `[Cleanup] CallbackStress completed after 52 callbacks.` |

The Slow call row is the notable one: Back was pressed while the 15-second
promise was still running, and cleanup completed with the call state recorded as
`in flight`.

**Back from the native page.** Seven returns, each producing the same ordered
sequence — WebView destroyed, component cleaned up, fresh host created:

```
2026-09-04T09:50:41.2995155+05:30  [WebView] BlazorWebView handler disconnected. WebView destroyed.
2026-09-04T09:50:41.3720833+05:30  [Cleanup] ModuleLoop completed after 40 calls.
2026-09-04T09:50:42.4750767+05:30  [WebView] BlazorWebView host created. Cycle 5.
2026-09-04T09:50:46.1835385+05:30  [Module] ModuleLoop acquired JS module reference.
```

The trailing `[Module] … acquired` record on each cycle is the host-health check:
a rebuilt host that failed to load would not acquire a module. All seven did,
with counter values of 43, 50, 47, 40, 52, 41 and 33 calls.

Across the whole run: 11 cleanup records, 7 host rebuilds, **0** `[Error]`
records and **0** stale-callback errors.

## Video evidence

Screen recordings for the cases whose result cannot be read from a log alone.
Recordings are in
[`evidence/video/`](https://github.com/surya3655/blazor-hybrid-teardown-validation/tree/main/evidence/video).

| Case | Platform | Shows | Recording |
| --- | --- | --- | --- |
| TC02 | Windows, Android | The native page replaces the host, and a working Blazor host is created on every return | [tc02.zip](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/video/tc02.zip) |
| TC04 | Windows, Android | The app closes promptly while the 15-second call is still in flight | [tc04.zip](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/video/tc04.zip) |
| TC06 | Android | The app resumes responsive after a 60-second background period | [tc06-android.zip](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/video/tc06-android.zip) |
| TC08-A | Android | Rotation with the activity preserved: the counter continues and no duplicate work source appears | [tc08-A-android.zip](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/video/tc08-A-android.zip) |
| TC08-B | Android | Rotation with the activity recreated: the host is rebuilt and usable after every rotation | [tc08-b-android.zip](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/video/tc08-b-android.zip) |

TC01, TC03, TC05 and TC07 are covered by their logs: disposal ordering,
durations and error counts are recorded there.

## Instrumentation correction — the WebView destruction marker

Two defects made every earlier `WebView destroyed first` reading unsound. Both
have been corrected and the affected cases re-run.

**The synthetic call.** `NativePageNavigator.ShowNativePage()` called
`TeardownDiagnostics.MarkWebViewDestroyed()` directly, immediately after queuing
the page swap. That marker recorded an intention rather than an observed event.
The call has been removed; the navigator now records only the navigation itself.

**The wrong observed object.** `MainPage.OnHandlerChanged` marked the WebView
destroyed when **`MainPage.Handler`** went null. That is the page's handler, not
the `BlazorWebView`'s — a proxy that fires earlier and for a different reason.
`MainPage` now subscribes to `BlazorHost.HandlerChanged` and marks create/destroy
from the `BlazorWebView`'s own handler transition. The page-level event is still
recorded, under its own `[Page] MainPage handler attached/detached` records, so it
cannot be mistaken for WebView evidence.

The two events are now visibly distinct in every cycle:

```
20:07:20.207  [NativeNavigation] Replacing window content with native page
20:07:20.224  [Page] MainPage handler detached
20:07:20.232  [WebView] BlazorWebView handler disconnected. WebView destroyed.
20:07:20.236  [Dispose] ModuleLoop entered ... WebView destroyed first: True
```

| Signal | Before | After |
| --- | --- | --- |
| Marker source | `MainPage.Handler`, plus a direct call from the navigator | `BlazorWebView.Handler` only |
| Page and WebView events | one record, indistinguishable | separate records, 8 ms apart on Windows, 6–32 ms on Android |
| Hosts created : handlers disconnected | 2 : 1 | 16 : 15 on both platforms in TC03; 36 : 36 on Windows in TC09 |

The synthetic call fired on the calling thread, outside the queued swap callback,
so it always preceded the navigation record by less than a millisecond and
double-counted because the real handler transition still occurred. Neither
signature appears in the corrected runs.

**What was re-measured.** TC03 and TC09 were re-run in full on both platforms
against the corrected build. TC03 recorded 15 disposals per platform, every one
reading `WebView destroyed first: True`, with the WebView disconnect arriving
between the page detach and the disposal in all 30 cases. All earlier
`destroyed first` readings — including the 64-cycle and 62-cycle TC09 runs — were
produced by the defective instrumentation and are superseded rather than cited.

The remaining case logs (TC01, TC02, TC04 through TC08, TC10) also predate the
correction. Their results do not turn on the ordering flag, but any
`WebView destroyed first` value in them carries the same defect and is not relied
on in this report.

**One hypothesis closed.** The corrected runs show the `BlazorWebView` handler
disconnecting cleanly on every host replacement — 15 of 15 per platform in TC03,
36 of 36 on Windows in TC09. An undisconnected handler is therefore not the cause
of Finding 1.

## Disposal guarding and what TC03 actually measures

`ModuleLoop.razor`, `SlowCall.razor` and `TimerStress.razor` **do** catch
`JSDisconnectedException` around `_module.DisposeAsync()`. `README.md` previously
stated that components intentionally do not catch it; **that statement is wrong
and must be corrected in the README.**

The catch does not weaken TC03, because the pass criterion is not "no exception
was caught" — it is the value of the `Module disposed cleanly` flag, which the
component sets only after `DisposeAsync()` returns normally:

```csharp
try
{
    await _module.DisposeAsync().AsTask().WaitAsync(TeardownDiagnostics.DisposeTimeout);
    moduleDisposed = true;          // set ONLY on a normal return
}
catch (JSDisconnectedException)
{
    // would leave moduleDisposed false
}
```

So `Module disposed cleanly: True` is positive evidence that disposal completed
without throwing. Had the fix not been present, that flag would read `False` and
the catch block would have been entered. Across TC03 the flag reads `True` on all
30 post-destruction disposals (15 Windows + 15 Android).

TC05 supplies the complementary control: an identical component with the catch
removed does throw, proving the flag distinguishes the two states rather than
masking them.

## Shutdown timing: which number means what

Three distinct quantities appear around app close. They are not
interchangeable, and only two of them are used in the verdict.

| Quantity | Windows | Android | Definition | Used in verdict |
| --- | --- | --- | --- | --- |
| `Shutdown took N ms` | 1–3 ms | 0–11 ms | The interval between the `BeginShutdown()` and `EndShutdown()` calls. In the current wiring these are adjacent statements in the same hook, so the value spans two lines of code and **no teardown work at all**. | **No — excluded** |
| Deactivated → destroyed | not derivable | **759–1875 ms** | Elapsed time from `[Lifecycle] Window deactivated` to `[Lifecycle] Window destroyed`, read from log timestamps. This is the managed teardown window — the interval in which an abandoned promise would have blocked. | **Yes** |
| Teardown → next session start | ≤ 8.6 s | ≤ 3.0 s | Time from the destruction record to the following `Session started`. A relaunch requires the previous process to have exited, so this bounds full process exit from above. | **Yes** |

**The reported Android figure of 759–1875 ms is the deactivated → destroyed
interval**, not the `Shutdown took` value. All five iterations fall under the
2000 ms threshold.

**The Windows `Shutdown took` values of 1–3 ms are an instrumentation artefact
and are not quoted as shutdown times anywhere in this report.** Windows closes
were initiated from the title bar or taskbar, which emit no record at click time,
so no deactivated → destroyed interval is derivable there. The Windows conclusion
rests on the third row: with 12–14 seconds still to run on each promise,
processes exited and relaunched within 8.6 seconds, which rules out any wait on
the 15-second promise.

A future harness revision could bracket `base.OnDestroying()` with the stopwatch
and report a single self-measured figure. That would improve the instrumentation
rather than the result: the derived interval and the relaunch bound already
establish it.

## Observations

Neither item below is a defect. Each is recorded because it bears on how the
results should be read. The two unresolved retention results are reported
separately under Findings.

### Component cleanup does not run on normal app close

Observed in TC04 and TC07 on both platforms. A component holding a live JS module
produced no cleanup record when the app was closed normally, although cleanup runs
correctly on route navigation and on host replacement.

This is consistent with .NET shutdown semantics — neither `Dispose` nor finalizers
are guaranteed to run at process exit — and nothing throws or hangs. It is recorded
for one reason: **the app-close path never exercises the disposal guarantee**, so
TC04 establishes that close is prompt, not that post-disconnect disposal is safe.
TC03 is what establishes the latter.

### JavaScript intervals are throttled while backgrounded, and the two platforms throttle at different rates

In TC06 on both platforms, a periodic .NET timer firing every 250 ms continued at
its full rate through a 60-second background period — 283 and 376 ticks on Windows,
365 on Android, each matching elapsed time exactly. The .NET timer runs on the
thread pool, which the embedded browser does not govern.

A JavaScript interval at 200 ms — five callbacks per second when unthrottled — did
not. Both platforms throttled it to roughly 1 Hz, which is Chromium's deliberate
background-timer policy, but not to the same rate:

| | Background window | Callbacks in the window | Approximate rate |
| --- | ---: | ---: | ---: |
| Windows, Chromium 152 | 70.0 s | about 67 | about 0.96 per second |
| Android, Chromium 109 | 63.9 s | about 73 | about 1.14 per second |

Android sustained the interval roughly 20 per cent faster than Windows over a
shorter window. Both sit at the same order — the ~1 Hz clamp — so the difference is
one of degree within expected behaviour, not a behavioural divergence of the kind
the issue asks to be reported.

**These two rows are approximate and should not be quoted as measurements.** The
callback component in use did not emit a module-acquisition record, so the moment
each page began is inferred from the preceding cleanup record rather than measured.
The totals — 142 callbacks on Windows and 153 on Android — are exact; the split
between foreground and background portions is not. Confirming the rate difference
would need a run with the component instrumented to log its own start.

## Findings

Two results are reported as findings rather than observations: each is a
reproducible retention that no benign mechanism accounts for, and neither cause
was identified. Both sit outside what the fix under test governs, and neither
produced an exception, a hang or a failed host rebuild.

### Finding 1 — component retention after teardown with a call in flight

Every component disposed while a JavaScript call was in flight was retained after
a forced collection and finalizer drain, together with its JS module:

| Platform | SlowCall disposals | Components retained | Modules retained |
| --- | ---: | ---: | ---: |
| Windows | 8 | 8 of 36 | 8 of 36 |
| Android | 8 | 8 of 34 | 8 of 34 |

**The attribution is unambiguous.** The four stress pages produced different
disposal counts in each run, and the retained count matches SlowCall alone:

| Platform | ModuleLoop | SlowCall | TimerStress | CallbackStress | Retained |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows | 8 | **8** | 9 | 11 | **8** |
| Android | 8 | **8** | 8 | 11 | **8** |

`CallbackStress` disposed 11 times on both platforms and retained nothing, which
excludes it. All 16 SlowCall disposals recorded `Call state: in flight` and
`Was in flight at disposal: True`.

**The abandoned calls never resolve.** `SlowCall.razor` attaches a continuation
that records any fault through `TeardownDiagnostics.RecordObservedSlowCallFault`.
Across 16 abandoned promises on the two platforms, that record appears **zero
times**. The calls do not complete, do not cancel and do not fault; they remain
pending indefinitely, and the awaiting state machine holds the component. This
also explains why the session totals read `unobserved: 0` — a task that never
faults cannot produce an unobserved exception.

**Not a settling artefact.** On Android the retention was already at its final
value of 8 at the second checkpoint (cycle 24) and unchanged ten cycles later. The
final checkpoint was taken three times in succession, several seconds apart, and
read 8 every time while `MainPage` fell from 1 to 0 between the first and second —
so collection was still active in that window and did not touch the components.

**Not an artefact of the harness.** `TeardownDiagnostics` tracks through
`WeakReference` only; the fault continuation captures no `this`; and the in-flight
probe is a single static field, cleared in `DisposeAsync`, which cannot account for
8 instances. The three other stress pages, tracked by the same mechanism in the
same sessions, report zero retained.

**Not an undisconnected WebView.** The corrected instrumentation shows the
`BlazorWebView` handler disconnecting on every host replacement — 36 of 36 on
Windows — so a surviving handler is not what holds the component.

This reproduces in the **shipping configuration on both platforms**, with no
forced activity recreation and no modified activity attribute, and it reproduced
again on the corrected build after the marker defects were fixed. The retaining
reference was not traced; doing so would require a heap dump, which was not taken.

### Finding 2 — host retention under Android activity recreation

In the TC08 Part B configuration, where the activity is destroyed and rebuilt,
22 rotations produced seven module acquisitions and one cleanup. The page
handler is never transitioned to null on activity recreation, so no managed
teardown runs. After a forced collection and finalizer drain, retained objects
stood at 22 of 23 `MainPage` instances, 22 of 23 `BlazorWebView` instances, 16
of 16 components, 16 of 16 JS modules and 6 of 6 timers, with the working set
rising from 342 MB to 409 MB across the run.

`MainPage` is registered with `AddTransient`. Each instance is therefore a
fresh object that the container does not hold, and container lifetime does not
account for the retention. No cause was identified. Tracing the retaining
reference would require a heap dump, which was not taken.

This sits outside the scope of the fix under test, since disposal never runs on
the recreation path — the fix governs what happens when disposal runs with
calls in flight, not whether disposal is invoked at all. One bound applies to
how far the observation reaches: the configuration is not the shipping one.
Recreation was forced by removing `ConfigChanges.Orientation` and `ScreenSize`
from the activity attribute for this test. With the attribute as shipped, TC08
Part A showed 36 rotations with the WebView never destroyed, no duplicate work
source and no error.

## Platform comparison

The issue asks whether teardown that is clean on one platform is clean on the
other, and treats a divergence as a finding. **There is one divergence, and it is
recorded as Finding 2.**

For the teardown behaviour the fix governs, the two platforms agree. Every case
produced the same verdict on both: ten passes on Windows (TC08 and TC10 not
applicable) and ten on Android. No exception, hang or stale-callback record
appeared on one platform and not the other, despite the two embedded browsers
being roughly three years apart in version.

The divergence is in retention, not in teardown. Under forced activity
recreation Android retains 22 of 23 hosts along with every component, module and
timer; the Windows lifecycle has no equivalent path and shows nothing comparable.
Finding 1, by contrast, is **not** a divergence — component retention after
in-flight teardown reproduces on both platforms at the same rate.

The measurable differences are in rate, not behaviour:

- Disposal is two to three times slower on Android — a maximum of 18 ms against
  7 ms on Windows in TC03, and 177 ms against 52 ms in the longer TC09 run — but
  every value is far inside the two-second bound.
- Working set runs higher on Android, settling at 405–421 MB against 232–306 MB on
  Windows in TC09, and reaches a stable band on both.
- Background JavaScript throttling is marginally less aggressive on Android, about
  1.14 against about 0.96 callbacks per second. Both are the same ~1 Hz clamp; see
  the observation above, including why those two figures are approximate.

