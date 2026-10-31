# Launches the published BdoComboSetup.exe once per screenshot (synthetic data, --demo) and saves ONLY the app window to docs\screenshots.
# The folder is git-ignored (game art): the images are generated locally and never committed.
# The window is rendered by the app itself (PrintWindow with PW_RENDERFULLCONTENT), so other windows that cover it
# never appear in the image. The frame is cropped to the visible bounds reported by DWM (no invisible shadow border).
# Windows PowerShell 5.1 (System.Drawing is available there). Run through `mise run screenshots`.
[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$OutDir,
    [int]$StartupTimeoutSeconds = 60,
    [int]$SettleSeconds = 4
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$appRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $ExePath) {
    $ExePath = Join-Path $appRoot 'src\BdoComboSetup\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish\BdoComboSetup.exe'
}
if (-not $OutDir) {
    $OutDir = Join-Path $appRoot 'docs\screenshots'
}

# One entry per image: the command-line options that open the page. --demo gives synthetic characters, synthetic layouts and
# a synthetic 3440 x 1440 display, so the images never show the game account, its character names or its files.
$shots = @(
    @{ File = 'apply-combo.png';        Arguments = @('--page', 'apply', '--demo') },
    @{ File = 'apply-combo-shai.png';   Arguments = @('--page', 'apply', '--demo', '--class', 'shai', '--spec', 'talent') },
    @{ File = 'apply-combo-review.png'; Arguments = @('--page', 'apply', '--demo-review') },
    @{ File = 'apply-combo-write-hint.png'; Arguments = @('--page', 'apply', '--demo-write-hint') },
    @{ File = 'apply-character-picker-closed.png'; Arguments = @('--page', 'apply', '--demo', '--demo-characters', '35') },
    @{ File = 'apply-character-picker-open.png'; Arguments = @('--page', 'apply', '--demo', '--demo-characters', '35', '--demo-character-picker') },
    @{ File = 'settings.png';           Arguments = @('--page', 'settings', '--demo') },
    @{ File = 'update-banner.png';      Arguments = @('--page', 'apply', '--demo-update') },
    @{ File = 'preview-general.png';    Arguments = @('--page', 'preview', '--demo', '--layout', 'general') },
    @{ File = 'preview-preset-1.png';   Arguments = @('--page', 'preview', '--demo', '--layout', 'preset1') },
    @{ File = 'preview-preset-2.png';   Arguments = @('--page', 'preview', '--demo', '--layout', 'preset2') },
    @{ File = 'preview-preset-3.png';   Arguments = @('--page', 'preview', '--demo', '--layout', 'preset3') },
    @{ File = 'preview-hidden-slots.png'; Arguments = @('--page', 'preview', '--demo', '--layout', 'general', '--hidden-slots') },
    @{ File = 'preview-cooldown-demo.png'; Arguments = @('--page', 'preview', '--demo', '--layout', 'general', '--cooldown-demo') }
)

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class BdoComboWin32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, ref RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, ref RECT rect, int size);
}
'@

# DWMWA_EXTENDED_FRAME_BOUNDS: the visible window rectangle, without the invisible resize border.
$DwmExtendedFrameBounds = 9
# PW_RENDERFULLCONTENT: asks the window to draw its composed content (needed for XAML / DirectComposition output).
$PwRenderFullContent = 2

function Get-WindowRectangle {
    param([IntPtr]$Handle)
    $rect = New-Object BdoComboWin32+RECT
    if (-not [BdoComboWin32]::GetWindowRect($Handle, [ref]$rect)) {
        throw "GetWindowRect failed for window $Handle"
    }
    return $rect
}

function Get-VisibleFrame {
    param([IntPtr]$Handle)
    $rect = New-Object BdoComboWin32+RECT
    $size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'BdoComboWin32+RECT')
    $result = [BdoComboWin32]::DwmGetWindowAttribute($Handle, $DwmExtendedFrameBounds, [ref]$rect, $size)
    if ($result -ne 0) {
        # Fall back to the window rectangle when DWM does not answer.
        return Get-WindowRectangle -Handle $Handle
    }
    return $rect
}

function Test-BlankBitmap {
    param([System.Drawing.Bitmap]$Bitmap)
    # Samples a grid of pixels; a window that was never painted is all black.
    $bright = 0
    for ($i = 1; $i -le 9; $i++) {
        for ($j = 1; $j -le 9; $j++) {
            $pixel = $Bitmap.GetPixel([int]($Bitmap.Width * $i / 10), [int]($Bitmap.Height * $j / 10))
            if (($pixel.R + $pixel.G + $pixel.B) -gt 30) { $bright++ }
        }
    }
    return $bright -eq 0
}

function Save-WindowPng {
    param([IntPtr]$Handle, [string]$Path)

    $outer = Get-WindowRectangle -Handle $Handle
    $visible = Get-VisibleFrame -Handle $Handle
    $fullWidth = $outer.Right - $outer.Left
    $fullHeight = $outer.Bottom - $outer.Top

    $full = New-Object System.Drawing.Bitmap -ArgumentList $fullWidth, $fullHeight
    $graphics = [System.Drawing.Graphics]::FromImage($full)
    try {
        $hdc = $graphics.GetHdc()
        try {
            $printed = [BdoComboWin32]::PrintWindow($Handle, $hdc, $PwRenderFullContent)
        }
        finally {
            $graphics.ReleaseHdc($hdc)
        }
        if (-not $printed) {
            throw "PrintWindow did not render window $Handle"
        }

        $cropRectangle = New-Object System.Drawing.Rectangle -ArgumentList `
            ($visible.Left - $outer.Left), ($visible.Top - $outer.Top), `
            ($visible.Right - $visible.Left), ($visible.Bottom - $visible.Top)
        $cropped = $full.Clone($cropRectangle, $full.PixelFormat)
        try {
            if (Test-BlankBitmap -Bitmap $cropped) {
                throw "The captured image for $Path is blank; the window was not painted."
            }
            $cropped.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Output ("saved {0} ({1}x{2})" -f $Path, $cropped.Width, $cropped.Height)
        }
        finally {
            $cropped.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $full.Dispose()
    }
}

function Wait-MainWindow {
    param($Process)
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $handle = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline -and -not $Process.HasExited) {
        Start-Sleep -Milliseconds 300
        $Process.Refresh()
        $candidate = $Process.MainWindowHandle
        if ($candidate -eq [IntPtr]::Zero) {
            continue
        }

        # Accept only a window that belongs to this process, so a closing instance can never be captured instead.
        $ownerId = [uint32]0
        [void][BdoComboWin32]::GetWindowThreadProcessId($candidate, [ref]$ownerId)
        if ($ownerId -eq [uint32]$Process.Id) {
            $handle = $candidate
            break
        }
    }
    if ($handle -eq [IntPtr]::Zero) {
        throw "The main window of process $($Process.Id) did not appear within $StartupTimeoutSeconds seconds."
    }
    return $handle
}

function Wait-UsableWindow {
    param([IntPtr]$Handle)
    # Restored and at a real size (a first frame can be minimized or tiny).
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ([BdoComboWin32]::IsZoomed($Handle)) { [void][BdoComboWin32]::ShowWindow($Handle, 9) }   # maximized: back to the 1440x900 size
        [void][BdoComboWin32]::ShowWindow($Handle, 9)            # SW_RESTORE
        [void][BdoComboWin32]::SetForegroundWindow($Handle)
        Start-Sleep -Milliseconds 500
        $rect = Get-WindowRectangle -Handle $Handle
        if (-not [BdoComboWin32]::IsIconic($Handle) `
            -and ($rect.Right - $rect.Left) -ge 640 `
            -and ($rect.Bottom - $rect.Top) -ge 480) {
            return
        }
    }
    throw 'The main window never reached a usable size.'
}

$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

foreach ($shot in $shots) {
    $process = Start-Process -FilePath $ExePath -ArgumentList $shot.Arguments -PassThru
    try {
        $null = $process.WaitForInputIdle($StartupTimeoutSeconds * 1000)
        $handle = Wait-MainWindow -Process $process
        Wait-UsableWindow -Handle $handle
        # Give the page time to load the layout, the icons and the background.
        Start-Sleep -Seconds $SettleSeconds
        Save-WindowPng -Handle $handle -Path (Join-Path $OutDir $shot.File)
    }
    finally {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(10000)) {
                $process.Kill()
                Write-Output "$($shot.File): the app did not close on request; it was stopped."
            }
        }
    }
}

# Images of earlier versions of this script that are no longer produced.
foreach ($name in @('preview.png', 'preview-general-panels.png')) {
    $obsolete = Join-Path $OutDir $name
    if (Test-Path -LiteralPath $obsolete) {
        Remove-Item -LiteralPath $obsolete
        Write-Output "removed the obsolete $name"
    }
}
