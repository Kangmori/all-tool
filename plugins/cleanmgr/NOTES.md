# cleanmgr 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\cleanmgr.exe`（文件版本 10.0.26100.8521）—— Windows 自带。
> 清单：动作 7 个 / 字段 7 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `<机器名>\<用户名>`（非管理员）。

---

## 1. 参数知识来源（本包有一个必须先说的性质）

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cleanmgr` → 快照 `docs/reference/win-docs/cleanmgr.html` |
| 2 | 补充文档 | `https://learn.microsoft.com/en-us/troubleshoot/windows-server/backup-and-storage/automating-disk-cleanup-tool` → 快照 `docs/reference/win-docs/cleanmgr-aux.html`（讲 `/sageset` + `/sagerun` 的配合方式） |
| — | 本机 `cleanmgr /?` | **没有可用的控制台帮助**（见 §1.1） |

### 1.1 `cleanmgr` 是 GUI 程序，`/?` 不产生任何控制台输出

实测 `cleanmgr /?`：

- **stdout 0 字节、stderr 0 字节**
- 进程**不退出** —— 它弹出一个 Windows 对话框（超时瞬间抓到的窗口标题是 **`USAGE`**）

**这件事让我的抓取脚本挂了 300 秒。** 第一版 `scripts/fetch-win-help.ps1` 没有超时保护，
`cleanmgr /?` 一直等用户点「确定」，脚本就一直在等它，最后被 harness 挪进后台才发现。
现在的脚本给每个命令加了 8 秒超时，超时后先抓 `MainWindowTitle` 当证据、再 `Kill(true)`。

**所以本包是唯一一个"没能用本机 `/?` 核对开关"的工具包**（`sfc` 是第二个，
但那个至少还吐了一句话）。本清单的 8 个开关**全部只能依据官方文档**，如实记录。
好在 cleanmgr 的参数表很短（9 行），官方文档覆盖完整。

### 1.2 它也真的会删文件

`/sagerun`、`/tuneup`、`/lowdisk`、`/verylowdisk`、`/autoclean` 都会**真的删除磁盘文件**。
所以本包有 **5 个 `destructive` 动作**（本批最多的一个包），全部未实跑。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `sageset` | 交互设置 | `/sageset:n` | 1（枚举 n） | — | 否 |
| `sagerun` | 执行清理 | `/sagerun:n` | 1（枚举 n） | **destructive** | 否 |
| `tuneup` | 执行清理 | `/TUNEUP:n` | 1（枚举 n） | **destructive** | 否 |
| `lowdisk` | 自动清理 | `/LOWDISK` | 1 | **destructive** | 否 |
| `verylowdisk` | 自动清理 | `/VERYLOWDISK` | 1 | **destructive** | 否 |
| `autoclean` | 自动清理 | `/autoclean` | 1 | **destructive** | 否 |
| `drive-report` | 预览 | `/d <盘符>` | 1（枚举盘符） | — | 否 |

**没有任何动作标 `requiresAdmin`**：官方文档没有写 cleanmgr 需要提权，
本机也没有实测依据（所有动作都没跑）。这一点值得注意 ——
**清理系统盘往往需要管理员**，但既然没有依据就不标（R1 的精神）。
这是一个"保守到可能漏提示"的取舍，见 §9。

**`sagerun` 刻意没有 `/d` 字段**：官方文档明确写 `/d` 不与 `/sagerun` 一起使用
（"/d option isn't utilized with /sagerun:n"）。放一个填了不起作用的字段，
是"发明参数"的变体，所以不放。

## 3. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | stderr | 结果 |
|---|---|---|---|---|
| `cleanmgr /?` | **无（超时被杀）** | **0 B** | **0 B** | 弹出对话框（标题 `USAGE`），8 秒超时后强杀 |

**这就是全部实测结果 —— 本包没有成功跑过任何一条命令。** 原因是两条叠加：

1. 它的每个开关要么弹 GUI（没有控制台输出），要么真的删文件（安全红线）；
2. `/?` 本身也是 GUI 对话框。

`exitCodes` 里只写了 0 与 1 两条，并在 `meaning` 里**明确标注"含义未实测（官方无文档、
GUI 程序的控制台退出码不可靠）"**。这是本批唯一一个退出码表没有实测支撑的包。

**微软的 `cleanmgr` 文档没有退出码章节**（已核对）。

## 4. 编码

`runtime.encoding: oem` —— 但**实际上不起作用**：cleanmgr 没有控制台输出。
写 `oem` 只是与本批其它包保持一致（若将来发现它有输出，OEM 是最可能的编码）。
这一点在清单注释里写明了。

## 5. 本包特有的坑

1. **它是 GUI 程序**：控制台里什么都看不到，界面只能靠"命令已结束 + 退出码"判断。
   清单里每个动作的 `resultNote` 都写了这一点。
2. **`/?` 会让脚本永久挂住**（实测 300 秒）。抓帮助必须加超时。
3. **`/sageset:n` 与 `/sagerun:n` 是配套的**：先用 `/sageset:n` 弹出对话框勾选并保存到编号 n，
   再用 `/sagerun:n` 按该编号执行。**`/sagerun:n` 单独用没有任何效果**（编号不存在时它什么都不做）。
   这一点来自补充文档 `cleanmgr-aux.html`，清单的 `help` 里写明了。
4. **`/sagerun:n` 会作用于所有驱动器**（官方文档：All drives on the computer are enumerated
   and the selected profile runs against every drive）。
5. **`/verylowdisk` 完全没有用户提示** —— 它本来就是给无人值守脚本用的。
   正因为如此它最不该被随手点，`confirmText` 写得比较重。
6. **`/autoclean` 会删掉 Windows 升级残留**（通常就是旧的 Windows 安装），
   删了之后**无法回退到升级前的系统**。`confirmText` 里点明了这一点。
7. **`/autoclean` 只出现在官方参数表里，不在语法行里**（官方页面本身就不一致）。
8. **`/d` 的语法是 `/d <盘符>`（两个 token）**，不是 `/d<盘符>`。

## 6. `/?` 与真实输出的实测形态

- `cleanmgr /?`：**stdout 0 B、stderr 0 B、进程不退出**，超时强杀时窗口标题 `USAGE`。
- 官方语法行：`cleanmgr [/d <driveletter>] [/sageset:n] [/sagerun:n] [/TUNEUP:n] [/LOWDISK] [/VERYLOWDISK]`
- 官方参数表 9 行：`/d <driveletter>` `/sageset:n` `/sagerun:n` `/tuneup:n` `/lowdisk`
  `/verylowdisk` `/autoclean` `/?` —— **本清单收录了除 `/?` 之外的全部 7 个**（`/autoclean` 也在内）。
- 官方示例 3 条：`cleanmgr /sageset:1`、`cleanmgr /sagerun:1`、`cleanmgr /tuneup:1`。
- 补充文档 `cleanmgr-aux.html` 解释了 `/sageset` + `/sagerun` 的配套用法，是本包最有价值的一篇文档。

## 7. 故意没做的部分与原因

- **`/?`**：不做（它是帮助，不是功能）。
- **没有把 `/sageset:n` 的编号做成自由输入以外的形式**：编号现在是 `type: number` 自由输入。
  曾一度用 `literal` 枚举把 n 限制在 1/2/3/4/5/10，那是**过度保守**（见 §9.1），已改回自由输入。
- 没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`
  （**它连控制台输出都没有，进度只能靠"不确定"转圈**）；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

| 动作 | 命令 | 未跑原因 |
|---|---|---|
| `sageset` | `cleanmgr /sageset:1` | 弹 GUI 对话框，**不能自动化**（会卡住会话） |
| `sagerun` | `cleanmgr /sagerun:1` | **真的删除文件**，且编号 1 未必存在（跑了也可能看不到效果） |
| `tuneup` | `cleanmgr /tuneup:1` | 先弹对话框再**真的删除文件** |
| `lowdisk` | `cleanmgr /LOWDISK` | 用默认设置**真的删除文件**（所有驱动器） |
| `verylowdisk` | `cleanmgr /VERYLOWDISK` | **真的删除文件且无任何提示** —— 本批最危险的单个动作之一 |
| `autoclean` | `cleanmgr /autoclean` | **删除 Windows 升级残留，删了无法回退系统** |
| `drive-report` | `cleanmgr /d C:` | 弹 GUI 对话框（虽然是只读的，但同样卡住会话） |

**本包 7 个动作全部未实跑**，`examples` 里 3 条也全部标注"官方示例，未实跑"，
且**没有写 `expectExitCode`**（不猜测）。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及两条：

1. **【已解决，但值得记住我一开始判断错了】`/sageset:n` / `/sagerun:n` / `/TUNEUP:n`
   要求"开关字面量 + 用户输入的数字"在同一个 argv token 里。**
   - 我最初的判断是"规范 v1 做不到"，于是用 `literal` 把整个 token 枚举成 1/2/3/4/5/10，
     编号范围被枚举死。
   - **这个判断是错的。** `style: attached` + `prefix: "/sageset:"`（**prefix 末尾带冒号**）
     + `separator: ""` + 值为 `1`，`ArgvBuilder` 的 non-separate 分支是
     `prefix + separator + value`，生成的正好是单个 token **`/sageset:1`**。
   - 现在清单用的是这种写法，编号是 `type: number` 自由输入。
   - **教训**：`separator` 不是只能取 `""` / `" "` / `"="` 三种"语法意义"上的分隔；
     `prefix` 里可以直接带标点（冒号、等号、斜杠），从而表达 `/xxx:yyy` 这类形态。
     写清单时**先把 `prefix` 想成"任意字面前缀"**，很多"规范表达不了"的判断就不成立了。
   - **仍然真实存在的缺口**（`sfc` 的 `/verifyfile=<file>`）：那里需要
     `prefix="/verifyfile="` + 用户填的**任意路径**，`attached` 同样能生成
     `/verifyfile=C:\…\kernel32.dll` 单个 token —— 但 sfc 实测接受
     `["/verifyfile=", "C:\\…\\kernel32.dll"]` 这种**两个 token**的形态，
     所以清单现在用的是 `literal + positional` 的保守写法。
     两条路都能走通，**建议统一成 `attached` + 带标点的 prefix**（更贴合官方语法，显示也更清楚）。
2. **GUI 程序在 v1 里无法表达"这个动作会弹窗"**：`output.mode` 只有
   `stream` / `capture` / `progress`，没有"GUI/无控制台输出"这一类。
   本包靠 `resultNote` 用文字提醒，属于权宜之计。建议加一个 `output.mode: gui`
   或者在功能上支持"执行后立即返回、不等子进程"（GUI 程序通常需要这个）。

## 10. 给后来者的提醒

- **别用 `cleanmgr` 做自动化冒烟的样本**：它会弹窗并永久等待。
- `/sagerun:n` 单独用没有效果，必须先 `/sageset:n`。
- `/verylowdisk` 与 `/autoclean` 是本批风险最高的两个动作，改它们的文案时请保持"重"。
- 编号字段已经是自由输入（`type: number` + `attached` + `prefix: "/sageset:"`），
  生成单个 token `/sageset:1`。**但这三条命令都没法实跑**（GUI / 会删文件），
  所以"生成的 argv 形态正确"只有代码层面的依据，没有真机验证 —— 如果哪天能安全实跑，
  请优先补这一条。
- `sfc` 的 `/verifyfile=` 建议也统一成同样的写法（见 §9.1 末尾）。
