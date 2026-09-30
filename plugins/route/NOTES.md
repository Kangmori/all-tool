# route —— 实测记录（NOTES）

> 本包只做**只读**动作。公共事实（Windows 自带命令的通用性质、编码判定方法、
> 退出码为何不是「0 = 成功」）一律指回 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)，
> 本文只写 **route 特有的内容**。

实测环境：Windows 11 `10.0.26200`，`ROUTE.EXE` 文件版本 `10.0.26100.8875`
（`(Get-Item C:\Windows\System32\ROUTE.EXE).VersionInfo.FileVersion`）。
系统区域 `zh-CN`、新控制台默认代码页 **936**。当前账号**非管理员**。
实测日期 **2026-10-01**。

---

## 1. 参数知识来源

| # | 来源 | 位置 | 取得日期 |
|---|---|---|---|
| 1 | Microsoft Learn 官方参考 | <https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/route_ws2008> | 2026-10-01 |
| 2 | 本机 `route /?` 真实输出 | 本机帮助快照（**未入库**，`scripts/fetch-win-help.ps1` 可重抓） | 2026-10-01 |

**⚠ 官方 URL 不是标准形态**：`.../windows-commands/route` 与 `.../windows-commands/route_ws2008`
都试过，官方页面落在 **`route_ws2008`** 这个带后缀的 slug 上（HTTP 200）。
抓取时**必须实际请求确认**，不要按其它命令的规律猜（同 `powercfg` 那个坑）。

两个来源**对得上的部分**（本清单只依据这些）：
`print` / `<destination>` / 通配符规则（`*`、`?`，示例 `10.*`、`192.168.*`、`127.*`、`*224*`）、
`/p` 与 `print` 联用时列出永久路由、`/?`。

**对不上的部分**见 §7。

---

## 2. 覆盖范围

| 动作 | 生成的命令 | 字段 | 危险级别 |
|---|---|---|---|
| `print-routes` | `route print [-4|-6] [目标]` | 2 | 只读 |
| `print-persistent` | `route print /p` | 0 | 只读 |

共 **2 个动作 / 2 个字段**，全部只读；`danger` 一律不写（无风险），`requiresAdmin` 为 `false`。

### 字段风格与字段顺序（都实测过两种写法）

| 字段 | 风格 | 生成的 token | 实测依据 |
|---|---|---|---|
| `family` | `literal` | `-4` / `-6` / 无 | `route print -4` exit 0（3197 B）；`route print -6` exit 0（2201 B）；不选则 4637 B |
| `destination` | `positional` | 目标串本身 | `route print 127*` exit 0；`route print -4 127*` exit 0 |

**`-4`/`-6` 放前放后都行，实测两者等价**（这是本包唯一有歧义的地方，所以两侧都跑了）：

```
route print -4      exit=0  out=3141B     ← 本机帮助的示例形态
route -4 print      exit=0  out=3141B     ← 官方语法行的形态（[-4|-6] 在 command 前）
route print 0.0.0.0 -4   exit=0  out=1172B
route -4 print 0.0.0.0   exit=0  out=1172B
```

字节数完全相同，说明两种顺序生成的是同一个调用。本清单按**本机帮助的示例顺序**
（`print` 在前、`-4` 在后）声明字段，即 `command: print` + `family` + `destination`。
`/p` 则走 `commandArgs`，因此宿主组装出来的是 **`route print /p`**——
即官方**示例**里的形态（官方**语法行**把 `/p` 列在 `<command>` 之前，两种都实测可用，见 §7 第 7 条）。

> **为什么 `destination` 用 `positional` 而不是 `separate` + `prefix: ""`**：
> `separate` 的语义是「前缀与值两个 token」，配空前缀是一个没有依据的用法；
> 目标本身就是一个位置参数，`positional` 才是如实的表达（空值不产生 token）。

**注意 `route print` 是两个词**：`print` 是子命令，所以写 `command: print` 而不是塞进 `commandArgs`。

---

## 3. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| `route add` | **写操作**：改系统路由表，官方文档要求管理员。本包按任务要求只做只读，故不收录 |
| `route delete` | 同上。另外它对 `print`/`delete` 允许通配符，误用会一次删掉多条路由 |
| `route change` | 同上 |
| `route -f`（清空网关项） | **会清空路由表**，属于破坏性操作，与「只读」定位冲突，不收录 |
| `mask` / `netmask` / `gateway` / `metric` / `if` | 这些参数只有 `add`/`change`（及 `delete` 的部分形态）才需要，只读动作里用不到 |
| 「只看永久路由」做成会过滤的动作 | 官方没有「只列永久路由」的开关；`/p` 只是「列出永久路由」，**不排除活动路由**。本机实测 `/p print` 与 `print` 输出**字节数完全相同**（都是 4637 B）。所以只能如实地做成「打印并显式列出永久路由」，不能承诺它会过滤 |

> **管理员权限**：`route add/delete/change` 需要管理员——**但这些写操作本包一个都没收录**，
> 所以清单里**没有任何** `requiresAdmin: true`。只读的 `print` 实测在**非管理员**账号下
> 全部成功（6 条冒烟全 exit 0），因此 `runtime.requiresAdmin: false`。
> 这一条是**实测**的，不是照文档抄的（官方文档一个字都没写权限要求）。

---

## 4. 退出码（含失败路径，全部实测）

| 命令 | 退出码 | 说明 |
|---|---|---|
| `route print` | **0** | 成功，4637 B / 82 行 |
| `route print -4` | **0** | 成功，3197 B |
| `route print -6` | **0** | 成功，2201 B |
| `route print /p` | **0** | 成功，4637 B / 82 行（与 `route print` **逐字节相同**：本机 0 条永久路由，且 `/p` 并不过滤掉活动路由） |
| `route print 127*` | **0** | 命中，1387 B |
| `route print -4 127*` | **0** | 命中，1261 B |
| `route print 999.999.999.999` | **0** | **没匹配到也返回 0**：输出退化成只剩表头的 1065 B。不要用退出码判断「有没有查到」 |
| `route print a b c` | **1** | 多给一个位置参数：stderr 只有一行 `C:\Windows\System32\ROUTE.EXE: bad argument c`（47 B） |
| `route print -9` | **1** | 非法开关，stderr 打整篇帮助（2991 B） |
| `route`（无参数） | **1** | stderr 打整篇帮助 |
| `route /?` | **1** | 帮助，stderr 打整篇帮助 |

> 帮助走 **stderr**、真正打印的路由表走 **stdout**。只看 stdout 会以为 `route /?` 什么都没输出。
> 与 `nslookup` / `netstat` 的「帮助走 stderr」是同一类现象。

---

## 5. 编码：实测为 OEM，但**本地化受控制台有无影响**（本包特有的坑）

### 结论

`runtime.encoding: oem`（本机 = 代码页 936）。

### 判定依据（用带中文的输出，不看纯 ASCII）

| 执行条件 | `route /?` 原始字节 | 结论 |
|---|---|---|
| 真实控制台，`chcp 936` | 2367 B，**944 个字节 >127**，首行 `操作网络路由表。` | 中文 |
| `CreateNoWindow = true`（宿主式：无控制台 + 重定向） | 2367 B，**944 个字节 >127**，首行 `操作网络路由表。` | 中文 |
| `CreateNoWindow = false`（共享父进程控制台 + 重定向） | **2991 B，0 个字节 >127**，首行 `Manipulates network routing tables.` | **英文** |
| 真实控制台，`chcp 65001`（UTF-8） | 2991 B，0 个字节 >127 | **英文** |

**这是一个真坑，值得后来者记住**：`route.exe` 在**拿不到 / 不使用控制台**时会退回
**英文资源**（纯 ASCII），此时**根本无从判断它是哪个代码页**（纯 ASCII 在任何代码页下都一样）。
只有在控制台存在（或进程按控制台程序初始化）时才走中文资源、暴露 936 代码页。
所以「拿中文输出去判编码」这一步在 route 上**必须在带控制台的条件下做**——
我第一次在无控制台条件下抓，得到的是 2991 B 纯 ASCII，差点据此写下错误结论。

`route print` 的真实数据同理：`chcp 936` 下 4637 B、452 个字节 >127
（`IPv4 路由表`、`在链路上`、`网络目标` 等中文标签都在）。

**为什么写 `oem` 而不是写死 `gbk`**：换成英文 Windows（437）或日文 Windows（932）时
同一份清单仍然正确。

**与其它包的差别**：`ping` 在本机 **无论哪种条件都是纯英文**（282 B、0 个字节 >127），
`ipconfig` 在 `chcp 936` 下含中文（5400 B、58 个字节 >127）。
同一个系统里三个命令的本地化行为都不一样，**必须逐包实测**（R7）。

---

## 6. 真机冒烟结果（全部只读，均退出码 0）

| 动作 | 命令 | 退出码 | 输出 | 耗时 |
|---|---|---|---|---|
| print-routes | `route print` | 0 | 4637 B / 82 行 | 0.22 s |
| print-routes | `route print 127*` | 0 | 1387 B / 31 行 | 0.05 s |
| print-routes | `route print -4 127*` | 0 | 1261 B / 24 行 | 0.05 s |
| print-routes | `route print -4` | 0 | 3197 B / 48 行 | 0.23 s |
| print-routes | `route print -6` | 0 | 2201 B / 47 行 | 0.04 s |
| print-persistent | `route print /p` | 0 | 4637 B / 82 行 | 0.20 s |

清单里 6 条 `examples` **全部真跑过**且退出码与 `expectExitCode` 一致。
`route print -6` 的输出里能看到 `Interface List` 与 IPv6 路由段——地址族过滤确实生效，
不是「参数被忽略」的那种假通过。

### 没验的部分与原因

| 没验 | 原因 |
|---|---|
| `add` / `delete` / `change` 的退出码与 requiresAdmin | **没有收录这些写操作**，按「写操作不实跑」的规矩不碰。官方文档也没写退出码 |
| `-4`/`-6` 与通配符组合的**全部**形态 | 抽了代表性 5 条（见上表）。通配符只见于 `print`/`delete`，官方 Remarks 已明确 |
| 域名/`NETWORKS` 符号名作目标 | 官方 Remarks 说可以用 `Networks` 文件里的名字，但本机没有该文件，无从验证 |

---

## 7. 与官方文档对不上的地方（逐条）

1. **URL 形态**：官方页面 slug 是 **`route_ws2008`**，不是 `route`（后者 404）。
   实测确认，记在这里免得下一个人再猜一次。

2. **`-4` / `-6` 在官方页面里根本没有**：
   - 官方语法行：`route [/f] [/p] [<command> [<destination>] [mask <netmask>] [<gateway>] [metric <metric>]] [if <interface>]]`
     —— 没有 `-4`/`-6`。
   - 本机 `/?` 用法行：`ROUTE [-f] [-p] [-4|-6] command [destination]`
     —— 有 `-4`/`-6`，且有两个独立说明行（`-4 强制使用 IPv4。` / `-6 强制使用 IPv6。`）。
   - **处理方式**：`-4`/`-6` **收录**，但在 `print-routes` 的 `sources` 里把出处明确指向
     **本机帮助**一侧，不谎称官方页面有它。理由是它们在本机帮助里有独立说明行、
     且实测可用并确实改变了输出（4637 B → 3197 B / 2201 B）。

3. **`/f` 与 `/p` 均未收录**：`/f` 会清空网关项（破坏性，与只读定位冲突）；
   `/p` 收在 `print-persistent` 里。

4. **`0.0.0.0` 之类的「目标」不会报错**：官方把 `<destination>` 描述成
   「网络地址（主机位为 0）或主机路由的 IP 地址，或 `0.0.0.0` 表示默认路由」。
   本机实测 `route print 0.0.0.0`、`route print 999.999.999.999`、`route print 127.0.0.1/8`
   **全部 exit 0**（后两者退化成只剩表头）——说明它把这些当成**模式匹配**而不是地址校验。
   这一点官方文档没有写清楚，也不影响本清单（字段不限定取值）。

5. **退出码语义官方完全没有**：route 文档没有退出码章节（已逐页核对）。
   清单里的 0 / 1 全部来自本机实测。

6. **`route` 的帮助走 stderr**：官方文档只说 `/?`「Displays help at the command prompt」，
   没有说写到哪个流。实测写 **stderr**（stdout 0 B）。

7. **`/p` 的位置官方两处不一致**：**语法行**把 `/p` 放在 `<command>` 之前
   （`route [/f] [/p] [<command> …]`），而**示例**给出的是 `route print /p`。
   实测**两种顺序都 exit 0、输出完全相同**（4637 B）：
   `route /p print` = 4637 B、`route print /p` = 4637 B、`route /p print -4` = 3197 B、`route -4 /p print` = 3197 B。
   宿主按规范 §3.2 的 `[command] + commandArgs` 组装，所以清单实际生成 **`route print /p`**。

8. **`/p` 并不「过滤出永久路由」**：官方措辞是「the list of persistent routes is displayed」，
   容易被读成「只列永久路由」。本机实测 `route /p print` 与 `route print` 的 stdout
   **字节数完全相同（都是 4637 B）**，而且这份输出里**没有**「永久路由」段
   （本机 `PersistentRoutes` 为空，所以那一段不打印）。也就是说 `/p` 的可见效果
   在「本来就有永久路由」的机器上才看得出，本机**无法验证它到底多打印了什么**。
   清单里的描述已经按这个事实写成「打印路由表，并让其中『永久路由』的部分显式列出」，
   没有承诺过滤。**这是本包最没能验证的一条实测盲区。**

---

## 8. 临时对象申报

**没有创建、修改或删除任何系统对象。**

- 本包的全部动作都是只读的 `route print`；
- `route add` / `delete` / `change` / `-f` **一次都没有执行过**；
- 没有改注册表、没有改路由表、没有装任何东西。

探针脚本写在**仓库之外**（本机专属库 `D:\AI\swpj\local\tools\`，不入库）：
`probe-route-pathping.ps1`、`probe-route-argv.ps1`、`probe-route-encoding.ps1`、
`probe-route-stderr.ps1`、`probe-console-boundary.ps1`、`probe-final-smoke.ps1`。
临时输出落在 `%TEMP%\route-pathping-probe\`，可随时删除。

**复核命令**（确认路由表没被动过，与 `route print` 的默认路由条数对照）：

```powershell
route print | Select-String '0.0.0.0.*0.0.0.0'   # 默认路由条目；本机实测 2 条
```
