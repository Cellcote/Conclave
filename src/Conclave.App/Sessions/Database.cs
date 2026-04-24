using Dapper;
using Microsoft.Data.Sqlite;

namespace Conclave.App.Sessions;

// SQLite connection + migrations + Dapper-based CRUD.
// One connection per Database instance; assumed to be used from the UI thread.
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
    };

    static Database()
    {
        // Map project_id → ProjectId, etc.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private Database(SqliteConnection conn) => _conn = conn;

    public static Database Open(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        conn.Execute("PRAGMA foreign_keys = ON;");
        conn.Execute("PRAGMA journal_mode = WAL;");

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

    private void Migrate()
    {
        int current = CurrentVersion();
        foreach (var (v, sql) in Migrations)
        {
            if (v <= current) continue;
            using var tx = _conn.BeginTransaction();
            _conn.Execute(sql, transaction: tx);
            _conn.Execute("UPDATE meta SET version = @v;", new { v }, tx);
            tx.Commit();
        }
    }

    private int CurrentVersion()
    {
        var hasMeta = _conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='meta';") > 0;
        if (!hasMeta) return 0;
        return _conn.ExecuteScalar<int>("SELECT version FROM meta;");
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // --- Projects ---

    public IReadOnlyList<Project> GetProjects() =>
        _conn.Query<Project>("SELECT * FROM projects ORDER BY created_at ASC;").AsList();

    public Project? GetProject(string id) =>
        _conn.QuerySingleOrDefault<Project>(
            "SELECT * FROM projects WHERE id = @id;", new { id });

    public void InsertProject(Project p) =>
        _conn.Execute("""
            INSERT INTO projects (id, name, path, default_branch, created_at)
            VALUES (@Id, @Name, @Path, @DefaultBranch, @CreatedAt);
            """, p);

    public void UpdateProjectName(string id, string name) =>
        _conn.Execute("UPDATE projects SET name = @name WHERE id = @id;", new { id, name });

    public void DeleteProject(string id) =>
        _conn.Execute("DELETE FROM projects WHERE id = @id;", new { id });

    // --- Sessions ---

    public IReadOnlyList<Session> GetSessionsForProject(string projectId) =>
        _conn.Query<Session>(
            "SELECT * FROM sessions WHERE project_id = @projectId ORDER BY created_at ASC;",
            new { projectId }).AsList();

    public Session? GetSession(string id) =>
        _conn.QuerySingleOrDefault<Session>(
            "SELECT * FROM sessions WHERE id = @id;", new { id });

    public void InsertSession(Session s) =>
        _conn.Execute("""
            INSERT INTO sessions (
              id, project_id, name, branch_name, worktree_path,
              base_branch, model, started_utc, status, unread_count,
              pr_number, pr_state, diff_files, diff_add, diff_del,
              created_at, last_active_at)
            VALUES (
              @Id, @ProjectId, @Name, @BranchName, @WorktreePath,
              @BaseBranch, @Model, @StartedUtc, @Status, @UnreadCount,
              @PrNumber, @PrState, @DiffFiles, @DiffAdd, @DiffDel,
              @CreatedAt, @LastActiveAt);
            """, s);

    public void UpdateSessionStatus(string id, string status) =>
        _conn.Execute("UPDATE sessions SET status = @status, last_active_at = @ts WHERE id = @id;",
            new { id, status, ts = Now() });

    public void UpdateSessionDiff(string id, int files, int add, int del) =>
        _conn.Execute("UPDATE sessions SET diff_files = @files, diff_add = @add, diff_del = @del WHERE id = @id;",
            new { id, files, add, del });

    public void UpdateSessionPr(string id, int? prNumber, string? prState) =>
        _conn.Execute("UPDATE sessions SET pr_number = @prNumber, pr_state = @prState WHERE id = @id;",
            new { id, prNumber, prState });

    public void UpdateSessionUnread(string id, int unread) =>
        _conn.Execute("UPDATE sessions SET unread_count = @unread WHERE id = @id;",
            new { id, unread });

    public void UpdateClaudeSessionId(string id, string? claudeSessionId) =>
        _conn.Execute("UPDATE sessions SET claude_session_id = @cid WHERE id = @id;",
            new { id, cid = claudeSessionId });

    public void UpdateSessionPlan(string id, string? planJson) =>
        _conn.Execute("UPDATE sessions SET plan_json = @planJson WHERE id = @id;",
            new { id, planJson });

    public void UpdateSessionPermissionMode(string id, string mode) =>
        _conn.Execute("UPDATE sessions SET permission_mode = @mode WHERE id = @id;",
            new { id, mode });

    // --- Messages (transcript) ---

    public IReadOnlyList<MessageRow> GetMessages(string sessionId) =>
        _conn.Query<MessageRow>(
            "SELECT * FROM messages WHERE session_id = @sessionId ORDER BY seq ASC;",
            new { sessionId }).AsList();

    public int NextSeq(string sessionId)
    {
        var max = _conn.ExecuteScalar<long?>(
            "SELECT MAX(seq) FROM messages WHERE session_id = @sessionId;",
            new { sessionId });
        return max is null ? 0 : (int)max.Value + 1;
    }

    public void InsertMessage(MessageRow m) =>
        _conn.Execute("""
            INSERT INTO messages (id, session_id, role, content, tools_json, created_at, seq)
            VALUES (@Id, @SessionId, @Role, @Content, @ToolsJson, @CreatedAt, @Seq);
            """, m);

    public void UpdateMessage(string id, string content, string? toolsJson) =>
        _conn.Execute(
            "UPDATE messages SET content = @content, tools_json = @toolsJson WHERE id = @id;",
            new { id, content, toolsJson });

    public void UpdateSessionName(string id, string name) =>
        _conn.Execute("UPDATE sessions SET name = @name WHERE id = @id;", new { id, name });

    public void TouchSession(string id) =>
        _conn.Execute(
            "UPDATE sessions SET last_active_at = @ts WHERE id = @id;",
            new { id, ts = Now() });

    public void DeleteSession(string id) =>
        _conn.Execute("DELETE FROM sessions WHERE id = @id;", new { id });

    public void Dispose() => _conn.Dispose();
}
