# 界面冒烟：启动宿主 → 填好表单 → 真的点「执行」→ 检查输出。
#
# 为什么需要它：界面改动无法靠单元测试验收（AGENTS.md 里写明要靠"运行 + 截图"）。
# 但"人点一遍"不可复现，所以这里用 UI 自动化把这一步变成可重复执行的脚本。
#
# 已知脆弱点：UIA 只能按控件顺序定位输入框（WinUI 的 TextBlock 标签没有和输入框做 LabeledBy 关联），
# 所以脚本会把找到的输入框逐个打印出来，方便失败时定位是哪一步对不上。
#
# 用法：pwsh -File scripts/ui-smoke.ps1
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\Swpj.App\bin\Debug\net10.0-windows10.0.26100.0\Swpj.App.exe'),
    [int]$ActionIndex = 1,
    [string]$Screenshot = (Join-Path $PSScriptRoot '..\spike\ui-smoke.png'),
    [int]$RunTimeoutSeconds = 60
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$Exe = [IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $Exe)) { throw "找不到 $Exe，先 dotnet build src\Swpj.App\Swpj.App.csproj" }

# ------------------------------------------------------------------ 1. 造测试数据
$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) { throw "本机找不到 7z，无法准备测试数据" }

$work = Join-Path $env:TEMP ('swpj-uismoke-' + [guid]::NewGuid().ToString('N'))
$outDir = Join-Path $work 'extracted'
New-Item -ItemType Directory -Force -Path $work, $outDir | Out-Null
Set-Content (Join-Path $work 'hello.txt') 'hello from ui smoke' -Encoding ascii
$archive = Join-Path $work 'sample.7z'
& $sevenZip a $archive (Join-Path $work 'hello.txt') | Out-Null
if (-not (Test-Path $archive)) { throw "准备测试压缩包失败" }
Write-Host "测试数据：$archive → 解压到 $outDir"

# ------------------------------------------------------------------ 2. 启动应用
$env:SWPJ_SELECT_ACTION = "$ActionIndex"
$process = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:\SWPJ_SELECT_ACTION -ErrorAction SilentlyContinue

$automation = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]

function Get-AppWindow {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ProcessIdProperty, $process.Id)
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $found = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope::Children, $condition)
        if ($found) { return $found }
        if ($process.HasExited) { throw "应用启动即退出" }
    }
    throw "等不到应用窗口"
}

try {
    $window = Get-AppWindow
    Write-Host "窗口：$($window.Current.Name)"

    # 让窗口置顶，UIA 对不可见元素会返回空
    Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SwpjUiWin32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
'@ -ErrorAction SilentlyContinue
    [void][SwpjUiWin32]::ShowWindow($window.Current.NativeWindowHandle, 9)
    [void][SwpjUiWin32]::SetWindowPos($window.Current.NativeWindowHandle, [IntPtr]::Zero, 0, 0, 1440, 900, 0x40)
    Start-Sleep -Milliseconds 1200

    # ------------------------------------------------------------------ 3. 列出输入框
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $edits = $window.FindAll($scope::Descendants, $editCondition)
    Write-Host "找到 $($edits.Count) 个输入框："
    for ($i = 0; $i -lt $edits.Count; $i++) {
        $value = ''
        try { $value = $edits[$i].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $value = '<读不到>' }
        Write-Host ("  [{0}] name='{1}' value='{2}'" -f $i, $edits[$i].Current.Name, $value)
    }

    if ($edits.Count -lt 2) { throw "输入框数量不足，无法填表" }

    # ------------------------------------------------------------------ 4. 填表
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($archive)
    $edits[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($outDir)
    Start-Sleep -Milliseconds 800
    Write-Host "已填入：压缩包=$archive  解压到=$outDir"

    # ------------------------------------------------------------------ 5. 点「执行」
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $window.FindAll($scope::Descendants, $buttonCondition)
    $runButton = $null
    for ($i = 0; $i -lt $buttons.Count; $i++) {
        if ($buttons[$i].Current.Name -eq '执行') { $runButton = $buttons[$i]; break }
    }
    if (-not $runButton) {
        $names = ($buttons | ForEach-Object { $_.Current.Name }) -join ', '
        throw "找不到「执行」按钮。现有按钮：$names"
    }

    $runButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "已点击「执行」，等待完成…"

    # ------------------------------------------------------------------ 6. 等输出出现结果
    $deadline = (Get-Date).AddSeconds($RunTimeoutSeconds)
    $outputText = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        $editsNow = $window.FindAll($scope::Descendants, $editCondition)
        if ($editsNow.Count -gt 0) {
            try {
                $outputText = $editsNow[$editsNow.Count - 1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            } catch { }
        }
        if ($outputText -match '退出码|失败|取消') { break }
    }

    # 让输出区滚到底部后截图
    Start-Sleep -Milliseconds 800
    & (Join-Path $PSScriptRoot 'capture-app-window.ps1') -Out $Screenshot -KeepRunning | Out-Null

    Write-Host "`n================ 输出区内容 ================"
    Write-Host $outputText
    Write-Host "===========================================`n"

    # ------------------------------------------------------------------ 7. 判定
    $produced = Test-Path (Join-Path $outDir 'hello.txt')
    $succeeded = $outputText -match '成功'

    Write-Host "退出码解读里是否含「成功」：$succeeded"
    Write-Host "文件是否真的被解压出来    ：$produced  ($(Join-Path $outDir 'hello.txt'))"
    Write-Host "截图：$Screenshot"

    if (-not $produced) { throw "界面点了执行，但文件没有被解压出来——链路有问题" }
    if (-not $succeeded) { throw "没有从输出里看到成功结论" }

    Write-Host "`n界面冒烟通过：填表 → 执行 → 输出 → 产物 全部符合预期" -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
