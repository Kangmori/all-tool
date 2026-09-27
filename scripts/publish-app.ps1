# 把宿主交付成一个可以随手运行的目录，用于人工测试。
#
# 产出结构（默认 dist\swpj\）：
#   Swpj.App.exe            宿主
#   plugins\                工具包（放在 exe 旁边，宿主会优先用这里的）
#   README-启动说明.txt      给人看的几句话
#
# **为什么不用 dotnet publish**（踩过的坑，别再试）：
#   publish 的输出里会**漏掉 Swpj.App.pri 与 .xbf**（编译后的 XAML 资源），
#   程序启动即崩，退出码 0xC000027B（STOWED_EXCEPTION），WER 指向
#   Microsoft.UI.Xaml.dll + combase.dll 0x80004005(E_FAIL)。
#   实测对照：`dotnet build -c Release` 的产物能正常启动，publish 的不能；
#   两者差别就是那几个资源文件（另有 publish 会多塞 runtimes\win-arm64 之类的无用件）。
#   所以交付走"构建 + 拷贝"这条路。
#
# 交付前提：目标机器已装 .NET 10 桌面运行时与 Windows App Runtime（都是常见组件）。
#
# 用法：pwsh -File scripts/publish-app.ps1
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist\swpj')
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\Swpj.App\Swpj.App.csproj'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

Write-Host "构建（$Configuration）…" -ForegroundColor Cyan
& dotnet build $project -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败（退出码 $LASTEXITCODE）" }

# WinUI 的 TFM 目录名固定是这个形态；用通配符找，免得 TFM 升级后脚本失效
$buildRoot = Get-ChildItem (Join-Path $repoRoot 'src\Swpj.App\bin') -Directory |
    Where-Object Name -eq $Configuration |
    ForEach-Object { Get-ChildItem $_.FullName -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Swpj.App.exe') } } |
    Select-Object -First 1

if (-not $buildRoot) { throw "找不到构建产物（bin\$Configuration\<tfm>\Swpj.App.exe）" }
Write-Host "构建产物：$($buildRoot.FullName)" -ForegroundColor DarkGray

if (Test-Path $OutputDirectory) { Remove-Item -Recurse -Force $OutputDirectory }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "拷贝程序文件…" -ForegroundColor Cyan
Copy-Item -Path (Join-Path $buildRoot.FullName '*') -Destination $OutputDirectory -Recurse -Force

# 关键资源靠这一步带过来；若缺失程序会启动即崩（见文件头说明）
foreach ($required in 'Swpj.App.exe', 'Swpj.App.pri', 'App.xbf', 'MainWindow.xbf') {
    if (-not (Test-Path (Join-Path $OutputDirectory $required))) {
        throw "交付目录缺少关键文件：$required —— 程序会启动即崩，请检查构建是否完整"
    }
}

Write-Host "复制工具包…" -ForegroundColor Cyan
Copy-Item -Recurse -Force (Join-Path $repoRoot 'plugins') (Join-Path $OutputDirectory 'plugins')

$packages = Get-ChildItem (Join-Path $OutputDirectory 'plugins') -Directory | Select-Object -ExpandProperty Name

$readme = @"
swpj —— 把已安装的命令行软件变成可点击界面
================================================

怎么运行
  双击 Swpj.App.exe

怎么用
  1. 左侧「工具包」选一个软件（$(($packages -join '、'))）
  2. 左侧「动作」选一个操作
  3. 中间表单填好参数 —— 执行前会显示将要运行的完整命令行，请核对
  4. 点「执行」；有进度的动作会走进度条，长任务可以点「取消」中止
  5. 带 ! 警告的动作会覆盖或不可逆地修改数据，执行前会再确认一次

已经带的工具包
$(($packages | ForEach-Object { "  - $_" }) -join "`n")

注意
  - 工具包是纯声明式的 YAML，不含任何代码；宿主只按声明调用你机器上已装的程序
  - 「记住上次输入」的记录在 %APPDATA%\swpj\last-values.json（密码永不落盘）
  - 本目录可整体拷贝到别处使用（插件就在 exe 旁边）
  - 若提示"找不到可执行文件"，说明对应软件没装或不在 PATH 里
  - 需要预装：.NET 10 桌面运行时 + Windows App Runtime
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
Write-Host "  前提    : .NET 10 桌面运行时 + Windows App Runtime"
