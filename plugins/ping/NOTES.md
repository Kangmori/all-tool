# ping 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\PING.EXE`（文件版本 10.0.26100.8115）—— Windows 10/11 自带命令。
> 清单：`plugins/ping/manifest.yaml`　|　动作 3 个 / 字段 13 个 / 字段出处标注 **100%**
> 实测环境：Windows 11 `10.0.26200` x64，账号 `KANGMORI\Steve`（**非管理员**）
> 实测日期：2026-09-27

---

## 1. 参数知识来源（两类，都可离线复核）

| # | 来源 | 位置 |
|---|---|---|
| 1 | **官方文档（Microsoft Learn）** | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ping` → 离线快照 `docs/reference/win-docs/ping.html` |
| 2 | **本机 `ping /?` 的真实输出** | 快照 `docs/reference/win-help/ping.txt`（由 `scripts/fetch-win-help.ps1` 抓取，**不入库**，见 §3） |

**开关是拿本机 `/?` 核对过的** —— 本清单里每个字段都能在 `ping.txt` 的"选项"一节里找到对应行。

---

## 2. 覆盖范围

| 动作 id | 左侧分组 | 命令 | 字段数 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `test` | 连通性测试 | （无子命令） | 7 | none | 否 |
| `test-continuous` | 连通性测试 | （无子命令） | 2 | none | 否 |
| `test-v6-roundtrip` | 连通性测试 | （无子命令） | 4 | none | 否 |

说明：
- **动作级 `category`** 用于宿主左侧列表分组；**本清单刻意不写工具包级 `category`** ——
  按产品负责人的明确要求，清单只定义"动作"的分组，工具包自身归到哪个组由用户自己决定。
- **没有 progress.pattern**：ping 的进度不是百分比，硬写一个正则等于"凭印象编正则"，违反 R1。
- `requiresAdmin` 全部为否 —— 官方文档与本机实测都没有 ping 需要提权的依据。

---

## 3. 为什么 `/?` 快照不入库

`docs/reference/win-help/` 已加入 `.gitignore`，理由写在同一处：

1. **版权**：这些是微软的帮助文本，本项目即将转为公开仓库，不能随仓库发布。
2. **可再生**：任何一台 Windows 机器上跑 `pwsh -File scripts/fetch-win-help.ps1` 就能重新抓到。

代价：校验器的第 5 层"开关溯源（启发式）"在干净克隆里看不到这批语料。该层只提示、不阻断，
且它本来就只认 `-` 开头的开关（Windows 命令用 `/`），所以本批 12 个包在这层里等于没被检查 ——
这是个工具缺口，已写进根目录的 `docs/ai/windows-commands.md` §7。

---

## 4. 真机冒烟测试结果（含退出码）

冒烟脚本：`scripts/smoke-win-cmds.ps1`（**含只读白名单 + 红线自检**，判定不用 `-eq` 比数组）。

| 命令 | 实测退出码 | stdout | stderr | 耗时 |
|---|---|---|---|---|
| `ping 127.0.0.1 -n 2` | **0** | 305 B | 0 B | 1060 ms |
| `ping -n 2 localhost` | **0** | 262 B | 0 B | 1037 ms |

两条都真的收到了回复（输出含 `来自 127.0.0.1 的回复: 字节=32 时间<1ms TTL=128`）。

**退出码结论**：**0 = 至少收到一个回复；1 = 一个回复都没收到**。这与一般程序相反，
所以界面上不能把 1 直接当成"命令本身出错"。微软的 `ping` 文档**没有退出码章节**（已核对：
页面只有一张 Parameter 表），上面两条来自实测。`ping /?` 自身的退出码也是 **1**。

**版本**：Windows 自带命令没有 `--version` 开关，所以清单里**没有写 `versionArgs` /
`versionPattern` / `minVersion`** —— 不发明参数（R1）。后果是宿主会在输出区打一句
"# 注意：未能解析出版本号"，那是清单的诚实代价，不是错误。

---

## 5. 输出编码（实测原始字节，不是猜的）

| 输出 | 字节数 | 编码判定 | 解出来的首行 |
|---|---|---|---|
| `ping /?` | 1479 | **oem/cp936** | `用法: ping [-t] [-a] [-n count] …` |
| `ping 127.0.0.1 -n 2` | 305 | **oem/cp936** | `正在 Ping 127.0.0.1 具有 32 字节的数据:` |

字节证据：`ping /?` 的前 4 个字节是 `0d 0a d3 c3`，其中 `d3 c3` 按 cp936 解是「用」，
按 UTF-8 解是非法序列。所以 `runtime.encoding: oem` 是必须的，不能写 `utf-8`。

**我在这里踩了一个坑，值得后来者记住**：判断"是不是 UTF-16LE"**不能**只看"ASCII 后面跟不跟 `00`"。
`sfc.exe` 的整段输出都是 UTF-16LE，里面 `3a 4e`（「为」）这类汉字对的第二个字节不是 0，
用"数 00 的个数"的判定会把它误判成 cp936，输出全是乱码。见 `plugins/sfc/NOTES.md` §5。

---

## 6. Windows 命令的"怪脾气"（本包相关的）

1. **退出码语义与直觉相反**（0 = 通了，1 = 全丢），且 `/?` 也返回 1。见 §4。
2. **`-t` 永不结束**。宿主界面上的「取消」按钮是唯一的停止方式；被取消时退出码是 1，
   那是正常的（清单的 `resultNote` 里写明了）。
3. **`-f` / `-i` / `-v` / `-r` / `-s` / `-j` / `-k` 仅 IPv4；`-R` / `-S` 仅 IPv6。**
   官方文档逐条标注，本清单也写进了 `help`。
4. **`-a` 不改变 ping 的结果**，只多打印一个反向解析出的主机名，所以放进"高级"。

---

## 7. `/?` 与真实输出的实测形态

- `ping /?` 的帮助**写在 stdout**（1479 字节），stderr 为 **0 字节**，退出码 **1**。
- 用法行：`ping [-t] [-a] [-n count] [-l size] [-f] [-i TTL] [-v TOS] [-r count] [-s count] [[-j host-list] | [-k host-list]] [-w timeout] [-R] [-S srcaddr] [-c compartment] [-p] [-4] [-6] target_name`
- **一处来源不一致**：本机 `/?` 多列了 `-c compartment`（路由隔离舱标识符）与
  `-p`（Ping Hyper-V 网络虚拟化提供程序地址），**官方文档的参数表里没有这两条**。
  按"以官方文档为准"处理，本清单**不收录**它们 —— 收录了就没法用官方 URL 交代出处。

---

## 8. 故意没做的部分与原因

- `-c compartment`、`-p`：只有本机 `/?` 有，官方文档没有 → 不收（见 §7）。
- `-r count`（记录路由）/ `-s count`（时间戳）/ `-j` / `-k`（源路由）：冷门且多数网络会丢弃
  这类报文，没做成字段。
- 没有 `versionArgs` / `versionPattern` / `minVersion`（理由见 §4）。
- 没有 `progress.pattern`（理由见 §2）。

---

## 9. 未实跑的动作与原因（安全红线）

**本包全部只读动作都已实跑**（`ping` 本身不写任何数据）。唯一"没跑完整"的是
`test-v6-roundtrip`（`-R` 需要 IPv6 目标，官方文档还注明 RFC 5095 已弃用该 IPv6 路由扩展头，
本机没有合适对端）。它的字段依据是官方文档参数表，**行为未实测**，已在清单里注明。

---

## 10. 规范缺口 / 宿主问题

### 10.1 【阻塞】`command: ""`（无子命令）被宿主校验拒绝

Windows 自带命令**没有子命令这个概念**，所以"直接运行程序"这类动作只能写 `command: ""`。
JSON Schema 允许（`command` 是可选字符串），但宿主的运行时校验拒绝：

- 症状：`dotnet test` 报 `清单校验失败 plugins\chkdsk\manifest.yaml：- 动作 check：缺少 command`
- 位置：`src/Swpj.Core/Manifest/ManifestValidation.cs:78`
  ```csharp
  if (string.IsNullOrWhiteSpace(action.Command))
  {
      errors.Add($"{where}：缺少 command");
  }
  ```
- **但执行层本来就支持空命令**：`src/Swpj.Core/Execution/ArgvBuilder.cs:28`
  ```csharp
  if (!string.IsNullOrEmpty(action.Command))
  {
      argv.Add(action.Command);
  }
  ```
  也就是说校验与执行层**自相矛盾**：校验不允许，执行早就允许了。
- 复现：`dotnet test src\Swpj.slnx`（`RealManifestTests.加载全部工具包都不应抛异常` 会遍历 `plugins/`）
- 建议改法（一行，任选其一）：
  1. 把 78 行改成 `if (action.Command is null)` —— 只在"作者根本没写这个键"时报错；
  2. 或直接删掉这段校验，让 `ArgvBuilder` 的容错生效；
  3. 或在 schema 里显式区分「没有子命令」与「作者忘了写」。
- 受影响：本批 12 个包里凡是有"无子命令"动作的都会撞上 ——
  `ping` / `ipconfig` / `tracert` / `tasklist` / `systeminfo` / `robocopy`（以及 `chkdsk` 等）。
  受影响动作的 `fields`/`sources` 本身是合规的，卡住的只有这一条。

### 10.2 校验器的"开关溯源"只认 `-` 开头的开关

`scripts/validate-plugins.py` 的 `field_switches()` 里有 `prefix.startswith("-")`，
而 Windows 命令用 `/`（`/all`、`/svc`）。结果本批 12 个包**完全没被这一层检查过**，
校验输出里那句"检查 195 个开关"不含 Windows 命令的任何一个开关。
建议放宽成 `startswith(("-", "/"))`。

### 10.3 规范表达不了"开关与取值必须在同一个 argv token 里"

`/verifyfile=<file>`（sfc）、`/sageset:n`（cleanmgr）这类写法，规范 v1 的六种 style 都无法表达
（`separator` 只允许 `""` / `" "` / `"="`，且它作用在 prefix 与值之间，不支持把两个字面量粘起来）。
本批的绕法是把整个 token 写成 `literal` 的 `args`，代价是取值范围被枚举死。
详见 `plugins/sfc/NOTES.md` §10 与 `plugins/cleanmgr/NOTES.md` §10。

---

## 11. 给后来者的提醒

- 想加 `-c` / `-p` 之前先确认官方页面里有没有它们（本机 `/?` 与官方文档不一致）。
- 想验证"持续 ping 会不会卡住界面"，用 `test-continuous` 然后点「取消」；
  被取消时退出码 1 是正常现象。
