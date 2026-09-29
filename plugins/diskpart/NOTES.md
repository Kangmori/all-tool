# diskpart 工具包 —— 实测记录

## 这个工具包特殊在哪

1. **它是会话型程序**（`session`）：所有命令都对"当前焦点"（选中的磁盘/卷/分区）生效，
   而焦点存在于进程内部。所以宿主用**脚本重放**的方式执行：先把你做过的选择重放一遍，
   再跑这条命令。见 `docs/spec/manifest-v1.md` §2.8 与 `SessionScriptBuilder.cs` 顶部注释。
2. **它有不可逆的高危命令**（clean / clean all / delete / format / convert / offline）。
   按规范 §2.9 的三级风险，这些一律用 `execution: info`：**宿主不执行**，
   只把命令拼好、解释清楚，由用户自己在终端里回车。

## 环境与前置条件（实测）

| 项 | 实测结果 |
|---|---|
| 程序 | `C:\Windows\System32\diskpart.exe`（Windows 11 26200 自带） |
| **是否需要管理员** | **需要**。官方主页明确写着 "You must be in your local Administrators group, or a group with similar permissions, to run diskpart." |
| 本机非管理员下的实测 | `diskpart /s probe.txt`（脚本只有 `list disk` + `exit`）→ 进程启动即失败：`请求的操作需要提升`（Program 'diskpart.exe' failed to run ... 请求的操作需要提升）。**退出码取不到，因为没有真正跑起来** |

因此本机（非管理员）**无法验证"选择之后解锁"的成功路径**。下面写清楚哪些验过、哪些没验。

## 已实测（界面验收，2026-09-29）

用 UIA 读界面（`AllTool.exe` 调试构建）：

| 检查项 | 结果 |
|---|---|
| 工具包出现在左栏且带 🔴 标记 | ✅ 显示为 `🔴 DiskPart`（kind: dangerous） |
| 会话状态条 | ✅ 显示"会话状态（后续命令的作用对象）：未开始（没有前置条件被满足）" |
| `list-partition`（requires: disk） | ✅ 「执行」按钮**置灰**；主面板显示"⚠ 现在还不能执行：（暂时不可用）先执行「选择磁盘」……" |
| `select-disk`（无 requires） | ✅ 「执行」按钮**可点**（对照组，证明不是一律禁用） |
| `clean`（requires: disk + `execution: info`） | ✅ 没有普通「执行」按钮，改为"（此动作不直接执行）"；显示"这一步该由你自己按下回车""命令拼好放在下面"；同时因为 requires 未满足也标了原因 |
| 会话状态在换工具包时清空 | 代码路径已在 `SelectPackage` 里（`!ReferenceEquals(...)` 时 `Clear()`），未做界面级对比 |

**没验到的**（本机做不到，需要管理员）：
- `select disk N` 成功后状态条变成"已选中磁盘 N"、`list partition` / `create partition` 自动恢复可用；
- `create partition primary` 的逐字确认短语在真机上走完并成功建立分区；
- `clean` 走"复制命令 + 打开空终端"这条 info 具体路径（界面元素已存在，未在真机点过）。

**要验这些**：用「文件 → 以管理员身份重新启动」，然后在 `select disk` 里选一块**你自己的数据盘**
（不要选系统盘），再观察 `list partition` 是否解锁。**破坏性动作请继续留在 info 级别使用**——
本工具包刻意不给它们提供"宿主替你执行"的路径。

## 参数知识的出处（R1/R2）

全部来自 Microsoft Learn 的 `windows-commands` 文档，逐条抓取核对（2026-09-29）：

- 主页面 `.../diskpart`：命令总表（每个子命令的一句话说明）、"需要管理员"的 Important 段、
  MBR/GPT 的说明。
- 子命令页（有独立页的）：`list`、`create`、`delete`、`select`、`detail`、`clean`、`format`、
  `assign`、`shrink`、`remove`、`active`、`rescan`、`online`、`offline`、`convert`、
  `attributes`、`uniqueid`、`filesystems`、`extend`、`create-partition-extended`。
- **没有独立页的命令**（`create partition primary`、`create partition logical`、`list disk`、
  `list partition`、`list vdisk`）：语法与位置参数取自 `create` 页的**官方示例**
  （`select disk <disk-number>` + `create partition primary size=1000`）与其参数表
  （`create partition logical command — Creates a logical partition in an existing extended partition`），
  页面上没有写的一律没写。

**一个差点踩的坑**：`.../expand` 是**文件解压命令**（expand.exe）的页面，不是 diskpart 的扩卷命令
——diskpart 里扩卷是 `extend`。写清单时用的是 `extend` 页。

## 会话状态的正则依据

| 状态 | 判定依据（成功输出） | 出处 |
|---|---|---|
| disk | `Disk N is now the selected disk` | `select` 页（Select disk — Shifts the focus to a disk）+ 实际输出格式 |
| volume | `Volume N is now the selected volume` | `select` 页 |
| partition | `Partition N is now the selected partition` | `select` 页 |

`errorPattern` 覆盖 `Virtual Disk Service error`、`DiskPart has encountered an error`、
`The specified/selected ... is not`、`There is no ... selected`、`The disk you specified is not valid`
——命中就**不确立任何状态**（宁可让用户重选，也不要在"其实没选上"的状态下继续）。

> ⚠️ 这几个正则**没有在真机上验证过**（需要管理员才能跑起来）。第一次提权使用后，
> 请把 `select disk` 的真实输出贴进 `NOTES.md` 核对一次；如果格式不同，改这里的 `successPattern`。

## 12. 实测反馈修掉的 bug：状态被"不该写状态的动作"覆盖

**产品负责人的现象**：`select disk 0` 之后 `list partition` 成功，但之后别的动作失败、
会话状态从「已选中磁盘 0」变成「**已选中磁盘 yes**」。

**真因**（不是 exit 的问题，也不是脚本重放的问题）：

`SessionStateUpdater` 原来是这样写的——

```csharp
foreach (var spec in session.State) { state.Establish(spec.Key, ...); return true; }  // 取第一个状态
```

于是**任何执行成功的动作**都会去写**第一个**状态（也就是 `disk`）：

| 动作 | 它声明了什么 | 旧行为 | 后果 |
|---|---|---|---|
| `select disk 0` | `establishes: disk` | 写 disk = 0（对） | 正常 |
| `list partition` | 什么都没声明 | **也去写 disk** → 它的字段里没有 `index`，取不到值就回落成 `yes` | 状态被覆盖成 yes |
| `select volume 3` | `establishes: volume` | **也去写 disk** | volume 没建立、disk 反而被污染 |

**修法**：`Apply` 增加 `establishes` 参数——**只有声明了 `establishes` 的动作才写状态，
而且只写它声明的那一个**；没声明的动作（list / detail 这类）**什么都不写**。
回归测试三条（`SessionTests`）：不声明的不污染已有状态、只写自己声明的那个、连续只读动作后状态仍在。

## 13. 还没核实的一件事：中文错误文案

`errorPatterns` 目前只有英文（`Virtual Disk Service error` 等）。本机是中文系统，
diskpart 的错误提示是中文，所以**失败判定在中文系统上偏弱**：

- 状态值不受影响（现在从字段值取，不解析输出）；
- 但"选了一个不存在的磁盘"这种情况，可能仍然把状态建立起来。

**要补上只需一步**（需要管理员）：

```powershell
# 在管理员 PowerShell 里跑，把输出贴回本文件
diskpart /s <(Set-Content -PassThru "$env:TEMP\dp.txt" "select disk 9999`r`nexit")   # 故意选不存在的磁盘
# 或者更直接：
$s="$env:TEMP\dp.txt"; Set-Content $s "list disk`r`nselect disk 9999`r`nexit" -Encoding ascii; diskpart /s $s
```

把 `select disk 9999` 的中文报错原文抄进 `errorPatterns` 即可（一条正则）。
本次尝试提权时 UAC 未在 60 秒内确认，所以没有拿到真实文案——**没有编造中文模式**。

## 14. 中文系统的失败判定（已补齐，2026-09-30）

第 13 节留的尾巴已补上。产品负责人用**管理员 PowerShell** 实跑：

```powershell
$s="$env:TEMP\dp.txt"; Set-Content $s "select disk 9999`r`nexit" -Encoding ascii; diskpart /s $s
```

真实输出（原样抄录）：

```
Microsoft DiskPart 版本 10.0.26100.1150

Copyright (C) Microsoft Corporation.
在计算机上: <机器名>

你指定的磁盘无效。

没有选择磁盘。
```

于是 `errorPatterns` 现在是**两种语言各一条**：

| 语言 | 模式 |
|---|---|
| 英文 | `Virtual Disk Service error` / `DiskPart has encountered an error` / `The ... is not` / `There is no ... selected` / `The disk you specified is not valid` |
| 中文 | `你指定的磁盘无效` / `没有选择磁盘` / `指定的…无效` / `未选择…`（**实测原文**，不是猜的） |

**顺带印证了一件事**：这条输出同时确认了"本地化"这个根因——
中文系统上 diskpart 的提示**全是中文**，所以早期那版"靠英文 `successPattern` 判断成功"的实现
在任何中文机器上都不可能生效（这正是"select 之后依赖动作仍灰显"的真因之一）。
现在成功判定只看"有没有命中 errorPattern"，与语言无关；状态值从字段值取，也与语言无关。
