namespace Conclave.App.Sessions;

// Throwaway: exercise the database end-to-end with a temp file.
// Invoked via `dotnet run -- --smoke-db`. Will be removed once session management has real tests.
internal static class SmokeDb
{
    public static int Run()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"conclave-smoke-{Guid.NewGuid():N}.db");
        try
        {
            using var db = Database.Open(tmp);
            var now = Database.Now();

            var p = new Project(Guid.NewGuid().ToString("N"), "demo-repo",
                "/tmp/demo-repo", "main", now);
            db.InsertProject(p);
            Expect(db.GetProjects().Count == 1, "one project");
            Expect(db.GetProject(p.Id)?.Name == "demo-repo", "project round-trips");

            var s1 = SampleSession(p.Id, "brave otter", "conclave/brave-otter",
                "/tmp/wt/brave-otter", now);
            var s2 = SampleSession(p.Id, "quiet falcon", "conclave/quiet-falcon",
                "/tmp/wt/quiet-falcon", now + 1);
            db.InsertSession(s1);
            db.InsertSession(s2);

            var sessions = db.GetSessionsForProject(p.Id);
            Expect(sessions.Count == 2, "two sessions");
            Expect(sessions[0].Name == "brave otter", "session 1 name");

            db.UpdateSessionName(s1.Id, "actually-a-snake");
            Expect(db.GetSession(s1.Id)?.Name == "actually-a-snake", "rename works");
            Expect(db.GetSession(s1.Id)?.BranchName == "conclave/brave-otter",
                "rename does NOT touch branch_name");

            db.TouchSession(s1.Id);
            Expect(db.GetSession(s1.Id)?.LastActiveAt >= now, "touch updates last_active_at");

            db.DeleteSession(s1.Id);
            Expect(db.GetSessionsForProject(p.Id).Count == 1, "session deleted");

            // Cascade: deleting the project deletes remaining sessions.
            db.DeleteProject(p.Id);
            Expect(db.GetProjects().Count == 0, "project deleted");
            Expect(db.GetSessionsForProject(p.Id).Count == 0, "cascade deleted session");

            // Reopen to prove WAL/migrations are idempotent.
            db.Dispose();
            using var db2 = Database.Open(tmp);
            Expect(db2.GetProjects().Count == 0, "reopen works");

            Console.WriteLine("smoke-db: OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"smoke-db: FAIL — {ex}");
            return 1;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            try { File.Delete(tmp + "-wal"); } catch { }
            try { File.Delete(tmp + "-shm"); } catch { }
        }
    }

    private static Session SampleSession(string projectId, string name, string branch, string path, long ts) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            Name = name,
            BranchName = branch,
            WorktreePath = path,
            BaseBranch = "main",
            Model = "Sonnet 4.5",
            Status = "Idle",
            CreatedAt = ts,
            LastActiveAt = ts,
        };

    private static void Expect(bool cond, string what)
    {
        if (!cond) throw new Exception($"expectation failed: {what}");
    }
}
