# 把宿主交付成一个可以随手运行的目录。
#
# 两种模式：
#   - 默认（独立）：连 .NET 与 Windows App SDK 运行时一起带上，
#     **目标机器什么都不用装**（Win10/11 x64 即可）。适合发给别人或做 Release。
#   - -FrameworkDependent：体积小，但目标机器要预装 .NET 10 桌面运行时与 Windows App Runtime。
#
# 产出结构（默认 dist\swpj\）：
#   Swpj.App.exe            宿主
#   plugins\                工具包（放在 exe 旁边，宿主会优先用这里的）
#   README-启动说明.txt      给人看的几句话
#
# 关于 `dotnet publish` 的两个坑（都已处理，别再改回去）：
#   1. publish 的输出**漏掉 Swpj.App.pri 与 *.xbf**（编译后的 XAML 资源），
#      程序会启动即崩，退出码 0xC000027B。已在 Swpj.App.csproj 里用
#      CopyXamlResourcesToPublish 目标补齐（陷阱 P22）。
#   2. 只加 --self-contained 而不加 WindowsAppSDKSelfContained 时 WinUI 也起不来；
#      两者要一起加才是真正独立（实测已能启动）。
#
# 用法：pwsh -File scripts/publish-app.ps1
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\swpj'),
    [switch]$FrameworkDependent
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\Swpj.App\Swpj.App.csproj'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path $project)) { throw "找不到项目：$project" }

Write-Host "清理旧的产物…" -ForegroundColor Cyan
if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force $OutputDirectory }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $OutputDirectory,
    '--nologo',
    '-v', 'minimal'
)

if ($FrameworkDependent) {
    $publishArgs += '--self-contained', 'false'
    $mode = '依赖运行时（需要预装 .NET 10 与 Windows App Runtime）'
} else {
    $publishArgs += '--self-contained', 'true'
    $publishArgs += '-p:WindowsAppSDKSelfContained=true'
    $mode = '独立（目标机器不需要预装任何运行时）'
}

Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "发布失败（退出码 $LASTEXITCODE）" }

# 关键文件校验：少了 XAML 资源，程序会启动即崩（P22）
foreach ($required in 'Swpj.App.exe', 'Swpj.App.pri', 'App.xbf', 'MainWindow.xbf') {
    if (-not (Test-Path (Join-Path $OutputDirectory $required))) {
        throw "交付目录缺少关键文件：$required —— 发布不完整，程序会启动即崩（见 P22）"
    }
}

# 工具包放在 exe 旁边 —— 宿主优先在这里找（见 src/Swpj.App/RepoPaths.cs）
Write-Host "复制工具包…" -ForegroundColor Cyan
$pluginsSource = Join-Path $repoRoot 'plugins'
$pluginsTarget = Join-Path $OutputDirectory 'plugins'
Copy-Item -Recurse -Force $pluginsSource $pluginsTarget

# 不把回收站带进交付目录
$trash = Join-Path $pluginsTarget '.trash'
if (Test-Path $trash) { Remove-Item -Recurse -Force $trash }

$packages = Get-ChildItem $pluginsTarget -Directory | Select-Object -ExpandProperty Name

$readme = @"
swpj —— 把命令行软件变成可点击界面
================================================

怎么运行
  双击 Swpj.App.exe（解压后直接运行，不需要安装）

怎么用
  1. 左侧「工具包」选一个软件（$(($packages -join '、')))
  2. 左侧「动作」选一个操作
  3. 中间表单填好参数 —— 执行前会显示将要运行的完整命令行，请核对
  4. 点「执行」；有进度的动作会走进度条，长任务可以点「取消」中止
  5. 带 ! 警告的动作会覆盖或不可逆地修改数据，执行前会再确认一次
  6. 需要管理员权限的动作（例如 chkdsk /f、sfc /scannow）请先用
     「文件 → 以管理员身份重新启动」打开本程序

已经带的工具包
$(($packages | ForEach-Object { "  - $_" }) -join "`n")

注意
  - 工具包是纯声明式的 YAML，不含任何代码；宿主只按声明调用你机器上已装的程序
  - 「记住上次输入」与自定义分组存在 %APPDATA%\swpj\ 下（密码永不落盘）
  - 本目录可整体拷贝到别处使用（插件就在 exe 旁边）
  - 若提示"找不到可执行文件"，说明对应软件没装或不在 PATH 里
  - 把工具包文件夹拖到「工具包」区域即可安装；右键工具包可卸载
    （移到 plugins\.trash\，不是删除，随时可以拿回来）

发布模式：$mode
"@

Set-Content -Path (Join-Path $OutputDirectory 'README-启动说明.txt') -Value $readme -Encoding utf8

$exe = Join-Path $OutputDirectory 'Swpj.App.exe'
$sizeMb = [math]::Round((Get-ChildItem $OutputDirectory -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)

Write-Host ""
Write-Host "交付完成" -ForegroundColor Green
Write-Host "  目录    : $OutputDirectory"
Write-Host "  可执行  : $exe"
Write-Host "  工具包  : $($packages -join ', ')"
Write-Host "  总体积  : ${sizeMb} MB"
Write-Host "  模式    : $mode"
