# 环境探测与漂移核对。
#
# 用途：换机器、Windows 更新、或 token 额度重置后，用一条命令确认"文档里记录的环境事实"
# 是否还成立。期望值来自 docs/ai/project-state.json 的 environment 段（只比对那里出现过的键）。
#
# 用法：
#   pwsh -File scripts/check-env.ps1                    # 核对打印
#   pwsh -File scripts/check-env.ps1 -OutFile env.json # 附带导出完整探测结果
#
# 退出码：0 = 全部一致；2 = 存在漂移或未探测到（供 CI 判断）
[CmdletBinding()]
param(
    [string]$StateFile = (Join-Path $PSScriptRoot '..\docs\ai\project-state.json'),
    [string]$OutFile
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$ErrorActionPreference = 'SilentlyContinue'

function First-Match {
    param([string]$Text, [string]$Pattern)
    if (-not $Text) { return $null }
    $m = [regex]::Match($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Get-ExePath {
    param([string]$Name)
    $c = Get-Command $Name -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}

# ---------------------------------------------------------------- 探测
$os = Get-CimInstance Win32_OperatingSystem

$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'x64' }
    'ARM64' { 'arm64' }
    'x86'   { 'x86' }
    default { $env:PROCESSOR_ARCHITECTURE }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# 工作区可写性：真写一个文件再删
$wsPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wsWritable = $false
try {
    $probe = Join-Path $wsPath '.check-env-probe'
    'probe' | Set-Content -Path $probe -ErrorAction Stop
    Remove-Item $probe -Force -ErrorAction Stop
    $wsWritable = $true
} catch { $wsWritable = $false }

# .NET SDK
$sdkRaw = (& dotnet --list-sdks 2>&1 | Out-String)
$dotnetSdk = First-Match $sdkRaw '(?m)^([0-9]+(?:\.[0-9]+)+)'

# Visual Studio
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsProduct = $vsVersion = $vsPath = $null
$winuiWorkload = $false
if (Test-Path $vswhere) {
    $vsProduct = (& $vswhere -all -prerelease -format value -property displayName | Select-Object -First 1)
    $vsVersion = (& $vswhere -all -prerelease -format value -property installationVersion | Select-Object -First 1)
    $vsPath    = (& $vswhere -all -prerelease -format value -property installationPath | Select-Object -First 1)
}
$instDir = Get-ChildItem "$env:ProgramData\Microsoft\VisualStudio\Packages\_Instances" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($instDir) {
    # 注意变量命名：PowerShell 变量名大小写不敏感，早先这里叫 $stateFile，
    # 与参数 $StateFile（本项目状态文件）冲突并把它覆盖了，导致核对基准变成 VS 的 state.json。
    $vsStateFile = Join-Path $instDir.FullName 'state.json'
    if (Test-Path $vsStateFile) {
        $vsStateText = [IO.File]::ReadAllText($vsStateFile)
        $winuiWorkload = $vsStateText.Contains('Microsoft.VisualStudio.Component.WindowsAppSdkSupport.CSharp')
    }
}

# Windows SDK（取版本号最大的一个）
$sdkVersions = @(Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Include' -Directory -ErrorAction SilentlyContinue |
                 Select-Object -ExpandProperty Name | Sort-Object)
$windowsSdk = if ($sdkVersions.Count) { $sdkVersions[-1] } else { $null }

# 各工具版本。版本正则统一用 ([0-9]+(?:\.[0-9]+)+)，
# 不能用 ([0-9.]+)——它会把 "2.55.0.windows.5" 捕获成 "2.55.0."（末尾多一个点）。
# scoop 的 --version 会经由 Write-Host 打印 bucket 信息，用 6>&1 把信息流也吃掉。
$tools = [ordered]@{
    git    = First-Match (& git --version 2>&1 | Out-String) 'git version ([0-9]+(?:\.[0-9]+)+)'
    pandoc = First-Match (& pandoc --version 2>&1 | Out-String) 'pandoc ([0-9]+(?:\.[0-9]+)+)'
    uv     = First-Match (& uv --version 2>&1 | Out-String) 'uv ([0-9]+(?:\.[0-9]+)+)'
    scoop  = First-Match (& scoop --version 2>&1 6>&1 | Out-String) 'version\s+([0-9]+(?:\.[0-9]+)+)'
    '7z'   = First-Match (& 7z i 2>&1 | Out-String) '^7-Zip\s+([0-9]+(?:\.[0-9]+)+)'
    python = First-Match (& python --version 2>&1 | Out-String) 'Python ([0-9]+(?:\.[0-9]+)+)'
    node   = First-Match (& node -v 2>&1 | Out-String) 'v?([0-9]+(?:\.[0-9]+)+)'
}
$toolPaths = [ordered]@{
    git = Get-ExePath git; pandoc = Get-ExePath pandoc; uv = Get-ExePath uv
    scoop = Get-ExePath scoop; '7z' = Get-ExePath 7z; python = Get-ExePath python
    node = Get-ExePath node; pwsh = Get-ExePath pwsh; hh = Get-ExePath hh; gh = Get-ExePath gh
}

# 网络：是否被 fake-ip 劫持（解析到非公网地址）
$sampleDomain = 'github.com'
$sampleIp = $null
$fakeIpActive = $false
$dns = Resolve-DnsName $sampleDomain -Type A -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress } | Select-Object -First 1
if ($dns) {
    $sampleIp = $dns.IPAddress
    $privateRe = '^(10\.|127\.|169\.254\.|172\.(1[6-9]|2[0-9]|3[01])\.|192\.168\.|198\.1[89]\.|28\.)'
    $fakeIpActive = [bool]([regex]::IsMatch($sampleIp, $privateRe))
}

# git 凭据是否交给了 gh
$ghHelper = (& git config --global --get-all 'credential.https://github.com.helper' 2>&1 | Out-String)
$gitUsesGh = $ghHelper -match 'gh'

$report = [ordered]@{
    capturedAt    = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    os            = [ordered]@{ caption = $os.Caption; version = $os.Version; build = "$($os.BuildNumber)"; arch = $arch }
    user          = [ordered]@{ name = "$env:USERDOMAIN\$env:USERNAME"; isAdmin = $isAdmin }
    workspace     = [ordered]@{ path = $wsPath; writable = $wsWritable }
    dotnet        = [ordered]@{ sdk = $dotnetSdk }
    visualStudio  = [ordered]@{
        product = $vsProduct; version = $vsVersion; path = $vsPath
        winuiWorkload = $winuiWorkload; windowsSdk = $windowsSdk
    }
    tools         = $tools
    toolPaths     = $toolPaths
    network       = [ordered]@{ fakeIpActive = $fakeIpActive; sampleDomain = $sampleDomain; sampleIp = $sampleIp }
    git           = [ordered]@{ credentialHelperUsesGh = $gitUsesGh }
}

# ---------------------------------------------------------------- 核对
Write-Host "== 环境核对：$wsPath ==" -ForegroundColor Cyan
Write-Host "   探测时间 $($report.capturedAt)"
Write-Host "   核对基准 $((Resolve-Path $StateFile -ErrorAction SilentlyContinue).Path)`n"

$stats = @{ Total = 0; Ok = 0; Drift = 0; Missing = 0 }

# 注意：探测结果里既有 PSCustomObject（来自 ConvertFrom-Json）也有 OrderedDictionary（本脚本构造的）。
# Hashtable/OrderedDictionary 的键【不能】用 PSObject.Properties 访问，必须走 IDictionary 分支。
function Get-PropValue {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $prop = $Object.PSObject.Properties[$Name]
    if ($prop) { return $prop.Value }
    return $null
}

function Compare-Expected {
    param($Expected, $Actual, [string]$Prefix)

    foreach ($prop in $Expected.PSObject.Properties) {
        if ($prop.Name -like '_*') { continue }
        $path = "$Prefix.$($prop.Name)"
        $expectedValue = $prop.Value
        $actualValue = Get-PropValue -Object $Actual -Name $prop.Name

        # 嵌套对象继续下潜
        if ($expectedValue -is [System.Management.Automation.PSCustomObject]) {
            Compare-Expected -Expected $expectedValue -Actual $actualValue -Prefix $path
            continue
        }

        $stats.Total++
        if ($null -eq $actualValue) {
            $stats.Missing++
            Write-Host ("[未探测到] {0}  期望={1}  实际=<空>" -f $path, $expectedValue) -ForegroundColor Yellow
        } elseif ("$expectedValue" -ne "$actualValue") {
            $stats.Drift++
            Write-Host ("[漂移]     {0}  期望={1}  实际={2}" -f $path, $expectedValue, $actualValue) -ForegroundColor Yellow
        } else {
            $stats.Ok++
            Write-Host ("[一致]     {0} = {1}" -f $path, $actualValue) -ForegroundColor Green
        }
    }
}

if (Test-Path $StateFile) {
    $state = Get-Content $StateFile -Raw | ConvertFrom-Json
    if ($state.environment) {
        Compare-Expected -Expected $state.environment -Actual $report -Prefix 'environment'
    } else {
        Write-Host "!! project-state.json 里没有 environment 段，跳过核对" -ForegroundColor Yellow
    }
} else {
    Write-Host "!! 找不到状态文件 $StateFile，只输出探测结果" -ForegroundColor Yellow
}

Write-Host "`n== 摘要 ==" -ForegroundColor Cyan
Write-Host ("   检查 {0} 项：一致 {1} / 漂移 {2} / 未探测到 {3}" -f $stats.Total, $stats.Ok, $stats.Drift, $stats.Missing)
Write-Host "   有漂移时的处理：判断是环境真的变了（更新 project-state.json 与相关文档），还是只是记录过期。" -ForegroundColor DarkGray
Write-Host "   不要为了对上记录而改环境。" -ForegroundColor DarkGray

Write-Host "`n== 额外事实（不在核对范围内） ==" -ForegroundColor Cyan
Write-Host "   VS 路径          : $vsPath"
Write-Host "   Windows SDK 列表 : $($sdkVersions -join ', ')"
Write-Host "   git 凭据走 gh    : $gitUsesGh"
Write-Host "   工具路径         :"
$toolPaths.GetEnumerator() | ForEach-Object {
    Write-Host ("     {0,-8} {1}" -f $_.Key, $(if ($_.Value) { $_.Value } else { '<未找到>' }))
}

if ($OutFile) {
    $report | ConvertTo-Json -Depth 6 | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "`n完整探测结果已写入 $OutFile" -ForegroundColor Cyan
}

exit $(if ($stats.Drift -gt 0 -or $stats.Missing -gt 0) { 2 } else { 0 })
