using Microsoft.Win32;

namespace Bastion.Service.Ifeo;

/// <summary>
/// Manages the Image File Execution Options (IFEO) "Debugger" hooks that
/// implement interception. When a Debugger value is present for an image name,
/// the Windows loader launches "&lt;debugger&gt; &lt;original command line&gt;"
/// instead of the target — for every launch vector (shortcut, Start menu,
/// taskbar, file association, protocol handler, direct exec, child process).
///
/// Only the SYSTEM service touches these keys; they live under HKLM and are
/// therefore write-protected from standard users. We set the value under both
/// the native and WOW6432 views to cover 32- and 64-bit targets.
/// </summary>
public sealed class IfeoManager
{
    private const string IfeoSubKey =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    private const string DebuggerValue = "Debugger";

    private readonly RegistryView[] _views = { RegistryView.Registry64, RegistryView.Registry32 };

    // Protection eligibility lives in Bastion.Core.ProtectableRules so the UI and
    // the service share one source of truth.
    public static bool IsProtectable(string imageName) => Core.ProtectableRules.IsProtectable(imageName);

    public static string? DenylistReason(string imageName) => Core.ProtectableRules.DenylistReason(imageName);

    public void Register(string imageName, string gatekeeperPath)
    {
        if (!IsProtectable(imageName))
            throw new InvalidOperationException(DenylistReason(imageName) ?? "Not protectable.");

        var debugger = "\"" + gatekeeperPath + "\"";
        foreach (var view in _views)
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var ifeo = baseKey.CreateSubKey(IfeoSubKey, writable: true);
            using var img = ifeo.CreateSubKey(imageName, writable: true);
            img.SetValue(DebuggerValue, debugger, RegistryValueKind.String);
        }
    }

    public void Unregister(string imageName)
    {
        foreach (var view in _views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var ifeo = baseKey.OpenSubKey(IfeoSubKey, writable: true);
                using var img = ifeo?.OpenSubKey(imageName, writable: true);
                if (img is null) continue;
                if (img.GetValue(DebuggerValue) is not null)
                    img.DeleteValue(DebuggerValue, throwOnMissingValue: false);

                // If we own an otherwise-empty subkey, remove it to stay tidy.
                if (img.ValueCount == 0 && img.SubKeyCount == 0)
                {
                    img.Dispose();
                    ifeo!.DeleteSubKey(imageName, throwOnMissingSubKey: false);
                }
            }
            catch { /* best effort per view */ }
        }
    }

    public bool IsRegistered(string imageName) => GetDebugger(imageName) is not null;

    /// <summary>
    /// Removes every IFEO Debugger hook that points at our gatekeeper, across
    /// both registry views. Used at uninstall so protected apps become launchable
    /// again once Bastion is gone (otherwise the OS would keep redirecting them to
    /// a gatekeeper that no longer exists).
    /// </summary>
    public int RemoveAllBastionHooks()
    {
        int removed = 0;
        foreach (var view in _views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var ifeo = baseKey.OpenSubKey(IfeoSubKey, writable: true);
                if (ifeo is null) continue;
                foreach (var imageName in ifeo.GetSubKeyNames())
                {
                    try
                    {
                        using var img = ifeo.OpenSubKey(imageName, writable: true);
                        if (img?.GetValue(DebuggerValue) is string s &&
                            s.Contains("Bastion.Gatekeeper", StringComparison.OrdinalIgnoreCase))
                        {
                            img.DeleteValue(DebuggerValue, throwOnMissingValue: false);
                            removed++;
                            if (img.ValueCount == 0 && img.SubKeyCount == 0)
                            {
                                img.Dispose();
                                ifeo.DeleteSubKey(imageName, throwOnMissingSubKey: false);
                            }
                        }
                    }
                    catch { /* skip this image */ }
                }
            }
            catch { /* skip this view */ }
        }
        return removed;
    }

    public string? GetDebugger(string imageName)
    {
        foreach (var view in _views)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var img = baseKey.OpenSubKey($@"{IfeoSubKey}\{imageName}", writable: false);
                if (img?.GetValue(DebuggerValue) is string s && !string.IsNullOrEmpty(s))
                    return s;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Momentarily clears the Debugger hook, runs <paramref name="action"/>
    /// (the authorized pass-through launch), then restores the hook. This is the
    /// only window in which a protected image can start without interception,
    /// and it exists solely during a launch the user just authorized. Callers
    /// serialize this so concurrent launches cannot widen the window.
    /// </summary>
    public void WithHookSuspended(string imageName, string gatekeeperPath, Action action)
    {
        var hadHook = IsRegistered(imageName);
        try
        {
            if (hadHook) Unregister(imageName);
            action();
        }
        finally
        {
            if (hadHook) Register(imageName, gatekeeperPath);
        }
    }
}
