# 文档漂移检测。
#
# 为什么需要它：工具包的参数知识是从官方文档抄下来的，而官方文档会变。
# 慢节奏开发下，最大的风险不是写错，而是"三个月后清单悄悄过期了却没人发现"。
#
# 它做两件事：
#   1. 版本探测：问上游"最新版本是多少"，与清单里的 appVersion 比对。
#   2. 文档探测：抓上游文档页，与仓库里的快照做归一化哈希比对。
#
# 用法：
#   pwsh -File scripts/check-doc-drift.ps1                      # 打印报告，有漂移返回退出码 2
#   pwsh -File scripts/check-doc-drift.ps1 -ReportFile r.md     # 同时写 Markdown 报告（CI 用它当 issue 正文）
#
# 观测点清单在 docs/ai/drift-watch.json。
[CmdletBinding()]
param(
    [string]$WatchFile = (Join-Path $PSScriptRoot '..\docs\ai\drift-watch.json'),
    [string]$PluginsRoot = (Join-Path $PSScriptRoot '..\plugins'),
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..'),
    [string]$ReportFile
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'

$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$drifts = [System.Collections.Generic.List[string]]::new()
$lines = [System.Collections.Generic.List[string]]::new()

function Add-Line([string]$text, [string]$color = 'Gray') {
    $lines.Add($text)
    Write-Host $text -ForegroundColor $color
}

function Get-Sha256Short([string]$text) {
    # 归一化换行再哈希：本地 clone 可能是 CRLF，上游 raw 是 LF，不归一化会误报。
    $normalized = ($text -replace "`r`n", "`n").Trim()
    $bytes = [Text.Encoding]::UTF8.GetBytes($normalized)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return ([BitConverter]::ToString($hash) -replace '-', '').Substring(0, 16)
}

function Get-ManifestAppVersion([string]$pluginId) {
    # 用正则读 appVersion，而不是引入 YAML 解析器：
    # 这个脚本要在 GitHub runner 上只靠 pwsh 就能跑。清单格式是我们自己的，稳态可控。
    $manifest = Join-Path (Join-Path $PluginsRoot $pluginId) 'manifest.yaml'
    if (-not (Test-Path $manifest)) { return $null }
    $match = [regex]::Match((Get-Content $manifest -Raw), '(?m)^appVersion:\s*"?([^"\r\n]+?)"?\s*$')
    if ($match.Success) { return $match.Groups[1].Value.Trim('"', ' ') }
    return $null
}

Add-Line "# 文档漂移检测报告" 'Cyan'
Add-Line ""
Add-Line "生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Add-Line "观测点清单：$([IO.Path]::GetFileName($WatchFile))"
Add-Line ""

$watch = Get-Content $WatchFile -Raw | ConvertFrom-Json

foreach ($tool in $watch.tools) {
    Add-Line "## $($tool.displayName)（plugins/$($tool.plugin)）" 'Cyan'

    $declared = Get-ManifestAppVersion $tool.plugin
    if (-not $declared) {
        # 刻意把"检查器读不到东西"也算成需要处理的情况。
        # 否则脚本坏了会静默地报告"没有漂移"，比漏报更危险。
        $message = "读不到 plugins/$($tool.plugin)/manifest.yaml 里的 appVersion，检测无法进行"
        $drifts.Add("$($tool.displayName)：$message")
        Add-Line "  [检测失败] $message" 'Yellow'
        Add-Line ""
        continue
    }

    # ---------------- 版本探测 ----------------
    $latest = $null
    $probeError = $null

    try {
        switch ($tool.versionProbe.kind) {
            'http' {
                $response = Invoke-WebRequest $tool.versionProbe.url -UseBasicParsing -TimeoutSec 30
                $match = [regex]::Match($response.Content, $tool.versionProbe.pattern)
                if ($match.Success) { $latest = $match.Groups[1].Value } else { $probeError = "正则没匹配上：$($tool.versionProbe.pattern)" }
            }
            'gitTags' {
                $raw = & git ls-remote --tags $tool.versionProbe.url 2>&1
                if ($LASTEXITCODE -ne 0) { $probeError = "git ls-remote 失败：$raw" }
                else {
                    $versions = $raw |
                        ForEach-Object { ($_ -split 'refs/tags/')[-1] } |
                        Where-Object { $_ -match '^v?[0-9]+\.[0-9]+\.[0-9]+$' } |
                        ForEach-Object { $_ -replace '^v', '' }
                    if ($versions) {
                        $latest = ($versions | Sort-Object { [version]$_ } -Descending | Select-Object -First 1)
                    } else {
                        $probeError = "没找到形如 vX.Y.Z 的 tag"
                    }
                }
            }
            default { $probeError = "未知的探测方式：$($tool.versionProbe.kind)" }
        }
    } catch {
        $probeError = $_.Exception.Message
    }

    if ($probeError) {
        Add-Line "  [探测失败] $probeError" 'Yellow'
    } elseif ($latest -eq $declared) {
        Add-Line "  [版本一致] 清单 $declared = 上游 $latest" 'Green'
    } else {
        $message = "上游版本是 $latest，清单里写的是 $declared —— 需要复核该工具包的参数是否随版本变化"
        $drifts.Add("$($tool.displayName)：$message")
        Add-Line "  [版本漂移] $message" 'Yellow'
    }

    # ---------------- 文档探测 ----------------
    foreach ($doc in $tool.docs) {
        # 没有 snapshot 的条目只做登记：本项目转公开后不再随仓库分发第三方文档，
        # 所以这类条目跳过快照比对（否则每周 CI 都会报一串"快照文件不存在"的噪声）。
        if (-not $doc.snapshot) {
            Add-Line "  [仅登记] $($doc.url)" 'Gray'
            continue
        }

        $snapshotPath = Join-Path $RepoRoot $doc.snapshot.Replace('/', [IO.Path]::DirectorySeparatorChar)

        if (-not (Test-Path $snapshotPath)) {
            Add-Line "  [快照缺失] $($doc.snapshot)" 'Yellow'
            $drifts.Add("$($tool.displayName)：快照文件不存在 $($doc.snapshot)")
            continue
        }

        try {
            $upstream = (Invoke-WebRequest $doc.url -UseBasicParsing -TimeoutSec 30).Content
            $local = Get-Content $snapshotPath -Raw

            $upstreamHash = Get-Sha256Short $upstream
            $localHash = Get-Sha256Short $local

            if ($upstreamHash -eq $localHash) {
                Add-Line "  [文档一致] $([IO.Path]::GetFileName($doc.snapshot))  ($upstreamHash)" 'Green'
            } else {
                $message = "上游文档已变化：$($doc.url)`n      本地快照 $($doc.snapshot)  上游 $upstreamHash / 本地 $localHash"
                $drifts.Add("$($tool.displayName)：$($doc.snapshot) 与上游不一致")
                Add-Line "  [文档漂移] $([IO.Path]::GetFileName($doc.snapshot))  上游 $upstreamHash ≠ 本地 $localHash" 'Yellow'
                Add-Line "             $($doc.url)" 'DarkGray'
            }
        } catch {
            Add-Line "  [探测失败] $($doc.url) —— $($_.Exception.Message)" 'Yellow'
        }
    }

    Add-Line ""
}

# ---------------- 结论 ----------------
Add-Line "## 结论" 'Cyan'
if ($drifts.Count -eq 0) {
    Add-Line "没有发现漂移：上游版本与已收录的文档快照都与仓库一致。" 'Green'
} else {
    Add-Line "发现 $($drifts.Count) 处漂移：" 'Yellow'
    foreach ($d in $drifts) { Add-Line "  - $d" 'Yellow' }
    Add-Line ""
    Add-Line "处理建议：读出变化 → 判断是否影响清单 → 若是则更新快照与清单（并重跑冒烟）→ 提交。" 'DarkGray'
    Add-Line "刷新快照的命令见 docs/reference/README.md。" 'DarkGray'
}

if ($ReportFile) {
    $ReportFile = [IO.Path]::GetFullPath($ReportFile)
    New-Item -ItemType Directory -Force -Path (Split-Path $ReportFile) | Out-Null
    Set-Content -Path $ReportFile -Value ($lines -join "`n") -Encoding utf8
    Write-Host "`n报告已写入 $ReportFile" -ForegroundColor Cyan
}

exit $(if ($drifts.Count -gt 0) { 2 } else { 0 })
