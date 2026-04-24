namespace Conclave.App.ViewModels;

// Narrow subset of the CLI's --permission-mode choices that we surface in the UI.
// Values match the CLI flag exactly so `mode.ToString()` is the flag value.
public static class PermissionModes
{
    public const string Default = "default";                 // prompt the user for each tool
    public const string AcceptEdits = "acceptEdits";         // auto-accept file edits, prompt for bash
    public const string BypassPermissions = "bypassPermissions"; // auto-accept everything

    public static string DisplayName(string mode) => mode switch
    {
        AcceptEdits => "Auto-accept edits",
        BypassPermissions => "Full access",
        _ => "Prompt",
    };
}
