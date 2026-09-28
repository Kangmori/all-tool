# 抓取 Windows 自带命令的 `/?` 帮助快照，并顺带实测"输出到 stdout 还是 stderr、编码是什么"。
#
# 为什么用 .NET Process 而不是 `& cmd /?`：
#   1) PowerShell 的 `&` 会把子进程的 stdout 与 stderr 按**主机**编码解码后再交给管道，
#      原始字节在这一步就丢了。要判断编码（GBK / UTF-8 / UTF-16LE）必须看原始字节。
#   2) 需要分别记录 stdout 与 stderr —— **很多 Windows 命令把帮助写到 stderr**
#      （实测：nslookup /? 与 netstat /? 都是 stderr），这直接影响宿主能不能展示它。
#   3) 需要超时。**GUI 子系统的程序（cleanmgr.exe）带 /? 会弹一个 Windows 对话框并
#      一直等用户点确定**——不加超时脚本会永久挂住（实测挂到 300 秒被 harness 挪进后台）。
#
# 用法：pwsh -File scripts/fetch-win-help.ps1
#
# 产物：docs/reference/win-help/<命令>.txt      （解码后的帮助文本，两个流拼在一起）
#       docs/reference/win-help/_meta.json      （每个命令的 exit code / 流 / 字节数 / 编码判定）
#
# 注意：docs/reference/win-help/ 已加入 .gitignore —— 这些是微软的文本，
# 本项目转为公开仓库后不能随仓库发布。见 plugins/win-net/NOTES.md 的说明。
[CmdletBinding()]
param(
    [string[]]$Commands = @(
        'ping', 'ipconfig', 'tracert', 'nslookup', 'netstat', 'tasklist',
        'systeminfo', 'chkdsk', 'sfc', 'robocopy', 'cleanmgr', 'powercfg'
    ),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\reference\win-help'),
    # GUI 程序给 8 秒足够弹出对话框；控制台程序 /? 都是毫秒级返回
    [int]$TimeoutSeconds = 8
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 本机 OEM 代码页（中文 Windows = 936）。**不要用 [Text.Encoding]::Default**：
# 在 .NET Core / pwsh 7 上它的 CodePage 是 65001，那是 UTF-8，不是 OEM。
$OemCodePage = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage').OEMCP
$OemEnc = [Text.Encoding]::GetEncoding([int]$OemCodePage)

function Test-IsValidUtf8 {
    # 严格手写 UTF-8 合法性判定。
    # .NET 的 UTF8Encoding(throwOnInvalidBytes=true) 在这里**不能用**：
    # 实测它对 GBK 字节 d3 c3 不抛异常，而是静默替换成 U+FFFD（宽松解码），
    # 于是所有 GBK 输出都会被误判成 UTF-8。
    param([byte[]]$Bytes)
    $i = 0
    while ($i -lt $Bytes.Length) {
        $b = $Bytes[$i]
        if ($b -lt 0x80) { $i++; continue }
        if ($b -ge 0xC2 -and $b -le 0xDF) { $need = 1 }
        elseif ($b -ge 0xE0 -and $b -le 0xEF) { $need = 2 }
        elseif ($b -ge 0xF0 -and $b -le 0xF4) { $need = 3 }
        else { return $false }
        if ($i + $need -ge $Bytes.Length) { return $false }
        for ($k = 1; $k -le $need; $k++) {
            if ($Bytes[$i + $k] -lt 0x80 -or $Bytes[$i + $k] -gt 0xBF) { return $false }
        }
        # 过长编码（UTF-8 里不合法）
        if ($need -eq 2 -and $b -eq 0xE0 -and $Bytes[$i + 1] -lt 0xA0) { return $false }
        if ($need -eq 3 -and $b -eq 0xF0 -and $Bytes[$i + 1] -lt 0x90) { return $false }
        $i += $need + 1
    }
    return $true
}

function Get-EncodingVerdict {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return 'empty' }

    $hasNonAscii = $false
    foreach ($b in $Bytes) { if ($b -gt 0x7f) { $hasNonAscii = $true; break } }
    if (-not $hasNonAscii) { return 'ascii' }

    # UTF-16LE：**必须按字节长度是偶数来判断**，不能只看"ASCII 后面跟 00"。
    # 实测教训：sfc.exe 的整段输出都是 UTF-16LE（例如 `0d 00 0d 00 0a 00 3a 4e 86 4e …`
    # 解出来是"为了使用 sfc 工具，你必须作为管理员运行控制台会话。"），
    # 但其中 `3a 4e`（"为"）、`86 4e`（"了"）这类汉字对里第二个字节不是 00，
    # 于是"数 00 的个数"那种判定会把整段误判成 cp936，输出全是乱码。
    if ($Bytes.Length % 2 -eq 0) {
        $zerosInHighByte = 0
        $pairs = [Math]::Min($Bytes.Length / 2, 400)
        for ($k = 0; $k -lt $pairs; $k++) {
            if ($Bytes[($k * 2) + 1] -eq 0) { $zerosInHighByte++ }
        }
        # 纯 ASCII 文本按 UTF-16LE 看也会有一半的高字节是 0，
        # 所以这条只在前面的 ascii 判定失败后才生效，阈值取 30% 足够区分。
        if ($zerosInHighByte -gt ($pairs * 0.3)) { return 'utf-16le' }
    }

    if (Test-IsValidUtf8 -Bytes $Bytes) { return 'utf-8' }
    return "oem/cp$OemCodePage"
}

function Convert-Bytes {
    param([byte[]]$Bytes, [string]$Verdict)
    if ($Bytes.Length -eq 0) { return '' }
    if ($Verdict -eq 'utf-16le') { return [Text.Encoding]::Unicode.GetString($Bytes) }
    if ($Verdict -eq 'utf-8' -or $Verdict -eq 'ascii') { return [Text.Encoding]::UTF8.GetString($Bytes) }
    return $OemEnc.GetString($Bytes)
}

function Invoke-Capture {
    param([string]$Exe, [string[]]$Arguments, [int]$TimeoutSec)

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($a in $Arguments) { $null = $psi.ArgumentList.Add($a) }

    $p = [Diagnostics.Process]::Start($psi)
    # 必须用 BaseStream 的字节版：StandardOutput.ReadToEnd() 会按宿主编码解码，原始字节就丢了
    $outMs = [IO.MemoryStream]::new()
    $errMs = [IO.MemoryStream]::new()
    $t1 = $p.StandardOutput.BaseStream.CopyToAsync($outMs)
    $t2 = $p.StandardError.BaseStream.CopyToAsync($errMs)

    $timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
    $windowTitle = $null
    if ($timedOut) {
        # GUI 程序：/ ? 帮的是 Windows 对话框，进程一直等用户点「确定」。
        # 把对话框标题抓下来当证据，然后杀掉，绝不让它留在用户桌面上。
        try { $p.Refresh(); $windowTitle = $p.MainWindowTitle } catch { }
        try { $p.Kill($true) } catch { }
        $null = $p.WaitForExit(3000)
    }
    [Threading.Tasks.Task]::WaitAll(@($t1, $t2))
    $exit = $(if ($timedOut) { $null } else { $p.ExitCode })
    $p.Dispose()

    return [pscustomobject]@{
        ExitCode    = $exit
        TimedOut    = $timedOut
        WindowTitle = $windowTitle
        StdOut      = $outMs.ToArray()
        StdErr      = $errMs.ToArray()
    }
}

$records = @()

foreach ($name in $Commands) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Warning "找不到命令 $name"
        continue
    }
    if ($cmd.CommandType -ne 'Application') {
        Write-Warning "$name 不是可执行文件（$($cmd.CommandType)），本批不做"
        continue
    }

    $exe = $cmd.Source
    $r = Invoke-Capture -Exe $exe -Arguments @('/?') -TimeoutSec $TimeoutSeconds

    $outVerdict = Get-EncodingVerdict -Bytes $r.StdOut
    $errVerdict = Get-EncodingVerdict -Bytes $r.StdErr
    $outText = Convert-Bytes -Bytes $r.StdOut -Verdict $outVerdict
    $errText = Convert-Bytes -Bytes $r.StdErr -Verdict $errVerdict

    $helperStream = if ($r.StdOut.Length -gt 0 -and $r.StdErr.Length -gt 0) { 'both' }
                    elseif ($r.StdErr.Length -gt 0) { 'stderr' }
                    elseif ($r.StdOut.Length -gt 0) { 'stdout' }
                    else { 'none' }

    $sb = [Text.StringBuilder]::new()
    $null = $sb.AppendLine("### $name /?  ——  抓取于 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $null = $sb.AppendLine("### exe: $exe")
    $null = $sb.AppendLine("### exit code: $(if ($r.TimedOut) { '<超时未退出，已强杀>' } else { $r.ExitCode })")
    if ($r.WindowTitle) { $null = $sb.AppendLine("### 超时时的窗口标题: $($r.WindowTitle)") }
    $null = $sb.AppendLine("### 帮助写在: $helperStream")
    $null = $sb.AppendLine("### stdout: $($r.StdOut.Length) 字节, 编码判定 $outVerdict")
    $null = $sb.AppendLine("### stderr: $($r.StdErr.Length) 字节, 编码判定 $errVerdict")
    $null = $sb.AppendLine("### ===== STDOUT =====")
    $null = $sb.AppendLine($outText)
    $null = $sb.AppendLine("### ===== STDERR =====")
    $null = $sb.AppendLine($errText)
    Set-Content -Path (Join-Path $OutDir "$name.txt") -Value $sb.ToString() -Encoding utf8

    $records += [pscustomobject]@{
        command     = $name
        exe         = $exe
        fileVersion = $cmd.Version.ToString()
        exitCode    = $r.ExitCode
        timedOut    = $r.TimedOut
        windowTitle = $r.WindowTitle
        helpStream  = $helperStream
        stdoutBytes = $r.StdOut.Length
        stderrBytes = $r.StdErr.Length
        stdoutEnc   = $outVerdict
        stderrEnc   = $errVerdict
    }
    Write-Host ("{0,-12} exit={1,-6} {2,-7} stdout={3,-7} stderr={4,-7} {5}" -f `
        $name, $(if ($r.TimedOut) { 'TIMEOUT' } else { $r.ExitCode }), $helperStream,
        $r.StdOut.Length, $r.StdErr.Length, $outVerdict)
}

$meta = [pscustomobject]@{
    capturedAt   = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    osVersion    = (Get-CimInstance Win32_OperatingSystem).Version
    oemCodePage  = $OemCodePage
    note         = '本目录是微软的帮助文本快照，已加入 .gitignore，不随仓库发布。重新生成：pwsh -File scripts/fetch-win-help.ps1'
    commands     = $records
}
$meta | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir '_meta.json') -Encoding utf8

Write-Host ""
Write-Host "快照目录: $OutDir"
Write-Host ("命令数: {0}" -f $records.Count)
