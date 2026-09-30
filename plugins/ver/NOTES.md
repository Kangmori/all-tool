# ver 工具包 —— 实测记录

> 本包是 **Windows 自带命令工具包**，而且是这批里唯一一个**必须经由 `cmd.exe` 调用**的
> （`ver` 是 cmd 内建命令）。公共事实见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)；本文只写 ver 特有的部分。
> 本包同时也是全仓库**第一个走 cmd 内建路线**的包，所以 §2 写得比较细（表达方式的取舍）。

---

## 0. 一句话结论

**没有 `ver.exe`。** `ver` 是 `cmd.exe` 的内建命令，唯一诚实的表达是
`locate.executable: cmd.exe` + `command: ""` + `commandArgs: ["/c", "ver"]`，
组装出的 argv = `[cmd.exe, /c, ver]`，实测退出码 0、stdout 47 字节。
本包 2 个动作 / 0 个字段 —— 因为 `ver` 没有任何开关，一个字段都不该有。

---

## 1. 实测环境（环境事实，不是知识）

| 项 | 值 | 怎么取的 |
|---|---|---|
| 系统 | Windows 11，内部版本 `10.0.26200.8655`，DisplayVersion `25H2` | `cmd /c ver` + 注册表 |
| 账号 | **非管理员** | `WindowsPrincipal.IsInRole(Administrator)` = False |
| 控制台代码页 | **936** | `chcp.com` |
| cmd.exe | `C:\Windows\System32\cmd.exe`，文件版本 `10.0.26100.8875 (WinBuild.160101.0800)` | `Get-Command` / `VersionInfo.FileVersion` |
| 实测日期 | 2026-10-01 | — |

### 1.1 "没有 ver.exe" 的三条证据

```
PS> Get-Command ver          → 找不到（不是 Application、连 Alias 都没有）
PS> where.exe ver            → INFO: Could not find files for the given pattern(s).   exit=1
PS> Get-Command ver.exe      → 找不到
```

（对照：`Get-Command winver.exe` → `C:\Windows\system32\winver.exe`，说明"关于 Windows"
的 GUI 程序是真实存在的 exe，而 `ver` 不是。）
**注意 PowerShell 里的坑**：在 PowerShell 里直接敲 `ver` 不会报"找不到"，
而是命中 PowerShell 自己的 `Invoke-History` 别名（把 `ver` 当成"重跑第 N 条历史命令"）
—— 这也是官方文档专门写一句 "This command is supported in the Windows Command prompt (Cmd.exe),
but not in any version of PowerShell" 的原因。**这正好说明为什么必须走 `cmd /c`。**

---

## 2. `ver` 走 cmd 的写法：三种候选与最终选择（本任务的重点）

### 2.1 候选与实测

| # | 表达方式 | 组装出的 argv | 实测结果 | 结论 |
|---|---|---|---|---|
| A | `command: ""` + `commandArgs: ["/c", "ver"]`<br>（**本包采用**） | `[cmd.exe, /c, ver]` | exit **0**，stdout 47 B `Microsoft Windows [Version 10.0.26200.8655]` | ✔ 采用 |
| B | `command: "/c"` + `commandArgs: ["ver"]` | `[cmd.exe, /c, ver]` **完全相同** | exit 0，同 A | 放弃：见 §2.2 |
| C | `fixedArgs: ["/c", "ver"]`（`command: ""`） | `[cmd.exe, /c, ver]` **完全相同** | exit 0，同 A | 放弃：`fixedArgs` 是"用户永远不该改的字面参数"，而这里是**这个动作的全部内容**，藏进 fixedArgs 与 R6"把命令摆明白"的精神相悖 |

三种写法的 argv **逐字节相同**，所以"能不能跑通"区分不了它们 —— 区分它们的是**语义清晰度**。

### 2.2 为什么最终选 A（`command: ""` + `commandArgs: ["/c", "ver"]`）

规范里 `command` 的语义是"**要传给程序的首个非开关参数**，例如 7z 的 `x`"
（`manifest-v1.schema.json` 里 `command` 的 description、规范 §2.5 ①）。
用它来装 `/c` 有两个问题：

1. **`/c` 是 cmd 的开关，不是"子命令"**。官方语法行是
   `cmd [/c|/k] [/s] [/q] … [<string>]`，`/c` 与 `ver` 是**两种不同角色**，
   但**都不该独占 `command`**：`command` 这一格是留给"像 `7z a` 那样的子命令"的，
   cmd 根本没有子命令。
2. **规范 §2.5 ① 已经为这类程序定好了答案**：`ping 8.8.8.8`、`ipconfig` 这类
   "没有命令那一段"的调用，正确写法就是 `command: ""`。cmd 属于同一类
   （它只有"开关 + 一段字符串"），所以 `command: ""` 是一致的写法。

于是 `/c` 与 `ver` 一起进 `commandArgs`（schema 对它的定义正是"紧跟在 command 之后的固定参数"），
组装结果与候选 B 完全一样，但**读清单的人一眼就知道"这个动作整体就是 `cmd /c ver`"**，
不会误以为 cmd 有个叫 `/c` 的子命令。

**顺带一个"别这么写"的记录**：候选 D（`command: "/c ver"` —— 把两个 token 塞进一个 `command`）
**没试**。因为 `ArgvBuilder.Build` 是把 `action.Command` 整体当一个 argv 元素加进去的
（`argv.Add(action.Command)`），`"/c ver"` 会变成**一个**带空格的 argv 元素，
cmd 收到的是 `"/c ver"` 这一整段、拿不到 `ver` 这个命令 —— 这是"看起来对、其实错"的经典写法。

### 2.3 `/c` 之后那一段就是"要跑的命令"（官方依据）

- **cmd 官方页**（<https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cmd>，
  2026-10-01 抓取，HTTP 200）：
  语法行 `cmd [/c|/k] [/s] [/q] [/d] [/a|/u] [/t:{<b><f> | <f>}] [/e:{on | off}] [/f:{on | off}] [/v:{on | off}] [<string>]`；
  参数表 `/c` → `Carries out the command specified by <string>`。
- **本机 `cmd /?`**（8008 字节，stdout，退出码 **1**）：
  `/C      Carries out the command specified by string and then terminates`、
  `/K      Carries out the command specified by string but remains`，
  以及 `[[/S] [/C | /K] string]`。两处措辞一致。

### 2.4 为什么不用 `useShell` / `execution: terminal`

- `runtime.useShell` **宿主尚未实现**（规范 §2.4 表格），而且规范已经给出正解：
  "需要 cmd 内建命令时，正确做法是 `locate.executable: cmd.exe` + `command: /c` + `commandArgs: [dir]`"
  —— 本包就是照这句话做的。
- `execution: terminal` 不需要：`cmd /c ver` 不交互、不弹窗、立即返回，
  宿主直接捕获输出即可（`output.mode: capture`）。

---

## 3. 参数知识来源

1. **官方文档**：<https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ver>
   （"Last updated on 2023-02-03"，2026-10-01 抓取；HTTP 200）。语法只有 `ver` 一行，参数表只有 `/?`。
2. **cmd 官方文档**：见 §2.3（用于核对 `/c` 的语义）。
3. **本机 `cmd /c ver` 的真实输出**：47 字节，stdout，退出码 0。
4. **本机 `cmd /?`**：8008 字节，stdout，退出码 1。

**结论：三处一致**（`ver` 无参数无开关；`/?` 只显示帮助；`/c` 的语义如官方所述）。
没有"官方与实测对不上"的参数差异（唯一没写的是**退出码**，见 §7）。

---

## 4. 输出编码：**没有中文样本**，按字节判定为纯 ASCII（UTF-8 可解码）

`ver` 的输出是微软写死的英文前缀 + 数字版本号，**不可能**出现中文
（`cmd /?` 的帮助在本机也全是英文，虽然它很长）。因此照 playbook §10.3 第 14 条，
只能如实说明"无中文样本"，而不是声称"测出了编码"：

| 输出 | 字节数 | 非 ASCII 字节 | 严格 UTF-8 解码 | 原始字节 |
|---|---|---|---|---|
| `cmd /c ver` stdout | 47 | **0** | **成功** | `0d 0a` + `Microsoft Windows [Version 10.0.26200.8655]` + `0d 0a` |
| `cmd /c ver /?` stdout | 38 | **0** | 成功 | `Displays the Windows version.\r\n\r\nVER\r\n` |
| `cmd /c ver /nosuch` stderr | 41 | **0** | 成功 | `The syntax of the command is incorrect.\r\n` |
| `cmd /c nosuchcommandxyz` stderr | 107 | **0** | 成功 | `'nosuchcommandxyz' is not recognized as an internal or external command,\r\noperable program or batch file.\r\n` |
| `cmd /?` stdout | 8008 | **0** | 成功 | `Starts a new instance of the Windows command interpreter\r\n…` |

**结论**：全部输出都是**纯 ASCII**；清单写 `encoding: utf-8`
（严格 UTF-8 解码成功，且与代码页无关，换机器更安全）。

**如实声明**：本条**没有**中文样本可判。若要造样本，唯一办法是让 cmd 内建命令输出中文，
例如 `cmd /c "dir 不存在"` 之类 —— 但那已经超出 ver 的范围，不属于本包（见 §9）。

---

## 4.1 宿主侧集成验证（已跑，全绿）

```
PS> dotnet test src\AllTool.slnx --nologo
已通过! - 失败: 0，通过: 176，已跳过: 0，总计: 176，持续时间: 58 s
```

`RealManifestTests.加载全部工具包都不应抛异常` 会 `ManifestLoader.LoadAll(plugins/)`
加载仓库里所有工具包（本包在内）—— 也就是说，**`command: ""` + `commandArgs: ["/c", "ver"]`
这个新写法通过了宿主的运行时结构校验**（`ManifestValidation`），
没有触发 `缺少 command` 那类历史问题（那条已在 `ManifestValidation.cs:90` 修成"只要求键存在"）。
宿主测试只对 7zip 断言具体 argv，所以"组装结果是 `[cmd.exe, /c, ver]`"这条靠 §5 的真机冒烟验证。

---

## 5. 退出码与真机冒烟（全部实跑）

| 命令 | stdout | stderr | exit | 说明 |
|---|---|---|---|---|
| `cmd /c ver` | 47 B（版本行） | 0 | **0** | 成功；连跑两次输出完全一致 |
| `cmd /c ver`（第二次） | 47 B，与第一次逐字节相同 | 0 | **0** | 无抖动 |
| `cmd /c ver /?` | **38 B**：`Displays the Windows version.` + 空行 + `VER` | 0 | **1** | 帮助写 stdout，退出码 1 |
| `cmd /c ver -x` | 47 B（版本行） | 0 | **0** | `-x` 被 ver 忽略 —— 说明 ver **不做参数校验** |
| `cmd /c ver /nosuch` | 0 | 41 B `The syntax of the command is incorrect.` | **1** | ver 认得出 `/?`，其它参数报语法错 |
| `cmd /c nosuchcommandxyz` | 0 | 107 B `'…' is not recognized as an internal or external command, operable program or batch file.` | **1** | 命令不存在 |
| `cmd /c`（`/c` 后无内容） | 0 | 0 | **0** | cmd 直接退出，什么都不做 |
| `cmd /?` | 8008 B（帮助） | 0 | **1** | cmd 自身的帮助 |

**清单 `exitCodes` 的写法**：只有 `0`（成功打印了版本）与 `1`（语法错误 / 参数不被接受 / 命令不存在）。
**依据**：上表 + `cmd /c` 会**原样传递**被执行命令的退出码（`ver` 成功给 0、`ver /?` 给 1，
cmd 都如实传出）。微软的 ver 页面**没有退出码章节**（已核对整页）。

### 5.1 `versionPattern` 实测（本包**有**版本号，与同批其它 Windows 包不同）

同批 Windows 自带命令包（ping / systeminfo / hostname …）一律不写版本，
理由是"没有版本开关，无参数运行又有副作用"（`docs/ai/windows-commands.md` §4）。
**本包是例外**：`ver` 输出的**就是系统版本号**，"取版本"不需要额外跑一个动作。

```yaml
locate:
  executable: cmd.exe
  versionArgs: ["/c", "ver"]
  versionPattern: '(?m)Version\s+([0-9][0-9.]*)'
  minVersion: "6.1"
```

实测（用清单里的正则去匹配 `cmd /c ver` 的真实 stdout）：

```
输入（原始字节）: b'\r\nMicrosoft Windows [Version 10.0.26200.8655]\r\n'
正则            : (?m)Version\s+([0-9][0-9.]*)
匹配            : True
捕获组 1        : 10.0.26200.8655
```

- **必须带 `(?m)`**：输出**第一行是空行**（`0d 0a` 开头），不加多行标志的正则从串首开始找也能中，
  但规范 §3.1 与 playbook P4 要求显式写 `(?m)` —— 本包照做。
- **`(?m)` 与 CRLF**：pattern 用 `\s+` 而不是空格，因此行尾的 `\r` 不影响匹配
  （这里是取版本号，不涉及 `\r?$` 那种行尾锚点，playbook §10.3 第 18 条的坑不适用）。
- **`minVersion: "6.1"`**：Windows 7 的版本号。它的作用只是"当机器比 Windows 7 还老时禁用本包"，
  当前系统 10.0.26200 远高于它。**注意**：`VersionComparison` 是逐段数值比较，
  `10.0.26200.8655` 与 `6.1` 比较时第一段 10 > 6，判定成立。
  写这个值而不是写 `10.0`，是为了不谎称"只有 Windows 10+ 才有 ver"（Vista/7 也有）。

---

## 6. 交叉验证：`ver` 报的版本与别的途径一致

| 途径 | 结果 |
|---|---|
| `cmd /c ver` | `Microsoft Windows [Version 10.0.26200.8655]` |
| 注册表 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` → `CurrentBuild` + `.` + `UBR` | `26200.8655` |
| 注册表 → `DisplayVersion` | `25H2` |
| 注册表 → `ProductName` | `Windows 10 Pro for Workstations` ⚠️ |
| `[System.Environment]::OSVersion.Version` | `10.0.26200.0`（**没有 UBR**） |
| `(Get-CimInstance Win32_OperatingSystem).Version` | `10.0.26200`（**没有 UBR**） |

**结论**：`ver` 的数字与注册表的 `CurrentBuild.UBR` **完全吻合**，
比 .NET / CIM 的 API 更完整（那些只给到 `10.0.26200`，拿不到 `.8655`）。
这是本包的一个真实价值点：**一条命令拿到完整内部版本号**。

⚠️ **`ProductName` 那个坑**：注册表里写的是 "Windows 10 Pro for Workstations"，
而 `docs/ai/windows-commands.md` 记录的 `systeminfo` 在同一台机器上自报
"Windows 11 专业工作站版"。这是 Windows 的已知现象（`ProductName` 长期没跟着升大版本号改），
**不是 ver 的问题** —— `ver` 只报 `10.0.<build>.<ubr>`，本来就不报"Windows 11"这种商品名。
**如实记录**：所以**不要**用 ver 的输出判断"这是 Windows 10 还是 11"，
它给的是**内核版本**；要商品名得看 `systeminfo` 或"关于 Windows"。

---

## 7. 与官方文档对不上的地方

| # | 项 | 官方文档 | 实测 | 处理 |
|---|---|---|---|---|
| 1 | 退出码 | **整页没有退出码章节** | `0` = 成功；`1` = 语法错 / 参数不被接受 / 命令不存在 | 按"行为以实测为准"，写进 `exitCodes`（§5） |
| 2 | `/?` 的退出码 | 参数表只写 "Displays help at the command prompt"，没说退出码 | **1**（帮助文本在 stdout） | 写进 `exitCodes.meaning` 与 `help` 动作的 `resultNote` |
| 3 | `ver -x` 的行为 | 文档只列 `/?`，没说其它参数怎么办 | **被忽略，仍打印版本、退出码 0** | 不收录 `-x`（无出处）；行为记在本表 |
| 4 | `/?` 是"ver 的参数"还是"cmd 的" | ver 页把它列在 ver 的参数表里 | `cmd /c ver /?` 与 `cmd /c ver/?` 都打印 **ver 的**帮助（`Displays the Windows version.` + `VER`），说明是 ver 自己处理的 | 按官方写（归 ver）；见下 |

第 4 条多说一句：`cmd /c ver /?` 打印的是
`Displays the Windows version.\r\n\r\nVER\r\n`（ver 的帮助），**不是** cmd 的帮助 ——
所以 `/?` 确实是被 ver 处理的，官方把它列在 ver 的参数表里是对的。
`ver` 与 `/?` 之间**有没有空格都一样**（`cmd /c ver/?` 也得到同一份帮助）——
但这不代表可以用单 token 写法：本包用两个独立 argv 元素（`["/c", "ver", "/?"]`），
两个 token 之间会被 cmd 拼成命令行，空格是 cmd 加的，与我们的 argv 数组无关。

---

## 8. 字段风格 / argv 顺序

**零字段**，不涉及字段风格。实际 argv：

| 动作 | 组装结果 |
|---|---|
| `show` | `[C:\Windows\System32\cmd.exe, /c, ver]` |
| `help` | `[C:\Windows\System32\cmd.exe, /c, ver, /?]` |

`/c` 与 `ver` 之间的顺序由 `commandArgs` 的**数组顺序**固定，不存在被用户打乱的可能。

---

## 9. 故意没做 / 没验的部分

| 项 | 原因 |
|---|---|
| 直接 `locate.executable: ver.exe` | **ver.exe 不存在**（§1.1）。写了会让宿主找不到可执行文件、整个包不可用 |
| `command: "/c ver"`（一个 token） | 宿主会把整串当一个 argv 元素，cmd 收到的是 `"/c ver"` 而不是 `/c` + `ver`（§2.2 候选 D） |
| `runtime.useShell: true` | 规范 §2.4：宿主尚未实现；且官方已给出 `cmd.exe` + `/c` 的正解 |
| `cmd` 自身的其它开关（`/q` `/d` `/u` `/a` …） | 与"显示版本"无关，放进本包就是给用户加噪音；它们属于"cmd 工具包"该有的东西，不是 ver 的 |
| `winver.exe`（"关于 Windows"GUI） | 它是**图形界面**程序 —— 按产品负责人"系统本来就有称手图形界面的命令不做"的规则，不该进本包（而且 `plugins/systools` 已经有它的快捷入口） |
| PowerShell 的替代写法（`$PSVersionTable.OS`） | 官方页提到它，但那不是本包要包装的命令（本包只包装 `ver`） |
| `cmd /c` 输出中文样本以判编码 | 要构造中文输出来源（例如目录不存在的 `dir` 报错），那属于"cmd 内建命令"这一批的课题，不是 ver 的输出 |
| `dotnet test src\AllTool.slnx` 里 ver 的 argv 断言 | 宿主测试只对 7zip 断言具体 argv；ver 的 argv 由 §5.2 的真机冒烟验证 |
| `nextSteps` / `quickActions` | `help` → `show` 这种推荐没有真实依据（帮助输出里没有"该看版本了"的信号），凭空写违反规范 §2.2 第 2 条 |

## 10. 临时对象申报

**没有创建任何临时对象。** `cmd /c ver` 与 `cmd /c ver /?` 都是只读、
不写文件、不改环境、不改注册表。实测只在 `%TEMP%\alltool-probe\` 下落了探针脚本与输出文本
（**不在仓库内**），收尾时可整体删除：

```powershell
Remove-Item -Recurse -Force (Join-Path $env:TEMP 'alltool-probe')
```

仓库内**只新增** `plugins/ver/manifest.yaml` 与 `plugins/ver/NOTES.md` 两个文件。

---

## 11. 给后来者：做 cmd 内建命令（dir / echo / copy / type / cls …）时的复用点

本包把"怎么把内建命令表达进清单"这条路走通了，结论可以直接复用：

1. `locate.executable: cmd.exe`（**不要**写 `ver.exe` 之类不存在的东西）；
2. `command: ""`，把 `/c` 与内建命令名一起放进 `commandArgs`（本包的选择，理由见 §2.2）；
   如果你要做的是一个"跑用户输入的命令"的动作，那又是另一个边界问题（`useShell` 已实现与否），
   本包不涉及；
3. **退出码规律比 exe 更杂**：cmd 会原样传递内建命令的退出码
   （`ver` = 0、`ver /?` = 1、找不到的命令 = 1、语法错 = 1），
   但**别假设"0 = 成功"**：`cmd /c` 什么都不跑也返回 0（本包实测）；
4. **编码要重测**：本包全是纯 ASCII，判不出编码；`dir` / `type` 这类会输出中文的命令
   必须按 `whoami/NOTES.md` §4 的方法重新实测（不能照抄本包的 `utf-8`）；
5. `cmd /?` 是 8008 字节的长帮助（stdout、退出码 1），其中第二段专门讲
   `/C` 或 `/K` 之后引号怎么处理 —— 内建命令的参数里**带空格或引号**时，
   那段规则（"正好两个引号且中间没有特殊字符时保留引号"）必须先读。
