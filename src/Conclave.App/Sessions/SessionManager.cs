using System.Collections.ObjectModel;
using System.Text.Json;
using Conclave.App.Design;
using Conclave.App.ViewModels;

namespace Conclave.App.Sessions;

// Owns the observable project/session VMs seen by the shell. Single source of truth:
// mutations go through here, which transactionally updates the DB and the VM collections.
public sealed class SessionManager : IDisposable
{
    private readonly Database _db;
    private readonly string _worktreeRoot;
    private readonly Tokens _tokens;

    public ObservableCollection<ProjectVm> Projects { get; } = new();

    public SessionManager(Database db, string worktreeRoot, Tokens tokens)
    {
        _db = db;
        _worktreeRoot = worktreeRoot;
        _tokens = tokens;
        Directory.CreateDirectory(_worktreeRoot);
        LoadFromDatabase();
    }

    public static SessionManager Open(Tokens tokens)
    {
        var dbPath = Database.DefaultPath();
        var db = Database.Open(dbPath);
        var root = Path.Combine(Path.GetDirectoryName(dbPath)!, "worktrees");
        return new SessionManager(db, root, tokens);
    }

    // --- Loading ---

    private void LoadFromDatabase()
    {
        Projects.Clear();
        foreach (var p in _db.GetProjects())
        {
            var vm = BuildProjectVm(p);
            foreach (var s in _db.GetSessionsForProject(p.Id))
                vm.Sessions.Add(BuildSessionVm(s));
            Projects.Add(vm);
        }
    }

    private ProjectVm BuildProjectVm(Project p)
    {
        var vm = new ProjectVm(_tokens)
        {
            Id = p.Id,
            Path = p.Path,
            DefaultBranch = p.DefaultBranch,
        };
        vm.Name = p.Name;
        return vm;
    }

    private SessionVm BuildSessionVm(Session s)
    {
        // A session persisted as Working/RunningTool means the app was killed mid-turn — the
        // claude process is gone and there's no CancellationSource to cancel, so leaving it
        // busy would wedge the composer with a Stop button that does nothing. Reset to Idle.
        var loadedStatus = Enum.TryParse<SessionStatus>(s.Status, out var st) ? st : SessionStatus.Idle;
        if (loadedStatus is SessionStatus.Working or SessionStatus.RunningTool)
        {
            loadedStatus = SessionStatus.Idle;
            _db.UpdateSessionStatus(s.Id, loadedStatus.ToString());
        }

        var vm = new SessionVm(_tokens)
        {
            Id = s.Id,
            Worktree = s.WorktreePath,
            Branch = s.BranchName,
            BaseBranch = s.BaseBranch,
            Model = s.Model,
            StartedUtc = s.StartedUtc is { } t
                ? DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime
                : DateTime.UtcNow,
            LastActivity = RelativeTime(s.LastActiveAt),
            Status = loadedStatus,
            Unread = s.UnreadCount,
            Diff = BuildDiff(s),
        };
        vm.Title = s.Name;
        vm.ClaudeSessionId = s.ClaudeSessionId;
        vm.PermissionMode = string.IsNullOrEmpty(s.PermissionMode) ? "default" : s.PermissionMode;
        vm.TotalCostUsd = s.TotalCostUsd;

        // Restore Plan state if claude has ever run TodoWrite for this session.
        if (!string.IsNullOrEmpty(s.PlanJson))
        {
            try
            {
                var items = ParsePlanJson(s.PlanJson, vm.Tokens);
                vm.ReplacePlan(items);
            }
            catch { /* malformed plan_json — ignore, start empty */ }
        }

        // Seed PR state from cached DB values so the card renders immediately; refresh from
        // `gh pr view` in the background so stale info gets replaced without blocking load.
        if (s.PrNumber is { } n && s.PrState is { } state)
        {
            vm.Pr = new PullRequestVm
            {
                Number = n,
                State = Enum.TryParse<PrState>(state, out var ps) ? ps : PrState.Open,
                Branch = s.BranchName,
                Base = s.BaseBranch,
            };
        }
        _ = Task.Run(() => RefreshPrInBackground(vm));

        return vm;
    }

    private void RefreshPrInBackground(SessionVm vm)
    {
        try
        {
            var info = GhService.TryGetPullRequest(vm.Worktree);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (info is not { } pr) { vm.Pr = null; _db.UpdateSessionPr(vm.Id, null, null); return; }
                var state = pr.IsDraft ? PrState.Draft
                          : pr.State.Equals("MERGED", StringComparison.OrdinalIgnoreCase) ? PrState.Merged
                          : pr.State.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) ? PrState.Closed
                          : PrState.Open;
                vm.Pr = new PullRequestVm
                {
                    Number = pr.Number, State = state,
                    Branch = pr.HeadRefName, Base = pr.BaseRefName, MetaTail = pr.Title,
                };
                _db.UpdateSessionPr(vm.Id, pr.Number, state.ToString());
            });
        }
        catch { /* best-effort */ }
    }

    private DiffStatVm BuildDiff(Session s)
    {
        // Compute from the worktree directly — cheaper and always fresh. The cached values
        // on the row (DiffFiles/Add/Del) are only used as an initial paint if the git call
        // fails for any reason.
        try
        {
            var diff = WorktreeService.ComputeDiff(s.WorktreePath, s.BaseBranch);
            var vm = new DiffStatVm { Files = diff.Files, Add = diff.Add, Del = diff.Del };
            foreach (var c in diff.Changes)
            {
                vm.Changes.Add(new FileChangeVm
                {
                    Kind = c.Kind switch
                    {
                        "A" => FileChangeKind.Added,
                        "D" => FileChangeKind.Deleted,
                        _ => FileChangeKind.Modified,
                    },
                    Path = c.Path,
                    Add = c.Add,
                    Del = c.Del,
                });
            }
            // Persist so the sidebar still has numbers if the worktree disappears later.
            if (diff.Files != s.DiffFiles || diff.Add != s.DiffAdd || diff.Del != s.DiffDel)
                _db.UpdateSessionDiff(s.Id, diff.Files, diff.Add, diff.Del);
            return vm;
        }
        catch
        {
            return new DiffStatVm { Files = s.DiffFiles, Add = s.DiffAdd, Del = s.DiffDel };
        }
    }

    // Refresh PR info for one session from `gh pr view`. Safe no-op if gh isn't installed
    // or the branch has no associated PR. Called after a claude turn and on session load.
    public void RefreshPr(SessionVm s)
    {
        try
        {
            var info = GhService.TryGetPullRequest(s.Worktree);
            if (info is not { } pr)
            {
                s.Pr = null;
                _db.UpdateSessionPr(s.Id, null, null);
                return;
            }
            var state = pr.IsDraft ? PrState.Draft
                      : pr.State.Equals("MERGED", StringComparison.OrdinalIgnoreCase) ? PrState.Merged
                      : pr.State.Equals("CLOSED", StringComparison.OrdinalIgnoreCase) ? PrState.Closed
                      : PrState.Open;
            s.Pr = new PullRequestVm
            {
                Number = pr.Number,
                State = state,
                Branch = pr.HeadRefName,
                Base = pr.BaseRefName,
                MetaTail = pr.Title,
            };
            _db.UpdateSessionPr(s.Id, pr.Number, state.ToString());
        }
        catch { /* best-effort */ }
    }

    // Refresh diff stats for one session — call after a claude turn completes.
    public void RefreshDiff(SessionVm s)
    {
        try
        {
            var diff = WorktreeService.ComputeDiff(s.Worktree, s.BaseBranch);
            s.Diff.Files = diff.Files;
            s.Diff.Add = diff.Add;
            s.Diff.Del = diff.Del;
            s.Diff.Changes.Clear();
            foreach (var c in diff.Changes)
            {
                s.Diff.Changes.Add(new FileChangeVm
                {
                    Kind = c.Kind switch
                    {
                        "A" => FileChangeKind.Added,
                        "D" => FileChangeKind.Deleted,
                        _ => FileChangeKind.Modified,
                    },
                    Path = c.Path, Add = c.Add, Del = c.Del,
                });
            }
            _db.UpdateSessionDiff(s.Id, diff.Files, diff.Add, diff.Del);
        }
        catch { /* best-effort */ }
    }

    public void UpdateClaudeSessionId(SessionVm s, string claudeSessionId)
    {
        _db.UpdateClaudeSessionId(s.Id, claudeSessionId);
        s.ClaudeSessionId = claudeSessionId;
    }

    public void PersistPlan(SessionVm s, string? planJson) =>
        _db.UpdateSessionPlan(s.Id, planJson);

    public void UpdatePermissionMode(SessionVm s, string mode)
    {
        _db.UpdateSessionPermissionMode(s.Id, mode);
        s.PermissionMode = mode;
    }

    public void AddCost(SessionVm s, double delta)
    {
        if (delta <= 0) return;
        _db.AddSessionCost(s.Id, delta);
        s.TotalCostUsd += delta;
    }

    // --- Transcript persistence ---

    // Load this session's messages from the DB the first time it's activated. Idempotent —
    // subsequent calls no-op via SessionVm.TranscriptLoaded.
    public void LoadTranscriptIfNeeded(SessionVm s)
    {
        if (s.TranscriptLoaded) return;
        s.TranscriptLoaded = true;
        foreach (var row in _db.GetMessages(s.Id))
        {
            var msg = new TranscriptMessageVm
            {
                Id = row.Id,
                Tokens = _tokens,
                Role = row.Role == "User" ? MessageRole.User : MessageRole.Assistant,
                Time = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt)
                    .ToLocalTime().ToString("HH:mm"),
                Content = row.Content,
            };
            if (!string.IsNullOrEmpty(row.ToolsJson))
            {
                foreach (var tool in DeserializeTools(row.ToolsJson))
                    msg.Tools.Add(tool);
            }
            s.AppendTranscript(msg);
        }
    }

    // Called at each append to Transcript. Must be invoked from the UI thread.
    public void PersistMessage(SessionVm session, TranscriptMessageVm msg)
    {
        _db.InsertMessage(new MessageRow
        {
            Id = msg.Id,
            SessionId = session.Id,
            Role = msg.Role == MessageRole.User ? "User" : "Assistant",
            Content = msg.Content,
            ToolsJson = msg.Tools.Count > 0 ? SerializeTools(msg.Tools) : null,
            CreatedAt = Database.Now(),
            Seq = _db.NextSeq(session.Id),
        });
    }

    // Called when tool status/meta changes after its enclosing message was already persisted.
    public void UpdateMessageTools(TranscriptMessageVm msg)
    {
        var json = msg.Tools.Count > 0 ? SerializeTools(msg.Tools) : null;
        _db.UpdateMessage(msg.Id, msg.Content, json);
    }

    // Parses raw TodoWrite input JSON (as emitted by claude) into PlanItemVm instances.
    // Shape: { "todos": [{ "content", "status", "activeForm" }, ...] }.
    public static IReadOnlyList<PlanItemVm> ParsePlanJson(string json, Tokens tokens)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("todos", out var todos) || todos.ValueKind != JsonValueKind.Array)
            return Array.Empty<PlanItemVm>();

        var result = new List<PlanItemVm>(todos.GetArrayLength());
        foreach (var t in todos.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            result.Add(new PlanItemVm
            {
                Tokens = tokens,
                Content = Str(t, "content"),
                ActiveForm = Str(t, "activeForm"),
                Status = Str(t, "status") switch
                {
                    "in_progress" => PlanItemStatus.InProgress,
                    "completed" => PlanItemStatus.Completed,
                    _ => PlanItemStatus.Pending,
                },
            });
        }
        return result;

        static string Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
    }

    private static string SerializeTools(IEnumerable<ToolCallVm> tools)
    {
        var rows = tools.Select(t => new ToolCallRow(t.Kind, t.Target, t.Meta, t.Status.ToString()))
            .ToArray();
        return JsonSerializer.Serialize(rows, AppJsonContext.Default.ToolCallRowArray);
    }

    private IEnumerable<ToolCallVm> DeserializeTools(string json)
    {
        var rows = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolCallRowArray)
            ?? Array.Empty<ToolCallRow>();
        foreach (var r in rows)
        {
            yield return new ToolCallVm
            {
                Tokens = _tokens,
                Kind = r.Kind,
                Target = r.Target,
                Meta = r.Meta,
                Status = Enum.TryParse<ToolStatus>(r.Status, out var s) ? s : ToolStatus.Ok,
            };
        }
    }

    public void UpdateStatus(SessionVm s, SessionStatus status)
    {
        _db.UpdateSessionStatus(s.Id, status.ToString());
        s.Status = status;
    }

    private static string RelativeTime(long unixMs)
    {
        var diff = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        if (diff.TotalSeconds < 60) return "now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    // --- Projects ---

    public ProjectVm CreateProject(string name, string path)
    {
        if (!WorktreeService.IsGitRepo(path))
            throw new InvalidOperationException(
                $"Not a git repository: {path}\nPick a folder that contains a .git directory.");
        var defaultBranch = WorktreeService.DetectDefaultBranch(path);
        var p = new Project(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Path: path,
            DefaultBranch: defaultBranch,
            CreatedAt: Database.Now());
        _db.InsertProject(p);
        var vm = BuildProjectVm(p);
        Projects.Add(vm);
        return vm;
    }

    public void RenameProject(ProjectVm vm, string newName)
    {
        _db.UpdateProjectName(vm.Id, newName);
        vm.Name = newName;
    }

    public void DeleteProject(ProjectVm vm)
    {
        foreach (var s in vm.Sessions.ToList())
            DeleteSession(s, skipRemovalFromParent: true);
        _db.DeleteProject(vm.Id);
        Projects.Remove(vm);
    }

    // --- Sessions ---

    public SessionVm CreateSession(
        ProjectVm project,
        string? branchName = null,
        string model = "Sonnet 4.5",
        string permissionMode = "default")
    {
        var projectRecord = _db.GetProject(project.Id)
            ?? throw new InvalidOperationException($"project {project.Id} not found");

        string slug;
        string branch;
        string display;
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            branch = branchName.Trim();
            slug = DeriveSlug(branch);
            display = slug.Replace('-', ' ');
            if (SessionExistsByBranch(project, branch))
                throw new InvalidOperationException($"session for branch '{branch}' already exists");
        }
        else
        {
            (display, slug) = UniqueSlug(project);
            branch = $"conclave/{slug}";
        }

        var wtPath = Path.Combine(_worktreeRoot, project.Id, slug);
        WorktreeService.AddWorktree(projectRecord.Path, wtPath, branch, projectRecord.DefaultBranch);

        var now = Database.Now();
        var s = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = project.Id,
            Name = display,
            BranchName = branch,
            WorktreePath = wtPath,
            BaseBranch = projectRecord.DefaultBranch,
            Model = model,
            Status = SessionStatus.Idle.ToString(),
            PermissionMode = permissionMode,
            CreatedAt = now,
            LastActiveAt = now,
        };
        _db.InsertSession(s);

        var vm = BuildSessionVm(s);
        project.Sessions.Add(vm);
        return vm;
    }

    public void RenameSession(SessionVm s, string newName)
    {
        var trimmed = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("session name cannot be empty", nameof(newName));
        _db.UpdateSessionName(s.Id, trimmed);
        s.Title = trimmed;
    }

    public void DeleteSession(SessionVm s, bool skipRemovalFromParent = false)
    {
        var project = FindProjectOf(s);
        var projectRecord = project is null ? null : _db.GetProject(project.Id);
        if (projectRecord is not null)
        {
            try { WorktreeService.RemoveWorktree(projectRecord.Path, s.Worktree, s.Branch); }
            catch { /* best-effort */ }
        }
        _db.DeleteSession(s.Id);
        if (!skipRemovalFromParent && project is not null) project.Sessions.Remove(s);
    }

    public void TouchSession(SessionVm s)
    {
        _db.TouchSession(s.Id);
        // Intentionally not rebuilding the VM on touch — LastActivity strings are approximate.
    }

    // --- Helpers ---

    private ProjectVm? FindProjectOf(SessionVm s) =>
        Projects.FirstOrDefault(p => p.Sessions.Contains(s));

    private (string Display, string Slug) UniqueSlug(ProjectVm project)
    {
        var existing = project.Sessions.Select(x => x.Branch).ToHashSet();
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var (display, slug) = SlugGenerator.New();
            if (!existing.Contains($"conclave/{slug}")) return (display, slug);
        }
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return ($"session {suffix}", $"session-{suffix}");
    }

    private static string DeriveSlug(string branch)
    {
        var lastSlash = branch.LastIndexOf('/');
        var raw = lastSlash >= 0 ? branch[(lastSlash + 1)..] : branch;
        return new string(raw.Select(c => char.IsLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '-').ToArray());
    }

    private bool SessionExistsByBranch(ProjectVm project, string branch) =>
        project.Sessions.Any(s => string.Equals(s.Branch, branch, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _db.Dispose();
}
