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
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\AllTool.App\bin\Debug\net10.0-windows10.0.26100.0\AllTool.App.exe'),
    [int]$ActionIndex = 1,
    [string]$Screenshot = (Join-Path $PSScriptRoot '..\spike\ui-smoke.png'),
    [int]$RunTimeoutSeconds = 60
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

$Exe = [IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $Exe)) { throw "找不到 $Exe，先 dotnet build src\AllTool.App\AllTool.App.csproj" }

# ------------------------------------------------------------------ 1. 造测试数据
$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) { throw "本机找不到 7z，无法准备测试数据" }

$work = Join-Path $env:TEMP ('alltool-uismoke-' + [guid]::NewGuid().ToString('N'))
$outDir = Join-Path $work 'extracted'
New-Item -ItemType Directory -Force -Path $work, $outDir | Out-Null
Set-Content (Join-Path $work 'hello.txt') 'hello from ui smoke' -Encoding ascii

# 刻意造一个不可压缩的大文件：7z 只在"真控制台 + 任务够长"时才会画进度，
# 小文件秒完根本看不到百分比，那样就验证不了进度条（N5 的全部意义所在）。
$big = Join-Path $work 'big.bin'
$payload = New-Object byte[] (48MB)
(New-Object Random 42).NextBytes($payload)
[IO.File]::WriteAllBytes($big, $payload)

$archive = Join-Path $work 'sample.7z'
& $sevenZip a -mx1 $archive (Join-Path $work 'hello.txt') $big | Out-Null
if (-not (Test-Path $archive)) { throw "准备测试压缩包失败" }
Write-Host "测试数据：$archive ($([math]::Round((Get-Item $archive).Length/1MB)) MB) → 解压到 $outDir"

# ------------------------------------------------------------------ 2. 启动应用
# 必须同时钉住工具包：宿主现在会"恢复上次选中的工具包/动作"，
# 只设 ALLTOOL_SELECT_ACTION 的话，序号会被套用到上次那个包上（脚本会失灵）。
$env:ALLTOOL_SELECT_PACKAGE = '7zip'
$env:ALLTOOL_SELECT_ACTION = "$ActionIndex"
$process = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:\ALLTOOL_SELECT_ACTION -ErrorAction SilentlyContinue
Remove-Item Env:\ALLTOOL_SELECT_PACKAGE -ErrorAction SilentlyContinue

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
public static class AllToolUiWin32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
'@ -ErrorAction SilentlyContinue
    [void][AllToolUiWin32]::ShowWindow($window.Current.NativeWindowHandle, 9)
    [void][AllToolUiWin32]::SetWindowPos($window.Current.NativeWindowHandle, [IntPtr]::Zero, 0, 0, 1440, 900, 0x40)
    Start-Sleep -Milliseconds 1200

    # ------------------------------------------------------------------ 3. 列出输入框
    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)

    # 只取"表单字段"：按 AutomationId 排掉界面固定控件（筛选框、命令预览、输出区）。
    # 不能按序号硬取——界面加一个输入框就会全部错位（踩过）。
    function Get-FieldEdits {
        $all = $window.FindAll($scope::Descendants, $editCondition)
        $fields = @()
        foreach ($e in $all) {
            $id = $e.Current.AutomationId
            if ($id -in @('FilterBox', 'CommandLineBox', 'OutputBox')) { continue }
            $fields += $e
        }
        return $fields
    }

    # 输出区同样按 AutomationId 找，不靠"最后一个输入框"这种位置假设
    function Get-OutputBox {
        foreach ($e in $window.FindAll($scope::Descendants, $editCondition)) {
            if ($e.Current.AutomationId -eq 'OutputBox') { return $e }
        }
        return $null
    }

    $edits = Get-FieldEdits
    Write-Host "找到 $($edits.Count) 个表单输入框："
    for ($i = 0; $i -lt $edits.Count; $i++) {
        $value = ''
        try { $value = $edits[$i].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $value = '<读不到>' }
        Write-Host ("  [{0}] id='{1}' value='{2}'" -f $i, $edits[$i].Current.AutomationId, $value)
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
    $maxProgress = 0.0
    $progressCondition = New-Object System.Windows.Automation.PropertyCondition(
        $automation::ControlTypeProperty, [System.Windows.Automation.ControlType]::ProgressBar)

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400

        # 顺便盯住进度条：这是 N5（ConPTY）的验收点——只有伪控制台真的生效，它才会真的动
        foreach ($bar in $window.FindAll($scope::Descendants, $progressCondition)) {
            try {
                $value = $bar.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value
                if ($value -gt $maxProgress) { $maxProgress = $value }
            } catch { }
        }

        $editsNow = Get-FieldEdits
        if ($editsNow.Count -gt 0 -and (Get-OutputBox)) {
            try {
                $outputText = (Get-OutputBox).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
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
    $noEscapes = $outputText -notmatch [regex]::Escape([char]27)   # 界面里不该出现原始转义序列

    Write-Host "退出码解读里是否含「成功」：$succeeded"
    Write-Host "文件是否真的被解压出来    ：$produced  ($(Join-Path $outDir 'hello.txt'))"
    Write-Host "进度条观察到的最大值      ：$maxProgress   （N5：> 0 说明 ConPTY 生效、进度真的在走）"
    Write-Host "输出区是否已清理转义序列  ：$noEscapes"
    Write-Host "截图：$Screenshot"

    if (-not $produced) { throw "界面点了执行，但文件没有被解压出来——链路有问题" }
    if (-not $succeeded) { throw "没有从输出里看到成功结论" }
    if (-not $noEscapes) { throw "输出区里出现了原始 ANSI 转义序列，界面应当先清理再显示" }
    if ($maxProgress -le 0) { throw "进度条始终为 0：ConPTY 可能没生效（见 docs/ai/development.md 里 usePseudoConsole 的说明）" }

    Write-Host "`n界面冒烟通过（场景一）：填表 → 执行 → 进度条走动 → 输出无转义序列 → 产物正确" -ForegroundColor Green

    # ==================================================================
    # 场景二：取消。目标里写的是"有实时进度、能取消"，取消这条必须在界面上真点一次。
    # 选 7z 的「添加到压缩包」（动作 0），用 48 MB 输入让它跑几秒，中途点取消。
    # ==================================================================
    Write-Host "`n================ 场景二：取消 ================" -ForegroundColor Cyan

    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800

    $cancelArchive = Join-Path $work 'cancelled.7z'
    $env:ALLTOOL_SELECT_ACTION = '0'
    $process = Start-Process -FilePath $Exe -PassThru
    Remove-Item Env:\ALLTOOL_SELECT_ACTION -ErrorAction SilentlyContinue
    $window = Get-AppWindow
    [void][AllToolUiWin32]::ShowWindow($window.Current.NativeWindowHandle, 9)
    [void][AllToolUiWin32]::SetWindowPos($window.Current.NativeWindowHandle, [IntPtr]::Zero, 0, 0, 1440, 900, 0x40)
    Start-Sleep -Milliseconds 1200

    # 「添加到压缩包」的表单：0=压缩包，1=要压缩的文件/文件夹（每行一项）
    $edits = Get-FieldEdits
    $edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($cancelArchive)
    $edits[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($big)
    Start-Sleep -Milliseconds 600

    $buttons = $window.FindAll($scope::Descendants, $buttonCondition)
    $runButton = $null; $cancelButton = $null
    foreach ($b in $buttons) {
        if ($b.Current.Name -eq '执行') { $runButton = $b }
        if ($b.Current.Name -eq '取消') { $cancelButton = $b }
    }
    if (-not $runButton -or -not $cancelButton) { throw "找不到执行/取消按钮" }

    $runButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "已点击「执行」，等 2 秒后点「取消」…"
    Start-Sleep -Seconds 2

    if (-not $cancelButton.Current.IsEnabled) { throw "执行中「取消」按钮应当是可用状态" }
    $cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Host "已点击「取消」"

    $cancelDeadline = (Get-Date).AddSeconds(30)
    $cancelOutput = ''
    while ((Get-Date) -lt $cancelDeadline) {
        Start-Sleep -Milliseconds 500
        $editsNow = Get-FieldEdits
        if ($editsNow.Count -gt 0 -and (Get-OutputBox)) {
            try { $cancelOutput = (Get-OutputBox).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
        }
        if ($cancelOutput -match '取消') { break }
    }

    Write-Host "`n---- 取消后的输出区 ----"
    Write-Host $cancelOutput
    Write-Host "------------------------"

    $reportedCancel = $cancelOutput -match '已被用户取消'
    $runUsable = $runButton.Current.IsEnabled
    Write-Host "输出里是否报告「已被用户取消」：$reportedCancel"
    Write-Host "取消后「执行」按钮是否恢复可用：$runUsable"

    if (-not $reportedCancel) { throw "点了取消，但界面没有报告「已被用户取消」" }
    if (-not $runUsable) { throw "取消之后「执行」按钮仍是禁用状态，界面没恢复可用" }

    Write-Host "`n界面冒烟通过（场景二）：执行 → 取消 → 报告已取消 → 界面恢复可用" -ForegroundColor Green
    Write-Host "`n界面冒烟全部通过：两个场景都符合预期" -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
