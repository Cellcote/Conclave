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

    // Exposed so subsystems wired alongside SessionManager (settings UI, AutoCleanupService)
    // can read/write directly without re-routing every helper through this class.
    public Database Db => _db;

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
            Kind = p.Kind,
        };
        vm.Name = p.Name;
        if (p.Kind == ProjectKinds.Fusion)
            foreach (var m in _db.GetMembers(p.Id))
                vm.MemberIds.Add(m.MemberId);
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
        vm.PendingPreamble = s.PendingPreamble;

        // Fusion sessions: load secondary worktree paths so claude can be spawned with --add-dir
        // for each. Ordinal 0 == primary == s.WorktreePath, so skip it.
        var fusionDirs = _db.GetSessionWorktrees(s.Id);
        if (fusionDirs.Count > 0)
            vm.AdditionalDirs = fusionDirs.Where(w => w.Ordinal > 0)
                .Select(w => w.WorktreePath).ToList();

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
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyPr(vm, info));
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
        try { ApplyPr(s, GhService.TryGetPullRequest(s.Worktree)); }
        catch { /* best-effort */ }
    }

    // Apply a (possibly null) PR-info result to a session VM and the cached DB row. Must be
    // called from the UI thread. Split out so background workers can fetch off-thread, then
    // marshal the apply step here without duplicating the state-mapping logic.
    public void ApplyPr(SessionVm s, GhService.PullRequestInfo? info)
    {
        if (info is not { } pr)
        {
            s.Pr = null;
            _db.UpdateSessionPr(s.Id, null, null, null);
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
        _db.UpdateSessionPr(s.Id, pr.Number, state.ToString(), pr.MergedAtUnixMs);
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

    public void ClearPendingPreamble(SessionVm s)
    {
        if (s.PendingPreamble is null) return;
        _db.UpdateSessionPendingPreamble(s.Id, null);
        s.PendingPreamble = null;
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
                ClaudeUuid = row.ClaudeUuid,
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
            ClaudeUuid = msg.ClaudeUuid,
        });
    }

    // Update claude's per-message uuid after the assistant message has been persisted.
    // Stream-delta paths insert the row before the AssistantEvent arrives (which is what
    // carries the uuid), so we set it as a follow-up rather than at insert time.
    public void UpdateMessageClaudeUuid(TranscriptMessageVm msg)
    {
        if (string.IsNullOrEmpty(msg.ClaudeUuid)) return;
        _db.UpdateMessageClaudeUuid(msg.Id, msg.ClaudeUuid);
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
        // Only "claude is done" transitions bump the session — intermediate Working /
        // RunningTool flips during a turn must not reorder the sidebar.
        if (status is SessionStatus.Idle or SessionStatus.Error)
        {
            _db.TouchSession(s.Id);
            BumpToTop(s);
        }
    }

    private void BumpToTop(SessionVm s)
    {
        var project = FindProjectOf(s);
        if (project is null) return;
        var idx = project.Sessions.IndexOf(s);
        if (idx > 0) project.Sessions.Move(idx, 0);
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
            CreatedAt: Database.Now(),
            Kind: ProjectKinds.Repo);
        _db.InsertProject(p);
        var vm = BuildProjectVm(p);
        Projects.Add(vm);
        return vm;
    }

    // Composes a fusion project from existing repo-kind projects. The primary's worktree is
    // the cwd for sessions; secondaries are passed to claude via --add-dir. Members must all
    // be Kind = "repo"; nested fusions are not supported.
    public ProjectVm CreateFusionProject(string name, ProjectVm primary, IReadOnlyList<ProjectVm> secondaries)
    {
        if (primary.Kind != ProjectKinds.Repo)
            throw new InvalidOperationException("primary must be a repo project");
        if (secondaries.Count == 0)
            throw new InvalidOperationException("a fusion project needs at least one secondary repo");
        foreach (var sec in secondaries)
        {
            if (sec.Kind != ProjectKinds.Repo)
                throw new InvalidOperationException($"member '{sec.Name}' is not a repo project");
            if (sec.Id == primary.Id)
                throw new InvalidOperationException("primary and secondary must differ");
        }
        if (secondaries.Select(s => s.Id).Distinct().Count() != secondaries.Count)
            throw new InvalidOperationException("duplicate secondary repos");

        var p = new Project(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Path: "",
            DefaultBranch: "",
            CreatedAt: Database.Now(),
            Kind: ProjectKinds.Fusion);
        _db.InsertProject(p);
        _db.InsertMember(new ProjectMember(p.Id, primary.Id, 0));
        for (int i = 0; i < secondaries.Count; i++)
            _db.InsertMember(new ProjectMember(p.Id, secondaries[i].Id, i + 1));

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

        return projectRecord.Kind == ProjectKinds.Fusion
            ? CreateFusionSession(project, projectRecord, branch, slug, display, model, permissionMode)
            : CreateRepoSession(project, projectRecord, branch, slug, display, model, permissionMode);
    }

    private SessionVm CreateRepoSession(
        ProjectVm project, Project projectRecord, string branch, string slug,
        string display, string model, string permissionMode)
    {
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
        project.Sessions.Insert(0, vm);
        return vm;
    }

    private SessionVm CreateFusionSession(
        ProjectVm project, Project projectRecord, string branch, string slug,
        string display, string model, string permissionMode)
    {
        // Resolve members in ordinal order. Ordinal 0 == primary (whose worktree is cwd).
        var members = _db.GetMembers(project.Id);
        if (members.Count < 2)
            throw new InvalidOperationException(
                "fusion project has fewer than 2 active member repos — cannot start a session");

        var memberRecords = new List<Project>(members.Count);
        foreach (var m in members)
        {
            var rec = _db.GetProject(m.MemberId)
                ?? throw new InvalidOperationException($"fusion member {m.MemberId} no longer exists");
            memberRecords.Add(rec);
        }

        // worktrees/<fusion_id>/<slug>/<member_id[..8]>. Same branch name in every repo, each
        // based off its own DefaultBranch. The fusion add helper rolls back partial successes.
        var fusionRoot = Path.Combine(_worktreeRoot, project.Id, slug);
        var specs = memberRecords
            .Select(r => new WorktreeService.FusionAddSpec(
                r.Path, Path.Combine(fusionRoot, r.Id[..8]), branch, r.DefaultBranch))
            .ToList();
        WorktreeService.AddWorktreesForFusion(specs);

        // Primary's worktree path/base_branch hydrate the sessions row; other members live in
        // session_worktrees. The primary is also recorded in session_worktrees (ordinal 0)
        // so callers can iterate uniformly without special-casing it.
        var primaryWt = specs[0].WorktreePath;
        var now = Database.Now();
        var s = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = project.Id,
            Name = display,
            BranchName = branch,
            WorktreePath = primaryWt,
            BaseBranch = memberRecords[0].DefaultBranch,
            Model = model,
            Status = SessionStatus.Idle.ToString(),
            PermissionMode = permissionMode,
            CreatedAt = now,
            LastActiveAt = now,
        };
        _db.InsertSession(s);
        for (int i = 0; i < specs.Count; i++)
        {
            _db.InsertSessionWorktree(new SessionWorktree(
                SessionId: s.Id,
                MemberProjectId: memberRecords[i].Id,
                WorktreePath: specs[i].WorktreePath,
                BranchName: branch,
                BaseBranch: memberRecords[i].DefaultBranch,
                Ordinal: i));
        }

        var vm = BuildSessionVm(s);
        project.Sessions.Insert(0, vm);
        return vm;
    }

    // Fork: spawn a new session that branches off the source's current state. The new
    // worktree starts at the source's HEAD with its uncommitted tracked changes carried over
    // (untracked files are skipped — v1 limitation). The transcript is copied wholesale so
    // the UI reads continuously, and the source's claude session id is staged so the first
    // turn passes `--fork-session`, making the model retain its prior context.
    public SessionVm ForkSession(SessionVm source)
    {
        var project = FindProjectOf(source)
            ?? throw new InvalidOperationException("source session is not in any project");
        var projectRecord = _db.GetProject(project.Id)
            ?? throw new InvalidOperationException($"project {project.Id} not found");

        var sourceSlug = DeriveSlug(source.Branch);
        var forkSuffix = Guid.NewGuid().ToString("N")[..6];
        var slug = $"{sourceSlug}-fork-{forkSuffix}";
        var branch = $"conclave/{slug}";

        string primaryWt;
        string baseBranch;
        IReadOnlyList<SessionWorktree>? forkedFusionRows = null;

        if (projectRecord.Kind == ProjectKinds.Fusion)
        {
            (primaryWt, baseBranch, forkedFusionRows) =
                ForkFusionWorktrees(project, source, slug, branch);
        }
        else
        {
            primaryWt = Path.Combine(_worktreeRoot, project.Id, slug);
            WorktreeService.ForkWorktree(projectRecord.Path, source.Worktree, primaryWt, branch);
            baseBranch = source.BaseBranch;
        }

        var now = Database.Now();
        var s = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = project.Id,
            Name = $"Fork of {source.Title}",
            BranchName = branch,
            WorktreePath = primaryWt,
            BaseBranch = baseBranch,
            Model = source.Model,
            Status = SessionStatus.Idle.ToString(),
            PermissionMode = source.PermissionMode,
            CreatedAt = now,
            LastActiveAt = now,
        };
        _db.InsertSession(s);

        if (forkedFusionRows is not null)
            foreach (var row in forkedFusionRows)
                _db.InsertSessionWorktree(row with { SessionId = s.Id });

        foreach (var row in _db.GetMessages(source.Id))
        {
            _db.InsertMessage(new MessageRow
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = s.Id,
                Role = row.Role,
                Content = row.Content,
                ToolsJson = row.ToolsJson,
                CreatedAt = row.CreatedAt,
                Seq = row.Seq,
                ClaudeUuid = row.ClaudeUuid,
            });
        }

        var vm = BuildSessionVm(s);
        vm.PendingForkFromClaudeSessionId = source.ClaudeSessionId;

        var idx = project.Sessions.IndexOf(source);
        if (idx < 0) project.Sessions.Insert(0, vm);
        else project.Sessions.Insert(idx + 1, vm);
        return vm;
    }

    // Forks a fusion session's worktrees in lockstep across every member repo. Returns the
    // primary's new worktree path, the primary's base branch, and the list of session_worktrees
    // rows the caller should persist after inserting the new session row. The session_id field
    // on the returned rows is empty — callers fill it in.
    private (string PrimaryWt, string BaseBranch, IReadOnlyList<SessionWorktree> Rows)
        ForkFusionWorktrees(ProjectVm project, SessionVm source, string slug, string branch)
    {
        var sourceRows = _db.GetSessionWorktrees(source.Id);
        if (sourceRows.Count < 2)
            throw new InvalidOperationException(
                "fusion session is missing per-member worktree rows — cannot fork");

        var fusionRoot = Path.Combine(_worktreeRoot, project.Id, slug);
        var specs = new List<WorktreeService.FusionForkSpec>(sourceRows.Count);
        var newRows = new List<SessionWorktree>(sourceRows.Count);
        foreach (var row in sourceRows)
        {
            var memberRec = _db.GetProject(row.MemberProjectId)
                ?? throw new InvalidOperationException(
                    $"fusion member {row.MemberProjectId} no longer exists");
            var newWt = Path.Combine(fusionRoot, memberRec.Id[..8]);
            specs.Add(new WorktreeService.FusionForkSpec(
                memberRec.Path, row.WorktreePath, newWt, branch));
            newRows.Add(new SessionWorktree(
                SessionId: "",
                MemberProjectId: row.MemberProjectId,
                WorktreePath: newWt,
                BranchName: branch,
                BaseBranch: row.BaseBranch,
                Ordinal: row.Ordinal));
        }
        WorktreeService.ForkWorktreesForFusion(specs);
        return (specs[0].NewWorktreePath, sourceRows[0].BaseBranch, newRows);
    }

    // Fork at a specific message: same shape as ForkSession, but the new session's transcript
    // is truncated at `forkPoint` and the prior conversation is injected as a system-prompt
    // preamble on the first turn (path A — synthetic context). The model sees a "you have
    // already had this conversation" prelude rather than truly resuming server-side state,
    // since the CLI's --fork-session only forks at the resumed session's tail, not at an
    // arbitrary point.
    public SessionVm ForkSessionAtMessage(SessionVm source, TranscriptMessageVm forkPoint)
    {
        var project = FindProjectOf(source)
            ?? throw new InvalidOperationException("source session is not in any project");
        var projectRecord = _db.GetProject(project.Id)
            ?? throw new InvalidOperationException($"project {project.Id} not found");

        // Pull the source's persisted messages and truncate at the fork point. We use the DB
        // (not the in-memory Transcript) because tool-result updates are persisted out-of-band
        // and we want the authoritative on-disk content.
        var allMessages = _db.GetMessages(source.Id);
        var keep = new List<MessageRow>();
        foreach (var m in allMessages)
        {
            keep.Add(m);
            if (m.Id == forkPoint.Id) break;
        }
        if (keep.Count == 0 || keep[^1].Id != forkPoint.Id)
            throw new InvalidOperationException("fork point not found in source session");

        var sourceSlug = DeriveSlug(source.Branch);
        var forkSuffix = Guid.NewGuid().ToString("N")[..6];
        var slug = $"{sourceSlug}-fork-{forkSuffix}";
        var branch = $"conclave/{slug}";

        string primaryWt;
        string baseBranch;
        IReadOnlyList<SessionWorktree>? forkedFusionRows = null;

        if (projectRecord.Kind == ProjectKinds.Fusion)
        {
            (primaryWt, baseBranch, forkedFusionRows) =
                ForkFusionWorktrees(project, source, slug, branch);
        }
        else
        {
            primaryWt = Path.Combine(_worktreeRoot, project.Id, slug);
            WorktreeService.ForkWorktree(projectRecord.Path, source.Worktree, primaryWt, branch);
            baseBranch = source.BaseBranch;
        }

        var preamble = BuildForkPreamble(keep);

        var now = Database.Now();
        var s = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = project.Id,
            Name = $"Fork of {source.Title}",
            BranchName = branch,
            WorktreePath = primaryWt,
            BaseBranch = baseBranch,
            Model = source.Model,
            Status = SessionStatus.Idle.ToString(),
            PermissionMode = source.PermissionMode,
            CreatedAt = now,
            LastActiveAt = now,
            PendingPreamble = preamble,
        };
        _db.InsertSession(s);

        if (forkedFusionRows is not null)
            foreach (var row in forkedFusionRows)
                _db.InsertSessionWorktree(row with { SessionId = s.Id });

        foreach (var row in keep)
        {
            _db.InsertMessage(new MessageRow
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = s.Id,
                Role = row.Role,
                Content = row.Content,
                ToolsJson = row.ToolsJson,
                CreatedAt = row.CreatedAt,
                Seq = row.Seq,
                ClaudeUuid = row.ClaudeUuid,
            });
        }

        var vm = BuildSessionVm(s);

        var idx = project.Sessions.IndexOf(source);
        if (idx < 0) project.Sessions.Insert(0, vm);
        else project.Sessions.Insert(idx + 1, vm);
        return vm;
    }

    // Build the system-prompt preamble for a fork-at-message session. Includes only message
    // text (tool calls + their outputs are summarised in aggregate) to keep the token cost
    // bounded, since the whole thing rides on every turn until the first one consumes it.
    private static string BuildForkPreamble(IReadOnlyList<MessageRow> messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(
            "You are continuing a forked Claude Code session. The user picked a branch point " +
            "in a prior conversation; the text exchange up to that point is reproduced below " +
            "(tool calls and their outputs have been omitted for brevity). Treat this as your " +
            "own prior reasoning and the user's prior input — do NOT summarise it back at the " +
            "user; just respond naturally to whatever they say next.");
        sb.AppendLine();
        sb.AppendLine("--- PRIOR TRANSCRIPT ---");
        foreach (var m in messages)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            sb.AppendLine();
            sb.AppendLine(m.Role.Equals("User", StringComparison.OrdinalIgnoreCase) ? "USER:" : "ASSISTANT:");
            sb.AppendLine(m.Content);
        }
        sb.AppendLine();
        sb.AppendLine("--- END PRIOR TRANSCRIPT ---");
        return sb.ToString();
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
            if (projectRecord.Kind == ProjectKinds.Fusion)
            {
                // Remove every member's worktree using its own repo path. Look up the rows
                // before DeleteSession cascades them away.
                foreach (var row in _db.GetSessionWorktrees(s.Id))
                {
                    var memberRec = _db.GetProject(row.MemberProjectId);
                    if (memberRec is null) continue;
                    try { WorktreeService.RemoveWorktree(memberRec.Path, row.WorktreePath, row.BranchName); }
                    catch { /* best-effort */ }
                }
            }
            else
            {
                try { WorktreeService.RemoveWorktree(projectRecord.Path, s.Worktree, s.Branch); }
                catch { /* best-effort */ }
            }
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
