using System.Runtime.InteropServices;
using System.Threading.Channels;
using Porta.Pty;

namespace Conclave.App.Terminal;

// Wraps Porta.Pty. Spawns claude if on PATH, else the user's default shell.
// Read loop runs on a background Task and pushes chunks onto a channel.
// Write and Resize are thread-safe enough to call from the UI thread.
public sealed class PtySession : IAsyncDisposable
{
    private readonly IPtyConnection _pty;
    private readonly Channel<byte[]> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readTask;

    public ChannelReader<byte[]> Output => _channel.Reader;

    private PtySession(IPtyConnection pty, Channel<byte[]> channel)
    {
        _pty = pty;
        _channel = channel;
        _pty.ProcessExited += (_, _) => _channel.Writer.TryComplete();
        _readTask = Task.Run(ReadLoop);
    }

    // Bounded so a runaway producer (`yes`, a misbehaving TUI) can't allocate without
    // limit while the UI is busy. 256 chunks × 8KB read buf = up to ~2MB queued. When
    // the channel is full, ReadLoop's WriteAsync blocks; the PTY pipe fills; the child
    // eventually blocks on its own write — natural backpressure.
    private const int ChannelCapacity = 256;

    public static async Task<PtySession> SpawnAsync(int cols, int rows, string? workingDirectory = null)
    {
        string? app = ResolveCommand();
        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cols = cols,
            Rows = rows,
            Cwd = workingDirectory ?? Environment.CurrentDirectory,
            App = app ?? throw new InvalidOperationException("No command available to spawn."),
            Environment = BuildEnvironment(),
        };
        var pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        return new PtySession(pty, channel);
    }

    private static string? ResolveCommand()
    {
        // Prefer claude if on PATH; otherwise fall back to the user's shell.
        var claude = WhichOnPath("claude");
        if (claude != null) return claude;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WhichOnPath("pwsh.exe")
                ?? WhichOnPath("pwsh")
                ?? Environment.GetEnvironmentVariable("ComSpec")
                ?? "cmd.exe";
        }
        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
    }

    private static string? WhichOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        char sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    private static Dictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            if (e.Key is string k && e.Value is string v) env[k] = v;
        }
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        return env;
    }

    private async Task ReadLoop()
    {
        var buf = new byte[8192];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _pty.ReaderStream.ReadAsync(buf.AsMemory(), _cts.Token);
                if (n <= 0) break;
                var chunk = new byte[n];
                Buffer.BlockCopy(buf, 0, chunk, 0, n);
                await _channel.Writer.WriteAsync(chunk, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        _pty.WriterStream.Write(bytes);
        _pty.WriterStream.Flush();
    }

    public void Resize(int cols, int rows)
    {
        try { _pty.Resize(cols, rows); } catch { /* best-effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _pty.Kill(); } catch { }
        try { await _readTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        _cts.Dispose();
    }
}
