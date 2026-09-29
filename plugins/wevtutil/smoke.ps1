# wevtutil 冒烟测试（只跑只读动作）
#
# 自检：任何"危险动作的 argv"被判定为可跑 -> 立刻 throw，不进入执行循环。
#
# **这里的 argv 不是手写的**：每条都按 manifest.yaml 的
# command + commandArgs + 字段声明顺序 + style(attached 折叠成 prefix+value)
# 展开规则算出来，与宿主 ArgvBuilder 的规则一一对应。
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$w = 'C:\Windows\System32\wevtutil.exe'
$q = [char]39   # 单引号

# 先把带单引号的查询串拼好再放进哈希表。
# 注意：**不能**在哈希表的条目里跨行用 `+` 续接（PowerShell 会在那一行把条目截断，
# 剩下的片段被当成新语句，于是 argv 被拆成好几段 -> wevtutil 报 87 太多参数）。
$qChkdsk  = '/q:*[System[Provider[@Name=' + $q + 'Chkdsk' + $q + ']]]'
$qUpdate  = '/q:*[System[Provider[@Name=' + $q + 'Microsoft-Windows-WindowsUpdateClient' + $q + ']]]'

function A([string]$prefix, [string]$value) { "$prefix$value" }

# ---------- 1. 白名单：只读动作真实会生成的 argv ----------
$allow = [ordered]@{
  'list-logs'                     = @('el')
  'query-log-config/System'       = @('gl','System','/f:Text')
  'query-log-config/System+XML'   = @('gl','System','/f:XML')
  'query-log-status/Application'  = @('gli','Application')
  'list-publishers'               = @('ep')
  'query-publisher/default'       = @('gp','Microsoft-Windows-Eventlog')
  'query-publisher/ge'            = @('gp','Microsoft-Windows-Eventlog','/ge:true')
  'query-channel-events/官方示例' = @('qe','Application','/c:3','/rd:true','/f:Text')
  'query-channel-events/xml+root' = @('qe','System','/c:3','/rd:true','/f:XML','/e:root')
  'query-system-errors/默认'      = @('qe','System','/f:text','/c:50','/q:*[System[(Level=1 or Level=2)]]')
  'query-chkdsk/默认'             = @('qe','Application','/f:text','/c:5', $qChkdsk)
  'query-chkdsk/26226'            = @('qe','Application','/f:text','/c:3','/q:*[System[(EventID=26226)]]')
  'query-application-errors/默认' = @('qe','Application','/f:text','/c:50','/q:*[System[(Level=1 or Level=2)]]')
  'query-warnings/默认'           = @('qe','System','/f:text','/c:50','/q:*[System[(Level=3)]]')
  'query-update-events/默认'      = @('qe','System','/f:text','/c:30', $qUpdate)
  'query-shutdown/默认'           = @('qe','System','/f:text','/c:20','/q:*[System[(EventID=41) or (EventID=1001)]]')
  'query-shutdown/1001'           = @('qe','System','/f:text','/c:20','/q:*[System[(EventID=1001)]]')
}

# ---------- 2. 红线：这些 argv 绝不允许被判为可跑 ----------
$forbidden = @(
  @('cl','Application','/bu:D:\backup\al-before-clear.evtx'),
  @('cl','System'),
  @('sl','System','/ms:20971520'),
  @('sl','/c:C:\config.xml'),
  @('im','D:\sdk\myManifest.xml'),
  @('um','D:\sdk\myManifest.xml'),
  @('epl','System','D:\backup\system.evtx'),
  @('al','D:\backup\a.evtx'),
  @('qe','System','/c:1','/sbm:bm.xml')
)

function Test-Allowed([string[]]$argv) {
  $key = ($argv -join "`0")
  foreach ($v in $allow.Values) { if (($v -join "`0") -eq $key) { return $true } }
  return $false
}
function Test-Forbidden([string[]]$argv) {
  $key = ($argv -join "`0")
  foreach ($v in $forbidden) { if (($v -join "`0") -eq $key) { return $true } }
  return $false
}

# ---------- 3. 自检 ----------
$selfTestFail = @()
foreach ($f in $forbidden) { if (Test-Allowed $f) { $selfTestFail += ($f -join ' ') } }
foreach ($v in $allow.Values) {
  if ($v[0] -in @('cl','sl','im','um','epl','al')) { $selfTestFail += "白名单里混入了写动作: $($v -join ' ')" }
}
# 反例也要自检：白名单里不许出现"选项与值分成两个 token"的形态
foreach ($v in $allow.Values) {
  for ($i = 1; $i -lt $v.Count; $i++) {
    if ($v[$i] -match '^/[a-z]+$') { $selfTestFail += "白名单里有分离式选项（会以 87 失败）: $($v -join ' ')" }
    if ($v[$i] -match '^[^*]' -and $i -gt 0 -and $v[$i-1] -match '^/[a-z]+$') { }
  }
}
if ($selfTestFail.Count -gt 0) {
  throw "自检失败：$($selfTestFail -join ' | ')"
}
"自检通过：$($forbidden.Count) 条红线命令全部判为'不可跑'；白名单 $($allow.Count) 条全是只读动作且选项都是 attached 形态。"
''

# ---------- 4. 执行循环 ----------
function Invoke-Wevt([string[]]$argv) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $w
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  foreach ($a in $argv) { [void]$psi.ArgumentList.Add($a) }
  $p = [System.Diagnostics.Process]::Start($psi)
  $ms = New-Object System.IO.MemoryStream
  $p.StandardOutput.BaseStream.CopyTo($ms)
  $errText = $p.StandardError.ReadToEnd()
  $p.WaitForExit()
  return [pscustomobject]@{ Exit = $p.ExitCode; Bytes = $ms.ToArray(); Err = $errText }
}

$results = @()
foreach ($kv in $allow.GetEnumerator()) {
  $argv = $kv.Value
  if (-not (Test-Allowed $argv)) { throw "内部错误：$($kv.Key) 未通过白名单判定" }
  if (Test-Forbidden $argv) { throw "内部错误：$($kv.Key) 命中红线" }

  $r = Invoke-Wevt $argv
  $bytes = $r.Bytes
  $nonAscii = 0; $nulls = 0
  foreach ($b in $bytes) { if ($b -gt 127) { $nonAscii++ }; if ($b -eq 0) { $nulls++ } }
  $text = [Text.Encoding]::GetEncoding(936).GetString($bytes)
  $evCount = ([regex]::Matches($text, '(?m)^Event\[')).Count
  $lines = if ($text.Length) { ($text -replace "`r`n", "`n").TrimEnd("`n").Split("`n").Count } else { 0 }
  $utf8ok = $true
  try { $u = [Text.UTF8Encoding]::new($false, $true); $null = $u.GetString($bytes) } catch { $utf8ok = $false }

  $results += [pscustomobject]@{
    Case = $kv.Key; Exit = $r.Exit; Bytes = $bytes.Length; Lines = $lines
    Events = $evCount; NonAscii = $nonAscii; Nulls = $nulls; Utf8StrictOK = $utf8ok
    Err = ($r.Err -replace "`r`n", ' / ').Trim()
  }
}
$results | Format-Table -AutoSize | Out-String -Width 400

'--- 编码判定的原始证据（qe ... /f:text 的原始 stdout）---'
$r = Invoke-Wevt @('qe','System','/c:3','/rd:true','/f:text')
$b = $r.Bytes
$idx = @(); for ($i = 0; $i -lt $b.Length; $i++) { if ($b[$i] -gt 127) { $idx += $i } }
"非 ASCII 偏移: $($idx -join ',')"
$s = $idx[0]
"上下文原始字节: " + (($b[($s-6)..($s+8)] | ForEach-Object { $_.ToString('x2') }) -join ' ')
"按 cp936 解: " + [Text.Encoding]::GetEncoding(936).GetString($b[($s-6)..($s+8)])
"按 utf-8 解: " + [Text.Encoding]::UTF8.GetString($b[($s-6)..($s+8)])
try { $u = [Text.UTF8Encoding]::new($false, $true); $null = $u.GetString($b); 'utf-8 严格解码: 通过（不抛错）' }
catch { "utf-8 严格解码: 抛异常 -> $($_.Exception.Message)" }

''
'--- 失败路径（都是只读判定，不改动任何东西）---'
foreach ($c in @(
  @{l='日志名不存在'; a=@('qe','NoSuchLog__AllToolNotCreated','/c:1','/f:text')},
  @{l='格式取值非法'; a=@('qe','System','/c:1','/f:json')},
  @{l='条数 0';      a=@('qe','System','/c:0','/f:text')},
  @{l='查询串语法错'; a=@('qe','System','/c:1','/f:text','/q:*[System[')},
  @{l='属性名写错（不报错！）'; a=@('qe','System','/c:1','/f:text','/q:*[System[(Bogus=1)]]')},
  @{l='发布者不存在'; a=@('gp','NoSuchPublisher__AllToolNotCreated')},
  @{l='选项写成两个 token（反例）'; a=@('qe','System','/c','1','/f:text')}
)) {
  $r = Invoke-Wevt $c.a
  $t = [Text.Encoding]::GetEncoding(936).GetString($r.Bytes)
  "{0,-26} exit={1,-6} out={2,-6} ev={3,-3} err=[{4}]" -f $c.l, $r.Exit, $r.Bytes.Length, ([regex]::Matches($t,'(?m)^Event\[')).Count, (($r.Err -replace "`r`n",' / ').Trim())
}
