# `reg` 工具包 —— 实测记录（NOTES）

> 本包只做**只读查询**：只收录 `reg query`。
> 公共结论（Windows 自带命令的通用坑：没有 `--version`、帮助写哪个流、退出码不是"0＝成功"等）
> 见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)；本文只写 `reg` 特有的部分。

---

## 0. 环境事实（本机实测，不是记忆）

| 项 | 值 | 怎么来的 |
|---|---|---|
| 操作系统 | Windows 11 `10.0.26200`（25H2），zh-CN | `[Environment]::OSVersion` + `HKLM\...\CurrentVersion\DisplayVersion` |
| 控制台代码页 | 936（系统 OEMCP=936、ACP=936） | `chcp` / `Nls\CodePage` |
| 账号 | `<机器名>\<用户名>`，**非管理员** | `WindowsPrincipal.IsInRole(Administrator)` = False |
| 可执行文件 | `C:\Windows\System32\reg.exe`（`SysWOW64\reg.exe` 也存在） | `Get-Command` / `Test-Path` |
| 文件版本 | **10.0.26100.8875** | `(Get-Item ...).VersionInfo.FileVersion` |
| 实测日期 | 2026-10-01 | — |

**没有版本开关**：`reg` 没有 `--version`，`/?` 是帮助而不是版本输出，无参数运行会「无效语法」（不是"跑命令"）。
按本批惯例（`windows-commands.md` §4）本包**不写** `versionArgs` / `versionPattern` / `minVersion`，
文件版本只记在上表里。

---

## 1. 参数知识来源

| 类别 | 位置 | 取到日期 | 适用版本 |
|---|---|---|---|
| 官方命令索引 | <https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/reg> | 2026-10-01 | Win 10/11、Server 2016-2025 |
| 官方参考页（字段依据全部来自它） | <https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/reg-query> | 2026-10-01 | 同上 |
| 本机 `reg /?` 真实输出 | `C:\Windows\System32\reg.exe`（快照不入库，可再生） | 2026-10-01 | reg.exe 10.0.26100.8875 |
| 本机 `reg query /?` 真实输出 | 同上 | 2026-10-01 | 同上 |

两个帮助页都实跑：**退出码都是 0、都写 stdout、stderr 0 字节**。
`reg /?` 只有 577 字节（是操作索引，纯 ASCII），真正的开关说明在 `reg query /?` 里。

复现：

```powershell
# 无控制台条件（与宿主一致）抓原始字节
$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "$env:SystemRoot\System32\reg.exe"; $psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
[void]$psi.ArgumentList.Add('query'); [void]$psi.ArgumentList.Add('/?')
$p = [Diagnostics.Process]::Start($psi); $p.WaitForExit()
$b = [IO.File]::ReadAllBytes($outfile)   # 或 $p.StandardOutput.BaseStream 直接读原始字节
[Text.Encoding]::GetEncoding(936).GetString($b)
```

---

## 2. 覆盖范围

| 动作 | 实际命令行 | 字段 | execution | danger | 实测退出码 |
|---|---|---|---|---|---|
| `query-key` | `reg query <键> [/se] [/t] [/z] [/reg:…]` | 5 | run | — | 0 |
| `query-recursive` | `reg query <键> /s [/se] [/t] [/z] [/reg:…]` | 6 | run | — | 0 |
| `query-value` | `reg query <键> /v <值名> [/z] [/reg:…]` | 4 | run | — | 0（值不存在则 1） |
| `query-default-value` | `reg query <键> /ve [/z] [/reg:…]` | 4 | run | — | 0 |
| `query-search` | `reg query <键> [/s] /f <数据> [{/k\|/d}] [/c] [/e] [/t] [/z] [/reg:…]` | 9 | run | — | 0（命中）/ 1（0 命中） |
| `query-search-value-names` | `reg query <键> /v /f <数据> [/t] [/z] [/reg:…]` | 7 | run | — | 0 |
| `query-help` | `reg query /?` | 0 | run | — | 0 |

合计 **7 动作 / 35 字段**，字段出处标注 35/35 = 100%。
刻意**不写工具包级 `category`**（与第一批 Windows 命令包一致：只由清单定义动作分组，工具包归到哪一组由用户自己决定）。

---

## 3. 字段风格实测（`separate` vs `attached`）—— 这一层只有真机能发现

统一样本：`reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" ...`。
每条都跑了「两 token」与「一 token」两种写法：

| 开关 | 两 token（separate） | 一 token（attached） | 结论 |
|---|---|---|---|
| `/v` | `/v CurrentVersion` → **exit 0** ✔ | `/vCurrentVersion` → exit 1（stderr `错误: 无效语法。`）✗ | `separate` |
| `/f` | `/f Current` → **exit 0** ✔ | `/fCurrent` → exit 1 ✗ | `separate` |
| `/t` | `/t REG_SZ` → **exit 0** ✔ | `/tREG_SZ` → exit 1 ✗ | `separate` |
| `/se` | `/se #` → **exit 0** ✔ | `/se#` → exit 1 ✗ | `separate` |
| `/reg:32` `/reg:64` | —（不是这个形态） | `/reg:32`、`/reg:64` → **exit 0** ✔；`/reg32` → exit 1 ✗ | 单 token、冒号在 token 里 → `literal`（每个取值带 `args`） |

**键路径是位置参数**：`reg query <键>` 直接跟在 `query` 后面（官方语法行的第一个参数就是 `<keyname>`），
本清单用 `style: positional`。实测带空格的键路径必须作为**一个 argv** 传（见 §6.3）。

**字段声明顺序 = argv 顺序**，本清单按**官方语法行的顺序**排：
`<keyname>` → `[/v | /ve]` → `[/s]` → `[/se]` → `[/f …]` → `[{/k | /d}]` → `[/c]` → `[/e]` → `[/t]` → `[/z]` → `[/reg:32 | /reg:64]`。
顺序其实**不严格**（实测 `/f Current /k /s` 与 `/s /f Current /k` 都能 exit 0），
但按文档顺序排能让"界面上的顺序"和"官方语法行"对得上，可读性最好。

---

## 4. 输出编码：**oem（本机 cp936）** —— 并且它随「控制台条件」整份变

### 4.1 结论

`runtime.encoding: oem`。判据：**用带中文的真实输出看原始字节**，
样本是系统上本来就有的、含中文的只读键
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HuorongSysdiag`（`DisplayName` = `火绒安全软件`）。

### 4.2 原始字节证据（同一条命令、三种控制台条件）

| 条件 | 字节数 | 第一个非 ASCII 起 12 字节（hex） | 按 cp936 解 | 按 UTF-8 解 | 严格 UTF-8？ |
|---|---|---|---|---|---|
| **无控制台**（`CreateNoWindow=true` + 重定向，**= 宿主条件**） | 502 | `bb f0 c8 de b0 b2 c8 ab c8 ed bc fe` | **火绒安全软件** ✔ | 乱码 | **False** |
| 继承控制台（本机 pwsh 的控制台，其输出代码页 = 65001） | 520 | `e7 81 ab e7 bb 92 e5 ae 89 e5 85 a8 e8 bd af e4 bb b6` | 乱码 | **火绒安全软件** ✔ | True |
| 同上并显式 `chcp 65001` | 520 | 与上行**逐字节相同** | 乱码 | **火绒安全软件** ✔ | True |

另用「全新控制台」再验一次（`cmd.exe` 新窗口，新控制台默认代码页 936）：
子进程里 `chcp` 报 `Active code page: 936`，`reg query <Fonts>` 输出 **29209 字节、utf8strict=False**（cp936）；
把同一条件改成先 `chcp 65001`，`chcp` 报 65001，输出 **29261 字节、utf8strict=True**（UTF-8）。
⇒ **决定因素是控制台输出代码页**：936（或没有控制台，回落到系统 OEMCP 936）→ cp936；65001 → UTF-8。

> ⚠ **这一步我自己先踩了一次坑**：第一版探针在脚本开头写了
> `[Console]::OutputEncoding = [Text.Encoding]::UTF8`（这是本项目的常规习惯），
> 于是**我把自己量成了 utf-8**、还得到"汇总行是英文"的错误结论。
> 换到"无控制台"条件重测才看到真相。教训与 `windows-commands.md` §5.3 一致：
> **判编码必须写清当时的控制台条件，而且探针不要顺手改控制台编码。**

### 4.3 附带发现：不止编码变，**语言**也变

同一条命令在两处的措辞不同（都是实测原文）：

| 流 | 无控制台（宿主条件，cp936 中文） | 控制台输出代码页 65001（英文） |
|---|---|---|
| stdout | `搜索结束: 找到 6 匹配。` | `End of search: 6 match(es) found.` |
| stdout | `    (默认)    REG_SZ    (数值未设置)` | `    (Default)    REG_SZ    (value not set)` |
| stderr | `错误: 无效语法。` | `ERROR: Invalid syntax.` |

⇒ **绝对不能用"提示措辞"判断成败**，也不能拿 `End of search` 之类的英文短语做 `nextSteps` 正则。
本清单的 `nextSteps.when` 用的是纯结构正则 `(?m)^\s{4}\S.*?\s{4}REG_[A-Z_]+`，
对着两种语言的真实输出都命中（英文条件 32 命中、中文条件 6 命中）；只用 `REG_` 这种
**数据里本来就有的类型标记**，不依赖任何一句提示文案。

### 4.4 帮助输出也一样

| 命令 | 无控制台（宿主条件） | 控制台代码页 65001 |
|---|---|---|
| `reg /?` | 中文、cp936、约 577→（顶层页全 ASCII） | 纯 ASCII |
| `reg query /?` | **2282 字节、cp936 中文** | 3015 字节、纯 ASCII 英文 |

---

## 5. 退出码实测（含全部失败路径）

官方 `reg-query` 的 Remarks 与 `reg /?` 都只给两个码：
**0 = Successful、1 = Failed**（原文还注了 *Except for REG COMPARE*）。实测把这两个码的边界摸清了：

### 5.1 退出 0

| 命令 | 结果 |
|---|---|
| `reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"` | 11962 B / 0 |
| `reg query HKCU\Environment /s` | 1449 B / 0 |
| `reg query <Fonts> /v "华文中宋 (TrueType)"` | 126 B / 0（**中文带空格的值名，argv 直接过**） |
| `reg query HKLM\SOFTWARE /ve` | 59 B / 0 |
| `reg query HKCU\Environment /ve` | 73 B / 0（默认值没设置也算成功，数据列写「(数值未设置)」） |
| `reg query "…\CurrentVersion" /f Current` | 363 B / 0（末尾 `搜索结束: 找到 6 匹配。`） |
| `reg query "…\CurrentVersion" /s /f Current /k` | 16555 B / 0 |
| `reg query "…\CurrentVersion" /v /f Current` | 363 B / 0（**证明"/v 紧跟 /f"的无参数写法被接受**） |
| `reg query HKCC` | 60 B / 0 |
| `reg query HKLM\SAM`（非管理员） | **30 B / 0** —— 只打印 `HKEY_LOCAL_MACHINE\SAM\SAM`（子项名） |
| `reg query HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion /reg:32` | 5913 B / 0（与 `/reg:64` 的 11962 B 明显不同） |
| `reg /?`、`reg query /?` | 0 |

### 5.2 退出 1 —— **同一个码管"没找到"和"真失败"**

| 情形 | 退出码 | stdout | stderr（cp936 原文，无控制台条件） |
|---|---|---|---|
| 键不存在 | 1 | 0 B | 38 B `错误: 系统找不到指定的注册表项或值。` |
| 值不存在（`/v`） | 1 | 4 B（空行） | 38 B 同上 |
| `/f` 搜索 **0 命中** | 1 | 27 B `搜索结束: 找到 0 匹配。` | **0 B（空）** |
| `/k` 在无同名子项的键上搜 | 1 | 27 B 同上 | 0 B |
| 语法错误（缺键、`/v`+`/ve` 同给、`/k` 不带 `/f`、`/se` 长度≠1、`/t` 类型名非法、`/reg:32`+`/reg:64` 同给、未知开关 `/zzz`） | 1 | 0 B | 54 B `错误: 无效语法。` + `键入 "REG QUERY /?" 了解用法信息。` |
| 根键名写错（`HKLM2\SOFTWARE`） | 1 | 0 B | 54 B `错误: 无效项名。` + 同样的第二行 |
| **权限不足**（`reg query HKLM\SECURITY`，非管理员） | 1 | 0 B | 19 B `错误: 拒绝访问。` |

⇒ **`exitCodes` 的严重度定为 `warning` 而不是 `error`**：本包里 1 最常见的成因是"没查到"
（键/值打错、搜索无命中），把它染成红色错误会误导用户；真失败的文本在输出区里一眼能看出。
区别"没找到"与"真失败"的正确姿势：**看它在哪个流上写了什么**，不要只看数字。

---

## 6. 这个工具咬人的地方

### 6.1 编码会随控制台条件整份变（最坑的一条）
见 §4。同一个 exe、同一条命令，在"无控制台"下是 cp936 中文、在"65001 控制台"下是 UTF-8 英文。
**新做包时不要照抄本结论，也不要照抄别的包** —— 必须写清控制台条件重测。

### 6.2 「0 命中」也是退出码 1
`reg query <键> /f <模式>` 没搜到时，**退出码 1、stderr 空**，只在 stdout 写一句 `搜索结束: 找到 0 匹配。`。
这与 `icacls /findsid` 零命中返回 1332 是同一类坑（"码不等于成败，要配合输出看"）。

### 6.3 不要手写引号
宿主按 argv 数组直接传参、不经过 shell。实测：把带空格的键路径作为一个 argv 传进去完全正常；
反过来，如果用户在界面里自己写了 `"…"`，那两个引号会变成键名/值名的一部分。
（对比：早期探针里我用 `Start-Process -ArgumentList` 传数组，PowerShell 把数组用空格拼起来且不加引号，
带空格的键立刻变成"系统找不到指定的注册表项" —— 这正是"绕过 shell 直接传 argv"的价值。）

### 6.4 `/v` 紧跟 `/f` 是一种"无参数开关"写法，**依赖字段顺序**
`/v` 的值名参数"只有与 `/f` 一起时才可省略"，省略后含义变成"只在值名称中搜索"。
也就是说 `/v /f X` 与 `/v Y /f X` 是两条不同的命令，而**它靠的是 `/v` 后面紧跟 `/f`**。
本清单把它独立成 `query-search-value-names` 动作、并把 `/v` 声明在 `/f` **之前**，
就是用"声明顺序 = argv 顺序"这条规则表达它（`docs/spec/manifest-v1.md` §3.1）。

### 6.5 `/s` 的输出量
`/s` 的作用范围是整棵子树。实测小键：`HKCU\Environment /s` 1449 B / 12 ms；
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion /f Current /k /s` 16555 B / 285 ms。
**根键级递归（`HKLM /s`、`HKCU /s`）按红线没有实跑**（见 §8），
但官方在这一页给的示例就是 `reg query HKLM\SOFTWARE\Microsoft /s /f asp.net` 这种量级 —— 请务必先缩键。

### 6.6 键存在但没有值 → 退出码 0
`reg query HKLM\SAM`（非管理员）返回 0，只打印一行键名/子项名。
所以"退出码 0"不等于"有内容"。

### 6.7 大小写与"完全匹配"必须一起想
`/f CURRENT /c /e` 在 `CurrentVersion` 这个键上 **0 命中**（因为值名是 `CurrentBuild` 这种大小写）；
`/f CurrentBuild /c /e` 命中 1 处。默认搜索是**不区分大小写**的（官方原文）。

---

## 7. 与官方文档对不上的地方（逐条）

| # | 官方页（reg-query） | 本机 `reg query /?` | 实测 | 本清单怎么处理 |
|---|---|---|---|---|
| 7.1 | `/t` 的合法类型列 **6** 种：`REG_SZ` `REG_MULTI_SZ` `REG_EXPAND_SZ` `REG_DWORD` `REG_BINARY` `REG_NONE` | 列 **7** 种，多一个 **`REG_QWORD`** | 未单独验证（不打算用） | 按任务口径"**只写两边都能对应的**"：`type` 枚举只收 6 种，**不收录 `REG_QWORD`**，差异记在这里 |
| 7.2 | `/se` 排在 `/s` 之后 | `/se` 排在 `/z` 之后 | 两种位置实测都 exit 0 | 都收录；**字段声明顺序按官方页**排（`/se` 在 `/t` 之前） |
| 7.3 | `/k` 明确写 **Must be used with /f** | 没写这句 | 实测 `/k` 不带 `/f` → `错误: 无效语法。` + exit 1，**官方是对的** | 采信官方：`searchScope` 只在带 `/f` 的动作里出现 |
| 7.4 | 语法行写 `[{/k \| /d}]`（互斥） | 也写 `[/k] [/d]` 但没有"互斥"二字 | 实测 `/k` 与 `/d` **同给不报参数错**，而是 `搜索结束: 找到 0 匹配。` + exit 1 | 互斥由本清单的**枚举**保证（三选一），不指望程序拦 |
| 7.5 | 官方页只有英文 | 本机帮助的中文译文在 `/t` 与 `/f` 两段**有明显错位/重复的碎片**，例如 `/f` 那段印出「当大于 0 的值为 / 时，隐含 /f 参数  则默示为 /f 参数。」、`/t` 那段印出「则 /f 参数为 /f 参数。」（英文帮助同一处是完整的） | 中文帮助可读但这两句是坏的 | 字段说明**以官方页为准**，此处只记录本机帮助的本地化缺陷 |
| 7.6 | 官方页 `/d` 没写"必须配 `/f`" | 本机帮助也没写 | 未验证（不需要） | 不做额外约束 |

另：`reg /?` 顶层页写明「Return Code: (Except for REG COMPARE) 0 - Successful / 1 - Failed」，
`reg query /?` 页**没有**返回码一节 —— 两者不冲突，都记在此。

---

## 8. 没验的部分与原因（这一节比"全绿"更有用）

| 没验的东西 | 原因 |
|---|---|
| `reg query HKLM /s`、`reg query HKCU /s` 等**根键级递归** | 按任务红线：递归只在**很小的键**上试（`HKCU\Environment`、`HKLM\...\CurrentVersion` 已试）。根键递归会输出几十 MB，风险收益不成比例 |
| 远程机器形式 `reg query \\<机器>\HKLM\...` | 没有第二台机器；本机远程注册表服务默认也不开。字段的 `help` 里如实写了"未实测" |
| `REG_QWORD` 过滤 | 未收录（§7.1），所以无从验 |
| `reg` 的写操作（`add` / `delete` / `copy` / `save` / `restore` / `load` / `unload` / `import` / `export`） | **一个都不收录、一条都不实跑**（本包只做只读；`export` 会写文件也不收） |
| `requiresAdmin` 的最终标注 | 按"只在有依据时标"的口径**没有标 `requiresAdmin`**：实测读 `HKLM\SOFTWARE\...`、`HKCR`、`HKCC`、`HKCU`、甚至 `HKLM\SAM` 都不需要管理员（当然后者只给出空壳），**只有 `HKLM\SECURITY` 这类受保护键**才 `错误: 拒绝访问。`。给整个包标 `requiresAdmin` 会让用户误以为"查询都要提权"而放弃本来能用的功能（`docs/spec/manifest-v1.md` §2.5）。这条实测结论写在这里，也写进了 `description` 与各动作的 `resultNote` |

**这一批还有一条校验层的已知缺口**（`playbook` §10 第 31 条）：校验器的"开关溯源"只对
`docs/reference/<包名>*` 语料、以及出处写成 `.../windows-commands` 的包生效，
而它对 Windows 命令的 `/` 开关**另有一层限制**；本包**没有** `docs/reference/reg*` 快照，
所以报告里那句"检查 N 个开关"**不包含** `reg` 的任何开关。**这不是"检查通过"，而是"没检查"** ——
本包的开关依据完全靠逐字段 `doc` 指向官方页 + §3 的真机两种写法实测。

---

## 9. 真机冒烟测试结果

冒烟**不是手敲命令**：写了一个小脚本按 `manifest-v1.md` §3.2 的展开规则
（`command` + `commandArgs` + 按声明顺序的字段 token + `fixedArgs`，空值不产生 token）
**从清单本身生成 argv**，再用与宿主一致的"无控制台"条件（`CREATE_NO_WINDOW` + 重定向）真跑，
比对实测退出码与 `expectExitCode`。脚本放在仓库之外的临时目录（见 §10），内容不随包发布。

**18 条全部与预期一致（reg 17 条 + shutdown 1 条，见各自 NOTES）**：

| # | 动作 | 展开出的 argv | 实测 exit | 期望 | stdout | stderr |
|---|---|---|---|---|---|---|
| 1 | query-key | `query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"` | 0 | 0 | 11962 B | 0 B |
| 2 | query-key | `query HKCU\Environment /se # /t REG_EXPAND_SZ /z /reg:32` | 0 | 0 | 1262 B | 0 B |
| 3 | query-recursive | `query HKCU\Environment /s` | 0 | 0 | 1449 B | 0 B |
| 4 | query-value | `query "…\CurrentVersion" /v CurrentVersion` | 0 | 0 | 106 B | 0 B |
| 5 | query-value | `query "…\CurrentVersion\Fonts" /v "华文中宋 (TrueType)"` | 0 | 0 | 126 B | 0 B |
| 6 | query-default-value | `query HKLM\SOFTWARE /ve` | 0 | 0 | 59 B | 0 B |
| 7 | query-search | `query "…\CurrentVersion" /f Current` | 0 | 0 | 363 B | 0 B |
| 8 | query-search | `query HKCU\Environment /s /f USERPROFILE /d` | 0 | 0 | 1118 B | 0 B |
| 9 | query-search | `query "…\CurrentVersion" /f CurrentBuild /c /e /t REG_SZ` | 0 | 0 | 131 B | 0 B |
| 10 | query-search | `query "…\CurrentVersion" /f Current /k /reg:64` | **1** | 1 | 27 B（`搜索结束: 找到 0 匹配。`） | 0 B |
| 11 | query-search | `query "…\CurrentVersion" /s /f Current /k` | 0 | 0 | 16555 B | 0 B |
| 12 | query-search-value-names | `query "…\CurrentVersion" /v /f Current` | 0 | 0 | 363 B | 0 B |
| 13 | query-help | `query /?` | 0 | 0 | 2282 B | 0 B |
| 14 | query-key（失败路径） | `query "HKLM\SOFTWARE\__NoSuchKeyAllToolProbe__"` | 1 | 1 | 0 B | 38 B `错误: 系统找不到指定的注册表项或值。` |
| 15 | query-value（失败路径） | `query "…\CurrentVersion" /v __NoSuchValue__` | 1 | 1 | 4 B | 38 B 同上 |
| 16 | query-search（失败路径） | `query "…\CurrentVersion" /f __NoSuchPattern__` | 1 | 1 | 27 B `搜索结束: 找到 0 匹配。` | 0 B |
| 17 | query-key（权限路径） | `query HKLM\SECURITY` | 1 | 1 | 0 B | 19 B `错误: 拒绝访问。` |

**第 10 条是本轮唯一"期望值写错、被冒烟抓住"的地方**：我一开始把 `searchScope: keyNames`
（`/k`）在 `CurrentVersion` 上的期望写成 0，实测是 1 —— 因为那个键下**没有名字含 `Current` 的子项**，
0 命中就是退出码 1（§5.2）。这不是清单错，是我的期望错；改正后 18/18 通过。

**冒烟脚本的自检**（照 `playbook` §5.1）：本脚本用 `json.dumps` 打印完整 argv、用元组比较而不是
`-eq` 比数组；执行前先跑红线自检（见 shutdown 包 NOTES §9）。

---

## 10. 临时对象申报

**无。**

- **没有创建、修改或删除任何注册表项/值**：全部探针都是 `reg query`；测试用的键
  （`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`、`HKCU\Environment`、
  `HKLM\SOFTWARE\...\Uninstall\HuorongSysdiag`、`HKCR\.txt`、`HKCC` 等）**都是系统本来就有的**。
- 没有创建计划任务、没有写仓库内或仓库外的持久文件。
- 探针产物（`*.stdout.bin` / `*.stderr.bin` / 脚本）**全部落在 `%TEMP%\regshut-*` 下**，
  属临时文件，不入库、不影响系统；本次交付只新增 `plugins/reg/manifest.yaml` 与本文件。
- 按任务要求**没有执行任何 `git` 命令**。

复核命令（确认注册表没被动过）：

```powershell
# 探针用过的"不存在的键/值"现在依然不存在（都是查询、没有创建）
reg query "HKLM\SOFTWARE\__NoSuchKeyAllToolProbe__"    # 期望 exit 1 + 系统找不到指定的注册表项或值。
reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v __NoSuchValue__   # 期望 exit 1
# 只读性自检：7 个动作的 command 必须全是 query（本包不含任何写操作）
#   （注意别直接 grep add/delete/… —— 清单头部的注释里就列出了那些写操作的名字）
uv run --with pyyaml python -c "import yaml,pathlib; d=yaml.safe_load(pathlib.Path('plugins/reg/manifest.yaml').read_text(encoding='utf-8')); print(sorted({a['command'] for a in d['actions']}))"
# 期望输出：['query']
```

---

## 11. 校验器输出（原文）

```
[ OK ] plugins\reg\manifest.yaml  (7 动作 / 35 字段 / 字段出处标注 35 个 = 100%)
```

（同一次运行的整体行：`40/40 个 manifest 通过`、`字段出处覆盖率: 1060/1060 (100%)`。）

## 附：REG_QWORD 的取舍（2026-10-01 追加）

官方 reg-query 页的 `/t` 只列 6 种类型，本机 `reg query /?` 列 7 种（多 `REG_QWORD`）。
最初按「只写两边都能对应的」没有收录；后经实测 `/t REG_QWORD` 的**语法被接受**
（退出码 0，而不是「无效语法」），故按 playbook §10.5 第 32 条的口径**收录**，
并在枚举里标注「出处只有本机帮助一侧」。
