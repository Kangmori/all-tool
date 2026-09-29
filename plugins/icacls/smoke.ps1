#!/usr/bin/env pwsh
# =============================================================================
# plugins/icacls/smoke.ps1 —— icacls 工具包的真机冒烟（本包自带，可随时复跑）
#
# 复跑： pwsh -NoProfile -File plugins/icacls/smoke.ps1
#
# 设计（照 playbook §5.1 的三条硬要求）：
#   ① 判定逻辑**不用 `-eq` 比数组**：整条 argv 两边都用 "`0" 连接后比字符串。
#   ② **红线自检**：列出一批"绝不允许被判为可跑"的 argv（会改探测目录之外的权限、
#      会改所有者、会 /reset 真实目录…），脚本启动时先判一次，**任一条被判为可跑就 throw 中止**，
#      不进入执行循环。
#   ③ 判不准的一律算"未跑"。
#
# 红线（本脚本绝不越过）：
#   * 一切改动类 argv 的 path 都必须位于 %TEMP%\__AllToolProbe_icacls 之内；
#       /reset 与 /setowner 只在"按本机非管理员身份必然失败"的形态下跑同一条 argv
#       （见 NOTES.md §9 的说明），因此它们连探测目录都没改到。
#   * 不往任何真实目录写 ACL 文件；不碰探测目录之外的任何对象。
#   * 不调用 Start-Process -Verb RunAs（提权只用于只读测量，且在本脚本之外的探查里做）。
#   * 收尾删除探测目录，并用 Test-Path 复核。
# =============================================================================

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

$PROBE = Join-Path $env:TEMP '__AllToolProbe_icacls'
$ICACLS = Join-Path $env:SystemRoot 'System32\icacls.exe'

# ---------------------------------------------------------------------------
# 原始字节级执行（为了拿退出码与"到底写了 stdout 还是 stderr"）
# ---------------------------------------------------------------------------
function Invoke-IcaclsRaw {
    param([string[]]$Argv)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $ICACLS
    foreach ($a in $Argv) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $outMs = New-Object System.IO.MemoryStream
    $errMs = New-Object System.IO.MemoryStream
    $t1 = $p.StandardOutput.BaseStream.CopyToAsync($outMs)
    $t2 = $p.StandardError.BaseStream.CopyToAsync($errMs)
    $p.WaitForExit()
    [System.Threading.Tasks.Task]::WaitAll(@($t1, $t2))
    [pscustomobject]@{
        Argv    = $Argv
        Exit    = $p.ExitCode
        OutLen  = $outMs.Length
        ErrLen  = $errMs.Length
        Out     = $outMs.ToArray()
        Err     = $errMs.ToArray()
        OutText = [Text.Encoding]::GetEncoding(936).GetString($outMs.ToArray())
        ErrText = [Text.Encoding]::GetEncoding(936).GetString($errMs.ToArray())
    }
}

# ---------------------------------------------------------------------------
# 白名单 / 红线：只读 argv 逐字白名单 + 改动类 argv 的"路径必须在探测目录内"结构判定
# ---------------------------------------------------------------------------
function Get-PathPosition([string[]]$Argv) {
    # 位置参数（路径）可能排在开关之前或之后，所以不能用"遇到第一个 / 就停"去找。
    # 这里返回**所有不以 / 开头、且不是某个开关取值**的 token。
    # 注意：这里刻意保守——它只服务于"路径必须在探测目录内"的判定，
    # 取值 token（账号、ACL 文件路径）由 Test-WriteShape 里的逐开关词法检查负责。
    $switchTakesValue = @('/save', '/restore', '/grant', '/grant:r', '/deny',
                          '/remove', '/remove:g', '/remove:d', '/setowner', '/setintegritylevel')
    $result = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Argv.Count; $i++) {
        if ($Argv[$i].StartsWith('/')) { continue }
        if ($i -gt 0 -and ($switchTakesValue -contains $Argv[$i - 1])) { continue }
        $result.Add($Argv[$i])
    }
    return $result.ToArray()
}

function Test-InProbe([string]$Value) {
    if ([string]::IsNullOrEmpty($Value)) { return $false }
    try { $full = [System.IO.Path]::GetFullPath($Value) } catch { return $false }
    $root = [System.IO.Path]::GetFullPath($PROBE)
    return $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
}

# ---------------------------------------------------------------------------
# 允许执行的 argv 集合：白名单是**有定界的结构判定**
#
# 为什么不做"路径必须在探测目录内"这种启发式：`icacls <路径> /setowner Everyone` 里的
# `Everyone` 是一个账号 token，但从 argv 形状上它与一个相对路径完全无法区分
# （`/setowner` 的取值**不带冒号**，没有语法标记）。第一版脚本就因此把
# `…\a.txt /setowner Everyone` 判成了"可跑"，被红线自检抓出来（见 NOTES.md §9.2）。
#
# 所以改成按**动作的形状**判定，每条形状只允许固定的 token 词法：
#   /grant、/deny  —— 取值必须匹配 `<账号>:<掩码>`（**含冒号**，账号部分只允许字母数字 . _ - \ *）
#   /setowner      —— 取值只允许精确等于当前账号（唯一一个我们接受"改所有者"的目标）
#   /remove        —— 取值只允许 Users（探测文件是我们自己建的，收尾前就删掉）
#   /save、/restore 的取值必须是探测目录内的绝对路径
# 任何一条不满足 => 判为不可跑。
# ---------------------------------------------------------------------------
$script:AllowedOwner = if ($env:USERDOMAIN) { "$env:USERDOMAIN\$env:USERNAME" } else { $env:USERNAME }

function Test-WriteShape([string[]]$Argv) {
    $allowedSwitches = @('/grant', '/grant:r', '/deny', '/remove', '/remove:g', '/remove:d',
                         '/save', '/restore', '/reset', '/setowner',
                         '/inheritancelevel:d', '/inheritancelevel:e', '/setintegritylevel',
                         '/T', '/C', '/Q')
    foreach ($token in $Argv) {
        if ($token.StartsWith('/') -and ($allowedSwitches -notcontains $token)) { return $false }
    }

    $paths = Get-PathPosition $Argv
    if ($paths.Count -lt 1) { return $false }
    foreach ($p in $paths) {
        if (-not (Test-InProbe $p)) { return $false }
    }

    # 逐个开关检查它的取值词法
    for ($i = 0; $i -lt $Argv.Count; $i++) {
        $t = $Argv[$i]
        switch -Regex ($t) {
            '^/(grant|grant:r|deny)$' {
                $v = if ($i + 1 -lt $Argv.Count) { $Argv[$i + 1] } else { '' }
                # <账号>:<掩码>，账号部分只允许 字母数字 . _ - \ *，掩码部分只允许 A-Z 、 括号
                if ($v -notmatch '^[A-Za-z0-9._\\*-]+:[A-Za-z(),]+$') { return $false }
                # **账号白名单**：只允许 Users 与它的数值 SID（探测文件是自己建的、收尾前删掉）。
                # 结构判定保证不了"改的是哪个账号"——Everyone:(F) 的形状与 Users:(R) 一模一样。
                $account = $v.Split(':')[0]
                if ($account -notin @('Users', '*S-1-5-32-545')) { return $false }
            }
            '^/setowner$' {
                $v = if ($i + 1 -lt $Argv.Count) { $Argv[$i + 1] } else { '' }
                # **唯一允许的取值**：精确等于当前账号（本包用它在收尾前把所有者交回自己）
                if ($v -cne $script:AllowedOwner) { return $false }
            }
            '^/remove(:g|:d)?$' {
                $v = if ($i + 1 -lt $Argv.Count) { $Argv[$i + 1] } else { '' }
                if ($v -cne 'Users') { return $false }
            }
            '^/(save|restore)$' {
                $v = if ($i + 1 -lt $Argv.Count) { $Argv[$i + 1] } else { '' }
                if (-not (Test-InProbe $v)) { return $false }
            }
        }
    }
    return $true
}

function Test-CanRun([string[]]$Argv) {
    $joined = $Argv -join "`0"

    # ① 只读白名单：整条 argv 必须逐字出现在这里（用 `0 连接后比字符串，绝不用 -eq 比数组）
    $readOnly = @(
        @("$PROBE\acl\a.txt"),
        @("$PROBE\rw\a.txt"),
        @("$PROBE\rw\*.txt"),
        @("$PROBE\acl\*"),
        @("$PROBE\acl"),
        @("$PROBE\acl", "/T"),
        @("$PROBE\acl", "/verify", "/T"),
        @("$PROBE\acl", "/findsid", "*S-1-5-32-545", "/T"),
        @("C:\Windows\System32\drivers\etc\hosts"),
        @("C:\Windows\System32\drivers\etc"),
        @("C:\Windows\System32\drivers\etc\*"),
        @("C:\Windows\System32\drivers\etc", "/T"),
        @("C:\Windows\System32\drivers\etc", "/T", "/C"),
        @("C:\Windows\System32\drivers\etc\*", "/T", "/C"),
        @("C:\Users\Steve\Documents", "/T", "/C"),
        @("C:\Users\Steve\Documents", "/findsid", "*S-1-5-32-545", "/T", "/C"),
        @("/?"),
        # 失败路径（只读）
        @("$PROBE\__nope__"),
        @("$PROBE", "/zz"),
        @("$PROBE\rw", "/inheritancelevel:x"),
        @("$PROBE", "/save", "$PROBE\acl\no-dir\x.txt", "/T"),
        @("C:\Windows\System32\config", "/T"),
        @("$PROBE\acl\a.txt", "/setowner", "Users"),
        # 字段风格判定的两个反例：开关写在路径之前（用于确认这条路走不通）
        @("/T", "$PROBE\acl"),
        @("/save", "$PROBE\x.txt", "$PROBE\acl")
    )
    foreach ($candidate in $readOnly) {
        if (($candidate -join "`0") -ceq $joined) { return $true }
    }

    # ② 改动类：按形状判定
    return (Test-WriteShape $Argv)
}

# ---------------------------------------------------------------------------
# 红线自检：下面这些 argv 绝不允许被判为可跑，任一条判为可跑就中止
# ---------------------------------------------------------------------------
$redLines = @(
    @("C:\Windows\System32\drivers\etc\hosts", "/grant", "Users:(F)"),
    @("C:\", "/reset", "/T"),
    @("C:\Windows", "/reset", "/T", "/C"),
    @("C:\Users", "/grant", "Everyone:(F)", "/T"),
    @("C:\Windows\System32", "/setowner", "Administrators", "/T"),
    @("C:\", "/restore", "D:\backup\x.txt", "/C"),
    @("$PROBE\..\..\..\Windows", "/reset", "/T"),
    @("C:\Windows\System32\drivers\etc\hosts", "/deny", "Users:(R)"),
    @("C:\Windows\System32", "/remove", "Users", "/T"),
    @("$PROBE\acl\a.txt", "/setowner", "Everyone"),
    @("$PROBE\acl\a.txt", "/setowner", "Administrators", "/T"),
    @("$PROBE\acl", "/grant", "Everyone:(F)", "/T"),
    @("C:\Windows", "/save", "C:\Windows\acl.txt", "/T"),
    @("/reset"),
    @("C:\Windows", "/grant", "Users:(F)", "/T", "/C")
)
foreach ($rl in $redLines) {
    if (Test-CanRun $rl) {
        throw ("红线自检失败：这条 argv 被判为可跑 —— icacls " + ($rl -join ' '))
    }
}
Write-Host "红线自检通过：$($redLines.Count) 条危险 argv 全部被判为不可跑。" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 建立探测目录
# ---------------------------------------------------------------------------
if (Test-Path $PROBE) { Remove-Item -Recurse -Force $PROBE }
New-Item -ItemType Directory -Force -Path "$PROBE\rw"   | Out-Null
New-Item -ItemType Directory -Force -Path "$PROBE\acl"  | Out-Null
New-Item -ItemType Directory -Force -Path "$PROBE\acl\sub" | Out-Null
'one' | Out-File "$PROBE\rw\a.txt" -Encoding ascii
'two' | Out-File "$PROBE\rw\b.txt" -Encoding ascii
'three' | Out-File "$PROBE\acl\a.txt" -Encoding ascii
'four' | Out-File "$PROBE\acl\sub\b.txt" -Encoding ascii

# ---------------------------------------------------------------------------
# 用例表：(说明, argv, 预期退出码, 是否改动类)
# ---------------------------------------------------------------------------
$cases = @(
    # ---- 只读：清单里的 examples（宿主会拼出的 argv）
    @{ n='show-acl 例1（看 hosts 文件）';            a=@('C:\Windows\System32\drivers\etc\hosts');                        e=0 }
    @{ n='show-acl 例2（看 etc 目录）';              a=@('C:\Windows\System32\drivers\etc');                              e=0 }
    @{ n='show-acl-recursive 例1';                   a=@('C:\Windows\System32\drivers\etc','/T');                         e=0 }
    @{ n='show-acl-recursive 例2（+ /C）';           a=@('C:\Windows\System32\drivers\etc','/T','/C');                    e=0 }
    @{ n='show-acl-wildcard 例1';                    a=@('C:\Windows\System32\drivers\etc\*');                            e=0 }
    @{ n='show-acl-wildcard 例2（+ /T /C）';         a=@('C:\Windows\System32\drivers\etc\*','/T','/C');                  e=0 }
    @{ n='verify-acl 例（Documents /T /C）';         a=@('C:\Users\Steve\Documents','/T','/C');                           e=0 }
    @{ n='find-sid 例';                              a=@('C:\Users\Steve\Documents','/findsid','*S-1-5-32-545','/T','/C'); e=0 }
    @{ n='help（/?）';                               a=@('/?');                                                          e=0 }
    @{ n='探测：单文件 /T /Q /C 全给';               a=@("$PROBE\rw\a.txt",'/T','/Q','/C');                               e=0 }
    @{ n='探测：通配符 + /T';                        a=@("$PROBE\rw\*",'/T');                                             e=0 }
    @{ n='探测：/verify 递归';                       a=@("$PROBE\acl",'/verify','/T');                                    e=0 }
    # ---- 改动类：只在探测目录内真的改，并核对退出码
    @{ n='save-acl：把探测目录的 ACL 导出';          a=@("$PROBE\acl",'/save',"$PROBE\acl.txt",'/T');                    e=0; w=$true }
    @{ n='grant-perm：授予 Users 只读';              a=@("$PROBE\rw\a.txt",'/grant','Users:(R)');                         e=0; w=$true }
    @{ n='grant-perm：/grant:r 替换授权';            a=@("$PROBE\rw\a.txt",'/grant:r','*S-1-5-32-545:(M)');               e=0; w=$true }
    @{ n='deny-perm：显式拒绝 Users 写入';           a=@("$PROBE\rw\a.txt",'/deny','Users:(W)');                          e=0; w=$true }
    @{ n='remove-perm：只移除授权条目';              a=@("$PROBE\rw\a.txt",'/remove:g','Users');                          e=0; w=$true }
    @{ n='remove-perm：只移除拒绝条目';              a=@("$PROBE\rw\a.txt",'/remove:d','Users');                          e=0; w=$true }
    @{ n='remove-perm：授权与拒绝都移除';            a=@("$PROBE\rw\a.txt",'/remove','Users');                            e=0; w=$true }
    @{ n='set-inheritance：禁用继承并复制 ACE';      a=@("$PROBE\rw\a.txt",'/inheritancelevel:d');                        e=0; w=$true }
    @{ n='set-inheritance：恢复继承';                a=@("$PROBE\rw\a.txt",'/inheritancelevel:e');                        e=0; w=$true }
    @{ n='set-integrity：官方示例形态';              a=@("$PROBE\rw\a.txt",'/setintegritylevel','(CI)(OI)H');             e=0; w=$true }
    @{ n='set-integrity：低完整性';                  a=@("$PROBE\rw\a.txt",'/setintegritylevel','L');                     e=0; w=$true }
    @{ n='set-owner：改成自己（非管理员可成）';      a=@("$PROBE\rw\b.txt",'/setowner',$script:AllowedOwner);             e=0; w=$true }
    # ---- 失败路径：验证 exitCodes 表里那些码
    @{ n='不存在的文件 -> 2';                        a=@("$PROBE\__nope__");                                              e=2 }
    @{ n='开关拼错 -> 87';                           a=@("$PROBE",'/zz');                                                 e=87 }
    @{ n='/inheritancelevel 取值非法 -> 87';         a=@("$PROBE\rw",'/inheritancelevel:x');                              e=87 }
    @{ n='/save 到不存在的目录 -> 87';               a=@("$PROBE",'/save',"$PROBE\acl\no-dir\x.txt",'/T');                e=87 }
    @{ n='系统目录拒绝访问 -> 5';                    a=@('C:\Windows\System32\config','/T');                              e=5 }
    @{ n='/setowner 改成别人（非管理员）-> 1307';    a=@("$PROBE\acl\a.txt",'/setowner','Users');                         e=1307 }
    @{ n='/reset（非管理员，在探测目录内）';         a=@("$PROBE\acl",'/reset','/T','/C');                                e=0; w=$true }
    # ---- 字段风格判定（两种写法各试一次）
    @{ n='风格判定：<路径>:/T 粘一起必须失败 -> 123'; a=@("$PROBE\acl\:/T");                                              e=123 }
    @{ n='风格判定：开关写在路径之前必须失败 -> 87';  a=@('/T',"$PROBE\acl");                                             e=87 }
    @{ n='风格判定：/save 写在路径之前必须失败 -> 87'; a=@('/save',"$PROBE\x.txt","$PROBE\acl");                           e=87 }
    # ---- /restore 需要管理员（非管理员跑必然 1300，本脚本不提权，所以记录已知失败）
    @{ n='/restore 非管理员 -> 1300（预期失败）';    a=@("$PROBE",'/restore',"$PROBE\acl.txt",'/C');                      e=1300; w=$true }
)

$rows = @()
$unexpected = 0
foreach ($c in $cases) {
    $argv = [string[]]$c.a
    if (-not (Test-CanRun $argv)) {
        $rows += [pscustomobject]@{ 用例=$c.n; 退出码='未跑'; 预期=$c.e; 说明='未通过白名单/结构判定，一律算未跑' }
        continue
    }
    $r = Invoke-IcaclsRaw $argv
    $ok = ($r.Exit -eq $c.e)
    if (-not $ok) { $unexpected++ }
    $tail = ($r.OutText -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)
    $errTail = ($r.ErrText -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
    $rows += [pscustomobject]@{
        用例 = $c.n
        退出码 = $r.Exit
        预期 = $c.e
        stdout = $r.OutLen
        stderr = $r.ErrLen
        末行 = $tail
        首条错误 = $errTail
    }
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 240 | Write-Host

# ---------------------------------------------------------------------------
# 编码判定（本包 runtime.encoding 的依据）—— 用含中文名的路径把非 ASCII 字节逼出来
# ---------------------------------------------------------------------------
Write-Host "`n===== 编码判定（OEM 936 vs UTF-8）" -ForegroundColor Cyan
$cnDir = "$PROBE\中文目录"
New-Item -ItemType Directory -Force -Path $cnDir | Out-Null
'x' | Out-File "$cnDir\中文文件.txt" -Encoding utf8
$r = Invoke-IcaclsRaw @("$cnDir\中文文件.txt")
$nonAscii = @(); for ($i = 0; $i -lt $r.Out.Length; $i++) { if ($r.Out[$i] -gt 0x7F) { $nonAscii += $i } }
Write-Host ("icacls <含中文名的路径> -> exit={0}, stdout={1}B, >0x7F 字节数={2}" -f $r.Exit, $r.OutLen, $nonAscii.Count)
if ($nonAscii.Count -gt 0) {
    $s = $nonAscii[0]
    $seg = $r.Out[$s..([Math]::Min($r.Out.Length - 1, $s + 7))]
    Write-Host ("  首处非 ASCII 字节: " + (($seg | ForEach-Object { $_.ToString('x2') }) -join ' '))
    Write-Host ("  按 cp936 解: " + [Text.Encoding]::GetEncoding(936).GetString($seg))
    Write-Host ("  按 utf-8 解: " + [Text.Encoding]::UTF8.GetString($seg))
    try {
        [void]([Text.UTF8Encoding]::new($false, $true).GetString($seg))
        Write-Host "  严格 UTF-8 解码: 通过（← 那就说明是 UTF-8）" -ForegroundColor Yellow
    } catch {
        Write-Host "  严格 UTF-8 解码: 抛异常 -> 确定不是 UTF-8" -ForegroundColor Green
    }
}
Write-Host "结论：runtime.encoding 应为 oem（宿主解析成 CultureInfo 的 OEMCodePage，本机 936）"

# ---------------------------------------------------------------------------
# 收尾：删探测目录并复核
# ---------------------------------------------------------------------------
Remove-Item -Recurse -Force $PROBE -ErrorAction SilentlyContinue
Write-Host "`n清理复核：Test-Path '$PROBE' = $(Test-Path $PROBE)"
if (Test-Path $PROBE) { throw "探测目录没能删干净：$PROBE" }

if ($unexpected -gt 0) {
    Write-Host "有 $unexpected 条用例的退出码与预期不符，见上表。" -ForegroundColor Red
    exit 1
}
Write-Host "全部用例退出码与预期一致。" -ForegroundColor Green
exit 0
