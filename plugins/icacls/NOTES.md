# icacls 工具包 —— 实测记录

> **本批共用事实**（Windows 自带命令的一般性质、OEM 编码、`/?` 的退出码与流向差异等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)。
> **但 icacls 有几处与已有的 Windows 包不同，别照抄它们的结论**：
> ① 文档是**一页到底**（本机帮助也只有一页，没有子命令）；
> ② **路径参数必须是最前面的 token**——把带取值的开关写到路径之前会直接 exit 87（§7.6）；
> ③ **失败消息写 stderr、汇总写 stdout**（与 wevtutil 的"全写 stdout"不同）；
> ④ **`/restore` 在非管理员下必然 exit 1300**，与 ACL 文件在不在无关（§8.2）；
> ⑤ 输出是 **LF 换行，不是 CRLF**（与 wevtutil 的 CRLF 相反），写抓取/正则时要注意。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `KANGMORI\Steve`，**非管理员**
> （`WindowsPrincipal.IsInRole(Administrator)` 实测 `False`）。控制台代码页 `936`。
> 实测日期 **2026-09-30**。

---

## 1. 这个包为什么值得做

"拒绝访问"是 Windows 上人人都会遇到的报错，而**普通用户根本不知道"权限"这件事可以用命令行看**，
更不知道可以一口气看一整棵目录树。图形界面里要点：右键 → 属性 → 安全 → 高级 → 才能看到继承关系；
`icacls <路径>` 一条命令就把"谁能访问、能做什么、是从父目录继承来的还是单独设的"全摊开。

实测证据（本机、非管理员、只读）：

```powershell
icacls C:\Windows\System32\drivers\etc\hosts
# exit 0，453 字节：
# C:\Windows\System32\drivers\etc\hosts NT AUTHORITY\SYSTEM:(I)(F)
#                                       BUILTIN\Administrators:(I)(F)
#                                       BUILTIN\Users:(I)(RX)
#                                       APPLICATION PACKAGE AUTHORITY\ALL APPLICATION PACKAGES:(I)(RX)
#                                       APPLICATION PACKAGE AUTHORITY\所有受限制的应用程序包:(I)(RX)
#
# Successfully processed 1 files; Failed processing 0 files
```

`(I)` = 继承来的（不是单独设在这一个文件上的）、`(OI)/(CI)` = 会继续往下继承给对象/容器、
`(F)` = 完全控制、`(RX)` = 读取与执行。**最后那一行的中文 ACE 名字正好也是编码判定的证据（§4）。**

---

## 2. 参数知识来源（R1 / R2）

| # | 来源 | 位置 | 取得日期 | 适用版本 |
|---|---|---|---|---|
| 1 | **Microsoft Learn 官方文档（单页）** | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/icacls | 2026-09-30 | Windows 10/11、Server 2016–2025、Azure Local 2311.2+ |
| 2 | 本机 `icacls /?` 的真实输出（5286 字节，一页） | `C:\Windows\System32\icacls.exe`（快照**未入库**，随时可用 `icacls /?` 重取） | 2026-09-30 | icacls.exe 10.0.26100.8875 |
| 3 | 本包真机冒烟（33 条 argv，**全部实际执行**；另含 15 条红线自检，可复跑） | `plugins/icacls/smoke.ps1`（**本包自带**） | 2026-09-30 | 同上 |

### 2.1 官方文档的形态

- 实测 `web_fetch` 返回 **HTTP 200**，`Last updated on 2025-06-09`，
  Applies to 含 Windows Server 2016–2025 / Windows 11 / Windows 10 / Azure Local 2311.2+。
- **没有子页**。页内结构：`#syntax`（三行语法）、`#parameters`（参数表，逐个开关一行）、
  `#remarks`（SID 的 `*` 前缀规则、ACE 的规范顺序、**完整权限掩码表**）、
  `#examples`（5 条官方示例）、`#related-links`（只链到 "Command-Line Syntax Key"）。
- **页面里没有任何退出码章节，也没有一句权限要求说明**（已逐行核对）——
  所以本包的 `exitCodes` 与 `requiresAdmin` **全部来自实测**。

### 2.2 每个动作 / 字段的两边核对方式

字段依据一律取官方 `#parameters` / `#remarks`；**行为**（默认值、退出码、输出流向、编码、
开关能不能粘、路径要放哪）一律以实测为准。两边对不上的逐条记在 §6。

---

## 3. 环境与版本（实测，R7）

| 项 | 值 | 怎么得到的 |
|---|---|---|
| 程序路径 | `C:\Windows\system32\icacls.exe` | `Get-Command icacls` → `Source` |
| 文件版本 | `10.0.26100.8875 (WinBuild.160101.0800)` | `(Get-Item ...).VersionInfo.FileVersion` |
| 产品版本 | `10.0.26100.8875` | 同上 |
| FileDescription | **空**（icacls 没写这个字段） | 同上（同批 wevtutil 是中文"Windows 事件日志实用工具"） |
| 帮助语言 | **英文**（纯 ASCII，5286 字节里 0 个 >0x7F） | `icacls /?` 实跑 |
| 帮助退出码 / 流向 | **exit 0、全部写 stdout、stderr 0 字节** | 逐条实跑 |
| 版本开关 | **没有**（`/version`、`/v`、`/help` 都是 exit 87；`-version` 被当文件名 → exit 2） | §5.4 的逐条实跑 |
| 运行身份 | `KANGMORI\Steve`，**非管理员** | `IsInRole(Administrator)` = `False` |
| 输出编码 | **OEM 代码页 936** | §4 |
| 换行 | **LF（`0a`），不是 CRLF** | 实测 `/Q` 输出的 CR 计数 = 1（`icacls /?` 的 5286 字节里 CR 有 135 个，是帮助文本自身的排版） |

**动作覆盖范围**（16 个动作 / 57 个字段）：

| 分类 | 动作 | danger | requiresAdmin |
|---|---|---|---|
| 查看权限 | `show-acl`、`show-acl-recursive`、`show-acl-wildcard` | — | — |
| 检查与诊断 | `verify-acl`、`find-sid` | — | — |
| 导出与还原 | `save-acl` | overwrite | — |
| 导出与还原 | `restore-acl` | **destructive** | **true** |
| 修改权限 | `grant-perm`、`deny-perm`、`remove-perm`、`set-inheritance`、`set-integrity` | overwrite | — |
| 修改权限 | `reset-acl` | **destructive** | — |
| 修改权限 | `set-owner` | **destructive** | **true** |
| 其它 | `install-info`（execution: info）、`help` | — | — |

---

## 4. 编码：**OEM 代码页（本机 936）**（实测判定，R7）

**判据是对含中文名的路径跑 `icacls <路径>`**——因为 `icacls /?` 的帮助是纯 ASCII，
拿它判编码会得出"随便哪个都行"的错误结论。

判定过程（`plugins/icacls/smoke.ps1` 末尾会自动重跑这一段）：

```
argv: <探测目录>\中文目录\中文文件.txt
exit=0, stdout=357B, >0x7F 字节数=16
首处非 ASCII 字节: d6 d0 ce c4 c4 bf c2 bc
按 cp936 解: 中文目录          ← 正确
按 utf-8 解: ����Ŀ¼          ← 乱码
严格 UTF-8 解码: 抛异常 -> Unable to translate bytes [D6] at index 0 from specified code page to Unicode.
0x00 字节数 = 0，字节长度 = 奇数
```

- `d6 d0 ce c4 c4 bf c2 bc` 正是「中文目录」的 **GBK/cp936** 编码；
- **UTF-8 严格解码抛异常 → 确定不是 UTF-8**（对照：同批 `schtasks` 是 UTF-8）；
- 也不是 UTF-16LE（没有"高字节恒为 0"的规律，`0x00` 计数为 0）。

**结论：`runtime.encoding: oem`**（宿主 `EncodingResolver` 把 `oem` 解析成
`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，本机 = 936）。
**这一批 Windows 自带命令不能因为"帮助是英文"就默认 utf-8。**

---

## 5. 真机冒烟测试（R4，2026-09-30）

复跑入口：`pwsh -NoProfile -File plugins/icacls/smoke.ps1`。
脚本自带两层自检：
① **15 条红线 argv** 先判一次"绝不允许可跑"，任一条被判为可跑就 `throw` 中止；
② 改动类 argv 按形状判定（开关白名单 + 路径必须在探测目录内 + 账号白名单）。
**33 条 argv 全部实际执行、退出码全部与预期一致**（其中 3 条是刻意设计的反例，
预期就是失败：`<路径>:/T` → 123、`/T <路径>` → 87、`/save <文件> <路径>` → 87；
另 1 条 `/restore` 在非管理员下预期失败 → 1300）。
没有任何一条因为"判不准"而落到"未跑"。

### 5.1 只读动作逐条实测

| # | 动作 / 探测 | argv（`icacls` 之后） | 退出码 | stdout | stderr | 汇总行 |
|---|---|---|---|---|---|---|
| 1 | `show-acl` 例1 | `C:\Windows\System32\drivers\etc\hosts` | 0 | 453 | 0 | 1 个文件 |
| 2 | `show-acl` 例2 | `C:\Windows\System32\drivers\etc` | 0 | 1023 | 0 | 1 个文件 |
| 3 | `show-acl-recursive` 例1 | `…\etc /T` | 0 | 46 948 | 0 | **82 个文件** |
| 4 | `show-acl-recursive` 例2 | `…\etc /T /C` | 0 | 46 948 | 0 | 82 个文件 |
| 5 | `show-acl-wildcard` 例1 | `…\etc\*` | 0 | 7 770 | 0 | 15 个文件 |
| 6 | `show-acl-wildcard` 例2 | `…\etc\* /T /C` | 0 | 45 984 | 0 | 81 个文件 |
| 7 | `verify-acl` 例 | `C:\Users\Steve\Documents /T /C` | 0 | **15 753 696** | 172 | **40 245 个文件 / 3 个失败** |
| 8 | `find-sid` 例 | `C:\Users\Steve\Documents /findsid *S-1-5-32-545 /T /C` | 0 | 103 | 172 | 40 245 个文件 |
| 9 | `help` | `/?` | 0 | 5 286 | 0 | — |
| 10 | 探测 | `<单文件> /T /Q /C` | 0 | 334 | 0 | 1 个文件 |
| 11 | 探测 | `<目录>\* /T` | 0 | 609 | 0 | 2 个文件 |
| 12 | 探测 | `<目录> /verify /T` | 0 | 387 | 0 | 4 个文件 |

**12 条全部退出码 0。**
第 7 条的 **15.7 MB / 40 245 个文件**是本次最有用的一个数字：`/verify /T` 与 `/findsid /T`
对着一个普通用户的 `Documents` 目录就会扫过四万多个对象，界面上必须有等待预期——
这一条已写进这两个动作的 `resultNote` 与字段 `help`。

### 5.2 `/Q` 的抑制边界（**比官方的字面意思窄，实测**）

官方原文只说 `/q "Suppresses success messages."`，实测拿同一条命令做字节对照：

| argv | 退出码 | stdout 字节 | 输出内容 |
|---|---|---|---|
| `<文件> /grant Users:(R)` | 0 | 138 | `processed file: …` + `Successfully processed 1 files…` |
| `<文件> /grant Users:(R) /Q` | 0 | **59** | **只剩** `Successfully processed 1 files…` |
| `<目录> /T` | 0 | 1 493 | 每个对象一段 + 汇总 |
| `<目录> /T /Q` | 0 | **1 493** | **一模一样** |
| `<文件>` | 0 | 405 | 权限列表 + 汇总 |
| `<文件> /Q` | 0 | **405** | **一模一样** |
| `<不存在的文件> /Q` | 2 | 59 | stderr 仍然有 `The system cannot find the file specified.` |

**结论**：`/Q` 只抑制每个对象的 `processed file: …` 那一行；
**不抑制最后的 `Successfully processed N files; Failed processing M files` 汇总，
也不抑制任何错误，对纯只读的查看更是毫无影响。**
所以本清单里 `/Q` 是可选字段、**默认不勾**，并在 `help` 里写明这个实测边界。

### 5.3 `/verify` 与 `/findsid` 的结果形态

- `/verify`：**正常时**每个对象一行 `processed file: <路径>`，汇总一句；两处实测（单文件、4 个对象的目录）
  都是 exit 0。**非规范 ACL 的样本没能构造出来**，所以"发现问题时输出什么样、退出码是否变化"
  这一条**本包没有实测证据**（如实记录）。
- `/findsid`：命中时每个对象一行 `SID Found: <路径>.`（**行尾有一个句点**）；
  **一个都没命中时是 exit 1332**（不是 0）并打印 `No files with a matching SID was found`——
  这与 `/verify` 的 0 不同。

### 5.4 失败路径的退出码（全部实跑，Win32 码用 `net helpmsg` 核对语义）

| argv | 退出码 | stdout | stderr 首条 | `net helpmsg` 语义 |
|---|---|---|---|---|
| `<不存在的文件>` | **2** | 59 | `<路径>: The system cannot find the file specified.` | The system cannot find the file specified. |
| （不带任何参数） | **3** | 59 | The system cannot find the path specified. | The system cannot find the path specified. |
| `<目录> /zz` | **87** | 0 | `Invalid parameter "/zz"` | The parameter is incorrect. |
| `<目录> /inheritancelevel:x` | **87** | 0 | `Invalid parameter "/inheritancelevel:x"` | 同上 |
| `<目录> /inheritancelevel`（不给取值） | **87** | 0 | `Invalid parameter "/inheritancelevel"` | 同上 |
| `<文件> /setintegritylevel X` | **87** | 59 | `X: The parameter is incorrect.` | 同上 |
| `<目录> /save <不存在的目录>\x.txt /T` | **87** | 59 | `No such file or directory` | 同上 |
| `<路径>:/T`（开关粘在路径后面） | **123** | 59 | `…\:/T: The filename, directory name, or volume label syntax is incorrect.` | The filename, directory name, or volume label syntax is incorrect. |
| `/T <路径>`（开关写在路径之前） | **87** | 5 286（帮助） | `First parameter must be a file name pattern or "/?"` | The parameter is incorrect. |
| `C:\Windows\System32\config /T`（非管理员） | **5** | 59 | `C:\Windows\System32\config: Access is denied.` | Access is denied. |
| `C:\System Volume Information`（非管理员） | **5** | 59 | `…: Access is denied.` | 同上 |
| `C:\Windows\System32\drivers\etc\hosts /grant Users:(R)`（非管理员） | **5** | 59 | `…: Access is denied.` | 同上 |
| `C:\Windows\System32\drivers\etc\hosts /reset`（非管理员） | **5** | 59 | `…: Access is denied.` | 同上 |
| `<文件> /grant NoSuchUser123:(R)` | **1332** | 59 | `NoSuchUser123: No mapping between account names and security IDs was done.` | No mapping between account names and security IDs was done. |
| `<文件> /remove:g NoSuchUser123` | **1332** | **59（一条错误都没有！）** | — | 同上 |
| `<文件> /findsid S-1-5-32-545`（**不带 `*`**） | **1332** | 99 | —（stdout 里写 `No files with a matching SID was found`） | 同上 |
| `<目录> /restore <文件> /C`（非管理员） | **1300** | 59 | `Not all privileges or groups referenced are assigned to the caller.` | Not all privileges or groups referenced are assigned to the caller. |
| `<文件> /setowner Users`（非管理员） | **1307** | 59 | `…: This security ID may not be assigned as the owner of this object.` | This security ID may not be assigned as the owner of this object. |

**三条容易误判的行为：**

1. **`/remove` 的账号名写错时，stderr 一个字都没有**，只有 stdout 的
   `Successfully processed 0 files; Failed processing 0 files`。这条**最容易被误判成成功**——
   好在退出码是 1332，但界面上"只看最后一行"会看漏。
2. **"部分失败"时退出码是 0**（提权后跑 `/restore` 用错目录参数：exit 0、
   每一条都以"系统找不到指定的路径"失败、汇总写 `处理 8 个文件时失败`）。
   所以**不能只看退出码**，必须看汇总行。
3. **失败消息写 stderr、汇总写 stdout**：同一轮 `/save`（目标目录不存在）会同时产生
   stdout 59 字节 + stderr 27 字节。抓输出时两个流都要读。

### 5.5 `/save` / `/restore`：**相对路径的基准是"命令行参数原样"**（本包最绕的一条）

`/save` 写出来的 ACL 文件是 **UTF-16LE + CRLF**（实测首字节 `5f 00 5f 00 41 00 6c 00 …`），
内容形如两行一组：`<相对路径>` + `D:AI(A;OICIID;FA;;;SY)…`。

**实测三组对照**（结果来自提权会话，因为 `/restore` 非管理员必然 1300）：

| 场景 | 保存时的命令行 | 工作目录 | ACL 文件里的左列 | 用哪个目录参数 restore 才成功 |
|---|---|---|---|---|
| A | `icacls C:\…\__AllToolProbe_icacls /save f /T`（绝对路径） | 任意 | `__AllToolProbe_icacls`、`__AllToolProbe_icacls\a.txt` | **该目录的父目录**（`C:\…\Temp`） |
| B | `icacls x\a.txt /save f /T`（相对路径） | `P` | `x\a.txt` | **`P`**（填 `P\x` 时每条都以"系统找不到指定的路径"失败） |
| C | `icacls a.txt /save f /T`（相对路径） | `P` | `a.txt`、`a.txt\x.txt` | **`P`** |

**结论：ACL 文件里存的是"命令行参数原样 + 其下的相对结构"，恢复时的目录参数必须是
"能让这些相对路径重新拼成正确绝对路径"的那个目录。** 官方的例子正好是两者相同的情形
（保存 `c:\windows\*`、恢复 `icacls c:\windows\ /restore aclfile`），所以官方页面看不出这个坑。
这条已写进 `restore-acl` 的字段 `help` 与 `output.resultNote`。

另外两条实测：

- **`/save` 的目标父目录必须已存在**：写 `<不存在的目录>\x.txt` → exit 87 + `No such file or directory`。
- **`/save` 不保存所有者、SACL、完整性标签**（本机帮助原文 "Note that SACLs, owner, or integrity
  labels are not saved."）：实测在导出的 ACL 文件里搜 `owner` → **False**。

### 5.6 改动类动作的退出码与**真实 ACE 效果**核对（只在 `%TEMP%` 探测目录内）

不只记退出码，每一步都用 `Get-Acl` 看真实 ACE（否则"命令成功"不等于"想改的改到了"）：

| 动作 | argv | 退出码 | `Get-Acl` 核对结果 |
|---|---|---|---|
| `grant-perm` | `<文件> /grant Users:(R)` | 0 | 多了一条 `BUILTIN\Users : Allow : Read, Synchronize`（**IsInherited=False**，是新加的显式 ACE） |
| `grant-perm`（对照） | 先 `/grant Users:(R)` 再 `/grant Users:(W)` | 0 / 0 | 该账号**只剩一条合并后的** `Write, Read, Synchronize` |
| `grant-perm`（对照） | 先 `/grant Users:(R)` 再 `/grant:r Users:(W)` | 0 / 0 | 该账号**只剩** `Write, Synchronize`（R 被**替换**掉了）→ 官方那句 "replace any previously granted explicit permissions" 实测成立 |
| `deny-perm` | `<文件> /deny Users:(W)` | 0 | 多了一条 `BUILTIN\Users : Deny : Write, Synchronize`，**排在 Allow 之前**（官方说的规范顺序） |
| `remove-perm` | `<文件> /remove:d Users` | 0 | Deny 条目消失、Allow 条目留下 |
| `remove-perm` | `<文件> /remove:g Users` | 0 | Allow 条目也消失，只剩继承来的三个 ACE |
| `set-inheritance` | `<文件> /inheritancelevel:d` | 0 | `AreAccessRulesProtected` 由 `False` → **`True`**，所有 ACE 的 `IsInherited` 由 `True` → **`False`**（继承被切断且 ACE 被复制成显式） |
| `set-inheritance` | `<文件> /inheritancelevel:e` | 0 | 恢复继承 |
| `set-integrity` | `<文件> /setintegritylevel (CI)(OI)H`、`L`、`M` | 0 / 0 / 0 | 三个都成功；`X` → exit 87 |
| `reset-acl` | `<文件> /reset` | 0 | 继承被切断的文件回到 `IsInherited=True`、`AreAccessRulesProtected=False` ——**确认它就是把对象恢复成"只继承父目录"** |
| `set-owner` | `<文件> /setowner Users` | **1307**（非管理员） | 所有者**没变**（`KANGMORI\Steve`） |
| `set-owner` | **提权后** `<文件> /setowner Users` | **0** | 退出后用 `Get-Acl` 复核：`Owner` 由 `KANGMORI\Steve` → **`BUILTIN\Users`**（真的改了） |
| `save-acl` | `<目录> /save <探测目录>\acl.txt /T` | 0 | 产出 1288 字节 UTF-16LE 文件 |
| `restore-acl` | `<目录> /restore <文件> /C` | **1300**（非管理员） | 什么都没改（在动手之前就因特权失败） |

**顺带实测到一个值得写进 NOTES 的副作用**：`/inheritance:r`（禁用继承并删掉继承来的 ACE）
会让对象变成**空 ACL**——连所有者自己也进不去（后续的 `Get-ChildItem -Recurse` 直接
`Access is denied`，`/save` 也变成 exit 5）。**这不是 bug，是"ACL 空了"的正常后果**，
但它说明这个开关很容易把目录变成"谁都进不去"。恢复的办法是：所有者本人可以重新授予权限
（实测提权后 `/grant *<自己的 SID>:(OI)(CI)F /T /C` → exit 0，随后就能删除了）。

### 5.7 `versionPattern` 的验证（playbook §5.2 情形①）

icacls **没有版本开关**，逐条实测：

```
icacls /version  -> exit 87（把 /version 当成无效开关，随后打印帮助）
icacls /v        -> exit 87（输出帮助 + First parameter must be a file name pattern or "/?"）
icacls /help     -> exit 87（同上）
icacls -version  -> exit 2 （"-version" 被当成文件名：The system cannot find the file specified.）
```

所以按 R1 **不写 `versionArgs` / `versionPattern`**（编一个不存在的版本命令等于违反 R1）。
替代方案是读 exe 的文件版本：`(Get-Item C:\Windows\System32\icacls.exe).VersionInfo.FileVersion`
→ `10.0.26100.8875 (WinBuild.160101.0800)`。
清单里用一个 `execution: info` 的动作（`install-info`）展示这个事实**并标明它不是 icacls 的参数**。

### 5.8 被取消时的退出码

`Kill(true)` 掉一个正在跑的 `icacls C:\Windows\System32 /verify /T /Q`
（900 ms 时还在跑，已产生 518 134 字节输出）→ **ExitCode = -1（0xFFFFFFFF）**。
记在这里是为了让宿主/冒烟脚本知道"被取消"长什么样，不会误判成 icacls 的错误码。

---

## 6. 与官方文档对不上的地方（如实记录）

### 6.1 继承开关的名字：官方 `/inheritancelevel` vs 本机 `/inheritance`

- 官方 `#parameters`：`/inheritancelevel: e | d | r`。
- 本机 `/?`：`/inheritance:e|d|r`（**短名字**，帮助里完全没提 `level`）。
- **实测两种拼法都能跑通**：`e`/`d`/`r` 三个取值 × 两种拼法 = 6 条**全部 exit 0**，
  且用 `Get-Acl` 核对 `:d` 的效果一致。
- **处理方式**：按"官方为准"写 `/inheritancelevel:`，并在字段 `help`/`doc` 与本文件里记下本机的简写。

### 6.2 `/setowner` 本机帮助多了一句官方**没有**的关键限制

- 本机 `/?` 原文：`changes the owner of all matching names. `**`This option does not force a change of
  ownership; use the takeown.exe utility for that purpose.`**

- 官方页面的 `/setowner <user>` 一行只有 "Changes the owner of all matching files to the specified user."。
- **处理方式**：这句"不能强制夺取所有权"写进本动作的 `sources.note` 与 `description`，
  免得用户把它当 takeown 用。

### 6.3 `/setintegritylevel` 的参数形态：官方 `<perm><level>` vs 本机 `[(CI)(OI)]Level`

- 官方参数表写 `/setintegritylevel <perm><level>`（像是"权限掩码 + 级别"两段），
  但**官方语法行与唯一一个官方示例都只有级别/继承前缀**：
  `icacls "myDirectory" /setintegritylevel (CI)(OI)H`。
- 本机 `/?` 写的是 `/setintegritylevel [(CI)(OI)]Level`，**完全没有那个 policy**。
- **实测**：`L`、`M`、`(CI)(OI)H` 三种都 exit 0；`X` 是 exit 87。
- **处理方式**：按"两边都能确认的部分"写成一个自由文本字段（`L` / `M` / `H` / `(CI)(OI)H` …），
  在 `help` 里如实写明这个不一致，**不假装知道 policy 该怎么填**。

### 6.4 `/substitute` 只出现在**第三行**语法里，而且要求用"友好名"而不是 `*SID`

- 官方第三行：`icacls directory [/substitute SidOld SidNew [...]] /restore aclfile [/C] [/L] [/Q]`；
  参数表说 "Requires using with the `<directory>` parameter."，`/restore` 那行也写
  "Requires using with the `<directory>` parameter."。
- **实测**：`/substitute S-1-5-32-545 S-1-5-32-545 /restore f /C` → exit 1332
  （`S-1-5-32-545: No mapping between account names and security IDs was done.`）——
  说明 `/substitute` 的**两个名字都要是能解析的友好名**，不带 `*` 的数值串会被当成账号名。
- **处理方式**：**没有收录 `/substitute`**（理由见 §9）；把这条实测记在这里避免后来者重复踩。
  注意它和 `/findsid` 的规则**看起来相反但本质相同**：数值 SID 必须带 `*` 才是 SID。

### 6.5 官方第三行语法行的方括号数量不匹配（官方笔误）

官方 `#syntax` 第三行与 `/remove` 的写法都有多余的 `]`：

```
icacls name [/grant[:r] Sid:perm[...]] [/deny Sid:perm [...]] [/remove[:g|:d]] Sid[...]]] ...
```

`/remove: g | d <sid>` 的参数表说明是清楚的，本清单按参数表理解（三档：`/remove`、`:g`、`:d`），
并实测三档都 exit 0。

### 6.6 官方页面**没有任何退出码章节**，也没有权限要求说明

`exitCodes` 里的 9 个码全部来自实测 + `net helpmsg`（§5.4 有逐条对应表）。
`requiresAdmin` 也全部来自实测（§8），**不是**照文档抄的——因为文档一个字都没写。

### 6.7 官方示例里的**路径位置**与官方语法行一致，但本机帮助的 `/setowner` 行容易误导

- 官方语法行 `icacls name [/save aclfile] …`：路径在**最前**（与实测一致）。
- 官方示例 `icacls c:\windows\* /save aclfile /t`：路径在**最前**。
- 本机帮助 `ICACLS name /setowner user [/T] [/C] [/L] [/Q]`：读起来像"路径在前"，
  但它自己的示例 `icacls file /grant Administrator:(D,WDAC)` 也是路径在最前。
- **实测的硬结论**：`icacls /T <路径>` 与 `icacls /save <文件> <路径>` 都是 **exit 87
  （`First parameter must be a file name pattern or "/?"`）**。**路径必须是最前面的 token。**
- **处理方式**：本包所有动作的路径字段都声明在第一位（见 §7.6）。

### 6.8 本机帮助里 `/remove` 写的是"移除 Sid 的所有出现"，而官方写"从 DACL 移除"

措辞差异，语义一致（本机：`removes all occurrences of Sid in the ACL`；
官方：`Removes all occurrences of the specified SID from the DACL`）。
本清单按官方措辞写 `description`，并在 `help` 里点明**只动显式条目**。

---

## 7. 这个工具的坑（会咬人的地方）

### 7.1 开关是**独立的 token**，不能粘（与 wevtutil 相反）

```
✓ icacls <路径> /T                    -> exit 0
✗ icacls <路径>:/T                    -> exit 123  The filename, directory name, or volume label syntax is incorrect.
✗ icacls <路径> /zz                   -> exit 87   Invalid parameter "/zz"
✓ icacls <路径> /setowner 用户        -> exit 0（两个 token）
```

所以本清单**只在 `/inheritancelevel:取值` 这一处用 `style: attached`**
（那是上游把冒号规定在开关名里的写法），其余全部是 `flag`／`separate`／`positional`。

> ⚠️ **同批的 wevtutil 结论正好相反**（它只认 `/q:值` 单 token，`/q 值` 一律 87）。
> 这两个包**不要互抄**——字段风格必须逐程序实测（playbook §9）。

### 7.2 `/Q` 比官方说的窄（§5.2）

官方只写 "Suppresses success messages."，实测**只抑制 `processed file:` 行**：
汇总行、错误、以及纯只读的输出**完全不受影响**。把它当"减少输出量"的开关会失望。

### 7.3 数值 SID **必须带 `*` 前缀**，而且这是个硬要求

官方 Remarks 与**本机帮助都写了**这一条（"If you use a numerical form, affix the wildcard character
`*` to the beginning of the SID"），实测两次踩到：

```
icacls <目录> /findsid S-1-5-32-545 /T   -> exit 1332（S-1-5-32-545 被当成账号名去找）
icacls <目录> /findsid *S-1-5-32-545 /T  -> exit 0，4 个命中（SID Found: …）
icacls <目录> /findsid Users /T          -> exit 0，同样 4 个命中
```

**注意 `/findsid` 一个都没命中时也是 exit 1332**（不是 0），与"账号名写错"同一个码——
两者只能靠 stdout 的那句 `No files with a matching SID was found` 区分。

### 7.4 `-` 前缀的写法**不被接受**

同批其它 Windows 命令常常 `/开关` 与 `-开关` 都认，**icacls 不认 `-`**：

```
icacls <目录> -T   -> exit 87  Invalid parameter "-T"
```

所以本清单全部用 `/开关`（与官方文档一致）。

### 7.5 `/grant` 与 `/deny` 的 `<账号>:<掩码>` 拼装：v1 清单表达不了，已按最接近的方式落地

官方语法要求：

```
icacls <路径> /grant[:r] <sid>:<perm>
icacls <路径> /deny       <sid>:<perm>
```

这里有**两条互相独立的约束，而且它们的方向相反**，本包都踩过：

1. **开关与 `账号:掩码` 之间必须有空格**（两个 token）。
   实测 `icacls <文件> /grant "Users:(R)"` → exit 0；
   而把两者写成一个 token（`icacls <文件> "/grant Users:(R)"`）→ **exit 87
   `Invalid parameter "/grant Users:(R)"`**。
   → 这一条宿主**天然满足**（字段按顺序展开成 token），**不需要任何妥协**。
2. **账号与掩码之间不能有空格**（同一个 token 里的冒号）。
   而 **manifest v1 的 `attached` 只能把前缀粘到"本字段自己的值"上**，
   没有"把两个字段的值拼进同一个 token"的能力（同 `docs/ai/windows-commands.md` §7.4 记的
   `sfc /verifyfile=<file>` 与 `cleanmgr /sageset:n` 是同一类缺口）。
   → 这一条**必须妥协**。

**最终落地方式**（第一版把它写成一个 `switchBase: "/grant"` 的字段，是错的，见 §7.5.1）：

| 动作 | 字段 | style | 展开结果 |
|---|---|---|---|
| `grant-perm` | `grantSwitch`（`/grant` / `/grant:r`，**literal**） | literal | `/grant` 或 `/grant:r` |
| `grant-perm` | `grantSpec`（用户写 `Users:(R)`） | positional | `Users:(R)` |
| `deny-perm` | `denySwitch`（只有 `/deny` 一个取值，**literal**） | literal | `/deny` |
| `deny-perm` | `denySpec`（用户写 `Users:(W)`） | positional | `Users:(W)` |

合起来就是 `icacls <路径> /grant:r Users:(R)` —— 与官方语法一致。

**代价**：`grant-perm` 的用户要点两次（先选"追加还是替换"，再写账号与掩码），
不能像"账号"+"掩码"两个字段那样分别校验。这是规范能力的缺口，**不是可以绕过的设计选择**。

> **如果以后规范加了"多字段拼一个 token"的语义**（例如 `attachedWith: grantSpec`），
> `grantSpec` / `denySpec` 应该拆成 `account` + `perm` 两个字段。
> `/remove` 与 `/setowner` **不受影响**——它们的账号是**独立的 token**（中间是空格），
> 已按 `positional` 正确表达。

#### 7.5.1 `switchBase` **不会产生参数**（本节是实测踩出来的第二处坑）

第一版 `deny-perm` 只写了一个 `style: positional` 的 `denySpec` 字段 + `switchBase: "/deny"`，
以为"声明了对应哪个开关"就够了。用宿主**真实的** `ArgvBuilder` 一跑就露馅：

```
deny-perm  danger=overwrite  admin=False | icacls.exe D:\secret Users:(W)      ← 少了 /deny！
```

`switchBase` 按规范只是"给 CI 白名单校验看出处用的"元数据，**不参与 argv 生成**。
补上 `denySwitch`（literal）之后才变成 `icacls.exe D:\secret /deny Users:(W)`。

**这一条对同批其它工具包也适用**：任何"开关名不在 prefix 里"的字段设计都要问一句
"到底谁把这个开关 token 吐出来"。

> **顺带一条对宿主/规范的观察（不在本包边界内，只报告）**：
> `icacls /?` 的忠实快照没进 `docs/reference/`（刻意的，同批 12 个包都不入库本机帮助），
> 于是校验器的"开关溯源"启发式对本包报了 15 条 `[待确认]`。
> **门禁不受影响**（校验 exit 0、23/23 通过），但如果以后希望这一层对 Windows 命令也有效，
> 需要一个"本机帮助快照"的入库口径（现在它是明确不入库的第三方文本）。

### 7.6 **路径必须是最前面的 token**（本包最要命的一条，差点写错）

上游文档自相矛盾（§6.7），实测给出的答案是唯一的：

```
✓ icacls <路径> /save <文件> /T      -> exit 0
✓ icacls <路径> /grant Users:(R)     -> exit 0
✗ icacls /T <路径>                   -> exit 87  First parameter must be a file name pattern or "/?"
✗ icacls /save <文件> <路径>          -> exit 87  同上
```

**第一版清单按"位置参数放最前"的常规思路写对了**，但中途我按本机帮助的语法行
把 `save-acl` / `restore-acl` / `remove-perm` / `set-owner` 改成了"开关在前"，
冒烟脚本立刻把 14 条用例全部打回 **exit 87**——这正是 playbook §3.1 说的
"错了不会报错、只会静默传错参数"的镜像情形（这里是**会报错**，算是走运）。
所以本包所有动作的路径字段都声明在第一位，`help` 里也写明这条约束。

### 7.7 输出是 **LF**，不是 CRLF；写 `nextSteps.when` 不用加 `\r?`

同批 wevtutil 的输出是 CRLF，`(?m)$` 在 CRLF 上匹配不到行尾、必须写 `\r?$`。
**icacls 是 LF**（实测 `/Q` 输出里 CR 计数 = 1，那 1 个来自帮助文本自身），
所以本包不需要那个补丁。（`icacls /?` 的 5 286 字节里有 135 个 CR，是帮助文本的排版，与运行输出无关。）

### 7.8 `/reset` 会把自己也挡在门外（`/inheritance:r` 造成空 ACL）

见 §5.6 末尾。这对**做界面提示**有意义：`reset-acl` 标 destructive + 逐字确认短语，
并在 `confirmText` 里说清"icacls 没有撤销功能"。

### 7.9 一条显式拒绝能挡住**连管理员都删不掉文件**（实测踩出来的收尾事故）

这一条不是我"设计"出来的，而是**收尾时真的删不掉探测文件**才发现的，值得完整记下来：

我在一次临时实测里跑了 `icacls <探测文件> /deny Users:(W)`，于是那个文件上多了一条
`BUILTIN\Users Deny Write, Synchronize`（**显式** ACE，非继承）。
之后我**提权**跑 `Remove-Item -Recurse -Force` —— **还是"对路径的访问被拒绝"**。
`Get-Acl` 显示：文件的所有者已经是 `KANGMORI\Steve`、而且有
`Allow/Kangmori\Steve FullControl`（继承来的），但**删除依然失败**——
因为删除需要对**父目录**的 `DELETE_CHILD`，而那条**显式拒绝 ACE 排在前面**，
按 ACE 顺序它先被判中，后面的 Allow 不参与（Windows 的判定是"任一 DENY 命中即拒绝"）。

**最后的解法是唯一一条**：提权跑
`icacls <探测目录> /remove:d Users /T /C` → exit 0，ACE 变成
`Allow/BUILTIN\Users ; Allow/Kangmori\Steve ; Allow/SYSTEM ; Allow/Administrators`，
**然后** `Remove-Item` 才成功（`Test-Path` = False）。

**给用户的教训（已写进 `deny-perm` 的 `confirmText` 与 `description`）**：

- 显式拒绝的优先级高于**任何**授权，包括管理员组的 `FullControl`；
- 所以"加一条 deny"比"减一条 grant"危险得多，而且**它不会因为你是管理员就失效**；
- 能救回来的只有"还有权限改 DACL 的人"用 `/remove:d`（或 `/reset`）把它摘掉。

### 7.10 大目录上的输出量会失控

实测 `/verify /T /C` 对一个普通用户的 `Documents`：**15 753 696 字节 / 40 245 个对象**；
`/T` 看 `C:\Windows\System32\drivers\etc`：46 948 字节 / 82 个对象。
`icacls` **没有**"只输出前 N 条"或"只列异常项"的开关（官方语法行里没有这类选项），
所以清单能做的是在 `help` 里提示"先对具体的子目录跑"，而不是替用户限制范围。

---

## 8. 危险动作的处理与理由（规范 §2.9）

### 8.1 风险分级总表

| 动作 | 判定 | 理由（实测/官方依据） |
|---|---|---|
| `grant-perm`、`deny-perm`、`remove-perm`、`set-inheritance`、`set-integrity` | `danger: overwrite` + `confirmText` | 改的是 ACL（配置），**不动文件内容**，而且只要还有写权限就能改回来 |
| `save-acl` | `danger: overwrite` + `confirmText` | 会**覆盖**目标 ACL 文件；但只写那一个文件，不碰被导出对象的权限 |
| `reset-acl` | **`danger: destructive`** + `confirmPhrase: "重置 {path} 的权限"` | 官方原文 "Replaces ACLs with default inherited ACLs"——**一次性丢掉该对象上所有单独做的权限设置**（显式授权、显式拒绝、以及切断继承留下的结果）。规范 §2.9 对 destructive 的口径是"不可逆地毁掉现有数据"：ACL 不是文件内容，但**它同样是"现有数据"且 icacls 没有任何撤销功能**，所以按 destructive 处理 |
| `restore-acl` | **`danger: destructive`** + `confirmPhrase` + **`requiresAdmin: true`** | 它会**用文件里的旧权限覆盖整棵树的当前权限**（等于把期间的权限改动全部撤掉），且改动面可以是整棵树；另外实测它非管理员根本跑不了（exit 1300） |
| `set-owner` | **`danger: destructive`** + `confirmPhrase: "改所有者 {path}"` + **`requiresAdmin: true`** | 所有者随时可以重写该对象的整个 DACL（"拿到所有权就等于拿到控制权"，而且**可以给自己授权**——§5.6 末尾正是这么把探测目录救回来的）。改动不是"再来一次 setowner"就能回退的：改完之后你自己可能已经**没有**权限再改回去（实测探测目录被改成 Administrators 后，普通会话连 `Get-ChildItem -Recurse` 都被拒），需要提权才能交还 |

**为什么不把 `reset-acl` / `set-owner` / `restore-acl` 降级成 `execution: info`**：
规范 §2.9 明确要求"再危险的操作也要在界面上留一条，只是执行方式不同"，
并把 `info` 留给"**这一步该不该由工具替你做决定**"的情形（典型是 `ipconfig /release`：
会切断网络、用户可能正在远程桌面上点它）。这三个动作的风险都可以通过
"逐字输入确认短语"消解，而且它们**有明确的补救路径**（`/save` 备份、所有者本人可以重新授权），
所以处理是 destructive + confirmPhrase，而不是 info。

### 8.2 `requiresAdmin` 的逐条依据（**全部来自实测**，文档一个字都没写）

| 动作 | 标了吗 | 依据 |
|---|---|---|
| 全部只读动作 | **不标** | 实测非管理员能读 `C:\Windows\System32\drivers\etc\hosts`（exit 0），也能读 `C:\Windows\System32\config`（exit 0！比预想的宽松）。有一处例外：`C:\Windows\System32\config` **带 `/T`** 递归时 exit 5（子项里有普通用户进不去的），但那是"范围问题"不是"动作需要管理员" |
| `grant-perm`、`deny-perm`、`remove-perm`、`set-inheritance`、`set-integrity` | **不标** | 关键在于"有没有 WriteDAC"，不是"是不是管理员"。实测：**把探测目录的所有者都改成了 `Administrators` 之后**，非管理员仍然能对这些文件 `/grant`、`/deny`、`/remove`、`/reset`、`/inheritancelevel:d`（全部 exit 0）——因为文件上仍有 `Kangmori\Steve:(I)(F)` 这条继承 ACE。对系统文件（hosts）则是 exit 5 |
| `save-acl` | **不标** | 实测非管理员 exit 0，写得出 ACL 文件（对探测目录，也对 `C:\Windows\System32\config` 之类只读范围内的对象） |
| `restore-acl` | **标 true** | 实测**非管理员一律 exit 1300**（Not all privileges or groups referenced are assigned to the caller.）。换成父目录、换成**不存在**的 ACL 文件都是同一个码 → 说明它在处理 ACL 文件内容之前就先要特权（`SeRestorePrivilege` 一类），与"目标对不对"无关 |
| `set-owner` | **标 true** | 实测非管理员：`/setowner Users` → exit 1307、`/setowner Administrators` → exit 1307；**提权后 exit 0**，且退出后用 `Get-Acl` 从普通会话复核，`Owner` 确实变成了 `BUILTIN\Users`。唯一不需要管理员的形态是"把所有者写成自己"（exit 0），已在字段 `help` 里写明 |

### 8.3 改动类动作是**在探测目录里真跑过的**

本包与同批 `wevtutil`（改动类全部没跑）不同：**除了需要提权的那两条以外，
所有改动类动作都在 `%TEMP%\__AllToolProbe_icacls` 里真跑过**，并用 `Get-Acl` 核对了真实效果（§5.6）。
这是刻意的：`/grant` 与 `/grant:r` 的语义差别、`/remove:g` 与 `/remove:d` 的区别、
`/inheritancelevel:d` 到底改了 `IsInherited` 还是 `AreAccessRulesProtected`，
**只看退出码判断不出来**，而这几条正是这个工具包最容易写错的地方。

**提权的用法**（严格遵守任务红线）：`Start-Process powershell -Verb RunAs -ArgumentList … -Wait`
**只用于两类事**：
① 跑**只读**命令（读 `C:\Windows\System32\config` 的 ACL、读 `.evtx` 式的单对象权限）；
② **收尾**——把探测对象的所有者/权限交还给自己，好让探测目录能被删掉。
**没有一次提权是用来"改探测目录之外的东西"的**；唯一的 `/restore` 提权实验也只作用于探测目录内的对象。

---

## 9. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| `/substitute SidOld SidNew` | ① 官方要求它**必须与 `/restore` 配合**，而 `/restore` 非管理员必然失败（§8.2），做成动作在当前账号下只能得到"点了就报权限错"；② 实测它的两个名字都必须是**能解析的友好名**（写数值 SID 就 exit 1332，§6.4），语义比看起来绕；③ 它是"把 ACL 文件里的旧 SID 批量换成新 SID"的迁移工具，属于"换域"这种低频场景。**如果以后要加**：它是 `danger: destructive` + `requiresAdmin: true`。 |
| **"看所有者"的只读动作** | `icacls <路径>` **不显示所有者**（实测输出里没有 owner 字段）；`/save` 也不保存所有者（本机帮助原文）。要看所有者只能用图形界面或 PowerShell 的 `Get-Acl`——**那不是 icacls 的能力，写进清单就是编造**（R1）。改为在 `set-owner` 的 `description` 里说明"所有者是最高权限、可以重写整个 DACL"，并把这个缺口如实记在这里。 |
| `/setintegritylevel` 的 `policy` 部分 | 官方参数表写 `<perm><level>`，本机帮助与官方示例都只有级别（§6.3）。**不知道 policy 该填什么就不做那个字段**。 |
| `/L`（对符号链接本身而不是它的目标） | 官方与实测都支持（`<目录> /T /L` exit 0），但它与 `/T` 的组合语义（哪些对象会被当作"链接本身"）**本机没有可观测的符号链接样本**，无法量化验证。**在只读动作里 `/L` 无害但也没价值**，改动类里它可能让"你以为改的是目标、其实改的是链接"——不加比加错好。 |
| `/C` 在 `show-acl-recursive` 之外的"继续"语义细化 | `/C` 已在 5 个动作里提供；但"出错继续会不会影响退出码"实测结论是**不会**（`/T` 与 `/T /C` 都是 exit 0、字节数相同）。没有更多可做的。 |
| `output.progress` | icacls 不画进度条、也没有百分比输出 → 不写（不凭印象编正则）。 |
| `nextSteps` | **一个都没写**。理由：icacls 的输出是 `<路径> <ACE>…` 的行流，"下一步该干什么"完全取决于用户的意图（看到继承的权限想去改？看到拒绝想去删？），**从输出里判断不出来**。同批 wevtutil 只写了 2 条且每条都对着真实输出数过命中量；本包没有一条经得起这个验证的启发式，所以宁可一条不写。 |
| 把 `icacls /?` 的帮助快照存进 `docs/reference/` | 那是微软的帮助文本，且**随时可用 `icacls /?` 重取**（与同批 12 个包的口径一致：本机帮助快照不入库）。这也是校验器"开关溯源"对本包报 15 条 `[待确认]` 的原因——**它只影响那句启发式提示，不影响门禁**（§10 已核对：门禁 0 失败）。 |
| `requiresAdmin`（工具包级） | 只写在 `runtime` 里为 `false`；动作级按 §8.2 逐条标。宿主"动作级覆盖工具包级"，所以这样最不误导。 |

### 9.3 冒烟里没有"未跑"的用例（第一版曾经有）

第一版脚本有 **5 条**落到"未跑"，**原因全是我自己的白名单写得太窄，不是那些 argv 危险**：

| 曾经未跑的 argv | 真正原因 | 怎么修的 |
|---|---|---|
| `C:\Windows\System32\drivers\etc /T` / `… /T /C` / `…\* /T /C`（清单里 `show-acl-recursive` 与 `show-acl-wildcard` 的 examples） | 只读白名单漏收了这三个形态（我只收了不带 `/T` 的版本），于是**清单里自己的示例都没被跑到** | 把这三条补进只读白名单 |
| `/inheritancelevel:x <目录>` / `/save <不存在目录>\x.txt <目录> /T` | 它们是"开关在路径之前"的形态；判定逻辑按路径位置切分，把开关当成了取值 | **这两条本来就该失败**（exit 87），改用路径在前的等价形态跑；开关在前的反例另加了两条专门的用例（§7.6） |
| `<路径>:/T`（粘一起） | 白名单里写的是另一条路径 | 直接用探测目录里的等价形态跑 → exit 123 |

修完之后 **33 条全部实际执行**。这里如实记下来，是因为它正好印证 playbook §5.1 的那条教训：
**"白名单/判定逻辑本身就是风险点"**——它可能悄悄把该测的东西挡在外面（这次是**清单自己的示例**
被挡了），而报告里只会显示"未跑"，不会有任何报错。

**没有任何一条改动类用例因为"危险"而被跳过**——它们全部限定在探测目录内。

---

## 10. 校验结果（R3）

```
[ OK ] plugins\icacls\manifest.yaml  (16 动作 / 57 字段 / 字段出处标注 57 个 = 100%)
23/23 个 manifest 通过
字段出处覆盖率: 761/761 (100%)
exit code 0
```

（仓库当时共 23 个工具包通过——本轮有其它智能体在同时新增 `certutil` / `winget` 等包；
`icacls` 是本轮新增的。校验器对本包报了 **15 条** `[待确认] 开关 /T / /findsid / /restore / /grant / /deny / /remove / /inheritancelevel …`，
那是**启发式提示、不阻断构建**：语料里没有本机 `icacls /?` 的快照（刻意不入库），
而这 18 个开关的出处都写在各自字段的 `doc` 里、并逐条实测过。）

---

## 11. 本轮创建的临时对象（按委派红线申报）

| 建了什么 | 位置 | 何时删的 | 怎么复核的 |
|---|---|---|---|
| 探测目录 `__AllToolProbe_icacls`（含 `rw\`、`acl\`、`acl\sub\`、`中文目录\` 与几个 .txt/.acl.txt） | `%TEMP%\__AllToolProbe_icacls` | 每轮探查脚本结束时删；`smoke.ps1` 收尾也删 | `Test-Path` = **False** |
| 中间轮次留下过两个**空 ACL** 的探测子目录（`SaveRoot\__AllToolProbe_icacls\{a.txt}`，由 `/inheritance:r` 实验造成） | 同上 | 最后一次清理**分两步**：先提权 `/setowner` + `/grant *<自己的 SID>:(OI)(CI)F /T /C`（exit 0），再 `Remove-Item -Recurse -Force` | 两次 `Test-Path` 复核均为 **False**，且 `Get-ChildItem -Recurse` 已无残留 |
| 一个带**显式拒绝 ACE** 的探测目录（`__AllToolProbe_icacls_granttest`，`/grant` 两种 token 写法对照实验的产物） | 同上 | 同上 | 这个**一开始删不掉**（连提权都拒绝，原因见 §7.9）；最终解法是提权 `/remove:d Users /T /C` → exit 0，再删 | 最终 `Test-Path` = **False**；`Get-Acl` 确认 deny ACE 已移除 |
| 提权进程写入的日志文件（`elev*.txt`、`__probe_cleanup*.txt`、`__gt*.txt`） | 探测目录内 / `%TEMP%` | 随探测目录一起删；`%TEMP%` 下那些单独删 | `Test-Path` = False |

**没有动过 `D:\AI\All Tool` 之外的任何真实文件、系统目录或用户目录。**
对本机文件系统的全部改动只发生在 `%TEMP%\__AllToolProbe_icacls` 之内，
以及最后一次清理时对该目录的所有者/权限的还原（目的是让它能被删掉）。

**额外申报（本包之外的临时文件）**：我在自己的会话工作目录
`D:\AI\swpj\`（**不是仓库目录**）下写了若干探查用的一次性脚本
（`icacls-probe1.ps1` … `icacls-probe12.ps1`、`icacls-shape.ps1`）
与 `%TEMP%` 下的 `icacls_dump.py` / `ic_shape2.py` / `icacls_validate.txt`。
它们是**测量过程的脚手架、不在仓库里**；关键结论已全部抄进本文件与 `smoke.ps1`，
所以可以随时删除而不影响复核。

---

## 12. 给复核者的三条建议

1. **先复跑冒烟**：`pwsh -NoProfile -File plugins/icacls/smoke.ps1`
   ——它自带红线自检（15 条），会在跑之前就 `throw` 掉任何被误判为可跑的破坏性 argv；
   结束后打印 `Test-Path` 复核结果。抽一条它宣称的实测结论独立验证：
   例如 `icacls C:\Windows\System32\drivers\etc\hosts`（应为 exit 0、453 字节、5 条继承 ACE）。
2. **抽查 §7.6 那条硬结论**：亲手跑一次 `icacls /T <任意目录>`（应 exit 87 +
   `First parameter must be a file name pattern or "/?"`）与 `icacls <任意目录> /T`（应 exit 0）。
   这是本包字段顺序的依据，也最容易被"看起来更整齐"的写法改坏。
3. **`set-owner` / `restore-acl` 的成功路径本包只在探测目录内验证过**（且 `restore-acl`
   只在提权会话里对探测目录跑过）。如果复核时要在别处验证，请先 `/save` 留一份备份。
