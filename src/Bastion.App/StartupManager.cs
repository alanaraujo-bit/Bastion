using Microsoft.Win32;

namespace Bastion.App;

/// <summary>Manages the per-user "run Bastion console at login" entry.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Bastion";

    public static void SetRunAtLogin(bool enabled, bool startMinimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                var cmd = startMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
                key.SetValue(ValueName, cmd, RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* non-critical */ }
    }
}
