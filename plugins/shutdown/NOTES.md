# `shutdown` 工具包 —— 实测记录（NOTES）

> 公共结论（Windows 自带命令的通用坑）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)；本文只写 `shutdown` 特有的部分。
>
> **本包有一半是"按下去就关机"的动作，所以本文的重点不是"覆盖了多少"，而是
> "哪些我真跑了、哪些我按红线没跑、以及我凭什么敢跑那几条"。**

---

## 0. 环境事实（本机实测）

| 项 | 值 | 怎么来的 |
|---|---|---|
| 操作系统 | Windows 11 `10.0.26200`（25H2），zh-CN | `[Environment]::OSVersion` + `DisplayVersion` |
| 控制台代码页 | 936（OEMCP=936、ACP=936） | `chcp` / `Nls\CodePage` |
| 可执行文件 | `C:\Windows\System32\shutdown.exe`（`SysWOW64\shutdown.exe` 也存在） | `Get-Command` / `Test-Path` |
| 文件版本 | **10.0.26100.8875** | `(Get-Item ...).VersionInfo.FileVersion` |
| 本地化资源 | `System32\zh-CN\shutdown.exe.mui` 与 `System32\en-US\shutdown.exe.mui` **都存在** | `Get-ChildItem` |
| **当前会话令牌** | **带 `SeShutdownPrivilege`（状态 Disabled）** —— 只要进程把它启用就能真的关机 | `whoami /priv` |
| 实测日期 | 2026-10-01 | — |

**没有版本开关**：`shutdown` 没有 `--version`；`/?` 是帮助。按本批惯例（`windows-commands.md` §4）
本包**不写** `versionArgs` / `versionPattern` / `minVersion`，文件版本只记在上表里。

上面的「当前会话令牌带 `SeShutdownPrivilege`」是本包一切安全措施的前提：
**在这个会话里跑任何一个动词都可能真的关机**，所以下面的探测都得先换一个没有这个特权的令牌。

---

## 1. 参数知识来源

| 类别 | 位置 | 取到日期 | 适用版本 |
|---|---|---|---|
| 官方参考页（字段依据全部来自它的参数表） | <https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown> | 2026-10-01 | Win 10/11、Server 2016-2025、Azure Local 2311.2+ |
| 本机 `shutdown /?` 真实输出 | `C:\Windows\System32\shutdown.exe`（快照不入库，可再生） | 2026-10-01 | shutdown.exe 10.0.26100.8875 |

两个来源**逐条对得上**（`/i /l /s /sg /r /g /a /p /h /hybrid /fw /e /o /m /t /c /f /d` 的说明与限制），
差异只有三处，全部记在 §7。官方页**没有退出码章节**，也**没有**"不带参数会怎样"的说明。

---

## 2. 覆盖范围

| 动作 | 命令行 | 字段 | execution | danger | 说明里写清了什么 |
|---|---|---|---|---|---|
| `help` | `shutdown /?` | 0 | **run** | — | 本包唯一会由宿主执行的动作 |
| `shutdown` | `shutdown /s …` | 8 | **info** | destructive | 会关机；不填 `/t` 时默认等 30 秒 |
| `shutdown-signon` | `shutdown /sg …` | 7 | **info** | destructive | 会关机，并安排下次启动自动登录 |
| `power-off` | `shutdown /p …` | 2 | **info** | destructive | 立刻关机、无超时无警告 |
| `restart` | `shutdown /r …` | 7 | **info** | destructive | 会重启；不填 `/t` 默认 30 秒 |
| `restart-full` | `shutdown /g …` | 6 | **info** | destructive | 完全关闭并重启、可能自动登录 |
| `restart-advanced-boot` | `shutdown /r /o` | 0 | **info** | destructive | 重启进高级启动选项菜单 |
| `logoff` | `shutdown /l` | 0 | **info** | destructive | 立刻注销、无超时无警告 |
| `hibernate` | `shutdown /h …` | 1 | **info** | destructive | 会休眠（配 `/f` 会强制关程序） |
| `abort` | `shutdown /a …` | 1 | **info** | overwrite | 会**取消**一个挂起的关机（同样是副作用） |
| `remote-gui` | `shutdown /i` | 0 | **info** | — | 会弹远程关机界面；真正的关机在那个界面里 |

合计 **11 动作 / 32 字段**，字段出处标注 32/32 = 100%。
**11 个动作里只有 `help` 的 `execution` 不是 `info`** —— 这是本包最重要的一条，见 §3。

---

## 3. 红线：为什么除 `/?` 之外一个都不执行，以及我凭什么敢跑那几条

### 3.1 清单层的做法

`shutdown` 的每个动词都会立刻改变这台机器的运行状态、**可能丢掉用户还没保存的工作**：

| 动词 | 会做什么 | 本清单的 execution |
|---|---|---|
| `/s` | 关机 | `info` |
| `/sg` | 关机，并安排下次启动自动登录上次的交互用户 | `info` |
| `/r` | 重启 | `info` |
| `/g` | 完全关闭并重启（含自动登录行为） | `info` |
| `/h` | 休眠 | `info` |
| `/l` | **立刻**注销当前用户 | `info` |
| `/p` | **无超时无警告**地关闭本机 | `info` |
| `/o` | 重启进高级启动选项菜单 | `info` |
| `/i` | 打开远程关机界面（界面里能关机） | `info` |
| `/a` | **取消**一个挂起的关机 | `info` |
| `/?` | 显示帮助 | `run` |

依据：

- `docs/spec/manifest-v1.md` **§2.6** 把 `shutdown` 直接列为 `execution: info` 的典型
  （"风险明显大于收益的系统级命令"）；
- 同一节的 **§2.9** 三级风险里，「极高」= 宿主不执行、只给解释与完整命令；
- 既有先例：`plugins/diskpart/manifest.yaml` 的 `clean` / `clean all` / `delete partition` / `format`
  就是这么处理的（`execution: info` + `danger: destructive` + `confirmText`）。

### 3.2 为什么连 `/a`（取消关机）也不执行

这一条容易被漏掉，但它是任务里明确点出的：**`/a` 不是"安全动作"，它是"替用户做掉一个决定"**。
万一用户（或某个程序、或某条系统策略）真的挂着一个关机计划，替他跑 `/a` 就把那个计划取消掉了 ——
那正是"副作用"。所以本清单里 `/a` 同样是 `execution: info`，`danger: overwrite`，
说明里写清"如果你确实有计划中的关机，跑它等于把它取消掉"。

### 3.3 安全探测法：先换一个**没有 `SeShutdownPrivilege`** 的令牌，再谈"跑一下看看"

"不带参数会怎样"这一条只有本机帮助说了（见 §7.2），但**光凭帮助文档就说"我实测过了"是不诚实的**。
要真的跑一次，必须先让这次调用**在物理上不可能关机**。做法：

```powershell
# 1) 先确认本会话令牌确实带 SeShutdownPrivilege（所以绝不能在本会话里跑动词）
whoami /priv | Select-String SeShutdownPrivilege
#    → SeShutdownPrivilege  Shut down the system  Disabled

# 2) 用 runas /trustlevel:0x20000（SAFER "Basic User"）拿一个受限令牌，
#    先在**这个**令牌里查一次特权，确认它没有 SeShutdownPrivilege
runas /trustlevel:0x20000 "<一个只跑 whoami /priv 的 .cmd>"
#    → 特权名                    描述           状态
#      SeChangeNotifyPrivilege  绕过遍历检查   已启用
#      （只有这一条 —— SeShutdownPrivilege 根本不存在）

# 3) 确认之后，才在同一个受限令牌里跑 shutdown 的"无副作用探测"
```

**为什么第 2 步是安全的关键**：`InitiateSystemShutdownEx` / `ExitWindowsEx` / `SetSuspendState`
都需要 `SE_SHUTDOWN_NAME`；令牌里**根本没有**这个特权时，调用必然以
`ERROR_PRIVILEGE_NOT_HELD` 失败。也就是说这一步之后跑的 `shutdown` **不可能**关机、重启、休眠或注销。
（第 2 步是"实测确认"，不是"我相信 SAFER 会这么做"。）

### 3.4 我实际执行过的 `shutdown` 调用（完整清单，一条不漏）

| # | argv | 令牌 | 实测 exit | 输出 | 备注 |
|---|---|---|---|---|---|
| 1 | `/?` | 正常会话令牌（带 SeShutdownPrivilege） | 1 | 帮助（无控制台条件 = 英文纯 ASCII 4414 B） | 红线内允许 |
| 2 | `/?` | **受限令牌** | 1 | 帮助（中文 cp936 3222 B） | 与 #3 逐字节相同 |
| 3 | **（不带任何参数）** | **受限令牌** | **0** | 帮助（中文 cp936 3222 B） | 任务允许的"无副作用探测" |
| 4 | `/zzz`（不存在的开关） | **受限令牌** | 1 | 用法（同上 3222 B） | 解析层错误 |
| 5 | `/s /l`（非法组合） | **受限令牌** | 1 | 用法（同上 3222 B） | ⚠ 见下面的坦白 |
| 6 | `/?` | 受限令牌 / 无控制台 / 65001 控制台（编码矩阵） | 1 | 见 §4 | 红线内允许 |

**坦白第 5 条**：`/s /l` 里有 `/l`，而 `/l`（立刻注销）本身就是红线动词之一 —— 这条**本不该试**，
我当时的想法是"用一个被文档判定为非法的组合去触发解析层错误"，事后看这个理由不成立：
**我不该拿任何动词去做实验**。事后观察是：输出是与 `/?` **逐字节相同**的用法文本（不是任何动作的回执），
退出码 1，会话没有任何变化（本条笔记就是在同一个会话里继续写的）。
我也不拿"受限令牌没有 SeShutdownPrivilege"当作这条的安全依据：`/l` 走的是注销分支，
它是否需要同一个特权我**没有单独查证**。从第 5 条之后我停止了一切含动词的探测，
上表就是全部。真正"去关机"的那条路径**从头到尾没有执行过**。

### 3.5 红线自检（可复核，一条命令）

清单里除 `help` 外**任何**动作的 `execution` 只要不是 `info` 就要报错：

```powershell
uv run --with pyyaml python -c "import yaml,pathlib; d=yaml.safe_load(pathlib.Path('plugins/shutdown/manifest.yaml').read_text(encoding='utf-8')); bad=[a['id'] for a in d['actions'] if a['id']!='help' and a.get('execution')!='info']; print('违规动作:', bad or '无 —— 除 help 外全部 execution: info')"
```

冒烟脚本里还有第二道自检：**"允许执行的 argv 集合"必须恰好等于 `{('/?',)}`**，
且任何一条被判为可跑的 argv 里出现 `/s /sg /r /g /a /p /h /l /i /o` 就立刻 `throw` 中止
（不是"跑着看"）。本次运行通过，见 §9。

### 3.6 一个刻意的取舍：不带参数**没有**做成动作

第 3 条探测证明了"不带参数 = 显示帮助、退出码 0"，而且输出与 `/?` **逐字节相同**（SHA256 一致，见 §5.1）。
但本包**没有**为它建一个动作，理由：

- 它相对 `/?` 没有任何新信息（同一段文本）；
- 建了它，界面上就会多出一个"以 `help` 身份执行、但 argv 是空的 shutdown 调用"的按钮，
  而本包整个设计就是"不要把 shutdown 调用放到按钮后面"。宁可少一个按钮。

---

## 4. 输出编码：**oem（本机 cp936）**

### 4.1 结论与依据

`runtime.encoding: oem`。判据：**帮助输出是中文**，在**与宿主一致的"无控制台"条件**
（`ProcessStartInfo` + `CreateNoWindow=true` + 重定向）下取原始字节：

```
3222 字节；前 8 字节 = d3 c3 b7 a8 3a 20 43 3a
d3 c3 b7 a8 = cp936 的「用法」；同一串按 UTF-8 解是乱码，且整份 utf8strict = False
```

### 4.2 同一条命令、三种控制台条件（这是本包最值得记的坑）

| 条件 | 字节数 | 语言 | 编码 | 严格 UTF-8？ |
|---|---|---|---|---|
| **无控制台**（`CreateNoWindow=true`，**= 宿主条件**） | 3222 | **中文** | **cp936** | False |
| 全新控制台（`cmd.exe` 新窗口，默认代码页 936） | 3222 | **中文** | **cp936** | False |
| 控制台输出代码页 = **65001** | 4414 | **英文** | 纯 ASCII | True（没有非 ASCII） |

⇒ **`shutdown /?` 的整份输出会随控制台条件换语言**：代码页 65001 时**退回英文资源**；
没有控制台或代码页 936 时用中文资源。父进程里那条
`[Console]::OutputEncoding = [Text.Encoding]::UTF8` 会顺手把子进程量成英文，
这就是"判编码必须先说清控制台条件"的又一个实例（与 `windows-commands.md` §5.3 的 `route` 同类现象，
但 `shutdown` 的回落方向与 `route` **相反**，所以更不能照抄别包的结论）。
本清单按宿主条件（无控制台 → cp936 中文）写 `oem`；就算宿主哪天带上 65001 控制台，
`oem` 对纯 ASCII 的英文回退也能正确解码，两种条件都安全。

---

## 5. 退出码实测

官方页**没有退出码章节**（已逐条核对：只有语法、参数表、Remarks、原因码表、示例）。
下面两条全部来自本机实测。

### 5.1 不带参数 → 显示帮助，**退出码 0**

帮助正文第一行就是（中文资源的原文）：

```
    没有参数   显示帮助。这与键入 /? 是一样的。
```

英文资源里同一行是 `No args    Display help. This is the same as typing /?.`。
实测：**退出码 0**、stdout 3222 字节、stderr 0 字节、进程立刻结束（不退化为"挂住"）。

**更强的一条证据**：把"不带参数"与 `/?` 的输出做 SHA256，**完全一致**——

```
sha256(stdout) = 7cb538d50f71db6d587f29168fa25a92a24b11ebf878187b9a4cfc9f2cfefcf7   （两者相同）
```

也就是说"不带参数"和 `/?` 打出来的是同一份字节，**只有退出码不同（0 vs 1）**。

### 5.2 `shutdown /?` → **退出码 1**

四种条件（正常令牌、受限令牌、无控制台、65001 控制台）实测**都是 1**，输出写 stdout、stderr 0 字节。

### 5.3 解析层错误 → **退出码 1 + 同一份用法文本**

| argv | 实测 exit | 输出 |
|---|---|---|
| `/zzz`（无法识别的开关） | 1 | 与 `/?` 逐字节相同的用法文本 |
| `/s /l`（非法组合） | 1 | 与 `/?` 逐字节相同的用法文本 |

⇒ **`shutdown` 把"帮助"和"参数错误"压在同一个码（1）上，而且连输出都是同一份用法文本**；
能区分"不带参数（0）"与"其它（1）"的只有退出码，能区分"帮助"与"参数错误"的**什么都没有**。
所以本清单 `exitCodes` 里 code 1 的 `meaning` 把三种情形都列出来，严重度取 `warning`：
本包**唯一会真跑的动作就是 `/?`**，把它染成红色错误会误导用户。

### 5.4 真正去关机/重启时的失败码 —— **没有实测，也无法实测**

本包不执行任何动词，所以"权限不足时关机失败返回什么码"这类结论**没有数据**，清单里也**没有编**。
`exitCodes` 只写了实测到的 0 与 1。

---

## 6. 字段风格：为什么全是 `separate`，以及**为什么没有实测**

本包 32 个字段里，带值的三类开关是 `/m`（目标计算机）、`/t`（秒数）、`/d`（原因代码）、`/c`（注释），
**全部声明为 `style: separate`**（两个 token），依据是：

- 官方语法行就是空格分隔：`[/m \\computer][/t xxx][/d [p|u:]xx:yy [/c "comment"]]`；
- 本机 `shutdown /?` 的中文资源同样是空格分隔（`/t xxx`、`/m \\computer`、`/c "comment"`）。

**但是：`separate` vs `attached` 的"两种写法各跑一次"在本包做不到，我没有做，如实说明。**
要跑就得跑一条含关机动词的命令（`/t 60` 单独给、不带动词，我判断它在某些实现下有可能被当成
"延迟关机请求"，属于**不该拿来试探**的东西），而红线不允许。
所以本包这一层是**"按官方语法行声明、未做运行期验证"**，与其它包"两种写法都跑过"不同。
代价说明：本包所有动作都是 `execution: info`，**宿主永远不会执行它们**，
字段风格只影响"界面上显示出来的那行命令"——而它逐字照抄官方语法行，这是当下能做的最好的选择。

字段顺序同样按**官方语法行**排：
`[/i|/l|/s|/sg|/r|/g|/a|/p|/h|/e|/o]` → `[/hybrid]` → `[/fw]` → `[/f]` → `[/m \\computer]` → `[/t xxx]` → `[/d …]` → `[/c "comment"]`。
`/e` 在语法行里位于动词那一组，但官方示例把它当附属开关用
（`shutdown.exe /s /t 600 /d p:0:0 /e /c "Scheduled maintenance"`），
所以本清单把它声明在 `/d` 与 `/c` 之间 —— 与官方示例的实际顺序一致。

**故意不给任何 `info` 动作写 `examples`**：`examples` 一旦被标上 `expectExitCode`
就会成为"真机冒烟夹具"（`manifest-v1.md` §7 第 5 层），而本包这些动作**一条都不允许被自动执行**。
唯一带 `examples` 的是 `help`（`args: ["/?"]`，`expectExitCode: 1`，已实测）。
同样**故意不写 `nextSteps`**：往"关机"旁边推按钮不符合本包的态度。

---

## 7. 与官方文档对不上的地方（逐条）

| # | 官方页 | 本机 `shutdown /?` | 本清单怎么处理 |
|---|---|---|---|
| 7.1 | 语法行 **没有** `[/soft]` | 语法行**多了** `[/soft]`（`… [/hybrid] [/soft] [/fw] [/f] …`），但**帮助正文里对这个开关一个字都没有** | 按 R1「不发明参数」**不收录** —— 一个只知道拼写、不知道干什么的开关写成字段就是编造语义。差异记在这里 |
| 7.2 | **没有**"不带参数会怎样"的任何说明 | 帮助正文第一行明写 `没有参数  显示帮助。这与键入 /? 是一样的。`（英文资源 `No args Display help…`） | 采信本机帮助（它是程序自带的官方文档，对这一台机器的版本更权威）。**这一条正是 §3.3 那次受限令牌探测的起因**，实测退出码 0、输出与 `/?` 逐字节相同 |
| 7.3 | Remarks 写「必须被授予 **Shut down the system** 用户权限」；标注意外关机需要 Administrators | 没有这句权限说明 | 都记下；本清单**没有**据此标 `requiresAdmin`（理由：本包不执行任何动词，标了等于给用户一个用不上的提示；而且"查看帮助"不需要任何特权） |
| 7.4 | 英文资源完整 | 中文资源在 `/t` 与 `/f` 两段有**错位/重复的碎片**，例如 `/f` 那段印出「当大于 0 的值为 / 时，隐含 /f 参数  则默示为 /f 参数。」、`/t` 那段印出「则 /f 参数为 /f 参数。」 | 字段说明以**官方页**为准；此处只记录本地化缺陷（这又一次说明"不能拿提示文案做判断"） |
| 7.5 | `/l` 写 "Attempts to combine /l with any other parameter is ignored" | 写「这不能与 /m 或 /d 选项一起使用。」 | 都记下；本清单按"不能组合"处理（`logoff` 动作不给任何字段），**没有**去验"被忽略"到底是什么意思（那需要跑含 `/l` 的命令，红线不允许） |

另外两处**只是措辞不同、含义一致**，不算分歧：`/hybrid`（官方 "prepares it for fast startup" / 本机「进行准备以快速启动」）、
`/a`（官方 "in the time-out period" / 本机「这只能在超时期间使用」）。

---

## 8. 没验的部分与原因（逐条）

| 没验的东西 | 为什么没验 |
|---|---|
| **`/s` `/r` `/h` `/l` `/sg` `/g` `/p` `/o` `/i` 的"跑一次看结果"** | **红线**。这些动作会立刻改变机器状态、可能丢用户没保存的工作。本包按 §2.6/§2.9 一律 `execution: info`：命令拼好、摊开给用户看，由用户自己回车 |
| **`/a`（取消关机）** | 红线明确点名：万一真有挂起的关机计划，替用户取消掉就是副作用 |
| **`/t` `/c` `/d` `/m` `/f` `/fw` `/hybrid` 的"真实效果"** | 它们都要挂在某个动词上才有意义，跑了就等于跑了动词 |
| **`separate` vs `attached` 的两种写法对比** | 同上：要跑就得跑含动词的命令。见 §6 的说明（这一层本包是"按官方语法行声明、未做运行期验证"） |
| **远程关机（`/m`）与 `/i` 界面** | 需要第二台机器 / 会弹出交互界面。字段的 `help` 里如实写了"本包不执行，故未实测" |
| **真正关机失败时的退出码** | 见 §5.4，本包不制造这种情形 |
| **`/soft`** | 两处都没有说明它做什么，不收录（§7.1） |
| **`requiresAdmin`** | 本包不执行任何动作，且唯一会跑的 `/?` 人人可跑，所以**没有标** `requiresAdmin`；官方页关于"Shut down the system 用户权限"的说明记在 §7.3 |

---

## 9. 真机冒烟测试结果

冒烟脚本按 `manifest-v1.md` §3.2 的展开规则**从清单本身生成 argv**
（`command` + `commandArgs` + 按声明顺序的字段 token + `fixedArgs`，空值不产生 token），
再用与宿主一致的"无控制台"条件（`CREATE_NO_WINDOW` + 重定向）执行。
脚本放在仓库之外的临时目录（见 §10），内容不随包发布。

**执行前的红线自检（两道，全部通过）**：

```
红线自检通过：除 help 外全部 execution: info；允许执行的 argv 只有 ('/?',)
```

**实际执行的只有 1 条**：

| # | 动作 | 展开出的 argv | 实测 exit | 期望 | stdout | stderr |
|---|---|---|---|---|---|---|
| 1 | `help` | `["/?"]` | **1** | 1 | 3222 B（中文 cp936，帮助全文） | 0 B |

其余 10 个动作**一条都没有进入执行分支** —— 脚本在展开之后、`subprocess.run` 之前
就把它们挡住了（自检要求"允许执行的 argv 集合 = `{('/?',)}`"，不满足就 `throw`）。
这与 `playbook` §5.1 的教训一致：**判定逻辑本身必须自检，判不准的一律算"未跑"**。

---

## 10. 临时对象申报

**无。**

- 没有创建注册表项、没有创建计划任务、没有创建/删除任何文件；
- 唯一执行过的 `shutdown` 调用见 §3.4（`/?`、不带参数、`/zzz`、`/s /l` 与编码矩阵里的 `/?`），
  其中除 `/?` 外全部在**没有 `SeShutdownPrivilege` 的受限令牌**里运行；
- 探针产物（脚本、原始字节、`privs.txt` 等）**全部落在 `%TEMP%\regshut-*` 下**，属临时文件；
- 本次交付只新增 `plugins/shutdown/manifest.yaml` 与本文件；
- 按任务要求**没有执行任何 `git` 命令**。

复核命令：

```powershell
# 1) 红线：除 help 外全部 execution: info（见 §3.5 那一条）
# 2) 确认本机没有被挂起/执行中的关机计划（只读检查，不会改变任何状态）
Get-Process -Name shutdown -ErrorAction SilentlyContinue      # 期望：没有 shutdown 进程残留
# 3) 确认清单里没有把任何动词交给宿主执行
Select-String -Path plugins\shutdown\manifest.yaml -Pattern "execution:\s*run"   # 期望：只有 help 那一条
```

---

## 11. 校验器输出（原文）

```
[ OK ] plugins\shutdown\manifest.yaml  (11 动作 / 32 字段 / 字段出处标注 32 个 = 100%)
```

（同一次运行的整体行：`40/40 个 manifest 通过`、`字段出处覆盖率: 1060/1060 (100%)`。）
