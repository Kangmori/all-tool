# tasklist 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\tasklist.exe`（文件版本 10.0.26100.1）—— Windows 自带。
> 清单：动作 5 个 / 字段 11 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `KANGMORI\Steve`（非管理员）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tasklist` → 快照 `docs/reference/win-docs/tasklist.html` |
| 2 | 本机 `tasklist /?` | 快照 `docs/reference/win-help/tasklist.txt`（2690 字节，**写 stdout**，**退出码 0**，不入库） |

**开关是拿本机 `/?` 核对过的**：`/S` `/U` `/P` `/M` `/SVC` `/V` `/FI` `/FO` `/NH`
与官方参数表逐条对上。

**一处来源冲突**：本机 `/?` 多列了一个 `/APPS`（"显示 Microsoft Store 应用及其关联的进程"），
官方页面的语法行与参数表里都没有 → **按官方优先，不收录**。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `list` | 进程列表 | `/fo` `/nh` | 2 | — | 否 |
| `list-verbose` | 进程列表 | `/v` `/svc` `/fo` | 3 | — | 否 |
| `list-services` | 服务映射 | `/svc` | 1 | — | 否 |
| `list-modules` | 进程列表 | `/m` | 2 | — | 否 |
| `filter` | 筛选 | `/fi` `/fo` | 3 | — | 否 |

**字段顺序有两处是刻意的**（字段顺序 = argv 顺序）：
`list-modules` 里 `byModule` 声明在 `module` **之前**（否则会拼出 `tasklist wbem* /m`）；
`filter` 里 `byFilter` 声明在 `filter` **之前**。这两处清单里都有注释。

## 3. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | 实测输出首行 |
|---|---|---|---|
| `tasklist` | **0** | 19 424 B | `映像名称                       PID 会话名              会话#       内存使用` |
| `tasklist /svc` | **0** | 20 252 B | 同上，第三列变成 `服务` |
| `tasklist /fo csv /nh` | **0** | 12 472 B | `"System Idle Process","0","Services","0","8 K"` |
| `tasklist /v`（探针跑过） | **0** | **57 983 B** | 含状态、用户名、CPU 时间、窗口标题 |
| `tasklist /?` | **0** | 2690 B | `TASKLIST [/S system [/U username [/P [password]]]]` |

**注意 `tasklist /?` 的退出码是 0** —— 本批里 `ipconfig` / `tracert` / `netstat` / `nslookup`
的 `/?` 都返回 1，`tasklist` 与 `systeminfo` / `powercfg` 返回 0。**不能对"帮助的退出码"做统一假设。**

## 4. 编码

stdout 实测 **OEM 代码页 936**，`runtime.encoding: oem`。注意 `/v` 输出的 58 KB 里
用户名与窗口标题都可能含中文（例如"暂缺"），解码正确性依赖 `oem`。

## 5. 本包特有的坑

1. **`/fi` 的筛选表达式含空格，必须加引号**。官方示例：`tasklist /fi "USERNAME ne NT AUTHORITY\SYSTEM"`。
   宿主会自动给含空格的 token 加引号（`ArgvBuilder.FormatForDisplay` 的 `Quote`），
   所以界面上预览能看到正确的带引号形态。
2. **一次只能填一条 `/fi`**：官方支持多个 `/fi`（它们之间是"与"关系），
   但规范 v1 表达不了"每行生成一个 `/fi 值`"这种**开关与取值成对重复**的结构
   （`repeatable` 只对同一个字段的多个值生成多个 token，不会为每个值补一个前缀）。
   本清单把 `filter` 限制成单值，并在 `help` 里写明。**这是规范缺口，见 §9。**
3. **不带模块名的 `/m` 会列出所有已加载模块**，输出极大。所以 `module` 字段是可选的但
   `help` 里警告了这一点。
4. **筛选器名称大小写**：官方参数表里写 `CPUtime`（注意大小写），本机 `/?` 写 `CPUTIME`。
   实际匹配不区分大小写（tasklist 自己做的），清单的 `doc` 保留了官方写法。
5. **`/fo` 的取值不带斜杠也不带引号即可**（`table` / `list` / `csv`），
   官方写法是 `/fo {table | list | csv}`，本清单用 `separate` 生成两个 token：`/fo` + `csv`。
6. **`/nh` 只在 `/fo` 为 `table` 或 `csv` 时有效**（官方文档与 `/?` 都写了）。

## 6. `/?` 与真实输出的实测形态

- `tasklist /?`：**stdout** 2690 B、stderr 0 B、**退出码 0**。
- 用法行：`TASKLIST [/S system [/U username [/P [password]]]] [{/m <module> | /svc | /v}] [/fo {table | list | csv}] [/nh] [/fi <filter> [/fi <filter> [ ... ]]]`
- 本机 `/?` 有 9 条 Examples，其中 `TASKLIST /APPS /FI "STATUS eq RUNNING"` 用到了官方没有的 `/APPS`。
- 筛选器表（官方 `#parameters`，共 11 行）：`STATUS` `IMAGENAME` `PID` `SESSION` `SESSIONNAME`
  `CPUtime` `MEMUSAGE` `USERNAME` `SERVICES` `WINDOWTITLE` `MODULES`，
  运算符 `eq` `ne` `gt` `lt` `ge` `le`（后四个只对部分筛选器有效）。
- 官方注明：`WINDOWTITLE` 与 `STATUS` 在查询远程计算机时**不支持**（本包不做远程，所以无影响）。

## 7. 故意没做的部分与原因

- **`/s` `/u` `/p`（连远程机器并换账号）**：会把**明文密码**带进界面。
  本项目的界面虽然对 `type: password` 做了"永不落盘"，但远程管理不是这个工具包要解决的问题，
  而且 `/u` 的写法要求 `域\用户名`，在界面上容易填错。**整包不做远程。**
- **`/APPS`**：只有本机 `/?` 有，官方页面没有（见 §1）。
- **多个 `/fi`**：规范表达不了（见 §5.2）。
- **`/m` 不带模块名**：输出太大且没有实际用途，所以 `module` 是可选字段但 `help` 里警告。
- 没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

**本包全部动作都是只读的**，且 `list` / `list-verbose` / `list-services` / `filter` 的等价命令
都实跑过（`/v`、`/svc`、`/fo csv /nh`）。没有需要跳过的动作。

**未逐一实跑的取值**：`filter` 的 11 个筛选器名称只实跑了官方示例里的
`STATUS eq running`（在冒烟脚本里用 `/fo csv /nh /fi "STATUS eq running"` 的形态）。
其余筛选器名称经官方表格核对，未逐一实跑。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及：

**"开关与取值成对重复"表达不了**：`tasklist /fi "A" /fi "B"` 这种结构，
v1 的 `repeatable` 无法表达（它让**同一个字段**产出多个 token，但没有"每个值都补一个前缀"的语义）。
`repeatable` + `style: flag` 的 `prefix` 是固定值，也不会随值变化；`positionalMode: perLine`
只能把一行拆成多个 token，同样不补前缀。
建议：给 `repeatable` 增加一个明确语义——"每个值按 `prefix` 与 `separator` 各自展开一次"
（对 `attached` / `separate` / `flag` 都适用），这样 `-x a -x b` 与 `/fi A /fi B` 都能表达。
**同类需求在 Windows 命令里很常见**（`robocopy /XD a /XD b`、`chkdsk` 没有、`netsh` 很多）。

## 10. 给后来者的提醒

- 加 `/fi` 的多条件支持之前，先看 §9 的规范缺口 —— 现在只能填一条。
- `tasklist /v` 输出 58 KB，做界面预设时注意。
- 别加 `/APPS`（官方页面没有）。
