const callbackTimers = new Map();
let nextCallbackHandle = 1;

// Called by CallbackStress.razor. Returns a handle used later by stopCallbacks.
export function startCallbacks(dotNetRef) {
    const handle = nextCallbackHandle++;
    let sequence = 0;

    const timerId = setInterval(async () => {
        sequence++;
        try {
            await dotNetRef.invokeMethodAsync('OnJavaScriptTick', sequence);
        } catch (error) {
            // The .NET target is gone (component disposed, WebView torn down,
            // or the page navigated away). Stop immediately instead of dialling
            // a dead reference forever. This is the actual fix.
            stopCallbacks(handle);
        }
    }, 200);

    callbackTimers.set(handle, timerId);
    return handle;
}

// Safe to call more than once, and safe to call after the interval already
// self-cleared. Never throws.
export function stopCallbacks(handle) {
    const timerId = callbackTimers.get(handle);
    if (timerId !== undefined) {
        clearInterval(timerId);
        callbackTimers.delete(handle);
    }
}

// Called by ModuleLoop.razor on every loop iteration.
export function recordModuleCall(count) {
    return count;
}

// Called by TimerStress.razor on every timer tick.
export function timerTick(count) {
    return count;
}

// Called by SlowCall.razor. Resolves after the given delay.
export function slowCall(delayMilliseconds) {
    return new Promise(resolve => {
        setTimeout(() => resolve('completed'), delayMilliseconds);
    });
}

// Belt and braces: if the page itself is torn down, drop every interval.
window.addEventListener('pagehide', () => {
    for (const timerId of callbackTimers.values()) {
        clearInterval(timerId);
    }
    callbackTimers.clear();
});
