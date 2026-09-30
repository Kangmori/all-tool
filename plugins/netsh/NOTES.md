# netsh 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\netsh.exe`（**文件版本 10.0.26100.8328**，`Get-Command netsh` 实测为 `Application`）。
> 清单：动作 **39** 个 / 字段 **32** 个 / 字段出处标注 **100%**；每个动作都有 `sources`（共 8 条工具包级 + 39 组动作级，全部带 `retrieved`）。
> 39 个动作落在 **8 个上下文**上：`interface`（含 `ipv4` / `ipv6` / `portproxy` 子上下文）21 个、
> `http` 7 个、`wlan` 5 个、`advfirewall` 4 个、根上下文 `help` 3 个（`help` / `help <ctx>` / `help <ctx> <sub>`）、
> `winhttp` / `winsock` / `dnsclient` 各 1 个。
> **公共事实**（Windows 自带命令的编码规律、`/?` 形态、`command: ""` 的宿主缺口等）见
> ``docs/ai/windows-commands.md``；本文只写 netsh 特有的内容。
> 实测环境：Windows 11 `10.0.26200` x64，账号 `kangmori\steve`（**非管理员**，`IsInRole(Administrator)` 实测 `False`），
> 实测日期 **2026-09-30**（`Get-Date` 实读，非推算）。

---

## 1. 参数知识来源

| # | 来源 | URL / 位置 | 取到日期 | 用在哪 |
|---|---|---|---|---|
| 1 | netsh 总览（Microsoft Learn） | `.../windows-commands/netsh` | 2026-09-30 | 用法行 `netsh [-c <Context>] ... [<Command>]`、上下文与子上下文清单、`netsh ?` / `netsh help` 的说明 |
| 2 | netsh interface（Microsoft Learn） | `.../windows-commands/netsh-interface` | 2026-09-30 | **ipv4 / ipv6 / portproxy 全部 `<show ...>` 的语法行与参数说明**（本包 21 个动作的依据） |
| 3 | netsh wlan（Microsoft Learn） | `.../windows-commands/netsh-wlan` | 2026-09-30 | `show all\|drivers\|filters\|interfaces\|networks\|profiles\|settings\|...` |
| 4 | netsh advfirewall（Microsoft Learn） | `.../windows-commands/netsh-advfirewall` | 2026-09-30 | `show <allprofiles\|currentprofile\|...> <state\|firewallpolicy\|settings\|logging>`、`show global <ipsec\|...>`、`firewall show rule name=` |
| 5 | netsh winhttp（Microsoft Learn） | `.../windows-commands/netsh-winhttp` | 2026-09-30 | `show <advproxy\|proxy\|tracing>` |
| 6 | netsh http（Microsoft Learn） | `.../windows-commands/netsh-http` | 2026-09-30 | `show <cacheparam\|iplisten\|setting\|timeout>`、`show cachestate / servicestate / sslcert / urlacl` |
| 7 | netsh dnsclient（Microsoft Learn） | `.../windows-commands/netsh-dnsclient` | 2026-09-30 | `show encryption / global / state` |
| 8 | **本机内置帮助**（快照，**不入库**） | `<临时目录>\netsh-help\*.txt` | 2026-09-30 | 每个动作的 `doc` 都指向其中具体一份；重新抓取见 §10 |

**7 个官方页面全部用 `web_fetch` 实际请求过，HTTP 200**（`netsh` / `netsh-interface` / `netsh-wlan` /
`netsh-advfirewall` / `netsh-winhttp` / `netsh-http` / `netsh-dnsclient`）。试过但**不存在**的页面记在 §7。

**内置帮助快照共 100+ 份**：覆盖根上下文（`?` / `help`）与
`interface` / `interface ipv4` / `interface ipv6` / `interface tcp` / `interface udp` / `interface portproxy` /
`wlan` / `advfirewall` / `advfirewall firewall` / `winhttp` / `winsock` / `http` / `dnsclient` 的
`<cmd> ?` 与 `<cmd> show ?` 两级用法。

**版本号**：netsh **没有版本开关**（`netsh ?` 与 `netsh help` 都不打印版本，两者实测都只有命令清单），
所以本包**不写** `versionArgs` / `versionPattern` / `minVersion`（R1 + 共用文档 §4）。
可执行文件的版本只能从文件资源读：`(Get-Item C:\Windows\System32\netsh.exe).VersionInfo.FileVersion` → `10.0.26100.8328`。

## 2. 覆盖范围

| 分组 | 动作 id | netsh 命令 | 字段 | danger / requiresAdmin |
|---|---|---|---|---|
| 帮助与上下文 | `help` | `netsh help` | — | — / 否 |
| 帮助与上下文 | `context-help` | `netsh help <context>` | 1 | — / 否 |
| 帮助与上下文 | `subcontext-help` | `netsh help <parent> <child>` | 2 | — / 否 |
| 网卡与接口 | `show-interfaces` | `interface show interface` | 1 | — / 否 |
| 网卡与接口 | `ipv4-show-config` | `interface ipv4 show config` | 1 | — / 否 |
| 网卡与接口 | `ipv4-show-addresses` | `interface ipv4 show addresses` | 1 | — / 否 |
| 网卡与接口 | `ipv6-show-addresses` | `interface ipv6 show addresses` | 1 | — / 否 |
| 网卡与接口 | `ipv4-show-interfaces` | `interface ipv4 show interfaces` | 2 | — / 否 |
| 网卡与接口 | `ipv6-show-interfaces` | `interface ipv6 show interfaces` | 1 | — / 否 |
| DNS 与解析 | `ipv4-show-dnsservers` | `interface ipv4 show dnsservers` | 1 | — / 否 |
| DNS 与解析 | `ipv6-show-dnsservers` | `interface ipv6 show dnsservers` | 1 | — / 否 |
| DNS 与解析 | `dnsclient-show-global` | `dnsclient show global` | — | — / 否 |
| 路由与邻居 | `ipv4-show-route` | `interface ipv4 show route` | 1 | — / 否 |
| 路由与邻居 | `ipv6-show-route` | `interface ipv6 show route` | 1 | — / 否 |
| 路由与邻居 | `ipv4-show-neighbors` | `interface ipv4 show neighbors` | 1 | — / 否 |
| 路由与邻居 | `ipv6-show-neighbors` | `interface ipv6 show neighbors` | 1 | — / 否 |
| 协议与端口 | `ipv4-show-global` | `interface ipv4 show global` | 1 | — / 否 |
| 协议与端口 | `ipv6-show-global` | `interface ipv6 show global` | 1 | — / 否 |
| 协议与端口 | `ipv4-show-dynamicportrange` | `interface ipv4 show dynamicportrange` | 1 | — / 否 |
| 协议与端口 | `ipv4-show-excludedportrange` | `interface ipv4 show excludedportrange` | 1 | — / 否 |
| 协议与端口 | `ipv6-show-privacy` | `interface ipv6 show privacy` | 1 | — / 否 |
| 协议与端口 | `portproxy-show-all` | `interface portproxy show all` | — | — / 否 |
| 无线（WLAN） | `wlan-show-interfaces` | `wlan show interfaces` | — | — / 否 |
| 无线（WLAN） | `wlan-show-profiles` | `wlan show profiles` | 1 | — / 否 |
| 无线（WLAN） | `wlan-show-networks` | `wlan show networks` | 2 | — / 否 |
| 无线（WLAN） | `wlan-show-settings` | `wlan show settings` | — | — / 否 |
| 无线（WLAN） | `wlan-show-drivers` | `wlan show drivers` | 1 | — / 否 |
| 防火墙 | `advfirewall-show-allprofiles` | `advfirewall show allprofiles` | 1 | — / 否 |
| 防火墙 | `advfirewall-show-currentprofile` | `advfirewall show currentprofile` | 1 | — / 否 |
| 防火墙 | `advfirewall-show-global` | `advfirewall show global` | 1 | — / 否 |
| 防火墙 | `advfirewall-firewall-show-rule` | `advfirewall firewall show rule` | 1 | — / 否 |
| 代理与 Winsock | `winhttp-show-proxy` | `winhttp show proxy` | — | — / 否 |
| 代理与 Winsock | `winsock-show-catalog` | `winsock show catalog` | — | — / 否 |
| 代理与 Winsock | `http-show-iplisten` | `http show iplisten` | — | — / 否 |
| 代理与 Winsock | `http-show-urlacl` | `http show urlacl` | 1 | — / 否 |
| 代理与 Winsock | `http-show-cacheparam` | `http show cacheparam` | — | — / 否 |
| 代理与 Winsock | `http-show-timeout` | `http show timeout` | — | — / 否 |
| 代理与 Winsock | `http-show-setting` | `http show setting` | — | — / 否 |
| 代理与 Winsock | `http-show-servicestate` | `http show servicestate` | 2 | — / 否 |

**没有工具包级 `category`**：按产品负责人要求，清单只定义**动作级**分组，工具包归到哪一组由用户自己决定。

**`kind: system`**：Windows 自带。**没有**任何动作标 `danger` 或 `requiresAdmin` —— 全部是查询，
且全部在非管理员账号下实测成功（§6）。

## 3. 多词子命令的表达方式（本包的核心规范用法，实测）

netsh 的上下文是**多级子命令**：`netsh interface ipv4 show config` 里的
`interface` / `ipv4` / `show` / `config` 是**四段**，不是"命令 + 参数"两段。

**本包采用的写法**（39 个动作里的 37 个）：

```yaml
command: "interface"
commandArgs: ["ipv4", "show", "config"]
```

即 **`command` = 第一级，`commandArgs` = 其余各级，一级一个 argv token**。
`help` 开头的三个动作同理（`command: "help"` + `commandArgs: ["interface", "ipv4"]`），
因为本机实测 `netsh interface help`、`netsh interface ipv4 help`、`netsh interface ipv4 show ?`
与 `netsh interface ?` 等价（都是 1241 / 650 / 1268 B、退出码 0）。

**为什么不用 `-c` 把整条上下文塞进一个 token**：

| 写法 | 实测结果 |
|---|---|
| `netsh -c interface ipv4 show config` | 退出码 **0**、输出 **3088 B**，但末尾多一个 `netsh>` 提示符 —— **它进了交互模式并在等 stdin**，不适合宿主捕获 |
| `netsh -c "interface ipv4 show config"` | 退出码 0、输出 3084 B |
| **`netsh interface ipv4 show config`（本包采用）** | 退出码 **0**、输出 **3084 B**、正常退出、不进交互模式 |
| `netsh int ip show config`（缩写） | 退出码 0、输出 3084 B —— 缩写可用，但本包**一律写全称**（可读且与官方文档一致） |

结论：**`-c` 既非必要、还会把进程留在交互模式**，所以本包全部用"一级一个 token"的全路径写法。

### 3.1 字段风格实测（`separate` vs `attached` vs `positional`）

本包用到三种风格，每一处都是**用同一条真实命令把两种写法各跑一次**得出的结论：

| 字段类型 | 采用风格 | 实测证据 |
|---|---|---|
| `[name=]<string>`（`ipv4 show config` / `addresses` / `dnsservers` / `show interface`） | **`positional`**（裸值） | `show config 以太网` / `show config name=以太网` / `show config name=18` **三者输出都是 531 B、退出码 0**；`show interface 以太网` 与 `name=以太网` 都是 198 B。裸值更短，取它 |
| `[interface=]<string>`（`ipv4/ipv6 show addresses`、`show interfaces`、`show neighbors`） | **`positional`**（裸值） | `show addresses interface=以太网` → 1190 B；`show neighbors interface=以太网` → 1400 B；`show interfaces interface=18` → 1461 B（verbose 明细）；裸值 `以太网` 同样可用。**`wlan show drivers` 也是这一类**：裸值 `WLAN` 与 `interface=WLAN` 输出**逐字节相同**（2212 B） |
| `[url=]<string>`（`http show urlacl`） | **`separate`**（`url` + 值**两个** token） | `url http://+:80/MyUri`（两 token）→ **937 B**，真的列出了那条保留项；`url=http://+:80/MyUri`（粘成一个 token）→ **46 B**，只有标题行。两者退出码都是 0，**但结果不同** —— 取与文档 `[url=]<string>` 一致的两 token 写法 |
| `interface=`（**仅** `wlan show networks`） | **`attached`**（`interface=` 与值**一个** token） | ⚠ **反例**：裸值 `show networks WLAN` → 报 `One or more parameters for the command are not correct or missing.`（**退出码 1**，还打印了整个 usage）；必须写 `interface=WLAN` 才走正常路径（变成"无线网卡已断电"的 135 B 报错）。**同一台机器上 `wlan show drivers` 恰好相反** —— 所以这一条只能逐个命令实测，不能按"看起来像哪一类"归类 |
| `name=<string>`（`advfirewall firewall show rule`、`wlan show profiles`） | **`attached`** | 官方与内置帮助都写成 `name=<string>`（无空格），且 `name=all` 实测 412866 B、`name=*` 实测 23667 B，都是单 token 形态 |

**没有用 `literal` 去拼 `<tag>=<值>`**：那种写法会把取值范围枚举死（规范 §7.4 记的同一个缺口），
本包改用 `attached` + `prefix: "name="` / `prefix: "interface="`，值仍由用户自由输入。
本包用到 `literal` 的只有 3 处，且都是**文档本身就枚举死的单选项**：
`level=normal|verbose`（两处：ipv4/ipv6 的 `show route`）、
`store=active|persistent`（四处：ipv4/ipv6 `show global`、ipv6 `show privacy` 等）、
`protocol=tcp|udp`（两处）、`view=session|requestq|client` 与 `verbose=yes|no`（`http show servicestate`），
以及 `advfirewall show` 那四个**不带 `=`** 的位置式参数（`state` / `firewallpolicy` / `settings` / `logging`）。

**字段声明顺序 = argv 顺序**在本包的两个双字段动作上体现为：
`protocol` / `store` / `level` / `parameter` / `name=` 这些**开关必须在位置参数之前或按文档顺序**——
本包严格照官方语法行的顺序声明（例如 `ipv4 show interfaces [interface=] [rr=] [level=] [store=]` →
`ifTag`（interface）声明在 `level` 之前；`show dynamicportrange [protocol=] [store=]` → `protocol` 唯一）。
本机实测 `show interfaces interface=以太网 level=normal` → 190 B、退出码 0，
`level=verbose` → 1461 B、退出码 0，顺序正确。

## 4. 编码实测：**UTF-8**（与其它 Windows 自带命令相反）

**判定依据（原始字节，不是靠"看起来对不对"）**：

`netsh interface ipv4 show config` 输出的前 40 个原始字节：

```
0d 0a 43 6f 6e 66 69 67 75 72 61 74 69 6f 6e 20 66 6f 72 20 69 6e 74 65 72 66 61 63 65 20 22 e4 bb a5 e5 a4 aa e7 bd 91 22
```

- `e4 bb a5` = UTF-8 的「以」、`e5 a4 aa` = 「太」、`e7 bd 91` = 「网」→ 接口名 `"以太网"`。
- **同一串字节按 cp936 解出来是 `浠ュお缃?`**（乱码），所以**肯定不是 OEM/GBK 代码页 936**。
- 用 `UTF8Encoding(throwOnInvalidBytes)` 严格解码**成功**（无异常），而同样的严格解码器对
  本批其它命令（`ipconfig` / `netstat` …）的 `d3 c3` 这类字节会失败 —— 对照鲜明。
- 本机控制台代码页确实是 936（`chcp` 环境），但 **netsh 自己的输出是 UTF-8**，与代码页无关。

**结论**：`runtime.encoding: utf-8`。

> ⚠ 给后来者的警告：本批 12 个 Windows 命令都是 `oem`（`sfc` 是 `utf-16le`），
> **netsh 是第一个 `utf-8` 的**。如果照抄同批的 `encoding: oem`，中文接口名（`以太网`、
> `本地连接* 8`、`蓝牙网络连接 2`）在界面上会全变成乱码。判编码必须用带中文的输出 + 看原始字节（共用文档 §5.1）。

`stderr` 实测**始终 0 字节**（本包跑过的 100+ 条命令无一例外）—— 与 `nslookup` / `netstat` 恰好相反，
netsh 的错误与帮助**都写 stdout**。

## 5. 退出码实测（含失败路径）

| 退出码 | 触发场景（全部实测） |
|---|---|
| **0** | 查询被识别并执行。**注意"0 不等于有结果"**：`interface ipv6 show neighbors` 输出约 11.7 KB（两轮实测 11737 B / 11733 B —— 缓存会变，所以**体积类数字都有 ±几字节的抖动**）、退出码 0；而 `interface portproxy show all` 输出 **2 B（空）**、`winsock audit trail` 输出 **2 B**、`interface ipv4 show route store=persistent` 输出 **2 B**，**退出码都是 0** |
| **1** | ① 未知上下文/子命令：`netsh boguscontext` / `netsh this is nonsense` / `interface ipv4 show bogus` / `interface ipv4 bogus` → `The following command was not found: <全部 token 原样拼回>。`（54 / 56 / 65 / 60 B）<br>② **接口名不存在**：`interface ipv4 show config name=__NoSuch__` → `The filename, directory name, or volume label syntax is incorrect.`（72 B）<br>③ 防火墙规则零命中：`advfirewall firewall show rule name=__NoSuchRule__` → `No rules match the specified criteria.`（44 B）<br>④ **无线网卡被关闭**：`wlan show networks` → `The wireless local area network interface is powered down and doesn't support the requested operation.`（135 B）<br>⑤ **根帮助 `netsh ?`**（2258 B，而 `netsh help` 是 0）<br>⑥ `interface ipv4 show config name=__NoSuch__` 与 `name=NoSuchIf__Probe` 都是 1<br>⑦ `netsh interface ipv6 show config`（该命令不存在）<br>⑧ `netsh advfirewall firewall show rule`（缺必需的 `name=`）→ 打印 usage、1<br>⑨ `netsh wlan show profiles name=WLAN`（本机没有名为 WLAN 的配置文件）→ `Profile "WLAN" is not found on the system.`（44 B） |

**没观察到 1 以外的非零码**（本包跑过的失败路径全部是 1）。官方文档**没有任何退出码章节**
（7 个页面逐个核对过），所以 `exitCodes` 只有 0 与 1 两条，全部来自实测。

**一个反直觉的对照**（写进清单 `help` 动作的 `resultNote`）：

```
netsh help   → stdout 2088 B, exit 0
netsh ?      → stdout 2258 B, exit 1    ← 内容几乎一样，后者多 7 行（多了 6to4 之类），退出码却不同
```

## 6. `requiresAdmin` 实测：**39 个动作全部不需要管理员**（所以一个都没标）

当前账号 `kangmori\steve` **不是管理员**（`IsInRole(Administrator)` 实测 `False`）。
在这个前提下**逐个真跑**，结论如下：

| 动作组 | 非管理员实测结果 |
|---|---|
| `advfirewall show allprofiles` / `currentprofile` / `global` | **全部成功、退出码 0**（2147 / 721 / 1029 B）。⚠ 但 `show global` 里的 `Main Mode:` 段三行值显示 **`Access Denied`** —— 命令本身成功，**具体密钥参数读不到**（见 §7 第 5 条） |
| `advfirewall firewall show rule name=all` | **成功、退出码 0**（412866 B ≈ 403 KB）。**查询防火墙规则不需要提权** |
| `interface ipv4/ipv6 show ...` 全部 | 成功、退出码 0 |
| `wlan show interfaces / profiles / settings / drivers` | 成功、退出码 0 |
| `winhttp show proxy` / `winsock show catalog` | 成功、退出码 0（后者 27072 B） |
| `http show iplisten / urlacl / cacheparam / timeout / setting / servicestate` | 成功、退出码 0 |
| `dnsclient show global` | 成功、退出码 0 |

**对照**（用来证明"实测"不是"想当然"）：同一批里 `powercfg /waketimers` 与 `/requests`
在本机是**失败（exit 1）**的（见共用文档 §6.8），而我们没有照抄那条结论 —— netsh 的查询类动作
**一条都不需要提权**，所以本包 `requiresAdmin` 一个都不标（`runtime.requiresAdmin: false`）。

按规范 §2.5② 的规则（"不确定就不标；标错会让用户以为必须提权"），这里属于"实测确认不需要"，
所以**不标**就是正确写法。

## 7. 与官方文档对不上的地方（逐条记录）

1. **`netsh interface ipv6 show config` 不存在**。本机实测：
   ```
   > netsh interface ipv6 show config
   The following command was not found: interface ipv6 show config.     exit=1
   ```
   官方 `netsh-interface` 页的 ipv6 语法段里**也确实没有 `show config`**（ipv4 段有）——
   **两边一致**，所以本包不收录。IPv6 侧要看配置只能用 `show addresses` / `show dnsservers` / `show global`。
   （本包原本计划收录它，实测后删掉了。）

2. **`netsh wlan show networks` 的官方语法行与实测行为不符**。
   官方 `netsh-wlan` 页只写 `netsh wlan show networks`，不带参数；内置帮助写
   `show networks [[interface=]<string>] [[mode=]ssid/bssid]`（方括号表示可选）。
   但**实测裸值 `show networks WLAN` 直接报参数错误（退出码 1）**，必须写 `interface=WLAN`。
   → **只写两边都能对应的部分**：`mode` 两个取值在官方语法行里没有，所以 `mode` 字段的 `doc` 只引内置帮助；
   `interface` 字段用 `attached` 风格规避这个差异（§3.1）。

3. **`netsh interface tcp show global` / `interface udp show global` 没有官方文档出处**。
   官方 `netsh` 总览页的上下文清单与 `netsh-interface` 页的语法段都**没有** `interface tcp` / `interface udp`
   的 `show` 语法（后者只覆盖 ipv4 / ipv6 / portproxy）。
   → **本包不收录这两个动作**（值很好用，但按 R1"文档没说的一律不写"只能放弃）。
   本机实测它们可用（761 / 197 B、退出码 0），记在这里供将来补文档时用。

4. **`netsh winsock` 的官方页面没能取到正文**。
   试过 `.../windows-commands/netsh-winsock`（搜索结果里存在该页，但本机 `web_fetch` 报 fetch failed）
   与 `.../networking/technologies/netsh/netsh-winsock`（**HTTP 404**）。
   → `winsock-show-catalog` 的依据**只有内置帮助**（`netsh winsock ?` 594 B + `netsh winsock show ?` 198 B，
   后者写着 `show catalog - Displays contents of Winsock Catalog.`）。
   **如实标注：这一条没能用官方文档交叉验证。**

5. **`advfirewall show global` 的 Main Mode 段在非管理员下显示 `Access Denied`**。
   官方文档说 `show global` 不需要提权（实测命令退出码也确实是 0），但**这三个值读不到**：
   ```
   Main Mode:
   KeyLifetime                           Access Denied
   SecMethods                            Access Denied
   ForceDH                               Access Denied
   ```
   → 这不是命令失败，是**权限对具体值的限制**。已写进该动作的 `resultNote`。

6. **`netsh wlan show profiles name=<X>` 的匹配语义容易误解**。本机的配置文件是按 **SSID** 命名的
   （实测清单里有 `王のWIFI`、`上海未知生物研究所_5G`、`Ciallo～(∠・ω< )⌒☆` 等 **17 条**），
   所以拿**接口名**去查（`name=WLAN`）会得到 `Profile "WLAN" is not found on the system.`（退出码 1）。
   帮助里的参数说明写的是 `name - Name of the profile to display`，没点明"就是 SSID"。
   → 已写进该动作的 `help`，避免用户以为命令写错了。

7. **`netsh advfirewall show allprofiles state` 的输出末尾多一行 `Ok.`**（官方页面没有提这个）。
   只是提示性文字，不影响解析。

8. **`netsh http show urlacl`：`url=<值>` 与 `url <值>` 结果不同**（46 B vs 937 B，见 §3.1）。
   官方文档给出的例子是 `url=http://+:80/MyApp`（粘成一个 token），而语法行写的是 `[url=]<string>`。
   → 本包按**语法行**（两 token）实现，因为它真的能查到东西；差异记在这里。

9. **`netsh interface ipv4 show dynamicportrange udp persistent` 静默给空输出**（2 B、退出码 0）。
   官方语法是 `[protocol=]tcp|udp [store=]active|persistent`，`persistent` 是**命名参数**；
   裸写 `persistent` 时 netsh 既不报错也不查东西 —— 典型的"不报错但行为不同"（playbook §10.2 第 11 条）。
   → 本包所有 `store` / `level` / `view` / `verbose` 字段都用 `literal` 生成**带 `=` 的命名参数**
   （`store=persistent` / `level=verbose` / …），绝不生成裸值。

10. **`netsh interface tcp show global store=persistent` 在没有任何持久设置时报错退出 1**：
    ```
    Querying persistent storage...
    No persistent TCP global settings have been found.      exit=1
    ```
    即"持久设置不存在"被当成错误码。这也是 §7 第 3 条之外不收录它的一个附带理由。

## 8. 故意没做的部分与原因（给后来者的地图）

**① 所有会改网络配置的动作：一个都不收录。**
`set` / `add` / `delete` / `reset` / `install` / `uninstall` / `connect` / `disconnect` / `start` / `stop` /
`flush` / `update` / `import` / `export` 这些动词下**全部**不收录，包括（举例）：

| 没做的 | 为什么 |
|---|---|
| `interface ipv4 set address`（改静态 IP）/ `set dnsservers` / `set route` | 会改网络配置；写错即断网 |
| `interface ip set ...` / `interface ipv4 set interface` | 同上；且本机非管理员，**成功路径无法验证** |
| `interface ipv4 reset` / `interface ipv6 reset` / `interface ip set ...` | 重置 IP 配置，规范 §2.6 明确属于该用 `execution: info` 的一类（会断网） |
| **`winsock reset`** | 重置 Winsock 目录，**通常需要重启**，属于"会造成网络中断"的典型 |
| `winhttp reset proxy` / `winhttp import proxy source=ie` / `set proxy` | 改系统代理，会静默改掉其它程序的联网路径 |
| `advfirewall set ...` / `reset` / `import` / `export` | 关闭或重置防火墙 |
| `advfirewall firewall add/delete/set rule` | 改防火墙规则（安全的最后一道门） |
| `interface portproxy add/delete` | 新增端口转发 = 打开一个监听入口 |
| `wlan connect` / `disconnect` / `set` / `delete profile` | 连接/断开无线、改配置文件 |
| `http add/delete/update ...`（`urlacl` / `sslcert` / `iplisten` / `timeout`） | 改 HTTP.sys 的监听与证书绑定 |
| `dnsclient set/add/delete ...` | 改 DNS 服务器与加密设置 |
| `interface ipv4 show route store=persistent` 的"清空"类操作 / `delete arpcache` | 改缓存 |
| `netsh dump` / `exec` / `-f <脚本>` | 生成或执行配置脚本；`dump` 只读但会把**全部**配置（含敏感项）刷屏，价值低 |
| `netsh -r <远程机器>` / `-u` / `-p` | **远程**操控与凭据参数；官方明确需要远程注册表服务，且把口令放命令行不安全 |
| `interface ipv6 set slaacsecretkey` / `show slaacsecretkey` | 前者写密钥；后者读出 RFC 7217 密钥（敏感），不收录 |

> 取舍理由：规范 §2.9 说"再危险也要在界面上留一条"，但那条要求的前提是**动作有可读产出、
> 且作者能验证它的行为**。netsh 的写动作两条都不满足：本机是标准用户，**写动作的成功路径一次都验证不了**
> （无法如实标 `requiresAdmin`、无法验证确认短语之后的真实效果），而且多数写动作的"产出"就是网络断开。
> 所以本包选择**只做查询**，把这个取舍明确记在这里 —— 如果将来要在提权会话里补写动作，
> 请先把 §3.1 的风格结论与"哪些动作需要提权"逐条实测出来。

**② 只读但本包仍然没收的**：

| 没做的只读动作 | 为什么 |
|---|---|
| `interface tcp show global` / `interface udp show global` | **没有官方文档出处**（§7 第 3 条）。实测可用，缺的是出处 |
| `wlan show all`（34851 B）/ `show filters` / `show randomization` / `show blockednetworks` / `show autoconfig` / `show tracing` / `show wirelesscapabilities` / `show hostednetwork` | 官方有出处，但**价值/体积比低**或与本机状态无关（承载网络基本淘汰）；`show all` 一条就把前 5 条的内容全包含了 |
| **`wlan show wlanreport`** | 官方有出处，但它**会写文件**（默认在 `C:\ProgramData\Microsoft\Windows\WlanReport\` 生成 HTML 报告），不是纯查询，故不收 |
| `wlan show profiles ... key=clear` | 官方有出处，且**只有本地管理员**才能看到明文；本质是"读出已保存的 Wi-Fi 明文密码"，做成一个点的按钮不合适（写进 `wlan-show-profiles` 的 `note` 说明为何不提这个参数） |
| `winsock show autotuning`（39 B）/ `winsock audit trail`（2 B） | 输出太小、价值低 |
| `interface ipv4 show icmpstats` / `tcpstats` / `udpstats` / `ipstats` / `ipnettomedia` / `compartment` / `joins` / `subinterfaces` / `destinationcache` / `tcpconnections` / `udpconnections` / `offload` | 官方有出处，但要么与 `netstat -s` 重复、要么看的人极少；本包按"普通用户最可能用到"的口径挑 |
| `http show sslcert`（62 B，本机为空）/ `show cachestate`（143 B） | 官方有出处但本机为空；`show sslcert` 的信息在 `netsh http show urlacl` 之外的场景才用得上 |
| `advfirewall show store` / `monitor show ...` | `show store` 输出一行策略库名（144 B）；`monitor` 是运行时会话统计，属于另一个上下文（`advfirewall monitor`），本包未展开 |
| `interface show ...` 的其他子命令（`ipv4 show compartments` 等） | 与上同理，按价值取舍 |
| `tcp` / `udp` / `bridge` / `lan` / `netio` / `nlm` / `namespace` / `ras` / `rpc` / `trace` / `mbn` / `branchcache` / `ipsec` / `ztdns` / `wfp` / `wcn` / `dhcpclient` / `firewall`（旧上下文） | 这些上下文**本机实测存在**（部分 `show` 也跑通了，例如 `trace show` 599 B、`mbn show` 2664 B、`ras show` 1374 B、`nlm show` 211 B、`branchcache show` 343 B），但要么是服务器/移动宽带场景、要么没有官方页面可引（`routing` / `ipsec show` 本机实测**不存在**）。按"普通用户最可能用到"的口径不收 |
| `versionArgs` / `versionPattern` / `minVersion` | netsh 没有版本开关（§1） |
| `output.progress.pattern` | netsh 没有任何百分比进度输出（实测全部是表格文本），凭空写正则会违反 R1 |
| `nextSteps` / `quickActions` | 没有"跑完 A 就该自动跑 B"的确定链条，不写比乱写安全（规范 §2.2 要求 `when` 正则必须对真实输出验证过） |

## 9. 未跑的动作与原因

**一个都没有"没跑"** —— 本包 39 个动作**全部在真机跑过**（退出码与输出大小在 §2 / §5 / 清单的
`examples` 里逐条记录，最后一轮完整扫过一遍：38 个 `exit=0` + `wlan show networks` 的 `exit=1`）。
因为全部是只读查询，不存在"清单照写但不实跑"的动作。

不过有 **两处"跑了但没法验证差异"**，如实列出：

| 动作 | 跑了什么 | 没能验证什么 |
|---|---|---|
| `wlan-show-networks` | 跑了不带参数、`WLAN`（裸值）、`interface=WLAN` 三种写法 | 本机无线网卡处于 **Radio: Software Off**，`wlan show networks` 一律报
`The wireless local area network interface is powered down and doesn't support the requested operation.`（exit 1），所以**没能验证"正常列出可见网络"的输出形态**，也**没能验证 `mode=bssid` 与不带的差异**（两种写法都撞同一个错误）。这是环境状态，不是清单问题 |
| `wlan-show-profiles` 的 `profileName` 字段 | 跑了不带参数（906 B）、`name=WLAN`（exit 1）、`name=*`（23667 B，展开全部 17 条） | `name=<某个具体 SSID>` 的"只展开那一条"没单独跑（`name=*` 已经证明匹配逻辑可用，且 `name=*` 的输出里逐条都带完整内容）。**但这需要输入中文字符串**（本机 SSID 是中文），在界面里是常见情形 |

**没有任何动作需要管理员而无法验证** —— 全部查询在非管理员下成功（§6）。

**绝对没有跑过的**：任何 `set` / `add` / `delete` / `reset` / `install` / `uninstall` / `connect` / `disconnect` /
`start` / `stop` / `flush` / `update` / `import` / `export`；没有用 netsh 启动或停止任何服务；
没有用 `-r` / `-u` / `-p` 走过远程或带凭据的路径。**本包全程只读**（§8① 列出的全部写动作都没碰）。

## 10. 复现本文档全部结论的方法

```powershell
# 0) 环境（按共用文档 §10：抓中文输出前必须先设编码）
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$env:PYTHONUTF8 = '1'
(Get-Date -Format 'yyyy-MM-dd')                                   # 实测日期
([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
  [Security.Principal.WindowsBuiltInRole]::Administrator)          # False = 非管理员
(Get-Item C:\Windows\System32\netsh.exe).VersionInfo.FileVersion    # 10.0.26100.8328

# 1) 编码判定（原始字节，不靠肉眼）：d3 c3 之类的字节不会出现，e4 bb a5 会出现
$psi = [Diagnostics.ProcessStartInfo]::new('C:\Windows\System32\netsh.exe')
foreach ($a in 'interface','ipv4','show','config') { $psi.ArgumentList.Add($a) }
$psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
$psi.StandardOutputEncoding = [Text.Encoding]::GetEncoding(28591)   # Latin-1 = 保字节
$p = [Diagnostics.Process]::Start($psi)
$raw = [Text.Encoding]::GetEncoding(28591).GetBytes($p.StandardOutput.ReadToEnd()); $p.WaitForExit()
($raw[30..45] | ForEach-Object { $_.ToString('x2') }) -join ' '      # 期望看到 e4 bb a5 e5 a4 aa e7 bd 91

# 2) 抓一份内置帮助快照（把 <...> 换成任意上下文路径，结尾加 ?）
$dir = Join-Path $env:TEMP 'netsh-help'; New-Item -ItemType Directory -Force $dir | Out-Null
function Get-NetshHelp([string]$name, [string[]]$path) {
  $psi = [Diagnostics.ProcessStartInfo]::new('C:\Windows\System32\netsh.exe')
  foreach ($a in $path) { $psi.ArgumentList.Add($a) }
  $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
  $psi.StandardOutputEncoding = $psi.StandardErrorEncoding = [Text.Encoding]::GetEncoding(28591)
  $p = [Diagnostics.Process]::Start($psi)
  $o = $psi.StandardOutputEncoding.GetBytes($p.StandardOutput.ReadToEnd())
  $e = $psi.StandardErrorEncoding.GetBytes($p.StandardError.ReadToEnd())
  if (-not $p.WaitForExit(30000)) { $p.Kill($true); throw "timeout: $name" }
  # 用 UTF-8 落盘（netsh 的输出就是 UTF-8）
  [IO.File]::WriteAllText((Join-Path $dir "$name.txt"),
    "netsh $($path -join ' ')`nexit=$($p.ExitCode)`n--- stdout ---`n" + [Text.Encoding]::UTF8.GetString($o) +
    "`n--- stderr ---`n" + [Text.Encoding]::UTF8.GetString($e), [Text.UTF8Encoding]::new($false))
  "{0,-40} exit={1} stdout={2}B stderr={3}B" -f $name, $p.ExitCode, $o.Length, $e.Length
}
Get-NetshHelp 'root-q'            @('?')                                  # exit=1
Get-NetshHelp 'root-help'         @('help')                               # exit=0
Get-NetshHelp 'interface-q'       @('interface','?')
Get-NetshHelp 'ipv4-show-q'       @('interface','ipv4','show','?')
Get-NetshHelp 'ipv4-show-config-q'@('interface','ipv4','show','config','?')
Get-NetshHelp 'wlan-show-q'       @('wlan','show','?')
Get-NetshHelp 'advfirewall-q'     @('advfirewall','?')
# …共 100+ 份，覆盖 §1 表格里列出的每个上下文（每个都带 `?`，不会改任何配置）

# 3) 只读冒烟（与清单 examples 一一对应；只读，安全）
$cases = @(
  @('?'), @('help'),
  @('interface','show','interface'), @('interface','ipv4','show','config'),
  @('interface','ipv4','show','addresses'), @('interface','ipv6','show','addresses'),
  @('interface','ipv4','show','interfaces'), @('interface','ipv6','show','interfaces'),
  @('interface','ipv4','show','dnsservers'), @('interface','ipv6','show','dnsservers'),
  @('interface','ipv4','show','route'), @('interface','ipv6','show','route'),
  @('interface','ipv4','show','neighbors'), @('interface','ipv6','show','neighbors'),
  @('interface','ipv4','show','global'), @('interface','ipv6','show','global'),
  @('interface','ipv4','show','dynamicportrange','protocol=tcp'),
  @('interface','ipv4','show','excludedportrange','protocol=tcp'),
  @('interface','ipv6','show','privacy'), @('interface','portproxy','show','all'),
  @('wlan','show','interfaces'), @('wlan','show','profiles'), @('wlan','show','networks'),
  @('wlan','show','settings'), @('wlan','show','drivers'),
  @('advfirewall','show','allprofiles'), @('advfirewall','show','currentprofile'),
  @('advfirewall','show','global'), @('advfirewall','firewall','show','rule','name=all'),
  @('winhttp','show','proxy'), @('winsock','show','catalog'),
  @('dnsclient','show','global'), @('http','show','iplisten'), @('http','show','urlacl'),
  @('http','show','cacheparam'), @('http','show','timeout'), @('http','show','setting'),
  @('http','show','servicestate')
)
foreach ($c in $cases) {
  $psi = [Diagnostics.ProcessStartInfo]::new('C:\Windows\System32\netsh.exe')
  foreach ($a in $c) { $psi.ArgumentList.Add($a) }
  $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
  $psi.StandardOutputEncoding = [Text.Encoding]::GetEncoding(28591)
  $p = [Diagnostics.Process]::Start($psi)
  $b = $psi.StandardOutputEncoding.GetBytes($p.StandardOutput.ReadToEnd()); $p.WaitForExit(120000)
  "exit={0,-3} {1,8}B  netsh {2}" -f $p.ExitCode, $b.Length, ($c -join ' ')
}

# 4) 校验（必须全绿；本包的输出见 §11）
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
```

> **安全说明**：第 2、3 步里的每一个 token 都以 `?` 或 `show` 结尾/开头，**没有任何
> `set` / `add` / `delete` / `reset` 出现**。`netsh` 无参数运行会进交互模式，所以上面
> 一律带 `<path> ?` 或 `show`（不会卡住：`?` 打印完就退出）。

## 11. 校验结果

```
$ uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
[ OK ] plugins\netsh\manifest.yaml  (39 动作 / 32 字段 / 字段出处标注 32 个 = 100%)

25/25 个 manifest 通过
字段出处覆盖率: 830/830 (100%)
```

校验器退出码 `0`。**开关溯源那一层对本包是空跑**：`/` 与被 netsh 大量使用的 `xxx=` 形式
在 `field_switches()` 里只认以 `-` / `/` 开头的 `prefix`，而本包的 `prefix` 是 `name=` / `interface=` /
`url`，所以那句"检查 305 个开关"里**不含本包的任何开关**（与共用文档 §7.2 记录的情况同源，
只是这次是 `=` 形式而非 `/` 形式）。这意味着本包的开关**没有自动门禁**，
每一个都必须靠 §3.1 的真机实测来保证 —— 这也是本文档把每条实测证据都写下来的原因。

## 12. 本包特有的坑（会咬人的地方）

1. **编码是 UTF-8**（§4）。抄同批 `oem` 会让所有中文接口名乱码。
2. **`netsh ?` 与 `netsh help` 输出几乎一样但退出码不同（1 vs 0）**（§5）。
   清单里 `help` 动作用 `netsh help`（exit 0）而不是 `netsh ?`。
3. **空输出 + 退出码 0 是常事**（§5）：`portproxy show all`、`winsock audit trail`、
   `route store=persistent` 都是 2 B / exit 0。界面必须能区分"查到了但为空"与"失败了"——
   靠退出码区分不了，要靠输出文本。
4. **同一个"看起来一样"的参数标签，风格要求不同**（§3.1）：`wlan show drivers` 裸值可用、
   `wlan show networks` 裸值必错、`interface *= show ...` 裸值可用、`http show urlacl` 的 `url` 必须两 token。
   **一律真机各跑一次两种写法**，不要按类别推断。
5. **裸写命名参数会被静默吞掉**（§7 第 9 条）：`dynamicportrange udp persistent` 不报错但什么也不查。
   所以所有 `store` / `level` / `view` / `verbose` 字段都生成 `xxx=值`，绝不生成裸值。
6. **接口名可以是名字也可以是索引**（本机 `以太网` = index **18**；`netsh interface ipv6 show neighbors`
   的输出里也带出了 `Interface 20: WLAN` 与 `Interface 1: Loopback Pseudo-Interface 1`），
   官方文档与内置帮助都明确支持"名字或索引"两种。含空格与中文的名字（`本地连接* 8`、`蓝牙网络连接 2`）由宿主加引号。
7. **`wlan show profiles` 的 `name` 是 SSID 而不是接口名**（§7 第 6 条）。
8. **`advfirewall firewall show rule name=all` 输出 412866 B（≈403 KB）**、
   `winsock show catalog` 27072 B、`wlan show profiles name=*` 23667 B。
   界面要能承受刷屏（清单的 `resultNote` 都标了体积）。
9. **`wlan show networks` 在无线关着时是退出码 1 的"失败"**，而这不是命令或清单的问题。
   同理 `show config name=<不存在的接口>` 报的是一句毫无帮助的
   `The filename, directory name, or volume label syntax is incorrect.`（这是 netsh 自己的措辞）。
10. **官方文档没有退出码章节**，本包的 `exitCodes` 全部是实测（§5），不是从文档抄的。

## 13. 临时对象申报

**无。** 本包全程只执行只读查询：
没有创建/删除/修改任何网络配置、注册表项、文件、端口代理、防火墙规则、无线配置文件或计划任务；
没有用 netsh 启动/停止任何服务；没有用 `-f` 执行任何脚本。

唯一的磁盘副作用是**我自己**写在系统临时目录 `%TEMP%\netsh-probe\` 与 `%TEMP%\netsh-help\` 下的
帮助/输出快照（纯读取的产物），它们**不在仓库里**、也不影响任何系统状态；
不需要复核残留（`Remove-Item -Recurse -Force "$env:TEMP\netsh-probe"` 即可清掉，但保留它们便于复核本文档的数字）。

## 14. 给后来者的提醒

- 想补**写**动作（`set address` / `set dnsservers` / `winsock reset` …）之前，先在**提权会话**里
  把"哪些动作真的需要管理员"逐条实测出来，再按规范 §2.5② 标 `requiresAdmin`，
  并按 §2.9 给会断网的动作配 `execution: info`（`reset` 类）或 `danger` + `confirmPhrase`（`set` 类）。
  **本包的 §3.1 风格结论可以直接复用**（`name=` / `interface=` 用 `attached`，`url` 用 `separate`）。
- `netsh interface tcp show global` / `interface udp show global` 实测可用但**没有官方出处**，
  一旦找到官方页面就可以照 §2 的格式补进来（一个动作 + 一个 `store` 枚举字段）。
- 本包**没有**任何 `example` 缺 `expectExitCode`：`wlan show networks` 那条例外地**故意不写**
  `expectExitCode`，因为本机环境（无线关着）下它是 1，而在无线开着的机器上应当是 0 ——
  写死任何一个都会在另一类机器上"冒烟失败"。
