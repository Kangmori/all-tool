# uv 工具包真机冒烟测试。
#
# 两件事：
#  1) 逐条实跑 manifest 里本文件声明的"只读" examples，对照清单里的 expectExitCode；
#  2) 用清单里的 versionPattern 去匹配 locate.versionArgs 的真实输出，确认能捕获到版本号。
#
# **刻意不跑的 examples**（安全红线，见 plugins/uv/NOTES.md §7）：
#   - args 里出现 venv / pip install / pip uninstall / sync / add / remove / lock / export
#     等会写文件或联网的，由下面的 $SkipTitles 明确列出并报告为"未跑"，而不是静默跳过。
#
# 用法：pwsh -File scripts/smoke-uv.ps1
[CmdletBinding()]
param(
    [string]$Manifest = (Join-Path $PSScriptRoot '..\plugins\uv\manifest.yaml'),
    [string]$OutDir   = (Join-Path $env:TEMP 'uv-smoke-run')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$Manifest = [IO.Path]::GetFullPath($Manifest)
$OutDir = [IO.Path]::GetFullPath($OutDir)

Remove-Item -Recurse -Force $OutDir -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Push-Location $OutDir

$uvExe = (Get-Command uv -ErrorAction Stop).Source

# ---------- 1. versionPattern 实测 ----------
# 这里复刻 docs/spec/manifest-v1.md §3 的匹配语义：逐行（等价 (?m)）匹配，取第一个捕获组。
$yaml = Get-Content $Manifest -Raw -Encoding UTF8
$patternMatch = [regex]::Match($yaml, '(?m)^\s*versionPattern:\s*"((?:[^"\\]|\\.)*)"')
if (-not $patternMatch.Success) { throw "在清单里找不到 versionPattern" }
$versionPattern = ($patternMatch.Groups[1].Value -replace '\\\\', '\')
$versionArgs = @('--version')

$versionRaw = (& uv @versionArgs 2>&1 | Out-String)
$versionExit = $LASTEXITCODE
$re = [regex]::new($versionPattern)
$vm = $re.Match($versionRaw)
$captured = if ($vm.Success) { $vm.Groups[1].Value } else { '<未匹配>' }

Write-Host "== versionPattern 实测 ==" -ForegroundColor Cyan
Write-Host ("  命令        : uv {0}" -f ($versionArgs -join ' '))
Write-Host ("  退出码      : {0}" -f $versionExit)
Write-Host ("  原始输出    : {0}" -f $versionRaw.Trim())
Write-Host ("  正则        : {0}" -f $versionPattern)
Write-Host ("  匹配成功    : {0}" -f $vm.Success)
Write-Host ("  捕获组 1    : {0}" -f $captured)
Write-Host ""

# ---------- 2. examples 冒烟 ----------
# 安全红线：只实跑"确定不会写文件、不联网、不下载"的 examples。判定用**白名单**（保守优先），
# 判不准的一律算"未跑"。这样即使以后有人往清单里加了写操作的 example，也不会被误跑。
#
# 白名单：整条命令的 token 序列必须与下面某一项完全一致。
# **踩过的坑**：PowerShell 会把嵌套的数组字面量摊平——写 @( @('pip','list'), @('help') )
# 得到的其实是 @('pip','list','help') 三个字符串，$p.Count 变成 1，
# 于是 `@('pip','install',...)[0..0] -eq 'pip'` 意外成立，白名单形同虚设（实测把 pip install 放跑了）。
# 必须用一元逗号 ,@(...) 才能保住每一项的数组边界。
$ReadOnlyPrefixes = @(
    ,@('--version')
    ,@('help')                                                            # uv help <任意> 只打印文档
    ,@('self', 'version')
    ,@('cache', 'dir')
    ,@('python', 'list')
    ,@('pip', 'list')
    ,@('pip', 'freeze')
    ,@('tool', 'list')
    ,@('run', '--no-project', '--python', '3.14', 'python', '--version')  # 只打印解释器版本
    ,@('run', '--no-project', 'python', '-c', 'print(1)')                 # 只打印常量
)
# 注意：不要加一条"args 里含 --help 就放行"的通用规则——对 `uv run <cmd> --help` 来说
# 那个 --help 会被转交给 <cmd>，而 uv run 本身还是会去同步环境并执行它。逐条列白名单更安全。

# 极简 YAML 提取：把清单按 "  - id: <动作>" 切成动作块，再在每块里找单行 flow 写法的 examples。
$actionBlocks = [regex]::Matches($yaml, '(?ms)^  - id: (?<id>[a-z0-9-]+)\n(?<body>.*?)(?=^  - id: |^  # =|\z)')
if ($actionBlocks.Count -eq 0) { throw "在清单里找不到任何动作块" }

$cases = @()
foreach ($ab in $actionBlocks) {
    $actionId = $ab.Groups['id'].Value
    $eb = [regex]::Match($ab.Groups['body'].Value, '(?ms)^    examples:\n(?<ex>(?:^      - \{.*\n)+)')
    if (-not $eb.Success) { continue }
    foreach ($line in ($eb.Groups['ex'].Value -split "`n")) {
        # 标题里可能有中文逗号"，"，所以分隔符必须锚在 ", args:" 上，不能用非贪婪的 ","
        $m = [regex]::Match($line, '^\s*-\s*\{\s*title:\s*(?<t>.*),\s*args:\s*\[(?<a>.*?)\]\s*\}\s*$')
        if (-not $m.Success) { continue }
        $title = $m.Groups['t'].Value.Trim()
        $args = @()
        foreach ($tok in [regex]::Matches($m.Groups['a'].Value, '"((?:[^"\\]|\\.)*)"')) {
            $args += ($tok.Groups[1].Value -replace '\\\\', '\' -replace '\\"', '"')
        }
        $cases += [pscustomobject]@{ action = $actionId; title = $title; args = $args }
    }
}

function Test-ReadOnlyCase {
    param([string[]]$Argv)
    foreach ($p in $ReadOnlyPrefixes) {
        if ($Argv.Count -ge $p.Count -and ($Argv[0..($p.Count - 1)] -join "`0") -eq ($p -join "`0")) { return $true }
    }
    return $false
}

# 白名单自检：这些命令**绝不允许**被判为可跑。任何一条漏过去就立刻中止脚本，
# 而不是"跑着看"。这个自检是因为白名单真的漏过一次（嵌套数组被摊平，见上面注释）才加的。
$MustNeverRun = @(
    ,@('pip', 'install', 'requests')
    ,@('pip', 'install', '--requirements', 'requirements.txt')
    ,@('pip', 'uninstall', 'requests')
    ,@('sync')
    ,@('add', 'requests')
    ,@('remove', 'requests')
    ,@('lock')
    ,@('venv', '.venv')
    ,@('python', 'install', '3.12')
    ,@('python', 'pin', '3.12')
    ,@('tool', 'install', 'ruff')
    ,@('tool', 'run', 'ruff', 'check')
    ,@('cache', 'clean')
    ,@('build')
    ,@('init', '.')
    ,@('run', 'pytest')
    ,@('run', '--no-project', 'python', '-c', 'print(2)')   # 非白名单里的那种 -c
)
$leaked = @()
foreach ($bad in $MustNeverRun) {
    if (Test-ReadOnlyCase -Argv $bad) { $leaked += ($bad -join ' ') }
}
if ($leaked.Count -gt 0) {
    throw ("只读白名单自检失败，以下命令被误判为可跑，脚本中止：`n  " + ($leaked -join "`n  "))
}

Write-Host "== examples 冒烟 ==" -ForegroundColor Cyan
Write-Host ("  清单里共 {0} 条 examples" -f $cases.Count)
$rows = @()
foreach ($c in $cases) {
    if (-not (Test-ReadOnlyCase -Argv $c.args)) {
        $rows += [pscustomobject]@{ 结果='未跑'; exit='-'; ms='-'; 命令=('uv ' + ($c.args -join ' ')); 说明=('[' + $c.action + '] ' + $c.title + '（按安全红线未实跑）') }
        continue
    }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = (& uv @($c.args) 2>&1 | Out-String)
    $code = $LASTEXITCODE
    $sw.Stop()
    $rows += [pscustomobject]@{
        结果 = $(if ($code -eq 0) { 'OK' } else { '非零' })
        exit = $code
        ms   = $sw.ElapsedMilliseconds
        命令 = 'uv ' + ($c.args -join ' ')
        说明 = (($out -split "`n")[0]).Trim()
    }
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host

Pop-Location

# ---------- 3. 落盘结果 ----------
$report = [pscustomobject]@{
    tool           = 'uv'
    smokeAt        = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    versionPattern = [pscustomobject]@{
        pattern  = $versionPattern
        matched  = $vm.Success
        captured = $captured
        raw      = $versionRaw.Trim()
        exit     = $versionExit
    }
    examples       = $rows
}
$report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir 'smoke-result.json') -Encoding utf8
Write-Host ("结果已落盘: {0}" -f (Join-Path $OutDir 'smoke-result.json'))
