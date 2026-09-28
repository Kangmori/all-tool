# 抓取 Windows 自带命令的 `/?` 帮助快照，并顺带实测"输出到 stdout 还是 stderr、编码是什么"。
#
# 为什么用 .NET Process 而不是 `& cmd /?`：
#   1) PowerShell 的 `&` 会把子进程的 stdout 与 stderr 按**主机**编码解码后再交给管道，
#      原始字节在这一步就丢了。要判断编码（GBK / UTF-8 / UTF-16LE）必须看原始字节。
#   2) 需要分别记录 stdout 与 stderr——很多 Windows 命令把帮助写到 stderr，
#      这直接影响宿主能不能展示它。
#
# 用法：pwsh -File scripts/fetch-win-help.ps1
#
# 产物：docs/reference/win-help/<命令>.txt      （解码后的帮助文本，两个流拼在一起）
#       docs/reference/win-help/_meta.json      （每个命令的 exit code / 流 / 字节数 / 编码判定）
#
# 注意：docs/reference/win-help/ 已加入 .gitignore —— 这些是微软的文本，
# 本项目转为公开仓库后不能随仓库发布。见 plugins/<id>/NOTES.md 的说明。
[CmdletBinding()]
param(
    [string[]]$Commands = @(
        'ping', 'ipconfig', 'tracert', 'nslookup', 'netstat', 'tasklist',
        'systeminfo', 'chkdsk', 'sfc', 'robocopy', 'cleanmgr', 'powercfg'
    ),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\docs\reference\win-help')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Get-EncodingVerdict {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return 'empty' }

    # UTF-16LE 的典型特征：ASCII 字符后面跟一个 00
    $utf16Pairs = 0
    for ($i = 0; $i + 1 -lt [Math]::Min($Bytes.Length, 200); $i += 2) {
        if ($Bytes[$i + 1] -eq 0) { $utf16Pairs++ }
    }
    if ($utf16Pairs -gt 20) { return 'utf-16le' }

    $utf8Strict = [Text.UTF8Encoding]::new($false, $true)
    try {
        $null = $utf8Strict.GetString($Bytes)
        $hasNonAscii = $false
        foreach ($b in $Bytes) { if ($b -gt 0x7f) { $hasNonAscii = $true; break } }
        return $(if ($hasNonAscii) { 'utf-8' } else { 'ascii' })
    } catch {
        # 不是合法 UTF-8：中文环境下就是 OEM 代码页（936 = GBK）
        return ('oem/cp' + [Text.Encoding]::Default.CodePage + '（非 UTF-8）')
    }
}

function Invoke-Capture {
    param([string]$Exe, [string[]]$Arguments)

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($a in $Arguments) { $null = $psi.ArgumentList.Add($a) }

    $p = [Diagnostics.Process]::Start($psi)
    # 必须用 ReadToEnd 的字节版：StandardOutput.ReadToEnd() 会按宿主编码解码，原始字节就丢了
    $outMs = [IO.MemoryStream]::new()
    $errMs = [IO.MemoryStream]::new()
    $t1 = $p.StandardOutput.BaseStream.CopyToAsync($outMs)
    $t2 = $p.StandardError.BaseStream.CopyToAsync($errMs)
    $p.WaitForExit()
    [Threading.Tasks.Task]::WaitAll(@($t1, $t2))
    $exit = $p.ExitCode
    $p.Dispose()

    return [pscustomobject]@{
        ExitCode = $exit
        StdOut   = $outMs.ToArray()
        StdErr   = $errMs.ToArray()
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
    $r = Invoke-Capture -Exe $exe -Arguments @('/?')

    $outVerdict = Get-EncodingVerdict -Bytes $r.StdOut
    $errVerdict = Get-EncodingVerdict -Bytes $r.StdErr

    # 解码：优先严格 UTF-8，失败则按 OEM 代码页（中文环境=936）
    function Convert-Bytes {
        param([byte[]]$Bytes, [string]$Verdict)
        if ($Bytes.Length -eq 0) { return '' }
        if ($Verdict -eq 'utf-16le') { return [Text.Encoding]::Unicode.GetString($Bytes) }
        if ($Verdict -eq 'utf-8' -or $Verdict -eq 'ascii') { return [Text.Encoding]::UTF8.GetString($Bytes) }
        return [Text.Encoding]::GetEncoding([Text.Encoding]::Default.CodePage).GetString($Bytes)
    }

    $outText = Convert-Bytes -Bytes $r.StdOut -Verdict $outVerdict
    $errText = Convert-Bytes -Bytes $r.StdErr -Verdict $errVerdict

    $sb = [Text.StringBuilder]::new()
    $null = $sb.AppendLine("### $name /?  ——  抓取于 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $null = $sb.AppendLine("### exe: $exe")
    $null = $sb.AppendLine("### exit code: $($r.ExitCode)")
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
        stdoutBytes = $r.StdOut.Length
        stderrBytes = $r.StdErr.Length
        stdoutEnc   = $outVerdict
        stderrEnc   = $errVerdict
        helpStream  = $(if ($r.StdOut.Length -gt 0 -and $r.StdErr.Length -gt 0) { 'both' }
                        elseif ($r.StdErr.Length -gt 0) { 'stderr' }
                        elseif ($r.StdOut.Length -gt 0) { 'stdout' }
                        else { 'none' })
    }
    Write-Host ("{0,-12} exit={1,-4} stdout={2,-7} stderr={3,-7} {4}" -f `
        $name, $r.ExitCode, $r.StdOut.Length, $r.StdErr.Length, $outVerdict)
}

$meta = [pscustomobject]@{
    capturedAt = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    os         = (Get-CimInstance Win32_OperatingSystem).Version
    note       = '本目录是微软的帮助文本快照，已加入 .gitignore，不随仓库发布。'
    commands   = $records
}
$meta | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir '_meta.json') -Encoding utf8

Write-Host ""
Write-Host "快照目录: $OutDir"
Write-Host ("命令数: {0}" -f $records.Count)
