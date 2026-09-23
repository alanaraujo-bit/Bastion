namespace Bastion.Core;

/// <summary>
/// Rules for which executables may be placed under protection. Shared by the
/// UI (to guide the user) and the service (to enforce). Critical OS processes
/// are refused because hooking them would destabilize logon or the shell.
/// </summary>
public static class ProtectableRules
{
    public static readonly IReadOnlySet<string> Denylist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe",
        "lsass.exe", "smss.exe", "svchost.exe", "dwm.exe", "fontdrvhost.exe",
        "sihost.exe", "taskhostw.exe", "ctfmon.exe", "logonui.exe", "userinit.exe",
        "system", "systemsettings.exe", "runtimebroker.exe", "searchhost.exe",
        "startmenuexperiencehost.exe", "shellexperiencehost.exe", "conhost.exe",
        "bastion.service.exe", "bastion.gatekeeper.exe", "bastion.app.exe",
    };

    public static bool IsProtectable(string imageName) =>
        !string.IsNullOrWhiteSpace(imageName) &&
        imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        !Denylist.Contains(imageName);

    public static string? DenylistReason(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return "No executable name.";
        if (!imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Only .exe applications can be protected.";
        if (Denylist.Contains(imageName)) return "This is a critical Windows component and can't be protected safely.";
        return null;
    }

    /// <summary>
    /// Windows 11 ships these as Store (MSIX) apps whose real image lives under
    /// WindowsApps and launches via an execution alias, so an IFEO hook on the
    /// System32 stub name does not intercept them. We warn rather than block,
    /// because the same names can be genuine classic apps on some systems.
    /// </summary>
    private static readonly IReadOnlySet<string> StoreRedirected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "notepad.exe", "mspaint.exe", "calc.exe", "calculator.exe",
        "wordpad.exe", "snippingtool.exe", "snip.exe",
    };

    /// <summary>True when the target is likely a Store/MSIX app that won't intercept reliably.</summary>
    public static bool IsLikelyStoreRedirected(string imageName, string? targetPath)
    {
        if (!string.IsNullOrEmpty(targetPath) &&
            targetPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return true;
        return StoreRedirected.Contains(imageName);
    }
}
