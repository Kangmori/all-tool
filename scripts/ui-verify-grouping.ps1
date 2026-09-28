# 验证"工具包分组"这条路径真的能用。
#
# 为什么必须单独有这个脚本：用户**连续两次**反馈"改不了分组"，而这件事原本依赖右键菜单与鼠标拖动，
# 两者都很难自动化验证 —— UIA 读不到 ContextFlyout；合成的鼠标右键又需要窗口在前台，
# 而 SetForegroundWindow 从非前台进程调用会被系统拒绝（实测：模拟右键落在别的窗口上，菜单没弹）。
# 所以界面上补了一个**看得见的「分组…」按钮**，本脚本验证的就是那条路径：
#   选中工具包 → 点「分组…」→ 输入新分组名 → 应用 → 断言新分组出现、未分组少一个
#   → 再用下拉框选「未分组」→ 断言移回。
# 右键菜单里的「移动到分组」是同一套逻辑的另一个入口（都走 MovePackageToGroup），
# 只是没法自动化，需要人工点一次。
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\Swpj.App\bin\Debug\net10.0-windows10.0.26100.0\Swpj.App.exe'),
    [string]$PackageId = 'ping',
    [string]$GroupName = '我的常用'
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

if (-not (Test-Path $Exe)) { throw "找不到 $Exe，先 dotnet build src\Swpj.slnx" }

# 从干净状态开始，否则断言会被上次残留影响
Remove-Item (Join-Path $env:APPDATA 'swpj\grouping.json') -Force -ErrorAction SilentlyContinue

$AE = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$true_ = [System.Windows.Automation.Condition]::TrueCondition

$env:SWPJ_SELECT_PACKAGE = $PackageId
$process = Start-Process -FilePath $Exe -PassThru
Remove-Item Env:\SWPJ_SELECT_PACKAGE -ErrorAction SilentlyContinue

$window = $null
for ($i = 0; $i -lt 40 -and -not $window; $i++) {
    Start-Sleep -Milliseconds 500
    $window = $AE::RootElement.FindFirst($scope::Children,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $process.Id)))
    if ($process.HasExited) { throw "应用启动即退出" }
}
if (-not $window) { throw "等不到应用窗口" }

function Get-All([string]$type) {
    $r = @()
    foreach ($e in $window.FindAll($scope::Descendants, $true_)) {
        if ($e.Current.ControlType.ProgrammaticName -eq "ControlType.$type") { $r += $e }
    }
    return $r
}
function Find-Control([string]$type, [string]$pattern) {
    foreach ($e in Get-All $type) { if ($e.Current.Name -like $pattern) { return $e } }
    return $null
}
function Invoke-Control($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Get-GroupTitles {
    $t = @()
    foreach ($e in (Get-All 'Button') + (Get-All 'Text')) { if ($e.Current.Name -match '^.+（\d+）$') { $t += $e.Current.Name } }
    return ($t | Select-Object -Unique)
}
function Get-Ungrouped($titles) {
    $m = $titles | Where-Object { $_ -match '^未分组（(\d+)）$' } | Select-Object -First 1
    if (-not $m) { throw "找不到「未分组（n）」（当前：$($titles -join ' / ')）" }
    return [int]($m -replace '^未分组（(\d+)）$', '$1')
}

try {
    Write-Host "窗口：$($window.Current.Name)"
    $before = Get-GroupTitles
    Write-Host "改分组前：" ; $before | ForEach-Object { "  $_" }
    $beforeUngrouped = Get-Ungrouped $before

    $btn = Find-Control 'Button' '分组…'
    if (-not $btn) { throw "找不到「分组…」按钮" }
    Invoke-Control $btn
    Start-Sleep -Milliseconds 1500

    $edits = @()
    foreach ($e in Get-All 'Edit') {
        if ($e.Current.AutomationId -notin @('FilterBox', 'CommandLineBox', 'OutputBox')) { $edits += $e }
    }
    if ($edits.Count -lt 1) { throw "对话框里找不到输入框" }
    $edits[-1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($GroupName)
    Start-Sleep -Milliseconds 400

    $apply = Find-Control 'Button' '应用'
    if (-not $apply) { throw "找不到「应用」按钮" }
    Invoke-Control $apply
    Start-Sleep -Milliseconds 1800

    $after = Get-GroupTitles
    Write-Host "改分组后：" ; $after | ForEach-Object { "  $_" }

    $expected = "$GroupName（1）"
    if (-not ($after -contains $expected)) { throw "没有出现分组「$expected」（当前：$($after -join ' / ')）" }
    $afterUngrouped = Get-Ungrouped $after
    if ($afterUngrouped -ne $beforeUngrouped - 1) { throw "未分组没有减 1：$beforeUngrouped → $afterUngrouped" }
    Write-Host "★ 分组成功：$expected；未分组 $beforeUngrouped → $afterUngrouped" -ForegroundColor Green

    Invoke-Control (Find-Control 'Button' '分组…')
    Start-Sleep -Milliseconds 1500
    $combo = (Get-All 'ComboBox') | Select-Object -First 1
    if (-not $combo) { throw "找不到分组下拉框" }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 800

    $item = $null
    foreach ($e in Get-All 'ListItem') { if ($e.Current.Name -eq '未分组') { $item = $e; break } }
    if (-not $item) { throw "下拉框里找不到「未分组」" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
    Invoke-Control (Find-Control 'Button' '应用')
    Start-Sleep -Milliseconds 1800

    $final = Get-GroupTitles
    Write-Host "移回后：" ; $final | ForEach-Object { "  $_" }
    $finalUngrouped = Get-Ungrouped $final
    if ($finalUngrouped -ne $beforeUngrouped) { throw "移回后未分组应为 $beforeUngrouped，实际 $finalUngrouped" }

    Write-Host "★ 已移回「未分组（$finalUngrouped）」" -ForegroundColor Green
    Write-Host "分组功能验证通过：分组… → 新建并移入 → 移回未分组" -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
