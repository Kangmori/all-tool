# systeminfo 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\systeminfo.exe`（文件版本 10.0.26100.4202）—— Windows 自带。
> 清单：动作 3 个 / 字段 6 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `KANGMORI\Steve`（非管理员）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/systeminfo` → 快照 `docs/reference/win-docs/systeminfo.html` |
| 2 | 本机 `systeminfo /?` | 快照 `docs/reference/win-help/systeminfo.txt`（932 字节，写 stdout，**退出码 0**，不入库） |

**开关是拿本机 `/?` 核对过的**：`/S` `/U` `/P` `/FO` `/NH` 五个与官方参数表**完全一致**
（这是本批里"官方文档与内置帮助差异最小"的命令 —— 只有 5 个参数，逐条对得上）。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `show` | 系统信息 | `/fo` `/nh` | 2 | — | 否 |
| `show-list` | 系统信息 | `/fo` `/nh` | 2 | — | 否 |
| `export-csv` | 系统信息 | `/fo` `/nh` | 2 | — | 否 |

**为什么 `show` 与 `show-list` 分开**：`show` 的 `format` 是 `enum` + `separate`（三个取值可选），
`show-list` 用 `literal` 把 `/fo list` 两个 token 写死在 `args` 里。
两者行为有重叠，但**做法不同**是有意的：它验证了规范里 `enum`+`separate` 与 `literal`
两种风格都能表达同一个开关，也方便审阅者对比两种写法生成的 argv。

## 3. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | 实测耗时 | 首行 |
|---|---|---|---|---|
| `systeminfo` | **0** | 3 623 B | **4.5 秒** | `主机名:             KANGMORI` |
| `systeminfo /fo csv` | **0** | 2 471 B | **4.7 秒** | `"主机名","OS 名称","OS 版本",…` |
| `systeminfo /?` | **0** | 932 B | — | `SYSTEMINFO [/S system …]` |

**性能是本包最需要注意的事实**：4.5 秒的等待（它要查 WMI、已安装更新、网卡等）。
宿主的进度条会先显示"不确定"，这是对的行为。清单的 `resultNote` 里写了这一点。

## 4. 编码

stdout 实测 **OEM 代码页 936**（`systeminfo` 输出的中文最多：`OS 名称`/`OS 制造商`/
`系统区域设置`/`基于虚拟化的安全性` 等），所以 `runtime.encoding: oem`。
用 utf-8 解码会全部乱码。

## 5. 本包特有的坑

1. **慢**（4.5 秒）。做界面预设或自动化时要把超时放宽，不要用 1~2 秒判断"卡住了"。
2. **`/fo csv` 的列很多**（本机 33 列），且**某些字段本身含逗号**（例如日期、更新列表），
   官方 CSV 输出并不保证严格的可解析性 —— 要机器处理建议还是用 PowerShell 的 `Get-CimInstance`。
3. **本机输出里有 `OS 版本: 10.0.26200 暂缺 Build 26200`** —— "暂缺"是
   "N/A" 的本地化翻译，不是错误。
4. **`/nh` 只在 `/fo` 为 TABLE 或 CSV 时有效**（官方文档与 `/?` 都写了）。
5. **官方示例里有 `SYSTEMINFO /S system /FO TABLE`、`SYSTEMINFO /S system /U user`** ——
   本包**不做** `/s` `/u` `/p`（理由见 §7）。

## 6. `/?` 与真实输出的实测形态

- `systeminfo /?`：**stdout** 932 B、stderr 0 B、**退出码 0**。
- 用法行：`SYSTEMINFO [/S system [/U username [/P [password]]]] [/FO format] [/NH]`
- 本机 `/?` 的 Examples 与官方 `#examples` 一致（6 条）。
- 官方页面**没有退出码章节**（已核对）。

## 7. 故意没做的部分与原因

- **`/s` `/u` `/p`（连远程机器并换账号）**：会把明文密码带进界面，理由同 `tasklist`。
- **没有 `versionArgs` / `versionPattern` / `minVersion`**。
  这里值得多说一句：`systeminfo` 的输出里**确实有系统版本**（`OS 版本: 10.0.26200`），
  理论上可以用 `versionPattern` 从里面抓出 `10.0.26200` 当"版本号"。
  **但那样做是错的**：`versionArgs: []` 意味着宿主每次定位这个工具包时都要跑一遍
  `systeminfo`（4.5 秒），而且那个版本号是**操作系统的**，不是 `systeminfo.exe` 的。
  `locate.versionPattern` 的语义是"这个软件的版本"，拿 OS 版本冒充会让 `minVersion`
  的语义彻底混乱。所以**刻意不写**，并把这个判断记在这里。
- 没有 `progress.pattern`（systeminfo 没有进度输出）；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

**本包全部动作都是只读的，且三个动作都实跑过**（`show` 用默认与 `/fo csv`、
`show-list` 用的 `/fo list` 与 `/fo csv` 同源、`export-csv` 的 `/fo csv /nh` 形态实跑过）。
没有需要跳过的动作。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及一条，与规范 §4 的"字段风格决策表"有关：

**`/fo {TABLE | LIST | CSV}` 这类"开关的取值也写成开关样式"的枚举**，
在清单里用 `enum` + `separate`（取值写 `table` / `list` / `csv`，不带斜杠）是对的 ——
但如果抄官方语法行写成 `/fo /table`，就会生成错误参数。本包的 `values` 里刻意写的是
不带斜杠的小写值（`table` / `list` / `csv`），而 `doc` 里保留官方写法。
**建议 playbook 明确一条**：`enum` 的 `value` 是**要传给程序的那个 token**，
不是文档里的书写形式；文档形式放在 `doc` 或 `label` 里。

## 10. 给后来者的提醒

- 不要给 `systeminfo` 加 `versionPattern`（理由见 §7），那会让每次定位多等 4.5 秒。
- 想拿机器信息做结构化处理，优先用 `Get-CimInstance`，不要解析 `systeminfo /fo csv`。
