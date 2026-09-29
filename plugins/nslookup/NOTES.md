# nslookup 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\nslookup.exe`（文件版本 10.0.26100.8521）—— Windows 自带。
> 清单：动作 3 个 / 字段 11 个 / 字段出处标注 **100%**
> **公共事实见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**。
> 实测环境：Windows 11 `10.0.26200`，账号 `<机器名>\<用户名>`（非管理员）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/nslookup` → 快照 `docs/reference/win-docs/nslookup.html` |
| 2 | 本机 `nslookup /?` | 快照 `docs/reference/win-help/nslookup.txt`（**274 字节，写 stderr**，退出码 1，不入库） |

**关于"开关是拿本机 `/?` 核对过的"这一条，本包要说清楚**：本机 `/?` 只给出 4 行**用法**，
**没有任何开关列表**：

```
用法:
   nslookup [-opt ...]             # 使用默认服务器的交互模式
   nslookup [-opt ...] - server    # 使用 "server" 的交互模式
   nslookup [-opt ...] host        # 仅查找使用默认服务器的 "host"
   nslookup [-opt ...] host server # 仅查找使用 "server" 的 "host"
```

所以本包的开关依据**主要来自官方文档**（页面用表格逐条列出 `nslookup set type` /
`nslookup set debug` / `nslookup set timeout` / `nslookup set retry` … 共 34 行），
本机 `/?` 只用于核对**位置参数的顺序**（`host` 在前、`server` 在后）。
**所有实际用到的长开关都另外做了真机验证**（见 §4），不是只靠文档。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | 字段 | danger | requiresAdmin |
|---|---|---|---|---|---|
| `query` | 解析 | `-type=`（枚举，可留空） | 3 | — | 否 |
| `query-verbose` | 解析 | `-debug`、`-type=` | 4 | — | 否 |
| `query-timeout` | 解析 | `-timeout=`、`-retry=` | 5 | — | 否 |

## 3. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | stderr | 实测输出 |
|---|---|---|---|---|
| `nslookup localhost` | **0** | 92 B | 0 B | `服务器:  UnKnown` / `Address:  fdfe:dcba:9876::2` / `名称:    localhost` / `Address:  127.0.0.1` |
| `nslookup -type=AAAA localhost` | **0** | 49 B | **56 B** | stdout：服务器与地址；stderr：`*** 没有 localhost 可以使用的 IPv6 address (AAAA)记录` |
| `nslookup -type=AAAA example.com` | **0** | 95 B | **0 B** | 正常返回 AAAA 记录 |
| `nslookup -type=MX example.com` | **0** | 105 B | 0 B | 正常返回 MX 记录 |
| `nslookup -timeout=5 -retry=2 example.com` | **0** | 112 B | 0 B | 正常 |
| `nslookup /?` | **1** | **0 B** | 274 B | 帮助全在 stderr |

**最重要的两条实测结论**：

1. **`-type=` 的长开关写法可用**，但必须是**单个 token**（见 §5.1）。
2. **"查不到该类型的记录"会返回退出码 0，只在 stderr 打一行 `***`**。
   所以 **"退出码 0" ≠ "查到了记录"**，判定要看输出文本。本机 `localhost` 只有 IPv4 的
   A 记录（解析成 `127.0.0.1`）与一个 IPv6 的本地地址，没有 AAAA 记录，于是 `-type=AAAA` 就打出那行。

## 4. 编码

stdout 与 stderr 实测都是 **OEM 代码页 936**（stderr 那 56 字节按 cp936 解是完整的中文句子），
所以 `runtime.encoding: oem`。

## 5. 本包特有的坑

### 5.1 `-type=A` 必须是一个 argv token —— 这一条我改了两次才对

- `separator: "="` + `style: literal` 会生成**三个** token：`-type`、`=`、`A`。
  实测**能用**（nslookup 自己会拼），但显示出来是 `nslookup -type = A`，很别扭，而且
  把"一个参数"拆成三个 token 是**依赖被包装程序宽容**，不是规范保证。
- `separator: "="` + `style: attached` 才生成**单个** token `-type=A`，与官方语法一致。
  本清单现在用的是这一种（`prefix: "-type"`、`separator: "="`、`style: attached`）。
- 同一条适用于 `-timeout=` 与 `-retry=`。
- **给后来者的可复用结论**：Windows 命令里大量存在 `-xxx=yyy` / `/xxx:yyy` 这种"开关与取值
  在同一个 token 里"的写法，**用 `attached` + `separator`**，不要用 `separator` + `literal`。

### 5.2 `nslookup` 不带参数会进交互模式

`nslookup`（无参数）会打印 `>` 提示符然后**一直等输入**。所以本包**没有"无参数动作"**，
这也是为什么本包躲过了 `command: ""` 那个阻塞问题（见共用文档 §7.1）—— 巧合，但值得记下。

### 5.3 位置参数顺序是 `host` 在前、`server` 在后

官方语法：`nslookup [-opt ...] host server`。**这决定了字段声明顺序**（字段顺序 = argv 顺序）：
`host` 必须在 `server` 之前声明。若反过来写，会生成 `nslookup <server> <host>`，语义完全颠倒 ——
而且**不会报错**，只会查错地方。这是规范 §3.1"顺序铁律"的一个实例。

## 6. `/?` 与真实输出的实测形态

- `nslookup /?`：**stderr** 274 B、stdout **0 B**、退出码 **1**。
  本批里只有 `nslookup` 与 `netstat` 两个命令的帮助走 stderr。
- 官方页面的表格共 34 行，分三组：子命令（`exit` / `finger` / `help` / `ls` / `lserver` /
  `root` / `server` / `set` / `view`）、`set` 选项（`set all` / `set class` / `set d2` /
  `set debug` / `set domain` / `set port` / `set querytype` / `set recurse` / `set retry` /
  `set root` / `set search` / `set srchlist` / `set timeout` / `set type` / `set vc`）、
  错误信息说明（`timed out` / `No response from server` / `No records` / `Nonexistent domain` …）。
- 官方示例里有 `nslookup -debug -type=A+AAAA -nosearch -recurse mydomain.com 1.1.1.1`
  —— 注意 `-type=A+AAAA` 这种**复合取值**（本清单没做）。

## 7. 故意没做的部分与原因

- **交互模式（无参数 `nslookup`）**：会一直等输入，v1 没有 stdin 能力。
- **`set` 的 15 个选项只做了 4 个**（`type` / `debug` / `timeout` / `retry`），
  其余（`class` / `d2` / `domain` / `port` / `querytype` / `recurse` / `root` / `search` /
  `srchlist` / `vc`）没收：多数是交互模式下的会话设置，命令行一次性查询用不到。
- **子命令 `ls` / `view` / `finger`**：`ls` 是区域传送查询、`finger` 是上古协议、`view` 依赖 `ls`
  的结果，都不适合做成一键动作。
- **`-type=A+AAAA` 这类复合取值**：官方示例里有，但 v1 的 `enum` 表达不了"多选拼成一个 token"。
- **`-nosearch` / `-recurse` / `-d2`**：官方示例里出现，但参数表里只以 `nslookup set xxx` 的形式
  描述，没有写 `-xxx` 的等价写法 → 按 R1 不收录（本清单只收参数表里能一一对上的）。
- 没有 `versionArgs` / `versionPattern` / `minVersion`；没有 `progress.pattern`；没有工具包级 `category`。

## 8. 未实跑的动作与原因（安全红线）

**本包全部动作都是只读的，且都实跑过**（`query`、`query-verbose` 的字段、`query-timeout` 的
超时/重试都验证过等价写法）。没有需要跳过的动作。

**未实跑的具体取值**：`recordType` 枚举里的 `CNAME` / `NS` / `SOA` / `TXT` / `PTR`
（枚举本身经过 schema 与文档核对，但每个取值没有逐一实跑；`A` / `AAAA` / `MX` 实跑过）。

## 9. 规范缺口 / 宿主问题

公共缺口见共用文档 §7。本包额外涉及：

- **§7.4（开关与取值粘成一个 token）** 在本包的正确解法是 `attached` + `separator`（见 §5.1），
  所以 `nslookup` **不需要**规范扩展 —— 但 `sfc` 与 `cleanmgr` 需要（那两处是两个字面量拼接，
  `attached` 也做不到）。这三处放一起看才看得出规范的边界在哪。

## 10. 给后来者的提醒

- 抄官方示例时注意 `-type=A+AAAA` 这种复合取值，本清单不支持。
- 判断 nslookup 是否"查到东西"必须看输出文本，退出码 0 只说明命令跑完了。
