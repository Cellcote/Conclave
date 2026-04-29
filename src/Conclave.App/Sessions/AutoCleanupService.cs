using Conclave.App.ViewModels;

namespace Conclave.App.Sessions;

// Background loop that periodically deletes sessions whose PR has been merged for at least
// `auto_cleanup.days` (default 7) days. Off by default — only runs when the user has
// enabled it in the Preferences dialog.
//
// Cadence: one tick ~30s after Start() (so a recently-relaunched app catches up quickly),
// then every hour. Each tick:
//   1. Snapshot the current sessions (UI thread).
//   2. For every session that has a cached PR record, refetch via `gh pr view` on the
//      loop's background thread so a freshly-merged PR's mergedAt timestamp lands in the
//      DB (otherwise we'd never observe the merge that happened while the app was closed).
//   3. Apply the refreshed PR info on the UI thread.
//   4. Decide which sessions to delete and call SessionManager.DeleteSession on the UI
//      thread for each.
//
// The "Clean up now" button in Preferences calls RunOnceAsync() which runs a single tick
// regardless of the enabled setting (the user explicitly asked for it).
public sealed class AutoCleanupService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly SessionManager _manager;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public AutoCleanupService(SessionManager manager) => _manager = manager;

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try { await Task.Delay(StartupDelay, ct); } catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(respectEnabled: true, ct); }
            catch { /* never let the loop die — try again next tick */ }

            try { await Task.Delay(TickInterval, ct); } catch (TaskCanceledException) { return; }
        }
    }

    // Public hook for the "Clean up now" button. Runs one tick regardless of the enabled
    // setting since this is a user-initiated action.
    public Task RunOnceAsync() => Task.Run(() => TickAsync(respectEnabled: false, _cts.Token));

    private async Task TickAsync(bool respectEnabled, CancellationToken ct)
    {
        // Read settings AND snapshot sessions inside a single UI-thread hop. Database owns
        // a single SqliteConnection that the rest of the app touches only from the UI
        // thread, so any settings/session-row read or write must run there. Background work
        // (the gh fetches below) reuses the snapshot without going back to the DB.
        var prep = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var enabled = SettingsKeys.ReadAutoCleanupEnabled(_manager.Db);
            var thresholdMs = (long)TimeSpan
                .FromDays(SettingsKeys.ReadAutoCleanupDays(_manager.Db))
                .TotalMilliseconds;
            var sessions = _manager.Projects
                .SelectMany(p => p.Sessions)
                .Select(s => (Session: s, HasPr: s.Pr is not null, Worktree: s.Worktree))
                .ToArray();
            return (enabled, thresholdMs, sessions);
        });

        if (respectEnabled && !prep.enabled) return;

        // Force a PR refresh on every session that has a cached PR — even open ones — so
        // mergedAt lands for PRs that were merged while Conclave was closed. Skip sessions
        // with no PR record at all (never had one) to keep the gh process count down.
        foreach (var (session, hasPr, worktree) in prep.sessions)
        {
            if (ct.IsCancellationRequested) return;
            if (!hasPr) continue;

            GhService.PullRequestInfo? info = null;
            try { info = GhService.TryGetPullRequest(worktree); } catch { /* ignore */ }
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _manager.ApplyPr(session, info));
        }

        var live = prep.sessions.Select(t => t.Session).ToArray();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Sweep(live, prep.thresholdMs));
    }

    private void Sweep(SessionVm[] sessions, long thresholdMs)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var s in sessions)
        {
            // Skip if the VM has been removed since the snapshot (e.g. user deleted it manually).
            if (s.Pr is not { State: PrState.Merged }) continue;
            // Don't yank a worktree out from under an in-progress turn.
            if (s.Status is not (SessionStatus.Idle or SessionStatus.Completed)) continue;

            var row = _manager.Db.GetSession(s.Id);
            if (row?.PrMergedAt is not { } mergedAt) continue;
            if (now - mergedAt < thresholdMs) continue;

            try { _manager.DeleteSession(s); } catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}
