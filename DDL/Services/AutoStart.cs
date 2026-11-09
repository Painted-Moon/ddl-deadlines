using Microsoft.Win32;

namespace DDL.Services;

/// "Start with Windows": a per-user Run entry that launches DDL quietly in the tray at sign-in.
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string ValueName => TaskStore.IsCustomLocation ? "DDL (test)" : "DDL";
    private static string ExePath => Environment.ProcessPath!;

    /// On only if the Run entry points at this exe and it isn't switched off in Settings > Apps > Startup.
    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string command ||
                !command.Contains(ExePath, StringComparison.OrdinalIgnoreCase))
                return false;

            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            // First byte odd (e.g. 03) = turned off in Windows Settings; even (02) or missing = on.
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    public static void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue(ValueName, $"\"{ExePath}\" --tray");

        // Clear an earlier "off" from Windows Settings so the new choice takes effect.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
