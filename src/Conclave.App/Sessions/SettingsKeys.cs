namespace Conclave.App.Sessions;

// Keys persisted in the `settings` table. Centralised so callers don't typo strings.
public static class SettingsKeys
{
    public const string AutoCleanupEnabled = "auto_cleanup.enabled";
    public const string AutoCleanupDays = "auto_cleanup.days";

    public const int DefaultAutoCleanupDays = 7;

    public static bool ReadAutoCleanupEnabled(Database db) =>
        string.Equals(db.GetSetting(AutoCleanupEnabled), "true", StringComparison.OrdinalIgnoreCase);

    public static int ReadAutoCleanupDays(Database db)
    {
        var raw = db.GetSetting(AutoCleanupDays);
        if (int.TryParse(raw, out var n) && n > 0) return n;
        return DefaultAutoCleanupDays;
    }
}
