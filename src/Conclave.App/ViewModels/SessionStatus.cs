namespace Conclave.App.ViewModels;

public enum SessionStatus
{
    Working,
    Waiting,
    Idle,
    RunningTool,
    Error,
    Queued,
    Completed,
}

public static class SessionStatusExtensions
{
    public static string Label(this SessionStatus s) => s switch
    {
        SessionStatus.Working => "Working",
        SessionStatus.Waiting => "Waiting for you",
        SessionStatus.Idle => "Idle",
        SessionStatus.RunningTool => "Running tool",
        SessionStatus.Error => "Error",
        SessionStatus.Queued => "Queued",
        SessionStatus.Completed => "Completed",
        _ => "",
    };

    public static bool Pulses(this SessionStatus s) =>
        s is SessionStatus.Working or SessionStatus.RunningTool;
}
