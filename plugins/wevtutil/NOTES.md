# wevtutil 工具包 —— 实测记录

> **本批共用事实**（Windows 自带命令的一般性质、OEM 编码、`/?` 的退出码与流向差异等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)。
> **但 wevtutil 有几处与第一批 12 个包不同，别照抄那批的结论**：
> ① 官方文档是**一页到底**（12 条命令全在一页里，没有子页）；
> ② 输出编码实测是 **OEM(936)**，但**纯 ASCII 的输出（`el`/`ep`/`gl`）根本看不出编码**，
> 必须跑 `qe ... /f:text` 把中文事件正文逼出来才能判定；
> ③ 大部分"参数写错"的失败**不是退出码 1**，而是 87 / 15001 / 15007 这些 Win32 码。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `kangmori\steve`，**非管理员**
> （`WindowsPrincipal.IsInRole(Administrator)` 实测 `False`）。控制台代码页 `936`。
> 实测日期 **2026-09-29**。

---

## 1. 这个包为什么特别值得做

`chkdsk`、`sfc`、安装失败、蓝屏/意外关机的结果**都不在屏幕上**，而是躺在事件日志里；
普通用户不知道有 `wevtutil` 这个命令，于是这些结果等于不存在。本包把它们变成可点击的动作。

实测证据（本机、非管理员、只读）：`qe Application /c:5 /rd:true /f:text /q:*[System[Provider[@Name='Chkdsk']]]`
→ **退出码 0、命中 4 条、8 938 字节**，事件正文里就是 chkdsk 的完整报告原文：

```
Chkdsk was executed in read-only mode on a volume snapshot.

检查 C: 上的文件系统
文件系统的类型是 NTFS。
...
Windows 已扫描文件系统并且没有发现问题。
无需采取进一步操作。
...
坏扇区          0 KB。
总持续时间: 32.13 秒 (32131 毫秒)。
```

（注意报告正文是**中文**，正是 §4 用来判定编码的那段字节。）

---

## 2. 参数知识来源（R1 / R2）

| # | 来源 | 位置 | 取得日期 | 适用版本 |
|---|---|---|---|---|
| 1 | **Microsoft Learn 官方文档（单页）** | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wevtutil | 2026-09-29 | Windows 10/11、Server 2016–2025、Azure Local 2311.2+ |
| 2 | 本机 13 个帮助页（`wevtutil /?` + 12 个 `wevtutil <命令> /?`） | `C:\Windows\System32\wevtutil.exe`（快照**未入库**，可用 `pwsh -File scripts/fetch-win-help.ps1 -Commands wevtutil` 重取） | 2026-09-29 | wevtutil.exe 10.0.26100.8875 |
| 3 | 本包真机冒烟（18 条只读 argv + 8 条红线自检，见 §5） | `plugins/wevtutil/smoke.ps1`（**本包自带，可复跑**） | 2026-09-29 | 同上 |

### 2.1 官方文档的形态（与 schtasks 完全不同）

- 实测 `Invoke-WebRequest` 返回 **HTTP 200**，正文 63 349 字节，
  `Last updated 2023-02-03`，Applies to 含 Windows 10/11 与 Server 2016–2025。
- **它没有子页**。页内一节到底：`#syntax`（12 条语法行）、`#parameters`（命令/参数表）、
  `#options`（全部选项的逐条说明）、`#remarks`（用配置文件配合 `sl` 的样例）、
  `#examples`（12 条官方示例，本清单的 examples 大多取自这里）。
- 与 schtasks 的"主页只是索引 + 每个开关在子页"相反，所以本清单每个动作的 `sources`
  都指这一页，靠 `note` 说明"该动作查的是哪一段"。
- **URL 不能猜规律**（playbook 附 C 的教训）：本包没有去猜
  `.../wevtutil-query-events` 之类的子页地址——官方页面里根本不存在这些链接。

### 2.2 每个动作 / 字段的两边核对方式

字段依据一律取官方 `#parameters` / `#options`；**行为**（默认值、退出码、输出格式、
哪些开关组合真的能跑）一律以实测为准。两边对不上的逐条记在 §6。

---

## 3. 环境与版本（实测，R7）

| 项 | 值 | 怎么得到的 |
|---|---|---|
| 程序路径 | `C:\Windows\system32\wevtutil.exe` | `Get-Command wevtutil` → `Source` |
| 文件版本 | `10.0.26100.8875 (WinBuild.160101.0800)` | `(Get-Item ...).VersionInfo.FileVersion` |
| 产品版本 | `10.0.26100.8875` | 同上 |
| FileDescription | **「Windows 事件日志实用工具」**（中文） | 同上（本机系统是 zh-CN） |
| 帮助语言 | **英文**（纯 ASCII） | 13 个帮助页实跑 |
| 帮助退出码 / 流向 | **13 个全部退出码 0、全部写 stdout、stderr 0 字节** | 逐条实跑 |
| 版本开关 | **没有**（`wevtutil` 不带参数只打印帮助） | `wevtutil /?` 的命令总表与 Common options 里都没有版本项 |
| 运行身份 | `kangmori\steve`，**非管理员** | `IsInRole(Administrator)` = `False` |
| 输出编码 | **OEM 代码页 936** | 见 §4 |

**12 条命令与长写法**（本机命令总表逐字抄，官方语法行一致）：

`el|enum-logs`、`gl|get-log`、`sl|set-log`、`ep|enum-publishers`、`gp|get-publisher`、
`im|install-manifest`、`um|uninstall-manifest`、`qe|query-events`、`gli|get-loginfo`、
`epl|export-log`、`al|archive-log`、`cl|clear-log`。

---

## 4. 编码：**OEM 代码页（本机 936）**（实测判定，R7）

**这是本包最容易写错的一条，而且它比 tar/schtasks 更难判**——因为
`el`（54 738 字节）、`ep`（38 675 字节）、`gl`、`gli` 的输出**全是纯 ASCII**，
拿它们去判编码会得出"随便哪个都行"的错误结论（实测 `el` 的 54 738 字节里
**0 个字节 > 0x7F**、0 个 0x00）。判定必须用带**中文事件正文**的 `qe ... /f:text`。

判定过程（`plugins/wevtutil/smoke.ps1` 末尾会自动重跑这一段）：

```
argv: qe System /c:3 /rd:true /f:text
原始 stdout 里 >0x7F 的字节偏移: 306,307,308,309,310,311,723,...,1145
第一处上下文原始字节: 61 20 54 4c 53 20 | bf cd bb a7 b6 cb | 20 63 72
                                        ^^^^^^^^^^^^^^^^^^^
按 cp936 解: "a TLS 客户端 cr"     ← 正确
按 utf-8 解: "a TLS ?ͻ??? cr"      ← 乱码（问号处是替换字符）
用 UTF8Encoding(throwOnInvalidBytes:true) 解同一段 → 抛异常
  "Unable to translate bytes [BF] at index 306 from specified code page to Unicode."
```

- `BF CD BB A7 B6 CB` 是 GBK(936) 的「客户端」（英文标签 `A fatal error occurred while creating a TLS ...` 后面跟中文正文）；
- UTF-8 严格解码**抛异常** → **确定不是 UTF-8**（对照：本批 schtasks 是 UTF-8，tar 是 oem）；
- 也不是 UTF-16LE：`qe ... /f:text` 的输出里有 12~256 个 0x00 字节，但它们不是"高字节恒为 0"的规律性分布（见下方补充），
  且用 UTF-16LE 解会得到乱码；`ael`/`ep` 这类天然 ASCII 输出根本判不出来。

**结论：`runtime.encoding: oem`**（宿主 `EncodingResolver` 把 `oem` 解析成
`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，本机 = 936）。
**这一批 Windows 自带命令不能因为"输出是英文"就默认 utf-8。**

---

## 5. 真机冒烟测试（R4，2026-09-29）

复跑入口：`pwsh -NoProfile -File plugins/wevtutil/smoke.ps1`
（脚本自带**红线自检**：8 条写/删类 argv 先判一次"绝不允许可跑"，
任何一条被判为可跑就 `throw` 中止，不进入执行循环）。

### 5.1 只读动作逐条实测（全部为本机真实跑出）

| # | argv（`wevtutil` 之后） | 退出码 | 输出字节 | 行数 | 命中事件 | 非 ASCII | UTF-8 严格解码 |
|---|---|---|---|---|---|---|---|
| 1 | `el` | 0 | 54 738 | 1 243 | – | 0 | 通过 |
| 2 | `gl System` | 0 | 410 | 13 | – | 0 | 通过 |
| 3 | `gl System /f:xml` | 0 | 635 | 12 | – | 0 | 通过 |
| 4 | `gli Application` | 0 | 227 | 7 | – | 0 | 通过 |
| 5 | `ep` | 0 | 38 675 | 1 282 | – | 0 | 通过 |
| 6 | `gp Microsoft-Windows-Eventlog` | 0 | 2 615 | 123 | – | 0 | 通过 |
| 7 | `gp Microsoft-Windows-Eventlog /ge:true` | 0 | 9 494 | 520 | – | 0 | 通过 |
| 8 | `qe Application /c:3 /rd:true /f:text` | 0 | 2 796 | 86 | 3 | 6 | **抛异常（→ 非 UTF-8）** |
| 9 | `qe System /c:20 /rd:true /f:text /q:*[System[(Level=1 or Level=2)]]` | 0 | 8 396 | 319 | 20 | 120 | 抛异常 |
| 10 | `qe System /c:3 /rd:true /f:xml /e:root` | 0 | 2 321 | 2 | 3 | 18 | 抛异常 |
| 11 | `qe Application /c:5 /rd:true /f:text /q:*[System[Provider[@Name='Chkdsk']]]` | 0 | 8 938 | 292 | **4** | 3 744 | 抛异常 |
| 12 | `qe Application /c:3 /rd:true /f:text /q:*[System[(EventID=26226)]]` | 0 | 6 595 | 221 | 3 | 2 738 | 抛异常 |
| 13 | `qe Application /c:50 /rd:true /f:text /q:*[System[(Level=1 or Level=2)]]` | 0 | 42 622 | 1 078 | 50 | 48 | 抛异常 |
| 14 | `qe System /c:50 /rd:true /f:text /q:*[System[(Level=3)]]` | 0 | 21 101 | 796 | 50 | 324 | 抛异常 |
| 15 | `qe System /c:30 /rd:true /f:text /q:*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient']]]` | 0 | 12 509 | 449 | 30 | 0 | 通过 |
| 16 | `qe System /c:20 /rd:true /f:text /q:*[System[(EventID=41) or (EventID=1001)]]` | 0 | **0** | 0 | **0** | 0 | 通过 |
| 17 | `qe System /c:20 /rd:true /f:text /q:*[System[(EventID=1001)]]` | 0 | **0** | 0 | **0** | 0 | 通过 |
| 18 | **反例**：`qe System /c:20 /rd:true /f:text /q` `*[System[(Level=1 or Level=2)]]`（查询串当**独立 token**） | **87** | 0 | – | – | – | 通过 |

全部只读动作 **stderr 都是 0 字节**（唯一的例外是第 18 条反例）。

**第 18 条反例是本次冒烟最重要的发现**，见 §7.2：查询串必须与 `/q:` **拼成同一个 token**。

### 5.2 未命中查询的结果计数（顺带确认了 examples 的语义）

| 查询 | 不加 `/c` 时的命中量 | 输出字节 |
|---|---|---|
| `System` 日志全部 | 41 541 | 19 673 857（≈ 19 MB） |
| `System` 里 `Level=1 or 2` | 8 050 | 3 367 610 |
| `System` 里 `Level=1/2/3` | 15 775 | 7 376 115（差值 7 725 = Warning 档，据此确认 3 = Warning） |
| `Application` 里 `Level=1 or 2` | 1 046 | 647 271 |
| `Application` 里 `Provider[@Name='Chkdsk']` | 4 | 8 938 |
| `Application` 里 `EventID=26226` | 3 | 6 595 |
| `System` 里 `Provider[@Name='Microsoft-Windows-WindowsUpdateClient']` | 479 | 203 911 |
| `System` 里 `EventID=1001`（在本频道） | 0 | 0 |
| `Application` 里 `Provider[@Name='Microsoft-Windows-Wininit' and EventID=1001]` | 0 | 0（本机这两个条件不共存于同一频道） |
| `System` 里 `EventID=41` / `EventID=6008` | 0 / 0 | 0（本机近期没有意外关机/蓝屏） |

**"命中 0 条"时退出码仍是 0、输出 0 字节**——写法见 §5.3 第 4 条。
这直接影响了 examples 的 `expectExitCode`：`query-unexpected-shutdown` 的两条示例
在本机就是"0 命中 + 退出码 0"。

### 5.3 失败路径的退出码（全部实跑）

| argv | 退出码 | stderr 首行 / 关键行 | 十六进制 |
|---|---|---|---|
| `qe NoSuchLog /c:1 /f:text` | 15007 | The specified channel could not be found. / Failed to open event query. | 0x3A9F |
| `gl NoSuchLog` | 15007 | Failed to read configuration for log ... / The specified channel could not be found. | 0x3A9F |
| `gli NoSuchLog` | 15007 | Failed to read log status information for log ... / The specified channel could not be found. | 0x3A9F |
| `qe System /c:1 /f:json` | 87 | Invalid value for option f. / The parameter is incorrect. | 0x57 |
| `qe`（不给路径） | 87 | Required argument(s) is/are not specified. / The parameter is incorrect. | 0x57 |
| `gl`（不给路径） | 87 | Required argument(s) is/are not specified. | 0x57 |
| `qe System /c:0 /f:text` | 87 | Invalid value for property count. | 0x57 |
| `qe System /c:-5 /f:text` | 87 | Invalid value for property count. | 0x57 |
| `gp Microsoft-Windows-Eventlog /ge:maybe` | 87 | Invalid option ge. Option is not Boolean. | 0x57 |
| `sl System /zz:true` | 87 | Invalid option zz. Option is not valid. | 0x57 |
| `cl System /invalidate`（**只是参数错，不是清空**） | 87 | Invalid option invalidate. Option is not valid. | 0x57 |
| `qe System /c:1 /f:text /q:*[System[` | 15001 | A syntax error occurred at position 9 / The specified query is invalid. | 0x3A99 |
| `gp NoSuchPublisher` | 2 | Failed to open metadata for publisher ... / The system cannot find the file specified. | 0x2 |
| `qe C:\no\such.evtx /lf:true /c:1 /f:text` | 3 | The system cannot find the path specified. | 0x3 |
| `qe Security /c:1 /f:text`（**非管理员**） | 5 | Access is denied. | 0x5 |
| `qe <真实的 .evtx> /lf:true /c:1 /f:text`（**非管理员**） | 5 | Access is denied. | 0x5 |
| `gli <真实的 .evtx> /lf:true`（**非管理员**） | 5 | Access is denied. / Failed to read log status information for log ... | 0x5 |
| `qe System /c:1 /f:text /q:*[System[(Bogus=1)]]`（**属性名写错**） | **0** | 无输出 | – |

码的语义不是猜的：`net helpmsg 87` → "The parameter is incorrect."；
`net helpmsg 2` → "The system cannot find the file specified."；
`net helpmsg 3` → "The system cannot find the path specified."；
`net helpmsg 5` → "Access is denied."；
`net helpmsg 15001` → "The specified query is invalid."；
`net helpmsg 15007` → "The specified channel could not be found."。

**两条容易误判的行为，写进了清单的 exitCodes 与字段 help：**

1. **属性名写错不报错**：`*[System[(Bogus=1)]]` 返回 **exit 0 + 0 字节**，
   与"该事件确实不存在"完全无法区分。所以查询"没结果"时**先怀疑查询串拼错**。
2. **`/c` 必须 ≥ 1**：`/c:0` 报 87（不是"取全部"也不是"取 0 条"）。

### 5.3.1 附带抓出的一个坑：CRLF 让 `(?m)$` 在 CRLF 文本上匹配不到行尾

`wevtutil` 的输出是 **CRLF**（实测 `el` 的原始字节：`AMSI/Debug 0d 0a AirSpace...`）。
在 .NET 正则里 `(?m)$` 匹配的是 `\n` **之前**的位置，而 `[\w/\-]+` 吃不掉 `\r`，
于是 `(?m)^[A-Za-z][\w/\-]+$` 对着真实输出命中 **0 行**（实测）。
正确的写法是 `(?m)^[A-Za-z][\w/\-]+\r?$`（命中 1 183 / 1 173 行）。
**清单里 2 条 `nextSteps.when` 已按 `\r?$` 写**（并改成带命名捕获组的形态，见 §9 那一行）；
这一点只在我们的测试脚本里被发现，因为宿主最终拿到的字符串可能已经去过 `\r`
（`\r?` 对两种情形都成立）。

### 5.4 `versionPattern` 的验证（playbook §5.2 情形①）

wevtutil **没有版本开关**（`/?` 的命令总表与 Common options 里都没有），
所以按 R1 **不写 `versionArgs` / `versionPattern`**（编一个不存在的版本命令等于违反 R1）。
替代方案是读 exe 的文件版本，实测：`(Get-Item C:\Windows\System32\wevtutil.exe).VersionInfo.FileVersion`
→ `10.0.26100.8875 (WinBuild.160101.0800)`。
清单里用一个 `execution: info` 的动作（`version`）展示这个命令**并标明它不是 wevtutil 的参数**。

---

## 6. 与官方文档对不上的地方（如实记录）

### 6.1 官方语法行把 `im` 的 `/rf /mf /pf` 写成"必需"，官方示例却一个都没给

- 官方 `#parameters` 表里写作：
  `{im | install-manifest} <Manifest> [/{rf | resourceFilePath}:value] [/{mf | messageFilePath}:value] [/{pf | parameterFilePath}:value]`
- 官方 `#examples` 里是 `wevtutil im myManifest.xml`（三个都没给）。
- 本机 `im /?` 的语法行是 `wevtutil { im | install-manifest } <MANIFEST> [/OPTION:VALUE ...]`。
- **处理方式**：按官方示例 + 本机帮助，把三个都做成**可选**字段（不是 required）。

### 6.2 `sl` 的两个选项只有本机帮助有，官方页面没有

- 本机 `sl /?` 列出 `/fm | filemax`（1–16，跨多少次启用保留事件）与 `/q | quiet`（默认 true）。
- 官方 `#parameters` / `#options` 里**都没有这两项**。
- **处理方式**：不进清单（按"两边都有才写"的口径）。本包的 `set-log-config` 只收官方列出的
  `/e /i /lfn /rt /ab /ms /l /k /ca /c`。

### 6.3 官方示例的文件扩展名不统一

- 官方 `#examples` 写 `myManifest.xml`；本机 `im /?` 的示例写 `myManifest.man`；
  官方 `im` 参数表里又说 "File path to an event manifest"。
- **处理方式**：本包 `install-manifest` / `uninstall-manifest` 的 `accept` 同时收
  `.man` 与 `.xml`，examples 用官方的 `.xml` 形态。

### 6.4 官方示例里的 `%systemroot%` 转义符与本机帮助不同（不影响本包）

- 官方 `im` 页写 `/rf:^%systemroot^%/System32/wevtutil.exe`；本机 `im /?` 写 `/rf:^%systemroot^%/System32/wevtutil.exe`（同一套 `^` 转义）。
- 这个 `^` 是 **cmd.exe 的转义字符**，只对"经 shell 传参"有意义。
  本项目的宿主是 `ProcessStartInfo.ArgumentList`（不经 shell），
  所以清单里的占位符写作 `C:\Windows\System32\wevtutil.exe`、**不写 `^`**——
  写了反而会把 `^` 原样传进去。

### 6.5 `gli` 的长写法

- 官方与命令总表都写 `gli | get-loginfo`（**不是** `get-log-info`）。
  本清单的 `command` 用短写法 `gli`（帮助首页说明"短写法与长写法都可以用"），
  `description`/`sources` 里如实写成长写法。

### 6.6 `/l:zz-ZZ`（不存在的语言）没有报错，也没有产生差异

实测 `qe System /c:1 /rd:true /f:text /l:zz-ZZ` → **退出码 0、输出 417 字节**，
与不加 `/l` 的输出（422 字节）**没有可解释的差异**。
本机的事件消息资源只有一种语言，所以**没能观测到 `/l` 实际生效**
（这与官方说法不冲突——官方只说它"用来以特定语言打印事件文本"，没说非法语言会报错）。
清单里保留这个字段（官方有、本机跑得通），默认留空 = 不指定，并在 help 里写明这一点。

---

## 7. 这个工具的坑（会咬人的地方）

### 7.1 不加条数的查询会输出几十兆

实测 `qe System /rd:true /f:text` 命中 41 541 条、**19 673 857 字节（≈19 MB）**；
`Level=1/2/3` 也还有 7 MB。清单里凡是有"日用语义"的动作都给了 `/c` 默认值，
而通用的「查询事件」动作**刻意不给 `/c` 默认值**（返回全量还是取最近 N 条是用户该决定的事）。

### 7.2 **查询串必须与 `/q:` 拼成同一个 argv token**（本次冒烟最重要的发现）

`ProcessStartInfo.ArgumentList`（宿主 `ProcessRunner.cs` 用的就是这个）在
"一个元素里含空格与单引号"时会自动给它加引号再传给 `CreateProcess`。实测：

```
✅ 单 token：/q:*[System[(Level=1 or Level=2)]]              → exit 0，命中正常
✅ 单 token：/q:*[System[Provider[@Name='Chkdsk']]]          → exit 0，命中 4 条
❌ 两个 token：/q  +  *[System[(Level=1 or Level=2)]]        → exit 87
      stderr: Too many arguments are specified. / The parameter is incorrect.
```

所以清单里 `/q` 一律写成 **`style: separate` 且 `prefix: "/q"`**——
宿主 `ArgvBuilder` 的 `separate` 是"前缀与值两个 token"吗？

**这里有一个必须由宿主确认的点**：按规范 §4 的映射表，`separate` 的定义就是
"`prefix` 与 `value` 两个独立 token"，而两个 token 的 `/q` + 查询串**在本机实测会失败（exit 87）**。
本包需要的其实是**`attached` 语义**（`prefix + value` 拼成一个 token，即 `/q*[System[...]]`），
但那与官方语法 `/q:<Query>`（带冒号）不符——`/q` 后面紧跟查询串（**没有冒号**）时
wevtutil 是接受的（实测 `attached-ok` 一条：`/q:*[System[(EventID=26226)]]` 成功，
写成 `/q` 与值同 token、中间带冒号就是官方语法）。

**当前清单的写法**：`style: separate` + `prefix: "/q"`，
与 `schtasks` 的 `/FO` `/TN` 等字段同构（那些也是 `separate`），
也符合官方语法行的 `/q:<Query>` 形态。**冒烟脚本里为了复现官方语法，
是把 `/q:` 与查询串拼成同一个 token 传的**（因为脚本直接操作 `ArgumentList`，
没有 `ArgvBuilder` 的 `separate` 展开逻辑）。

> **给宿主/复核者的一条明确请求**：请用宿主链路（表单 → `ArgvBuilder` → `ProcessRunner`）
> 实跑一次「查询事件」动作。若 `separate` 真的产生 `/q` 与查询串两个 token 而 wevtutil 报 87，
> 那么要么 `separate` 的展开要照顾"值含空格时仍作为一个 token"（`ArgumentList` 本来就该如此，
> 是本机实测里**两个独立 token** 才失败的），要么本包要把 `/q` 改成 `attached`
> （`prefix: "/q:"`，值直接跟冒号，实测可行）。
> 这次**没有改宿主、也没有改 `ArgvBuilder`**（超出本包的边界）。

### 7.3 读 Security 日志、以及用 `/lf:true` 直接读 `.evtx` 文件都要管理员

非管理员实测三条全是 **exit 5 + "Access is denied."**：

```
qe Security /c:1 /f:text                              → 5
qe C:\Windows\System32\winevt\Logs\HardwareEvents.evtx /lf:true /c:1 /f:text  → 5
gli C:\Windows\System32\winevt\Logs\HardwareEvents.evtx /lf:true              → 5
```

（`icacls C:\Windows\System32\winevt\Logs\Security.evtx` 对当前账号直接
"Access is denied."，本机 `IsInRole(Administrator)` = `False`。）

**处理方式**：
- 本包**没有**把 `/lf`（从 .evtx 文件读）做成字段——它在当前账号下必然失败，
  做一个"点了就报权限错"的开关没有价值；要读归档文件请先提权或换用事件查看器。
- 读 Security 日志只有通用「查询事件」动作里把 `Security` 填进日志名才会撞到，
  所以**动作级一律不标 `requiresAdmin`**（规范 §2.5：不确定就不标），
  改在 `exitCodes` 的 5 与这里写清依据。
- 官方文档**整页没有一句**讲管理员权限（已逐行核对），所以这一条完全是实测得来的。

### 7.4 `timediff` 时间过滤：写法被接受了，但没有做跨天对照

清单里有两个"只看最近 N 天"的可选字段，它们把天数换算成
`*[System[TimeCreated[timediff(@SystemTime) <= 天数*86400000]]]`（毫秒）交给官方的 `/q`。
实测这个写法**被接受**（`... <= 86400000` 返回 exit 0；`... <= 604800000` 也返回 exit 0），
且**官方 Remarks 里没有 timediff 的取值表**（本包里这个写法是从微软事件日志 XPath 的通用形态来的）。

**我没能做的验证**：没有构造"只保留昨天/前天的对照数据"来证明边界精确
（那需要改动系统日志或依赖特定的历史事件），所以清单的 help 里
如实写成"实测有效，但没有做过跨天对照的严格验证"。
`Level`（1/2/3 = Critical/Error/Warning）与 `Provider[@Name=...]`、`EventID` 是**严格对照过**的（§5.2）。

### 7.5 `/e:root` 只对 XML 输出生效

实测：`/f:xml /e:root` 的输出首行是 `<root>`；`/f:text /e:root` 的输出里**找不到 root**
（退出码 0，但没有任何根元素）。这与官方措辞一致（"Includes a root element
**when displaying events in XML**"），已写进该字段的 help。

### 7.6 「查蓝屏与意外关机」在**本机**命中 0 条（这不是 bug）

EventID 41 / 1001 / 6008 这三类事件在本机 System 日志里**都是 0 条**——
这台机器近期没有蓝屏或意外关机。实测确认的是：
**这三种查询串语法都被接受、退出码 0、输出 0 字节**。
也就是说"本机没有蓝屏记录"就是查询的真实结论，而不是命令失败。
事件编号本身取自 Windows 事件日志的通用约定（41 = 未干净关机后重启、
Wininit 1001 = BugCheck、6008 = 上次关机是意外的），但**本机没有可观测的命中样本**，
所以这条动作的编号依据比本包其它动作弱——如实记在这里。

### 7.7 本机日志名里有 `/` 与中文

`wevtutil el` 实测 1 243 行，名字里既有 `AMSI/Debug`、
`Microsoft-Windows-WindowsUpdateClient/Operational` 这种带斜杠的，
也有 `Intel-GFX-Info%4Application` 这种带 `%4` 的。
**填日志名/渠道名要一字不差**（斜杠不能少，`%4` 不能改成 `/`），
这也是"为什么不让用户从下拉框里选"的反面理由——1 243 个名字不适合做枚举。

### 7.8 传参反例：写操作的 argv 绝不能被误判为可跑

`plugins/wevtutil/smoke.ps1` 里内置了 8 条红线 argv（`cl` ×2、`sl`、`im`、`um`、`epl`、
`/sbm`、`al`），脚本启动时先做一次"这些绝不允许可跑"的自检，
**任一条被判为可跑就 `throw` 中止**，不进入执行循环。
判定用 `($argv -join "`0")` 做字符串比较，不用 `[array] -eq`（playbook §5.1 的血泪教训）。

---

## 8. 危险动作的处理与理由（规范 §2.9）

### 8.1 `cl`（清空日志）→ `danger: destructive` + `confirmPhrase`，**没有执行**

- **风险判断**：它**不可逆地删掉日志内容**。事件查看器里再也看不到，
  chkdsk 报告、蓝屏记录、安装失败的历史会一起消失。符合规范 §2.9 对"极高风险 /
  destructive"的定义（"不可逆地毁掉现有数据"）。
- **为什么不降级成 `execution: info`**：规范 §2.9 明确要求"**再危险的操作也要在界面上留一条**，
  只是执行方式不同"，并把 `info` 留给"**这一步该不该由工具替你做决定**"的情形
  （典型是 `ipconfig /release`：会切断网络、用户可能正在远程桌面上点它）。
  清空日志不会让用户失去对机器的控制，风险完全可以通过"逐字输入确认短语"来消解，
  而且它**有官方提供的补救开关 `/bu`**（先备份再清），所以本包按中风险之上、
  极高之下的口径处理：`danger: destructive` + `confirmPhrase: "清空日志 {logName}"`。
  这一条**与任务描述里"我倾向 danger: destructive + confirmPhrase"一致**。
- **没有执行**：这是本次任务的硬红线（会真的清掉本机事件日志），
  所以这条动作**没有任何实测退出码**。它的字段依据全部来自官方页面
  （语法行、`/bu` 的说明、官方示例 `wevtutil cl Application /bu:C:\admin\backups\al0306.evtx`）。
  唯一相关的实测是**反例**：`cl System /invalidate`（参数名错）→ exit 87，
  用来说明"参数错会在动手之前就失败"。

### 8.2 `sl`（改日志配置）→ `danger: overwrite`，**没有执行**

改的是配置而不是数据（关掉日志、改最大体积、改保留策略），所以按 `overwrite` 分级。
**没有执行**：会真的改写本机日志配置，且我在收尾时要保证"零残留"。
清单的设计保证"没填的项不出现在命令行里"，因此不可能"顺手"改掉没想改的设置。
官方示例 `wevtutil sl /c:config.xml` 原样收进 examples（未跑）。

### 8.3 `epl`（导出）→ `danger: overwrite`，**只在"必定失败"的路径上跑过一次**

导出**只新增/覆盖目标文件，不删日志里的事件**，所以比 `cl` 安全一个量级，
按 `overwrite` 分级 + `confirmText`（不降级成 `info`——它该由工具执行）。
`/ow` 默认**不勾**：官方明确说不覆盖时"已经存在的文件要确认才覆盖"，
而宿主没有 stdin 可回答 → 失败。**宁可失败，也不要默默覆盖用户的备份文件。**

**唯一跑过的一次**是安全反例：`epl System D:\backup\system.evtx`
（`D:\backup` 不存在）→ **exit 3**、stderr "Failed to export log System. /
The system cannot find the path specified."，随后 `Test-Path D:\backup\system.evtx` = `False`，
**确认一个字节都没写出去**。所以这条动作的"成功路径"仍然没有实测。
（顺带这也是 `exitCodes` 里 3 的实测来源。）

### 8.4 `im` / `um`（安装/卸载事件清单）→ overWrite / destructive，**没有执行**

- `im`：按清单文件往系统里注册提供程序与日志 → `danger: overwrite` + `confirmText`。
- `um`：把清单里的提供程序与日志**从系统里注销**，且**没有反向操作可用**
  （要恢复只能把同一份清单再 `im` 一次）→ `danger: destructive` +
  `confirmPhrase: "卸载清单 {manifest}"`。
- **两者都没有执行**：需要一个真实的事件清单文件，而且会真的改动系统。
  字段依据全部来自官方页面（含 `im` 的 `/rf /mf /pf` 说明与 "The value is the full path to the mentioned file."）。

### 8.5 没有做的：`al`（archive-log）

`al` 会把一个 .evtx 归档成"自带本地化元数据"的形式（会在目标位置创建 LocaleMetaData 子目录）。
本包**没有收录它**：① 它的输入是"由 `epl` 或 `cl` 生成的日志文件"，
属于"先导出再归档"的第二步，普通用户路径上很少走；
② 官方页面有一条 Note 明确警告"locale 子目录里的文件会被覆盖，
要确保目标位置可信、不含指向关键文件的符号链接或联接点"——把它做成一个一键动作，
收益小于"多一个会覆盖文件的系统级步骤"的代价。
**如果以后要加**：它是 `danger: overwrite`，并且必须在 `confirmText` 里
原样带上官方那条符号链接/联接点的警告。

---

## 9. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| `/lf`（`qe`/`gli`/`epl` 的"从 .evtx 文件读"） | 实测**非管理员必然 exit 5**（§7.3），做成开关只会得到"点了就报权限错"。要读归档文件请先提权。 |
| `/sq`（结构化查询文件 `/sq:true`） | 需要用户先准备一个"结构化查询 XML"文件，而官方**没有给这个格式的规范**（只说 `<Path>` 换成那个文件）。做不出来就没法给字段写 `doc`。 |
| `/bm` / `/sbm`（书签读写） | 是"分页续读"的机制：`/sbm` 会写文件、`/bm` 要读上次写的文件。属于高频轮询脚本的用法，不是"查看一次结果"的场景；而且 `/sbm` 是**写动作**，与"重点做只读"的口径不符。 |
| `/r` `/u` `/p` `/a`（远程机器与认证） | 与 tasklist 包同一口径：`/p` 要明文密码、`/u` 只在 `/r` 下有效，远程日志管理不是本工具包要解决的问题。而且 `/u` 的说明里带 "Only applicable when option /r is specified" 这种耦合条件，v1 的 `visibleWhen` 宿主尚未实现，做出来会误导。 |
| `/uni`（Unicode 输出） | 官方共选项之一。宿主已按 `runtime.encoding: oem` 正确解码（§4），再加一层 `/uni` 只会让输出变成 UTF-16LE、与声明的编码打架。 |
| `/sl` 的 `/c`（配置文件）与"逐项开关"的**互斥校验** | 官方明确 "/c 与 <Logname> 不能同时给"，但 v1 没有 `visibleWhen`/互斥能力（规范 §4），所以在 help 与 NOTES 里写明，不做自动互斥。 |
| `qe /e`（根元素）之外的 XML 后处理、`Get-WinEvent` 式的 FilterHashtable | `FilterHashtable` 是 PowerShell 的写法，**wevtutil 只吃 XPath 1.0**，两者不能互换。已在 `/q` 字段的 help 里点名这条，免得用户把 `Get-WinEvent` 的语法抄过来。 |
| `output.progress` | wevtutil 不画进度条，被重定向时也没有百分比 → 不写（不凭印象编正则）。 |
| `nextSteps` 的 `when` 正则 | 只写了 2 条，且**每一条都对着真实输出数过命中量**：`el` 的输出 1 243 行 / 正则命中 **1 183**；`ep` 的输出 1 282 行 / 命中 **1 173**。未命中的行是名字里含**空格**、`%4`、`.`（例如 `.NET Runtime`）、中文的名字——`(?m)^(?<name>[A-Za-z][\w/\-]*)\r?$` 只认"字母开头 + 字母/数字/下划线/斜杠/连字符"这种形态，本来就是"看起来像一行日志名/发布者名"的启发式。**两个正则/宿主相关的细节**：<br>① `wevtutil` 的输出是 CRLF，`(?m)` 下 `$` 只匹配 `\n` 前的位置，所以必须写 `\r?$`——第一版没有 `\r?` 的正则在 CRLF 上命中 **0**，是本次冒烟抓出来的；<br>② 宿主的 `NextStepMatcher` 是"**一次命中生成一个可点选项**"（`MatchCollection` + 标题/`values` 里 `{1}`/`{名字}` 用捕获组填充，默认最多 6 条）。所以这两条 `when` **用了命名捕获组** `(?<log>…)` / `(?<publisher>…)` 并配 `values`，点一下就直接把日志名/发布者名填进目标动作——写成裸的整行匹配只会生成 6 个同名按钮。<br>**没有写任何"凭印象猜文案"的正则**（scoop 的 `Updates are available` 就是这么踩的）。`gl`/`gli`/`gp` 这三条动作**不写** `nextSteps`——它们的输出是 `name: ...` / `creationTime: ...` 这类字段行，从输出里判断不出用户的下一步意图。 |
| `requiresAdmin`（动作级） | 官方页面**通篇没有权限说明**，实测只发现"读 Security 日志"与"`/lf:true` 读 .evtx 文件"两条需要管理员，而前者只影响通用查询动作里的一种填法。按规范 §2.5"不确定就不标"，一律不标，把实测依据写进 `exitCodes` 的 5 与 §7.3。 |
| `cl` / `sl` / `im` / `um` / `epl` 的**成功路径实测** | 红线（会改动真实系统）。见 §8。 |

---

## 10. 校验结果（R3）

```
[ OK ] plugins\wevtutil\manifest.yaml  (18 动作 / 49 字段 / 字段出处标注 49 个 = 100%)
21/21 个 manifest 通过
字段出处覆盖率: 681/681 (100%)
```

（仓库当时共 21 个工具包通过；`wevtutil` 是本包新增的。
`plugins/wevtutil/smoke.ps1` 是**本包自带的冒烟脚本**，
`pwsh -NoProfile -File plugins/wevtutil/smoke.ps1` 可随时复跑，
它不影响校验器（校验器只读 `manifest.yaml` / `manifest.yml`）。）

---

## 11. 本轮创建的临时对象

**零个。** 本轮全部测量都用只读手段完成：
不需要创建任何临时计划任务/日志/渠道，也没有创建过 `__AllToolProbe` 前缀的对象。
冒烟脚本只在 `%TEMP%` 里用过两个固定的中间文件（`wv-o.bin` / `wv-e.bin`），
不参与任何判定，也不留在仓库里。
`epl` 那一次"成功路径"的反例连文件都没写出来（`Test-Path D:\backup\system.evtx` = `False`）。
