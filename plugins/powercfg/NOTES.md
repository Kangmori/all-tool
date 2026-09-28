# powercfg 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\powercfg.exe`（文件版本 10.0.26100.8521）—— Windows 自带。
> 清单：动作 14 个 / 字段 23 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/reference/win-commands-shared.md`](../../docs/reference/win-commands-shared.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `KANGMORI\Steve`（非管理员）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options` → 快照 `docs/reference/win-docs/powercfg.html` |
| 2 | 本机 `powercfg /?` | 快照 `docs/reference/win-help/powercfg.txt`（2494 字节，写 stdout，**退出码 0**，不入库） |

### 1.1 官方 URL 的位置是本批唯一的例外

- `.../windows-server/administration/windows-commands/powercfg` → **HTTP 404**（实测）
- 正确位置在 `.../windows-hardware/design/device-experiences/powercfg-command-line-options` → HTTP 200

**给后来者的教训**：Windows 命令的官方参考页**不是都在一个目录下**。写清单前必须
实际请求确认 URL 返回 200，不能照着其它命令的路径规律猜。

### 1.2 本机 `/?` 只列命令名，没有逐条参数

本机 `/?` 是"命令列表"（中文说明 + 英文开关名，例如
`/HIBERNATE、/H 启用或禁用休眠功能。`），**没有**每个子命令的参数语法。
逐条语法、参数、示例只有官方网页有（34 个 `h3` 锚点，每子命令一个）。
所以本包的开关依据**以官方网页为主**，本机 `/?` 用于核对命令名与中英文写法。

**这是一处"内置帮助不如官方文档"的实例**（与 `robocopy` 相反 —— 那个的内置帮助更好读）。

### 1.3 URL 锚点的形态值得注意

官方页面的锚点 id **保留了对开关字母的转义**：

| 子命令 | 锚点 |
|---|---|
| `/list` / `/L` | `#list-or-l` |
| `/query` / `/Q` | `#-query-or-q-` |
| `/hibernate` / `/H` | `#hibernate-or-h` |
| `/availablesleepstates` / `/A` | `#availablesleepstates-or-a` |
| `/setactive` / `/S` | `#setactive-or-s` |
| `/change` / `/X` | `#change-or-x` |
| `/batteryreport` | `#batteryreport` |

注意 `#-query-or-q-` 这种**前后带减号**的形式（因为开关本身带 `/`，转义后留下了连字符）。
引用锚点时不要自己"想当然"地写成 `#query`。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `list` | 方案查询 | `/list` | 0 | — | 否 |
| `get-active-scheme` | 方案查询 | `/getactivescheme` | 0 | — | 否 |
| `query` | 方案查询 | `/query` | 2 | — | 否 |
| `aliases` | 方案查询 | `/aliases` | 0 | — | 否 |
| `available-sleep-states` | 睡眠与唤醒 | `/a` | 0 | — | 否 |
| `last-wake` | 睡眠与唤醒 | `/lastwake` | 0 | — | 否 |
| `wake-timers` | 睡眠与唤醒 | `/waketimers` | 0 | — | **是（实测）** |
| `power-requests` | 睡眠与唤醒 | `/requests` | 0 | — | **是（实测）** |
| `battery-report` | 报告 | `/batteryreport` `/output` `/xml` `/duration` | 4 | overwrite | 否 |
| `energy` | 报告 | `/energy` `/output` `/duration` `/xml` | 4 | overwrite | 否 |
| `set-active` | 修改设置 | `/setactive` | 2 | overwrite | 否（**未实测**） |
| `change-timeout` | 修改设置 | `/change` | 3 | overwrite | 否（**未实测**） |
| `hibernate` | 修改设置 | `/hibernate` `/size` `/type` | 3 | overwrite | **是（推断）** |
| `set-value-index` | 修改设置 | `/setacvalueindex` `/setdcvalueindex` | 5 | overwrite | 否（**未实测**） |

## 3. `requiresAdmin` 的判定（本包有两种不同性质的依据，必须分开看）

### 3.1 官方文档明确写的（2 个）

只有两个子命令的说明里有这句话：

> This command requires administrator privileges and must be executed from an elevated command prompt.

- `/systempowerreport`（本清单**没做**，见 §7）
- `/systemsleepdiagnostics`（同上）

**两个都没做**，所以本清单里**没有一个 `requiresAdmin` 是官方文档明确要求的**。
这一条容易被误读成"powercfg 不需要管理员"，所以特别说明清楚。

### 3.2 本机实测发现的（2 个）

| 动作 | 命令 | 实测 |
|---|---|---|
| `wake-timers` | `powercfg /waketimers` | 退出码 **1**，stderr：`此命令需要管理员权限，并且必须从提升的命令提示符中执行。`（58 字节，cp936） |
| `power-requests` | `powercfg /requests` | 同上，退出码 **1**，同样的 58 字节 stderr |

**官方文档完全没有写这两个命令需要提权** —— 这是本机实测的额外发现，清单里注明了两者的
依据差异（文档 vs 实测）。这两条是本批里"实测推翻/补充了文档"的最好例子。

### 3.3 按"会改系统状态"推断的（1 个，标注为推断）

`hibernate`（启用/禁用休眠、改休眠文件大小）标了 `requiresAdmin: true`，但依据只是
"它会改系统状态，且同族的 `/waketimers` 已实测因权限失败"。**官方文档没写它需要提权，
本机也没有实测**。清单的 `resultNote` 里写明了这一点。

### 3.4 没有标的（`set-active` / `change-timeout` / `set-value-index` / `battery-report` / `energy`）

它们同样会改状态（前三个）或写文件（后两个），但**没有任何依据**，所以**不标**。
这是"保守到可能漏提示"的取舍，与 `chkdsk` 的 `check` 是同一类问题，见共用文档 §7.5 与
各包 NOTES 的对应条目。**建议产品负责人拍一个统一规则。**

## 4. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | stderr | 实测输出首行 |
|---|---|---|---|---|
| `powercfg /list` | **0** | 131 B | 0 B | `现有电源使用方案 (* Active)` |
| `powercfg /getactivescheme` | **0** | 59 B | 0 B | `电源方案 GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)` |
| `powercfg /query` | **0** | 9 929 B | 0 B | `电源方案 GUID: 381b4222-… (平衡)` / `GUID 别名: SCHEME_BALANCED` / `子组 GUID: 0012ee47-… (硬盘)` |
| `powercfg /a` | **0** | 417 B | 0 B | `此系统上有以下睡眠状态:` / `待机 (S0 低电量待机) 连接的网络` / `休眠` |
| `powercfg /aliases` | **0** | 1 726 B | 0 B | `a1841308-3541-4fab-bc81-f71556f20b4a  SCHEME_MAX` |
| `powercfg /lastwake` | **0** | 22 B | 0 B | `唤醒历史记录计数 - 0` |
| `powercfg /waketimers` | **1** | **0 B** | **58 B** | stderr：`此命令需要管理员权限，并且必须从提升的命令提示符中执行。` |
| `powercfg /requests` | **1** | **0 B** | **58 B** | 同上 |
| `powercfg /?` | **0** | 2494 B | 0 B | `POWERCFG /命令 [参数]` |

**两处值得注意**：

1. **`/waketimers` 与 `/requests` 的失败信息走 stderr、退出码 1、stdout 完全为空** ——
   只看 stdout 会以为命令没反应。
2. **`powercfg /?` 的退出码是 0**（`ipconfig` / `tracert` / `netstat` / `nslookup` 的 `/?` 都是 1）。

**本机电源方案的事实**（`/list` 与 `/getactivescheme` 的实测输出）：只有一个方案
`381b4222-f694-41f0-9685-ff5bb260df2e  (平衡)`，且它是活动的。

## 5. 编码

stdout 与 stderr 实测都是 **OEM 代码页 936**，`runtime.encoding: oem`。
`powercfg /query` 的 9.9 KB 里中文很多（`硬盘`/`在此时间后关闭硬盘`/`显示器`…），
用 utf-8 解码会全乱。

## 6. 本包特有的坑

1. **官方 URL 不在标准目录下**（见 §1.1）。
2. **本机 `/?` 没有逐条参数**（见 §1.2）。
3. **`/waketimers` 与 `/requests` 需要管理员 —— 文档没说，是实测发现的**（见 §3.2）。
4. **`/query` 是拿 GUID 的地方**：`/setacvalueindex` / `set-value-index` 需要三层 GUID
   （scheme / sub / setting），全部从 `/query` 的输出里抄。清单的 `help` 里指明了这一点。
5. **`/hibernate /size` 的默认不能小于 50**（百分比），且**低于 40% 会被视为"完整休眠文件"**
   —— 官方注释里写的（`HiberFileSizePercent >= 40 is considered as a full hiberfile`）。
6. **`/change` 的 `setting` 只有 8 个取值**（`monitor-timeout-ac/dc`、`disk-timeout-ac/dc`、
   `standby-timeout-ac/dc`、`hibernate-timeout-ac/dc`），全部收录为枚举。
7. **`/change` 的"永不"怎么写，官方文档没说**（通常填 0，但这是经验）。清单**没有替用户预置**，
   在 `help` 里如实说明"文档没写这一点"。
8. **`/batteryreport` 与 `/energy` 默认把报告写到"当前路径"**（官方文档：
   generates an HTML report file in the current path）。所以它们的 `output` 字段留空时，
   文件名是 powercfg 自己定的。本清单没有覆盖 `workingDirectory`
   （工具包级是 `inherit`）—— 这是有意为之：让报告落在宿主的工作目录里，
   用户能从输出里看到。若产品负责人希望固定落到某个目录，再覆盖 `workingDirectory`。
9. **`/energy` 默认观测 60 秒**，可以到几分钟。界面上要有等待预期。
10. **`/systempowerreport` 的官方语法行里写的是 `/getsecuritydescriptor GUID | action`**
    —— 文档原文的语法行有错（复制粘贴错误）。这也是本清单**没有做它**的原因之一（§7）。

## 7. 故意没做的部分与原因

| 子命令 | 为什么不做 |
|---|---|
| `/systempowerreport` | 官方文档明确需要管理员；且**官方语法行写错了**（写成 `/getsecuritydescriptor GUID | action`），照抄会做出错误参数 |
| `/systemsleepdiagnostics` | 同上（需要管理员；官方也标注它"已弃用，改用 /systempowerreport"） |
| `/sleepstudy` | 生成报告但需要现代待机（Modern Standby）支持；本机是台式工作站 |
| `/srumutil` | 从 SRUM 转储能量估算数据，极冷门 |
| `/energytrace` / `/energy /trace` | 记录 trace 而不分析；输出是二进制 trace 文件，界面展示不了 |
| `/devicequery` `/deviceenableawake` `/devicedisablewake` | 设备唤醒管理。`devicequery` 是只读的、可以做，但它需要 11 个 `query_flag` 取值（`wake_from_S1_supported` … `all_devices`），枚举很长且冷门。**留作下一批** |
| `/import` `/export` `/provisioningxml` | 方案导入导出，涉及文件路径与 XML，冷门 |
| `/changename` `/duplicatescheme` `/delete` `/deletesetting` | 改方案本身（改名/复制/删除），`/delete` 尤其危险（删掉正在用的方案）。本批只做了"切换"与"改设置值" |
| `/getsecuritydescriptor` `/setsecuritydescriptor` | 操作电源设置的安全描述符，极冷门且需要管理员 |
| `/requestsoverride` | 设置电源请求替代，极冷门 |
| `/powertrottling` | 为应用控制电源节流，极冷门（且官方只在命令列表里列了名字） |
| `/queryaltitudes` `/qa` | 高度相关的电源方案（飞行器/数据中心用），冷门 |

另外：没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`
（`/energy` 与 `/batteryreport` 的进度是分阶段文字，不是百分比）；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

| 动作 | 命令 | 未跑原因 |
|---|---|---|
| `set-active` | `powercfg /setactive <GUID>` | 会切换系统电源方案（改系统状态） |
| `change-timeout` | `powercfg /change <项> <分钟>` | 会改系统超时设置 |
| `hibernate` | `powercfg /hibernate off|on|/size|/type` | 会启用/禁用休眠、改休眠文件（禁用会删 `hiberfil.sys`） |
| `set-value-index` | `powercfg /setacvalueindex …` | 会按 GUID 直接改电源设置值 |
| `battery-report` | `powercfg /batteryreport` | **会往磁盘写 HTML 报告文件**。虽然只读系统数据，但按本批"只跑绝无副作用的命令"的红线算作写操作 |
| `energy` | `powercfg /energy` | 会写文件并**持续观测至少 60 秒** |
| `wake-timers` / `power-requests` | `/waketimers` `/requests` | **需要管理员，实测直接失败**（退出码 1） |

**实跑的是 7 个只读动作**（`/list`、`/getactivescheme`、`/query`、`/a`、`/aliases`、
`/lastwake`，以及两次失败的 `/waketimers`、`/requests`）。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及：

1. **`requiresAdmin` 的判定标准需要统一规则**（见 §3）：
   本包同时存在"官方明确"（没做）、"实测发现"（做了两个）、"经验推断"（做了一两个）三种依据，
   还有一批"会改状态但没依据所以没标"的。**这是本批暴露出的最需要拍板的一条规范问题。**
2. **`/hibernate` 是"带可选子参数的命令"**：`powercfg /hibernate`（无参，报告状态）、
   `/hibernate on`、`/hibernate off`、`/hibernate /size 100`、`/hibernate /type reduced`
   是**五种不同形态**。本清单用 `literal` 把前三种写成一条枚举（`args: ["/hibernate"]` /
   `["/hibernate","on"]` / `["/hibernate","off"]`），另把 `/size` 与 `/type` 做成独立字段。
   组合起来能表达全部五种，但**这是靠 `literal` 的 `args` 装多个 token 实现的**，
   属于"用现有能力拼出来"，不是规范为这种形态设计的。建议规范考虑
   "子命令 + 可选子参数"的表达方式。

## 10. 给后来者的提醒

- **URL 一定要实际请求确认**（`powercfg` 就是 404 的反例）。
- 锚点别自己猜（`#-query-or-q-` 前后带减号）。
- `/waketimers` 与 `/requests` 需要管理员是**实测**结论，官方文档没有。
- 想加 `/systempowerreport` 之前先看它的官方语法行是错的（§7）。
- 想加 `/devicequery`：它是只读的、比较适合做成动作，但需要 11 个取值枚举。
