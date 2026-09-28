# 从抓下来的 Microsoft Learn 页面里提取"官方文档到底写了哪些开关"，以及每个小节的可引用锚点。
#
# 为什么要有这一步：R1（不发明参数）要求清单里每个开关都能指出出处。
# 光有 URL 不够——审阅者要能在页面上找到那个开关。这个脚本把页面里的
#   表格行（<td> 里的开关写法） 与  小节标题 id（可做 URL 锚点）
# 提取成 JSON，写清单时逐条对照。
#
# 用法：pwsh -File scripts/extract-win-docs-switches.ps1
# 产物：docs/reference/win-docs/_switches.json
[CmdletBinding()]
param(
    [string]$DocsDir = (Join-Path $PSScriptRoot '..\docs\reference\win-docs')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$DocsDir = [IO.Path]::GetFullPath($DocsDir)

function ConvertFrom-HtmlText {
    param([string]$Html)
    $t = $Html -replace '(?s)<script.*?</script>', ' '
    $t = $t -replace '(?s)<style.*?</style>', ' '
    $t = $t -replace '<[^>]+>', ''
    $t = $t -replace '&lt;', '<' -replace '&gt;', '>' -replace '&amp;', '&'
    $t = $t -replace '&quot;', '"' -replace '&#39;', "'" -replace '&nbsp;', ' '
    $t = $t -replace '&#x([0-9A-Fa-f]+);', '' -replace '&#(\d+);', ''
    return ($t -replace '\s+', ' ').Trim()
}

$result = [ordered]@{}

foreach ($file in (Get-ChildItem $DocsDir -Filter *.html | Sort-Object Name)) {
    $html = Get-Content $file.FullName -Raw -Encoding UTF8

    # 小节标题 + 锚点 id
    $headings = @()
    foreach ($m in [regex]::Matches($html, '(?s)<h([23])[^>]*\bid="([^"]+)"[^>]*>(.*?)</h\1>')) {
        $headings += [pscustomobject]@{
            level = [int]$m.Groups[1].Value
            id    = $m.Groups[2].Value
            text  = (ConvertFrom-HtmlText $m.Groups[3].Value)
        }
    }

    # 表格行的第一列通常是开关写法；这是官方文档里最结构化的开关清单
    $rows = @()
    foreach ($tr in [regex]::Matches($html, '(?s)<tr[^>]*>(.*?)</tr>')) {
        $cells = @()
        foreach ($td in [regex]::Matches($tr.Groups[1].Value, '(?s)<t[dh][^>]*>(.*?)</t[dh]>')) {
            $cells += (ConvertFrom-HtmlText $td.Groups[1].Value)
        }
        if ($cells.Count -ge 2) {
            $rows += [pscustomobject]@{ switch = $cells[0]; meaning = ($cells[1..($cells.Count - 1)] -join ' | ') }
        }
    }

    # 语法块（<pre><code>）里也含开关
    $syntax = @()
    foreach ($m in [regex]::Matches($html, '(?s)<pre[^>]*>\s*<code[^>]*>(.*?)</code>')) {
        $syntax += (ConvertFrom-HtmlText $m.Groups[1].Value)
    }

    $result[$file.BaseName] = [pscustomobject]@{
        file       = $file.Name
        headings   = $headings
        tableRows  = $rows
        syntax     = $syntax
    }

    Write-Host ("{0,-14} 标题 {1,-3} 表格行 {2,-4} 语法块 {3}" -f $file.BaseName, $headings.Count, $rows.Count, $syntax.Count)
}

$result | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $DocsDir '_switches.json') -Encoding utf8
Write-Host ""
Write-Host "已落盘: $(Join-Path $DocsDir '_switches.json')"
