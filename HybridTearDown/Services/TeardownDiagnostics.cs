using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace HybridTearDown.Services;

/// <summary>
/// Central instrumentation for the #68813 teardown validation harness.
///
/// Provides the evidence records the test plan requires but the original harness
/// did not emit: dispose ordering, WebView destruction, shutdown duration,
/// memory sampling, retained-object counts, and a finalizer drain that makes
/// unobserved task exceptions actually observable.
///
/// Wire the sink once at startup:
///     TeardownDiagnostics.Sink = EvidenceLog.Write;
///     TeardownDiagnostics.Install();
/// </summary>
public static class TeardownDiagnostics
{
    /// <summary>Upper bound on any single await inside a DisposeAsync.</summary>
    public static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Set this to the existing evidence-log writer at startup.</summary>
    public static Action<string>? Sink;

    private static readonly List<WeakReference> TrackedPages = new();
    private static readonly List<WeakReference> TrackedWebViews = new();
    private static readonly List<WeakReference> TrackedComponents = new();
    private static readonly List<WeakReference> TrackedModules = new();
    private static readonly List<WeakReference> TrackedTimers = new();
    private static readonly List<WeakReference> TrackedDotNetReferences = new();
    private static readonly object Gate = new();

    private static Func<bool>? _inFlightProbe;
    private static Timer? _memoryTimer;
    private static Stopwatch? _shutdownStopwatch;
    private static int _shutdownBegun;
    private static int _shutdownEnded;
    private static int _rotationCount;

    private static int _unhandledCount;
    private static int _unobservedCount;
    private static int _observedSlowCallFaults;
    private static int _hostCycle;

    /// <summary>True once the BlazorWebView has been destroyed. TC03 ordering proof.</summary>
    public static bool WebViewDestroyed { get; private set; }

    public static int HostCycle => _hostCycle;

    public static int UnhandledCount => _unhandledCount;

    public static int UnobservedCount => _unobservedCount;

    // ---------------------------------------------------------------- startup

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Interlocked.Increment(ref _unhandledCount);
            Write($"[Error] UnhandledException. Terminating: {e.IsTerminating}. {Describe(e.ExceptionObject as Exception)}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Interlocked.Increment(ref _unobservedCount);
            Write($"[Error] UnobservedTaskException. {Describe(e.Exception)}");
            e.SetObserved();
        };

        Write($"[Session] Diagnostics installed. Harness: v2. DisposeTimeout: {DisposeTimeout.TotalSeconds}s.");
    }

    /// <summary>Starts periodic memory sampling. Call after the first window opens.</summary>
    public static void StartMemorySampling(TimeSpan interval)
    {
        _memoryTimer?.Dispose();
        _memoryTimer = new Timer(_ => SampleMemory("periodic"), null, TimeSpan.Zero, interval);
    }

    public static void StopMemorySampling()
    {
        _memoryTimer?.Dispose();
        _memoryTimer = null;
    }

    // ------------------------------------------------------- teardown markers

    /// <summary>Call immediately AFTER the BlazorWebView has actually been removed.</summary>
    public static void MarkWebViewDestroyed()
    {
        WebViewDestroyed = true;
        Write("[WebView] BlazorWebView handler disconnected. WebView destroyed.");
    }

    /// <summary>
    /// Call when a fresh BlazorWebView host has been created.
    /// Idempotent: MAUI raises HandlerChanged more than once for a single host,
    /// so only a genuine destroyed-to-live transition advances the cycle counter.
    /// </summary>
    public static void MarkWebViewCreated()
    {
        if (_hostCycle > 0 && !WebViewDestroyed)
        {
            // Already live. A repeat HandlerChanged for the same host, not a new cycle.
            return;
        }

        WebViewDestroyed = false;
        var cycle = Interlocked.Increment(ref _hostCycle);
        Write($"[WebView] BlazorWebView host created. Cycle {cycle}.");
    }

    public static void MarkNativeNavigation(string destination)
    {
        Write($"[NativeNavigation] Replacing window content with {destination}. Cycle {_hostCycle}.");
    }

    /// <summary>
    /// TC12. Records a device orientation change so the log can prove a rotation
    /// happened; nothing else in the harness observes orientation.
    ///
    /// <paramref name="activityRecreated"/> distinguishes the two TC12 parts:
    /// false when the activity handled the config change itself (Part A), true
    /// when Android destroyed and rebuilt it (Part B). In Part B a
    /// [WebView] handler disconnected record must follow this one.
    /// </summary>
    public static void MarkOrientationChanged(string orientation, bool activityRecreated)
    {
        var rotation = Interlocked.Increment(ref _rotationCount);

        Write($"[Rotation] Orientation changed to {orientation}. " +
              $"Rotation {rotation} this session. Activity recreated: {activityRecreated}. " +
              $"Cycle {_hostCycle}.");
    }

    public static int RotationCount => _rotationCount;

    /// <summary>
    /// Call when window destruction begins.
    /// Idempotent: several hooks can fire on one close (the close button, the
    /// Window.Destroying event, and MainActivity.OnDestroy on Android). Only the
    /// FIRST call starts the clock, so the measurement spans the whole shutdown
    /// rather than the gap between two duplicate hooks.
    /// </summary>
    public static void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownBegun, 1) == 1)
        {
            return;
        }

        _shutdownStopwatch = Stopwatch.StartNew();

        var inFlight = false;
        try
        {
            inFlight = _inFlightProbe?.Invoke() ?? false;
        }
        catch
        {
            // A probe failure must never affect shutdown.
        }

        Write($"[Lifecycle] Window destroying. Slow call in flight at shutdown: {inFlight}.");
    }

    /// <summary>
    /// Call when window destruction has finished. Provides the TC04 measurement.
    /// Idempotent for the same reason as BeginShutdown: the LAST hook to run is
    /// the one that matters, so only the first EndShutdown is recorded and later
    /// duplicates are ignored.
    /// </summary>
    public static void EndShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownEnded, 1) == 1)
        {
            return;
        }

        var elapsed = _shutdownStopwatch?.ElapsedMilliseconds ?? -1;
        _shutdownStopwatch?.Stop();

        Write($"[Lifecycle] Window destroyed. Shutdown took {elapsed} ms. " +
              $"Unhandled: {_unhandledCount}. Unobserved: {_unobservedCount}.");
    }

    // ------------------------------------------------------------- slow call

    public static void RegisterInFlightProbe(Func<bool> probe) => _inFlightProbe = probe;

    public static void ClearInFlightProbe() => _inFlightProbe = null;

    public static void RecordObservedSlowCallFault(Exception? exception)
    {
        var count = Interlocked.Increment(ref _observedSlowCallFaults);
        Write($"[SlowCall] Abandoned promise faulted and was observed. Total observed: {count}. " +
              $"Type: {exception?.GetBaseException().GetType().Name ?? "unknown"}.");
    }

    // -------------------------------------------------------------- tracking

    public static void TrackPage(object page) => Track(TrackedPages, page);

    public static void TrackWebView(object webView) => Track(TrackedWebViews, webView);

    public static void TrackComponent(object component) => Track(TrackedComponents, component);

    public static void TrackModule(object module) => Track(TrackedModules, module);

    public static void TrackTimer(object timer) => Track(TrackedTimers, timer);

    public static void TrackDotNetReference(object reference) => Track(TrackedDotNetReferences, reference);

    private static void Track(List<WeakReference> bucket, object instance)
    {
        lock (Gate)
        {
            bucket.Add(new WeakReference(instance));
        }
    }

    // ---------------------------------------------------------------- memory

    public static void SampleMemory(string reason)
    {
        var managed = GC.GetTotalMemory(false) / 1024 / 1024;
        var workingSet = Environment.WorkingSet / 1024 / 1024;

        Write($"[Memory] Sample ({reason}). Cycle {_hostCycle}. " +
              $"GC heap: {managed} MB. Working set: {workingSet} MB.");
    }

    /// <summary>
    /// Forces a collection, drains finalizers, then reports what is still alive.
    /// Run this at the end of any case that checks for unobserved task exceptions,
    /// and at the end of TC13.
    /// </summary>
    public static void DrainAndReport(string reason)
    {
        var unobservedBefore = _unobservedCount;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // A second pass: the first drain can itself surface unobserved exceptions.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        var managed = GC.GetTotalMemory(true) / 1024 / 1024;
        var workingSet = Environment.WorkingSet / 1024 / 1024;

        var report = new StringBuilder();
        report.Append($"[Memory] Retained after collection ({reason}). Cycle {_hostCycle}. ");
        report.Append($"MainPage={AliveCount(TrackedPages)}/{TotalCount(TrackedPages)} ");
        report.Append($"BlazorWebView={AliveCount(TrackedWebViews)}/{TotalCount(TrackedWebViews)} ");
        report.Append($"Components={AliveCount(TrackedComponents)}/{TotalCount(TrackedComponents)} ");
        report.Append($"Modules={AliveCount(TrackedModules)}/{TotalCount(TrackedModules)} ");
        report.Append($"Timers={AliveCount(TrackedTimers)}/{TotalCount(TrackedTimers)} ");
        report.Append($"DotNetRefs={AliveCount(TrackedDotNetReferences)}/{TotalCount(TrackedDotNetReferences)}. ");
        report.Append($"GC heap: {managed} MB. Working set: {workingSet} MB.");

        Write(report.ToString());

        var surfaced = _unobservedCount - unobservedBefore;
        Write($"[Drain] Finalizer drain complete ({reason}). " +
              $"Unobserved exceptions surfaced by this drain: {surfaced}. " +
              $"Session totals — unhandled: {_unhandledCount}, unobserved: {_unobservedCount}.");
    }

    private static int AliveCount(List<WeakReference> bucket)
    {
        lock (Gate)
        {
            return bucket.Count(w => w.IsAlive);
        }
    }

    private static int TotalCount(List<WeakReference> bucket)
    {
        lock (Gate)
        {
            return bucket.Count;
        }
    }

    // ----------------------------------------------------------------- output

    private static string Describe(Exception? exception)
    {
        if (exception is null)
        {
            return "No exception object.";
        }

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null && depth < 5)
        {
            builder.Append(depth == 0 ? "Type: " : " ---> Inner: ");
            builder.Append(current.GetType().FullName);
            builder.Append(". Message: ");
            builder.Append(current.Message);
            current = current.InnerException;
            depth++;
        }

        builder.Append(" Stack: ");
        builder.Append(exception.StackTrace);

        return builder.ToString();
    }
    

    private static void Write(string message)
    {
        try
        {
            Sink?.Invoke(message);
        }
        catch
        {
            // Never let instrumentation break the run.
        }

        Debug.WriteLine(message);
    }
}
