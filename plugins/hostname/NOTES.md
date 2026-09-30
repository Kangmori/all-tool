# hostname 工具包 —— 实测记录

> 本包是 **Windows 自带命令工具包**。公共事实（退出码各家不同、`/?` 走哪个流、
> `command: ""` 的由来、校验器对 `/` 开关不生效等）都在
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md) 里，
> 本文只写 **hostname 特有的部分**。

## 0. 一句话结论

`hostname` 是本项目里最极端的工具包：**一个动作、零字段、零开关**。
官方语法就只有 `hostname` 一行，参数表里只有 `/?` —— 多一个字段就是发明参数（R1）。
所以本包的"准确"体现在**不写**上：不写 `versionArgs`、不写任何字段、不写 `requiresAdmin`。
唯一值得记住的实测结论是：**`hostname /?` 的退出码是 1（不是 0）**，与官方正文一致。

---

## 1. 实测环境（环境事实，不是知识）

| 项 | 值 | 怎么取的 |
|---|---|---|
| 系统 | Windows 11，内部版本 `10.0.26200.8655`，DisplayVersion `25H2` | `cmd /c ver`、注册表 |
| 账号 | **非管理员** | `WindowsPrincipal.IsInRole(Administrator)` = False |
| 控制台代码页 | **936** | `chcp.com` |
| HOSTNAME.EXE | `C:\Windows\System32\HOSTNAME.EXE`（注意 `Get-Command` 返回的名字是全大写），文件版本 `10.0.26100.8875 (WinBuild.160101.0800)` | `Get-Command` / `VersionInfo.FileVersion` |
| 本机计算机名 | `hostname` → **Kangmori**（原始大小写） | 实跑 |
| 实测日期 | 2026-10-01 | — |

`Get-Command hostname.exe` → `Application`（是 exe，不是内建命令）。

---

## 2. 参数知识来源

1. **官方文档**：<https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/hostname>
   （页面尾部 "Last updated on 2024-11-01"，2026-10-01 抓取核对；HTTP 200）
2. **本机 `hostname /?` 的真实输出**：54 字节，stdout，退出码 **1**

**结论：两处完全一致**，没有冲突项。官方页面的语法行只有 `hostname`，参数表只有 `/?`；
`/?` 的实际输出（前有空行）：

```
Prints the name of the current host.

hostname
```

**官方文档里有三条对本包有用、而且本机实测印证了的信息**：

| 官方原文 | 本机实测 |
|---|---|
| "Any parameter different than /? produces an error message and sets the errorlevel to 1." | `hostname /nosuchswitch` → stderr 99 B、**退出码 1** ✔ |
| "Environment variable %COMPUTERNAME% usually will print the same string as hostname, but in uppercase." | `hostname` = `Kangmori`，`%COMPUTERNAME%` = `KANGMORI` ✔ |
| "If environment variable _CLUSTER_NETWORK_NAME_ is defined, hostname will print its value." | **没验**（见 §9） |

---

## 3. 覆盖范围

| 动作 id | 命令 | 字段 | 危险级别 | 实测退出码 |
|---|---|---|---|---|
| `show` | `hostname` | **0** | none | 0 |

**为什么零字段**：官方语法 `hostname` 没有任何开关，唯一参数 `/?` 是帮助
（把它做成"动作"没有意义：用户点一下"执行"只想拿到机器名）。
本包也没有 `help` 动作 —— 与同样零开关的 `ver` 不同，`hostname /?` 输出只有一句话，
而"看帮助"的价值完全被 `show` 覆盖。

**为什么 `command: ""`**：`hostname` 没有子命令（Windows 命令是"开关 + 位置参数"模型）。
这不是漏写，是规范 §2.5 ① 明确允许的写法（`ManifestValidation.cs:90` 也已放行）。

---

## 4. 输出编码：**没有中文样本**，按字节判定为纯 ASCII（UTF-8 可解码）

照 playbook §10.3 第 14 条，判编码**必须**用带中文的输出；hostname 恰好**不可能**有中文
（计算机名在 Windows 上只能是字母数字与连字符，最多 15 字符；它的帮助文本也是英文）。
所以这里只能如实说明判定依据，而不是"测出了编码"：

| 输出 | 字节数 | 非 ASCII 字节 | 偶数位为 0 的比例 | 严格 UTF-8 解码 | 原始头 16 字节 |
|---|---|---|---|---|---|
| `hostname` | 10 | **0** | 0% | **成功** | `4b 61 6e 67 6d 6f 72 69 0d 0a` = `Kangmori\r\n` |
| `hostname /?` | 54 | **0** | 0% | **成功** | `0d 0a 50 72 69 6e 74 73 20 74 68 65 20 6e 61 6d 65 20` |
| `hostname /nosuchswitch`（stderr） | 99 | **0** | 0% | **成功** | `73 65 74 68 6f 73 74 6e 61 6d 65 3a 20 55 73 65 …` = `sethostname: Use …` |

**结论**：全部输出都是**纯 ASCII**。纯 ASCII 在 UTF-8、OEM 936、UTF-16LE 之外的任何
单字节编码下字节都相同，因此这里**测不出**"真正的编码"，只能说它不与 `utf-8` 冲突。
清单里写 `encoding: utf-8` 的理由：字节层面严格 UTF-8 解码成功，且 UTF-8 在任何代码页的
机器上都解得对（比写死 `oem` 更安全）。

**如实声明**：本条**没有**用中文样本判定过 —— 不是漏做，是这类输出不存在中文样本。
如果将来发现 hostname 的输出出现了非 ASCII（例如通过 `_CLUSTER_NETWORK_NAME_` 环境变量
塞进中文），本结论就需要重测，判定方法见 `whoami/NOTES.md` §4。

> **与 whoami 的对照**：同一台机器上 `whoami` 的 `/groups` 输出**是** UTF-8（有中文组名可判），
> 而 `hostname` 无从判定。两个包都写 `utf-8`，但**证据强度完全不同** ——
> whoami 是"实测出 UTF-8"，hostname 是"实测不出别的、且不冲突"。这个区别值得留着。

---

## 5. 退出码与真机冒烟（全部实跑）

| 命令 | stdout | stderr | exit | 备注 |
|---|---|---|---|---|
| `hostname` | 10 B `Kangmori\r\n` | 0 B | **0** | 成功 |
| `hostname /?` | 54 B（帮助） | 0 B | **1** | 帮助写 **stdout**，退出码却是 1 |
| `hostname /nosuchswitch` | 0 B | 99 B | **1** | `sethostname: Use the Network Control Panel Applet to set hostname.` + `hostname -s is not supported.` |

**注意 `/nosuchswitch` 的错误文本很有意思**：它不是说"未知开关"，而是走到了
**sethostname**（"设置主机名"）那条分支 —— 也就是说 `hostname` 把任何非 `/?` 的参数
都当成"要设置的主机名"，然后提示你只能通过图形界面（网络控制面板）或 `sethostname` API 去改。
`hostname -s` 这条 Unix 习惯用法在 Windows 上不支持，错误里专门写了这一句。
**这说明 `hostname` 在 Windows 上是"只读打印"命令，不提供改名功能** —— 改名的正路是
`Rename-Computer`（PowerShell）或 设置 → 系统 → 重命名这台电脑。

**`exitCodes` 依据**：官方正文那句话（"sets the errorlevel to 1"）+ 上表两条实测。
`0` = 打印了主机名；`1` = 给了参数（含 `/?`）。清单里就是这两条。

### 5.1 宿主会多跑一次 hostname（提醒）

`ToolLocator.LocateAsync` 在 `versionPattern` 为空时仍会把 `locate.versionArgs ?? []`
跑一遍取版本。本包没写 `versionArgs`，所以宿主会跑一次**无参数的 `hostname.exe`**：
只打印主机名、退出码 0、无副作用。这与同批其它 Windows 包一致
（那批也一律不写版本，理由见 `docs/ai/windows-commands.md` §4）。

**为什么 hostname 不学 `ver` 去写 `versionPattern`**：`ver` 本身输出的就是**系统版本号**，
拿它当版本有意义；hostname 输出的是**计算机名**，把它当"工具包的版本号"是假信息。
`hostname /?` 是帮助而不是版本输出，按 R1 也不能当成版本命令。

### 5.2 `versionPattern` 实测

**本包没有 `versionPattern`**（§5.1）。作为对照记录一条：如果硬要匹配，
`hostname` 的输出 `Kangmori` 里没有任何数字，任何版本正则都匹配不到 ——
这也从反面说明"不写版本"是唯一诚实的选择。

### 5.3 宿主侧集成验证（已跑，全绿）

```
PS> dotnet test src\AllTool.slnx --nologo
已通过! - 失败: 0，通过: 176，已跳过: 0，总计: 176，持续时间: 58 s
```

`RealManifestTests.加载全部工具包都不应抛异常` 会 `ManifestLoader.LoadAll(plugins/)`
把仓库里所有工具包加载一遍（本包在内），即宿主运行时结构校验（`ManifestValidation`）实测通过
—— 包括本包的 `command: ""` 与"动作必须有 sources/title"这些规则。
**注意**：宿主测试只对 7zip 断言**具体 argv**，所以"`hostname` 的 argv 就是一个 token"
这条是靠上面的真机冒烟验证的（`[HOSTNAME.EXE]`，无参数，exit 0）。

---

## 6. requiresAdmin：**不需要**（实测）

官方页面**没有**任何权限要求（只写了一条环境前提："available only if the Internet Protocol
(TCP/IP) protocol is installed"，这是功能前提，不是权限前提）。实测（非管理员账号）：

- `hostname` → 退出码 0，正常打印 `Kangmori`；
- `hostname /?`、`hostname /nosuchswitch` → 退出码 1，是**用法**错误，不是权限错误
  （错误文本里完全没有"权限/管理员/提升"字样）。

因此清单包级写 `runtime.requiresAdmin: false`，动作级不写。

---

## 7. 与官方文档对不上的地方

**没有。** 语法、参数、`errorlevel` 语义、`%COMPUTERNAME%` 大小写差异全部一致（§2）。

一处**补充**（不算冲突）：官方参数表列了 `/?`，但正文的"Any parameter different than /?"
把 `/?` 自身排除在"错误"之外 —— 而本机实测 **`hostname /?` 的退出码同样是 1**。
两句话合在一起读并不矛盾（文档没说 `/?` 返回 0），但**"`/?` 返回 1"这个事实文档没写**，
属于实测补充，已写进清单的 `exitCodes.meaning`。

---

## 8. 字段风格 / argv 顺序

本包**零字段**，因此不涉及 `separate` / `attached` / `positional` 的选择，
也不涉及"字段声明顺序 = argv 顺序"。实际组装出的 argv 就是 `[HOSTNAME.EXE]` 一个 token。

---

## 9. 故意没做 / 没验的部分

| 项 | 原因 |
|---|---|
| `_CLUSTER_NETWORK_NAME_` 覆盖行为 | 要用 `set` 改环境变量再跑 —— 那会**改当前进程的环境**，属写操作；且这是集群场景，普通 PC 用不到。官方已写明行为，本包不验 |
| `/?` 做成单独动作 | 输出只有一句话（`Prints the name of the current host.` + `hostname`），价值被 `show` 覆盖 |
| `versionArgs` / `versionPattern` / `minVersion` | hostname 没有版本开关，输出也不是版本号（§5.1）；同批 Windows 包一致做法 |
| 非 ASCII（`_CLUSTER_NETWORK_NAME_` 塞中文）时的编码 | 需要改环境变量才能造样本，属写操作；方法已写在 §4 |
| `dotnet test src\AllTool.slnx` 里 hostname 的 argv 断言 | 宿主测试只对 7zip 断言具体 argv；hostname 的 argv 由 §5.3 的真机冒烟验证 |
| `nextSteps` / `quickActions` / `category` | 只有 1 个动作，推荐与分组都没有意义 |

## 10. 临时对象申报

**没有创建任何临时对象。** 唯一的动作是只读打印计算机名。
实测只在 `%TEMP%\alltool-probe\` 下落了探针脚本与输出文本（**不在仓库内**），
收尾时可整体删除：

```powershell
Remove-Item -Recurse -Force (Join-Path $env:TEMP 'alltool-probe')
```

仓库内**只新增** `plugins/hostname/manifest.yaml` 与 `plugins/hostname/NOTES.md` 两个文件。
