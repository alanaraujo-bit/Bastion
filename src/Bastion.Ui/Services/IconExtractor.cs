using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bastion.Ui.Services;

/// <summary>Extracts a crisp application icon straight from an executable.</summary>
public static class IconExtractor
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(
        string lpszFile, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, int[] piconid, int nIcons, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns an ImageSource for the exe's icon at the requested size, or null
    /// if none can be extracted (caller should show a fallback glyph).
    /// </summary>
    public static BitmapSource? FromExecutable(string exePath, int size = 64)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var key = exePath + "|" + size;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        BitmapSource? result = null;
        try
        {
            if (File.Exists(exePath))
            {
                var handles = new IntPtr[1];
                var ids = new int[1];
                int count = PrivateExtractIcons(exePath, 0, size, size, handles, ids, 1, 0);
                if (count > 0 && handles[0] != IntPtr.Zero)
                {
                    try
                    {
                        result = Imaging.CreateBitmapSourceFromHIcon(
                            handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        result.Freeze();
                    }
                    finally { DestroyIcon(handles[0]); }
                }
            }
        }
        catch { result = null; }

        Cache[key] = result;
        return result;
    }
}
