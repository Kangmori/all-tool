# chkdsk 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\chkdsk.exe`（文件版本 10.0.26100.1150）—— Windows 自带。
> 清单：动作 5 个 / 字段 11 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `<机器名>\<用户名>`（**非管理员**）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/chkdsk` → 快照 `docs/reference/win-docs/chkdsk.html` |
| 2 | 本机 `chkdsk /?` | 快照 `docs/reference/win-help/chkdsk.txt`（2089 字节，写 stdout，**退出码 3**，不入库） |

**开关是拿本机 `/?` 核对过的**：`/F` `/V` `/R` `/L` `/X` `/I` `/C` `/B` `/scan`
`/forceofflinefix` `/perf` `/spotfix` `/sdcleanup` `/offlinescanandfix`
`/freeorphanedchains` `/markclean` —— 本机 `/?` 的 16 条与官方参数表**逐条对得上**。

**这是本批里唯一一个有官方退出码表的命令**（官方 `#understanding-exit-codes` 一节）。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `check` | 只读检查 | （无） | 1（卷，可留空） | — | 否 |
| `fix` | 修复 | `/f` | 2 | overwrite | **是** |
| `recover` | 修复 | `/r` | 2 | overwrite | **是** |
| `force-dismount` | 修复 | `/x` | 2 | **destructive** | **是** |
| `scan` | 联机扫描 | `/scan` `/forceofflinefix` `/perf` | 4 | — | **是** |

**只有 `check` 没标 `requiresAdmin`**，理由是：只读检查本身没有"需要提权"的语义，
它失败是因为当前账号权限不足（见 §3）—— 标上 `requiresAdmin` 会把"这个动作本身需要提权"
和"这个账号权限不够"混为一谈。这个取舍本身值得产品负责人确认（见 §9）。

## 3. 真机冒烟测试结果（含退出码）—— 本包最重要的一节

| 命令 | 退出码 | 输出 |
|---|---|---|
| `chkdsk`（只读，无参数） | **3** | 136 B：`访问被拒绝，因为你没有足够的权限，或该磁盘可能被另一个进程锁定。你必须调用这一在提升模式下运行的实用工具并确保磁盘处于解锁状态。` |
| `chkdsk /?` | **3** | 2089 B 帮助（**注意帮助本身也返回 3**） |

**结论：在当前非管理员账号下，本工具包的每一个动作都跑不通。**

- 只读的 `chkdsk` 就已经因为权限被拒（退出码 3）；
- `/f` `/r` `/x` 等会改盘的动作**按安全红线一律未跑**（见 §8）；
- 官方 `#understanding-exit-codes` 一节写明：**必须是管理员，且必须从提升的命令提示符里运行**。

所以这个工具包在界面上**必须靠 `requiresAdmin` 的提示告诉用户"先以管理员身份重启"** ——
这也是本次任务把 `requiresAdmin` 用起来的最强动机。宿主实测行为见共用文档 §7.5。

**退出码表（官方文档，不是实测推断）**：

| 码 | 官方含义 | 本清单 severity |
|---|---|---|
| 0 | No errors were found. | ok |
| 1 | Errors were found and fixed. | warning |
| 2 | Performed disk cleanup (such as garbage collection) or didn't perform cleanup because /f was not specified. | warning |
| 3 | Could not check the disk, errors could not be fixed, or errors were not fixed because /f was not specified. | error |

四条全部收录，且 `meaning` 里保留了官方措辞的翻译与"实测当前非管理员账号下运行只读 chkdsk
就返回 3"这一实测补充。

## 4. 编码

stdout 实测 **OEM 代码页 936**（2089 B 帮助与 136 B 的权限错误都是），`runtime.encoding: oem`。

## 5. 本包特有的坑

1. **`/?` 自身返回 3**。这是本批 12 个命令里最反直觉的一个 —— 一个"显示帮助"的操作
   返回了"无法检查磁盘"的错误码。写自动化脚本时不要用 `chkdsk /?` 来"探活"。
2. **只读检查也需要管理员**（实测）。所以"不带开关就是安全的只读操作"这个直觉在 chkdsk 上不成立。
3. **系统卷通常无法立即锁定**：官方文档说 `/f` 如果锁不住盘，会提示"是否在下次重启时检查"，
   那意味着**重启时才真的动手**。`confirmText` 里点明了这一点。
4. **`/r` 隐含 `/f`，`/x` 隐含 `/f`，`/b` 隐含 `/r`** —— 官方文档逐条标注。
   所以"只做 /r 不做 /f"是不可能的。
5. **`/scan` 是"联机扫描"**：卷保持挂载，找到的问题默认排队等脱机修复
   （配 `/forceofflinefix` 强制全部排队）。
6. **`/perf` 与 `/forceofflinefix` 必须与 `/scan` 一起使用**（官方文档与 `/?` 都写了）。
   本清单把这三个放在同一个动作里，所以组合天然成立。
7. **`/l[:size]`（改日志文件大小）没有做成字段**：它是"可选带值"的开关
   （`/l` 单独用显示当前大小，`/l:size` 改大小），要表达需要"带可选值的 flag"，
   规范 v1 没有这种语义。见 §7。
8. **`/spotfix`（点修复）与 `/offlinescanandfix`（脱机扫描并修复）没有做成动作**：
   它们通常由 `/scan` 之后的流程驱动，单独暴露给用户容易误用（都隐含改盘）。见 §7。

## 6. `/?` 与真实输出的实测形态

- `chkdsk /?`：**stdout** 2089 B、stderr 0 B、**退出码 3**。
- 用法行：`CHKDSK [volume[[path]filename]]] [/F] [/V] [/R] [/X] [/I] [/C] [/L[:size]] [/B] [/scan] [/spotfix] [/?] …`
  （本机 `/?` 与官方语法行一致，官方多了 `/forceofflinefix` `/perf` `/sdcleanup`
  `/offlinescanandfix` `/freeorphanedchains` `/markclean` 的说明，本机 `/?` 也都有）
- 本机 `/?` 的一处排版小瑕疵：`CHKDSK [volume[[path]filename]]]` 有**三个右方括号**
  （官方页面写的是 `[<volume>[[<path>]<filename>]]`，两个）。不影响语义。
- 官方页面另有 `#how-chkdsk-performs-on-different-media` 与 `#viewing-chkdsk-logs` 两节
  （讲不同介质上的行为差异、以及怎么从事件日志里看 chkdsk 的结果）。
  后者给出了 PowerShell 取日志的办法（`get-winevent -FilterHashTable @{logname="Application"} …`）。

## 7. 故意没做的部分与原因

| 开关 | 为什么不做 |
|---|---|
| `/l[:size]` | "带可选值的开关"（`/l` vs `/l:size`）v1 表达不了；且改日志文件大小是极冷门操作 |
| `/spotfix` | 点修复，通常由 `/scan` 流程驱动；单独暴露容易误用（会改盘） |
| `/offlinescanandfix` | 脱机扫描并修复，会改盘；同上 |
| `/sdcleanup` | 回收安全描述符数据，隐含 `/f`；极冷门 |
| `/i` `/c` | 减少检查强度（跳过某些检查）。**做出来等于让用户"检查得不彻底"**，没有正面价值 |
| `/b` | 重新评估坏簇，隐含 `/r`；与 `recover` 重叠 |
| `/v` | 显示每个文件的名字（FAT/FAT32）或清理消息（NTFS）。输出极长，价值低 |
| `/freeorphanedchains` `/markclean` | 仅 FAT/FAT32/exFAT；本机是 NTFS，无法实测 |

另外：没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`
（chkdsk 的进度是分阶段的百分比，重定向时格式不稳定，不凭空写正则）；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

| 动作 | 命令 | 未跑原因 |
|---|---|---|
| `fix` | `chkdsk <卷> /f` | **会修改文件系统**；且系统卷要重启后才执行 |
| `recover` | `chkdsk <卷> /r` | 逐扇区扫描，**可能数小时**，且隐含 `/f` |
| `force-dismount` | `chkdsk <卷> /x` | **强制卸除卷**，该卷所有打开句柄失效，正在用的程序可能报错或丢数据 |
| `scan` | `chkdsk <卷> /scan` | 会改盘（联机修复）；且需要管理员 |
| `check` 的带卷参数形态 | `chkdsk D:` | 只读但需要管理员；实测当前账号连默认卷都检查不了（exit 3） |

**唯一实跑的是**：`chkdsk`（无参数只读，退出码 3）与 `chkdsk /?`（退出码 3）。
两者都写进了 `examples`（`check` 的 example 带 `expectExitCode: 3`），
并在 NOTES 里注明"3 是权限不足导致的，不是文档语义上的'磁盘有问题'"。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及：

1. **`requiresAdmin` 的语义边界**：`check`（只读）该不该标？
   本包选择"不标"，因为只读检查本身不需要提权，失败是账号权限问题。
   但用户看到界面上没有警告、点下去又被"访问被拒绝"，体验并不好。
   建议宿主/规范考虑三态：`无需` / `建议提权` / `必须提权`，
   或者让 `check` 这类动作也标上 `requiresAdmin: true`（宁多提示不漏提示）。
   **这是需要产品负责人拍板的一条。**
2. **"带可选值的开关"（`/l[:size]`）v1 表达不了**：`flag` 不能带值，`attached` 又要求必须有值。
   建议给 `flag` 加一个可选的 `valueField`，或者接受"用两个动作表达"
   （`/l` 一个、`/l:size` 一个）这种笨办法。

## 10. 给后来者的提醒

- **别用 `chkdsk /?` 探活**（它返回 3）。
- **别以为不带开关就是安全的**（只读检查也需要管理员）。
- 想加 `/spotfix` / `/offlinescanandfix` 之前先想清楚"用户为什么会单独点它"。
