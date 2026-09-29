# schtasks 工具包 —— 实测记录

> **本批共用事实**（Windows 自带命令的一般性质、OEM 编码、`/?` 的流差异等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)。
> **但 schtasks 有三处与第一批 12 个包不同，别照抄那批的结论**：
> ① 输出编码是 **UTF-8**（不是 OEM/cp936）；② 帮助文本是**英文**（不是中文）；
> ③ 官方文档是**一页索引 + 六个子页**（不是"一页到底"）。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `kangmori\steve`，**非管理员**
> （`WindowsPrincipal.IsInRole(Administrator)` 实测 `False`）。控制台代码页 936。
> 实测日期 **2026-09-29**。

---

## 1. 环境与版本（实测）

| 项 | 值 | 怎么得到的 |
|---|---|---|
| 程序路径 | `C:\Windows\system32\schtasks.exe` | `Get-Command schtasks` → `CommandType: Application` |
| 文件版本 | `10.0.26100.8875 (WinBuild.160101.0800)` | `(Get-Item ...).VersionInfo.FileVersion` |
| 产品版本 | `10.0.26100.8875` | 同上 |
| 是否随系统提供 | 是，`System32` 下，不需要安装 | 实测 |
| 运行身份 | `kangmori\steve`，**非管理员** | `whoami` + `IsInRole(Administrator)` = `False` |
| 输出编码 | **UTF-8** | 见 §3 |
| 帮助语言 | **英文**（纯 ASCII，904 / 1847 / 9964 字节） | 见 §5 |
| 版本开关 | **没有**（无 `--version`，`/?` 是帮助） | 按 R1 不写 `versionArgs`/`versionPattern` |

---

## 2. 参数知识来源（R1）

| # | 来源 | 位置 | 取得日期 |
|---|---|---|---|
| 1 | schtasks 命令主页（索引 + 必需权限） | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks` | 2026-09-29 |
| 2 | `/query` 官方参考 | `.../schtasks-query` | 2026-09-29 |
| 3 | `/create` 官方参考 | `.../schtasks-create` | 2026-09-29 |
| 4 | `/change` 官方参考 | `.../schtasks-change` | 2026-09-29 |
| 5 | `/delete` 官方参考 | `.../schtasks-delete` | 2026-09-29 |
| 6 | `/run` 官方参考 | `.../schtasks-run` | 2026-09-29 |
| 7 | `/end` 官方参考 | `.../schtasks-end` | 2026-09-29 |
| 8 | 本机 8 个帮助页的真实输出 | `schtasks /?`、`/query /?`、`/create /?`、`/change /?`、`/delete /?`、`/run /?`、`/end /?`、`/showSid /?` | 2026-09-29 |

**七个官方 URL 全部实测 HTTP 200**（用 `Invoke-WebRequest` 逐个请求；`web_fetch` 在本机不可用，
见 `AGENTS.md` §4.1）。**主页只是索引**：它只有命令总表（`schtasks change` / `create` / `delete` /
`end` / `query` / `run` 六行）与"必需权限"一节，**所有开关细节都在子页里**。
这一点与 `ipconfig`/`powercfg`（一页到底）不同，写清单时容易只抓主页而漏掉开关依据。

本机帮助快照**不入库**（微软文本，见 `.gitignore` 与 `docs/ai/windows-commands.md` §2.1），
需要时重新抓：

```powershell
pwsh -File scripts/fetch-win-help.ps1 -Commands schtasks
```

---

## 3. 输出编码：utf-8（实测，与第一批 12 个包不同）

**步骤一：发现"看不出来"。** `schtasks /?` 的 904 字节、`schtasks /query` 的 41 827 字节、
甚至 `/query /fo csv /v` 的 184 257 字节，**全是纯 ASCII（>127 的字节数为 0）**。
这台机器的 MUI 是英文、任务名也全是 ASCII，所以从正常输出里**根本推不出编码**。

**步骤二：用中文任务名触发一次，让编码自己暴露。** 建一个名字带中文的探针任务
（`__AllTool编码探针`），然后 `/Query /TN` 取原始字节：

```
字节序列（任务名部分）: 5f 5f 41 6c 6c 54 6f 6f 6c | e7 bc 96 e7 a0 81 e6 8e a2 e9 92 88
                                              ↑「编」  ↑「码」  ↑「探」  ↑「针」
按 UTF-8 解: __AllTool编码探针     ← 正确
按 cp936  解: __AllTool缂栫爜鎺㈤拡  ← 乱码
```

`e7 bc 96` / `e7 a0 81` / `e6 8e a2` / `e9 92 88` 正是「编码探针」的 UTF-8 编码。
**结论：`runtime.encoding: utf-8`。** 探针任务已当场删除（`Get-ScheduledTask` 复查 0 条）。

### 3.1 这个结论为什么可信：它没有受我的测试方式污染

第一次测到 UTF-8 时我怀疑是"我自己的 pwsh 把控制台代码页设成 UTF-8 影响了子进程"（P4 那类陷阱）。
所以补做了隔离复验：给子进程显式设 `StandardOutputEncoding = cp936`
（`STARTUPINFO`/`ConsoleOutputCP` 于是是 936），输出**仍然是 UTF-8**。
证据是 `GetString(936)` 得到乱码 `缂栫爜鎺㈤拡`，`GetString(utf8)` 得到正确的 `编码探针`。
另外 `chcp` 实测为 `Active code page: 936`，说明不是"整个会话被改成 65001"造成的假象。

### 3.2 对界面的含义

- 按 `utf-8` 解码正确；如果哪天发现中文任务名显示成乱码，第一个要怀疑的是这条。
- 反过来，**如果照抄第一批的 `oem`，中文任务名会全部显示成乱码**——这是本清单最值得记住的一点。

---

## 4. 覆盖范围（12 个动作 / 41 个字段，出处 100%）

| 动作 id | 命令 | 字段 | danger | 实测 |
|---|---|---|---|---|
| `list-tasks` | `/Query` | 0 | — | ✅ exit=0，41 827 B |
| `list-tasks-detail` | `/Query /FO LIST /V` | 0 | — | ✅ exit=0，**437 167 B** |
| `list-tasks-csv` | `/Query /FO CSV /NH` | 0 | — | ✅ exit=0，21 641 B |
| `list-tasks-table` | `/Query /FO TABLE /NH /V` | 0 | — | ✅ exit=0，272 067 B |
| `query-task` | `/Query /TN …`（+`/V`、`/FO`） | 3 | — | ✅ exit=0（存在）/ exit=1（不存在） |
| `show-sid` | `/ShowSid /TN …` | 1 | — | ✅ exit=0，173 B |
| `create-basic` | `/Create` | 21 | overwrite | ⚠️ 只做了参数形状探针后即删，见 §11 |
| `create-from-xml` | `/Create /XML` | 4 | overwrite | ❌ 未跑 |
| `change-task` | `/Change` | 8 | overwrite | ⚠️ 探针任务上跑过 `/ENABLE`；系统任务上实测 Access is denied |
| `run-task` | `/Run` | 1 | — | ⚠️ 探针任务上跑过；系统任务上 Access is denied |
| `end-task` | `/End` | 1 | — | ⚠️ 探针任务上跑过；系统任务上也返回 0 |
| `delete-task` | `/Delete` | 2 | **destructive** | ⚠️ 只删过自己建的探针任务 |

左侧分组：`查询任务`（6）/ `新建任务`（2）/ `修改任务`（1）/ `运行与结束`（2）/ `删除任务`（1）。
**刻意不写工具包级 `category`**——按产品负责人的要求，工具包归到哪一组由用户自己决定
（第一批 12 个包也都是这么做的）。

### 4.1 校验器的"开关溯源"对 schtasks 报了 30 条"待确认"——这不是清单的问题

`scripts/validate-plugins.py` 的第 5 层（R1 的启发式检查）会去
`docs/reference/<id>*` 与 `docs/reference/win-*/` 里找语料。本机
`docs/reference/win-switches/_switches.json`（81 KB）是第一批 13 个命令的
"官方页面表格行 + 小节锚点 + 语法块"提取结果，**里面没有 `schtasks` 键**，
所以 schtasks 的 `/TN`、`/SC`、`/FO`… 一条都匹配不上，全部落进"[待确认]"清单。
（顺带记录一个不一致：`scripts/extract-win-docs-switches.ps1` 的 `-DocsDir` 默认值是
`docs/reference/win-docs`，而现存的产物在 `docs/reference/win-switches/`。
重新跑那个脚本前要先对齐目录。）

- 这一层**只提示、不阻断**（脚本注释里写着"刻意只做提示、不做门禁"，它的定位是给审阅者一个信号）。
- 校验结论本身是绿的：`[ OK ] plugins\schtasks\manifest.yaml (12 动作 / 41 字段 / 字段出处标注 41 个 = 100%)`。
- **每条开关的真实出处都在清单的 `doc` / `sources` 里**，指向具体的官方子页与锚点，可以人工逐条核对。
- 想让这层也变绿，正确做法是**把 7 个 schtasks 官方页面也抓进 `win-docs/` 再跑一次
  `scripts/extract-win-docs-switches.ps1`**（`scripts/**` 与 `docs/reference/**` 在本次任务的禁改范围内），
  而不是手工往 `_switches.json` 里塞一个键——那会变成"语料是人写的"，
  与那层"用抓下来的官方语料做启发式"的前提相冲突。

---

## 5. `/?` 的实测行为（8 个帮助页）

| 帮助页 | 退出码 | stdout | stderr | 语言 |
|---|---|---|---|---|
| `schtasks /?` | 0 | 904 B | 0 B | 英文 |
| `/query /?` | 0 | 1 847 B | 0 B | 英文 |
| `/create /?` | 0 | 9 964 B | 0 B | 英文 |
| `/change /?` | 0 | 3 984 B | 0 B | 英文 |
| `/delete /?` | 0 | 1 245 B | 0 B | 英文 |
| `/run /?` | 0 | 1 021 B | 0 B | 英文 |
| `/end /?` | 0 | 896 B | 0 B | 英文 |
| `/showSid /?` | 0 | 472 B | 0 B | 英文 |

- **八个全是退出码 0、全写 stdout**。这与第一批"各命令 `/?` 退出码互不相同、
  `nslookup`/`netstat` 写 stderr"的混乱情况完全不同。**不要照抄 `/?` 退出码 = 1 的假设**。
- **帮助是英文**：系统是 zh-CN、代码页 936，但 schtasks 的帮助语言集是英文，且全部纯 ASCII。
  所以"用中文帮助核对中文开关名"这条路在这里走不通，我是用英文帮助逐条对官方页面的。

---

## 6. 退出码（官方各子页**都没有退出码章节**，下面是实测）

| 命令 | exit | 关键输出 |
|---|---|---|
| `schtasks /?` 及 7 个子命令 `/?` | **0** | 帮助文本（8/8 都是 0） |
| `/Query`、`/Query /FO LIST /V`、`/Query /FO CSV /NH`、`/Query /FO TABLE /NH /V` | 0 | 正常列表 |
| `/Query /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"` | 0 | 280 B |
| `/ShowSid /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"` | 0 | 173 B |
| `/Query /TN "NoSuchTaskXYZ"` | **1** | stderr：`ERROR: The system cannot find the file specified.`（52 B） |
| `/Query /TN "\No Such Folder\NoSuchTask"` | **1** | 同上 |
| `/Create`（非管理员，普通计划类型，自己文件夹） | **0** | `SUCCESS: The scheduled task "..." has successfully been created.` |
| `/Create /SC ONSTART /DELAY 0001:00`（非管理员） | **1** | stderr：`ERROR: Access is denied.` |
| `/Create`（任务名已存在且**没给** `/F`） | **1** | stdout：`WARNING: The task name "..." already exists. Do you want to replace it (Y/N)?` |
| `/Change … /ENABLE`（自己文件夹的任务） | **0** | `INFO: … has already been enabled.` + `SUCCESS: …` |
| `/Change … /DISABLE`（系统任务） | **1** | stderr：`ERROR: Access is denied.`（27 B） |
| `/Run /TN …`（自己文件夹的任务） | **0** | `SUCCESS: Attempted to run the scheduled task "..."`（69 B） |
| `/Run /TN …`（系统任务） | **1** | stderr：`ERROR: Access is denied.` |
| `/End /TN …`（没有实例在跑） | **0** | `SUCCESS: The scheduled task "..." has been terminated successfully.`（85 B） |
| `/End /TN …`（**系统任务**） | **0** | 同样 SUCCESS（见 §8.1） |
| `/Delete /TN … /F`（自己文件夹的探针任务） | **0** | `SUCCESS: … was successfully deleted.`（77 B） |
| `/Delete /TN … /F`（系统任务） | **1** | stderr：`ERROR: Access is denied.` |
| `/Delete /TN "\Microsoft" /F` | **1** | stderr：`ERROR: Access is denied.`（文件夹当任务名删） |

清单里的 `exitCodes` 只写 0 / 1 两档，因为**实测只见到这两个值**，官方也没有退出码表。
（`/HRESULT` 会改变退出码形态，本清单刻意不收——理由见 §7.6。）

### 6.1 两条会"骗人"的成功输出

1. **`/Run` 的 `SUCCESS: Attempted to run …`**——`Attempted` 只表示"已请求启动"，
   任务本身成没成功要看它的「上次运行结果」。清单的 `resultNote` 写明了这一点。
2. **`/End` 对没在跑的任务也返回 `SUCCESS: … terminated successfully`**——
   这句 SUCCESS 不等于真的杀掉了一个进程。所以 `end-task` 也写了 `resultNote`。

---

## 7. 来源冲突记录（官方页面 vs 本机 `/?`）

**处理原则**（沿用第一批）：**字段依据以官方页面为准**；本机独有的开关一律不写进清单；
两边对不上的逐条记在这里。

| # | 项 | 官方页面 | 本机 `/?` | 本清单怎么处理 |
|---|---|---|---|---|
| 7.1 | `/ShowSid` | **七个子页与主页都没有** | 有，且有独立帮助页（472 B） | **收了**（`show-sid` 动作），但在 `sources` 里如实写明"依据只有本机帮助 + 实测" |
| 7.2 | `/change` 的 `/DELAY` | change 页的**语法行与参数表都没有** | `/change /?` 的语法行**和**参数表都有（`/DELAY delaytime`，注明仅 ONSTART/ONLOGON/ONEVENT） | **不收**。create 页有 `/delay`，那是 `/Create` 的；`/Change` 的 `/DELAY` 只有本机这一侧有依据，按"只用官方确认存在的开关"处理 |
| 7.3 | `/v` 与 `/nh` 的适用范围 | `/v` "is valid with the LIST or CSV output formats"、`/nh` "is valid with the TABLE or CSV output formats" | 本机 `/query /?` 的示例里有 `SCHTASKS /Query /FO TABLE /NH /V` | **两个开关都收**（官方页面各自也定义了它们）；实测 `TABLE+/V` 与 `/nh` 都正常工作，即官方那句限制与实际行为不符。差异记在这里 |
| 7.4 | `/tn` 的**通配符** | "Does not accept wildcards." | 未提 | **按官方写"不接受通配符"**。实测确认：`/Query /TN "\Microsoft\Windows\Defrag\*"` → exit=1 `The system cannot find the file specified.`；`"...\Defrag\"`、`"...\Defrag"`、`"...\Defrag*"` 也都失败（最后一个是 `The system cannot find the path specified.`）。**所以任务书里"查询某文件夹下的任务（`/TN <文件夹>\*`）"这条做不到**——真正能用的办法是 `/Query` 列出全部后再看 `Folder:` 行，或用 `/Query /TN` 精确到一个任务 |
| 7.5 | `/RU` 的取值 | create/change 页只列 `""` / `NT AUTHORITY\SYSTEM` / `SYSTEM` | 另外列了 `NT AUTHORITY\LOCALSERVICE` / `NT AUTHORITY\NETWORKSERVICE` 与三者已知 SID（"For v2 tasks"） | 两个来源不冲突（是超集）；`change-task` 的三个运行身份项按**本机帮助**写，并在字段 help 里点明"官方网页未列" |
| 7.6 | `/HRESULT` | query/delete/run/end 四个页面的参数表里有 | 每个子命令的语法行与参数表都有 | **不收**。它的作用只是"让退出码用 HRESULT 形态"，对本项目"帮人看清命令"没有增益，而且会把 §6 实测的 0/1 语义换成另一种编码。**这是刻意的取舍，不是漏写** |
| 7.7 | `/Run` 的 `/I` | run 页**没有** | `/run /?` 的语法行与参数表都有（"Runs the task immediately by ignoring any constraint."） | **不收**：只有本机这一侧有依据，且"忽略约束立即运行"属于会绕过电池/空闲等限制的行为，不该由我按记忆放进去 |
| 7.8 | `/Query` 的 `/XML` | query 页有 `/xml`（"Outputs all task definitions on the system to XML format"） | `/query /?` 写作 `/XML [xml_type]`，并解释 `xml_type=ONE` 才输出单个合法 XML | **不收**（见 §10）：`/Query /XML` 会把上百个任务的完整 XML 一次性倒出来（本机 192 个 `\Microsoft\` 任务），是"原始转储"而不是"帮人看清"，而且 `xml_type` 的写法只有本机帮助有 |
| 7.9 | `/create` 的 `/V1`、`/EC`、`/I`、`/NP`、`/Z` 等 | 前三个 create 页都有 | 也都有 | 只有 `/V1`/`/EC`/`/I` **没收**（理由见 §10）；`/NP`/`/Z`/`/IT`/`/RL`/`/DELAY`/`/F` 收了。**`/Z` 例外**：它在 create 页与 change 页都有，但**本机 `/change /?` 的参数表漏了它**（只出现在语法行里）——那种情况我按官方页面收（两者都有依据，不冲突） |
| 7.10 | 日期格式 | "varies with the locale … Only one format is valid for each locale" | 写死 "The format is yyyy/mm/dd" | **两个来源不冲突**（本机帮助说的是这台机器上的形态）。本机实测输出里日期是 `2026/9/29`，确认 `yyyy/M/d`。清单里 `/SD`、`/ED` 只做文本框、**不校验格式**，help 里写明随区域设置变化 |

---

## 8. 非管理员下的真实行为（本节是本次实测最有价值的部分）

官方主页只有一句概括："To schedule, view, and change **all** tasks on the local computer,
you must be a member of the Administrators group."
**实测下来这句话不能简化成"schtasks 要管理员"**——它对"自己的任务"和"系统任务"是两回事。

### 8.1 逐条实测（当前账号非管理员）

| 操作 | 对自己文件夹里的任务 | 对系统任务 `\Microsoft\Windows\Defrag\ScheduledDefrag` |
|---|---|---|
| `/Query`（读） | ✅ 可读（也可读系统任务，exit=0） | ✅ exit=0 |
| `/Query /FO LIST /V` | ✅ exit=0 | ✅ exit=0 |
| `/Create`（普通计划类型） | ✅ **exit=0，成功创建** | —（新建总是落在自己的文件夹） |
| `/Change /ENABLE` | ✅ exit=0 | ✅ **exit=0**（原本就启用，返回 INFO + SUCCESS） |
| `/Change /DISABLE` | （未单独测） | ❌ **exit=1 Access is denied.** |
| `/Run` | ✅ exit=0 | ❌ **exit=1 Access is denied.** |
| `/End` | ✅ exit=0 | ✅ **exit=0**（与 `/Run`、`/Change /DISABLE` 不同！） |
| `/Delete /F` | ✅ exit=0 | ❌ **exit=1 Access is denied.** |

**结论：`/Change`、`/Run`、`/Delete` 的成败取决于那个任务自己的安全描述符**，
不是"这个命令要不要管理员"。同样是 `/Change`，`/ENABLE` 在一个已经启用的系统任务上返回 0
（因为它没真正改动任何东西），`/DISABLE` 就 Access is denied；`/End` 一律返回 0
（请求被接受，即使没有实例在跑）。

**因此本清单动作级一律不标 `requiresAdmin: true`**（按规范 §2.5 "不确定就不标"）。

### 8.2 唯一一条"实测确认非管理员会失败"的动作内开关

```
schtasks /Create /TN <探针> /TR cmd.exe /SC ONSTART /DELAY 0001:00 /F
→ exit=1，stderr：ERROR: Access is denied.
```

同一批探针里，`/SC WEEKLY /D MON`、`/SC WEEKLY /D MON,WED,FRI`、`/SC DAILY /MO 2`、
`/SC MONTHLY /MO LASTDAY /M *`、`/SC MONTHLY /MO FIRST /D MON` **全部 exit=0 成功**。
只有带 `/SC ONSTART` **且** `/DELAY` 的那条被拒——需要"以系统权限在开机时运行"，
所以非管理员建不了。这条写进了 `create-basic` 的 `delay` 字段 help。

> 说明：因为需要管理员，`/SC ONSTART` 本身**没有**和 `/DELAY` 拆开单独测过。
> 所以我不说"ONSTART 需要管理员"，只说"ONSTART + DELAY 实测被拒"（就事论事，不推广）。

---

## 9. 风险分级的理由（规范 §2.9）

| 级别 | 动作 | 理由 |
|---|---|---|
| 低（什么都不写） | `list-tasks`、`list-tasks-detail`、`list-tasks-csv`、`list-tasks-table`、`query-task`、`show-sid` | 纯读。实测退出码 0/1，不改动任何状态 |
| 中（`overwrite` + `confirmText`） | `create-basic`、`create-from-xml`、`change-task` | 会改动**系统计划任务库**：新建一条任务、用 XML 里的定义建任务、改动已有任务的属性。按规范"创建类操作不动已有数据，属于中风险" |
| 逐字确认（`destructive` + `confirmPhrase`） | `delete-task` | `/Delete` 会把任务从库里删掉，schtasks **没有任何撤销手段**。官方示例还给了 `schtasks /delete /tn * /f`（删本机全部任务），所以用 `confirmPhrase: "删除计划任务 {taskName}"` 让人逐字输入 |
| **无 danger（刻意）** | `run-task`、`end-task` | 见 §9.3 |

### 9.1 为什么 `/Change` 只给 overwrite、不给 destructive

官方原文说明 `/change` 能改的是"要运行的程序、运行账号与密码、交互属性"——**它改的是任务的配置**，
不删除任务、也不动任务以外的数据。规范 §2.9 的 destructive 口径是"**不可逆地毁掉现有数据**"，
`/change` 不符合（改动本身可以用同一个命令改回去）。所以给 overwrite + `confirmText`。

### 9.2 为什么 `create-basic` 的 `/SC` 刻意不给默认值

"默认值等于替用户做决定"。这里还有一条实测依据：**不给 `/F` 而任务名已存在时，
schtasks 会打印 `WARNING: … Do you want to replace it (Y/N)?` 并以退出码 1 失败**
（宿主没有 stdin 可以回答）。如果我再替用户预选一个 `/SC`，
"一键执行"的路径就更顺、更容易在没想清楚的时候写出任务。所以 `/SC` 留空、由用户自己选。

`/F` 也默认 `false`（不覆盖），理由同上：默认不覆盖比默认覆盖安全。

### 9.3 为什么 `/Run` 与 `/End` 不标 danger（这是判断，不是漏写）

- **官方对 `/Run` 的定性**："The run operation ignores the schedule, but uses the program file
  location, user account, and password **saved in the task** to run the task immediately."
  —— 它**启动的就是那个任务平时会启动的东西**，没有引入任何新增的破坏能力。
  会出事的是"那个任务本身要干什么"，而不是 `/Run` 这一步。
- **官方对 `/End` 的定性**："Stops **only the instances of a program started by a scheduled
  task**." —— 它只结束由该任务启动的实例，不碰别的进程。
- **规范 §2.9 的分级口径**是"不可逆地毁掉现有数据"。`/Run`/`/End` 都不属于这一类。
- 同时我**刻意不给它们加 `confirmText`**：这两条动作最正当的用途就是"我刚建了个任务，
  想立刻试一下"（官方文档自己也是这么建议的）。给一次纯粹的试跑加确认框，
  只会训练用户无脑点"确定"，反而稀释了真正危险动作（`/Delete`）的确认分量。
- 代价我如实写在这里：**用这条命令启动的任务，可能是一个会关机、会覆盖文件的任务**。
  所以两条动作的 `description` 都写明了"它启动/结束的是任务里保存的那个程序"，
  而界面上永远显示完整命令行（`showCommandLine: true`）。

---

## 10. 故意没做的部分

| 没做 | 为什么 |
|---|---|
| `/Query /XML`（导出任务定义 XML） | 会把**上百个任务的完整 XML** 一次性倒出来（本机 192 个 `\Microsoft\` 任务）。这是原始转储，不是"帮人看清"；而且 `/XML [xml_type]` 里 `xml_type=ONE` 的写法只有本机帮助有（官方页面只写 `/xml`）。要看某个任务的全貌用 `query-task` + `/V`/`/FO LIST`，XML 需要时自己在终端跑 |
| `/Query /S <computer>` 与 `/U` / `/P`（远程） | 官方前提是"必须是**远程**计算机的 Administrators 组成员，且本地与远程要在同一域/受信任域。本机不在域里，**无法实测**；不该把一个没验证过、又需要凭据的功能放进界面 |
| `/create` 的 `/V1` | "Creates a task visible to pre-Vista operating systems"，是给远古兼容用的，没有实际价值 |
| `/create` 的 `/EC <channelname>` | 官方说明它指定的是"ONEVENT 计划要匹配的事件通道"，而 ONEVENT 还需要 `/MO` 里塞 XPath 事件查询串（create 页：`ONEVENT: XPath event query string`；本机帮助示例 `SCHTASKS /Create /TN EventLog /TR wevtvwr.msc /SC ONEVENT /EC System /MO *[System/EventID=101]`）——这是个需要专门教程的场景，不是一个下拉框加一个文本框能表达好的 |
| `/create` 的 `/I <idletime>` | 只在 `ONIDLE` 计划下有效（且必需）。v1 的 `visibleWhen` 宿主尚未实现，做出来就会在 9 种计划类型里都显示一个只对 1 种有效的字段。**如果以后实现了 `visibleWhen`，这个字段应该补上** |
| `/change` 的 `/ST` `/RI` `/ET` `/DU` `/K` `/SD` `/ED` | 这些在 change 页里有明确依据（不是"发明参数"），但一次"改任务"里同时给 7 个时间字段、彼此还有互斥关系（`/ET | /DU`），表单会变成一堵墙。而且实测发现**对系统任务 `/Change` 直接 Access is denied**，日常能改的多是自己的任务，改时间用任务计划程序 GUI 更合适。这一条是**覆盖范围取舍**，不是缺依据 |
| `/ShowSid` 之外的"任务专用账号"相关操作 | 没有其它相关命令 |
| `progress.pattern` | schtasks 没有进度输出（都是"跑完给一行结果"），凭空写正则会违反 R1 |
| `workingDirectory` | schtasks 不写文件（`/Create /XML` 只**读** XML、不写），所以不需要覆盖 `workingDirectory` |

---

## 11. 冒烟测试结果（R4）

### 11.1 只读动作：逐条实跑

全部只读动作都在**真实系统**上跑过（**未做任何改动**），原始字节取回后按 UTF-8 解码核对，
退出码如下（完整结论见 §6）：

```
/Query                                                    exit=0   41 827 B
/Query /FO LIST /V                                        exit=0  437 167 B
/Query /FO CSV /NH                                        exit=0   21 641 B
/Query /FO TABLE /NH /V                                   exit=0  272 067 B
/Query /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"     exit=0      280 B
/Query /TN "NoSuchTaskXYZ"                                exit=1    stderr 52 B
/Query /TN "\No Such Folder\NoSuchTask"                   exit=1    stderr 52 B
/Query /TN "\Microsoft\Windows\Defrag\*"                  exit=1    stderr 52 B   ← 通配符确实不支持
/ShowSid /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"   exit=0      173 B
8 个 /? 帮助页                                             exit=0
```

**另外验证的三件事**：

1. **`nextSteps` 的正则对着真实输出跑过**（规范 §2.2 的硬性要求）：
   - `list-tasks` → `(?m)^(?!TaskName|Folder:|=)\S.*?\s{2,}\S`：在 41 827 B 的真实输出里命中
     （`Adobe Acrobat Update Task                N/A                    Disabled`），
     且**不会**命中表头行 `TaskName ... Next Run Time ... Status`、`Folder: \` 与 `====` 分隔行。
   - `list-tasks-detail` → `(?m)^TaskName:\s+\S`：在 437 167 B 的真实输出里命中。
   - `query-task` → 两个分支都命中。
   - 三条都用 Python `re.search` 在真实 stdout 上实测过，不是凭印象写的。
2. **`quickActions` 的三个目标 `action` id 都存在**（用脚本核对过）。
3. **手工核对 argv 展开**：写了个脚本按宿主的规则（`字段声明顺序 = argv 顺序`，
   空值不产出 token）把 12 个动作的每个字段展开成 token 打印出来核对，
   并解开 `examples` 里的 YAML 转义确认最终 token 正确
   （例如 `"C:\\Windows\\System32\\notepad.exe"` → `C:\Windows\System32\notepad.exe`）。

### 11.2 改动类动作：只做了"参数形状探针"，没有留下任何东西

**这一节必须如实说明**：为了让"字段写法是否真的被 schtasks 接受"有实测依据
（而不是纸上推演），我建了**临时的探针任务，用完立刻删除**：

| 探针任务名 | 用途 | 计划 | 收尾 |
|---|---|---|---|
| `__AllToolSmokeProbe` | 验证 `/Create`、`/Change /ENABLE`、`/Run`、`/End`、`/Delete` 的行为 | `/SC ONCE /ST 23:59` | 当场 `/Delete /F`（还顺带实测了"同名任务 + 不给 /F → 交互提问 + exit=1"） |
| `__AllTool编码探针` | 确定输出编码（中文任务名） | `/SC ONCE /ST 23:58` | 当场 `/Delete /F` |
| `__AllToolDayProbe` | 验证 `/D MON`、`/D MON,WED,FRI`、`/MO 2`、`/MO LASTDAY /M *`、`/MO FIRST /D MON` 这些写法被接受 | `/SC WEEKLY`/`DAILY`/`MONTHLY`，`/ST 23:57` | 每次创建后立刻 `/Delete /F`（共 5 轮） |

- 探针要跑的程序是 `cmd.exe /c exit 0`（**不产生输出、不写文件、不联网**）。
- 探针的计划时间都设在当晚 23:57-23:59，而全部实测在 **22:4x** 完成、任务随即被删——
  **所以除下面这一条之外，探针的计划都没到点，程序没有因计划而被启动过**。
  唯一的例外：`__AllToolSmokeProbe` 被 `/Run` **手动立即运行**过一次
  （那正是 `/Run` 的实测内容），跑的就是 `cmd.exe /c exit 0`，无害，随后即被删除。
- **没有创建/修改/删除任何用户的既有任务**。唯一一次"碰到真实任务"是
  `/End /TN "\Microsoft\Windows\Defrag\ScheduledDefrag"`（实测返回 0），
  但那个任务当时并没有在运行，所以实际上什么也没发生。
- **收尾复查**：`Get-ScheduledTask -TaskName` 对三个探针名查询都是 **0 条**；
  `schtasks /Query /TN "__AllToolSmokeProbe"` 返回 exit=1（找不到）；
  `Get-ScheduledTask | Where TaskName -like '*AllTool*'` 只剩用户自己装的
  `UninstallTool_SkipUAC_Portable_Steve`。**残留：无。**
  （根目录用户任务清单与开工前逐条一致：Adobe Acrobat Update Task / Clash Verge /
  CreateExplorerShellUnelevatedTask / NahimicTask32 / NahimicTask64 /
  NIUpdateServiceStartupTask / NVIDIA App SelfUpdate_{…} / UninstallTool_SkipUAC_Portable_Steve /
  ViGEmBus_Updater。）

**做这件事的收益**（这些事实只靠读文档得不到）：§8.1 的权限矩阵、§8.2 的
`/SC ONSTART /DELAY` 被拒、§6 的 `/Create` 缺 `/F` 时的 `WARNING … (Y/N)?` + exit=1、
`/D MON,WED,FRI` 与 `/MO LASTDAY /M *` 这些写法真的被接受。

**明确没跑的动作**：

| 动作 | 为什么没跑 |
|---|---|
| `create-from-xml` | 会创建真实任务，且需要一个 XML 定义文件（本机没有现成的任务定义导出）。**字段依据完整**（官方 `/xml` 一节），但**没有真机执行记录** |
| `create-basic` 的 `/RU` + `/RP`、`/NP`、`/Z`、`/RL HIGHEST`、`/IT` 组合 | 会创建真实任务，且带密码/权限组合的任务**有副作用**（`/RL HIGHEST`、`/Z` 都会实质改变系统行为）。只验证了 `/SC` `/MO` `/D` `/M` `/ST` 这些形状 |
| `change-task` 的 `/RU`、`/RP`、`/TR`、`/RL`、`/IT`、`/Z` | 会改动真实任务的配置。只验证了 `/ENABLE`（对自己任务 = 0，对系统任务 `/DISABLE` = 1 Access is denied） |
| `delete-task` 对**已有任务**的删除 | **绝不允许**：删掉用户的真实计划任务不可逆。只在自建探针上验证过 `/Delete /F` |

---

## 12. 这个工具的坑（给后来者）

1. **编码是 UTF-8，不是 cp936。** 中文任务名按 cp936 解会变乱码。别照抄第一批 12 个包的 `oem`。
2. **`/TN` 不接受通配符**（官方明写 + 实测确认）。想按文件夹看任务只能先 `/Query` 再看 `Folder:` 行。
3. **不给 `/F` 而任务已存在 = 交互提问 + exit=1。** 宿主没有 stdin，界面上会看到一句
   `WARNING: … Do you want to replace it (Y/N)?` 加一个错误码。这就是 `/F` 默认 `false`、
   但字段 help 里写清"填了就直接覆盖"的原因。
4. **`/Run` 的成功输出是 `Attempted to run`**，别当成"任务已经跑成功了"。
5. **`/End` 对没在跑的任务也报 SUCCESS**，别当成"杀掉了一个进程"。
6. **权限取决于任务自己的 ACL，不是命令**：`/Run` 系统任务被拒、`/End` 同一个任务却返回 0。
   所以"这个动作需要管理员吗"没有统一答案，见 §8。
7. **输出可能非常大**：`/Query /FO LIST /V` 在本机是 **437 KB**，
   `/Query /FO TABLE /NH /V` 是 272 KB。界面上要有等待预期与截断。
8. **`/SC ONCE` 必须给 `/ST`，`/SC ONIDLE` 必须给 `/I`，`/MO LASTDAY` 必须给 `/M`**——
   这些必填关系在官方页面里分散在各 schedule type 的小节里，不在 Parameters 表里。
   本清单没有用 `visibleWhen`（宿主未实现）去表达它们，而是写进了字段 help——
   **用户填错会以 exit=1 失败，不是静默错误**。
9. **`/D` 有歧义**：官方表格里 `/d` 既能是星期几（`MON`-`SUN`）也能是日期（1-31），
   取决于 `/SC` 与 `/MO` 的组合。所以我用文本框而不是枚举，help 里并列两种写法。
10. **`/create` 不校验程序和密码**（官方 Remarks 明写）：路径写错、密码写错，任务照样"创建成功"，
    只是永远不会跑。界面上看到 `SUCCESS` 不代表任务是对的。
