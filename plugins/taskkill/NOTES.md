# taskkill（结束进程）—— 实测记录

> 产品负责人的规则：**系统已有称手图形界面的命令不做**。
> 任务管理器能"列进程 + 结束"，所以本包**不做**那一套；
> 只收图形界面**做不到**的三件事：一次结束一批（/IM 或 /FI）、强制结束卡死进程（/F）、连子进程（/T）。

## 1. 实测（本机 Windows 11 build 26200，非管理员）

| 命令行 | 退出码 | 输出 |
|---|---|---|
| `taskkill /?` | **0** | 用法（纯 ASCII） |
| `taskkill`（无参数） | **1** | `ERROR: Invalid syntax. Neither /FI nor /PID nor /IM were specified.` |
| `taskkill /PID <自己起的 ping>`（**不带 /F**） | **1** | `ERROR: The process with PID … could not be terminated. Reason: This process can only be terminated forcibly` |
| `taskkill /F /T /PID <自己起的 ping>` | **0** | `SUCCESS: The process with PID … (child process of PID …) has been terminated.` |
| `taskkill /PID 999999`（不存在） | **128** | `ERROR: The process "999999" not found.` |
| `taskkill /IM nosuchapp.exe` | **128** | `ERROR: The process "nosuchapp.exe" not found.` |
| `taskkill /FI "IMAGENAME eq nosuchapp.exe"` | **0** | `INFO: No tasks running with the specified criteria.` |

**两条关键结论**：
1. **不带 `/F` 结束控制台程序会失败**（只能强制结束）→ 所以 `/F` 默认开启，并在字段 help 里写明理由。
2. **`/FI` 无匹配时退出码仍是 0** → 与其它 Windows 命令一样，"0" 只表示命令被接受，不代表有结果。

## 2. 编码为何写 oem
本机 taskkill 的输出**全是 ASCII**（帮助与错误消息都是英文），**没有中文样本可判**。
按同族 Windows 控制台命令的实测惯例（tasklist/ipconfig 等都是 oem）写 `oem`，
并在此如实说明：**这一条不是实测得出，而是按同族惯例推定**。

## 3. requiresAdmin 没标
非管理员实测可以结束**自己启动的**进程（上表第 4 行退出码 0）。
结束系统进程或别的用户的进程才需要提权 —— 这取决于用户选的目标，按项目惯例「不确定就不标」，
在 description 里说明即可。

## 4. 靶子都是自己起的
所有"结束"实验的靶子都是本进程自己启动的 `ping -n 120 127.0.0.1`，
**没有碰任何他人的进程**；实验的残留（第一条没杀掉的那个 ping）已在写包前用 `/F` 清掉。
