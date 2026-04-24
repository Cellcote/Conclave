using Avalonia.Media;
using Conclave.App.Design;

namespace Conclave.App.ViewModels;

public enum LogLevel { Dbg, Inf, Wrn, Err }

// One line on the Logs tab. Immutable after construction.
public sealed class LogLineVm : Views.Observable
{
    public Tokens Tokens { get; init; } = null!;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public LogLevel Level { get; init; } = LogLevel.Inf;
    public string Message { get; init; } = "";

    public string TimestampText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");

    public string LevelText => Level switch
    {
        LogLevel.Dbg => "DBG",
        LogLevel.Inf => "INF",
        LogLevel.Wrn => "WRN",
        LogLevel.Err => "ERR",
        _ => "?",
    };

    public IBrush LevelBrush => Level switch
    {
        LogLevel.Dbg => Tokens.TextDim,
        LogLevel.Inf => Tokens.Info,
        LogLevel.Wrn => Tokens.Warn,
        LogLevel.Err => Tokens.Err,
        _ => Tokens.TextMute,
    };
}
