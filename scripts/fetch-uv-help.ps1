# 抓取本机 uv 的命令行帮助，作为"与安装版本匹配"的权威参数来源。
#
# 为什么用 `uv <cmd> --help` 而不是 `uv help <cmd>`：
#   `uv help <cmd>` 打印的是 docs 站上的长文档，`uv <cmd> --help` 打印的是该二进制内置的
#   简洁帮助（每个开关都带默认值与取值说明）。两者内容不同，本工具包以 **`--help` 为准**
#   （它与已安装版本严格对应，且不需要联网），需要长解释时再查 docs/reference/uv-docs/。
#   实测两者都在，且退出码均为 0。
#
# 嵌套子命令用空格分隔（如 `pip install`），文件名里的空格换成 '-'。
#
# 用法：pwsh -File scripts/fetch-uv-help.ps1
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\reference\uv-help')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$uvExe = (Get-Command uv -ErrorAction Stop).Source

# `uv --version` 形如：uv 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)
$versionRaw = (& uv --version 2>&1 | Out-String).Trim()
$versionMatch = [regex]::Match($versionRaw, '^uv\s+(\d+\.\d+\.\d+)')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { 'unknown' }

# 顶层命令（取自 `uv --help` 的 Commands: 一节）。
# 注意：`uv help` 列出的命令比 `uv --help` 多一个 generate-shell-completion（见 NOTES.md）。
$commands = @(
    'auth', 'run', 'init', 'add', 'remove', 'version', 'sync', 'lock', 'export', 'tree',
    'format', 'audit', 'tool', 'python', 'pip', 'venv', 'build', 'publish', 'cache', 'self',
    'generate-shell-completion', 'help'
)

# 二级子命令：只抓与工具包相关、且确实存在的那些。
# 抓不到的会在下面被显式报告出来（P14：不要把失败静默吞掉）。
$subCommands = @(
    'pip compile', 'pip sync', 'pip install', 'pip uninstall', 'pip freeze', 'pip list',
    'pip show', 'pip tree', 'pip check', 'pip download', 'pip check',
    'python install', 'python list', 'python find', 'python pin', 'python uninstall',
    'python update-shell',
    'tool install', 'tool run', 'tool list', 'tool uninstall', 'tool upgrade', 'tool dir',
    'cache clean', 'cache dir', 'cache prune', 'cache size',
    'self version', 'self update'
) | Select-Object -Unique

function Get-HelpText {
    param([string]$Cmd)
    $argv = @()
    if ($Cmd) { $argv += $Cmd.Split(' ') }
    $argv += '--help'
    $text = (& uv @argv 2>&1 | Out-String)
    $text = $text -replace "`e\[[0-9;]*m", ''   # 去掉可能的 ANSI 颜色码
    return ($text.Trim() + "`n")
}

$index = @()
$missing = @()

foreach ($cmd in $commands) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $text = Get-HelpText $cmd
    $code = $LASTEXITCODE
    $sw.Stop()
    if ($code -ne 0) { $missing += "$cmd (exit=$code)"; continue }

    # 首行是命令的一句话说明（uv 的帮助首行恒为摘要，无前导空行——已实测）
    $summary = ($text -split "`n")[0].Trim()
    $file = ($cmd -replace ' ', '-') + '.txt'
    $text | Set-Content (Join-Path $OutDir $file) -Encoding utf8
    $index += [pscustomobject]@{
        command = $cmd
        summary = $summary
        file    = $file
        exit    = $code
        ms      = $sw.ElapsedMilliseconds
    }
}

foreach ($cmd in $subCommands) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $text = Get-HelpText $cmd
    $code = $LASTEXITCODE
    $sw.Stop()
    if ($code -ne 0) { $missing += "$cmd (exit=$code)"; continue }

    $summary = ($text -split "`n")[0].Trim()
    $file = ($cmd -replace ' ', '-') + '.txt'
    $text | Set-Content (Join-Path $OutDir $file) -Encoding utf8
    $index += [pscustomobject]@{
        command = $cmd
        summary = $summary
        file    = $file
        exit    = $code
        ms      = $sw.ElapsedMilliseconds
    }
}

# 顶层帮助另存一份（含全局开关，工具包运行时字段的依据之一）
Get-HelpText '' | Set-Content (Join-Path $OutDir '_uv.txt') -Encoding utf8

$index | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutDir 'index.json') -Encoding utf8

[pscustomobject]@{
    tool         = 'uv'
    version      = $version
    versionRaw   = $versionRaw
    executable   = $uvExe
    retrieved    = (Get-Date -Format 'yyyy-MM-dd')
    commandCount = @($index).Count
    missing      = @($missing)
} | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutDir '_meta.json') -Encoding utf8

Write-Host "uv $version -> 已保存 $(@($index).Count) 个命令的帮助到 $OutDir"
if ($missing.Count) {
    Write-Host "以下命令抓取失败（已记录在 _meta.json）：" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  $_" }
}
