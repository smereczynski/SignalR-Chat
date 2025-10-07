## 1. Concepts (they get mixed up a lot)
- Reconnect delay (backoff): How long you wait before the next attempt.
- Attempt (handshake) timeout: How long an individual connect attempt is allowed to run before it fails (handled internally by SignalR / fetch / WebSocket stack).
- Overall reconnect ceiling: A point after which you stop trying and declare the connection dead.

## 2. What your current code does
In chat.js you’re doing two different things:

1. Initial connection retries (before ever being “connected”):
   - Custom logic: `scheduleReconnect()` uses  
     `delay = Math.min(1000 * 2^attempt, 30000)`  
     (so: 1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000, …).
   - There is NO cap on number of attempts. It will retry forever with a max 30s delay.

2. After the hub was once connected:
   - You rely on `withAutomaticReconnect()` (no arguments), which uses the built‑in default retry delays: `[0, 2000, 10000, 30000]`.
   - After those 4 attempts fail, SignalR stops and fires `onclose` (your handler marks it disconnected and clears users). At that point your manual loop does NOT re-trigger (because that scheduleReconnect path is only used during initial connect). So once a post-connect reconnection cycle exhausts, it stays disconnected until the page is refreshed (unless you add logic to restart).

So: there is no configured “reconnection timeout” variable; rather:
- Initial phase: infinite retries, exponential capped at 30s.
- Post-connect phase: at most 4 retry attempts (≈ up to ~42s total wall clock plus handshake times) then it gives up.

## 3. What about the duration of each attempt?
Each attempt itself can fail fast (e.g., network error) or hang until:
- HTTP negotiate / WebSocket upgrade times out (browser/network layer).
- Server handshake timeout (SignalR server default is usually 15s / transport negotiation window).
You haven’t overridden those in the client.

## 4. If you want a true “reconnection timeout” (overall cap)
Decide a max wall-clock (e.g., 2 minutes) or max attempts, then:
- Track when `onreconnecting` first fires.
- If still not `onreconnected` after threshold, call `hub.stop()` and optionally start a slower “background retry” or show a “Click to retry” button.

Example pattern:

```javascript
let reconnectWindowStart = null;
hub.onreconnecting(() => {
  if (!reconnectWindowStart) reconnectWindowStart = Date.now();
  const elapsed = Date.now() - reconnectWindowStart;
  if (elapsed > 120000) { // 2 minutes
    try { hub.stop(); } catch {}
    showError('Connection lost. Manual retry required.');
    // Offer a button: startHub();
  }
});
hub.onreconnected(() => {
  reconnectWindowStart = null;
});
```

## 5. If you want infinite post-connect retries
Provide a custom retry array or a function:

```javascript
hub = new signalR.HubConnectionBuilder()
  .withUrl('/chatHub')
  .withAutomaticReconnect({
    nextRetryDelayInMilliseconds: ctx => {
      if (!ctx.elapsedMilliseconds) return 0;
      // Exponential with cap
      const attempt = ctx.previousRetryCount + 1;
      const delay = Math.min(1000 * Math.pow(2, attempt), 30000);
      return delay;
    }
  })
  .build();
```

That keeps trying forever (return `null` to stop).

## 6. Recommended cleanup or improvements
- Decide whether you want symmetry: either both phases infinite or both capped.
- If you want auto “second wave” retries after SignalR gives up, detect `onclose` and call a guarded `startHub()` again after a delay (avoid tight loops).
- Surface elapsed disconnected time in the header next to the warning to help users understand severity.

## 7. Quick summary
There is currently no single “reconnection timeout” variable:
- Initial connect: infinite retries (your code).
- After being connected: 4 automatic attempts (SignalR default) → then permanent disconnect.
If you expected infinite retries in both phases, you need to configure `withAutomaticReconnect` manually.

Let me know which behavior you’d like (infinite both phases, hard stop, manual button) and I can implement it.