using Conclave.App.Claude;
using Conclave.App.Platform;
using Conclave.App.ViewModels;

namespace Conclave.App.Sessions;

// Detects sessions whose claude stream has gone silent past a threshold while still
// nominally Working/RunningTool — typically caused by the laptop sleeping and the in-flight
// HTTPS stream dying without any visible event on our side. Surfaces them as IsStalled=true
// (the sidebar "Needs attention" filter picks them up).
//
// When the user has opted in via Preferences, the service additionally cancels the dead
// turn and sends a synthetic "continue" prompt so the conversation resumes silently.
//
// Cadence: a periodic scan every TickInterval, plus an immediate scan whenever the power
// service signals a wake event. Wake events use a much shorter silence threshold because
// a wake is a high-confidence indicator that the network was severed.
//
// Threading: the loop runs on a background task; status reads + VM mutations marshal back
// to the UI thread via Dispatcher.UIThread.InvokeAsync, mirroring the AutoCleanupService
// pattern.
public sealed class StallDetectionService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    // Periodic-scan threshold: a tool can legitimately run >60s without text deltas, so we
    // err on the conservative side. Network blips that resolve themselves in <90s won't
    // false-positive the user.
    private static readonly TimeSpan SilenceThreshold = TimeSpan.FromSeconds(90);
    // Post-wake threshold: the OS just resumed, so any silence is almost certainly a dead
    // socket. Shorter so the user sees the "Resume" affordance (or auto-resume kicks in)
    // within seconds of unlocking the screen.
    private static readonly TimeSpan PostWakeSilenceThreshold = TimeSpan.FromSeconds(20);
    // The window during which a freshly-arrived ResultEvent vetoes auto-resume — the network
    // recovered just as we tried to cancel and a real completion landed. Without this veto
    // we'd pile a synthetic "continue" on top of a successful turn.
    private static readonly TimeSpan ResultRaceWindow = TimeSpan.FromSeconds(2);
    // Cap on the await for an in-flight turn to fully unwind after we cancel it. Past this
    // we assume the subprocess is wedged and bail out (the kill in ClaudeClient's finally
    // is already a belt-and-braces fallback for the SIGTERM path).
    private static readonly TimeSpan CancelSettleTimeout = TimeSpan.FromSeconds(5);
    // Brief debounce for back-to-back wake notifications. macOS occasionally fires the wake
    // event multiple times for a single resume, and our HeartbeatPowerService can fire once
    // too if the next tick is also late.
    private static readonly TimeSpan WakeDebounce = TimeSpan.FromSeconds(3);

    private readonly SessionManager _manager;
    private readonly ClaudeService _claudeService;
    private readonly IPlatformPowerService _power;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private DateTime _lastWakeUtc;
    private DateTime _lastWakeHandledUtc;
    // Single-flight guard: at most one auto-resume orchestration in flight across all
    // sessions. Wake-storms or post-wake fan-outs would otherwise serialise themselves
    // through the Database (UI thread) anyway, but bounding to one keeps the flow obvious.
    private int _resumeInFlight;

    public StallDetectionService(SessionManager manager, ClaudeService claudeService, IPlatformPowerService power)
    {
        _manager = manager;
        _claudeService = claudeService;
        _power = power;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _power.DeviceWoke += OnDeviceWoke;
        _power.Start();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private void OnDeviceWoke()
    {
        var now = DateTime.UtcNow;
        if (now - _lastWakeUtc < WakeDebounce) return;
        _lastWakeUtc = now;
        // Best-effort kick — the periodic loop will follow up if this misses.
        _ = Task.Run(() => ScanAsync(postWake: true, _cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try { await Task.Delay(StartupDelay, ct); } catch (TaskCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await ScanAsync(postWake: false, ct); } catch { /* never let the loop die */ }
            try { await Task.Delay(TickInterval, ct); } catch (TaskCanceledException) { return; }
        }
    }

    private async Task ScanAsync(bool postWake, CancellationToken ct)
    {
        if (postWake) _lastWakeHandledUtc = DateTime.UtcNow;
        var threshold = postWake ? PostWakeSilenceThreshold : SilenceThreshold;
        var now = DateTime.UtcNow;

        // Snapshot candidates inside one UI-thread hop. Taking SessionVm references out is
        // safe — they're long-lived and we re-check status under the dispatcher before
        // mutating.
        var candidates = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var list = new List<SessionVm>();
            foreach (var p in _manager.Projects)
                foreach (var s in p.Sessions)
                {
                    if (s.IsStalled) continue; // already flagged
                    if (s.Status is not (SessionStatus.Working or SessionStatus.RunningTool)) continue;
                    if (s.LastStreamEventAt == default) continue; // turn just started
                    if (now - s.LastStreamEventAt < threshold) continue;
                    list.Add(s);
                }
            return list;
        });

        if (candidates.Count == 0 || ct.IsCancellationRequested) return;

        foreach (var session in candidates)
        {
            if (ct.IsCancellationRequested) return;

            // Re-check on the UI thread immediately before flipping; the periodic-scan
            // window can race with a real event arriving (e.g. tool just finished) and we
            // don't want to flag a session that's already moved past silence.
            var shouldFlag = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (session.IsStalled) return false;
                if (session.Status is not (SessionStatus.Working or SessionStatus.RunningTool)) return false;
                if (DateTime.UtcNow - session.LastStreamEventAt < threshold) return false;
                session.IsStalled = true;
                return true;
            });

            if (!shouldFlag) continue;

            // Auto-resume eligibility: opt-in setting, only Working (not mid-tool, where
            // resume could re-execute side effects), no composer text being typed, and
            // we haven't already retried this stall once.
            var shouldAutoResume = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                SettingsKeys.ReadAutoResumeStalledSessions(_manager.Db)
                && session.Status == SessionStatus.Working
                && string.IsNullOrEmpty(session.ComposerDraft)
                && session.AutoResumeAttempts < 1
                && DateTime.UtcNow - session.LastResultEventAt > ResultRaceWindow);

            if (shouldAutoResume)
            {
                await ResumeAsync(session, ignoreRetryCap: false);
            }
        }
    }

    // Public so ShellVm.ResumeStalledSession (the manual button) can call it. Manual clicks
    // pass ignoreRetryCap=true since the user explicitly asked.
    public async Task ResumeAsync(SessionVm session, bool ignoreRetryCap)
    {
        // Single-flight: avoid two parallel resumes piling up if a wake event and the timer
        // tick race. A second caller just waits for the first to finish — there's no work
        // for it to do once the session has resumed (IsStalled clears).
        if (Interlocked.Exchange(ref _resumeInFlight, 1) == 1) return;
        try
        {
            // Final eligibility re-check on the UI thread. State may have changed since
            // ScanAsync took its snapshot (user may have clicked Stop, network may have
            // recovered). Capture the in-flight task while we're here so we can await it.
            var prep = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<(bool Proceed, Task? Task)>(() =>
            {
                if (!session.IsStalled) return (false, null);
                if (!ignoreRetryCap && session.AutoResumeAttempts >= 1) return (false, null);
                if (session.Status != SessionStatus.Working) return (false, null);
                session.AutoResumeAttempts++;
                session.SuppressNextTurnCompleteNotification = true;
                try { session.CancellationSource?.Cancel(); }
                catch (ObjectDisposedException) { /* race: already disposed */ }
                return (true, session.CurrentTurnTask);
            });

            if (!prep.Proceed) return;

            // Wait for the cancelled turn to fully unwind. ClaudeClient kills the
            // subprocess in its finally when the token is cancelled, so this should
            // complete promptly even if the network is dead.
            if (prep.Task is { } t)
            {
                try { await t.WaitAsync(CancelSettleTimeout); }
                catch (TimeoutException)
                {
                    // Subprocess wedged — give up and leave the session as needs-attention.
                    // Future scans won't retry because AutoResumeAttempts is now 1.
                    return;
                }
                catch (OperationCanceledException) { /* expected */ }
                catch { /* swallow — the OCE-handler already flipped status to Idle */ }
            }

            // Race guard: a ResultEvent may have landed in the stream between Cancel() and
            // the subprocess actually exiting. ClaudeService clears IsStalled on any event,
            // so if it's false here the network recovered and the turn legitimately
            // completed — sending "continue" would tack a fresh turn onto a successful one.
            var stillStalled = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!session.IsStalled)
                {
                    // Real completion landed; the suppression flag is no longer needed.
                    session.SuppressNextTurnCompleteNotification = false;
                    return false;
                }
                return true;
            });
            if (!stillStalled) return;

            // Send the synthetic prompt. RunTurnAsync re-stamps LastStreamEventAt and
            // clears IsStalled at the top, so by the time it returns control we're cleanly
            // back in Working with a fresh stream.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var task = _claudeService.RunTurnAsync(session, "continue", isAutoResume: true);
                session.CurrentTurnTask = task;
            });
        }
        finally
        {
            Interlocked.Exchange(ref _resumeInFlight, 0);
        }
    }

    public void Dispose()
    {
        try { _power.DeviceWoke -= OnDeviceWoke; } catch { }
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        _power.Dispose();
    }
}
