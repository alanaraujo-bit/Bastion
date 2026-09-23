using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Bastion.Ui.Services;

public sealed record AppCandidate(string DisplayName, string TargetPath, string ImageName)
{
    public string ImageNameLower => ImageName.ToLowerInvariant();
}

/// <summary>
/// Discovers user-facing installed applications by resolving Start Menu
/// shortcuts, then augments with currently running windowed processes. Runs in
/// the user context; needs no elevation.
/// </summary>
public static class InstalledAppScanner
{
    public static async Task<IReadOnlyList<AppCandidate>> ScanAsync()
        => await Task.Run(() => Scan());

    public static IReadOnlyList<AppCandidate> Scan()
    {
        var byPath = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in StartMenuDirs())
            ScanShortcuts(dir, byPath);

        // Augment with running, windowed processes the user can see.
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    var path = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    if (byPath.ContainsKey(path)) continue;
                    var name = FriendlyName(path);
                    byPath[path] = new AppCandidate(name, path, Path.GetFileName(path));
                }
                catch { /* access denied on protected processes — skip */ }
            }
        }
        catch { }

        return byPath.Values
            .Where(c => !IsNoise(c))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> StartMenuDirs()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    }

    private static void ScanShortcuts(string dir, Dictionary<string, AppCandidate> byPath)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        IEnumerable<string> links;
        try { links = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
        catch { return; }

        foreach (var lnk in links)
        {
            try
            {
                var target = ResolveShortcut(lnk);
                if (string.IsNullOrEmpty(target)) continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(target)) continue;
                if (byPath.ContainsKey(target)) continue;
                var name = Path.GetFileNameWithoutExtension(lnk);
                byPath[target] = new AppCandidate(name, target, Path.GetFileName(target));
            }
            catch { /* skip unreadable shortcut */ }
        }
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        // Use Windows Script Host COM to read the shortcut target without extra deps.
        object? shell = null;
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return null;
            shell = Activator.CreateInstance(t);
            dynamic sh = shell!;
            dynamic sc = sh.CreateShortcut(lnkPath);
            string target = sc.TargetPath;
            return target;
        }
        catch { return null; }
        finally { if (shell is not null) Marshal.ReleaseComObject(shell); }
    }

    private static string FriendlyName(string path)
    {
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(fvi.FileDescription)) return fvi.FileDescription!.Trim();
            if (!string.IsNullOrWhiteSpace(fvi.ProductName)) return fvi.ProductName!.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "unins000.exe", "uninstall.exe", "setup.exe", "vcredist.exe", "install.exe",
    };

    private static bool IsNoise(AppCandidate c)
        => Noise.Contains(c.ImageName) ||
           (c.TargetPath.Contains(@"\Windows\System32", StringComparison.OrdinalIgnoreCase) &&
            !c.ImageName.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase));
}
