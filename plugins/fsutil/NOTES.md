# fsutil 工具包 —— 实测记录

> **本批共用事实**（Windows 自带命令的一般性质、`/?` 的退出码与流向差异、编码判定的方法等）见
> ``docs/ai/windows-commands.md``。
> **但 fsutil 有几处与最早那批 12 个包不同，别照抄它们的结论**：
> ① 它是**两层子命令**（`fsutil <子命令> <操作>`），多数第三层根本不接受 `/?`——
>    你敲 `fsutil volume diskfree /?`，它会**真的去查一个叫 "/?" 的卷**；
> ② **绝大多数只读查询也要管理员**（12 个包那边只有 chkdsk/sfc/powercfg 少数几条要提权）；
> ③ 输出语言**不固定**：同一台机器上，我用 harness 跑出来是英文、用提权会话跑出来是中文，
>    而**两者的字节编码都是 cp936** —— 所以清单里**不能用文案判断成败**，只能用退出码；
> ④ 退出码全部是 0/1/2/3/5/87 这种 Win32 码（不是 HRESULT 负数，也不是"0 与 1 含义相反"），
>    但 **`fsutil <子命令> /?` 自己返回 1**、`fsutil cache` 这类"用法输出"也返回 1。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `kangmori\steve`，**非管理员**
> （`whoami /groups` 里 `BUILTIN\Administrators` 标着 `Group used for deny only`）。
> 控制台代码页 `936`（`chcp` 实测），系统 ACP/OEMCP 都是 936。
> fsutil.exe 文件版本 **10.0.26100.8875 (WinBuild.160101.0800)**。
> 实测日期 **2026-09-30**。

---

## 1. 这个包为什么值得做

fsutil 是「硬盘/文件系统到底怎么了」这一类问题的**唯一零安装答案**：

| 想干的事 | 别人怎么做 | 其实一条命令 |
|---|---|---|
| C 盘还剩多少、空间被谁占了 | 装第三方磁盘分析工具 | `fsutil volume diskfree C:` / `fsutil volume allocationReport C:` |
| 这块卷支不支持配额/稀疏/大小写敏感 | 翻注册表、查文档 | `fsutil fsinfo volumeInfo C:` |
| 上次是不是非正常关机（卷脏位） | 等下次开机 autochk 才发现 | `fsutil dirty query C:` |
| 这个文件到底占了哪些簇、碎不碎 | 装碎片分析工具 | `fsutil file queryAllocRanges` / `queryExtents` |
| 这个链接指向哪、是什么类型 | 属性对话框看不到 tag | `fsutil reparsePoint query <路径>` |
| 谁改过这个文件（USN 记录） | 装审计/监控软件 | `fsutil usn readdata <文件>` |

它同时是**存储排障的入口**（23 个子命令、上百条子操作），大量操作面向服务器与存储阵列。
本包只做「普通用户会问的问题」+「危险操作摊开给你看」（规范 §2.9）。

---

## 2. 参数知识来源（R1 / R2）

| # | 来源 | 位置 | 取得日期 | 适用版本 |
|---|---|---|---|---|
| 1 | **Microsoft Learn 官方文档**（1 张主页 + 18 张子页） | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/fsutil（主页是索引，子页见各动作 `sources`） | 2026-09-30 | Windows 10/11、Server 2016–2025、Azure Local 2311.2+ |
| 2 | 本机 `fsutil` / `fsutil <子命令>` / `fsutil <子命令> <操作> /?` 的真实输出（**未入库**，随时可重取） | `C:\Windows\System32\fsutil.exe` | 2026-09-30 | fsutil.exe 10.0.26100.8875 |
| 3 | 本包真机冒烟：只读 40+ 条 argv 的非管理员矩阵 + 提权对照矩阵 + 失败路径 | `%TEMP%\fsutil-probe\`（**临时目录，见 §11**） | 2026-09-30 | 同上 |

### 2.1 官方文档的形态（与 certutil / wevtutil 那种"一页到底"不同）

- **主页只是一张索引表**：18 个子命令各一行，指向 18 张子页；主页正文另有一句
  `You must log on as an administrator or a member of the Administrators group to use fsutil`。
  实测该页 `Last updated 2025-03-05`。
- **子页的形态是「语法行 + 参数表 + 示例」**，例如 `fsutil-volume` 给了 6 条语法行、
  `fsutil-file` 给了 12 条。本清单每个动作的 `sources` 都指到**具体那一页**，
  在 `note` 里写清"官方那一节说了什么"。
- **主页的索引表漏了 5 个子命令**：本机 `fsutil` 总表里有
  `behavior` / `bypassIo` / `dax` / `storageReserve` / `trace`，主页索引里没有
  （其中 `behavior` 其实另有子页 `fsutil-behavior`，是主页链接漏了）。
  本清单**没有收录**这 5 个，见 §9。

### 2.2 两边对不上的逐条处理

见 §8。总口径与 playbook §0.1 一致：**字段依据以官方页面为准、行为以实测为准，
两边都能对应的才写进清单**。

---

## 3. 环境与版本（实测，R7）

| 项 | 值 | 怎么得到的 |
|---|---|---|
| 程序路径 | `C:\Windows\system32\fsutil.exe` | `Get-Command fsutil` → `Source` |
| 文件版本 | `10.0.26100.8875 (WinBuild.160101.0800)` | `(Get-Item …).VersionInfo.FileVersion` |
| 产品版本 | `10.0.26100.8875` | 同上 |
| FileDescription | `fsutil.exe`（没有中文描述） | 同上 |
| **版本开关** | **没有**（`fsutil` 总表与各子命令表里都没有版本项） | 实跑 |
| 运行身份 | `kangmori\steve`，**非管理员** | `whoami /groups` → `BUILTIN\Administrators` 为 `Group used for deny only` |
| 控制台代码页 | `936` | `chcp` |
| 输出编码 | **OEM 代码页（cp936）** | §4 |
| 子命令总数 | **23**（无参数运行 `fsutil`，退出码 **0**） | 实跑 |
| 子命令帮助 | `fsutil <子命令>` 与 `fsutil <子命令> /?` 输出**同一张子命令表**，退出码都是 **1** | 实跑（23 个全部如此） |

---

## 4. 编码：**OEM 代码页（本机 936）**（原始字节判定，R7）

**判据必须用带中文的输出**（playbook §10.3 第 14 条）。fsutil 自己的文案大多数是英文，
但**系统错误消息、卷标、以及被回显的中文路径**都是中文。三条独立证据：

### 4.1 系统错误消息（最省事，不需要造任何文件）

```
argv: fsutil volume diskfree Z:      （提权或非管理员都一样）
stdout 64 字节，首个非 ASCII 字节在偏移 0
  原始字节[0..20] = b4 ed ce f3 20 32 3a 20 cf b5 cd b3 d5 d2 b2 bb b5 bd d6 b8 b6
  按 cp936 解 : 错误 2: 系统找不到指…
  按 utf-8  解 : ���� 2: ϵͳ�Ҳ���ָ�…
  严格 UTF-8 解码（UTF8Encoding(throwOnInvalidBytes: true)）:
    → 抛异常 "Unable to translate bytes [B4] at index 0 from specified code page to Unicode."
```

`b4 ed` 是 GBK 的「错」、`ce f3` 是「误」。**严格 UTF-8 解码抛异常 → 确定不是 UTF-8**。

### 4.2 中文路径回显（用自建的中文名探针文件）

```
argv: fsutil file queryEA %TEMP%\fsutil-probe\中文文件探针.txt
stdout 91 字节，首个非 ASCII 字节在偏移 2
  原始字节[0..22] = 0d 0a ce c4 bc fe 20 43 3a 5c …
  cp936 -> ⏎「文件 C:\…」
  utf8  -> ⏎「�ļ� C:\…」
  严格 UTF-8 解码 → 抛异常（bytes [CE] at index 2）
```

注意 **fsutil 会把用户给的中文路径原样回显**，所以这条路径上的编码问题是真实存在的，
不是"只有错误消息才中文"。

### 4.3 提权会话里连帮助都是中文（同一台机器、同一份 exe）

提权运行 `fsutil volume diskfree C:` 时输出是中文标签（「总可用字节数 / 总字节数 / 总配额可用字节数」），
而非管理员 harness 里同一个命令的输出是英文（`Total free bytes / Total bytes / Total quota free bytes`）。
**两种输出都是 cp936 字节**（§4.1 的字节证据在两种情况下都成立）。

- **不要**把这个当成"提权才中文"——它更像是 fsutil 按**控制台/管道上下文**选资源语言，
  本机同时撞到过英文与中文两种形态。
- **给后来者的结论**：**任何"靠文案判断成败"的写法在 fsutil 上都是定时炸弹**。
  本清单因此**一个文案正则都没写**（`output.progress.pattern`、`nextSteps.when` 全部为空），
  只依赖退出码 + 键名（键名如 `LogicalBytesPerSector`、`Usn Journal ID` 是英文，相对稳定）。

**结论：`runtime.encoding: oem`**（宿主把 `oem` 解析成
`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，本机 = 936；写 `oem` 而不是写死 `gbk`，
换英文 Windows（437）或日文 Windows（932）时同一份清单仍然正确）。

---

## 5. requiresAdmin：**同一条 argv 的"非管理员 vs 提权"对照矩阵**（R7，不照文档抄）

官方主页写了 `You must log on as an administrator … to use fsutil`，
但**本机实测远不止"整体要提权"这么简单**：同一子命令下有的操作普通用户能跑、有的不能。
做法是**把同一张 argv 清单跑两遍**（`%TEMP%\fsutil-probe\matrix-nonadmin.txt` 与
`matrix-elevated.txt`），逐条比对退出码与输出。

| 实测结论 | 动作（本清单收录的） |
|---|---|
| **普通用户可用，不需要提权** | `volume list`、`volume queryNumaInfo`、`fsInfo drives`、`fsInfo driveType`、`file queryFileID`、`file queryAllocRanges`、`file queryExtents`、`file queryValidData`、`file queryEA`、`file queryOptimizeMetadata`、`file queryCaseSensitiveInfo`、`hardlink list`、`objectID query`、`reparsePoint query`、`sparse queryFlag`、`sparse queryRange`、`quota violations`、`behavior query`、`8dot3name query`（不带卷参数）、`usn queryJournal`、`usn readdata` |
| **非管理员一律失败，提权后成功** | `volume diskfree`、`volume queryLabel`、`volume queryCluster`、`volume allocationReport`、`volume tpInfo`、`fsInfo volumeInfo`、`fsInfo ntfsInfo`、`fsInfo sectorInfo`、`fsInfo statistics`、`dirty query`、`file layout`、`quota query`、`repair query`、`repair state`、`repair enumerate`、`8dot3name query <卷>`、`bypassIo state`、`storageReserve query`、`tiering queryFlags` |
| **两种身份都不行（另有门槛，与权限无关）** | `fsInfo refsInfo`、`volume smrInfo`（都回「A local REFS volume is required for this operation.」，本机 C:/D:/E: 都是 NTFS）；`dax queryFileAlignment`、`file queryFileNameById`（参数不对 → 87） |

**失败时的真实症状**（非管理员）：

```
argv: fsutil dirty query C:
stdout 20 字节: 错误 5: 拒绝访问。        ← 原始字节 b4 ed ce f3 20 35 3a 20 be dc be f8 b7 c3 ce ca a1 a3 0d 0a
退出码: 1
（英文环境下同一行是 "Error 5: Access is denied."）
```

同一条 argv 提权后：`Volume - C: is NOT Dirty`，**退出码 0**。`dirty query D:` 与卷 GUID 形态同样。

**标记口径**（规范 §2.5「只在有依据时标」）：上面第二行的动作全部标
`requiresAdmin: true`（依据是实测矩阵，不是官方那句话）；
第一行全部**不标**（实测普通用户就能跑，标了反而让用户以为必须提权）；
第三行**本清单没有收录**（见 §9）。工具包级 `runtime.requiresAdmin: true` 则如实反映官方主页那句话。

**"未验证成功路径"的诚实记录**：
- 本报告里凡标 `requiresAdmin: true` 的动作，**成功路径都在提权会话里实测过**（上表第二行）；
- **7 个"会改动系统"的动作（`execution: info` + 危险分级）没有实跑成功路径**，
  原因不是权限，而是**红线要求不改文件系统**（§6）；
- 其中 `file setvaliddata` 与 `repair initiate` 的 `requiresAdmin` 依据分别是
  官方 `SeManageVolumePrivilege` 原文与"同子命令只读动作提权后才成功"的实测，已在字段里写明。

---

## 6. 危险动作：收录了什么、为什么这么分级、跑了没有

规范 §2.9 要求「再危险的操作也要在界面上留一条」。本包的处理：

| 动作 | 分级 | 实际执行方式 | 跑了没有 |
|---|---|---|---|
| `usn delete-journal` | `destructive` + `requiresAdmin` | **`execution: info`**（宿主不执行） | **没有**。官方自己标了 **CAUTION**：删日志会让 FRS/索引服务被迫全卷扫描、过程可能几分钟、期间日志不可访问、**记录不可恢复** |
| `file set-zero-data` | `destructive` | **`execution: info`** | **没有**。不可逆毁数据（官方原文 `which empties the file`） |
| `file set-valid-data` | `destructive` + `requiresAdmin` | **`execution: info`** | **没有**。官方明说是管理员专属（SeManageVolumePrivilege），且会改变文件有效数据语义 |
| `repair initiate` | `overwrite` + `requiresAdmin` | **`execution: info`** | **没有**。它会动卷上的 NTFS 元数据（在线自愈那套流程） |
| `volume dismount` | `destructive` + `requiresAdmin` | **`execution: info`** | **没有**。卸载卷会让正在使用该卷的程序立刻报错、未落盘数据有风险；本机的 C: 是系统卷 |
| `sparse set-flag` | `overwrite` | **`execution: info`** | **没有**。会改文件属性（可能触发 NTFS 释放全零簇） |
| `sparse set-range` | `destructive` | **`execution: info`** | **没有**。官方原文 `Fills a specified range of a file with zeros` |

**为什么这 7 个都是 `info` 而不是 `destructive`（可执行 + 确认短语）**：
`destructive` 的语义是"宿主执行 + 用户逐字确认"（diskpart 的 `clean` 那种）。
本包的 7 个动作**每一个都会不可逆地改文件系统或丢数据**，
且**本轮完全没有实测过它们的行为**（不知道失败时会发生什么、也不知道是否会半途中断）。
按 §2.9 的口径「不可逆地毁掉现有数据 = 极高风险 = `execution: info`」，
把它们定成"宿主不执行、只摊开命令行"是唯一诚实的做法。
`confirmPhrase` 仍然写了（规范要求极高风险动作写逐字确认短语，宿主在 info 视图里也会展示）。

**对"info 动作有没有价值"的一点说明**：这 7 条命令的参数都不好记
（`offset=<值> length=<值>` 的写法、`/d` 与 `/n` 的区别、`setvaliddata` 的取值范围约束），
宿主把命令拼好、摊在你面前、能一键复制到提升终端里，正是 §2.6 说的那个价值。

---

## 7. 这个工具的坑（会咬人的地方）

### 7.1 第三层子命令**多数不接受 `/?`**，乱加会真的执行

```
fsutil volume diskfree /?      → exit=1「错误 2: 系统找不到指定的文件。」
                                  （它把 "/?" 当成卷名去查了）
fsutil file queryFileID /?     → exit=1「错误 123: 文件名、目录名或卷标语法不正确。」
fsutil volume queryLabel /?    → exit=1「错误 123: …」
fsutil fsInfo volumeInfo /?    → exit=1「错误 123: …」
fsutil fsInfo driveType /?     → exit=0「/? - No such Root Directory」   ← 还返回 0！
```

**只有少数几个**真的打帮助：`file queryAllocRanges`、`file queryEA`、`file setZeroData`、
`file createNew`、`file setEOF`、`behavior query|set`、`8dot3name query|scan`、
`volume allocationReport|queryCluster|findShrinkBlocker`、`trace query`、`clfs authenticate`。

**教训**：给 fsutil 加参数前，**必须先用官方文档确认语法**，不能靠"敲 `/?` 试试"——
这条路上有的子命令会带着 `/ ?` 这个莫名其妙的参数**真的跑起来**。

### 7.2 字段风格：`offset=<值>` 只能是一个 token（**实测两种写法**）

playbook §9 要求"两种写法各试一次"。fsutil 里唯一有"开关带值"形态的就是
`file queryAllocRanges` / `file setZeroData` 的 `offset=` / `length=`：

```
① 单 token（attached 风格，prefix 直接写 "offset="）:
   fsutil file queryAllocRanges offset=0 length=16 <文件>
   → exit=0   「分配的范围[0]: 偏移: 0x0  长度: 0x10」        ✔

② 两 token（separate 风格: prefix "offset" + 值 "0"）:
   fsutil file queryAllocRanges offset 0 length 16 <文件>
   → exit=1   打印用法帮助「Usage: fsutil file queryAllocRanges offset=<val> length=<val> <filename>」  ✘
```

**结论：`offset` / `length` 两个字段用 `style: attached` + `prefix: "offset="` / `"length="`**
（值是数字，拼出来就是 `offset=0` 一个 token）。这与 wevtutil（只认单 token）同类，
与 certutil（两 token 才对）相反——**同一台机器上的 Windows 自带命令结论就是不一致的**。

另外两个"值必须粘在开关后面"的形态：
- `volume allocationReport /tier capacity C:`（`/tier` 与值之间是**空格**，即 `separate`）
  实测 exit=0；而 `/tiercapacity`（粘）与 `/tier:capacity`（冒号）**都被拒**
  （「/tiercapacity 是无效参数。」exit=1）。本清单用 `style: literal` 把
  `args: ["/tier", "capacity"]` 原样展开，避开了 separator 的语义歧义（规范 §7.4 那条限制）。
- `8dot3name query` 的 `$Corrupt` / `$Verify`：**`$` 在 PowerShell 里是变量前缀**，
  在终端里手敲要用单引号包住；本界面直接传 argv，不受影响（已写进字段 `help`）。

### 7.3 字段顺序（argv 顺序）在 fsutil 里**因动作而异**，必须逐条看帮助

| 动作 | 正确顺序 | 反例（实测） |
|---|---|---|
| `file queryAllocRanges` | `offset=… length=… <文件>` | 顺序换掉/少一个 → exit=1 用法 |
| `file queryExtents` | `[/R] <文件> [起始VCN] [VCN数]` | `queryExtents <文件> /R` → **exit=1 用法**（开关必须在文件前） |
| `file queryValidData` | `[/R] [/D] <文件>` | `queryValidData <文件> /D` → **exit=1 用法**（同上） |
| `volume allocationReport` | `[/tier <层>] <卷> [/v]` | 官方示例里 `/v` 出现在卷**之后**（`fsutil volume allocationReport C: /v /tier performance`），本清单把 `/tier` 放卷前、`/v` 放卷后，与官方示例一致 |
| `volume findShrinkBlocker` | `/noFileName <卷> …`（帮助里的示例把开关放**卷之前**，但另一种示例又把 `/newSize` 放卷**之后**） | 因为顺序说不清，**本包不收录这个动作**（见 §9） |
| `repair enumerate` | `<卷> [<日志名>]` | 位置参数，无开关 |
| `usn deletejournal` | `{/d \| /n} <卷>` | 开关在卷**之前**（官方语法行如此）本清单也这么排 |

**做法**：每个动作的字段声明顺序都按上表来；**没有一条是凭直觉排的**。

### 7.4 `/v`（verbose）在 `allocationReport` 上会扫很久

`fsutil volume allocationReport C: /v` 会列出系统文件目录下的每个文件——
本机在 25 秒超时保护下**没有跑完**（提权会话里被 `Stop-Job` + `Kill` 中止）。
它只读、安全，但**耗时不可预测**。字段 `verbose` 的 `help` 里写明了这一点。
同理被排除的还有 `volume findShrinkBlocker`（§9）：本机实测它一次扫描就吃掉
**180+ 秒 CPU** 还没结束，我不得不手工 kill（`Get-Process fsutil | Kill`）。

### 7.5 "成功"与"没结果"经常是同一个退出码

| 命令 | 结果 | 退出码 |
|---|---|---|
| `file queryAllocRanges offset=99 length=16 <文件>`（区间超出文件尾） | **没有任何输出** | **0** |
| `fsInfo driveType Z:`（盘不存在） | `Z: - No such Root Directory` | **0** |
| `volume queryNumaInfo`（漏参数） | 打印用法 | **0** |
| `file queryCaseSensitiveInfo <非法路径>` | `Failed to query … error: 0x0000007b` | **0** |
| `8dot3name query bogus`（卷名是瞎写的） | 「8dot3 名称创建已在"bogus"上禁用」 | **0** |
| `transaction list`（当前没有事务） | **空输出** | **0** |
| `quota query C:`（卷上没启用配额） | 「未在卷"C:"上启用配额」 | **1** ← 反过来！ |

**所以既不能"看到 0 就当查到了"，也不能"看到非 0 就当命令写错了"**——
每个动作的 `resultNote` 都写清了它自己的哪种输出配哪个退出码。

### 7.6 失败信息走 stdout，stderr 是空的

实测 40+ 条 argv（含全部失败路径）：**stderr 一律 0 字节**，
连「错误 5: 拒绝访问。」这种系统错误也写在 **stdout**。
这与那批 12 个包里的 `nslookup` / `netstat`（帮助写 stderr）不同。
宿主是两个流都收的，所以界面上不受影响；**但写自动化脚本时别只盯 stderr**。

### 7.7 卷参数写法不统一

- `dirty query C:` 可以，**`dirty query C:\` 不行**（→ exit=3「错误 3: 系统找不到指定的路径。」）；
- `volume queryLabel D:` 与 `D:\` **都可以**；
- `volume diskfree C:`、`\\?\Volume{GUID}\`（**带与不带尾斜杠都行**）都可以；
- `fsInfo` 系列用的是 `<rootpath>`，`volume` 系列用的是 `<volumepath>`，两者实测都能接受盘符形态。

写字段 `help` 时没有统一成一种说法，而是**逐条按实测写**。

---

## 8. 与官方文档对不上的地方（如实记录）

### 8.1 官方主页**漏了** 5 个子命令，其中 `behavior` 其实有子页

- 本机 `fsutil` 总表的 23 个子命令里有 `behavior` / `bypassIo` / `dax` / `storageReserve` / `trace`；
- 官方主页的索引表**只有 18 行**，上面 5 个都不在里面；
- 但 `fsutil behavior` **确实有独立子页**（`fsutil-behavior`，实测 HTTP 200、
  `Last updated 2023-02-03`），是主页链接漏了。
- **处理**：`behavior query` 收录（有官方子页可引用）；
  另外 4 个（bypassIo / dax / storageReserve / trace）**不收录**——官方没有任何页面可引用，
  它们的语法只能来自本机帮助，违反"必须有官方出处"的口径（见 §9）。

### 8.2 官方 `fsutil fsinfo` 的**语法块漏了 sectorinfo**，参数表里却有

- 官方语法块只有 5 条：`drives` / `drivetype` / `ntfsinfo` / `statistics` / `volumeinfo`；
- 但同一页的 **#parameters 表里有 `sectorinfo` 一行**（`Lists information about the hardware's
  sector size and alignment`），示例也给了 `fsutil fsinfo sectorinfo d:` 和一段输出。
- **处理**：`fsInfo sectorInfo` **收录**，`doc` 里注明依据是 #parameters 表与官方示例
  （不是语法块），并在这里如实记录这处不一致。

### 8.3 官方参数表**没有语法行**的若干子操作（本清单收录了，依据写清楚）

| 收录的动作 | 官方页面里找不到的 | 本清单的依据 |
|---|---|---|
| `volume queryLabel` | #parameters 表里没有它 | 本机 v1 帮助的子命令表 + 实测（`Volume Label for "D:" is "NGNL"`） |
| `volume queryNumaInfo` | 同上 | 本机 v1 帮助的 `Usage: fsutil volume queryNumaInfo <volume path>` |
| `volume tpInfo` | 同上 | 本机 v1 帮助的 `Usage: fsutil tpInfo <option> <volume pathname>` + 三个选项 |
| `file queryEA` | `fsutil file` 的 #parameters 表没有它 | 本机 v1 帮助的 `Usage: fsutil file queryEA <file path>` |
| `file queryCaseSensitiveInfo` | 同上 | 本机 v1 帮助的子命令表 |
| `volume allocationReport /tier` `/v` | 官方只给了语法行，**没列 Options** | 本机 v1 帮助的 Options 一节 |
| `repair state` | 语法块里没有 state 的独立语法行（只有 #parameters 的一句说明） | 本机 v1 帮助的 `Usage: fsutil repair state [<volume pathname>]`（`[ ]` 说明卷路径可选） |

**为什么敢收**：这些都是**官方页面明确提到（哪怕只在参数表/说明里）、且本机实测能跑**的；
字段的 `doc` 里逐条写明了依据是"本机 v1 帮助"还是"官方 #parameters"。
反例（**两边只有一侧有、就直接不收**）见 §9：`file queryExtentsAndRefCounts`、`file queryProcessesUsing`
等只在 v1 帮助里出现、官方页面完全没提的操作。

### 8.4 `behavior query` 的选项表：本机帮助比官方**多 7 个**

官方语法行的选项（17 个）：`allowextchar`、`bugcheckoncorrupt`、`disable8dot3 [<volumepath>]`、
`disablecompression`、`disablecompressionlimit`、`disableencryption`、
`disablefilemetadataoptimization`、`disablelastaccess`、`disablespotcorruptionhandling`、
`disabletxf`、`disablewriteautotiering`、`encryptpagingfile`、`mftzone`、`memoryusage`、
`quotanotify`、`symlinkevaluation`、`disabledeletenotify`。

本机 v1 帮助的选项表**多出**：`defaultNtfsTier`、`enableMaximumHardLinks`、`enableNonpagedNtfs`、
`enableReallocateAllDataWrites`、`parallelFlushOpenThreshold`、`parallelFlushThreads`，
外加 `disableDeleteNotify` 可带 `[NTFS|ReFS]` 参数、`disable8dot3` 可带 `<Volume Path>`。
**处理：多出来的一律不收**（来源只有一侧），本清单的 17 个选项**逐字来自官方语法行**。

### 8.5 本机帮助里**多出**的子操作（全部不收）

本机 `fsutil` 子命令表/深度帮助里还有：`file queryExtentsAndRefCounts`、
`file queryProcessesUsing`、`file setStrictlySequential`、`volume smrGC`、`volume smrInfo`、
`volume upgrade`、`volume findShrinkBlocker`、`dax queryFileAlignment`、`bypassIo state`、
`trace query|start|stop|decode`、`storageReserve query|repair|findByID`、
`devdrv` 的全部 7 个操作、`clfs authenticate`、`wim enumFiles|enumWims|removeWim|queryFile`、
`quota disable|enforce|modify|track`、`tiering clearFlags|setFlags`……
**官方主页/子页里没有对应说明的一律不收**（R1）。其中 `file queryExtentsAndRefCounts`
实测在 NTFS 上直接失败（`Error 1: Incorrect function.`），也印证了它属于 ReFS 专属。

### 8.6 官方**没有**退出码章节

`fsutil` 主页与 18 张子页**都没有 exit codes 一节**（逐页核对过）。
本清单的 6 个退出码（0 / 1 / 2 / 3 / 5 / 87）**全部来自实测**，
每条都附了实测来源（见清单 `exitCodes` 的 meaning）。

### 8.7 章节号

多个子页都存在**"官网锚点"写反**的问题：

- `fsutil volume` 的语法块里 `diskfree` 是小写 `diskFree`？不——语法行写的是
  `fsutil volume [diskfree]`，而参数名实际是 `diskFree`（本机帮助 `Usage: fsutil volume diskFree`），
  两者大小写不一致；
- 本机帮助里同一个操作的大小写也混用（`diskFree` / `queryNumaInfo` / `setZeroData`
  首字母大写，而 `queryjournal` / `deletejournal` 全小写）。
- **处理**：本清单的 `commandArgs` **逐字照抄官方语法行**（大小写按官方），
  实测 fsutil 对大小写不敏感（`volume diskFree` 与 `volume diskfree` 都能跑）。

---

## 9. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| **整包不收**：`bypassIo`、`dax`、`storageReserve`、`trace` | 官方主页索引里没有这 4 个子命令，也**找不到对应的官方子页**（实测猜了几个 URL 都不对）。它们的语法只能来自本机帮助 → 没有可引用的官方出处（R1）。本机实测 `bypassIo state` / `dax queryFileAlignment` 在非管理员下都是「Error 5: Access is denied.」，`trace query` 回「找不到数据收集器集」，收益也低 |
| **整包不收**：`clfs`、`wim`、`tiering`、`transaction`、`resource`、`devdrv` | 都是服务器/存储/开发者场景的元数据管理：CLFS 是日志文件格式、WIM 是映像承载、tiering 是分层存储、transaction 是 TxF（已被官方标记 deprecated）、resource 是事务资源管理器、devdrv 是 Dev Drive 管理。**在普通家用机上要么跑不起来**（本机 `tiering tierList C:` → 「C: 不是分层卷。」；`wim enumWims C:` → Error 2），**要么结果无法解释给用户**。其中 `clfs authenticate` 官方明说要管理员 |
| `quota disable` / `enforce` / `modify` / `track` | 四个都是**改配额配置**的动作（`modify` 会写每个用户的阈值与上限）。本机 C: 根本没启用配额（`quota query` → 「未在上启用配额」），做成按钮只会得到失败 |
| `volume findShrinkBlocker` | 只读，但**耗时不可控**：本机实测一次扫描吃掉 **180+ 秒 CPU 仍未结束**，我不得不 `Kill` 掉（见 §7.4）。而且它的开关注释里 `/newSize` 与 `/shrinkSize` 互斥、开关既能放卷前也能放卷后，顺序说不清。**宁可不做，也不做一个点了就卡住界面的按钮** |
| `volume dismount` 之外的"改卷状态"动作 | 只收了 `dismount`（且是 `info`）。`volume upgrade`、`volume flush`、`volume setLabel`、`tiering setFlags`、`usn createjournal` / `enablerangetracking`、`repair set`、`behavior set`、`8dot3name set` / `strip` / `scan`、`quota track` 等都不收——它们改的是卷/注册表/配额状态，而且**在本机非管理员下多半直接失败**，做成界面只会制造"点了没用"的体验 |
| `fsutil dirty set` | 官方明确说：置上脏位后**下次开机 autochk 会自动检查这个卷**。这既是"会改系统状态"，又会给用户带来一次意料之外的开机磁盘检查。收益（本来就有 chkdsk 可以做这件事）远小于风险 → 不做 |
| `file setZeroData` / `setValidData` / `setEOF` / `setShortName` / `setCaseSensitiveInfo` / `createNew` / `optimizeMetadata` / `setStrictlySequential` | 除了前两个（已按 `info` 收录）以外，其余都是**改文件内容或元数据**的动作：`setEOF` 会截断文件、`setShortName` 会改 8.3 名、`createNew` 会建文件、`optimizeMetadata` 会立刻压实元数据。它们的共同问题是**需要用户先知道自己在干什么**，而 `createNew` + `setZeroData` 还能组合成"用零填满磁盘"这种恶作剧。全部不收 |
| `objectID create` / `delete` / `set` | 官方在 `fsutil objectid` 页首直接给了 **Warning**：`Don't delete, set, or otherwise modify an object identifier. Deleting or setting an object identifier can result in the loss of data from portions of a file, up to and including entire volumes of data.` 官方都这么说了，本包只收 `query` |
| `reparsePoint delete` | 官方文档里这个操作是支持的（`fsutil reparsepoint delete <filename>`），但它会**删掉落链接的重分析点**（junction / 符号链接立刻失效）。本包只收 `query`：想删链接，用资源管理器或 `rmdir` 的语义更清楚 |
| `file findBySID` / `file queryFileNameById` | 前者需要配额启用（官方原文：`if Disk Quotas are enabled`），本机没启用；后者官方语法是 `<volume> <fileid>`，但实测**本机怎么填都是 Error 87**（`C:\ 0x…` / `C: 0x…` 都不行），而能跑通的那次（`C: 0x0005000000000005`）输出是「此文件的随机链接名称为 \\?\C:\」——**信息量极低**。两个都不收 |
| `fsInfo refsInfo` / `volume smrInfo` | 需要 ReFS 卷（实测本机 C:/D:/E: 全是 NTFS，两者都回「A local REFS volume is required for this operation.」）。做了只能在 ReFS 机器上有用，而 ReFS 上这两个命令的行为本机**无法实测**（R4）→ 不收 |
| `usn enumData` / `readJournal` / `enableRangeTracking` | `enumData` 与 `readJournal` 是**成批读日志**的操作（官方示例 `fsutil usn enumdata 1 0 1 c:` 的输出量不可控），而 `readjournal` 的官方语法行是 `[c= <chunk-size> s=<file-size-threshold>]`、参数表里却多出 `minver` / `maxver` / `startusn` —— **两边说不清**，按"对不上就不写"处理；`enableRangeTracking` 会改卷上的 USN 追踪行为 |
| `fsutil repair initiate` 的"成功路径" | 已按 `info` 收录，但**没有实跑**（§6） |
| `output.progress` | fsutil **不画进度条**（没有任何百分比输出），凭空写正则会违反 R1 → 不写 |
| `nextSteps` | 它的输出是"事实查询"（空间多少、脏不脏、有几个链接），**从输出判断不出用户下一步想干什么**。按规范 §2.2 的口径——不写 = 不推荐，比猜一条要好 |
| `locate.versionArgs` / `versionPattern` / `minVersion` | fsutil **没有任何版本开关**（`fsutil` 总表、23 个子命令表、各深度帮助里都没有）。按 playbook §5.2 情形①：**不编一个不存在的版本命令**，改读 exe 文件版本（已写进 `appVersion`）。代价与那批 12 个包相同：宿主会打一句"未能解析出版本号" |
| `fixedArgs`、`session`、`quickActions`、`workingDirectory` | fsutil 不是会话型程序（没有需要保持的选择状态）；所有文件/目录路径都是用户显式选的（`type: file` / `directory`），**没有"默认写到当前目录"的行为** → 不需要覆盖 `workingDirectory`；也没有值得放进右键菜单的固定动作 |

---

## 10. 校验结果（R3）

```
[ OK ] plugins\fsutil\manifest.yaml  (42 动作 / 58 字段 / 字段出处标注 58 个 = 100%)
```

整仓复跑：

```
27/27 个 manifest 通过
字段出处覆盖率: 897/897 (100%)
开关溯源（启发式，仅提示）: 检查 312 个开关，197 个能在参考文档快照里找到
    [待确认] fsutil → 动作 volume-allocation-report 字段 tier: 开关 /tier（参考快照 15 个文件）
```

那条 `[待确认] /tier` **是预期的**：这一层启发式检查用的语料是
`docs/reference/7zip-switch-matrix.json` 与其它参考快照，**不含 fsutil 的官方页面**。
`/tier` 的出处是本机 v1 帮助的 Options 一节（官方语法行里没有它，见 §8.3），
已写进该字段的 `doc`。这一层本来就不阻断构建。

**同一次校验里还发现并改掉的两处自己写的错**（值得记下来）：
① 我把 `type: literal` 写成了 `type`（`literal` 是 `style` 的值）——schema 抓住了；
② 动作 id `8dot3name-query` **以数字开头**，违反 `^[a-z][a-z0-9-]{0,63}$` → 改名 `shortname-query`。

---

## 11. 临时对象与清理（申报）

**系统层面：零个。** 没有创建/删除任何卷、没有改任何卷标、没有动 USN 日志、
没有改任何文件的内容或属性、没有改任何注册表值。
**7 个会改文件系统的动作一条都没执行过**（清单里全是 `execution: info`）。

**文件系统层面（全部在 `%TEMP%\fsutil-probe\` 下）**：

| 对象 | 用途 | 状态 |
|---|---|---|
| `中文文件探针.txt`（28 字节，内容 `hello AllTool fsutil probe`） | 测编码（中文路径回显）、测 `file queryFileID/queryAllocRanges/queryExtents/queryValidData/queryEA/queryOptimizeMetadata/layout/hardlink list/objectID/reparsePoint/sparse/usn readdata/transaction fileinfo` 等十几条只读命令 | **已删除**（收尾前），复核见下 |
| `中文目录探针\`（空目录） | 测 `file queryCaseSensitiveInfo`（目录属性） | **已删除** |
| `subcmd-help.txt`、`deep-help.txt` | 23 个子命令 + 65 个深度操作的 `/?` 快照 | 保留（在 `%TEMP%` 下，属于本轮证据） |
| `matrix-nonadmin.txt`、`matrix-elevated.txt`、`round3.txt`、`nonadmin-final.txt`、`examples-elevated.txt`、`quota-recheck.txt` | 只读矩阵、提权对照、字段风格试验、失败路径、examples 复核 | 保留 |
| `RawRun.ps1`、`ReadOnlyMatrix.ps1`、`Round3.ps1`、`ExamplesCheck.ps1`、`EncodingProbe.ps1`、`LangProbe.ps1`、`NonAdminFinal.ps1`、`QuotaRecheck.ps1`、`check_yaml.py` | 测量脚本（可复跑） | 保留 |

**清理复核命令与结果**（收尾时实跑）：

```powershell
$p = "$env:TEMP\fsutil-probe"
Test-Path "$p\中文文件探针.txt"   # → False
Test-Path "$p\中文目录探针"       # → False
Get-ChildItem $p -Recurse -Force | Where-Object { $_.Name -like '中文*' }   # → 无输出
```

**提权会话的说明**：为了拿到"非管理员 vs 提权"的对照矩阵（§5），
本轮通过 `Start-Process -Verb RunAs` 起了几次**提权 pwsh**，但**只跑只读 argv**：
矩阵脚本里没有任何一条会改文件系统的命令（`Round3.ps1` 里的 `volume dismount` 那一条
在脚本第一版就被删掉了，最终版没有它）。每次提权会话都是 `-Wait`/`WaitForExit` 等到结束，
没有留下长驻的提权进程；中途两条卡住的只读扫描（`volume findShrinkBlocker` 与
`volume allocationReport /v`）被我 `Kill` 掉了，没有影响文件系统。

---

## 12. 给复核者的三条提示

1. **"42 动作 / 58 字段"里有 7 个动作是 `execution: info`**（§6）。它们**永远不会被执行**，
   字段依据全部来自官方页面，`confirmText` 写明了后果。看到它们"没有冒烟结果"不是漏测，
   是红线要求的。
2. **`requiresAdmin` 是照实测矩阵标的**（§5），不是照官方那句"必须以管理员登录"整体套上去的。
   所以你会看到同一个子命令下 `volume list` 不标、`volume diskfree` 标 —— 这是实测结论。
   想复核就重跑 `ReadOnlyMatrix.ps1`（普通身份；脚本里的 argv 顺序就是清单字段的声明顺序）。
3. **本包的输出编码是 `oem`，但 fsutil 自己的文案大多是英文**——不要因为"看到的都是英文"
   就以为判错了。真正的中文出现在**系统错误消息**（「错误 5: 拒绝访问。」）与
   **用户给的中文路径回显**里，字节证据见 §4。
   另外**不要在清单里加文案正则**：同一台机器上 fsutil 的输出语言实测出现过英文与中文两种形态（§4.3）。
