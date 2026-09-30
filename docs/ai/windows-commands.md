<!-- 本文档是本项目自己写的实测记录，**不是**第三方文档快照。
     docs/reference/ 下那些第三方快照（7-Zip CHM 提取、scoop wiki、uv 文档、Microsoft Learn 页面）
     在转公开仓库前会被清理，本文档放在 docs/ai/ 下不受影响。 -->

# Windows 自带命令工具包 —— 共用事实与实测记录

> 本文档是这一批 12 个工具包（`ping` / `ipconfig` / `tracert` / `nslookup` / `netstat` /
> `tasklist` / `systeminfo` / `chkdsk` / `sfc` / `robocopy` / `cleanmgr` / `powercfg`）的**共用记录**。
> 每个包自己的 `plugins/<id>/NOTES.md` 只写该包特有的内容，公共事实一律指回这里。
>
> 实测环境：Windows 11 `10.0.26200` x64（`systeminfo` 自报"Windows 11 专业工作站版"），
> 账号 `<机器名>\<用户名>`（**非管理员**）。实测日期 2026-09-27。

---

## 1. 这批工具包的共同性质

| 事实 | 说明 |
|---|---|
| 一个 exe = 一个工具包 | 规范里 `locate` 是**包级**的，所以不能把多个 exe 塞进一个包。12 个命令 = 12 个包 |
| 都在 `C:\Windows\System32` 下 | 都是 `.exe`（`Get-Command` 实测均为 `Application`）。`searchPaths` 里写了 `%SystemRoot%\System32` |
| **没有统一的 `--version` 开关** | 所以 12 个包**都不写** `versionArgs` / `versionPattern` / `minVersion`。见 §4 |
| **没有子命令** | Windows 命令是"开关 + 位置参数"模型，没有 `git status` 那样的子命令层级 |
| 输出编码是 **OEM 代码页**（`sfc` 除外） | 见 §5 |
| 帮助是 `/?`，不是 `--help` | 且**有的写 stdout、有的写 stderr、有的根本不写控制台**。见 §6 |

### 1.1 命令形态实测（`Get-Command` 与文件版本）

| 命令 | 类型 | 路径 | 文件版本 |
|---|---|---|---|
| ping | Application | `C:\Windows\system32\PING.EXE` | 10.0.26100.8115 |
| ipconfig | Application | `C:\Windows\system32\ipconfig.exe` | 10.0.26100.8521 |
| tracert | Application | `C:\Windows\system32\TRACERT.EXE` | 10.0.26100.8115 |
| nslookup | Application | `C:\Windows\system32\nslookup.exe` | 10.0.26100.8521 |
| netstat | Application | `C:\Windows\system32\NETSTAT.EXE` | 10.0.26100.8521 |
| tasklist | Application | `C:\Windows\system32\tasklist.exe` | 10.0.26100.1 |
| systeminfo | Application | `C:\Windows\system32\systeminfo.exe` | 10.0.26100.4202 |
| chkdsk | Application | `C:\Windows\system32\chkdsk.exe` | 10.0.26100.1150 |
| sfc | Application | `C:\Windows\system32\sfc.exe` | 10.0.26100.8521 |
| robocopy | Application | `C:\Windows\system32\Robocopy.exe` | 10.0.26100.8521 |
| cleanmgr | Application | `C:\Windows\system32\cleanmgr.exe` | 10.0.26100.8521 |
| powercfg | Application | `C:\Windows\system32\powercfg.exe` | 10.0.26100.8521 |

**本批不含 cmd 内建命令**（`dir` / `echo` / `copy` / `del` / `cls` / `type`）。
它们不是 exe，规范里已验证的写法是 `locate.executable: cmd.exe` + `command: /c` +
`commandArgs: [原命令]`（`docs/spec/manifest-v1.md` §2.4），但本批按任务要求先不做。

---

## 2. 文档来源与快照

| 类别 | 位置 | 是否入库 |
|---|---|---|
| 官方文档 HTML 快照 | `docs/reference/win-docs/*.html` + `_switches.json` | **入库**（是抓下来的官方页面与提取结果） |
| 本机 `/?` 帮助快照 | `docs/reference/win-help/*.txt` + `_meta.json` + `_probe.json` | **不入库**（`.gitignore`，微软文本） |

抓取脚本：

```powershell
pwsh -File scripts/fetch-win-help.ps1        # 12 个命令的 /? 快照 + _meta.json
pwsh -File scripts/probe-win-output.ps1      # 真实输出（不是 /?）的编码与退出码实测 → _probe.json
pwsh -File scripts/extract-win-docs-switches.ps1  # 从官方 HTML 提取开关表与小节锚点 → _switches.json
```

**官方文档 URL 的形态（这一步有坑，见 §6.5）**：11 个命令的参考页在
`https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/<命令>`；
**唯独 `powercfg` 不在那里**（那个路径 404），它在
`https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options`。

### 2.1 为什么 `/?` 快照不入库

`.gitignore` 里 `docs/reference/win-help/` 下面写了三条理由，摘录：

1. **版权**：这些是微软的帮助文本，本项目即将转为公开仓库，不能随仓库发布。
2. **可再生**：任何一台 Windows 机器上跑 `scripts/fetch-win-help.ps1` 就能重新抓到。
3. **代价**：校验器的第 5 层"开关溯源"在干净克隆里看不到这批语料 —— 但那一层本来也只认
   `-` 开头的开关，对 Windows 命令的 `/` 开关无效（见 §7.2），所以实际损失为零。

---

## 3. 覆盖范围总表

| 工具包 | 动作 | 字段 | 左侧分组 | 需要管理员（官方依据） | 需要管理员（实测） | danger |
|---|---|---|---|---|---|---|
| ping | 3 | 13 | 连通性测试 | — | — | — |
| ipconfig | 11 | 12 | 地址信息 / DNS / DHCP 类 ID | — | — | `/flushdns` `/registerdns` `/release` `/renew` `/release6` `/renew6` `/setclassid` = overwrite |
| tracert | 4 | 13 | 路由跟踪 | — | — | — |
| nslookup | 3 | 11 | 解析 | — | — | — |
| netstat | 8 | 19 | 连接列表 / 进程归属 / 路由与统计 | `-b`（文档写"权限不足会失败"） | — | — |
| tasklist | 5 | 11 | 进程列表 / 服务映射 / 筛选 | — | — | — |
| systeminfo | 3 | 6 | 系统信息 | — | — | — |
| chkdsk | 5 | 11 | 只读检查 / 修复 / 联机扫描 | 全部（文档写"必须是管理员"） | **`chkdsk` 只读也失败（exit 3）** | `/f` `/r` = overwrite，`/x` = destructive |
| sfc | 3 | 4 | 校验 / 修复 / 单文件 | 全部（实测全部提示提权） | **四个动作都失败（exit 1）** | `/scannow` = overwrite |
| robocopy | 4 | 20 | 预览 / 复制 / 同步 | — | — | `/MIR` = destructive，`/E` 等 = overwrite |
| cleanmgr | 7 | 7 | 交互设置 / 执行清理 / 自动清理 / 预览 | — | — | 5 个清理动作 = destructive |
| powercfg | 14 | 23 | 方案查询 / 睡眠与唤醒 / 报告 / 修改设置 | `/systempowerreport`、`/systemsleepdiagnostics` | **`/waketimers`、`/requests`（exit 1）** | 6 个改设置动作 = overwrite |

**注意 `category`**：所有包都只写**动作级** `category`，**刻意不写工具包级 `category`** ——
按产品负责人的明确要求，清单只定义"动作"的分组，工具包自身归到哪个组由用户自己决定。

**本批没有任何 `progress.pattern`**：这些命令的进度都不是百分比（`robocopy` 那种逐文件百分比
在重定向时格式不稳定），凭空写正则会违反 R1。

---

## 4. 版本号：为什么 12 个包都不写 `versionPattern`

Windows 自带命令**没有统一的版本开关**：没有 `--version`，`/?` 是帮助而不是版本输出。
在"不发明参数"（R1）的前提下，可选项只有两个，都被否掉：

| 方案 | 为什么不做 |
|---|---|
| `versionArgs: []` + 猜版本正则 | 无参数运行这些命令**有副作用**：`robocopy` 会报用法并返回 16、`chkdsk` 会去检查当前卷、`ipconfig` 会列出全部配置。这不是"取版本"，是"跑命令" |
| 另找一个开关当版本输出 | 没有任何一个自带命令提供这种开关 |
| 读 exe 的文件版本 | 规范里 `locate` 没有这个能力（见 §7.3） |

**后果（已知且接受）**：宿主 `ToolLocator.LocateAsync` 在 `versionPattern` 为空时会返回
`Problem = "未能解析出版本号"`，界面执行时会在输出区打一句 `# 注意：未能解析出版本号`。
这是清单的诚实代价，不是清单写错了。`minVersion` 同样不写 —— 没有版本号就无从比较。

---

## 5. 输出编码：实测原始字节

用 `scripts/probe-win-output.ps1`（走 `.NET Process` + `BaseStream`，拿**原始字节**）实测：

| 命令 | 输出编码 | 证据 |
|---|---|---|
| ping | **cp936（OEM 代码页）** | `/?` 头 4 字节 `0d 0a d3 c3`，`d3 c3` = cp936 的「用」，UTF-8 下非法 |
| ipconfig | **cp936** | 同上（2296 B 帮助、2053/5263 B 实际输出） |
| tracert | **cp936** | 553 B 帮助 |
| nslookup | **cp936** | 帮助走 stderr，274 B，同编码 |
| netstat | **cp936** | 帮助走 stderr，2074 B |
| tasklist | **cp936** | 2690 B 帮助；`/v` 实际输出 58 KB |
| systeminfo | **cp936** | 3623 B |
| chkdsk | **cp936** | 2089 B 帮助 |
| **sfc** | **UTF-16LE** ⚠️ | 68 B 原始字节 `0d 00 0d 00 0a 00 3a 4e 86 4e …`，按 UTF-16LE 解 = `为了使用 sfc 工具，你必须作为管理员运行控制台会话。` |
| robocopy | **cp936** | 8524 B 帮助 |
| cleanmgr | **无控制台输出** | stdout/stderr 都是 0 字节（它是 GUI 程序，见 §6.4） |
| powercfg | **cp936** | 2494 B 帮助，stderr 也是 cp936 |

结论：11 个包写 `runtime.encoding: oem`（= 控制台输出代码页，本机 936），
`sfc` 写 `utf-16le`。**写 `oem` 而不是写死 `gbk` 的理由**：换成英文 Windows（437）或
日文 Windows（932）时同一份清单仍然正确。

### 5.1 判断 UTF-16LE 的坑（我自己踩了）

第一版判定逻辑是"ASCII 字符后面跟不跟 `00`"，结果把 `sfc` 的输出误判成 cp936。
原因是 UTF-16LE 里的汉字对（如 `3a 4e` =「为」）第二个字节不是 0，命中不了那条规则。
正确做法是**按字节长度是否为偶数 + 高字节 0 的比例**判断（阈值 30%）。
这个教训同时改进了 `fetch-win-help.ps1` 与 `probe-win-output.ps1`。

---

### 5.2 后续批次补测（第二批 / 第三批）

> 下面这些是后续包**各自实测**的结果（出处见各包 `NOTES.md`）。
> 加这一节的原因：同一条命令族里出现了三种不同答案，
> **照抄"Windows 命令都是 oem"会直接踩坑**。

| 命令 | 编码 | 判定依据（原始字节） |
|---|---|---|
| `arp` | **oem（cp936）** | 本机默认输出全是 ASCII（判不出），故构造中文样本 `arp -a -N 测试`：stderr 尾部 `b2 e2 ca d4` = cp936「测试」，严格 UTF-8 解码抛异常 |
| `getmac` | **utf-8** | `/v /fo list` 的 `Connection Name: 蓝牙网络连接 2`，字节 `e8 93 9d e7 89 99 …`；整份 1122 字节严格 UTF-8 通过，按 cp936 解成「钃濈墮缃戠粶杩炴帴」 |
| `whoami` | **utf-8** | `/groups` 中文组名字节 `e6 9c ac e5 9c b0 …`（`e6 9c ac` = 「本」的 UTF-8；GBK 下是 `b1 be`）；3289 字节严格 UTF-8 通过 |
| `netsh` | **utf-8** | `interface ipv4 show config` 中文接口名 `e4 bb a5 e5 a4 aa e7 bd 91` = 「以太网」；按 cp936 是乱码 |
| `fsutil` | **oem（cp936）** | 提权下 `volume diskfree Z:` 首字节 `b4 ed` = cp936「错」；严格 UTF-8 解码失败 |
| `dism` | **oem（cp936）** | 提权下 `/?`、`/Get-Packages` 等全部严格 UTF-8 解码失败、按 cp936 解出「部署映像服务和管理工具」 |
| `sqlite3` | **oem（cp936）** | 含中文表名的 `.tables` 输出 `d1 a7 c9 fa` = cp936「学生」；严格 UTF-8 失败 |
| `taskkill` | **oem（推定）** | 输出全 ASCII、**无中文样本可判** → 按同族惯例推定，已在包 NOTES 里声明"不是实测" |
| `hostname` · `ver` | **n/a（无中文样本）** | 输出全 ASCII（主机名/版本号不可能含中文）→ 判不出编码，两包按"与代码页无关更安全"写 utf-8 并在 NOTES 声明依据 |

**结论**：同一台机器、同一批 Windows 命令里，`oem`、`utf-8`、`utf-16le` 三种都存在 ✔
（`sfc` 是 utf-16le，见 §5 上文）。**新做包时必须自己量，不能按命令族推断。**

## 6. 这批 Windows 命令的"怪脾气"（对后来者最有用的一节）

### 6.1 退出码几乎都不是"0 = 成功"

| 命令 | 实测退出码 | 说明 |
|---|---|---|
| `ping 127.0.0.1 -n 2` | **0** | 收到回复 |
| `ping` 目标不可达 | 1 | **0 与 1 的含义与直觉相反** |
| 所有命令的 `/?` | ping 0 / ipconfig **1** / tracert **1** / nslookup **1** / netstat **1** / tasklist 0 / systeminfo 0 / chkdsk **3** / robocopy **16** / powercfg 0 | **帮助输出的退出码各家不同**，有的是 0，有的是错误码 |
| `chkdsk`（只读，非管理员） | **3** | 文档：3 = 无法检查磁盘 / 错误无法修复 |
| `sfc /verifyonly`（非管理员） | **1** | 只打印提权提示 |
| `robocopy /L`（只列出） | **1** | **1 表示"有文件被成功复制"**，不是失败！0 = 无需复制 |
| `robocopy` 到不存在的源 | **16** | 严重错误 |
| `powercfg /waketimers`、`/requests`（非管理员） | **1** | stderr 提示需要管理员 |
| `netstat`、`tasklist`、`systeminfo`、`powercfg /list` 等 | 0 | 正常 |

**最要命的一条是 `robocopy`**：官方文档 `#exit-return-codes` 一节明确写
"Any value equal to or greater than 8 indicates that there was at least one failure"，
也就是说 **0–7 全部是"没有失败"**（1 = 全部复制成功、2 = 目标端有多余文件、3 = 部分复制且有多余文件…），
**只有 ≥ 8 才是真失败**。清单的 `exitCodes` 就是照这个语义写的。

### 6.2 帮助写的流不一样：有的 stdout，有的 stderr

| 写 stdout | ping、ipconfig、tracert、tasklist、systeminfo、chkdsk、sfc、robocopy、powercfg |
| 写 **stderr** | **nslookup**（stdout 0 B / stderr 274 B）、**netstat**（stdout 0 B / stderr 2074 B） |
| 都不写 | **cleanmgr**（GUI 对话框） |

对宿主的含义：只看 stdout 会漏掉 `nslookup` 与 `netstat` 的帮助。宿主是同时合并两个流的，
所以界面上看得到；但**写自动化脚本时必须两个流都读**。

### 6.3 `sfc` 完全不接受 `/?`，而且输出是 UTF-16LE

- 实测 `sfc /?` 不打印任何参数说明，只打印一句
  `为了使用 sfc 工具，你必须作为管理员运行控制台会话。`（68 字节，**UTF-16LE**，退出码 1）。
- 也就是说 **`sfc` 是"本机 `/?` 拿不到参数表、只能靠官方文档"的正面案例**，
  与任务要求的"用本机 `/?` 核对开关"在这一条上冲突 —— 已在
  `plugins/sfc/NOTES.md` §7 如实写明"没能用 `/?` 交叉验证"。
- 顺带：`sfc` 的**所有**输出（含权限错误）都是 UTF-16LE。

### 6.4 `cleanmgr` 是 GUI 程序，`/?` 会弹出对话框并永久等待

- 实测 `cleanmgr /?`：**stdout 0 字节、stderr 0 字节**，进程**不退出**。
- 我的抓取脚本第一版**没有超时保护，脚本挂了 300 秒**（被 harness 挪进后台才被发现）。
- 修法：给每个命令加超时（默认 8 秒），超时后 `Process.Kill(true)`，
  并在杀之前把 `MainWindowTitle` 抓下来当证据 —— 实测标题是 **`USAGE`**，
  说明它开的是一个 Windows 对话框（十六进制里的"USAGE"字符串）。
- 对宿主的含义：cleanmgr 的任何动作在控制台里**什么也看不到**，界面只能靠
  "命令已结束 + 退出码"判断。已写进清单的 `resultNote`。
- **`gz`/`chkdsk` 之类没有这个问题**；具有同样性质的是 `diskpart`（本批不做）。

### 6.5 官方文档 URL 不在标准目录下的那个：`powercfg`

- `.../windows-server/administration/windows-commands/powercfg` → **HTTP 404**
- 正确位置：`.../windows-hardware/design/device-experiences/powercfg-command-line-options` → HTTP 200
- 该页有 34 个 `h3` 锚点（每个子命令一个），例如 `#-query-or-q-`、`#hibernate-or-h`、`#batteryreport`。
  注意锚点 id 里**保留了对开关字母的转义**（`#-query-or-q-`），这是 GitHub 风格的自动锚点。

### 6.6 `netstat /?` 里有个疑似笔误，且本机帮助比官方文档多 6 个开关

- 本机 `/?` 里 `-d` **出现了两次**，含义不同（"显示每个连接分配的 DSCP 值" 与
  "显示以太网统计信息"）。官方文档的语法行里**没有 `-d`**。
- 本机 `/?` 比官方语法行多了 `-c` `-d` `-f` `-i` `-t` `-x` `-y`。
- 处理方式：**本清单只收录官方文档语法行里有的开关**，多出来的一律不做 ——
  这样每个开关都有唯一可指的官方出处（R1）。多出来的清单记在各包 NOTES 的"故意没做"一节。

### 6.7 `tasklist /?` 多一个 `/APPS`，官方页面没有

同理不收（来源冲突时以官方页面为准）。另外 `tasklist /?` 的退出码是 **0**，
而 `ipconfig` / `tracert` / `netstat` / `nslookup` 的 `/?` 都是 1。

### 6.8 拿不到管理员权限时的真实症状（当前账号不是管理员）

| 命令 | 症状 |
|---|---|
| `chkdsk`（**连只读都不行**） | stdout：`访问被拒绝，因为你没有足够的权限，或该磁盘可能被另一个进程锁定。你必须调用这一在提升模式下运行的实用工具…`，退出码 **3** |
| `sfc /verifyonly`、`sfc /verifyfile=…` | stdout（UTF-16LE）：`为了使用 sfc 工具，你必须作为管理员运行控制台会话。`，退出码 **1** |
| `powercfg /waketimers`、`/requests` | stderr：`此命令需要管理员权限，并且必须从提升的命令提示符中执行。`（58 字节），退出码 **1** |

这三条是**实测**的，不是照文档抄的；清单里的 `requiresAdmin: true` 就标在这几处。
注意 `powercfg` 官方文档**没有**写 `/waketimers` 与 `/requests` 需要提权 —— 这是本机实测的额外发现。

### 6.9 `systeminfo` 很慢

实测本机 `systeminfo` **4.5 秒**、`systeminfo /fo csv` **4.7 秒**（要查 WMI、已安装更新等）。
`ipconfig /displaydns` 输出 **1.6 MB**（DNS 缓存条目多）。做界面时要有等待预期。

### 6.10 其它有用的数字（都是实测）

| 命令 | 输出大小 | 备注 |
|---|---|---|
| `tasklist` | 19.4 KB | |
| `tasklist /svc` | 20 KB | |
| `tasklist /v` | **58 KB** | 最详细，列数最多 |
| `tasklist /fo csv /nh` | 12.5 KB | |
| `netstat -an` | 19.5 KB | |
| `netstat -rn` | 4.6 KB | 含 `接口列表` 段，与路由表之间隔了 `====` 行 |
| `powercfg /query` | 9.9 KB | 每层 GUID 都缩进显示，是拿 GUID 的地方 |
| `powercfg /aliases` | 1.7 KB | |
| `ipconfig` | 2.1 KB | 本机装有第三方网络代理类软件，它会创建一个**虚拟适配器**（不是 Windows 自带组件），所以 `ipconfig /all` 的输出比干净系统多一块——这是环境事实，不是 ipconfig 的行为 |

---

## 7. 规范缺口与宿主问题（不改规范、不改宿主，只记录）

### 7.1 【阻塞宿主加载】`command: ""` 被运行时校验拒绝

这是本批唯一让 `dotnet test` 变红的问题，细节写在各包 NOTES 的第 10 节，
这里给出完整现象与复现：

**现象**：

```
$ dotnet test src\AllTool.slnx
失败 AllTool.Core.Tests.RealManifestTests.加载全部工具包都不应抛异常 [12 ms]
错误消息:
 AllTool.Core.Manifest.ManifestException : 清单校验失败 <仓库目录>\plugins\chkdsk\manifest.yaml：
- 动作 check：缺少 command
- 动作 fix：缺少 command
- 动作 recover：缺少 command
- 动作 force-dismount：缺少 command
- 动作 scan：缺少 command
失败!  - 失败: 6，通过: 136，已跳过: 0，总计: 142
```

**根因（校验与执行层自相矛盾）**：

- `src/AllTool.Core/Manifest/ManifestValidation.cs:78`
  ```csharp
  if (string.IsNullOrWhiteSpace(action.Command))
  {
      errors.Add($"{where}：缺少 command");
  }
  ```
- `src/AllTool.Core/Execution/ArgvBuilder.cs:28`
  ```csharp
  if (!string.IsNullOrEmpty(action.Command))   // ← 执行层本来就支持空命令
  {
      argv.Add(action.Command);
  }
  ```
- JSON Schema 也没要求 `command`（它不在 `action.required` 里，类型是 `string`）。

三处结论不一致：**schema 允许、执行层支持、只有运行时校验拒绝**。

**为什么清单要用 `command: ""`**：Windows 命令没有子命令。`ping 8.8.8.8`、`ipconfig`、
`netstat -an` 这些调用里**根本没有"命令"这一段**，给 `command` 填什么都是编造的参数（违反 R1）。
唯一如实的表达就是空。**`ping` / `ipconfig` / `tracert` / `tasklist` / `systeminfo` / `robocopy`
都受这条影响**（`chkdsk` / `sfc` / `cleanmgr` / `powercfg` 的部分动作也受影响）。

**建议改法（一行）**，任选其一：

1. `ManifestValidation.cs:78` 改成 `if (action.Command is null)` —— 只在"作者根本没写这个键"时报错；
2. 或删掉这段校验，让 `ArgvBuilder` 的容错生效；
3. 或在 schema 里显式区分「没有子命令」（`command: ""`）与「作者忘了写」（键缺失）。

**给后来者的提醒**：如果先修宿主再跑 `dotnet test`，`RealManifestTests.LoadAll` 会遍历
`plugins/` 下**全部**工具包，一个不合规的包会连累其它 5 个测试一起红（`NextStepRealManifestTests`
里有 5 个测试也调 `LoadAll`）。修好这一行之后这 6 个测试应当全绿。

### 7.2 校验器只认 `-` 开头的开关，Windows 命令的 `/` 开关完全没被检查

`scripts/validate-plugins.py` 的 `field_switches()` 里：

```python
if style in ("attached", "separate", "flag", "repeated"):
    prefix = field.get("prefix") or ""
    return [normalize_base(prefix)] if prefix.startswith("-") else []
```

`/all`、`/svc`、`/fo` 这些前缀以 `/` 开头，`startswith("-")` 为假 → 返回空列表 →
第 5 层"开关溯源"对本批 12 个包**一个开关都没检查**。校验输出里那句
"检查 195 个开关，195 个能在参考文档快照里找到"**不含 Windows 命令的任何开关**。

建议：把判定放宽成 `prefix.startswith(("-", "/"))`，并把
`normalize_base` 里的前导字符处理一并兼容 `/`。

### 7.3 `locate` 没有"从可执行文件本身取版本"的能力

Windows 自带命令的版本在 exe 的文件版本资源里（例如 `ping.exe` 是 `10.0.26100.8115`），
但规范只提供"跑一个命令 + 正则匹配输出"这一条路。于是 12 个包都只能放弃版本号（见 §4）。
建议：给 `locate` 加一个 `versionSource: fileVersion`（或 `versionFromFileVersion: true`），
宿主用 `FileVersionInfo` 读，这样系统自带命令这类"没有版本开关"的工具包也能有版本信息。

### 7.4 规范表达不了"开关与取值必须在同一个 argv token 里"

涉及两处：

| 命令 | 官方语法 | 为什么 v1 表达不了 |
|---|---|---|
| `sfc` | `/verifyfile=<file>` | 要求 `/verifyfile=` 与路径**粘成一个 token**。`separator` 只允许 `""` / `" "` / `"="`，且它作用在 prefix 与值之间，不支持把两个字面量拼起来 |
| `cleanmgr` | `/sageset:n`、`/sagerun:n`、`/TUNEUP:n` | 同理：开关与编号必须在同一个 token 里，而编号是用户输入的数字 |

绕法（本批采用）：把整个 token 写成 `literal` 的 `args`，编号做成枚举（`1`/`2`/`3`/`4`/`5`/`10`）。
代价是取值范围被枚举死。建议：给 `field` 加 `prefix + 值` 的"拼接"语义，
或者加一个 `attachedWith` 之类的显式声明。

### 7.5 `requiresAdmin` 已实现（这条是好消息）

`D13`（要不要实现 `requiresAdmin`）实际**已经落地**：

- `src/AllTool.Core/Manifest/ManifestModel.cs:123` → `ManifestAction.RequiresAdmin`（`bool?`）
- `ManifestModel.cs:126` → `RequiresAdminEffective(packageDefault)`（动作级覆盖工具包级）
- `src/AllTool.App/MainWindow.xaml.cs:1147` → 选中动作时在描述里加"⚠ 这个动作通常需要管理员权限…"，
  已是管理员则显示"✔ 已是管理员"
- `src/AllTool.App/MainWindow.xaml.cs:1996` → 执行前如果非管理员，在输出区加一行提示
- `src/AllTool.Core.Tests/RequiresAdminTests.cs` → 4 个测试覆盖动作级/包级组合

**但规范文档说它"尚未实现"**：`docs/spec/manifest-v1.md` §2.4 的表格里写着
"`requiresAdmin`｜**宿主尚未实现**（既不提示也不拦截）"。**这一条已经过期，应当更新**。
（本批没有改 `docs/spec/**` —— 那是禁改范围，所以记在这里。）

---

## 8. 冒烟测试

脚本：`scripts/smoke-win-cmds.ps1`。设计要点（照 playbook §5.1）：

1. **只读白名单**：整条 argv 必须与白名单里某一项**逐字**相同才允许执行。
2. **判定不用 `-eq` 比数组**：两边都用 `NUL` 连接后比字符串
   （PowerShell 里 `-eq` 用在数组上是**过滤**语义，uv 那批真的因此放跑过一条写操作）。
3. **红线自检**：30 条"绝不允许被判为可跑"的命令（`chkdsk /f`、`sfc /scannow`、
   `cleanmgr`、`robocopy /MIR`、`powercfg /hibernate off`、`shutdown /s` …），
   **任何一条被判为可跑就 `throw` 中止**，而不是"跑着看"。
4. **执行前二次确认**：每条在真正启动前会用**同一套判定**再确认一次，不在白名单里就中止。

实测结果（28 条只读命令，全部 exit 与预期一致）：

| 命令 | 退出码 | 命令 | 退出码 |
|---|---|---|---|
| `ping 127.0.0.1 -n 2` | 0 | `tasklist` | 0 |
| `ping -n 2 localhost` | 0 | `tasklist /svc` | 0 |
| `ipconfig` | 0 | `tasklist /fo csv /nh` | 0 |
| `ipconfig /all` | 0 | `systeminfo` | 0 |
| `tracert -h 1 127.0.0.1` | 0 | `systeminfo /fo csv` | 0 |
| `tracert -d -h 1 127.0.0.1` | 0 | `chkdsk`（只读） | **3** |
| `nslookup localhost` | 0 | `sfc /verifyonly` | **1** |
| `nslookup -type=AAAA` | 0 | `robocopy <src> <dst> /L` | **1** |
| `netstat -an` | 0 | `powercfg /list` | 0 |
| `netstat -rn` | 0 | `powercfg /getactivescheme` | 0 |
| `netstat -s` | 0 | `powercfg /query` | 0 |
| `netstat -e` | 0 | `powercfg /a` | 0 |
| | | `powercfg /aliases` | 0 |
| | | `powercfg /lastwake` | 0 |
| | | `powercfg /waketimers` | **1**（需管理员） |
| | | `powercfg /requests` | **1**（需管理员） |

**没跑的动作与原因**见各包 NOTES 的第 9 节。汇总一句：
一切会改系统（`chkdsk /f` `/r`、`sfc /scannow`、`cleanmgr` 的清理、`robocopy /MIR`、
`powercfg` 的改设置）或需要管理员（实测当前账号全部失败）的动作，**清单照写、字段照定义，
但不实跑**。

### 8.1 顺带验证了 `attached` + `separator: "="` 能生成单个 token

`nslookup` 的 `-type=A` 是"一个参数里带等号"。用 `separator: "="` + `literal` 风格会生成
**三个** token（`-type`、`=`、`A`）；改成 `style: attached` 才生成单个 `-type=A`。
实测三条长开关全部可用（`-type=AAAA example.com`、`-type=MX example.com`、
`-timeout=5 -retry=2 example.com`，均 exit 0）。

---

## 9. 给后来者：再补 Windows 命令时的操作顺序

```
1. Get-Command <cmd>                                     # 确认是 Application（exe）
2. 跑 scripts/fetch-win-help.ps1 -Commands <cmd>          # 抓 /? 快照（有超时保护）
3. 跑 scripts/probe-win-output.ps1                        # 看真实输出的编码与退出码
4. 抓官方文档（Invoke-WebRequest，web_fetch 在本机不可用）
   路径优先试 .../windows-server/administration/windows-commands/<cmd>
   404 就去搜（powercfg 就是一个反例）
5. 跑 scripts/extract-win-docs-switches.ps1               # 从 HTML 提取开关表与锚点
6. 对照写清单：只写官方文档里有的开关；冲突时以官方为准
7. 校验：uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
8. 冒烟：把新命令加进 scripts/smoke-win-cmds.ps1 的只读白名单与红线清单
9. dotnet test src\AllTool.slnx（注意 §7.1 的阻塞项）
```

**先做这 12 个的理由**：它们是 Windows 上"用户最可能想点一下"的诊断与维护命令 ——
网络不通查 `ping`/`ipconfig`/`tracert`/`nslookup`/`netstat`，机器慢查 `tasklist`/`systeminfo`，
磁盘有问题查 `chkdsk`/`sfc`，备份用 `robocopy`，清盘用 `cleanmgr`，电源用 `powercfg`。
它们覆盖了本次任务要求的全部命令，且都是**零安装依赖**（系统自带），
是"工具包能覆盖到什么程度"的最佳试金石。

**建议下一批**（本次未做，理由见下）：

| 建议 | 为什么值得做 |
|---|---|
| `diskpart` | 交互式，v1 无法驱动（本批按任务要求不做） |
| `netsh` | 网络配置面极大，按"界面子命令 + 再分子命令"拆分需要先解决 §7.4 |
| `schtasks` | 计划任务，只读查询部分（`/query`）很容易做，且很常用 |
| `wevtutil` | 事件日志查询，只读部分价值高 |
| `wmic` / `Get-CimInstance` | `wmic` 已弃用；PowerShell 的 CIM 是替代路线，但它属于"命令 + 脚本"，需要先想清楚边界 |
| `shutdown` | 会改系统状态（关机/重启），**建议永不做**或只做 `/a`（取消关机） |
| `curl` / `tar` | Windows 10 1803+ 自带，跨平台工具，与 7zip 同类 |
| cmd 内建命令（`dir`/`echo`/`copy`…） | 写法已验证（`cmd.exe` + `/c`），但输出编码与错误码规律更杂，值得单独一批 |

---

---

## 10. 清单作者视角：做这一批工具包时的特殊性

> 这一节原先是工具包开发手册的「附 C」，按"内容归位"的原则搬到这里——
> 它讲的是**这批命令本身**的特殊性，与 `windows-commands.md` 其它章节是一家人。

| 现象 | 怎么做 |
|---|---|
| **开关写成 `/xxx`**（官方文档用斜杠，程序也接受 `-xxx`） | 按官方文档写 `/xxx`；校验器的开关溯源已同时认 `-` 与 `/` |
| **退出码几乎都不是"0 = 成功"** | 逐个实测并写进 `exitCodes`。`robocopy` 最要命：**0–7 全是"没有失败"**（1 = 全部复制成功），≥8 才算失败；`ping` 是 0 = 通了、1 = 全丢；各命令 `/?` 的退出码还互不相同（chkdsk 3、robocopy 16、ipconfig 1…） |
| **帮助写到 stderr** | `nslookup` / `netstat` 的帮助在 stderr（stdout 0 字节）。抓快照时必须两个流都收，否则快照几乎是空的（实测 nslookup 快照只有 481 字节） |
| **输出编码不统一** | 多数是 `oem`(936)，但 `sfc` 是 **UTF-16LE**。判 UTF-16LE 不能用"ASCII 后跟 00"（汉字第二字节不为 0 会误判），要用"字节长度偶数 + 高字节为 0 的比例 > 30%"；也不要指望 `UTF8Encoding(throwOnInvalidBytes)` 能识别 GBK（它对 `d3 c3` 静默替换而不是抛错） |
| **有的会弹 GUI** | `cleanmgr`（`/?` 就弹）、`diskpart` 是交互式 → 不做，或只做"打开它"；抓帮助要带超时 |
| **很多动作需要管理员** | 标 `requiresAdmin: true`（动作级）。当前账号不是管理员时，实测 `chkdsk` 全部、`sfc` 全部、`netstat -b`、`powercfg /waketimers` 都会失败——这些不要实跑 |
| **没有版本命令** | 读 exe 的 `VersionInfo.FileVersion`（见 §5.2） |
| **官方文档 URL 不能猜规律** | `powercfg` 在 `.../windows-server/administration/windows-commands/powercfg` 是 404，真实位置在 `.../windows-hardware/design/device-experiences/powercfg-command-line-options`。**每个 URL 都要实际请求确认** |
| **cmd 内建命令不是 exe** | `dir` / `echo` / `copy` / `del` / `cls` / `type` 必须走 `locate.executable: cmd.exe` + `command: /c` + `commandArgs: [原命令]`（不要在清单里指望 `useShell`——宿主尚未实现它） |

## 相关文档

| 文档 | 讲什么 |
|---|---|
| ``AGENTS.md`` | 硬规则 R1–R7、接手顺序、命令速查、文档地图（**入口**） |
| [`docs/spec/manifest-v1.md`](../spec/manifest-v1.md) + ``schema.json`` | 清单规范（字段、风格、会话型、三级风险、kind） |
| [`docs/ai/playbook-tool-package.md`](playbook-tool-package.md) | 做工具包的逐步流程 + **§10 坑清单 + §11 检查单** |
| [`docs/ai/windows-commands.md`](windows-commands.md) | Windows 自带命令的公共实测结论 |
| [`docs/reference/README.md`](../reference/README.md) | 我们提取的事实性数据放哪、第三方原文去哪 |
| `plugins/<id>/NOTES.md` | 该包自己的实测记录（验了什么 / 没验什么） |
