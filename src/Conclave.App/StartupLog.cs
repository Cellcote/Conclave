using System.Diagnostics;

namespace Conclave.App;

// Lightweight startup tracer. Writes timestamped phase marks to <dataDir>/startup.log
// (truncated on every launch, so users can attach the most recent file when reporting
// "app takes forever to open"). Also mirrors to Trace so the existing Avalonia
// LogToTrace pipe surfaces it during development.
//
// All marks are relative to process start (Stopwatch.StartNew at static init), and the
// time-since-previous-mark is included so a slow phase jumps out at a glance.
public static class StartupLog
{
    private static readonly Stopwatch Sw = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static string? _path;
    private static long _lastElapsedMs;

    // Initialise the log file. Safe to call more than once; first call wins. Truncates
    // any existing file so each launch produces a clean trace.
    public static void Init(string path)
    {
        lock (Gate)
        {
            if (_path is not null) return;
            _path = path;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"=== Conclave startup {DateTime.Now:O} ===\n");
            }
            catch
            {
                // Logging is best-effort: a locked file or read-only data dir must never
                // crash startup. Drop the path so further Mark calls skip the file write.
                _path = null;
            }
        }
        Mark("startup log initialised");
    }

    public static void Mark(string label)
    {
        // Snapshot inside the lock so concurrent Mark calls (UI thread + background
        // probe thread) can't reorder past each other and produce negative deltas.
        long now, delta;
        lock (Gate)
        {
            now = Sw.ElapsedMilliseconds;
            delta = now - _lastElapsedMs;
            _lastElapsedMs = now;
        }
        var line = $"[{now,6} ms] (+{delta,5} ms) {label}";
        Trace.WriteLine("[startup] " + line);
        var path = _path;
        if (path is null) return;
        try
        {
            // Append-only; writes are infrequent and small, so File.AppendAllText is fine
            // and keeps us crash-safe (each phase is on disk before the next runs).
            File.AppendAllText(path, line + "\n");
        }
        catch
        {
            // best-effort
        }
    }
}
