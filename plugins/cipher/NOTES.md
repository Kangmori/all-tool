# cipher 工具包 —— 实测记录（NOTES）

> **公共事实先看** [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)：
> Windows 自带命令的编码不统一、退出码几乎都不是「0 = 成功」、帮助写 stdout/stderr 的差别、
> 管理员权限的真实症状都在那里。本文件只写 cipher 特有的部分。
>
> 实测环境：Windows 11 `10.0.26200` x64（`[Environment]::OSVersion`），账号 `Steve`，
> `IsInRole(Administrator)` = **False**（非管理员）。实测日期 2026-10-01。
> 命令：`C:\Windows\System32\cipher.exe`，文件版本 **10.0.26100.8875 (WinBuild.160101.0800)**。

---

## 0. 一句话结论

cipher 的**只读**用法（看加密状态）很好用、也不需要管理员；它的**写**用法
（`/e` `/d` `/w` `/k` `/r`）在本工具包里**只摆命令、绝不代跑**（`execution: info` + `danger`）。
本包收录 9 个动作，其中 4 个宿主会真跑、5 个只显示命令。

| 覆盖范围 | 数字 |
|---|---|
| 动作 | **9**（只读 4 / 只显示命令 5） |
| 字段 | **13**（字段出处标注 13/13 = 100%） |
| 清单里出现过的开关 | `/H` `/C` `/S:` `/Y` `/E` `/D` `/W:` `/K` `/R:` |
| 真机跑过的 argv | **25** 条（见 §11） |
| 没跑过的 argv | 5 个写类动作的全部命令（红线，见 §10） |

---

## 1. 参数知识来源

| # | 来源 | 取得日期 | 适用版本 | 备注 |
|---|---|---|---|---|
| 1 | Microsoft Learn `cipher` 页<br>`https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cipher` | 2026-10-01 | Win10/11、Server 2016–2025、Azure Local 2311.2+ | HTTP 200、正文 **53477** 字节、Last updated **2023-02-03**；锚点只有 `#syntax` `#parameters` `#remarks` `#examples`；**没有退出码章节、没有权限说明** |
| 2 | 本机 `cipher /?` | 2026-10-01 | cipher.exe 10.0.26100.8875 | 写 **stdout**、stderr 0 字节、**退出码 1**；中文 4088 字节 / 英文 5455 字节（语言随控制台代码页变，见 §4）。**未入库**，随时可重取 |
| 3 | 本包真机冒烟 | 2026-10-01 | 同上 | `plugins/cipher/smoke.ps1`（25 条只读 argv + 红线自检），原始结果见 §11 |

**两边逐条核对的口径**：开关名（`/b /c /d /e /h /k /r /s /u /w /x /y /adduser /removeuser /rekey`）
与冒号形式（`/s:<directory>`）两边一致 → 收录；只有一侧有的 → 不收录（§5）。

---

## 2. 动作总表

| # | 动作 id | 命令（argv 形态） | 字段 | danger | execution | 实跑？ |
|---|---|---|---|---|---|---|
| 1 | `list-state` | `cipher [/H] <pathname…>` | 2 | — | run | ✅ 真跑 |
| 2 | `list-tree` | `cipher /S:<目录>` | 1 | — | run | ✅ 真跑 |
| 3 | `show-file-info` | `cipher /C <pathname…>` | 2 | — | run | ✅ 真跑 |
| 4 | `show-cert` | `cipher /Y` | 1 | — | run | ✅ 真跑（本机 exit 1、无输出） |
| 5 | `encrypt` | `cipher /E <pathname…>` | 2 | overwrite | **info** | ❌ 红线 |
| 6 | `decrypt` | `cipher /D <pathname…>` | 2 | overwrite | **info** | ❌ 红线 |
| 7 | `wipe` | `cipher /W:<目录>` | 1 | **destructive** | **info** | ❌ 红线 |
| 8 | `new-key` | `cipher /K` | 1 | overwrite | **info** | ❌ 红线 |
| 9 | `recovery-key` | `cipher /R:<文件名>` | 1 | overwrite | **info** | ❌ 红线 |

`locate`：`cipher.exe`，`searchPaths` 含 `%SystemRoot%\System32` 与 `SysWOW64`。
**没有 `versionArgs` / `versionPattern` / `minVersion`**：cipher 没有版本开关（`/?` 是帮助、无参数运行有副作用），
按 windows-commands.md §4 的口径不编造版本命令；exe 文件版本只记在本文件里。

---

## 3. 环境事实（实测）

```
Get-Command cipher.exe → Application  C:\Windows\system32\cipher.exe
(Get-Item ...).VersionInfo.FileVersion → 10.0.26100.8875 (WinBuild.160101.0800)
[Environment]::OSVersion.VersionString → Microsoft Windows NT 10.0.26200.0
(WindowsPrincipal).IsInRole(Administrator) → False
chcp（外部 pwsh 的控制台默认输出代码页）→ 936
```

---

## 4. 输出编码：OEM 代码页（cp936）—— 以及一个**与 windows-commands.md §5.3 相反**的实测

`runtime.encoding: oem`。判据用**带中文的输出**（`cipher /?`）看**原始字节**：

| 条件 | 字节数 | >127 字节 | 严格 UTF-8 解码 | 结论 |
|---|---|---|---|---|
| **宿主同款**：`CreateNoWindow=true` + 重定向两个流 | **4088** | **2016** | **抛异常** | 中文 cp936（首 4 字节 `cf d4 ca be` =「显示」） |
| 控制台 CP=936（`cmd /c "chcp 936 & cipher /? > f"`） | 4088 | 2016 | 抛异常 | 与宿主同款**逐字节相同** |
| 控制台 CP=65001（`chcp 65001 …`） | 5455 | **0** | 通过 | 退回英文资源、纯 ASCII |
| 共享父控制台（`Start-Process -NoNewWindow`，控制台 CP=65001 时） | 5455 | 0 | 通过 | 同上 |

**结论与坑**：cipher 的**输出语言跟着"控制台输出代码页（GetConsoleOutputCP）"走**，
不是跟着"有没有控制台"走 —— 这与 `route` 的实测方向**相反**（windows-commands.md §5.3 记的是
route 有控制台→中文、无控制台→英文）。所以：
- **判这类程序的编码必须写清当时的控制台 CP**。作者第一次在设过
  `[Console]::OutputEncoding = UTF8`（= 把控制台 CP 设成 65001）的会话里跑 `cipher /?`，
  得到的是**纯 ASCII 英文**，差点得出"cipher 输出是 utf-8"的错误结论；
- 宿主的真实条件（`src/AllTool.Core/Execution/ProcessRunner.cs:99-113`：`CreateNoWindow = true`
  + 重定向 + 立刻关掉 stdin）会让子进程拿到一个**新的隐藏控制台**，其 CP = 系统 OEM = 936，
  于是宿主看到的就是 cp936 中文 —— 所以 `oem` 是对的，且换成英文（437）/日文（932）Windows 同样正确。

复现命令（`plugins/cipher/smoke.ps1` 里已自动化，跑完会把控制台 CP 恢复原值）：

```powershell
$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName='C:\Windows\System32\cipher.exe'; $psi.UseShellExecute=$false
$psi.CreateNoWindow=$true; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
$psi.RedirectStandardInput=$true; [void]$psi.ArgumentList.Add('/?')
$p=[Diagnostics.Process]::Start($psi); $p.StandardInput.Close()
$ms=[IO.MemoryStream]::new(); $p.StandardOutput.BaseStream.CopyTo($ms); $p.WaitForExit()
$b=$ms.ToArray(); "$($b.Length) / $(($b|?{$_ -gt 127}).Count)"   # → 4088 / 2016
```

---

## 5. 与官方文档对不上的地方（逐条）

> 口径（playbook §0.1）：**字段依据以官方文档为准，行为以实测为准，差异记在这里。**

### 5.1 本机 `/?` 比官方页多 / 少的开关

| 项 | 官方页 | 本机 `cipher /?` | 本包怎么处理 |
|---|---|---|---|
| `/K` 的取值 | `cipher /k`（**没有**取值） | `CIPHER /K [/ECC:256\|384\|521]` + "If ECC is specified, a self-signed certificate will be created with the supplied key size." | **不收 `/ECC`**（出处只有本机一侧，且属写类动作参数） |
| `/R` 的取值 | `/r:<filename> [/smartcard]` | `/R:filename [/SMARTCARD] [/ECC:256\|384\|521]` | 收 `/R:<filename>`（两边都有）；**不收 `/SMARTCARD` `/ECC`** |
| `/P` | **完全没有** | `CIPHER /P:filename.cer`（Creates a base64-encoded recovery-policy blob…） | **不收**（只有本机一侧） |
| `/FLUSHCACHE` | **完全没有** | `CIPHER /FLUSHCACHE [/SERVER:servername]` | **不收**（只有本机一侧） |
| `/ADDUSER` 的 `/USER:` | `/adduser [/certhash:<hash> \| /certfile:<filename>]` | `[/CERTHASH:hash \| /CERTFILE:filename \| /USER:username]` | **不收** `/USER:`（只有本机一侧） |
| `/R` 的大小写写法 | `/r:<filename>`（小写 + 冒号） | `/R:filename`（大写 + 冒号，语法行全大写） | 收 `/R:` —— 冒号形式两边一致，且**实测开关大小写不敏感**（`/S:` 与 `/s:` 都 exit 0） |
| `/E` 的说明 | "Directories are marked so that files that are added afterward will be encrypted." | 多一句 "It is recommended that you encrypt the file and the parent directory." | 两边意思都在（官方把这条放在 Remarks 第一条），清单的 help 同时写了两层语义 |
| `/Y` 的措辞 | "Displays your current EFS certificate **thumbnail**" | "Displays your current EFS certificate **thumbprint**" | 按本机（thumbprint）理解；官方那处是笔误，照抄不改、在本文件记着 |
| 退出码 | 页面上**没有任何退出码章节**（已逐行核对） | `/?` → 1 | `exitCodes` 全部来自实测（§7） |
| 权限要求 | 页面**一个字都没写** | 只读动作非管理员全部成功 | 见 §8 |

### 5.2 行为层面的实测差异

| 项 | 文档怎么说 | 实测 | 影响 |
|---|---|---|---|
| `/c <不存在的文件>` | 没说 | **exit 0**，只是清单里少了那一条，不报错 | `exitCodes` 的 code 0 里写明了 |
| `/c` 是旗标还是取值 | 语法行 `[/E \| /D \| /C] … [pathname [...]]`（= 旗标 + 位置参数） | 一致：`/c <文件>` → 0；**`/c:<文件>` → exit 1 + 打印帮助** | 字段用 `flag` + `positional` |
| `/s` 只认单 token | 语法行写 `/s:<directory>` | 一致：`/s:<目录>` → 0；`/c /s <目录>` → **exit 1 + 打印帮助** | 字段用 `attached` + `separator: ":"` |
| `/s:` 是否递归 | 官方 "/s:<directory> Performs the specified operation on all subdirectories in the specified directory."；本机 "on the given directory and all files and subdirectories within it" | **是**：`/s:<根>` 的输出里出现了 `清单 <根>\`、`清单 <根>\fs\`、`清单 <根>\fs\sub\`、`清单 <根>\sub\` 四段 | 动作说明里写"逐层列出来" |
| 无参数 `cipher` 的范围 | "displays the encryption state of the **current directory** and any files it contains" | 一致，但"当前目录"是**进程的工作目录**：同一台机器上把工作目录设成探测目录 → 477 B；设成 `%TEMP%` → **89309 B**（TEMP 很大） | 见 §9 第 4 条：本包不做"裸 cipher"动作 |
| `/y` 没有 EFS 证书时 | 没说 | **exit 1、stdout 0 字节、stderr 0 字节**（连一句提示都没有） | `show-cert` 的 resultNote 写明 |

---

## 6. 字段风格与顺序（实测判定）

### 6.1 风格：`attached`（`/S:`）vs `separate`（`/s` 值）—— 同一台机器上各跑一次

| 写法 | argv | 结果 |
|---|---|---|
| attached（官方语法） | `cipher /c /s:<目录>` | **exit 0**，607 字节清单 |
| separate | `cipher /c /s <目录>` | **exit 1** + 打印整份帮助（4088 字节） |
| attached（把值粘给 `/c`） | `cipher /c:<文件>` | **exit 1** + 打印整份帮助 |
| 旗标 + 位置参数 | `cipher /c <文件>` | **exit 0**，117 字节 |

→ **结论**：cipher 只对 `/S:` `/W:` `/R:` 这几个开关认冒号粘值写法；`/C` `/H` `/B` `/E` `/D` `/K` `/Y`
都是旗标，路径是位置参数。本包因此只用两种字段风格：`flag`（旗标）与
`attached`+`separator: ":"`（`/S:` `/W:` `/R:`），位置参数用 `positional`。
**没有用到 `separate`**（写成两 token 会被 cipher 直接拒掉）。

### 6.2 顺序与大小写

- **开关大小写不敏感**：`/C <文件>` 与 `/c <文件>` 输出相同（117 字节）；`/S:<目录>` 与 `/s:<目录>` 都 exit 0。
- **顺序**：`/c /s:<目录>` 与 `/s:<目录> /c` 都是 exit 0、607 字节 → 开关之间无顺序要求。
  本包统一按官方语法行的顺序把**开关字段声明在位置参数之前**（字段声明顺序 = argv 顺序）。

---

## 7. 退出码（全部来自实测，含失败路径）

| code | severity | 实测情形（原始结果） |
|---|---|---|
| **0** | ok | `cipher`（无参数，工作目录=探测目录）→ 0；`cipher <文件>` → 0；`cipher <目录>` → 0；`cipher <目录>\*` → 0；`cipher <目录>\*.txt` → 0；`cipher /c <文件>` → 0；`cipher /c <中文名文件>` → 0；`cipher /c <不存在的文件>` → **0**；`cipher /c <目录>` → 0；`cipher /h` → 0；`cipher /b /c <文件>` → 0；`cipher /s:<目录>` → 0；`cipher /S:<目录>` → 0 |
| **1** | error | ① **帮助输出**：`/?` → 1（4088 B 中文）；`/zz` → 1 + 帮助；`/c:<文件>` → 1 + 帮助；`/c /s <目录>` → 1 + 帮助；② **路径问题（写 stderr）**：`/s:`（空值）→ 1 + stderr `: 文件名、目录名或卷标语法不正确。`（37 B）；`/s:<不存在的目录>` → 1 + stderr `<路径>: 系统找不到指定的文件。`（94 B）；`/s:<文件>` → 1 + stderr `<路径>: 目录名称无效。`（91 B）；③ `cipher /y`（本机无 EFS 证书）→ 1、**两个流都 0 字节** |

**除 0 / 1 外，本包没有观察到别的退出码**（没有 `net helpmsg` 那套 Win32 码的直接暴露；
上表 stderr 里的中文对应的是 Win32 123 / 2 / 267 的错误文本，但**进程退出码仍是 1**）。

---

## 8. 管理员权限：只读全部不需要；写类**没有实测、也没有标**

- 本机账号 `Steve` **不是管理员**（`IsInRole(Administrator)` = False）。
- §11 的 25 条只读 argv **全部按预期完成**（没有一条因为权限失败）→ `runtime.requiresAdmin: false`，
  动作级一个都没标。
- 五个写类动作（`/e` `/d` `/w` `/k` `/r`）**没有实跑**（红线，§10），官方页也**没写**权限要求，
  所以按规范 §2.5「不确定就不标」——**一个都没标 `requiresAdmin`**。
  这是刻意的取舍：标错会让用户以为"必须提权"而放弃，漏标最多是失败后看到一句权限错误。

---

## 9. 故意没做的部分（给后来者的地图）

1. **`/X`（备份 EFS 证书与密钥到 .pfx）**：它会写出**含私钥**的文件、且会覆盖同名文件。
   它不是本任务点名的红线开关，但属于"写 + 私钥落地"，本轮不收录。
2. **`/U [/N]`（遍历本地盘上所有加密文件）**：`/U` 不带 `/N` 会**更新密钥**（写操作）；
   带 `/N` 虽然是只读，但要扫遍所有本地卷，又慢又难界定范围 → 不收录。
3. **`/P`（生成 base64 恢复策略 blob）**、**`/FLUSHCACHE`**、**`/ADDUSER`**、**`/REMOVEUSER`**、
   **`/REKEY`**：都是改动加密配置的动作；其中 `/P` `/FLUSHCACHE` 还**只有本机帮助一侧有出处**
   （§5.1）→ 一律不收录。
4. **"裸 `cipher`"（无参数、按工作目录列清单）没有做成独立动作**：实测它的范围就是
   **进程的工作目录**（把工作目录设成 `%TEMP%` 时输出 89309 字节）。规范里
   `workingDirectory: userSelected` 的取值来自一个**字段**（`workingDir` / `outputDir`），
   而任何字段都会按自己的 `style` 产出 argv —— v1 表达不了"只设工作目录、不产出参数"。
   想要那个效果的用户可以在「查看加密状态」里填 `<目录>\*`（实测与"把工作目录设过去再跑
   `cipher`"的输出**逐字节相同**）。
5. **`/ECC` 取值、`/SMARTCARD`**：只有本机一侧有出处（§5.1），不收。
6. **五个写类动作不给 `examples`**：宿主的「在终端中打开」是 `cmd /k <完整命令行>`
   （实测源码 `src/AllTool.App/MainWindow.xaml.cs:979`），而 **`cmd /k` 会立刻执行**。
   给一个能一键跑起来的预设（例如 `/W:C:\`）= 把不可逆操作交给一次点击。会话型动作才是
   "开一个空终端 + 把命令复制到剪贴板"（同文件 945-971）。**这条值得宿主侧复核**：
   `execution: info` 的界面文案写的是"不执行、只摆命令"，但 `cmd /k` 那条路是真执行。

---

## 10. 没有验的部分与原因（诚实清单）

| 没验的东西 | 原因 |
|---|---|
| `/e` `/d` `/w` `/k` `/r` 的**全部**行为（输出、退出码、权限、耗时） | **红线**：任务明确禁止执行 `/w`（不可中断）、`/d`、`/e`、`/k`、`/r`。清单里这五个动作只有 `execution: info`，也没有 `examples` |
| `/w:` `/r:` 的**字段风格**（attached 是否真的对） | 同上（跑一次就等于执行红线命令）。清单按**官方语法行** `/w:<directory>`、`/r:<filename>` 写 `attached` + `separator: ":"`，并借用同一命令里 `/s:` 的实测结论（**同一条程序对"带冒号的值"只认单 token**）。这是**推定，不是实测** |
| `/c` 在**已加密文件**上的输出（"Displays information on the encrypted file"到底多显示什么） | 造一个加密文件必须跑 `/e`（红线）。实测只知道：**未加密文件上 `/c` 与不加 `/c` 的输出逐字节相同**（都是 117 字节） |
| `/y` 在**有** EFS 证书时的输出 | 本机账号没有 EFS 证书（exit 1、0 字节）。要造一张必须跑 `/k`（红线） |
| 写类动作是否需要管理员 | 没有实跑；官方页也没写（见 §8） |
| `/e` `/d` 的 `resultNote` 里"输出形态" | 官方没给 `/d` 的示例输出；`/e` 的示例输出是官方的（不是本机实测），已在 resultNote 里标注来源 |

---

## 11. 真机冒烟结果（`plugins/cipher/smoke.ps1`）

复跑：`pwsh -NoProfile -File plugins/cipher/smoke.ps1`（自带两层红线自检 + 负向对照，收尾自动删临时目录）。

```
cipher 只读冒烟（C:\WINDOWS\System32\cipher.exe）
[ OK ] /?（帮助）                            exit=1     [ OK ] /s:<目录>（递归）              exit=0
[ OK ] 无参数（列当前目录）                     exit=0     [ OK ] /S:<目录>（大小写不敏感）        exit=0
[ OK ] /c <未加密文件>                        exit=0     [ OK ] /s:<不存在的目录>              exit=1
[ OK ] /c <中文名文件>                        exit=0     [ OK ] /s:<文件而不是目录>             exit=1
[ OK ] /c <不存在的文件>（实测不报错）            exit=0     [ OK ] /s:（空值）                   exit=1
[ OK ] /c <目录>                             exit=0     [ OK ] <文件路径>                    exit=0
[ OK ] /h（无参数）                           exit=0     [ OK ] <目录>\*（列目录内容）           exit=0
[ OK ] /b /c <文件>（两个 flag 组合）            exit=0     [ OK ] <目录>\*.txt                 exit=0
[ OK ] /zz（无法识别的开关 → 打印帮助）           exit=1     [ OK ] example: hosts 文件           exit=0
[ OK ] /c:<文件>（把值粘给 /c → 打印帮助）        exit=1     [ OK ] example: hosts 所在目录的 *    exit=0
[ OK ] /c /s <目录>（/s 两 token → 打印帮助）     exit=1     [ OK ] /S:drivers\etc              exit=0
                                                       [ OK ] /c hosts                    exit=0
                                                       [ OK ] /y（本机无 EFS 证书 → 1）       exit=1

编码断言：宿主同款进程 → 4088 字节、2016 个 >127 字节、严格 UTF-8 解码=False   [ OK ]
          控制台 CP=936 → 4088/2016；控制台 CP=65001 → 5455/0（跑完恢复原 CP）
临时对象申报：C:\Users\Steve\AppData\Local\Temp\__AllToolProbe_cipher → Test-Path = False
小结：通过 25 / 失败 0 / 跳过 0 / 环境性警告 0
```

冒烟里**只跑只读 argv**；写类开关（`/e /d /w /k /r /u /x /p /rekey /adduser /removeuser /flushcache`）
在脚本里是一张"禁用表"，并且**带负向对照**：脚本启动时会先拿 `/w:C:\`、`/e`、`/d`、`/k`、`/r:X`、
`/REKEY`、`/adduser` 这些**纯字符串探针**去测自检本身，测不出禁用就 `throw` 中止
（playbook §5.1：白名单判定自己就是风险点）。每条用例在**启动进程之前**还会用同一套判定再确认一次。

---

## 12. 临时对象申报（造了什么 / 何时删的 / 怎么复核）

| 临时对象 | 用途 | 何时删的 | 怎么复核 |
|---|---|---|---|
| `%TEMP%\__AllToolProbe_cipherfindstr\`（含 `plain.txt`、`探测中文.txt`、`sub\c.txt`、`sub\inner.txt` 与若干探测输出 txt） | 本次所有探测的脚步样本与输出落盘 | 探针跑完后、写 NOTES 之前用 `Remove-Item -Recurse -Force` 删除 | `Test-Path "$env:TEMP\__AllToolProbe_cipherfindstr"` → **False** |
| `%TEMP%\__AllToolProbe_cipher\`（含 `plain.txt`、`探测中文.txt`、`sub\inner.txt`） | `smoke.ps1` 的样本目录 | 每次跑完 `smoke.ps1` 自动删除（脚本末尾会打印 `Test-Path`） | 再跑一次 `smoke.ps1`，看末行 `临时对象申报：… → Test-Path = False` |

**没有创建、修改或删除任何用户数据**：所有样本文件都新建在 `%TEMP%` 下；
只读命令里对系统路径的访问（`C:\Windows\System32\drivers\etc\hosts` 等）**全部是读取**。
**没有跑过任何一次 `/e` `/d` `/w` `/k` `/r`**（这也是本包唯一没做的实测，见 §10）。

---

## 13. 给宿主的反馈（本包发现的、不属于清单的问题）

1. **`execution: info` + 「在终端中打开」= 真执行**：`MainWindow.xaml.cs:979` 用 `cmd.exe /k <命令>`，
   `cmd /k` 会**立刻执行**那条命令。界面文案说"这个命令会改动系统状态…这一步该由你自己按下回车"，
   语义上说得通（用户点两次），但对 cipher `/w` 这种"不可逆且不可中断"的命令，
   "打开终端就开跑"与"复制命令、开一个空终端让用户自己粘贴"相比风险明显更大。
   会话型动作走的是后一条路（同文件 948-971）——建议把 non-session 的 `info` 也对齐过去。
   （本包已据此**不给写类动作任何 examples**，见 §9 第 6 条。）
2. **`/y` 这类"没有输出也没有提示"的动作**在界面上表现为"命令结束了，什么都没有"。
   本包用 `resultNote` 说明，但如果宿主能显示退出码，用户体验会更好（`exitCodes` 已把 1 的意义写清）。
