# whoami 工具包 —— 实测记录

> 本包是 **Windows 自带命令工具包**。公共事实（编码不统一、`/?` 走哪个流、
> 退出码各家不同、`command: ""` 的由来、校验器对 `/` 开关不生效等）都在
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md) 里，
> 本文只写 **whoami 特有的部分**，不重复抄公共内容。

## 0. 一句话结论

`whoami.exe` 的开关集合极小（11 个，全都收录），**普通用户可跑、全部只读**。
本包 8 个动作 / 10 个字段 / 出处覆盖 10/10。唯一的"意外"是两条：
**输出编码是 UTF-8 而不是 OEM 代码页**（§4），以及 **`/claims` 在本机没有任何输出**（§3.2）。

---

## 1. 实测环境（环境事实，不是知识）

| 项 | 值 | 怎么取的 |
|---|---|---|
| 系统 | Windows 11，内部版本 `10.0.26200.8655`，DisplayVersion `25H2` | `cmd /c ver`、注册表 `CurrentBuild`/`UBR` |
| 账号 | 非管理员（Microsoft 账户 + 同名本地账号） | `WindowsPrincipal.IsInRole(Administrator)` = **False** |
| 控制台代码页 | **936** | `chcp.com` |
| whoami.exe | `C:\Windows\System32\whoami.exe`，文件版本 `10.0.26100.8875 (WinBuild.160101.0800)` | `Get-Command` / `VersionInfo.FileVersion` |
| 实测日期 | 2026-10-01 | — |

`Get-Command whoami.exe` → `Application`（是 exe，不是内建命令，与 `ver` 不同）。
账号名 `kangmori\steve`（域部分是**机器名** Kangmori，不是域）。

---

## 2. 参数知识来源

1. **官方文档**：<https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/whoami>
   （页面尾部 "Last updated on 2025-05-26"，2026-10-01 抓取核对；HTTP 200）
2. **本机 `whoami /?` 的真实输出**：2805 字节，stdout，退出码 0（用 .NET `Process` + `BaseStream` 抓原始字节）
3. 本文档里的每一条字节数、退出码都是本机实跑结果

**结论：两处来源完全一致，没有任何冲突项。** 开关集合
`{/upn /fqdn /logonid /user /groups /claims /priv /fo /nh /all /?}`、
`/fo` 的三个取值、默认 `table`、三种语法形态（互斥组）逐项对得上。
官方页面与 `/?` 都不提供退出码表，所以 `exitCodes` 全部来自实测（§5）。

---

## 3. 覆盖范围

### 3.1 动作表

| 动作 id | 命令 | 字段 | 危险级别 | 实测退出码 |
|---|---|---|---|---|
| `show` | `whoami` | 0 | none | 0 |
| `user` | `whoami /user` | 2（`/fo` `/nh`） | none | 0 |
| `upn` | `whoami /upn` | 0 | none | **1**（非域账号，见 §3.2） |
| `logonid` | `whoami /logonid` | 0 | none | 0 |
| `groups` | `whoami /groups` | 2（`/fo` `/nh`） | none | 0 |
| `priv` | `whoami /priv` | 2（`/fo` `/nh`） | none | 0 |
| `claims` | `whoami /claims` | 2（`/fo` `/nh`） | none | 0（**但输出为空**） |
| `all` | `whoami /all` | 2（`/fo` `/nh`） | none | 0 |

**为什么 `upn` / `logonid` 没有字段**：官方语法把 `/upn` `/fqdn` `/logonid` 单独放在
第一形态 `whoami [/upn | /fqdn | /logonid]` 里，与 `/user` 那组互斥，**也不接受 `/fo` 与 `/nh`**。
给它们加格式字段就是发明参数（违反 R1），所以只发一个开关。
本机实测 `whoami /fo list`（不带任何形态开关）退出码 1、stderr `ERROR: Invalid syntax.`
—— 反过来印证了"`/fo` 必须依附于某个形态开关"。

**`/fqdn` 刻意没做成动作**：本机实测 `whoami /fqdn` 与 `whoami /upn` 一样是
"非域账号取不到值"（退出码 1，stderr `not a domain user.`），行为与 `/upn` 完全同构；
本包收录 `/upn` 作为这一对互斥开关的代表（它是 UPN 这条路上更常被问到的那个）。
要 `FQDN` 时可以直接在自定义参数里补 `/fqdn` —— 这也是 §9 的"故意没做"之一。

### 3.2 两处与直觉不同的实测结果

1. **`whoami /claims` 在本机什么也不输出**：stdout **0 字节、stderr 0 字节、退出码 0**。
   `/claims /fo list`、以及 `/all` 输出里的 CLAIMS 段同样没有内容
   （`/all` 实测 4192 字节，只有 USER / GROUP / PRIVILEGES 三段）。
   当前账号是非域账号、没有动态访问控制声明，所以没有 claim 可显示。
   收录它是为了如实覆盖官方参数表；`help` 里已写明"当前账号形态下没有输出"。2. **`whoami /groups` 里出现了中文组名**：`NT AUTHORITY\本地帐户和管理员组成员`（S-1-5-114）、
   `NT AUTHORITY\本地帐户`（S-1-5-113）、`NT AUTHORITY\云帐户身份验证`（S-1-5-64-36）、
   以及 `MicrosoftAccount\steve-wzw@outlook.com`。**只有组名是中文，其它全是英文**
   —— 说明 whoami 的界面字符串没有本地化，中文只来自系统转译后的账户名。
   这也是 §4 判编码的样本来源。

---

## 4. 输出编码：实测是 **UTF-8**（不是 OEM）⚠️

**判定方法**（照 `docs/ai/playbook-tool-package.md` §10.3 第 14 条）：
用 `whoami /groups` 的中文组名当样本，看**原始字节**：

```
4e 54 20 41 55 54 48 4f 52 49 54 59 5c  e6 9c ac e5 9c b0 e5 b8 90 e6 88 b7  e5 92 8c …
N  T     A  U  T  H  O  R  I  T  Y  \   [   本        地        ］帐户 …        ]
```

- `e6 9c ac` 正是「本」的 **UTF-8** 编码（GBK 里「本」是 `b1 be`，`be` 会紧跟 `b5`）；
- `e5 9c b0` = 「地」的 UTF-8；整段 `e6 9c ac e5 9c b0 e5 b8 90 e6 88 b7` = 「本地帐户」；
- 用**严格 UTF-8**（`UTF8Encoding(false, throwOnInvalidBytes: true)`）解码
  `whoami /groups`（3289 B）与 `/all`（4192 B）的完整输出：**全部成功，不抛异常**；
- 偶数位为 `0x00` 的比例 **0%** → 不是 UTF-16LE。

因此 `runtime.encoding: utf-8`。

> **和另外 10 个 Windows 包不一样**：那批（ping/ipconfig/tasklist/systeminfo/…）实测都是
> `oem`(936)，只有 `sfc` 是 utf-16le。whoami 是同一个系统上第三个不同的答案 ——
> 这印证了 playbook 第 13 条"编码必须逐包实测，包之间不能互抄"。
> （`whoami /priv`、`/user`、`/?` 这些**纯 ASCII** 输出的包在两种编码下都解得对，
> 只用它们判编码会得出错误结论。）

**没验的部分**：换一台非中文 Windows（437 / 932 代码页）时 whoami 是否仍输出 UTF-8，
本机无法验证。由于 UTF-8 与代码页无关，这个选择在换机器时是**安全的**（不会比 936 更差）。

---

## 5. 退出码与真机冒烟（全部实跑）

### 5.1 成功路径（退出码 0）

| 命令 | stdout | stderr | exit |
|---|---|---|---|
| `whoami` | 16 B `kangmori\steve\r\n` | 0 | 0 |
| `whoami /user` | 229 B | 0 | 0 |
| `whoami /groups` | 3289 B | 0 | 0 |
| `whoami /priv` | 668 B | 0 | 0 |
| `whoami /logonid` | 18 B `S-1-5-5-0-359587\r\n` | 0 | 0 |
| `whoami /claims` | **0 B** | 0 | 0 |
| `whoami /all` | 4192 B | 0 | 0 |
| `whoami /all /fo list` | 2890 B | 0 | 0 |
| `whoami /user /fo csv /nh` | 67 B | 0 | 0 |
| `whoami /?` | 2805 B（帮助） | 0 | 0 |

**顺带记录一个输出长度的小抖动**：分两次会话把同一批命令各跑一遍，
`/groups` 得到过 **3289 B** 与 **3245 B**、`/all` 得到过 **4192 B** 与 **4148 B**、
`/all /fo csv /nh` 得到过 **1880 B** 与 **1836 B**（差值恒为 **44 B**）。
两个值在各自会话里都是**稳定重复**的（连续 12 次一致），所以不是读取竞态。
44 B 是"有内容的一行列宽"的量级，**推测**是某个组的**名字长度/列宽**在两次会话里不同
（表格列宽按最长内容自适应）—— **这一点没有验证到**（两次的逐行原文没有同时留存），
在这里如实标为推测，不当结论用。
**对清单没有影响**：正因为列宽自适应，清单里**不写**"输出一定是××字节"这种断言，
`resultNote` 里的字节数都注明是"实测本机"。

### 5.2 失败路径（这就是 exitCodes 里那条 1 的全部依据）

| 命令 | stdout | stderr（摘要） | exit |
|---|---|---|---|
| `whoami /nosuchswitch` | 0 | `ERROR: Invalid argument/option - '/nosuchswitch'.` + `Type "WHOAMI /?" for usage.` | **1** |
| `whoami /user /user` | 0 | `ERROR: Invalid syntax. '/user' option is not allowed more than '1' time(s).` | **1** |
| `whoami /all /user` | 0 | `ERROR: Invalid syntax.` | **1** |
| `whoami /fo list` | 0 | `ERROR: Invalid syntax.` | **1** |
| `whoami /upn`（非域账号） | 0 | `ERROR: Unable to get User Principal Name (UPN) as the current logged-on user is not a domain user.` | **1** |
| `whoami /fqdn`（非域账号） | 0 | `ERROR: Unable to get Fully Qualified Distinguished Name (FQDN) as … not a domain user.` | **1** |

**结论**（已写进清单的 `exitCodes`）：`0` = 成功；`1` = 用法错误**或**当前账号形态下查不到
（两种原因都返回 1，且**错误一定写 stderr、stdout 为空**）。
**没有**发现其它退出码。

### 5.3 宿主会多跑一次 whoami（提醒，不是缺陷）

`ToolLocator.LocateAsync` 在 `versionPattern` 为空时**仍然**会把
`locate.versionArgs ?? []` 跑一遍来取版本输出。本包没写 `versionArgs`，于是宿主会跑一次
**无参数的 `whoami.exe`**。对 whoami 来说这一跑是无害的：它只打印 `域\用户名`（16 B）、退出码 0、
不改任何状态。这里如实记录，是因为"取版本会真的执行一次程序"这件事在别的命令上可能有副作用。

**没验的部分**：`dotnet test` 里针对本包的**逐动作 argv 断言**（`RealManifestTests` 的
`解压动作能生成预期的 argv` 那类）只覆盖 7zip，不会覆盖 whoami 的 argv —— 所以
"每个动作组装出的 argv 是预期的"，本包是靠 §5.4 的逐条真跑来验证的，不是靠单测。

### 5.4 宿主侧集成验证（已跑，全绿）

```
PS> dotnet test src\AllTool.slnx --nologo
已通过! - 失败: 0，通过: 176，已跳过: 0，总计: 176，持续时间: 58 s
```

其中 `RealManifestTests.加载全部工具包都不应抛异常` 会 `ManifestLoader.LoadAll(plugins/)`
把**仓库里所有**工具包都加载一遍（本包在内）。这是宿主对清单的**运行时结构校验**
（`ManifestValidation`）的实测通过 —— 包括 `command: ""`、
动作级 `title`、`sources[].retrieved` 的 `YYYY-MM-DD` 格式等都被这一层检查过。
**注意**：这次全绿是在 176 个测试上取得的，与本次三个包同时存在的其它包（别的智能体正在
并行新增）一起通过；本包不依赖它们。

---

## 6. requiresAdmin：**不需要**（实测，不是照文档抄）

官方页面**一个字都没写**权限要求。实测（2026-10-01，**非管理员**账号）：

- 8 个动作对应的命令**全部退出码 0**（除 `/upn` 因"非域账号"返回 1，与权限无关）；
- `whoami /priv` 正常列出 6 条特权，可见它读的是**当前进程令牌**，不需要特权；
- 因此清单里没有写 `requiresAdmin`（包级 `runtime.requiresAdmin: false`，动作级一律不写）。

**没验的部分**：没有测"以管理员身份跑会不会输出不同内容"。
按文档语义 `/priv` 在提权会话里会多出 `SeDebugPrivilege` 之类的条目 ——
那是**输出内容**的差异，不是"需要管理员才能跑"，所以不影响 `requiresAdmin` 的判定。

---

## 7. 字段风格：为什么每个字段用 `separate`、`/fo` 的空值怎么处理

- `/fo` **没有**用 `style: attached`（`/fo:list`）而用 `style: separate`（`/fo list`）：
  官方语法行写的是 `/fo <format>`（两个 token），本机 `/?` 的写法也是 `/FO format`；
  而本机实测的命令行全部是两 token 形式（`WHOAMI /USER /FO LIST` 等官方示例同样是两 token）。
  **一个不能省的实测事实**：`whoami /fo:list`（单 token）本机未测
  —— 没有官方依据、也没有实测支持的写法不写进清单。
- `/fo` 的"表格（默认）"选项写成 `value: ""` + `isDefault: true`：按规范 §3.3 的空值规则，
  空值不产生任何 token，于是"默认表格"就等于**不加 `/fo`**，这正是官方"table is the default"的行为。
- `/nh` 用 `style: flag`、`default: false`（用户不勾就不发），
  与官方"valid only for table and CSV formats"的说明一致；**没有**把它做成枚举去限制组合
  （`/nh` 与 `list` 一起用时表格头本来就不存在，whoami 自己会忽略它，多条命令实测均 exit 0）。

---

## 8. 判"编码/退出码/权限"时踩到的坑（给后来者）

1. **不要用纯 ASCII 的输出判编码**。`whoami`（无参数）、`/user`、`/priv`、`/?`
   在本机全都是纯 ASCII，用它们判编码一定是错的；只有 `/groups` 与 `/all` 带中文组名。
2. **换行是 CRLF**：所有输出行尾都是 `0d 0a`。写"多行匹配"的正则时要考虑 `\r`
   （本包没有 `nextSteps`/`progress` 正则，所以清单里没有这个风险）。
3. **`whoami /user /user` 这种"同一个开关写两遍"也返回 1**：这不是未知开关，
   而是"语法互斥/唯一性"被违反。写自动化判定时别只匹配 `Invalid argument`。
4. **中文只出现在组名里**，界面文案（USER INFORMATION / Group Name / Attributes…）全是英文
   —— 所以**不要用英文标签当"成功标志"之外的语义推断**，老老实实用退出码。

---

## 9. 故意没做 / 没验的部分

| 项 | 原因 |
|---|---|
| `/fqdn` 单独做成动作 | 与 `/upn` 完全同构（非域账号同样 exit 1），收录一个作代表即可；差异写在本节 |
| `whoami /fo:list`（单 token 写法） | 官方语法与官方示例、本机实测都是两 token；单 token 无依据、未实测，不写 |
| 非中文 / 非 936 系统上的编码 | 本机无法验证；因选定 UTF-8 与代码页无关，风险低（§4） |
| 管理员会话下的 `/priv` 输出差异 | 当前账号非管理员，无法提权实测（§6） |
| `dotnet test src\AllTool.slnx` 里 whoami 的逐动作 argv 断言 | 宿主测试只对 7zip 断言具体 argv；whoami 的 argv 由 §5.4 的真机冒烟逐条验证 |
| `nextSteps` / `quickActions` | 这几个动作之间没有"跑完该接着做什么"的必然关系，凭空推荐违反"规则要对真实输出验证过" |

## 10. 临时对象申报

**没有创建任何临时对象。** 本包全部动作都是只读查询；实测只在
`%TEMP%\alltool-probe\` 下落了几个**探针输出文本**（`probe.ps1` / `probe2.ps1` / `whoami-matrix.txt` /
`diag.py` / 三个官方页面 HTML 与提取出的纯文本），它们都在系统临时目录里、**不在仓库内**，
收尾时可整体删除，不影响仓库状态：

```powershell
Remove-Item -Recurse -Force (Join-Path $env:TEMP 'alltool-probe')
```

仓库内**只新增** `plugins/whoami/manifest.yaml` 与 `plugins/whoami/NOTES.md` 两个文件。

---

## 11. 与官方文档对不上的地方

**没有。** 开关集合、`/fo` 取值与默认值、三种语法形态、参数说明全部逐项一致（§2）。
唯一的"文档没写"是**退出码**（官方页面没有退出码章节）与**权限要求**（官方一个字没写）
—— 这两项本包按"行为以实测为准"处理，全部实测记录在 §5、§6。
另外官方页面把 `/fqdn` 描述为 "fully qualified domain name (FQDN) format"，
而本机 `/?` 写的是 "Fully Qualified **Distinguished** Name (FQDN) format" ——
两种说法指同一个开关，属于**帮助文本本身**的措辞差异（不是参数差异），仅记录在此。
