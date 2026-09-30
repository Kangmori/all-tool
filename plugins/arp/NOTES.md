# arp 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\arp.exe`（文件版本 **10.0.26100.8875**）—— Windows 自带。
> 清单：**4 个动作 / 8 个字段 / 字段出处标注 100%**
> **公共事实（Windows 自带命令的普遍脾气、`command: ""` 的宿主缺口、校验器只认 `-` 开关等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**，本文只写本包特有的内容。
>
> 实测环境（2026-10-01）：Windows 11 `10.0.26200`、系统 UI 语言 zh-CN、
> 账号 `KANGMORI\Steve`、**非管理员**（`IsInRole(Administrator)=False`，完整性级别 Medium）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/arp`（2026-10-01 抓取，HTTP 200，页面自报 "Last updated 2025-08-15"） |
| 2 | 本机 `arp /?` 真实输出 | 1744 字节、写 **stdout**、**退出码 1**。快照**不入库**（微软文本），重抓命令见下方 |

```powershell
# 重新抓本机帮助（带超时保护，照 scripts/fetch-win-help.ps1 的思路）
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
& C:\Windows\System32\arp.exe /?          # 退出码 1，帮助在 stdout
```

**两侧核对结果：完全一致，无多无少。** 官方语法行三条：

```
arp -s [inet_addr] [eth_addr] [if_addr]
arp -d [inet_addr] [if_addr]
arp -a [inet_addr] [-N if_addr] [-v]
```

官方 `#parameters` 表列出的要素：`-a` `-g` `-v` `inet_addr` `-N if_addr` `-d` `-s` `eth_addr` `if_addr` `/?`；
本机 `/?` 列出的要素与之一一对应（`-g` 本机写作 "Same as -a"，官方写作 "Functions identically to -a"）。
**本包只收录其中 5 个只读要素**：`-a`、`-g`、`-v`、`inet_addr`、`-N if_addr`。

## 2. 覆盖范围

| 动作 id | 分组 | 生成的 argv | 字段 | 实跑 | 退出码 |
|---|---|---|---|---|---|
| `show` | ARP 表 | `arp -a` | `a`(默认 true)、`verbose` | ✅ | 0 |
| `show`（详细） | ARP 表 | `arp -a -v` | 同上 | ✅ | 0 |
| `show-interface` | 筛选 | `arp -a -N <if_addr>` | `a`、`ifAddr`(必填)、`verbose` | ✅ | 0 |
| `show-ip` | 筛选 | `arp -a <inet_addr>` | `a`、`inetAddr`(必填) | ✅ | 0（查不到也是 0） |
| `show-g` | ARP 表 | `arp -g` | `g`(默认 true) | ✅ | 0 |

`danger` 全部为空、`requiresAdmin` 全部为否（包级 `runtime.requiresAdmin: false`）——**本包没有任何写动作**。

## 3. 故意没做的部分与原因

| 没做 | 原因 |
|---|---|
| `arp -s inet_addr eth_addr [if_addr]` | **写操作**（加静态 ARP 项）。本次任务要求只做只读动作；且实测写路径需要提升（见 §7）。要做的话必须 `execution: info` 或至少 `danger: overwrite` + 明确的 `confirmText`。 |
| `arp -d inet_addr [if_addr]` | **写操作**（删 ARP 项）。同上。官方还注明 `inet_addr` 可以用 `*` 通配删除全部条目，风险更大。 |
| `/?` 动作 | 帮助不是"功能"，本批 12 个 Windows 包里也没有包把它做成动作。 |
| `versionArgs` / `versionPattern` / `minVersion` | Windows 自带命令没有版本开关（见共用文档 §4）。读 exe 文件版本要宿主支持，规范暂无此能力。 |
| `progress.pattern` | arp 没有进度输出（实测单次 30–100 ms）。 |
| 工具包级 `category` | 按产品负责人要求：清单只定义动作分组，工具包归到哪个组由用户自己决定。 |
| `nextSteps` / `quickActions` | 这些只读动作之间没有"跑完就该接着做"的语义，不硬凑。 |

## 4. 退出码实测（含失败路径）

微软的 arp 文档**没有退出码章节**（已核对），所以下表全部来自本机实测：

| 命令 | 退出码 | stdout/stderr | 说明 |
|---|---|---|---|
| `arp -a` | **0** | 2146 B / 0 B | 正常 |
| `arp -a -v` | **0** | 3166 B / 0 B | 正常，多出环回与 0.0.0.0 条目 |
| `arp -a -N 192.168.2.4` | **0** | 841 B / 0 B | 只显示该接口 |
| `arp -a 192.168.2.1` | **0** | 145 B / 0 B | 只显示该条 |
| `arp -a 10.0.0.1`（无此条） | **0** | 23 B / 0 B | `No ARP Entries Found.` —— **查不到也是 0** |
| `arp -a 1.2.3`（三段式） | **0** | 23 B / 0 B | 居然被接受，同样打印 `No ARP Entries Found.` |
| `arp /?` | **1** | 1744 B / 0 B | 帮助本身返回 1（与 ipconfig 同、与 tasklist 的 0 不同） |
| `arp`（不带参数） | **1** | 1744 B / 0 B | 打印同一份帮助 |
| `arp -x` / `arp --a` | **1** | 1744 B / 0 B | 不认识的开关 → 打印用法 |
| `arp -N 10.0.0.1`（没有 `-a`） | **1** | 1744 B / 0 B | 用法错误 |
| `arp -a -N 10.0.0.1` | **1** | 0 B / 29 B | stderr `ARP: bad argument: 10.0.0.1`（地址不是本机接口） |
| `arp -a -N abc` | **1** | 0 B / 24 B | stderr `ARP: bad argument: abc`（耗时 2.7 s，属超时/解析慢） |
| `arp -a 999.1.1.1` / `arp -a abc` | **1** | 0 B / 30 B, 24 B | stderr `ARP: bad argument: <原参数>` |
| `arp -a 10.0.0.1 10.0.0.2`（两个位置参数） | **1** | 0 B / 29 B | stderr `ARP: bad argument: 10.0.0.2` |
| `arp -d 203.0.113.7`（**写操作探针，见 §12**） | **0** ⚠ | 0 B / 78 B | stderr `The ARP entry deletion failed: The requested operation requires elevation.` —— **失败也返回 0** |

**清单里的 `exitCodes` 就按"0 = 命令跑完了（不代表操作成功）、1 = 参数/用法错误"写。**

## 5. 编码实测：判定依据

`runtime.encoding: oem`。**判定用的中文样本是构造出来的**，因为本机 `arp` 的输出除参数回显外全是 ASCII：

| 样本 | 原始字节（末尾） | 严格 UTF-8 解码 | 按 cp936 解码 |
|---|---|---|---|
| `arp -a -N 测试` 的 stderr | `41 52 50 3a 20 62 61 64 20 61 72 67 75 6d 65 6e 74 3a 20 **b2 e2 ca d4** 0d 0a` | **抛异常 → 不是 UTF-8** | `ARP: bad argument: 测试` ✅ |

`b2 e2 ca d4` 正是 cp936 里的「测试」，`41 52 50 …` 是 ASCII 的 `ARP: bad argument: `。
判定用的是 `.NET Process` 重定向 + `File.ReadAllBytes` 拿到的**原始字节**，没有经过 PowerShell 的解码。

**两条必须写明的环境事实**：

1. 本机 `arp` 的**界面文本是英文**（`Interface:` / `Internet Address` / `Physical Address` / `Type` /
   `static` / `dynamic` / `No ARP Entries Found.` / `ARP: bad argument:`），尽管系统 UI 语言是 zh-CN、
   `arp.exe` 的资源语言标记也是"简体中文(中国大陆)"。**这是本机的实测事实，我不做推测**；
   换到界面文本为中文的机器上，同样的字节路径仍走 OEM 代码页，所以 `oem` 这个声明不变。
2. 也正因为界面文本是英文，**默认输出是纯 ASCII（严格 UTF-8 解码通过）**，
   拿 `arp -a` 的输出根本判不出编码 —— 这就是上面必须构造中文样本的原因。

## 6. 字段风格与字段顺序：两种写法各跑一次

`-N` 是 arp 唯一"带取值"的开关，两种写法实测结果差别是**能不能跑**：

| 写法 | 命令 | 退出码 | 输出 |
|---|---|---|---|
| **`separate`（两 token）** | `arp -a -N 192.168.70.1` | **0** | 435 B，只有该接口的表 ✅ |
| `attached`（一 token，紧贴） | `arp -a -N192.168.70.1` | **1** | 1744 B = 打印用法（开关不被识别）❌ |
| `attached` + 冒号 | `arp -a -N:192.168.70.1` | **1** | 1744 B = 打印用法 ❌ |

→ 结论：`ifAddr` 用 `style: separate`（两 token），与官方语法行 `[-N if_addr]` 一致。

**字段顺序（= argv 顺序）也有实测依据**：`-a` 必须排在其它开关之前，否则直接失败。

| 顺序 | 命令 | 退出码 |
|---|---|---|
| `-a` 在前（本清单的声明顺序） | `arp -a -v` / `arp -a -N <ip> -v` | **0** ✅ |
| `-a` 在后 | `arp -v -a` | **1**（打印用法）❌ |
| `-N` 与 `-v` 互换 | `arp -a -v -N <ip>` | 0（这两个开关之间顺序无所谓） |

所以清单里 `show` 动作的字段顺序是 `a` → `verbose`，`show-interface` 是 `a` → `ifAddr` → `verbose`。

**另外两条实测细节**（不影响清单，但排错时有用）：

- 开关本身**大小写不敏感**：`arp -a -n 192.168.70.1` 与 `-N` 等价（exit 0），
  `arp /a` 与 `arp -a` 等价（exit 0），斜杠与减号都认。
- `arp -a <inet_addr> -N <if_addr>` 可以同时给出，两个过滤条件都生效（本机实测返回 "No ARP Entries Found."），
  本清单没有这种动作（每个动作只暴露一个过滤条件，避免用户拼出自己看不懂的组合）。

## 7. requiresAdmin：怎么定的

- 包级 `runtime.requiresAdmin: false`。**依据是实测**：非管理员账号（Medium 完整性级别）下，
  本包 4 个动作的 5 种 argv 全部 exit 0（见 §8/§9 的冒烟表）。
- **写操作确实要管理员**，这也是实测的（不是抄文档）：
  `arp -d 203.0.113.7` 的 stderr 是 `The ARP entry deletion failed: The requested operation requires elevation.`
  官方 arp 页面**没有**任何一句提到 `-d` / `-s` 需要提权。
- 写操作**没有收录**（§3），所以清单里不为它们标 `requiresAdmin`。
  `arp -s` 未执行（那会真的创建条目，见 §12）。

## 8. 真机冒烟测试结果

冒烟脚本（在仓库外，见 §12）照 playbook §5.1 写了三条：白名单逐字比对、红线自检（7 条红线里任何一条被判为可跑就 `throw`）、判不准就算未跑。
判定用 NUL 连接后比字符串，没有用 `-eq` 比数组。

```
红线自检通过：7 条红线全部判为不可跑
arp.exe -a                               exit=0        44ms out=2146   err=0
arp.exe -a -v                            exit=0        28ms out=3166   err=0
arp.exe -a -N 192.168.2.4                exit=0        29ms out=841    err=0
arp.exe -a 192.168.2.1                   exit=0        32ms out=145    err=0
arp.exe -a 10.0.0.1                      exit=0        51ms out=23     err=0
arp.exe -g                               exit=0       103ms out=2146   err=0
共跑 6 条 arp 用例，非 0 退出码的：（无）
```

- 清单里 `examples` 的 `expectExitCode` 全部按上表填写，实测逐条对上。
- `arp -g` 与 `arp -a` 的输出字节数完全相同（2146 B），证实官方那句 "Functions identically to -a"。
- **未跑**：`-s`、`-d`（写操作，见 §3/§12）。

## 9. 未验的部分与原因

| 没验 | 原因 |
|---|---|
| `arp -s` 的权限行为与成功路径 | 会真的创建静态条目；本次任务只做只读动作，不做写探针 |
| `arp -d *`（通配删除全部） | 同上，且风险更大 |
| `arp -N` 的 "This parameter is case-sensitive" 那句话 | 官方这句针对 `if_addr`，而 `if_addr` 是 IP 地址，大小写对它没有意义；我只能实测开关本身大小写不敏感（`-n` = `-N`）。文档这句话无法验证，也不影响本清单 |
| 中文界面机器上的措辞型断言 | 本机 arp 输出是英文，写不出中文正则，所以清单里**没有任何依赖文案的判定**（不用"成功提示"判成败） |
| `/?` 快照入库 | 微软文本，按 `.gitignore` 的既有约定不入库（重抓命令见 §1） |

## 10. 与官方文档对不上的地方

1. **`-N` 那条 "This parameter is case-sensitive"**：实测开关 `-N`/`-n` 等价（exit 0）。
   文档这句话既无法验证也不影响使用 —— 记录，不据此改清单。
2. **文档内部命名不统一**：Remarks 一节用 `inetaddr` / `ifaceaddr` / `etheraddr`，
   参数表用 `inet_addr` / `if_addr` / `eth_addr`。纯措辞问题，字段依据取参数表。
3. **官方没有任何退出码说明**：0/1 的语义全部来自实测（§4）。其中"写操作失败仍返回 0"
   在文档里找不到任何提示。
4. **`-d` / `-s` 需要提权，官方一个字没写**：实测才发现（§7）。这也解释了为什么本包只做只读。
5. **无来源冲突**：本机 `/?` 与官方语法行**完全一致**（不像 netstat 多出 7 个开关、tasklist 多一个 `/APPS`）。
   所以本包不存在"只写两侧都能对应的"这种取舍。

## 11. 本包特有的坑

1. **`-a` 必须排在前面**：`arp -v -a` 是错的（exit 1 + 用法），只有 `arp -a -v` 对。
   字段顺序即 argv 顺序，所以本包把 `a` 声明为每个动作的第一个字段。
2. **`-N` 只认两个 token**：`-N<ip>` / `-N:<ip>` 都会打印用法并 exit 1（不是"参数被忽略"，是直接失败）。
3. **"查不到"也是成功**：`arp -a <不存在的 IP>` 打印 `No ARP Entries Found.` 并返回 **0**，
   界面上不要当成错误。
4. **同一个 IP 可能出现在多张表里**：不加 `-N` 时是按所有接口扫，
   所以 `arp -a <ip>` 的过滤是"跨接口"的（本机实测 192.168.2.1 只在一个接口里命中）。
5. **接口是用 IP 地址指定的，不是接口名、不是 0x 索引**：文档与本机 `/?` 都只接受 IP；
   传非本机接口的 IP 会 `ARP: bad argument` 并以 1 退出。想按接口名查要先 `ipconfig` / `Get-NetAdapter`。
6. **物理地址列为空是正常的**：`-a -v` 里的环回（127.0.0.1）与 `0.0.0.0` 条目物理地址列是空白。
7. **超时**：正常情况下 arp 30–100 ms 就返回；`arp -a -N abc` 这类非法参数实测花了 2.7 s（在解析上卡了一会儿），
   所以宿主不要用很短的超时。

## 12. 临时对象申报

- **系统对象：无残留。** 唯一一次"非只读调用"是权限探针 `arp -d 203.0.113.7`
  （203.0.113.0/24 是 RFC 5737 的 TEST-NET-3 文档地址，不可能有真实邻居）。
  它**没有创建任何条目**、也**没有删除任何真实条目**，证据是前后各查一次：

  ```
  arp -a 203.0.113.7     →  No ARP Entries Found.   (exit 0)   ← 探针前
  arp -d 203.0.113.7     →  stderr: The ARP entry deletion failed: The requested operation requires elevation.   (exit 0)
  arp -a 203.0.113.7     →  No ARP Entries Found.   (exit 0)   ← 探针后，与探针前完全一致
  ```

  **没有执行 `arp -s`**（那会真的加条目）。
- **仓库内**：只新增了 `plugins/arp/manifest.yaml` 与 `plugins/arp/NOTES.md`，没有改任何其它文件。
- **仓库外的临时文件**（可随时删除，不属仓库内容）：
  `D:\AI\swpj\probe\probe-arp-getmac.ps1`、`probe2.ps1`、`smoke-arp-getmac.ps1` 与
  `report1.txt`、`report2.txt`、`smoke-report.txt`（探针脚本与原始报告，保留是为了可复现）。

## 13. 规范缺口 / 宿主问题

公共缺口（`command: ""` 被宿主运行时校验拒绝、校验器的开关溯源对 `/` 开关的历史问题、
`locate` 取不到 exe 文件版本、`requiresAdmin` 文档过期）见
[`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md) §7，本包不重复。

本包另外暴露的一条：**校验器的开关溯源语料里没有 arp 自己的 `/?` 快照**，
所以 `-N` 与 `-g` 在报告里会被列为"[待确认]"（`-a`/`-v` 因为在别的命令帮助里出现过所以碰巧命中）。
这是启发式提示、不阻断构建；真正依据是 §1 的两处来源 + §6 的实测。
