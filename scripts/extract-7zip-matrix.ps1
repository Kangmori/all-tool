# 从 7-Zip 官方 CHM 的转换产物中提取"命令 x 可用开关"矩阵。
#
# 7-Zip 帮助里每个命令页都有一节 "Switches that can be used with this command"，
# 这是官方给出的、逐命令的开关白名单。把它变成机器可读的 JSON 后，CI 就能校验
# 工具包 manifest 有没有给某个命令挂上它其实不支持的开关（例如 -o 只能用于解压、
# -y 只能用于 e/x）。这类错误光靠人读文档很难发现。
#
# 前置：先反编译 CHM 并用 pandoc 转成 Markdown（见 docs/reference/README.md）
# 用法：pwsh -File scripts/extract-7zip-matrix.ps1
[CmdletBinding()]
param(
    [string]$MdDir   = (Join-Path $PSScriptRoot '..\docs\reference\7zip-md'),
    [string]$OutFile = (Join-Path $PSScriptRoot '..\docs\reference\7zip-switch-matrix.json'),
    [string]$AppVersion = '26.03'
)

$ErrorActionPreference = 'Stop'

# CHM 里命令页的文件名 -> 真实命令字母（来自 cmdline/commands/index.htm 的快速参考表）
$fileToCommand = [ordered]@{
    'add'          = @{ cmd = 'a';  title = 'Add' }
    'bench'        = @{ cmd = 'b';  title = 'Benchmark' }
    'delete'       = @{ cmd = 'd';  title = 'Delete' }
    'extract'      = @{ cmd = 'e';  title = 'Extract' }
    'extract_full' = @{ cmd = 'x';  title = 'eXtract with full paths' }
    'hash'         = @{ cmd = 'h';  title = 'Hash' }
    'list'         = @{ cmd = 'l';  title = 'List' }
    'rename'       = @{ cmd = 'rn'; title = 'Rename' }
    'test'         = @{ cmd = 't';  title = 'Test' }
    'update'       = @{ cmd = 'u';  title = 'Update' }
}

$commands = [ordered]@{}
foreach ($name in $fileToCommand.Keys) {
    $file = Join-Path (Join-Path $MdDir 'commands') "$name.md"
    if (-not (Test-Path $file)) { Write-Warning "缺少 $file，跳过"; continue }

    $switches = @()
    $mentioned = @()
    $inSection = $false
    $lines = Get-Content $file
    foreach ($line in $lines) {
        # 页面上任意位置出现过的开关（语法行、示例里都会有）。
        # 前一个字符不能是字母数字，否则 "7-Zip" 里的 "-Zip" 会被误判。
        foreach ($m in [regex]::Matches($line, '(?<![0-9A-Za-z])-(?=[A-Za-z])[A-Za-z][A-Za-z0-9]*')) {
            $mentioned += $m.Value
        }

        if (-not $inSection) {
            if ($line -match '^#{2,6}\s*Switches that can be used with this command') { $inSection = $true }
            continue
        }
        if ($line -match '^#{2,6}\s') { break }         # 下一节开始，结束
        foreach ($m in [regex]::Matches($line, '\[(-{1,2}[A-Za-z0-9]+)')) {
            $switches += $m.Groups[1].Value
        }
    }

    $commands[$fileToCommand[$name].cmd] = [ordered]@{
        title    = $fileToCommand[$name].title
        sourcePage = "cmdline/commands/$name.htm"
        switchesFrom = 'switches-section'
        switches = @($switches | Select-Object -Unique)
        mentioned = @($mentioned | Select-Object -Unique)
    }
}

# 两处例外，都是文档结构造成的，不是解析错误：
#  - b (Benchmark)：help 页没有 "Switches that can be used with this command" 一节，
#    它的开关写在 Syntax 行里（b [n] [-mmt={N}] [-md{N}] [-mm={Method}] [-mtime={N}]）。
#  - i (Show information)：CHM 里没有独立页面，只在 commands/index.htm 的快速参考表中出现。
$commands['b'].switchesFrom = 'syntax'
$commands['b'].switches = @('-mmt', '-md', '-mm', '-mtime')
$commands['i'] = [ordered]@{
    title        = 'Show information about supported formats'
    sourcePage   = 'cmdline/commands/index.htm'
    switchesFrom = 'none'
    switches     = @()
    mentioned    = @()
}

# 反向索引：开关 -> 支持它的命令
$reverse = [ordered]@{}
foreach ($cmd in $commands.Keys) {
    foreach ($sw in $commands[$cmd].switches) {
        if (-not $reverse.Contains($sw)) { $reverse[$sw] = @() }
        $reverse[$sw] += $cmd
    }
}
$reverseSorted = [ordered]@{}
foreach ($sw in ($reverse.Keys | Sort-Object)) { $reverseSorted[$sw] = @($reverse[$sw]) }

$result = [ordered]@{
    generatedBy = 'scripts/extract-7zip-matrix.ps1'
    source      = "7-zip.chm (7-Zip $AppVersion), section: Command Line Version"
    retrieved   = (Get-Date -Format 'yyyy-MM-dd')
    commands    = $commands
    switchToCommands = $reverseSorted
}

$OutFile = [IO.Path]::GetFullPath($OutFile)
$result | ConvertTo-Json -Depth 6 | Set-Content $OutFile -Encoding utf8

Write-Host "已提取 $($commands.Count) 个命令、$($reverseSorted.Count) 个开关 -> $OutFile"
$commands.Keys | ForEach-Object { "  {0,-3} {1,-22} {2,2} 个开关" -f $_, $commands[$_].title, $commands[$_].switches.Count }
