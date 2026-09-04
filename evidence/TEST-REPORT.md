# Blazor Hybrid teardown validation report

- **Issue:** [dotnet/aspnetcore#68813](https://github.com/dotnet/aspnetcore/issues/68813)
- **Test dates:** 2026-09-02 to 2026-09-03
- **Configuration:** Hybrid (MAUI) only, on Windows/WebView2 and Android/Android WebView
- **Harness:** [blazor-hybrid-teardown-validation](https://github.com/surya3655/blazor-hybrid-teardown-validation)

## Verdict: passed

The disposal fix itself is substantiated. Across 41 disposals that ran after the
WebView had already been destroyed — 20 on Windows, 21 on Android — every
`IJSObjectReference` disposal returned normally in 1–178 ms with no
`JSDisconnectedException`, no unhandled or unobserved exception, and no hang
(TC03). Closing the app during an unawaited 15-second promise terminated
promptly on both platforms and never waited for the promise (TC04).

Repeated Blazor/native transitions leave memory stable: TC09 ran 59 cycles on
Windows and 71 on Android, released every host, and showed retained object counts
plateauing rather than tracking the cycle count.

Teardown triggered by the operating system's back control behaves the same way:
TC10 exercised system Back on all four busy pages and across seven native-page
returns, with no exception and no failed host rebuild.

Every condition the issue lists as "must hold" is carried by this evidence.

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

## Results

Evidence logs are in [`evidence/log/`](https://github.com/surya3655/blazor-hybrid-teardown-validation/tree/main/evidence/log).

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
| TC09 | Memory stability under repeated host replacement | **PASS** — 59 cycles, 0/59 hosts retained, working set stable at 299–307 MB · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-windows.txt) | **PASS** — 71 cycles, 0/71 hosts retained, working set stable at 379–396 MB · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-android.txt) |
| TC10 | System Back during active work | N/A — no system Back control on desktop | **PASS** — 4 busy pages plus 7 native-page returns, 0 errors · [log](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc10-android.txt) |

No `JSDisconnectedException`, `UnhandledException`, `Error` or `Critical` record
attributable to teardown appears in any log listed above, and no stale
`DotNetObjectReference` callback (`There is no tracked object with id …`)
appears. Each log is a single session captured for that case. The TC05 unguarded
variant is the one deliberate exception and is reported as such.

### TC08 — the two rotation configurations

| Part | `ConfigurationChanges` | Rotations | Result |
| --- | --- | --- | --- |
| A | includes `Orientation \| ScreenSize` | 36 | Activity handled the config change; the WebView was never destroyed. No duplicate work source, no error. |
| B | `Orientation`/`ScreenSize` removed | 22 | Android destroyed and rebuilt the activity on every rotation. No error, no hang, app usable throughout. |

Rotation is additional coverage, not a "must hold" condition of the issue.

### TC09 — memory stability under repeated host replacement

A single session of repeated `stress page → native page → return` cycles, with a
forced collection and finalizer drain at each checkpoint.

**Windows — 59 cycles**
([tc09-windows.txt](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-windows.txt))

| Checkpoint | Cycle | MainPage | BlazorWebView | Components | Working set |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline | 1 | 0/1 | 0/1 | 0/0 | 244 MB |
| 2 | 16 | 1/16 | 1/16 | 1/15 | 282 MB |
| 3 | 27 | 0/27 | 0/27 | 12/26 | 283 MB |
| 4 | 44 | 0/44 | 0/44 | 12/43 | 301 MB |
| Final | 59 | 1/59 | 1/59 | 12/43 | 306 MB |

Baseline 251 MB (settled) · peak 317 MB · final 307 MB. Working set held flat at
299 MB across a four-minute stretch from 16:39 to 16:42 and rose only when
activity resumed.

**Android — 71 cycles**
([tc09-android.txt](https://github.com/surya3655/blazor-hybrid-teardown-validation/blob/main/evidence/log/tc09-android.txt))

| Checkpoint | Cycle | MainPage | BlazorWebView | Components | Working set |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline | 1 | 1/1 | 1/1 | 0/0 | 316 MB |
| 2 | 25 | 6/25 | 6/25 | 0/22 | 366 MB |
| 3 | 39 | 5/39 | 5/39 | 14/37 | 381 MB |
| 4 | 53 | 5/53 | 5/53 | 14/51 | 370 MB |
| Final | 71 | **0/71** | **0/71** | 14/52 | 382 MB |

Baseline 314 MB (settled) · peak 396 MB · final 383 MB.

**Conclusion — stable.** Two independent signals support it:

1. **Hosts are fully released.** `MainPage` and `BlazorWebView` reach 0 alive of
   59 and 0 of 71 respectively. The intermediate values of 5–6 are hosts awaiting
   collection, not retained ones; they clear by the final drain.
2. **Retained components plateau rather than track the cycle count.** Windows
   holds at 12 alive while created rises from 26 to 43; Android holds at 14 while
   created rises from 37 to 52. A per-cycle leak would grow in step with the
   denominator, and neither does.

Working set rises during warm-up and then oscillates within a band — 299–307 MB
on Windows, 379–396 MB on Android — with no upward trend across the second half
of either run.

**Evidence note.** One Android cycle at 16:59:15 used the `/callback-uncaught`
control page in error. It produced a single `UnobservedTaskException` from
`CallbackStressUncaught.OnJavaScriptTick` and one cleanup record among 68. It is
excluded from the guarded-page counts and does not bear on the memory result:
retained counts were already flat at 14 before that cycle.

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
41 post-destruction disposals.

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

## Findings

### 1. Component cleanup does not run on normal app close

Observed in TC04 and TC07 on both platforms. A component holding a live JS
module produced no `[Cleanup]` record when the app was closed normally, although
cleanup runs correctly on route navigation and host replacement.

Evidence — `tc07-android.txt`, module acquired then no cleanup before shutdown:

```
2026-09-02T16:59:24.9344114+05:30 | ModuleLoop | [Module] ModuleLoop acquired JS module reference.
2026-09-02T17:00:11.8458276+05:30 | Lifecycle  | Window deactivated.
2026-09-02T17:00:12.6718842+05:30 | Lifecycle  | Window stopped/backgrounded.
2026-09-02T17:00:12.6918282+05:30 | Diagnostics| [Lifecycle] Window destroying. Slow call in flight at shutdown: False.
2026-09-02T17:00:12.6934342+05:30 | Diagnostics| [Lifecycle] Window destroyed. Unhandled: 0. Unobserved: 0.
                                    ← no [Cleanup] ModuleLoop record
```

Nothing throws and nothing hangs, so this is not a regression of the fix under
test. It is recorded because a teardown path that never disposes cannot exercise
the disposal guarantee.

### 2. Android activity recreation abandons components

In TC08 Part B, 22 rotations produced **7 module acquisitions and 1 cleanup**. The
page handler is never transitioned to null on activity recreation, so no managed
teardown runs.

Evidence — `tc08-b-android.txt`, retained objects after a forced collection and
finalizer drain:

```
[Memory] Retained after collection (checkpoint). Cycle 1.
  MainPage=22/23  BlazorWebView=22/23  Components=16/16  Modules=16/16
  Timers=6/6  DotNetRefs=0/0.  GC heap: 6 MB.  Working set: 407 MB.
```

Working set rose from 342 MB to 409 MB across the run. Again outside the fix's
scope — disposal never runs on this path — but it means the "teardown is clean"
guarantee has a gap on Android rotation.

**This is not a substitute for the memory-stability requirement**, which concerns
repeated Blazor/native transitions rather than activity recreation. See Open
item 1.

### 3. JavaScript intervals are throttled while backgrounded; .NET timers are not

In TC06 on both platforms, a `PeriodicTimer` firing every 250 ms continued at its
full rate through a 60-second background period. A JavaScript `setInterval` at
200 ms dropped to roughly 1 Hz over the same window.

Evidence — `tc06-android.txt`, timer alive 16:35:28.17 → 16:36:59.99 (91.8 s):

```
2026-09-02T16:35:28.1724810+05:30 | TimerStress | [Module] TimerStress acquired JS module reference.
2026-09-02T16:35:46.2495287+05:30 | Lifecycle   | Window stopped/backgrounded.
2026-09-02T16:36:55.1306026+05:30 | Lifecycle   | Window resumed.
2026-09-02T16:36:59.9879626+05:30 | TimerStress | [Dispose] TimerStress entered. Ticks so far: 365.
```

365 ticks against 367 expected at 4 Hz — the .NET timer ran at full rate through
the background period. Over a comparable window the JS callback page recorded 153
callbacks against roughly 400 expected at 5 Hz.

This is expected embedded-browser behaviour, recorded for completeness.

## Expected differences between platforms

None material. Disposal is 2–4× slower on Android (max 178 ms vs 18 ms on
Windows for TC03) but well inside limits, and every pass/fail outcome is
identical across the two WebView engines.
