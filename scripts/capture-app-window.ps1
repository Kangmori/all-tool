# 截取 All Tool 宿主窗口的画面，用于界面验收。
#
# 为什么需要它：这个项目的界面改动无法靠单元测试验收，必须"运行 + 看画面"。
# 而且必须**只截应用窗口**——截全屏会把桌面上其它窗口（浏览器、聊天工具）一起带进来，
# 既没有参考价值，也会把无关内容写进仓库。
#
# 用法：
#   pwsh -File scripts/capture-app-window.ps1                     # 启动应用并截图
#   pwsh -File scripts/capture-app-window.ps1 -ActionIndex 1      # 启动后自动选中第 2 个动作（生成表单）
#   pwsh -File scripts/capture-app-window.ps1 -KeepRunning        # 截完不关，留着手动操作
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\AllTool.App\bin\Debug\net10.0-windows10.0.26100.0\AllTool.App.exe'),
    [string]$Out = (Join-Path $PSScriptRoot '..\spike\app-window.png'),
    [int]$ActionIndex = -1,
    [int]$WaitSeconds = 6,
    [switch]$KeepRunning
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

if (-not ('AllToolWin32' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public struct AllToolRect { public int Left, Top, Right, Bottom; }

public static class AllToolWin32
{
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out AllToolRect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // 关键：PrintWindow 让窗口把自己的内容画到指定 DC 上，
    // **不依赖窗口是否在最上层**，所以不会把桌面上的其它窗口（浏览器之类）拍进来。
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    // 把窗口移到指定位置并置顶（HWND_TOP=0）。SetWindowPos 不受 SetForegroundWindow 的前台限制。
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
}
'@
}

$AllToolPwRenderFullContent = 2
$AllToolSwpShowWindow = 0x0040
$AllToolHwndTop = [IntPtr]::Zero

$Exe = [IO.Path]::GetFullPath($Exe)
$Out = [IO.Path]::GetFullPath($Out)

if (-not (Test-Path $Exe)) {
    throw "找不到可执行文件：$Exe`n先运行：dotnet build src\AllTool.App\AllTool.App.csproj"
}

$process = Get-Process -Name 'AllTool.App' -ErrorAction SilentlyContinue | Select-Object -First 1
$started = $false

if (-not $process) {
    if ($ActionIndex -ge 0) {
        $env:ALLTOOL_SELECT_ACTION = "$ActionIndex"
    }

    $process = Start-Process -FilePath $Exe -PassThru
    $started = $true
    Remove-Item Env:\ALLTOOL_SELECT_ACTION -ErrorAction SilentlyContinue
    Write-Host "已启动：PID $($process.Id)"
}

# 等窗口真正出来
for ($i = 0; $i -lt ($WaitSeconds * 2); $i++) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { throw "应用已退出（启动失败），退出码 $($process.ExitCode)" }
    if ($process.MainWindowHandle -ne 0) { break }
}

if ($process.MainWindowHandle -eq 0) {
    throw "等了 $WaitSeconds 秒仍没拿到窗口句柄"
}

Write-Host "窗口标题：$($process.MainWindowTitle)"

# 先把窗口挪到左上角并置顶。SetWindowPos 不受 SetForegroundWindow 的前台限制，
# 这一步是给 PrintWindow 失败时的兜底（屏幕抓取）创造干净的条件。
[void][AllToolWin32]::ShowWindow($process.MainWindowHandle, 9)   # SW_RESTORE
[void][AllToolWin32]::SetWindowPos($process.MainWindowHandle, $AllToolHwndTop, 0, 0, 1440, 900,
    [uint32]($AllToolSwpShowWindow))
[void][AllToolWin32]::SetForegroundWindow($process.MainWindowHandle)
Start-Sleep -Milliseconds 1000

$rect = New-Object AllToolRect
if (-not [AllToolWin32]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) {
    throw "GetWindowRect 失败"
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -le 0 -or $height -le 0) {
    throw "窗口尺寸异常：${width}x${height}"
}

# 首选：让窗口自己画自己，不受遮挡影响
$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
$printed = [AllToolWin32]::PrintWindow($process.MainWindowHandle, $hdc, $AllToolPwRenderFullContent)
$graphics.ReleaseHdc($hdc)

# 采样几个点判断是不是全黑（WinUI 在某些情况下 PrintWindow 会返回黑图）
function Test-BitmapMostlyBlank($bmp) {
    $samples = 0; $blank = 0
    foreach ($x in 20, [int]($bmp.Width / 2), ($bmp.Width - 20)) {
        foreach ($y in 40, [int]($bmp.Height / 2), ($bmp.Height - 20)) {
            if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
            $pixel = $bmp.GetPixel($x, $y)
            $samples++
            if ($pixel.R -lt 4 -and $pixel.G -lt 4 -and $pixel.B -lt 4) { $blank++ }
        }
    }
    return ($samples -gt 0 -and $blank -eq $samples)
}

$method = 'PrintWindow'
if (-not $printed -or (Test-BitmapMostlyBlank $bitmap)) {
    Write-Warning "PrintWindow 失败或返回空白，退回到屏幕抓取（此刻请勿让其它窗口盖住它）"
    $method = '屏幕抓取'
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
}

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "已保存窗口截图：$Out  (${width}x${height}，方式：$method)"

if ($started -and -not $KeepRunning) {
    Stop-Process -Id $process.Id -Force
    Write-Host "已关闭应用（要留着不动请加 -KeepRunning）"
}
