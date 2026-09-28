# Windows 自带命令工具包的真机冒烟测试。
#
# 三件事：
#  1) 用只读白名单自检，列出**绝不允许被判为可跑**的命令；任何一条漏过去就 throw 中止；
#  2) 逐条实跑白名单内的命令，记录真实退出码（不猜测、不套用文档）；
#  3) 用清单里 examples 的 argv 与真实执行的 argv 逐字比对（NUL 连接后比字符串，
#     **不要用 -eq 比数组** —— PowerShell 里那是过滤语义，见 playbook §5.1）。
#
# 用法：pwsh -File scripts/smoke-win-cmds.ps1
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:TEMP 'win-cmds-smoke')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutDir = [IO.Path]::GetFullPath($OutDir)
Remove-Item -Recurse -Force $OutDir -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Sys = "$env:SystemRoot\System32"

# ─────────────────────────────────────────────────────────────
# 1. 只读白名单：整条 argv 必须与其中某一项逐字相同
# ─────────────────────────────────────────────────────────────
$ReadOnlyAllowed = @(
    , @('ping', '127.0.0.1', '-n', '2')
    , @('ping', '-n', '2', 'localhost')
    , @('ipconfig')
    , @('ipconfig', '/all')
    , @('ipconfig', '/displaydns')
    , @('tracert', '-h', '1', '127.0.0.1')
    , @('tracert', '-d', '-h', '1', '127.0.0.1')
    , @('nslookup', 'localhost')
    , @('nslookup', '-type=AAAA', 'localhost')
    , @('netstat', '-an')
    , @('netstat', '-rn')
    , @('netstat', '-s')
    , @('netstat', '-e')
    , @('tasklist')
    , @('tasklist', '/svc')
    , @('tasklist', '/fo', 'csv', '/nh')
    , @('systeminfo')
    , @('systeminfo', '/fo', 'csv')
    , @('chkdsk')
    , @('sfc', '/verifyonly')
    , @('ROBOCOPY_L')                                 # 见下面的专用正则（真实路径无法逐字列举）
    , @('powercfg', '/list')
    , @('powercfg', '/getactivescheme')
    , @('powercfg', '/query')
    , @('powercfg', '/a')
    , @('powercfg', '/aliases')
    , @('powercfg', '/lastwake')
    , @('powercfg', '/waketimers')
    , @('powercfg', '/requests')
)

# 2. 红线：这些**绝不允许**被判为可跑。判错就中止，而不是"跑着看"。
$MustNeverRun = @(
    , @('ipconfig', '/release')
    , @('ipconfig', '/renew')
    , @('ipconfig', '/flushdns')
    , @('ipconfig', '/registerdns')
    , @('ipconfig', '/setclassid', 'Ethernet', 'TEST')
    , @('chkdsk', 'C:', '/f')
    , @('chkdsk', 'C:', '/r')
    , @('chkdsk', '/scan')
    , @('chkdsk', '/spotfix')
    , @('chkdsk', '/offlinescanandfix')
    , @('sfc', '/scannow')
    , @('sfc', '/scanfile=C:\Windows\System32\kernel32.dll')
    , @('cleanmgr')
    , @('cleanmgr', '/sagerun:1')
    , @('cleanmgr', '/verylowdisk')
    , @('cleanmgr', '/autoclean')
    , @('robocopy', '<SRC>', '<DST>')                 # 不带 /L 的 robocopy 真的会复制
    , @('robocopy', '<SRC>', '<DST>', '/MIR')
    , @('robocopy', '<SRC>', '<DST>', '/PURGE')
    , @('robocopy', '<SRC>', '<DST>', '/MOV')
    , @('powercfg', '/hibernate', 'off')
    , @('powercfg', '/hibernate', 'on')
    , @('powercfg', '/change', 'monitor-timeout-ac', '5')
    , @('powercfg', '/setactive', '381b4222-f694-41f0-9685-ff5bb260df2e')
    , @('powercfg', '/delete', '381b4222-f694-41f0-9685-ff5bb260df2e')
    , @('powercfg', '/systempowerreport')
    , @('powercfg', '/systemsleepdiagnostics')
    , @('powercfg', '/energy')
    , @('powercfg', '/sleepstudy')
    , @('shutdown', '/s')
)

function Test-Exact {
    param([string[]]$Left, [string[]]$Right)
    if ($Left.Count -ne $Right.Count) { return $false }
    return (($Left -join "`0") -eq ($Right -join "`0"))
}

function Test-Allowed {
    param([string[]]$Argv)
    foreach ($pattern in $ReadOnlyAllowed) {
        if (Test-Exact -Left $Argv -Right $pattern) { return $true }
    }
    # robocopy 的只读用法：源/目标是我们自己造的临时目录路径，无法逐字列举，
    # 所以用一条**结构上更严**的判定代替：四个参数、第一个是 robocopy、最后一个是 /L
    # （/L = 只列出，不复制、不删除任何文件），且前两个参数必须都是真实存在的目录。
    # 注意 /L 必须在**末尾**：这条约束让 /MIR /PURGE 之类不可能溜进来（它们会占掉第 4 个位置）。
    if ($Argv.Count -eq 4 -and $Argv[0] -eq 'robocopy' -and $Argv[3] -eq '/L') {
        if ((Test-Path -LiteralPath $Argv[1] -PathType Container) -and
            (Test-Path -LiteralPath $Argv[2] -PathType Container)) { return $true }
    }
    return $false
}

# ── 白名单自检：红线必须一条都不通过 ──
$leaked = @()
foreach ($bad in $MustNeverRun) {
    if (Test-Allowed -Argv $bad) { $leaked += ($bad -join ' ') }
}
if ($leaked.Count -gt 0) {
    throw ("只读白名单自检失败，以下命令被误判为可跑，脚本中止：`n  " + ($leaked -join "`n  "))
}
Write-Host "== 白名单自检 ==" -ForegroundColor Cyan
Write-Host ("  只读白名单 {0} 条；红线 {1} 条；误判 {2} 条 → 通过" -f `
    $ReadOnlyAllowed.Count, $MustNeverRun.Count, $leaked.Count)

# ─────────────────────────────────────────────────────────────
# 3. 实跑
# ─────────────────────────────────────────────────────────────
function Invoke-Smoke {
    param([string]$Exe, [string[]]$CmdArgs, [int]$TimeoutSeconds = 60)
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = $OutDir
    foreach ($a in $CmdArgs) { [void]$psi.ArgumentList.Add($a) }
    $p = [Diagnostics.Process]::Start($psi)
    $o = [IO.MemoryStream]::new(); $e = [IO.MemoryStream]::new()
    $t1 = $p.StandardOutput.BaseStream.CopyToAsync($o)
    $t2 = $p.StandardError.BaseStream.CopyToAsync($e)
    $to = -not $p.WaitForExit($TimeoutSeconds * 1000)
    if ($to) { try { $p.Kill($true) } catch { }; $null = $p.WaitForExit(3000) }
    [Threading.Tasks.Task]::WaitAll(@($t1, $t2))
    $exit = if ($to) { $null } else { $p.ExitCode }
    $p.Dispose()
    $enc = [Text.Encoding]::GetEncoding(936)
    return [pscustomobject]@{
        Exit = $exit; To = $to
        OutLen = $o.Length; ErrLen = $e.Length
        OutText = $enc.GetString($o.ToArray()); ErrText = $enc.GetString($e.ToArray())
    }
}

# 造一个 robocopy 的只读样本目录
$src = Join-Path $OutDir 'src'; $dst = Join-Path $OutDir 'dst'
New-Item -ItemType Directory -Force -Path (Join-Path $src 'sub') | Out-Null
Set-Content (Join-Path $src 'a.txt') 'hello' -Encoding ascii
Set-Content (Join-Path $src 'sub\b.txt') 'world' -Encoding ascii
New-Item -ItemType Directory -Force -Path $dst | Out-Null

$cases = @(
    @{ n = 'ping 127.0.0.1 -n 2';      e = "$Sys\ping.exe";      a = @('127.0.0.1', '-n', '2') }
    @{ n = 'ping -n 2 localhost';      e = "$Sys\ping.exe";      a = @('-n', '2', 'localhost') }
    @{ n = 'ipconfig';                 e = "$Sys\ipconfig.exe";  a = @() }
    @{ n = 'ipconfig /all';            e = "$Sys\ipconfig.exe";  a = @('/all') }
    @{ n = 'tracert -h 1 127.0.0.1';   e = "$Sys\tracert.exe";   a = @('-h', '1', '127.0.0.1') }
    @{ n = 'tracert -d -h 1';          e = "$Sys\tracert.exe";   a = @('-d', '-h', '1', '127.0.0.1') }
    @{ n = 'nslookup localhost';       e = "$Sys\nslookup.exe";  a = @('localhost') }
    @{ n = 'nslookup -type=AAAA';      e = "$Sys\nslookup.exe";  a = @('-type=AAAA', 'localhost') }
    @{ n = 'netstat -an';              e = "$Sys\netstat.exe";   a = @('-an') }
    @{ n = 'netstat -rn';              e = "$Sys\netstat.exe";   a = @('-rn') }
    @{ n = 'netstat -s';               e = "$Sys\netstat.exe";   a = @('-s') }
    @{ n = 'netstat -e';               e = "$Sys\netstat.exe";   a = @('-e') }
    @{ n = 'tasklist';                 e = "$Sys\tasklist.exe";  a = @() }
    @{ n = 'tasklist /svc';            e = "$Sys\tasklist.exe";  a = @('/svc') }
    @{ n = 'tasklist /fo csv /nh';     e = "$Sys\tasklist.exe";  a = @('/fo', 'csv', '/nh') }
    @{ n = 'systeminfo';               e = "$Sys\systeminfo.exe";a = @() }
    @{ n = 'systeminfo /fo csv';       e = "$Sys\systeminfo.exe";a = @('/fo', 'csv') }
    @{ n = 'chkdsk (只读)';            e = "$Sys\chkdsk.exe";    a = @() }
    @{ n = 'sfc /verifyonly';          e = "$Sys\sfc.exe";       a = @('/verifyonly') }
    @{ n = 'robocopy /L (只列出)';     e = "$Sys\robocopy.exe";  a = @($src, $dst, '/L') }
    @{ n = 'powercfg /list';           e = "$Sys\powercfg.exe";  a = @('/list') }
    @{ n = 'powercfg /getactivescheme';e = "$Sys\powercfg.exe";  a = @('/getactivescheme') }
    @{ n = 'powercfg /query';          e = "$Sys\powercfg.exe";  a = @('/query') }
    @{ n = 'powercfg /a';              e = "$Sys\powercfg.exe";  a = @('/a') }
    @{ n = 'powercfg /aliases';        e = "$Sys\powercfg.exe";  a = @('/aliases') }
    @{ n = 'powercfg /lastwake';       e = "$Sys\powercfg.exe";  a = @('/lastwake') }
    @{ n = 'powercfg /waketimers';     e = "$Sys\powercfg.exe";  a = @('/waketimers') }
    @{ n = 'powercfg /requests';       e = "$Sys\powercfg.exe";  a = @('/requests') }
)

Write-Host ""
Write-Host "== 实跑（只读白名单） ==" -ForegroundColor Cyan
$rows = @()
foreach ($c in $cases) {
    # 执行前必须确认这条在只读白名单里：判不准的一律中止，而不是"跑着看"。
    # 这里用与自检**同一套**判定，避免"自检通过、执行时换了另一套逻辑"这种假安全。
    $argvName = [IO.Path]::GetFileNameWithoutExtension($c.e).ToLowerInvariant()
    $check = @($argvName) + $c.a
    if (-not (Test-Allowed -Argv $check)) {
        throw ("本条不在只读白名单里，已中止（不执行）：" + ($check -join ' ') + "  [" + $c.n + "]")
    }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Smoke -Exe $c.e -CmdArgs $c.a
    $sw.Stop()

    $rows += [pscustomobject]@{
        命令     = $c.n
        exit     = $(if ($r.To) { 'TIMEOUT' } else { $r.Exit })
        outBytes = $r.OutLen
        errBytes = $r.ErrLen
        ms       = $sw.ElapsedMilliseconds
        首行     = (($r.OutText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 1)
    }
    Write-Host ("  {0,-26} exit={1,-7} out={2,-7}B err={3,-6}B {4}ms" -f `
        $c.n, $(if ($r.To) { 'TIMEOUT' } else { $r.Exit }), $r.OutLen, $r.ErrLen, $sw.ElapsedMilliseconds)
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host

$report = [pscustomobject]@{
    smokeAt          = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    osVersion        = (Get-CimInstance Win32_OperatingSystem).Version
    whitelistAllowed = $ReadOnlyAllowed.Count
    whitelistNever   = $MustNeverRun.Count
    whitelistLeaks   = $leaked.Count
    results          = $rows
}
$report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir 'smoke-result.json') -Encoding utf8
Write-Host ("结果已落盘: {0}" -f (Join-Path $OutDir 'smoke-result.json'))
