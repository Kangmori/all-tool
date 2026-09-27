# 抓取本机 scoop 的帮助输出，作为"与安装版本匹配"的权威参数来源。
#
# 为什么不用 wiki 上的命令列表：wiki 的 Commands 页面本身就是 `scoop help` 的转抄，
# 而本机 scoop 的帮助与已安装版本严格对应，且 `scoop help` 返回的是结构化对象
# （PSCustomObject: Command / Summary），比解析 wiki 文本更可靠。
#
# 用法：pwsh -File scripts/fetch-scoop-help.ps1
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\reference\scoop-help')
)

$ErrorActionPreference = 'Stop'
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$scoopExe = (Get-Command scoop -ErrorAction Stop).Source

# 注意：`scoop --version` 会顺带打印 bucket 更新信息（且是 Write-Host，无法用管道拦下），
# 所以从输出里正则提取干净的版本号，而不是直接取整段文本。
$versionRaw = (& scoop --version 2>&1 | Out-String)
$versionMatch = [regex]::Match($versionRaw, 'version\s+(\d+\.\d+\.\d+)')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { 'unknown' }

# `scoop help` 返回对象数组，每项含 Command / Summary
$entries = & scoop help
$index = foreach ($e in $entries) {
    [pscustomobject]@{
        command = $e.Command
        summary = ("$($e.Summary)").Trim()
    }
}

$index | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutDir 'index.json') -Encoding utf8

foreach ($e in $index) {
    $text = (& scoop help $e.command 2>&1 | Out-String)
    $text = $text -replace "`e\[[0-9;]*m", ''   # 去掉 ANSI 颜色码
    ($text.Trim() + "`n") | Set-Content (Join-Path $OutDir ($e.command + '.txt')) -Encoding utf8
}

[pscustomobject]@{
    tool         = 'scoop'
    version      = $version
    executable   = $scoopExe
    retrieved    = (Get-Date -Format 'yyyy-MM-dd')
    commandCount = @($index).Count
} | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $OutDir '_meta.json') -Encoding utf8

Write-Host "scoop $version -> 已保存 $(@($index).Count) 个命令的帮助到 $OutDir"
