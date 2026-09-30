# getmac 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\getmac.exe`（文件版本 **10.0.26100.8875**）—— Windows 自带。
> 清单：**3 个动作 / 6 个字段 / 字段出处标注 100%**
> **公共事实（Windows 自带命令的普遍脾气、`command: ""` 的宿主缺口等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**，本文只写本包特有的内容。
>
> 实测环境（2026-10-01）：Windows 11 `10.0.26200`、系统 UI 语言 zh-CN、
> 账号 `KANGMORI\Steve`、**非管理员**（`IsInRole(Administrator)=False`，完整性级别 Medium）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/getmac`（2026-10-01 抓取，HTTP 200，页面自报 "Last updated 2024-11-01"） |
| 2 | 本机 `getmac /?` 真实输出 | 1329 字节、写 **stdout**、**退出码 0**。快照**不入库**（微软文本），重抓命令见下方 |

```powershell
# 重新抓本机帮助（getmac /? 很快，但仍建议带超时）
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
& C:\Windows\System32\getmac.exe /?         # 退出码 0，帮助在 stdout
```

**两侧核对结果：完全一致，无多无少。** 官方语法行：

```
getmac[.exe][/s <computer> [/u <domain\<user> [/p <password>]]][/fo {table | list | csv}][/nh][/v]
```

官方 `#parameters` 表列 `/s <computer>`、`/u <domain>\<user>`、`/p <password>`、
`/fo {table | list | csv}`、`/nh`、`/v`、`/?`；本机 `/?` 列 `/S system`、`/U [domain\]user`、
`/P [password]`、`/FO format`、`/NH`、`/V` —— 一一对应。
**本包只收录只读查询**：`/fo`、`/nh`、`/v`、`/s`。

## 2. 覆盖范围

| 动作 id | 分组 | 生成的 argv | 字段 | 实跑 | 退出码 |
|---|---|---|---|---|---|
| `list` | 本地 | `getmac /fo table`（默认）/ `/fo list` / `/fo csv` / `/fo table /nh` | `fo`(enum，默认 table)、`nh` | ✅ | 0 |
| `verbose` | 本地 | `getmac /fo table /v`（默认）/ `/fo list /v` | `fo`、`nh`、`v`(默认 true) | ✅ | 0 |
| `remote` | 远程 | `getmac /s <computer>` | `computer`(必填) | ✅（`/s localhost`） | 0 |

`danger` 全部为空、`requiresAdmin` 全部为否（包级 `runtime.requiresAdmin: false`）——**getmac 本来就没有写操作**。

## 3. 故意没做的部分与原因

| 没做 | 原因 |
|---|---|
| `/u <domain>\<user>`、`/p <password>` | **会有交互陷阱**：官方 `/p` 原文是 "Prompts for input if omitted"，也就是说 `/u` 给了而 `/p` 留空时 getmac **会等在密码提示上**，宿主无法应答（`timeoutSeconds` 是包级设置，救不了单个动作）。要收就得把 `/p` 做成必填，那又逼用户把密码填进表单；而且本机没有第二台机器可以真机验证带凭据的远程查询。**宁可少做，也不留一个会挂住的字段。** |
| 真机验证"/s 到另一台真实计算机" | 手边没有第二台可连的机器。只验了 `/s localhost`（等价本机，exit 0）与 `/s <不存在的名字>`（exit 1 + `ERROR: The RPC server is unavailable.`），清单的 `examples` 只放验过的那条 |
| **"按传输名 / 连接名筛选"** | **getmac 没有这个参数**。官方语法行与本机 `/?` 里都不存在任何按名称过滤的开关；`/v` 只是把 `Connection Name`（连接名）与 `Network Adapter`（网卡名）两列**加进输出**。任务里"按传输名/连接名查看"这条需求，本包的实现是"`verbose` 动作把连接名/传输名列出来给你看"，而不是发明一个筛选开关（R1）。 |
| `versionArgs` / `versionPattern` / `minVersion` | Windows 自带命令没有版本开关（共用文档 §4） |
| `progress.pattern` | 没有进度输出（每次约 1.9 秒，是"慢"不是"有进度"） |
| 工具包级 `category` | 按产品负责人要求：只定义动作分组 |
| `nextSteps` / `quickActions` | 只读查询之间没有必然的下一步，不硬凑 |

## 4. 退出码实测（含失败路径）

微软的 getmac 文档**没有退出码章节**（已核对），下表全部来自本机实测：

| 命令 | 退出码 | stdout / stderr | 说明 |
|---|---|---|---|
| `getmac`（不带参数） | **0** | 642 B / 0 B | 直接给表格 |
| `getmac /?` | **0** | 1329 B / 0 B | **帮助是 0**（与 arp 的 1 相反） |
| `/fo table` `/fo list` `/fo csv` | **0** | 642 / 584 / 411 B | 三种格式都正常 |
| `/fo table /nh` `/nh /fo table` `/nh` `/fo csv /nh` | **0** | 482 / 482 / 482 / 374 B | `/nh` 单用也可以（默认就是 table） |
| `/fo list /v`、`/v /fo list`、`/fo table /v` | **0** | 1122 / 1122 / 907 B | 顺序不敏感 |
| `/FO LIST`（大写） | **0** | 584 B | 开关与取值大小写都不敏感 |
| `-v`（减号写法） | **0** | 907 B | 斜杠与减号都认（清单按官方写 `/`） |
| `/s localhost`、`/s localhost /v`、`/s localhost /fo list` | **0** | 642 / 907 / 584 B | 远程路径的本机自检 |
| `/x` | **1** | 0 B / 69 B | stderr `ERROR: Invalid argument/option - '/x'.` |
| `/fo xml` | **1** | 0 B / 98 B | stderr `ERROR: Invalid syntax. 'xml' value is not allowed for '/fo' option.` |
| `/fo`（缺值） | **1** | 0 B / 79 B | stderr `ERROR: Invalid syntax. Value expected for '/fo'.` |
| `/s`（缺值） | **1** | 0 B / 78 B | stderr `ERROR: Invalid syntax. Value expected for '/s'.` |
| `/fo=list` | **1** | 0 B / 75 B | stderr `ERROR: Invalid argument/option - '/fo=list'.` |
| `/fo list /nh`、`/nh /fo list`、`/fo list /v /nh` | **1** | 0 B / 107 B | stderr `ERROR: Invalid syntax. /NH option is allowed only for TABLE and CSV formats.` |
| `/s 测试主机`（连不上） | **1** | 0 B / 42 B | stderr `ERROR: The RPC server is unavailable.`，耗时约 5.5 s |

**所有错误都写 stderr，成功输出写 stdout。** 清单里的 `exitCodes` 就是 0 / 1 两条。

## 5. 编码实测：判定依据（本包与同批的 12 个包不同）

`runtime.encoding: utf-8`。

| 样本 | 原始字节 | 严格 UTF-8 解码 | 按 cp936 解码 |
|---|---|---|---|
| `getmac /v /fo list` 里的连接名 | `Connection Name:  ` + `e8 93 9d e7 89 99 e7 bd 91 e7 bb 9c e8 bf 9e e6 8e` | ✅ 通过 → **是 UTF-8** | `钃濈墮缃戠粶杩炴帴`（乱码） |

`e8 93 9d` / `e7 89 99` / `e7 bd 91` / `e7 bb 9c` / `e8 bf 9e` / `e6 8e a5` 正是 UTF-8 的
「蓝牙网络连接」。**整份 `getmac /v` 输出（含中文字节）严格 UTF-8 解码通过**，
所以不是一个字节流里混了两种编码。

- **中文样本是真实存在的、不需要构造**：`/v` 的 `Connection Name` 直接取自系统的网络连接名，
  本机就是「蓝牙网络连接 2」与「以太网」。
- 判定用的是 `.NET Process` 重定向 + `File.ReadAllBytes` 拿到的**原始字节**，
  没有经过 PowerShell 的解码（顺带：如果按 console 的 936 去解，中文会显示成
  `钃濈墮缃戠粶杩炴帴 2`，这正是我第一次看错的原因）。
- ⚠ **与同批包不能互抄**：`windows-commands.md` §5 那张表里 11 个命令是 `oem`(936)，
  本机 getmac 实测是 **UTF-8**。共用文档那张表可以补上 getmac 这一行。

## 6. 字段风格与字段顺序：两种写法各跑一次

| 写法 | 命令 | 退出码 | 结论 |
|---|---|---|---|
| **`separate`（两 token）** | `getmac /fo list` | **0** | 官方语法行就是 `{table \| list \| csv}` 用空格分隔 ✅ |
| `attached` + 冒号 | `getmac /fo:list` | **0** | **也接受**（本批的 wevtutil 是"只认单 token"，getmac 正好相反：两种都认） |
| `attached` + 等号 | `getmac /fo=list` | **1** | `ERROR: Invalid argument/option - '/fo=list'.` ❌ |

→ 两种都能跑，**按官方写法取 `style: separate`**（两 token，与文档语法一致，也让命令行预览更像文档）。
`/s` 同样：`/s localhost`（0）与 `/s:localhost`（0）都行，清单取 `separate`。

**字段顺序**（= argv 顺序）：按官方语法行的次序声明 `fo` → `nh` → `v`，
生成 `getmac /fo list /v`。实测这个顺序与逆序都返回 0（`/v /fo list`、`/nh /fo table`、`/fo list /v` 全部 exit 0），
即 getmac 对开关顺序不敏感 —— 但清单仍按文档顺序声明，保证命令行预览与官方语法行读起来一致。

## 7. requiresAdmin：怎么定的

- 包级 `runtime.requiresAdmin: false`，**依据是实测**：非管理员账号（Medium 完整性）下
  13 条只读调用全部 exit 0（§8）。
- **官方文档的措辞会误导**：页面描述与 `/?` 都写着 "This tool enables an administrator to display
  the MAC address …"。实测**标准用户本地查询完全可用**，一个字都不缺。
  → 文档里的 "administrator" 是行文习惯，不是权限要求；**不标 `requiresAdmin`**（宁可漏标也不误标，
  见规范 §2.5）。
- 远程查询（`/s`）的权限取决于**目标机**上的凭据，不是本机提权；官方也没写要提权 → 同样不标，
  只在 `remote` 动作的 `resultNote` 里说明"目标机需要可达且当前账号在目标机上有权限"。

## 8. 真机冒烟测试结果

冒烟脚本（在仓库外，见 §10）照 playbook §5.1 写了三条：白名单逐字比对、红线自检、判不准就算未跑。
判定用 NUL 连接后比字符串，没有用 `-eq` 比数组。

```
红线自检通过：7 条红线全部判为不可跑
getmac.exe /fo table                     exit=0      1981ms out=642    err=0
getmac.exe /fo list                      exit=0      1847ms out=584    err=0
getmac.exe /fo csv                       exit=0      1918ms out=411    err=0
getmac.exe /fo table /nh                 exit=0      1908ms out=482    err=0
getmac.exe /fo table /v                  exit=0      1982ms out=907    err=0
getmac.exe /fo list /v                   exit=0      1965ms out=1122   err=0
getmac.exe /s localhost                  exit=0      2031ms out=642    err=0
共跑 7 条 getmac 用例，非 0 退出码的：（无）
```

清单里 `examples` 的 `expectExitCode` 全部按上表填写，实测逐条对上。

**实测到的输出形态**（做界面时用得上）：

| 选项 | 首行 | 列 |
|---|---|---|
| （默认）/ `/fo table` | `Physical Address    Transport Name` | 2 列 |
| `/fo table /nh` | `18-93-41-54-F5-28   Media disconnected`（无表头） | 2 列 |
| `/fo list` | `Physical Address: 18-93-41-54-F5-28` | 每地址 2 行 |
| `/fo csv` | `"Physical Address","Transport Name"` | 2 列，带引号 |
| `/v`（table） | `Connection Name Network Adapter Physical Address    Transport Name` | 4 列 |
| `/v /fo list` | `Connection Name:  蓝牙网络连接 2` | 每地址 4 行 |

## 9. 未验的部分与原因

| 没验 | 原因 |
|---|---|
| `/u`、`/p`（带凭据的远程查询） | 未收录（§3）：`/p` 省略会交互式索要密码，宿主无法应答；且没有第二台机器可验 |
| `/s <真实的另一台计算机>` 的成功路径 | 手边没有第二台可连的机器；只验了 `/s localhost`（0）与不可达主机（1） |
| 中文界面机器上的措辞型断言 | 本机 getmac 的界面文本是英文；清单里**没有任何依赖文案的判定** |
| `/?` 快照入库 | 微软文本，按 `.gitignore` 既有约定不入库（重抓命令见 §1） |
| `/v` 在 100+ 网卡的机器上的列宽行为 | 本机只有 6 个适配器，列宽截断的实测样本有限 |

## 10. 与官方文档对不上的地方

1. **"an administrator"**：官方描述与 `/?` 都写这个工具是给管理员用的，实测标准用户本地查询
   完全正常（exit 0，数据齐全）。→ 不据文档标 `requiresAdmin`（§7）。
2. **`/fo` 的写法**：官方语法是空格分隔（`/fo {table | list | csv}`），实测 `/fo:list`（单 token）
   同样可用，而 `/fo=list` 报错。**两种都跑的结论写在这里，清单取官方那种。**
3. **`/nh` 的约束是硬错误**：官方只说 "Valid when the /fo parameter is set to table or csv"，
   实测与 `list` 同用会**直接报错并以 1 退出**（`ERROR: Invalid syntax. /NH option is allowed only for
   TABLE and CSV formats.`），不是静默忽略。清单在 `nh` 字段的 `help` 里写明了这一点。
4. **`/v` 的说明太简**：官方只有一句 "Specifies that the output display verbose information"，
   没说会多出哪两列。实测多出 `Connection Name`（连接名）与 `Network Adapter`（网卡名）。
5. **取值大小写**：官方没说 `/fo` 的值是否大小写敏感，实测 `/FO LIST` 可用（退出码 0），
   开关与取值都大小写不敏感。
6. **官方没有退出码章节**：0/1 的语义全部来自实测（§4）。
7. **输出编码与同族命令不一致**：getmac 是 UTF-8，而 ipconfig/netstat/tasklist 等是 OEM 936
   （§5）。这是**同机实测**的差异，不是"从别的包抄来的结论"。

## 11. 本包特有的坑

1. **每次调用约 1.9 秒**：getmac 要枚举网卡，`/?` 也一样（实测 43 ms 是例外，正常查询 1.85–2.03 s）。
   远程连不上时约 5.5 s。界面上要有等待预期。
2. **表格形式会截断内容**：列宽按最长内容对齐，本机
   `Bluetooth Device (Personal Area Network) #2` → `Bluetooth Devic`、
   `Realtek PCIe GbE Family Controller` → `Realtek PCIe Gb`。
   **要完整值必须用 `/fo list`**（清单在 `resultNote` 里写了）。
3. **`/nh` 与 `/fo list` 是互斥的（会 exit 1）**：`list` + `/nh` 不是"表头被去掉"，而是整个命令失败。
   清单的两个动作都有 `nh` 字段，`help` 里写明了这条。
4. **`Physical Address` 可能是 `N/A`**：本机的 Meta Tunnel 虚拟适配器就是 `N/A`
   （`Transport Name` 仍然是有效的 `\Device\Tcpip_{...}`）。界面上不要把它当异常。
5. **`Transport Name` 可能是 `Media disconnected`**：网线没插/无线没连时不是设备路径而是这句话（英文，不随语言变）。
6. **MAC 地址分隔符是减号**（`18-93-41-54-F5-28`），不是冒号；要填进别处（网络白名单、WOL）时注意。
7. **`/s` 不接受反斜杠**：官方原文 "do not use backslashes"，`\\计算机名` 这种写法是错的。
8. **远程查询走 RPC**：目标机防火墙/服务不可达时只有一行 stderr
   `ERROR: The RPC server is unavailable.`，没有更细的原因。

## 12. 临时对象申报

- **系统对象：无。** 本包全部实测都是只读调用，**没有创建、修改或删除任何系统对象**
  （没有加过 ARP 条目、没有改过网卡或连接名）。
- **仓库内**：只新增了 `plugins/getmac/manifest.yaml` 与 `plugins/getmac/NOTES.md`，没有改任何其它文件。
- **仓库外的临时文件**（可随时删除）：
  `D:\AI\swpj\probe\probe-arp-getmac.ps1`、`probe2.ps1`、`smoke-arp-getmac.ps1` 与
  `report1.txt`、`report2.txt`、`smoke-report.txt`（探针脚本与原始报告，保留是为了可复现）。

## 13. 规范缺口 / 宿主问题

公共缺口见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md) §7，本包不重复。

本包另外暴露的一条：**规范没有"字段值省略时要阻止程序进入交互提示"的机制**。
getmac 的 `/u` 不带 `/p` 会等在密码提示上，而 `timeoutSeconds` 是**包级**设置 ——
为了一个动作把整包的超时调小并不合理。这也是本包不收 `/u` `/p` 的直接原因（§3）。
