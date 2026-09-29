# certutil 工具包 —— 真机冒烟脚本（可随时复跑）
#
# 用法：
#   pwsh -NoProfile -File plugins/certutil/smoke.ps1
#
# 设计要点（照 playbook §5.1 的三条硬要求）：
#   ① 只读白名单：整条 argv 必须与白名单里某一项**逐字**相同才允许执行；
#   ② 判定不用 `-eq` 比数组：两边都用 NUL 连接后比字符串
#      （PowerShell 里 `-eq` 用在数组上是**过滤**语义，uv 那批真的因此放跑过一条写操作）；
#   ③ 红线自检：任何"改证书存储 / 动系统状态"的 argv 若被判为可跑，立刻 throw 中止，不进入执行循环。
#
# 本脚本用**原始字节**读 stdout/stderr（走 BaseStream），所以编码判定是字节级的、不依赖控制台代码页。
# 它只在 %TEMP%\certutil-smoke 下自建文件，**不碰任何证书存储**。

$ErrorActionPreference = 'Stop'
$certutil = Join-Path $env:SystemRoot 'System32\certutil.exe'
if (-not (Test-Path $certutil)) { throw "找不到 certutil.exe：$certutil" }

$work = Join-Path $env:TEMP 'certutil-smoke'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $work | Out-Null
Set-Content (Join-Path $work 'probe.txt') 'hello AllTool' -Encoding ascii -NoNewline
Set-Content (Join-Path $work 'plain.b64') 'aGVsbG8gQWxsVG9vbA==' -Encoding ascii
# -decode 的输入（带 PEM 包裹，certutil -encode 自己的格式）
$pem = "-----BEGIN CERTIFICATE-----`r`naGVsbG8gQWxsVG9vbA==`r`n-----END CERTIFICATE-----`r`n"
[IO.File]::WriteAllText((Join-Path $work 'wrapped.b64'), $pem, [Text.Encoding]::ASCII)

function Invoke-Certutil([string[]]$Argv) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $certutil
    foreach ($a in $Argv) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $out = New-Object System.IO.MemoryStream
    $err = New-Object System.IO.MemoryStream
    $p.StandardOutput.BaseStream.CopyTo($out)
    $p.StandardError.BaseStream.CopyTo($err)
    $p.WaitForExit()
    [pscustomobject]@{
        ExitCode = $p.ExitCode
        OutBytes = $out.ToArray()
        ErrBytes = $err.ToArray()
    }
}

function Join-Key([string[]]$Argv) { $Argv -join "`0" }

# ---------------------------------------------------------------- 白名单（只读 / 只写 %TEMP% 下的自建文件）
$file = Join-Path $work 'probe.txt'
$b64 = Join-Path $work 'wrapped.b64'
$plain = Join-Path $work 'plain.b64'
$decOut = Join-Path $work 'decoded.bin'
$encOut = Join-Path $work 'encoded.b64'
$encNew = Join-Path $work 'encoded-new.b64'
$dll = Join-Path $env:SystemRoot 'System32\msvcrt.dll'
# 从当前用户的 Root 存储导出一个真实证书到 %TEMP%（-store 的 OutputFile 是位置参数）
$certFile = Join-Path $work 'probe.cer'
Invoke-Certutil @('-store', 'Root', '0', $certFile) | Out-Null

$readOnly = @(
    @('-hashfile', $file, 'SHA256'),
    @('-hashfile', $file, 'SHA1'),
    @('-hashfile', $file, 'MD5'),
    @('-hashfile', $file, 'SHA512'),
    @('-hashfile', $file, 'SHA384'),
    @('-hashfile', $file),                       # 不填算法 → certutil 默认 SHA1（实测）
    @('-hashfile', $dll, 'SHA256'),
    @('-error', '0x80070002'),
    @('-error', '5'),
    @('-store'),
    @('-store', 'My'),
    @('-store', 'Root'),
    @('-store', 'CA'),
    @('-store', 'TrustedPublisher'),
    @('-store', 'Root', '0'),
    @('-user', '-store', 'My'),
    @('-store', '-user', 'My'),
    @('-enumstore'),
    @('-user', '-enumstore'),
    @('-verifystore', 'Root'),
    @('-verifystore', 'Root', '0'),
    @('-dump', $certFile),
    @('-dump', $dll),
    @('-asn', $certFile),
    @('-verify', $certFile),
    @('-verify', '-user', $certFile),
    @('-decode', $plain, $decOut),
    @('-encode', $file, $encNew),
    @('-encode', '-f', $file, $encOut),          # 复跑时覆盖自己上一轮生成的文件
    @('-decode', '-f', $b64, (Join-Path $work 'decoded-wrapped.bin')),
    # 参数错 / 文件不存在（只读失败路径，必须与预期退出码一致）
    @('-hashfile', (Join-Path $work 'no-such-file.bin'), 'SHA256'),
    @('-hashfile', $file, 'BOGUS'),
    @('-store', 'NoSuchStoreXYZ'),
    @('-asn', $dll),
    @('-verify', $dll),
    @('-error', 'zzz')
)

# ---------------------------------------------------------------- 红线（绝不允许被判为可跑）
$redLines = @(
    @('-addstore', 'Root', $certFile),
    @('-addstore', '-user', 'Root', $certFile),
    @('-addstore', 'My', $certFile),
    @('-delstore', 'Root', '0'),
    @('-delstore', '-user', 'My', '0'),
    @('-delstore', 'My', '0'),
    @('-viewdelstore', 'Root', '0'),
    @('-repairstore', 'My', '0'),
    @('-TPMInfo'),
    @('-syncWithWU', $work),
    @('-generateSSTFromWU', (Join-Path $work 'x.sst')),
    @('-URLCache', '*', 'delete'),
    @('-URLCache', 'delete'),
    @('-flushCache', 'lsass.exe'),
    @('-delkey', 'somekey'),
    @('-DeleteHelloContainer'),
    @('-setreg', 'Policy', 'x', 'y'),
    @('-delreg', 'Policy', 'x'),
    @('-importPFX', (Join-Path $work 'x.pfx')),
    @('-exportPFX', 'My', '0', (Join-Path $work 'x.pfx')),
    @('-shutdown'),
    @('-deleteEccCurve', '1.3.6.1.4.1'),
    @('-pulse'),
    @('-dspublish', $certFile),
    @('-deleterow', '37'),
    @('-backup', $work),
    @('-restore', $work),
    @('-UI', $certFile)
)

# ---------------------------------------------------------------- 第 ② 层自检：白名单里不许出现红线 argv
$allowKeys = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($a in $readOnly) { [void]$allowKeys.Add((Join-Key $a)) }
foreach ($a in $redLines) {
    if ($allowKeys.Contains((Join-Key $a))) {
        throw "自检失败：红线 argv 被判为可跑 -> $($a -join ' ')"
    }
}
Write-Host "自检 ① 通过：$($redLines.Count) 条红线 argv 全部不在只读白名单里" -ForegroundColor Green

# ---------------------------------------------------------------- 第 ③ 层自检：词法上不许出现"改存储"的动词
$mutationVerbs = @('-addstore', '-delstore', '-viewdelstore', '-repairstore', '-importPFX', '-exportPFX')
foreach ($a in $readOnly) {
    if ($mutationVerbs -contains $a[0]) { throw "自检失败：白名单里出现改动类动词 $($a[0])" }
}
Write-Host "自检 ② 通过：只读白名单里没有任何改动证书存储的动词" -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------- 证书存储基线指纹（收尾时比对，证明零改动）
# 注意：必须走 Invoke-Certutil 的原始字节 + cp936 解码，**不能**用 `& certutil ... | Out-String`——
# 后者受 PowerShell 的输出代码页影响，中文标签会被打乱，正则数不到「证书 N」。
function Get-StoreFingerprint([string]$StoreName) {
    $r = Invoke-Certutil @('-store', $StoreName)
    $t = [Text.Encoding]::GetEncoding(936).GetString($r.OutBytes)
    $certs = ([regex]::Matches($t, '================ 证书 \d+ ================')).Count
    "$StoreName|exit=$($r.ExitCode)|bytes=$($r.OutBytes.Length)|certs=$certs"
}
$fingerprintBefore = @('Root', 'My', 'CA') | ForEach-Object { Get-StoreFingerprint $_ }
Write-Host "证书存储基线指纹（开始）：" -ForegroundColor Cyan
$fingerprintBefore | ForEach-Object { Write-Host "  $_" }
Write-Host ""

# ---------------------------------------------------------------- 执行
$rows = @()
foreach ($a in $readOnly) {
    $key = Join-Key $a
    if (-not $allowKeys.Contains($key)) { throw "执行前二次确认失败：$($a -join ' ')" }
    $r = Invoke-Certutil $a
    $text = [Text.Encoding]::GetEncoding(936).GetString($r.OutBytes)
    $rows += [pscustomobject]@{
        Argv    = ($a -join ' ')
        Exit    = $r.ExitCode
        ExitHex = '0x{0:X8}' -f ($r.ExitCode -band 0xFFFFFFFF)
        Out     = $r.OutBytes.Length
        Err     = $r.ErrBytes.Length
        First   = (($text -split "`r?`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -First 1))
    }
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 400 | Write-Host

# ---------------------------------------------------------------- 编码判定（字节级）
Write-Host "=== 编码判定（不依赖控制台代码页，直接看原始字节）===" -ForegroundColor Cyan
$r = Invoke-Certutil @('-hashfile', $file, 'SHA256')
$bytes = $r.OutBytes
$nonAscii = @()
for ($i = 0; $i -lt $bytes.Length; $i++) { if ($bytes[$i] -gt 0x7F) { $nonAscii += $i } }
$firstAt = if ($nonAscii.Count -gt 0) { $nonAscii[0] } else { -1 }
Write-Host ("-hashfile SHA256: {0} 字节，>{1} 的字节 {2} 个，第一处在偏移 {3}" -f $bytes.Length, '0x7F', $nonAscii.Count, $firstAt)
if ($firstAt -ge 0) {
    $slice = $bytes[($firstAt - 4)..($firstAt + 5)]
    Write-Host ("  原始字节: " + (($slice | ForEach-Object { $_.ToString('x2') }) -join ' '))
    Write-Host ("  按 cp936 解: " + [Text.Encoding]::GetEncoding(936).GetString($slice))
    Write-Host ("  按 utf-8 解: " + [Text.Encoding]::UTF8.GetString($slice))
}
$strict = New-Object Text.UTF8Encoding($false, $true)
try {
    $null = $strict.GetString($bytes)
    Write-Host "  严格 UTF-8 解码：通过（=> 这一段输出其实无法证明不是 UTF-8）" -ForegroundColor Yellow
} catch {
    Write-Host "  严格 UTF-8 解码：抛异常 => 确定不是 UTF-8（结论：runtime.encoding: oem）" -ForegroundColor Green
}
Write-Host ""

# ---------------------------------------------------------------- 字段风格判定（playbook §9）
Write-Host "=== 字段风格判定：certutil 认不认『两个 token』/『单 token 粘连』 ===" -ForegroundColor Cyan
$styleCases = @(
    @{ Name = '两 token：-hashfile <file> SHA256'; Argv = @('-hashfile', $file, 'SHA256') },
    @{ Name = '两 token：-store My';                Argv = @('-store', 'My') },
    @{ Name = '两 token：-error 0x80070002';        Argv = @('-error', '0x80070002') },
    @{ Name = '单 token：-hashfile:<file>';         Argv = @("-hashfile:$file") },
    @{ Name = '单 token：-hashfile=<file>';         Argv = @("-hashfile=$file") },
    @{ Name = '单 token：-store:My';                Argv = @('-store:My') },
    @{ Name = '单 token：-encode:foo';              Argv = @('-encode:foo', 'bar') },
    @{ Name = '开关在前：-encode -f <in> <out>';    Argv = @('-encode', '-f', $file, $encOut) },
    @{ Name = '开关在后：-encode <in> -f <out>';    Argv = @('-encode', $file, '-f', $encOut) }
)
foreach ($c in $styleCases) {
    $r = Invoke-Certutil $c.Argv
    $first = ([Text.Encoding]::GetEncoding(936).GetString($r.OutBytes) -split "`r?`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -First 1)
    Write-Host ("  {0,-42} exit={1,-12} {2}" -f $c.Name, $r.ExitCode, $first)
}
Write-Host ""

# ---------------------------------------------------------------- nextSteps.when 正则（用宿主的 .NET 语义）
Write-Host "=== nextSteps.when 正则：用宿主的 Regex.Matches(text, pattern, Multiline) 语义核对 ===" -ForegroundColor Cyan
$hashText = [Text.Encoding]::GetEncoding(936).GetString((Invoke-Certutil @('-hashfile', $dll, 'SHA256')).OutBytes)
$when = '(?m)^(?:SHA(?:1|256|384|512)|MD5)\s+的\s+(?<path>[^\r\n]+?)\s+哈希:'
$ms = [regex]::Matches($hashText, $when, [Text.RegularExpressions.RegexOptions]::Multiline)
Write-Host ("  命中 {0} 行；捕获组 path = [{1}]" -f $ms.Count, $(if ($ms.Count -gt 0) { $ms[0].Groups['path'].Value } else { '<无>' }))
Write-Host ""

# ---------------------------------------------------------------- 证书存储零残留自检
Write-Host "=== 证书存储零残留自检 ===" -ForegroundColor Cyan
$fingerprintAfter = @('Root', 'My', 'CA') | ForEach-Object { Get-StoreFingerprint $_ }
$same = ($fingerprintBefore -join "`n") -eq ($fingerprintAfter -join "`n")
Write-Host "证书存储指纹（结束）："
$fingerprintAfter | ForEach-Object { Write-Host "  $_" }
if ($same) {
    Write-Host "  ✔ 与本轮开始前逐字相同（字节数、证书数、退出码都没变）" -ForegroundColor Green
} else {
    Write-Host "  ✘ 指纹发生变化——本轮可能改动了证书存储，必须人工追查！" -ForegroundColor Red
}
Write-Host "  本轮**没有**执行任何 -addstore / -delstore —— 见上面的红线自检"
Write-Host ""
Write-Host "临时文件目录：$work（脚本不自动删除；复核后可用 Remove-Item -Recurse -Force '$work' 清掉）" -ForegroundColor DarkGray
