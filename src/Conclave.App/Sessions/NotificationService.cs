using System.Diagnostics;

namespace Conclave.App.Sessions;

// Posts native OS notifications when claude finishes a turn or asks the user a question.
// Cross-platform via the system CLI on each OS — keeps NativeAOT clean (no extra packages,
// no reflection). Best-effort: a missing helper binary is silently ignored.
public sealed class NotificationService
{
    public bool Enabled { get; set; } = true;

    // Supplied by MainWindow so the service stays UI-framework-agnostic. When the window is
    // active we suppress notifications — the user can already see whatever changed in-app.
    public Func<bool>? IsWindowActive { get; set; }

    public void NotifyTurnComplete(string sessionTitle, bool error)
    {
        if (!ShouldNotify()) return;
        var title = error ? "Claude hit an error" : "Claude is done";
        Post(title, sessionTitle);
    }

    public void NotifyQuestionPending(string sessionTitle, string question)
    {
        if (!ShouldNotify()) return;
        var body = string.IsNullOrWhiteSpace(question) ? sessionTitle : $"{sessionTitle} — {question}";
        Post("Claude is asking a question", body);
    }

    private bool ShouldNotify()
    {
        if (!Enabled) return false;
        return IsWindowActive is null || !IsWindowActive();
    }

    private static void Post(string title, string message)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) PostMac(title, message);
            else if (OperatingSystem.IsLinux()) PostLinux(title, message);
            else if (OperatingSystem.IsWindows()) PostWindows(title, message);
        }
        catch { /* best-effort */ }
    }

    private static void PostMac(string title, string message)
    {
        var script = $"display notification \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\"";
        StartDetached("osascript", new[] { "-e", script });
    }

    private static void PostLinux(string title, string message)
    {
        StartDetached("notify-send", new[] { title, message });
    }

    private static void PostWindows(string title, string message)
    {
        // PowerShell + WinRT toast — works on Win10+ without an extra dependency. The
        // AppId we show under is "Conclave"; Windows will fall back to PowerShell's icon
        // until the shortcut is registered, which is acceptable for a lightweight toast.
        var xml = "<toast><visual><binding template=\"ToastGeneric\">"
            + $"<text>{EscapeXml(title)}</text><text>{EscapeXml(message)}</text>"
            + "</binding></visual></toast>";
        var ps = "$ErrorActionPreference='SilentlyContinue';"
            + "[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime];"
            + "$x=[Windows.Data.Xml.Dom.XmlDocument]::new();"
            + $"$x.LoadXml('{xml.Replace("'", "''")}');"
            + "$t=[Windows.UI.Notifications.ToastNotification]::new($x);"
            + "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Conclave').Show($t);";
        StartDetached("powershell.exe", new[] { "-NoProfile", "-WindowStyle", "Hidden", "-Command", ps });
    }

    private static void StartDetached(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try { Process.Start(psi); }
        catch { /* helper missing — best-effort */ }
    }

    private static string EscapeAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&apos;");
}
