# sfc 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\sfc.exe`（文件版本 10.0.26100.8521）—— Windows 自带。
> 清单：动作 3 个 / 字段 4 个 / 字段出处标注 **100%**
> **公共事实见 ``docs/ai/windows-commands.md``**。
> 实测环境：Windows 11 `10.0.26200`，账号 `<机器名>\<用户名>`（**非管理员**）。

---

## 1. 参数知识来源（本包有一处必须说清楚的例外）

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc` → 快照 `docs/reference/win-docs/sfc.html` |
| 2 | 本机 `sfc /?` | 快照 `docs/reference/win-help/sfc.txt` —— **68 字节，只有一句权限提示，没有任何参数表** |

### 1.1 本包**没能**用本机 `/?` 核对开关，原因如实记录

实测 `sfc /?` 的输出（UTF-16LE 解码后）**只有一句话**：

```
为了使用 sfc 工具，你必须作为管理员运行控制台会话。
```

- stdout 68 字节、stderr 0 字节、退出码 **1**；
- 它**不打印任何参数说明** —— `sfc` 根本不用 `/?` 表达帮助；
- 所以本清单的 4 个开关（`/scannow` `/verifyonly` `/scanfile=` `/verifyfile=`）
  **全部只能依据官方文档**，无法用本机 `/?` 交叉验证。

**这是"没有内置帮助、只能靠官方文档"的正面案例**，也是本次任务"用 `/?` 核对开关"这个要求
在 Windows 命令上并非普遍成立的一个反例。已如实记在这里，而不是假装核对过。

### 1.2 顺带：`sfc` 的**所有**输出都是 UTF-16LE

这一点比"没有帮助"更重要，见 §3。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `verifyonly` | 校验 | `/verifyonly` | 1 | — | **是** |
| `scannow` | 修复 | `/scannow` | 1 | overwrite | **是** |
| `verifyfile` | 单文件 | `/verifyfile=` 或 `/scanfile=` | 2 | — | **是** |

**三个动作全部标了 `requiresAdmin: true`**，依据是**实测**：
`sfc /verifyonly` 与 `sfc /verifyfile=…` 在非管理员下**都**返回退出码 1 并打印那句权限提示。
不是照文档推测，是四个动作里三个都亲手试过。

## 3. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | stderr | 输出（UTF-16LE 解码后） |
|---|---|---|---|---|
| `sfc /verifyonly` | **1** | 68 B | 0 B | `为了使用 sfc 工具，你必须作为管理员运行控制台会话。` |
| `sfc /verifyfile=C:\Windows\System32\kernel32.dll` | **1** | 68 B | 0 B | 同上 |
| `sfc /?` | **1** | 68 B | 0 B | 同上（`/?` 与不带 `/?` 的效果一样） |

**结论：在当前非管理员账号下，三个动作都跑不通。**

**关键实测发现：`sfc` 的输出编码是 UTF-16LE。** 原始字节：

```
0d 00 0d 00 0a 00 3a 4e 86 4e 7f 4f 28 75 20 00 73 00 66 00 63 00 20 00 …
```

- 按 UTF-16LE 解 → `为了使用 sfc 工具，你必须…`（正确）
- 按 cp936 解 → `\r \r \n :N哊O(u s f c 錧wQ…`（乱码）

于是本包的 `runtime.encoding` 必须是 **`utf-16le`**，**这是本批 12 个包里唯一一个非 OEM 的**。

**我在这里踩了一个坑（值得后来者记住）**：第一版编码判定逻辑是"ASCII 字符后面跟不跟 `00`"，
而这 68 字节里 `3a 4e`（「为」）、`86 4e`（「了」）、`7f 4f`（「使」）这些**汉字对的第二个字节
不是 0**，命中不了那条规则，于是 `sfc` 被误判成 cp936、快照文件里存的是乱码。
正确做法：按**字节长度是否为偶数 + 高字节 0 的比例（阈值 30%）**判断。
这个修法同时改进了 `scripts/fetch-win-help.ps1` 与 `scripts/probe-win-output.ps1`。

## 4. 编码

`runtime.encoding: utf-16le`（理由与字节证据见 §3）。
**注意**：这不代表 sfc 永远不输出别的编码 —— 但实测的四个输出（帮助、verifyonly、verifyfile、
以及 `/?`）全都是 UTF-16LE，所以就按 UTF-16LE 声明。

## 5. 本包特有的坑

1. **`sfc /?` 不是帮助**。想看参数必须去官方文档。
2. **输出是全 UTF-16LE 的**（含权限错误），与其它 11 个命令相反。
3. **`/scanfile` 与 `/verifyfile` 要求"开关与路径在同一个 argv token 里"**（`/verifyfile=<路径>`）。
   规范 v1 表达不了这个（见 §6.1），本清单的绕法是：
   `literal` 字段产出 `/verifyfile=` 这个**独立 token**，紧跟一个 `positional` 路径 token。
   生成的 argv 是 `["/verifyfile=", "C:\\…\\kernel32.dll"]`，
   Windows 命令行把它拼成 `sfc /verifyfile="C:\…\kernel32.dll"` —— **实测这个形态能被 sfc 接受**
   （它没报"参数不认识"，而是直接走到权限检查）。
4. **`/verifyfile` / `/scanfile` 官方页面写作 `/verifyfile <file>`，语法行写作 `/verifyfile=<file>`**
   —— 同一页里两种写法，实际要用等号形式。
5. **`/scannow` 耗时很长**（几分钟到十几分钟），且**会替换系统文件**。`confirmText` 里写明了。
6. **不要中断 `/scannow`**（官方页面没有明说，但这是常识级的风险；`confirmText` 里提醒了）。

## 6. `/?` 与真实输出的实测形态

- `sfc /?`：**stdout** 68 B（UTF-16LE）、stderr 0 B、**退出码 1**。内容只有权限提示。
- 官方语法行：
  `sfc [/scannow] [/verifyonly] [/scanfile=<file>] [/verifyfile=<file>] [/offwindir=<offline windows directory> /offbootdir=<offline boot directory> /offlogfile=<log file path>]`
- 官方参数表 9 行：`/scannow` `/verifyonly` `/scanfile <file>` `/verifyfile <file>`
  `/offwindir <…>` `/offbootdir <…>` `/offlogfile=<…>` `/?`（本清单收录前 4 个）。
- 官方示例 2 条：`sfc /verifyfile=c:\windows\system32\kernel32.dll`、
  `sfc /scanfile=D:\windows\system32\kernel32.dll /offbootdir=D:\ /offwindir=d:\windows`。

## 7. 故意没做的部分与原因

- **`/offwindir` `/offbootdir` `/offlogfile`（脱机修复）**：用于修复**离线的** Windows 安装
  （例如从 PE 里修另一块盘上的系统）。做成界面动作会被误用，而且需要挂载另一套系统目录。
- 没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`
  （sfc 的进度是分阶段百分比，实测在非管理员下只输出一句权限错误，拿不到进度格式）；
  没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

| 动作 | 命令 | 未跑原因 |
|---|---|---|
| `scannow` | `sfc /scannow` | **会替换系统文件**；耗时长；需要管理员 |
| `verifyfile` 的 `scanfile` 取值 | `sfc /scanfile=<file>` | 会修复该文件；需要管理员 |
| `verifyonly`（完整校验） | `sfc /verifyonly` | 命令跑了，但**没走到真正的校验**（权限不足直接返回 1） |
| `verifyfile`（完整校验） | `sfc /verifyfile=…` | 同上 |

**实跑的是**：`sfc /verifyonly`、`sfc /verifyfile=C:\Windows\System32\kernel32.dll`、`sfc /?`
—— 三条都只得到权限提示（退出码 1）。这两条写进了 `examples`（带 `expectExitCode: 1`），
并在 NOTES 里注明"1 是权限不足，不是校验失败"。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及两条：

1. **【重要】开关与取值必须粘成一个 token**：`/verifyfile=<file>`。
   `separator` 只允许 `""` / `" "` / `"="`，且它作用在 prefix 与**字段值**之间；
   要表达"`/verifyfile=` 这个字面前缀 + 路径"就变成了两个字面量的拼接，v1 做不到。
   本包的绕法（`literal` 产出字面前缀 + `positional` 产出路径）**能工作但不是规范本意** ——
   它依赖"两个相邻 token 在 Windows 命令行里会被拼成 `prefix=value`"这个事实。
   建议：给 `attached` 加一个"值可以为空、只输出 prefix"的能力，
   或者加一个显式的 `prefix` + `valueSeparator` 组合语义。
2. **编码是包级的，而 sfc 的输出编码与其它命令不同**：本批 11 个包写 `oem`、`sfc` 写 `utf-16le`，
   这没问题；但如果将来发现"同一个包内不同动作输出编码不同"（例如某个包既跑本程序又转发
   子进程输出），包级 `encoding` 就不够用了 —— 那是 `uv` 批已经记过的 `D10`。
   `sfc` 不属于那种情况，所以本包不受影响。

## 10. 给后来者的提醒

- **别用 `sfc /?` 找参数**，它只打印权限提示。
- **`sfc` 的输出是 UTF-16LE**，写脚本抓它的时候按 UTF-16LE 解，别按 GBK。
- 想加 `/scanfile` / `/verifyfile` 的新路径字段时，照 `verifyfile` 动作的 `literal + positional`
  写法（先看看 §9.1 的规范缺口有没有被解决，解决了就用更干净的写法）。
