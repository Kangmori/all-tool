# 会话型（session）机制的界面回归：验证「状态门控」与「脚本重放」。
#
# 用法：pwsh -NoProfile -File scripts/ui-verify-session.ps1 [-Package sqlite3]
#
# 判据（N35 要的三条）：
#   ① 没打开数据库时，依赖动作（列出表）的「执行」按钮必须**不可用**，并给出「先执行…」提示
#   ② 执行「打开数据库」时，宿主生成的会话脚本里必须**重放**.open（输出里能看到脚本内容）
#   ③ **同一进程内**切到「列出表」后，执行按钮必须**变为可用**，且执行结果里有库里的表名
#
# 踩过的四个坑（都写在这儿，免得下次重踩）：
#   1. 会话状态是**进程内**的 → 第 ③ 条不能靠重启进程验证；
#   2. **动作列表的分组是折叠的 Expander**：要先用 `ExpandCollapsePattern.Expand()` 展开，
#      否则动作行根本不在 UIA 树里（`InvokePattern` 会报"点不动"，TogglePattern 也不行）；
#   3. 动作行的可访问名称带后缀（形如「列出表（-）」）→ 按**子串**匹配，不要用等号；
#   4. 临时库用**唯一文件名**：否则上一次的库还在，`create table` 会报 already exists，
#      而 `Remove-Item -EA SilentlyContinue` 会把删除失败悄悄吞掉。
param(
    [string]$Package = 'sqlite3',
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\AllTool.App\bin\Debug\net10.0-windows10.0.26100.0\AllTool.exe')
)

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]::Descendants
$anyCond = [System.Windows.Automation.Condition]::TrueCondition
$editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)

function Start-AppWith([string]$package, [string]$actionIndex) {
    Get-Process AllTool -EA SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    $env:ALLTOOL_SELECT_PACKAGE = $package
    if ($actionIndex) { $env:ALLTOOL_SELECT_ACTION = $actionIndex } else { Remove-Item Env:\ALLTOOL_SELECT_ACTION -EA SilentlyContinue }
    $p = Start-Process $Exe -PassThru
    Remove-Item Env:\ALLTOOL_SELECT_PACKAGE -EA SilentlyContinue
    Remove-Item Env:\ALLTOOL_SELECT_ACTION -EA SilentlyContinue
    Start-Sleep -Seconds 14
    $p.Refresh()
    $w = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
    return @{ Proc = $p; Win = $w }
}

function Get-Texts($w) {
    $r = @()
    foreach ($e in $w.FindAll($scope, $anyCond)) {
        if ($e.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $e.Current.Name) { $r += $e.Current.Name }
    }
    return $r
}

# 把左栏所有可折叠的分组展开（动作行只有展开后才在 UIA 树里）
function Expand-AllGroups($w) {
    $n = 0
    foreach ($e in $w.FindAll($scope, $anyCond)) {
        if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { continue }
        if (-not $e.Current.Name) { continue }
        if ($e.Current.Name -notmatch '（\d+）$') { continue }        # 形如「查询（4）」
        try { $e.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); $n++ } catch { }
    }
    Start-Sleep -Milliseconds 800
    return $n
}

# $exact = $true 时按等号匹配（工具条按钮「执行」「取消」必须这样，
# 否则子串会先命中动作行「执行查询」——这一坑我踩过一次）
function Find-Clickable($w, [string]$name, [bool]$exact = $false) {
    foreach ($e in $w.FindAll($scope, $anyCond)) {
        $t = $e.Current.ControlType.ProgrammaticName
        if ($t -notin @('ControlType.Button', 'ControlType.ListItem', 'ControlType.TreeItem')) { continue }
        if (-not $e.Current.Name) { continue }
        if ($exact) { if ($e.Current.Name -eq $name) { return $e } }
        elseif ($e.Current.Name.Contains($name)) { return $e }
    }
    return $null
}

function Get-RunEnabled($w) {
    $e = Find-Clickable $w '执行' $true
    if ($e) { return $e.Current.IsEnabled }
    return $null
}

function Get-EditValue($w, [string]$id) {
    foreach ($e in $w.FindAll($scope, $editCond)) {
        if ($e.Current.AutomationId -eq $id) { return $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
    }
    return ''
}

function Set-FirstInput($w, [string]$value) {
    foreach ($e in $w.FindAll($scope, $editCond)) {
        if ($e.Current.AutomationId -in @('FilterBox', 'CommandLineBox', 'OutputBox')) { continue }
        $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
        return $true
    }
    return $false
}

function Click-Name($w, [string]$name, [bool]$exact = $false) {
    $e = Find-Clickable $w $name $exact
    if (-not $e) { return $false }
    try { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800; return $true }
    catch { return $false }
}

# ---------------------------------------------------------------- 准备一个真库（唯一文件名）
$dbDir = Join-Path $env:TEMP 'at-session-verify'
New-Item -ItemType Directory -Force $dbDir | Out-Null
$dbFile = Join-Path $dbDir ("demo-{0}.db" -f (Get-Date -Format 'HHmmssfff'))
$db = $dbFile.Replace('\', '/')          # 脚本里必须用正斜杠（见 sqlite3 的 NOTES）
& sqlite3 $db "create table 学生(id integer, 姓名 text); insert into 学生 values(1,'张三');" | Out-Null
Write-Host ("库: {0}" -f $db)

$failed = 0
$a = Start-AppWith $Package '1'          # 1 = 列出表（requires: db）
$expanded = Expand-AllGroups $a.Win
Write-Host ("（已展开分组 {0} 个）" -f $expanded)

$enabledBefore = Get-RunEnabled $a.Win
$hintShown = [bool]((Get-Texts $a.Win) -match '先执行')
Write-Host ("① 未打开库时「列出表」的执行按钮可用 = {0}（期望 False）" -f $enabledBefore)
Write-Host ("   出现「先执行…」提示 = {0}" -f $hintShown)
if ($enabledBefore -ne $false) { $failed++; Write-Host '   ✗ 门控没生效' }
if (-not $hintShown) { $failed++; Write-Host '   ✗ 没给出原因提示' }

# 同进程内切到「打开数据库」
Write-Host ("② 点「打开数据库」= {0}" -f (Click-Name $a.Win '打开数据库'))
Write-Host ("   填入路径 = {0}" -f (Set-FirstInput $a.Win $db))
Start-Sleep -Milliseconds 800
Click-Name $a.Win '执行' $true | Out-Null
Start-Sleep -Seconds 8
$out1 = Get-EditValue $a.Win 'OutputBox'
$replayed = $out1 -match '\.open'
Write-Host ("   会话脚本里重放了 .open = {0}" -f $replayed)
if (-not $replayed) { $failed++; Write-Host '   ✗ 没看到 .open 被重放' }

# 同进程内切回「列出表」：门控应已解除
Expand-AllGroups $a.Win | Out-Null
Click-Name $a.Win '列出表' | Out-Null
Start-Sleep -Seconds 1
$enabledAfter = Get-RunEnabled $a.Win
Write-Host ("③ 打开库后（同进程）「列出表」的执行按钮可用 = {0}（期望 True）" -f $enabledAfter)
if ($enabledAfter -ne $true) { $failed++; Write-Host '   ✗ 门控没有解除' }

if ($enabledAfter -eq $true) {
    Click-Name $a.Win '执行' $true | Out-Null
    Start-Sleep -Seconds 8
    $out2 = Get-EditValue $a.Win 'OutputBox'
    $hasTable = $out2 -match '学生'
    Write-Host ("   输出含表名「学生」= {0}" -f $hasTable)
    Write-Host ("   输出片段: {0}" -f (($out2 -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3) -join ' / '))
    if (-not $hasTable) { $failed++; Write-Host '   ✗ 输出里没有表名 —— 脚本重放可能没有真正打开库' }
}

Stop-Process -Id $a.Proc.Id -Force
Write-Host ''
Write-Host ("结论：失败项 {0} 个" -f $failed)
exit $failed
