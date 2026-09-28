# ipconfig 工具包说明（NOTES）

> 目标程序：`C:\Windows\System32\ipconfig.exe`（文件版本 10.0.26100.8521）—— Windows 自带。
> 清单：动作 11 个 / 字段 12 个 / 字段出处标注 **100%**
> **公共事实（编码、退出码、`/?` 形态、规范缺口）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)**，
> 本文只写本包特有的内容。实测环境：Windows 11 `10.0.26200`，账号 `KANGMORI\Steve`（非管理员）。

---

## 1. 参数知识来源

| # | 来源 | 位置 |
|---|---|---|
| 1 | 官方文档 | `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig` → 快照 `docs/reference/win-docs/ipconfig.html` |
| 2 | 本机 `ipconfig /?` | 快照 `docs/reference/win-help/ipconfig.txt`（2296 字节，写 stdout，退出码 1，**不入库**） |

**开关是拿本机 `/?` 核对过的**：`/all`、`/release`、`/release6`、`/renew`、`/renew6`、`/flushdns`、
`/registerdns`、`/displaydns`、`/showclassid`、`/setclassid` 都能在 `ipconfig.txt` 的"选项"一节找到。

## 2. 覆盖范围

| 动作 id | 分组 | 开关 | danger | requiresAdmin |
|---|---|---|---|---|
| `show` | 地址信息 | （无） | — | 否 |
| `show-all` | 地址信息 | `/all`（+ `/allcompartments`） | — | 否 |
| `show-dns-cache` | DNS | `/displaydns` | — | 否 |
| `flush-dns` | DNS | `/flushdns` | overwrite | **是** |
| `register-dns` | DNS | `/registerdns` | overwrite | **是** |
| `release` / `renew` | 地址信息 | `/release` `/renew` | overwrite | **是** |
| `release6` / `renew6` | 地址信息 | `/release6` `/renew6` | overwrite | **是** |
| `show-class-id` | DHCP 类 ID | `/showclassid` | — | 否 |
| `set-class-id` | DHCP 类 ID | `/setclassid` | overwrite | 否 |

## 3. `requiresAdmin` 的判定说明（本包要特别说清楚）

**官方文档与 `/?` 都没有明说哪些开关要提权。** 本包标了 6 个 `requiresAdmin: true`
（`flush-dns` / `register-dns` / `release` / `renew` / `release6` / `renew6`），依据是：

- 这 6 条都会**改动系统网络状态**（改 DHCP 租约、改 DNS 缓存、改注册），
  在 Windows 上这类操作按惯例需要在提升的命令行里执行；
- **但这不是官方文字依据，也没有实测**（本机不是管理员，一律未跑）。

`set-class-id` 同样会改状态，但**没有**标 requiresAdmin —— 原因是我没有找到任何依据，
宁可"少标"也不编造。这一条在清单里也写明了。**这个取舍本身是给产品负责人的一个待决问题**：
`requiresAdmin` 该只在"有官方文字或实测依据"时标，还是允许按"会改系统状态"的经验判断标？
（本次采用前者，理由是 R1 的精神：不发明。）

## 4. 真机冒烟测试结果（含退出码）

| 命令 | 退出码 | stdout | 实测输出首行 |
|---|---|---|---|
| `ipconfig` | **0** | 2053 B | `Windows IP 配置` |
| `ipconfig /all` | **0** | 5263 B | `Windows IP 配置`（含主机名、DNS 后缀、MAC、DHCP、DNS 服务器） |
| `ipconfig /displaydns` | **0** | **1 647 669 B（1.6 MB）** | `Windows IP 配置` |
| `ipconfig /?` | **1** | 2296 B | `用法:` |

**环境事实**：本机跑 `ipconfig` 会看到一个叫 `Mihomo` 的未知适配器、地址 `28.0.0.1`
—— 那是 Clash Verge 的 fake-ip DNS 适配器（见 `docs/ai/development.md` §4.4），不是清单的问题。

**未跑**：`/flushdns`、`/registerdns`、`/release`、`/renew`、`/release6`、`/renew6`、
`/showclassid`、`/setclassid`、`/allcompartments` —— 都会改系统状态或需要管理员（见 §6）。

## 5. 编码

实测 stdout 为 **OEM 代码页 936**（`ipconfig /?` 2296 B、真实输出 2053/5263 B 都是），
所以 `runtime.encoding: oem`。原始字节证据见共用文档 §5。

## 6. 本包特有的坑

1. **`/displaydns` 的输出量极大**：本机 1.6 MB。宿主会截断但仍如实计数，
   清单的 `resultNote` 里写了这一点。做界面时要预期"一屏刷不完"。
2. **`/release` 与 `/renew` 不指定适配器时作用于全部适配器** —— 官方文档明说
   "如果未指定适配器名称，则会释放或更新所有绑定到 TCP/IP 的适配器的 IP 地址租用"。
   所以 `adapter` 字段留空是**危险**的合法输入，已在 `confirmText` 里点明。
3. **`/setclassid` 不填类 ID 等于删除类 ID** —— 官方文档与 `/?` 都写了。
   清单的 `classId` 字段 `help` 里写明了这一点。
4. **适配器名允许通配符 `*` 和 `?`**（官方文档与 `/?` 都写了），且官方示例里
   适配器名含空格（`Local Area Connection`）。宿主会自动加引号。
5. **`/?` 里有 `/allcompartments`，但官方网页的语法行没有它**。本清单保留了它
   （标为高级），并在 `doc` 里如实注明"官方语法行未列该开关，只有本机 `/?` 有"。
   这是本批唯一一处"明知来源只有一侧却仍然收录"的字段 —— 如果产品负责人要求更严格，
   删掉这个字段即可（删除不影响其它任何东西）。

## 7. `/?` 与真实输出的实测形态

- `ipconfig /?`：**stdout** 2296 B、stderr 0 B、**退出码 1**（帮助本身返回错误码）。
- 用法行：
  `ipconfig [/allcompartments] [/? | /all | /renew [adapter] | /release [adapter] | /renew6 [adapter] | /release6 [adapter] | /flushdns | /displaydns | /registerdns | /showclassid adapter | /setclassid adapter [classid] | /showclassid6 adapter | /setclassid6 adapter [classid] ]`
- 本机 `/?` 有 6 组示例（`ipconfig`、`ipconfig /all`、`ipconfig /renew`、`ipconfig /renew EL*`、
  `ipconfig /release *Con*`、`ipconfig /allcompartments`）。

## 8. 故意没做的部分与原因

- `/showclassid6` 与 `/setclassid6`：**只有本机 `/?` 有，官方网页的语法行与参数表都没有** → 不收。
  这是"官方优先"的又一处应用。要做只需照 `/showclassid` / `/setclassid` 复制两份。
- `versionArgs` / `versionPattern` / `minVersion`：Windows 命令没有版本开关（共用文档 §4）。
- `progress.pattern`：ipconfig 没有进度输出。
- 没有工具包级 `category`：按产品负责人要求，只定义动作分组。

## 9. 未实跑的动作与原因（安全红线）

| 动作 | 命令 | 未跑原因 |
|---|---|---|
| `flush-dns` | `ipconfig /flushdns` | 改 DNS 缓存（会改系统状态） |
| `register-dns` | `ipconfig /registerdns` | 刷新 DHCP 租约并重新注册 DNS 名 |
| `release` / `renew` | `ipconfig /release` `/renew` | **会断开网络**，且需要管理员 |
| `release6` / `renew6` | `ipconfig /release6` `/renew6` | 同上（IPv6） |
| `show-class-id` / `set-class-id` | `/showclassid` `/setclassid` | 改 DHCP 类 ID；且需要真实适配器名 |
| `show-all` 的 `/allcompartments` | `/allcompartments` | 本机没有容器隔离舱，无意义 |

**实测确认"跑不通"的是**：以上带 `requiresAdmin` 的动作在当前非管理员账号下必然因权限失败，
所以本包**没有任何写动作被实跑**。

## 10. 规范缺口 / 宿主问题

公共缺口（`command: ""` 被宿主校验拒绝、校验器只认 `-` 开关、`locate` 取不到文件版本、
开关与取值无法粘成一个 token、`requiresAdmin` 文档过期）见共用文档 §7。

**本包另外涉及的一条**：`requiresAdmin` 的判定标准（官方依据 vs 经验判断）需要一个明确规则，
见本文 §3。

## 11. 给后来者的提醒

- 想加 `/showclassid6` / `/setclassid6` 的话，先在官方页面确认（目前只在 `/?` 里）。
- `ipconfig /release` 会真的断网。测试网络命令请用 `ping`/`tracert`/`netstat` 这类只读的。
