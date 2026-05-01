namespace Conclave.App.Platform;

// Cross-platform wake detector. Runs a low-frequency heartbeat loop and fires DeviceWoke
// whenever a tick lands significantly later than scheduled — a strong signal that the OS
// suspended the process (laptop sleep, Windows hybrid sleep, hibernation). Works on every
// platform without OS-specific bindings, which keeps the AOT story simple and avoids the
// brittle ObjC-block marshalling that NSWorkspaceDidWakeNotification would otherwise need.
//
// Trade-off: there's a small detection latency equal to TickInterval (1.5s). For waking
// from sleep that's imperceptible. False positives can occur if the process is starved of
// CPU for several seconds (debugger pause, severe GC); StallDetectionService treats wake
// events as a hint to scan, not a definitive stall signal, so this is harmless.
public sealed class HeartbeatPowerService : IPlatformPowerService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.5);

    // A tick that takes more than this long beyond TickInterval is treated as a wake.
    // 8s gives generous headroom for "the GC paused us" without missing actual sleeps,
    // which on macOS routinely register as 30s+ jumps even on short lid-closes.
    private static readonly TimeSpan WakeThreshold = TimeSpan.FromSeconds(8);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event Action? DeviceWoke;

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var expected = DateTime.UtcNow + TickInterval;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TickInterval, ct); }
            catch (TaskCanceledException) { return; }

            var now = DateTime.UtcNow;
            var drift = now - expected;
            if (drift > WakeThreshold)
            {
                try { DeviceWoke?.Invoke(); } catch { /* never let a subscriber kill the loop */ }
            }
            // Re-base off "now" rather than expected+TickInterval so a single late tick
            // doesn't keep firing wake events for several intervals after a long sleep.
            expected = now + TickInterval;
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}
