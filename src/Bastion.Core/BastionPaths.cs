namespace Bastion.Core;

/// <summary>
/// Canonical filesystem locations used across every Bastion component.
/// The machine data folder lives under ProgramData so a single, shared,
/// ACL-protected copy of the configuration is visible to the SYSTEM service
/// and readable (but not writable) by standard user sessions.
/// </summary>
public static class BastionPaths
{
    public const string ProductName = "Bastion";

    /// <summary>C:\ProgramData\Bastion — shared, machine-wide, ACL protected.</summary>
    public static string MachineDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProductName);

    public static string ConfigFile => Path.Combine(MachineDataDir, "config.json");

    public static string ConfigBackupFile => Path.Combine(MachineDataDir, "config.bak.json");

    public static string ActivityLogFile => Path.Combine(MachineDataDir, "activity.jsonl");

    public static string ServiceLogFile => Path.Combine(MachineDataDir, "service.log");

    /// <summary>Icon cache for extracted application icons.</summary>
    public static string IconCacheDir => Path.Combine(MachineDataDir, "icons");

    /// <summary>Install root, resolved from the running assembly location.</summary>
    public static string InstallDir =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(MachineDataDir);
        Directory.CreateDirectory(IconCacheDir);
    }
}
