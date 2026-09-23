namespace Bastion.Core.Models;

/// <summary>
/// A single application placed under protection. Interception is keyed on
/// <see cref="ImageName"/> (the bare executable file name, e.g. "msedge.exe")
/// because that is the granularity Windows' Image File Execution Options
/// feature operates on. <see cref="TargetPath"/> is the last known full path,
/// used for display, icon extraction, and "open file location".
/// </summary>
public sealed class ProtectedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Bare executable name including extension, lower-cased. The IFEO match key.</summary>
    public string ImageName { get; set; } = "";

    /// <summary>Friendly display name (e.g. "Microsoft Edge").</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Last known full path to the executable, for display and launching.</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>Cached extracted icon file (PNG) under the icon cache, if available.</summary>
    public string? IconPath { get; set; }

    /// <summary>Whether protection is currently armed for this app.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When true, use <see cref="Policy"/>; otherwise follow the global default policy.</summary>
    public bool OverridePolicy { get; set; }

    public UnlockPolicy Policy { get; set; } = UnlockPolicy.SecureDefault();

    /// <summary>Allow Windows Hello (PIN / fingerprint / face) as an unlock method for this app.</summary>
    public bool AllowWindowsHello { get; set; } = true;

    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool Matches(string imageName) =>
        string.Equals(ImageName, imageName, StringComparison.OrdinalIgnoreCase);
}
