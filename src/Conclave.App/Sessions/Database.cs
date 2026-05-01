using Microsoft.Data.Sqlite;

namespace Conclave.App.Sessions;

// SQLite connection + migrations + manual CRUD. No Dapper because Dapper does runtime
// IL emit, which trips NativeAOT trimming. Mapping is by ordinal — every SELECT lists
// columns explicitly so the mapper indices stay stable even if a future migration adds
// a column in the middle.
//
// One connection per Database instance; mutations assumed to come from the UI thread.
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, """
            CREATE TABLE projects (
              id             TEXT PRIMARY KEY,
              name           TEXT NOT NULL,
              path           TEXT NOT NULL,
              default_branch TEXT NOT NULL,
              created_at     INTEGER NOT NULL
            );
            CREATE TABLE sessions (
              id             TEXT PRIMARY KEY,
              project_id     TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
              name           TEXT NOT NULL,
              branch_name    TEXT NOT NULL,
              worktree_path  TEXT NOT NULL,
              created_at     INTEGER NOT NULL,
              last_active_at INTEGER NOT NULL
            );
            CREATE INDEX ix_sessions_project_id ON sessions(project_id);
            CREATE TABLE meta (version INTEGER NOT NULL);
            INSERT INTO meta (version) VALUES (1);
            """),
        (2, """
            ALTER TABLE sessions ADD COLUMN base_branch TEXT NOT NULL DEFAULT 'main';
            ALTER TABLE sessions ADD COLUMN model TEXT NOT NULL DEFAULT 'Sonnet 4.5';
            ALTER TABLE sessions ADD COLUMN started_utc INTEGER;
            ALTER TABLE sessions ADD COLUMN status TEXT NOT NULL DEFAULT 'Idle';
            ALTER TABLE sessions ADD COLUMN unread_count INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE sessions ADD COLUMN pr_number INTEGER;
            ALTER TABLE sessions ADD COLUMN pr_state TEXT;
            ALTER TABLE sessions ADD COLUMN diff_files INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE sessions ADD COLUMN diff_add INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE sessions ADD COLUMN diff_del INTEGER NOT NULL DEFAULT 0;
            """),
        (3, """
            ALTER TABLE sessions ADD COLUMN claude_session_id TEXT;
            CREATE TABLE messages (
              id          TEXT PRIMARY KEY,
              session_id  TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
              role        TEXT NOT NULL,
              content     TEXT NOT NULL,
              tools_json  TEXT,
              created_at  INTEGER NOT NULL,
              seq         INTEGER NOT NULL
            );
            CREATE INDEX ix_messages_session_seq ON messages(session_id, seq);
            """),
        (4, """
            ALTER TABLE sessions ADD COLUMN plan_json TEXT;
            """),
        (5, """
            ALTER TABLE sessions ADD COLUMN permission_mode TEXT NOT NULL DEFAULT 'default';
            """),
        (6, """
            ALTER TABLE sessions ADD COLUMN total_cost_usd REAL NOT NULL DEFAULT 0;
            """),
        (7, """
            ALTER TABLE sessions ADD COLUMN pr_merged_at INTEGER;
            """),
        (8, """
            CREATE TABLE settings (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """),
        (9, """
            ALTER TABLE messages ADD COLUMN claude_uuid TEXT;
            ALTER TABLE sessions ADD COLUMN pending_preamble TEXT;
            """),
        (10, """
            ALTER TABLE projects ADD COLUMN kind TEXT NOT NULL DEFAULT 'repo';
            CREATE TABLE project_members (
              fusion_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
              member_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
              ordinal   INTEGER NOT NULL,
              PRIMARY KEY (fusion_id, member_id)
            );
            CREATE INDEX ix_project_members_fusion ON project_members(fusion_id, ordinal);
            CREATE TABLE session_worktrees (
              session_id        TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
              member_project_id TEXT NOT NULL,
              worktree_path     TEXT NOT NULL,
              branch_name       TEXT NOT NULL,
              base_branch       TEXT NOT NULL,
              ordinal           INTEGER NOT NULL,
              PRIMARY KEY (session_id, member_project_id)
            );
            CREATE INDEX ix_session_worktrees_session ON session_worktrees(session_id, ordinal);
            """),
        (11, """
            -- Snapshot of the member repo's path at session-creation time. Stored on the
            -- session row so DeleteSession can still find and remove the worktree on disk
            -- after the underlying member project is deleted (the FK on member_project_id
            -- is intentionally absent so a deleted project doesn't cascade-orphan the
            -- session_worktrees row before we get a chance to clean it up).
            ALTER TABLE session_worktrees ADD COLUMN repo_path TEXT NOT NULL DEFAULT '';
            UPDATE session_worktrees
            SET repo_path = (
              SELECT p.path FROM projects p WHERE p.id = session_worktrees.member_project_id
            )
            WHERE repo_path = '';
            """),
    };

    // Explicit column lists so ordinal mapping in Read*() stays stable.
    private const string ProjectColumns = "id, name, path, default_branch, created_at, kind";
    private const string SessionColumns =
        "id, project_id, name, branch_name, worktree_path, created_at, last_active_at, " +
        "base_branch, model, started_utc, status, unread_count, " +
        "pr_number, pr_state, diff_files, diff_add, diff_del, " +
        "claude_session_id, plan_json, permission_mode, total_cost_usd, pr_merged_at, " +
        "pending_preamble";
    private const string MessageColumns =
        "id, session_id, role, content, tools_json, created_at, seq, claude_uuid";

    private Database(SqliteConnection conn) => _conn = conn;

    public static Database Open(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        Exec(conn, "PRAGMA foreign_keys = ON;");
        Exec(conn, "PRAGMA journal_mode = WAL;");

        var db = new Database(conn);
        db.Migrate();
        return db;
    }

    public static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir;
        if (OperatingSystem.IsMacOS())
            dir = System.IO.Path.Combine(home, "Library", "Application Support", "Conclave");
        else if (OperatingSystem.IsWindows())
            dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Conclave");
        else
            dir = System.IO.Path.Combine(home, ".local", "share", "Conclave");
        return System.IO.Path.Combine(dir, "conclave.db");
    }

    // Worktrees live under ~/.Conclave on every platform — separate from the app-data
    // directory (which on Windows sits inside AppData\Roaming\Conclave) so per-file paths
    // stay clear of Windows' 260-char MAX_PATH limit. Every saved character matters: the
    // 32-char project id and slug already cost ~40, and git itself appends paths like
    // ".git/worktrees/<name>/index.lock" inside the linked worktree.
    public static string DefaultWorktreeRoot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, ".Conclave");
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Migrate()
    {
        int current = CurrentVersion();
        foreach (var (v, sql) in Migrations)
        {
            if (v <= current) continue;
            using var tx = _conn.BeginTransaction();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE meta SET version = $v;";
                cmd.Parameters.AddWithValue("$v", v);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    private int CurrentVersion()
    {
        using (var probe = _conn.CreateCommand())
        {
            probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='meta';";
            if ((long)probe.ExecuteScalar()! == 0) return 0;
        }
        using var read = _conn.CreateCommand();
        read.CommandText = "SELECT version FROM meta;";
        return Convert.ToInt32(read.ExecuteScalar()!);
    }

    // --- Helpers (allocate a SqliteCommand per call; SQLite is tiny so this is fine) ---

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? NullableLong(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static int? NullableInt(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    // --- Projects ---

    public IReadOnlyList<Project> GetProjects()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {ProjectColumns} FROM projects ORDER BY created_at ASC;";
        var list = new List<Project>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadProject(r));
        return list;
    }

    public Project? GetProject(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {ProjectColumns} FROM projects WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadProject(r) : null;
    }

    private static Project ReadProject(SqliteDataReader r) => new(
        Id: r.GetString(0),
        Name: r.GetString(1),
        Path: r.GetString(2),
        DefaultBranch: r.GetString(3),
        CreatedAt: r.GetInt64(4),
        Kind: r.GetString(5));

    public void InsertProject(Project p) => Exec(
        "INSERT INTO projects (id, name, path, default_branch, created_at, kind) " +
        "VALUES ($id, $name, $path, $defaultBranch, $createdAt, $kind);",
        ("$id", p.Id), ("$name", p.Name), ("$path", p.Path),
        ("$defaultBranch", p.DefaultBranch), ("$createdAt", p.CreatedAt),
        ("$kind", p.Kind));

    public void UpdateProjectName(string id, string name) => Exec(
        "UPDATE projects SET name = $name WHERE id = $id;",
        ("$id", id), ("$name", name));

    public void DeleteProject(string id) => Exec(
        "DELETE FROM projects WHERE id = $id;", ("$id", id));

    // --- Sessions ---

    public IReadOnlyList<Session> GetSessionsForProject(string projectId)
    {
        using var cmd = _conn.CreateCommand();
        // Most-recently-active first. Sessions only count as "active" when a turn finishes
        // (see SessionManager.UpdateStatus) — intermediate tool calls don't bump them.
        cmd.CommandText = $"SELECT {SessionColumns} FROM sessions WHERE project_id = $pid " +
            "ORDER BY last_active_at DESC, created_at DESC;";
        cmd.Parameters.AddWithValue("$pid", projectId);
        var list = new List<Session>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadSession(r));
        return list;
    }

    public Session? GetSession(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {SessionColumns} FROM sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadSession(r) : null;
    }

    private static Session ReadSession(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        ProjectId = r.GetString(1),
        Name = r.GetString(2),
        BranchName = r.GetString(3),
        WorktreePath = r.GetString(4),
        CreatedAt = r.GetInt64(5),
        LastActiveAt = r.GetInt64(6),
        BaseBranch = r.GetString(7),
        Model = r.GetString(8),
        StartedUtc = NullableLong(r, 9),
        Status = r.GetString(10),
        UnreadCount = r.GetInt32(11),
        PrNumber = NullableInt(r, 12),
        PrState = Str(r, 13),
        DiffFiles = r.GetInt32(14),
        DiffAdd = r.GetInt32(15),
        DiffDel = r.GetInt32(16),
        ClaudeSessionId = Str(r, 17),
        PlanJson = Str(r, 18),
        PermissionMode = r.GetString(19),
        TotalCostUsd = r.GetDouble(20),
        PrMergedAt = NullableLong(r, 21),
        PendingPreamble = Str(r, 22),
    };

    public void InsertSession(Session s) => Exec(
        """
        INSERT INTO sessions (
          id, project_id, name, branch_name, worktree_path,
          base_branch, model, started_utc, status, unread_count,
          pr_number, pr_state, pr_merged_at, diff_files, diff_add, diff_del,
          created_at, last_active_at,
          claude_session_id, plan_json, permission_mode, total_cost_usd,
          pending_preamble)
        VALUES (
          $id, $projectId, $name, $branchName, $worktreePath,
          $baseBranch, $model, $startedUtc, $status, $unreadCount,
          $prNumber, $prState, $prMergedAt, $diffFiles, $diffAdd, $diffDel,
          $createdAt, $lastActiveAt,
          $claudeSessionId, $planJson, $permissionMode, $totalCostUsd,
          $pendingPreamble);
        """,
        ("$id", s.Id), ("$projectId", s.ProjectId), ("$name", s.Name),
        ("$branchName", s.BranchName), ("$worktreePath", s.WorktreePath),
        ("$baseBranch", s.BaseBranch), ("$model", s.Model),
        ("$startedUtc", (object?)s.StartedUtc),
        ("$status", s.Status), ("$unreadCount", s.UnreadCount),
        ("$prNumber", (object?)s.PrNumber), ("$prState", (object?)s.PrState),
        ("$prMergedAt", (object?)s.PrMergedAt),
        ("$diffFiles", s.DiffFiles), ("$diffAdd", s.DiffAdd), ("$diffDel", s.DiffDel),
        ("$createdAt", s.CreatedAt), ("$lastActiveAt", s.LastActiveAt),
        ("$claudeSessionId", (object?)s.ClaudeSessionId),
        ("$planJson", (object?)s.PlanJson),
        ("$permissionMode", s.PermissionMode),
        ("$totalCostUsd", s.TotalCostUsd),
        ("$pendingPreamble", (object?)s.PendingPreamble));

    // Status-only update. last_active_at is bumped separately via TouchSession when a turn
    // ends, so intermediate transitions (Working/RunningTool) don't reorder the sidebar.
    public void UpdateSessionStatus(string id, string status) => Exec(
        "UPDATE sessions SET status = $status WHERE id = $id;",
        ("$id", id), ("$status", status));

    public void UpdateSessionDiff(string id, int files, int add, int del) => Exec(
        "UPDATE sessions SET diff_files = $files, diff_add = $add, diff_del = $del WHERE id = $id;",
        ("$id", id), ("$files", files), ("$add", add), ("$del", del));

    public void UpdateSessionPr(string id, int? prNumber, string? prState, long? prMergedAt) => Exec(
        "UPDATE sessions SET pr_number = $prNumber, pr_state = $prState, pr_merged_at = $prMergedAt WHERE id = $id;",
        ("$id", id), ("$prNumber", (object?)prNumber), ("$prState", (object?)prState),
        ("$prMergedAt", (object?)prMergedAt));

    public void UpdateClaudeSessionId(string id, string? claudeSessionId) => Exec(
        "UPDATE sessions SET claude_session_id = $cid WHERE id = $id;",
        ("$id", id), ("$cid", (object?)claudeSessionId));

    public void UpdateSessionPlan(string id, string? planJson) => Exec(
        "UPDATE sessions SET plan_json = $planJson WHERE id = $id;",
        ("$id", id), ("$planJson", (object?)planJson));

    public void UpdateSessionPermissionMode(string id, string mode) => Exec(
        "UPDATE sessions SET permission_mode = $mode WHERE id = $id;",
        ("$id", id), ("$mode", mode));

    public void AddSessionCost(string id, double delta) => Exec(
        "UPDATE sessions SET total_cost_usd = total_cost_usd + $delta WHERE id = $id;",
        ("$id", id), ("$delta", delta));

    public void UpdateSessionPendingPreamble(string id, string? preamble) => Exec(
        "UPDATE sessions SET pending_preamble = $p WHERE id = $id;",
        ("$id", id), ("$p", (object?)preamble));

    public void UpdateSessionName(string id, string name) => Exec(
        "UPDATE sessions SET name = $name WHERE id = $id;",
        ("$id", id), ("$name", name));

    public void TouchSession(string id) => Exec(
        "UPDATE sessions SET last_active_at = $ts WHERE id = $id;",
        ("$id", id), ("$ts", Now()));

    public void DeleteSession(string id) => Exec(
        "DELETE FROM sessions WHERE id = $id;", ("$id", id));

    // --- Messages (transcript) ---

    public IReadOnlyList<MessageRow> GetMessages(string sessionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {MessageColumns} FROM messages WHERE session_id = $sid ORDER BY seq ASC;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        var list = new List<MessageRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadMessage(r));
        return list;
    }

    private static MessageRow ReadMessage(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        SessionId = r.GetString(1),
        Role = r.GetString(2),
        Content = r.GetString(3),
        ToolsJson = Str(r, 4),
        CreatedAt = r.GetInt64(5),
        Seq = r.GetInt32(6),
        ClaudeUuid = Str(r, 7),
    };

    public int NextSeq(string sessionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(seq) FROM messages WHERE session_id = $sid;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        var raw = cmd.ExecuteScalar();
        if (raw is null || raw is DBNull) return 0;
        return Convert.ToInt32(raw) + 1;
    }

    public void InsertMessage(MessageRow m) => Exec(
        "INSERT INTO messages (id, session_id, role, content, tools_json, created_at, seq, claude_uuid) " +
        "VALUES ($id, $sessionId, $role, $content, $toolsJson, $createdAt, $seq, $claudeUuid);",
        ("$id", m.Id), ("$sessionId", m.SessionId), ("$role", m.Role),
        ("$content", m.Content), ("$toolsJson", (object?)m.ToolsJson),
        ("$createdAt", m.CreatedAt), ("$seq", m.Seq),
        ("$claudeUuid", (object?)m.ClaudeUuid));

    public void UpdateMessageClaudeUuid(string id, string? uuid) => Exec(
        "UPDATE messages SET claude_uuid = $u WHERE id = $id;",
        ("$id", id), ("$u", (object?)uuid));

    public void UpdateMessage(string id, string content, string? toolsJson) => Exec(
        "UPDATE messages SET content = $content, tools_json = $toolsJson WHERE id = $id;",
        ("$id", id), ("$content", content), ("$toolsJson", (object?)toolsJson));

    // --- Settings (key/value) ---

    public string? GetSetting(string key)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var raw = cmd.ExecuteScalar();
        return raw is null || raw is DBNull ? null : (string)raw;
    }

    public void SetSetting(string key, string value) => Exec(
        "INSERT INTO settings (key, value) VALUES ($key, $value) " +
        "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
        ("$key", key), ("$value", value));

    // --- Project members (fusion projects) ---

    // Returns members in ordinal order. Ordinal 0 is the primary; the rest are secondaries.
    public IReadOnlyList<ProjectMember> GetMembers(string fusionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT fusion_id, member_id, ordinal FROM project_members " +
            "WHERE fusion_id = $fid ORDER BY ordinal ASC;";
        cmd.Parameters.AddWithValue("$fid", fusionId);
        var list = new List<ProjectMember>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProjectMember(r.GetString(0), r.GetString(1), r.GetInt32(2)));
        return list;
    }

    public void InsertMember(ProjectMember m) => Exec(
        "INSERT INTO project_members (fusion_id, member_id, ordinal) " +
        "VALUES ($fid, $mid, $ord);",
        ("$fid", m.FusionId), ("$mid", m.MemberId), ("$ord", m.Ordinal));

    // --- Session worktrees (one row per member repo for a fusion session) ---

    public IReadOnlyList<SessionWorktree> GetSessionWorktrees(string sessionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT session_id, member_project_id, repo_path, worktree_path, branch_name, base_branch, ordinal " +
            "FROM session_worktrees WHERE session_id = $sid ORDER BY ordinal ASC;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        var list = new List<SessionWorktree>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SessionWorktree(
                r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6)));
        return list;
    }

    public void InsertSessionWorktree(SessionWorktree w) => Exec(
        "INSERT INTO session_worktrees " +
        "(session_id, member_project_id, repo_path, worktree_path, branch_name, base_branch, ordinal) " +
        "VALUES ($sid, $mid, $repo, $wt, $br, $base, $ord);",
        ("$sid", w.SessionId), ("$mid", w.MemberProjectId),
        ("$repo", w.RepoPath),
        ("$wt", w.WorktreePath), ("$br", w.BranchName),
        ("$base", w.BaseBranch), ("$ord", w.Ordinal));

    public void Dispose() => _conn.Dispose();
}
