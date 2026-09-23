# Launches a WPF exe, waits for its window, captures it to a PNG, then closes it.
# Usage: capture.ps1 -Exe <path> -Args "--preview" -Out <png> [-WaitMs 1400]
param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [string]$ArgLine = "",
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$WaitMs = 1400
)

Add-Type -AssemblyName System.Drawing

$sig = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static List<IntPtr> ForPid(uint target){var r=new List<IntPtr>();EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==target&&IsWindowVisible(h))r.Add(h);return true;},IntPtr.Zero);return r;}
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
Add-Type $sig

$p = Start-Process -FilePath $Exe -ArgumentList $ArgLine -PassThru
Start-Sleep -Milliseconds $WaitMs

# poll for any visible top-level window belonging to the process
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
    if (-not $p.HasExited) {
        $wins = [Win]::ForPid([uint32]$p.Id)
        if ($wins.Count -gt 0) { $h = $wins[0]; break }
    }
    Start-Sleep -Milliseconds 100
}

if ($h -eq [IntPtr]::Zero) { Write-Output "NO_WINDOW"; try { $p.Kill() } catch {}; exit 1 }

[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 250

$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top
if ($w -le 0 -or $hgt -le 0) { Write-Output "BAD_RECT"; try { $p.Kill() } catch {}; exit 1 }

$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hgt))
$dir = Split-Path $Out -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

try { $p.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 200; if (-not $p.HasExited) { $p.Kill() } } catch {}
Write-Output ("OK " + $w + "x" + $hgt + " -> " + $Out)
