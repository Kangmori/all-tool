# 一次性探针：实测 Windows 自带命令**真实输出**（不是 /?）的编码与退出码。
# 产物：docs/reference/win-help/_probe.json
[CmdletBinding()]
param([string]$OutJson = (Join-Path $PSScriptRoot '..\docs\reference\win-help\_probe.json'))

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$OemCodePage = [int](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage').OEMCP
$OemEnc = [Text.Encoding]::GetEncoding($OemCodePage)

function Test-IsValidUtf8 {
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
        $i += $need + 1
    }
    return $true
}

function Get-Verdict {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return 'empty' }
    $hasNonAscii = $false
    foreach ($b in $Bytes) { if ($b -gt 0x7f) { $hasNonAscii = $true; break } }
    if (-not $hasNonAscii) { return 'ascii' }
    if (Test-IsValidUtf8 -Bytes $Bytes) { return 'utf-8' }
    return "cp$OemCodePage"
}

function Invoke-Probe {
    param([string]$Exe, [string[]]$CmdArgs, [int]$TimeoutSeconds = 20)

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    foreach ($a in $CmdArgs) { [void]$psi.ArgumentList.Add($a) }

    $p = [Diagnostics.Process]::Start($psi)
    $o = [IO.MemoryStream]::new()
    $e = [IO.MemoryStream]::new()
    $t1 = $p.StandardOutput.BaseStream.CopyToAsync($o)
    $t2 = $p.StandardError.BaseStream.CopyToAsync($e)
    $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { try { $p.Kill($true) } catch { } ; $null = $p.WaitForExit(3000) }
    [Threading.Tasks.Task]::WaitAll(@($t1, $t2))
    $exit = if ($timedOut) { $null } else { $p.ExitCode }
    $p.Dispose()

    $outBytes = $o.ToArray()
    $errBytes = $e.ToArray()
    return [pscustomobject]@{
        Exit    = $exit
        To      = $timedOut
        OutText = $OemEnc.GetString($outBytes)
        ErrText = $OemEnc.GetString($errBytes)
        OutLen  = $outBytes.Length
        ErrLen  = $errBytes.Length
        OutEnc  = (Get-Verdict -Bytes $outBytes)
        ErrEnc  = (Get-Verdict -Bytes $errBytes)
    }
}

$cases = @(
    @{ n = 'ipconfig';            e = 'ipconfig.exe';  a = @() }
    @{ n = 'ipconfig /all';       e = 'ipconfig.exe';  a = @('/all') }
    @{ n = 'ipconfig /displaydns';e = 'ipconfig.exe';  a = @('/displaydns') }
    @{ n = 'systeminfo';          e = 'systeminfo.exe';a = @() }
    @{ n = 'tasklist';            e = 'tasklist.exe';  a = @() }
    @{ n = 'tasklist /svc';       e = 'tasklist.exe';  a = @('/svc') }
    @{ n = 'tasklist /v (前几行)';e = 'tasklist.exe';  a = @('/v') }
    @{ n = 'netstat -an';         e = 'netstat.exe';   a = @('-an') }
    @{ n = 'netstat -rn';         e = 'netstat.exe';   a = @('-rn') }
    @{ n = 'netstat -e';          e = 'netstat.exe';   a = @('-e') }
    @{ n = 'ping 127.0.0.1 -n 2'; e = 'ping.exe';      a = @('127.0.0.1', '-n', '2') }
    @{ n = 'nslookup localhost';  e = 'nslookup.exe';  a = @('localhost') }
    @{ n = 'tracert -h 1 127.0.0.1'; e = 'tracert.exe'; a = @('-h', '1', '127.0.0.1') }
    @{ n = 'chkdsk (只读)';       e = 'chkdsk.exe';    a = @() }
    @{ n = 'sfc /verifyonly';     e = 'sfc.exe';       a = @('/verifyonly') }
    @{ n = 'robocopy 无参数';     e = 'robocopy.exe';  a = @() }
    @{ n = 'powercfg /query';     e = 'powercfg.exe';  a = @('/query') }
    @{ n = 'powercfg /list';      e = 'powercfg.exe';  a = @('/list') }
    @{ n = 'powercfg /a';         e = 'powercfg.exe';  a = @('/a') }
)

$rows = @()
foreach ($c in $cases) {
    $r = Invoke-Probe -Exe $c.e -CmdArgs $c.a
    $rows += [pscustomobject]@{
        case    = $c.n
        argv    = ($c.a -join ' ')
        exit    = $r.Exit
        timedOut= $r.To
        outLen  = $r.OutLen
        outEnc  = $r.OutEnc
        errLen  = $r.ErrLen
        errEnc  = $r.ErrEnc
        firstLines = (($r.OutText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 5) -join ' ⏎ '
        errFirst   = (($r.ErrText -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 3) -join ' ⏎ '
    }
    Write-Host ("{0,-24} exit={1,-7} out={2,-7}B {3,-7} err={4,-6}B {5}" -f `
        $c.n, $(if ($r.To) { 'TIMEOUT' } else { $r.Exit }), $r.OutLen, $r.OutEnc, $r.ErrLen, $r.ErrEnc)
}

$rows | ConvertTo-Json -Depth 4 | Set-Content -Path $OutJson -Encoding utf8
Write-Host ""
Write-Host "已落盘: $OutJson"
