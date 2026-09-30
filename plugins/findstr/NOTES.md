# findstr 工具包 —— 实测记录（NOTES）

> **公共事实先看** [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)：
> 编码不统一、退出码不是「0 = 成功」、帮助写 stdout/stderr、管理员权限的真实症状都在那里。
> 本文件只写 findstr 特有的部分。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `Steve`，`IsInRole(Administrator)` = **False**。
> 实测日期 2026-10-01。
> 命令：`C:\Windows\System32\findstr.exe`，文件版本 **10.0.26100.8875 (WinBuild.160101.0800)**。

---

## 0. 一句话结论

findstr 全是**只读**搜索、不需要管理员，适合直接放进界面。它有三个必须知道的脾气：
① **所有开关必须排在搜索串与文件名之前**；② `/c:` `/f:` `/g:` `/d:` `/a:` 的值**必须与开关粘成一个 token**；
③ 它按**字节**工作 —— 非 ASCII 的搜索串只在 OEM/ANSI（本机 GBK）编码的文件里能命中。

| 覆盖范围 | 数字 |
|---|---|
| 动作 | **8** |
| 字段 | **36**（字段出处标注 36/36 = 100%） |
| 清单里出现过的开关 | `/L` `/R` `/I` `/N` `/O` `/X` `/B` `/E` `/V` `/S` `/M` `/P` `/OFFLINE` `/c:` `/f:` `/g:` `/d:` |
| 真机跑过的 argv | **62** 条（见 §11） |
| 没收录的开关 | `/A:`（颜色）、`/Q:`（本机独有）→ 见 §9 |

---

## 1. 参数知识来源

| # | 来源 | 取得日期 | 适用版本 | 备注 |
|---|---|---|---|---|
| 1 | Microsoft Learn `findstr` 页<br>`https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/findstr` | 2026-10-01 | Win10/11、Server 2016–2025、Azure Local 2311.2+ | HTTP 200、正文 **54374** 字节、Last updated **2023-02-03**；锚点只有 `#syntax` `#parameters` `#remarks` `#examples`；**没有退出码章节、没有权限说明** |
| 2 | 本机 `findstr /?` | 2026-10-01 | findstr.exe 10.0.26100.8875 | 写 **stdout**、stderr 0 字节、**退出码 0**；中文 1856 字节 / 英文 2535 字节（语言随控制台代码页变，见 §4）。**未入库**，随时可重取 |
| 3 | 本包真机冒烟 | 2026-10-01 | 同上 | `plugins/findstr/smoke.ps1`（62 条只读 argv + 白名单/注入自检 + 负向对照），原始结果见 §11 |

---

## 2. 动作总表

| # | 动作 id | argv 形态 | 字段 | 实跑？ |
|---|---|---|---|---|
| 1 | `search-text` | `findstr [/L\|/R] [/I] [/N] [/O] <strings> <files…>` | 6 | ✅ |
| 2 | `search-literal` | `findstr /c:<串> [/I] [/N] <files…>` | 4 | ✅ |
| 3 | `search-line-position` | `findstr [/X] [/B] [/E] [/N] <strings> <files…>` | 6 | ✅ |
| 4 | `search-not-match` | `findstr /V [/N] <strings> <files…>` | 4 | ✅ |
| 5 | `search-recursive` | `findstr /S [/M] [/I] [/P] [/OFFLINE] <patterns> <strings>` | 7 | ✅ |
| 6 | `search-strings-from-file` | `findstr /g:<清单> [/S] <files…>` | 3 | ✅ |
| 7 | `search-filelist` | `findstr /f:<清单> [/g:<串清单>] [<strings>]` | 3 | ✅（机制；示例路径是预设） |
| 8 | `search-directories` | `findstr /d:<目录列表> <strings> <files…>` | 3 | ✅ |

`locate`：`findstr.exe`，`searchPaths` 含 `%SystemRoot%\System32` 与 `SysWOW64`。
**没有 `versionArgs` / `versionPattern` / `minVersion`**：findstr 没有版本开关（`/?` 是帮助，
无参数运行会去读 stdin），按 windows-commands.md §4 的口径不编造版本命令。

---

## 3. 环境事实（实测）

```
Get-Command findstr.exe → Application  C:\Windows\system32\findstr.exe
VersionInfo.FileVersion → 10.0.26100.8875 (WinBuild.160101.0800)
[Environment]::OSVersion.VersionString → Microsoft Windows NT 10.0.26200.0
IsInRole(Administrator) → False
控制台默认输出代码页 → 936
```

---

## 4. 输出编码：OEM 代码页（cp936）—— 但 findstr 是**逐字节**工具

`runtime.encoding: oem`。判据用**带中文的输出**（`findstr /?`）看**原始字节**：

| 条件 | 字节数 | >127 字节 | 严格 UTF-8 解码 | 结论 |
|---|---|---|---|---|
| **宿主同款**：`CreateNoWindow=true` + 重定向两个流 | **1856** | **934** | **抛异常** | 中文 cp936（首 4 字节 `d4 da ce c4` =「在文」） |
| 控制台 CP=936（`cmd /c "chcp 936 & findstr /? > f"`） | 1856 | 934 | 抛异常 | 与宿主同款**逐字节相同** |
| 控制台 CP=65001（`chcp 65001 …`） | 2535 | **0** | 通过 | 退回英文资源、纯 ASCII |

**与 cipher 同一条规律、与 windows-commands.md §5.3 的 route 相反**：提示语言跟着
**控制台输出代码页**走（判的时候必须写清当时的控制台条件；作者第一次在设过
`[Console]::OutputEncoding = UTF8` 的会话里跑，拿到的是英文 2535 字节）。
宿主的真实条件（`ProcessRunner.cs:99-113`）会让子进程拿到新的隐藏控制台（CP = 系统 OEM = 936）
→ **cp936**，所以写 `oem`。

### 4.1 ⚠ 但**命中的行是源文件的原始字节**，与上面那条是两件事（实测）

| 文件编码 | 文件内容 | 命令 | 结果 |
|---|---|---|---|
| GBK（cp936） | `中文测试` | `findstr 中文 cn_gbk.txt` | **exit 0**、输出 10 字节、**严格 UTF-8 解码失败**（回显的是文件里的 GBK 字节） |
| UTF-8（无 BOM） | `中文测试` | `findstr 中文 cn_utf8.txt` | **exit 1、0 字节**（0 命中） |
| UTF-8 + BOM | `中文测试` | `findstr 中文 cn_utf8bom.txt` | **exit 1、0 字节** |
| UTF-16LE | `alpha 中文测试` | `findstr alpha cn_utf16.txt` | **exit 1、0 字节**（连"不支持的 Unicode 格式"警告都没有） |

**含义（已写进每个动作的 resultNote）**：
- findstr 把搜索串按**当前代码页**转换后与文件**逐字节**比较 → 非 ASCII 串只能在 OEM/ANSI 编码的
  文件里命中；UTF-8 文件里的中文**搜不到**（这不是清单的问题，是工具的固有性质）。
- 反过来，ASCII 搜索串命中 UTF-8 文件时，回显出来的是 **UTF-8 字节**，宿主按 `oem` 解码会显示成乱码。
  这是一个已知的表达能力缺口：一个工具包只能声明**一种**输出编码，而 findstr 的回显编码随输入文件变。

---

## 5. 与官方文档对不上的地方（逐条）

### 5.1 本机 `/?` 比官方页多 / 少的开关

| 项 | 官方页 | 本机 `findstr /?` | 本包怎么处理 |
|---|---|---|---|
| `/Q:qflags` | **完全没有** | `/Q:qflags  Quiet mode flags:` / `u  Suppress warning about unsupported Unicode formats` | **不收**（只有本机一侧；且实测在 UTF-16 文件上并没有出现那条警告可压） |
| `/F:file` 的说明 | "Gets a file list from the specified file." | 多一句 "(/ stands for console)" | 不收那句语义（`/f:/` 会去读 stdin）；差异记在这里 |
| `/G:file` 的说明 | "Gets search strings from the specified file." | 多一句 "(/ stands for console)" | 同上 |
| 开关名与冒号形式 | `/c:<string>` `/f:<file>` `/g:<file>` `/d:<dirlist>` `/a:<colorattribute>` `/off[line]` | 完全一致 | 收录 |
| 退出码 | 页面上**没有任何退出码章节**（已逐行核对） | `/?` → 0 | `exitCodes` 全部来自实测（§7） |
| 权限要求 | 页面**一个字都没写** | 只读动作非管理员全部成功 | 见 §8 |

### 5.2 官方示例 `findstr hello there x.y` 与实测**对不上**（重要）

- 官方页 Examples 第一条："To search for *hello* or *there* in file *x.y*, type: `findstr hello there x.y`"
  —— 意思是"两个搜索串"。
- **实测**：`findstr alpha beta a.txt`（两个 token）→ 输出只有 alpha 的命中，
  **stderr 打 `FINDSTR: 无法打开 beta`** —— findstr 把 **beta 当成了文件名**。
  而 `findstr "alpha beta" a.txt`（**一个** token 里带空格）→ 命中 alpha 与 beta 两行、exit 0。
- 两边口径的统一解释：findstr 只把**第一个**非开关参数当搜索串参数，并对它**按空格拆分**成多个搜索串；
  其余参数都是文件名。官方那句例子只有在"把两个词放进同一个参数"时才成立
  （本机帮助的例子正是**带引号**的 `FINDSTR "hello there" x.y`，与实测一致）。
- **对清单的影响**：`strings` 字段是 `type: text` 且**刻意不 repeatable**（多行 = 多个 token = 多个文件名）。
  这条差异写进了字段 help。

### 5.3 其它行为层面的实测差异

| 项 | 文档怎么说 | 实测 | 影响 |
|---|---|---|---|
| 打不开的文件返回什么 | 官方没写退出码；社区口径常说是 2 | **`alpha nope.txt` → exit 1**（+ stderr `FINDSTR: 无法打开 nope.txt`），**不是 2** | `exitCodes` 里 1 的含义写明"没匹配**或**文件打不开" |
| 认不出来的开关 | 没说 | `/zz alpha a.txt` → stderr `FINDSTR: 忽略 /z`，**exit 0**（不影响结果、不报错） | 不做"开关拼错就失败"的假设 |
| `/a:` 非法取值 | 没说 | `/a:ZZ alpha a.txt` → stderr `FINDSTR: 忽略 /Z`（**两次**）、exit 0 | 未收录 `/a:`，见 §9 |
| `/c:` 与 `/r` 的组合 | 官方示例 `/b /n /r /c:^ *FOR *.bas` 把 /c 与 /r 一起用 | 一致：`/b /n /r /c:^ *FOR *.txt` → 命中 `1:FOR loop`；而同文本上 `/b /n /c:^FOR a.txt`（不加 /r）→ **0 命中**，说明 **/c 遇到 /r 会按正则处理**（官方 Parameters 表只说 /c 是 "literal search string"） | 写进 `search-line-position` 的 resultNote |
| `/l` 与默认 | 官方：/r 是默认 | 一致：`alp.a a.txt` 命中 alpha；`/l alp.a a.txt` → exit 1 | 无 |
| 默认大小写 | 官方：/i 才忽略大小写 | 一致：`Alpha a.txt` 只命中 `Alpha upper` | 无 |
| `/s` 的基准 | 官方：/s "Searches the current directory and all subdirectories." | 一致，但**参数必须是文件模式**：`/s alpha <目录>` → **exit 1、无输出**；`/s alpha <目录>\*.txt` → exit 0 并递归到子目录 | 字段 help 与 resultNote 都写明"必须带通配符" |
| 递归的性能 | 没说 | `findstr /s /m /i fonts C:\Windows\*.ini` → exit 0，但**耗时 23.9 秒**，并伴随 `FINDSTR: 警告 - 输入文件 … 不支持 Unicode 格式` | 写进 `search-recursive` 的字段 help |

---

## 6. 字段风格与顺序（实测判定）

### 6.1 冒号开关：`attached`（`/c:值`）vs `separate`（`/c 值`）—— 每个都各跑一次

| 字段 | attached（单 token） | separate（两 token） |
|---|---|---|
| `/c:` | `/c:Hello There a.txt` → **exit 0**、13 B | `/c Hello There a.txt` → exit 0 但 **stderr 18 B**（`FINDSTR: 忽略 /c`） |
| `/f:` | `/f:filelist.txt alpha` → **exit 0**、30 B | `/f filelist.txt alpha` → **exit 1**、stderr 43 B（`忽略 /f` + `无法打开 alpha`） |
| `/g:` | `/g:patterns.txt a.txt` → **exit 0**、13 B | `/g patterns.txt a.txt` → **exit 1**、stderr 18 B |
| `/d:` | `/d:<目录> alpha *.txt` → **exit 0**、120 B | `/d <目录> alpha *.txt` → **exit 1**、stderr 43 B |
| `/a:` | `/a:0A alpha a.txt` → **exit 0**、7 B | `/a 0A alpha a.txt` → **exit 1**、stderr 43 B |

→ 这五个字段一律 `style: attached` + `separator: ":"`。**`separate` 在本包一次都没用到**
（不是"看着像哪一类"，是逐条跑出来的）。

### 6.2 顺序：所有开关必须在 strings 与 filename 之前

| 写法 | 结果 |
|---|---|
| `/i alpha a.txt`（开关在前） | exit 0，命中 alpha 与 Alpha 两行（20 B）、stderr 0 B |
| `alpha a.txt /i`（开关在后） | exit 0，但 **stderr 22 B = `FINDSTR: 无法打开 /i`** —— /i 被当成了**文件名** |

→ 官方 Remarks 第一条（"All findstr command-line options must precede strings and filename in the
command string."）**实测成立**，所以本包每个动作都把开关字段声明在位置参数字段**之前**
（字段声明顺序 = argv 顺序；同 certutil 的规矩）。

### 6.3 大小写敏感

开关大小写不敏感（`/I` 与 `/i` 同效）；**搜索本身默认区分大小写**（`Alpha` 只命中 `Alpha upper`）。

---

## 7. 退出码（全部来自实测，含失败路径）

| code | severity | 实测情形 |
|---|---|---|
| **0** | ok | 命中：`alpha a.txt`、`/c:Hello There a.txt`、`/g:patterns.txt a.txt`、`/f:filelist.txt alpha`、`/d:<目录> alpha *.txt`、`/s alpha *.txt`、`/s /m /i ALPHA <绝对>\*.txt`、`/b /n /r /c:^ *FOR *.txt`、`/off`、`/offline`、`/p`、`/q:u`、`/m /n`、`/o`、`/v`、`/x`、`/b`、`/e`、`/r`、`/zz`（无法识别的开关只警告） |
| **1** | warning | ① 没匹配：`notfound a.txt`（0 字节）、`alpha empty.txt`、`alpha *.nomatch`、`/l a.*a a.txt`、`/c:alpha beta a.txt`；② **文件打不开**：`alpha nope.txt` → exit 1 + stderr 28 B；`alpha sub`（目录当文件）→ exit 1、无输出；③ `/s alpha <目录>`（光给目录）→ exit 1、无输出；④ 非 ASCII 串搜 UTF-8/UTF-16 文件 → exit 1、无输出 |
| **2** | error | 命令行本身有问题：无参数 → stderr 23 B（`FINDSTR: 错误的命令行`）；`/f:nope alpha` → stderr 35 B（`无法从 nope 读取文件列表`）；`/g:nope a.txt` → stderr 33 B（`无法从 nope 读取字符串`）；`/c:` 空串 → stderr 26 B（`/c 后面缺少参数`） |

**没有观察到除 0 / 1 / 2 以外的退出码。** 注意 1 同时覆盖"没找到"与"文件打不开"这两种
语义完全不同的情况，所以清单把 1 定为 `warning`，并在 `meaning` 里写明"看 stderr 有没有
那句『无法打开』来区分"。

### 7.3 一个会把脚本挂死的坑（作者踩过）

`findstr /c a.txt`（`/c` 缺值 → 没有搜索串也没有文件名）会去读 **stdin**。
作者的第一个探针脚本没有关掉 stdin 那一端，于是 **findstr 永远不返回，脚本挂了 240 秒被
挪进后台**才发现。宿主的做法是对的：启动进程后立刻 `process.StandardInput.Close()`
（`ProcessRunner.cs:176-183`，注释里记着 sqlite3 踩过同一个坑）。本包的 `smoke.ps1` 照做，
并且每条用例都带 60 秒超时。

---

## 8. 管理员权限

本机账号 `Steve` **不是管理员**；§11 的 62 条 argv 全部按预期完成，唯一的失败都是
"文件读不到/没命中"这类与权限无关的结果 → `runtime.requiresAdmin: false`，动作级一个都没标。

---

## 9. 故意没做的部分（给后来者的地图）

1. **`/A:<colorattribute>`（颜色）没有做成字段**：宿主是**捕获输出**的
   （`RedirectStandardOutput`），实测 `/a:0A alpha a.txt` 与不加它输出**逐字节相同**（7 字节），
   颜色属性在重定向下不产生任何可观察效果；非法值还只在 stderr 打两句"忽略 /Z"。
   收录它等于给界面加一个"什么都不做"的开关。
2. **`/Q:qflags` 没有做成字段**：它**只有本机帮助一侧有出处**（官方页完全没有，§5.1），
   而且唯一已知的取值 `u`（压掉"不支持的 Unicode 格式"警告）在实测的 UTF-16 文件上
   根本没有触发过那条警告 → 没有可验证的效果。
3. **`/F:` 的 "(/ stands for console)" 语义**（`/f:/` 读 stdin）没有表达：宿主给 stdin 的是
   **立刻 EOF**，真这么填只会立刻结束，没有实用价值。
4. **"多搜索串"没有做成"可重复字段"**：实测两个独立 token 里第二个会被当文件名（§5.2）。
   用户要搜多个词就在 `strings` 里用空格隔开（一个 token，findstr 自己拆）。
5. **超大范围的递归没有做保护**：`/s` 交给用户自己控制路径。作者实测在 `C:\Windows` 上递归
   `*.ini` 要 **23.9 秒**且会打警告 —— 已在字段 help 里劝退，但没有（也没法）在清单里限制范围。

---

## 10. 没有验的部分与原因（诚实清单）

| 没验的东西 | 原因 |
|---|---|
| `search-filelist` / `search-strings-from-file` / `search-directories` 的 **examples 里的 `D:\…` 路径** | 那是**预设**（作者机器上没有 `D:\filelist.txt`、`D:\patterns.txt`、`D:\proj1`），没法真跑。**机制本身**用 `%TEMP%` 自建的 `filelist.txt` / `patterns.txt` 验证过（`/f:filelist.txt alpha` → 0；`/g:patterns.txt a.txt` → 0；`/g:patterns.txt /f:filelist.txt` → 0；`/d:<目录> alpha *.txt` → 0），见 §11 |
| `search-recursive` 里 `D:\projects\*` 那条预设 | 同上（本机没有该目录）。另一条 `…drivers\etc\*` 跑过，但退出码取决于用户的 hosts 内容，所以**没有标 `expectExitCode`** |
| `/a:` 在**真控制台**里的颜色效果 | 宿主永远重定向输出，没有可观察路径；且本包不收录该开关 |
| `/q:u` 对"不支持的 Unicode 格式"警告的压制 | 没能触发那条警告（UTF-16 文件上实测 0 命中、无警告）；本包不收录 |
| 32 位 `SysWOW64\findstr.exe` 的行为 | 本机是 x64，只测了 System32 那一个（`locate` 里保留 SysWOW64 作为兜底路径） |

---

## 11. 真机冒烟结果（`plugins/findstr/smoke.ps1`）

复跑：`pwsh -NoProfile -File plugins/findstr/smoke.ps1`（白名单 + 注入自检 + 负向对照 + 每条 60 秒超时 +
立刻关 stdin，收尾自动删临时目录）。**62 条全部按预期**，摘录关键几组：

```
退出码三态
[ OK ] alpha a.txt（命中）               exit=0          [ OK ] /f:nope alpha（清单读不到）      exit=2 stderr=35B
[ OK ] notfound a.txt（没命中）           exit=1          [ OK ] /g:nope a.txt（串文件读不到）     exit=2 stderr=33B
[ OK ] alpha nope.txt（文件不存在）        exit=1 stderr=28B  [ OK ] /c:（空串）                  exit=2 stderr=26B
[ OK ] 无参数（命令行错误）                 exit=2 stderr=23B  [ OK ] /zz（无法识别→只警告）        exit=0 stderr=36B
字段风格（每对都跑了两种写法）
[ OK ] /c:Hello There a.txt（attached ⊙） exit=0 stderr=0B   [ OK ] /c Hello There a.txt（separate ✗） exit=0 stderr=18B
[ OK ] /f:filelist.txt alpha（attached ⊙） exit=0 stderr=0B   [ OK ] /f filelist.txt alpha（separate ✗） exit=1 stderr=43B
[ OK ] /g:patterns.txt a.txt（attached ⊙） exit=0 stderr=0B   [ OK ] /g patterns.txt a.txt（separate ✗） exit=1 stderr=18B
[ OK ] /d:<目录> alpha *.txt（attached ⊙）  exit=0 stderr=0B   [ OK ] /d <目录> alpha *.txt（separate ✗） exit=1 stderr=43B
[ OK ] /a:0A alpha a.txt（attached ⊙）     exit=0 stderr=0B   [ OK ] /a 0A alpha a.txt（separate ✗）    exit=1 stderr=43B
顺序与多串
[ OK ] /i alpha a.txt（开关在前 ⊙）        exit=0 stderr=0B   [ OK ] alpha a.txt /i（开关在后 ✗）    exit=0 stderr=22B
[ OK ] "alpha beta" a.txt（一个 token）    exit=0 stderr=0B   [ OK ] alpha beta a.txt（两个 token ✗） exit=0 stderr=24B
中文与代码页
[ OK ] 中文 in cn_gbk.txt（命中）          exit=0             [ OK ] 中文 in cn_utf8.txt（0 命中）    exit=1
[ OK ] 测试 in cn_gbk.txt（命中）          exit=0             [ OK ] 中文 in cn_utf8bom.txt（0 命中） exit=1
[ OK ] example: fonts win.ini …（6 条 example 全绿，均 exit=0）

编码与逐字节语义断言
  [ OK ] /?  宿主同款进程 → 1856 字节、934 个 >127 字节、严格 UTF-8 解码=False、exit=0
  [ OK ] 命中行的字节 = 源文件字节：GBK 文件 exit=0/10B/UTF-8合法=False；UTF-8 文件 exit=1/0B
  控制台 CP=936 → 1856/934；控制台 CP=65001 → 2535/0（跑完恢复原 CP）
临时对象申报：C:\Users\Steve\AppData\Local\Temp\__AllToolProbe_findstr → Test-Path = False
小结：通过 62 / 失败 0 / 跳过 0
```

> findstr 没有危险动作，所以脚本的自检重点放在"**别把 token 拼进 shell 字符串**"上：
> ① 只启动 `%SystemRoot%\System32\findstr.exe`（拒绝 PATH 劫持）；
> ② `UseShellExecute=false`、argv 以数组传递；
> ③ 负向对照两条 —— 白名单必须拒绝一条不存在的 argv；**独立的** `>` / `|` token 必须被拦下，
>   而正则里的 `\<` `\>`（含在字符串内部、不是独立 token）**不许**被误判（否则脚本会自己失效）。

---

## 12. 临时对象申报（造了什么 / 何时删的 / 怎么复核）

| 临时对象 | 用途 | 何时删的 | 怎么复核 |
|---|---|---|---|
| `%TEMP%\__AllToolProbe_cipherfindstr\fs\`（`a.txt`、`b.txt`、`sub\c.txt`、`patterns.txt`、`filelist.txt`、`empty.txt`、`cn_gbk.txt`、`cn_utf8.txt`、`cn_utf8bom.txt`、`cn_utf16.txt`） | 本次全部探测的样本；含 GBK / UTF-8 / UTF-8+BOM / UTF-16LE 四种中文编码 | 探针跑完后、写 NOTES 之前 `Remove-Item -Recurse -Force` 删除 | `Test-Path "$env:TEMP\__AllToolProbe_cipherfindstr"` → **False** |
| `%TEMP%\__AllToolProbe_findstr\fs\`（与上表同名的样本） | `smoke.ps1` 的样本目录 | 每次跑完 `smoke.ps1` 自动删除（脚本末行会打印 `Test-Path`） | 再跑一次 `smoke.ps1`，看末行 `临时对象申报：… → Test-Path = False` |

样本文件全部**新建在 `%TEMP%` 下**；对系统文件的访问（`C:\Windows\win.ini` 等）**全部只读**。
没有创建、修改或删除任何用户数据，也**没有递归搜过系统目录**（只在自建样本目录与
`%TEMP%` 里做过递归；唯一一次在 `C:\Windows` 上试耗时的那条只跑了 `*.ini` 并记录了 23.9 秒，
之后没有再跑）。

---

## 13. 给宿主的反馈（本包发现的、不属于清单的问题）

1. **回显编码随源文件变**：findstr 的输出里，"它自己打的提示/错误"是控制台 CP（本机 cp936），
   而"命中的行"是**源文件的原始字节**。一个工具包只能声明一个 `runtime.encoding`，
   所以 UTF-8 文件里 ASCII 模式的命中行会显示成乱码。这不是清单能修的，
   建议宿主（或规范）考虑"按行/按内容猜测编码"或至少允许 `encoding: auto` 时给出提示。
2. **`stdin` 必须给 EOF**：本包实测确认了 `ProcessRunner` 立刻关 stdin 的做法是对的
   （findstr 在缺文件名时会读 stdin，不关就永远不返回，见 §7.3）。
