# dism 工具包 —— 实测记录

> 共享结论（Windows 自带命令的公共事实：编码不统一、退出码不是「0 = 成功」、
> 帮助流的差异、`/?` 快照不入库等）见 [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)。
> 本文只写 **dism 特有的部分**。
>
> 实测环境：Windows 11 专业工作站版 `10.0.26200` x64，账号 `kangmori\steve`（**非管理员**），
> 代码页 936（`chcp` 实测 `Active code page: 936`），实测日期 **2026-02-12**。

---

## 1. 一句话结论

**DISM 在本机（非管理员）连帮助都跑不出来** —— 任何命令行都以 `740`（ERROR_ELEVATION_REQUIRED）
退出，stdout 只有 122 字节的英文提权提示，stderr 0 字节。原因是 DISM **在解析命令行之前就先做提权检查**
（`/NoSuchSwitch`、不存在的 WIM 文件路径、`/?` 全都得到同一个 740，而不是 87 或找不到文件）。

因此本包的核心事实分成两类，本文把它们分开标注，绝不混用：

| 来源 | 覆盖什么 | 本包里的用途 |
|---|---|---|
| **本机实测** | 权限行为（740）、输出编码方向、输出流、`/?` 是否可用、文件版本 | `requiresAdmin: true`、`runtime.encoding`、`exitCodes[740]` |
| **官方文档（Microsoft Learn）** | 所有命令/开关/参数/限制 | 全部字段与动作的 `doc`/`sources` |

---

## 2. 参数知识来源

| 类别 | 位置 | 是否入库 |
|---|---|---|
| 官方文档 HTML | Microsoft Learn 的 `dism-*` 一族（9 个 URL，见 manifest 顶层 `sources`），全部实测 HTTP 200 | 快照抓在本机专属库之外的工作目录，**未入库** |
| 本机 `dism /?` 帮助 | **拿不到**（见 §3） | — |
| 本机 zh-CN 本地化资源 | `C:\Windows\System32\zh-CN\Dism.exe.mui`（含完整中文 DISM 帮助串与全局选项表） | 替代证据，只用于交叉核对，**未入库** |

抓取方式（可复现，全部用 `curl.exe` —— 本机 `Invoke-WebRequest` 会挂起、`web_fetch` 报 fetch failed）：

```powershell
curl.exe -s -L --max-time 60 -A 'Mozilla/5.0' -o dism-os-package-servicing.html `
  'https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/dism-operating-system-package-servicing-command-line-options'
```

### 2.1 官方文档的形态与 schtasks/ipconfig 都不同（要记住）

官方的 DISM 文档**不是**「一个主页 + 每个子命令一页」，而是**一个主题一页、页内含多个命令**：

| 页面 | 覆盖的命令 |
|---|---|
| `what-is-dism` | 概念、权限要求、限制（通配符/多命令/版本兼容） |
| `dism-image-management-command-line-options-s14` | `/Get-ImageInfo` `/Mount-Image` `/Apply-Image` `/Capture-Image` 等 |
| `dism-operating-system-package-servicing-command-line-options` | `/Get-Packages` `/Get-Features` `/Enable-Feature` `/Cleanup-Image` 等 |
| `dism-driver-servicing-command-line-options-s14` | `/Get-Drivers` `/Get-DriverInfo` `/Add-Driver` `/Remove-Driver` `/Export-Driver` |
| `enable-or-disable-windows-features-using-dism` | 操作步骤（官方推荐流程） |
| `repair-a-windows-image` | 组件存储修复的三段式流程 |

**所以一个动作的 `sources` 常常要挂两页**（例如 `/Cleanup-Image` 的字段依据在「OS 包服务」页、
流程依据在「Repair a Windows Image」页）。下面这些看起来"该有"的 URL 实测是 **404**，不要去猜：
`.../desktop/dism`、`.../desktop/dism-cleanup-image-command-line-options`、`.../desktop/dism-apply-image`、
`.../desktop/dism-windows-capability-add`、`.../desktop/dism-global-command-line-options`、
`.../desktop/dism-api-errors`。

---

## 3. 实测记录①：非管理员下 DISM 什么都跑不了（本包最重要的实测）

测法：用 `.NET Process` 直接 `CreateProcess`（不经 shell），**分开抓 stdout / stderr 的原始字节**，
记录退出码、耗时、字节数。Dism.exe 路径 `C:\Windows\system32\Dism.exe`。

| # | argv | 退出码 | stdout | stderr | 耗时 |
|---|---|---|---|---|---|
| 1 | `/?` | **740** | 122 B | 0 B | 4 ms |
| 2 | `/English /?` | **740** | 122 B | 0 B | 3 ms |
| 3 | `/Online /?` | **740** | 122 B | 0 B | 0 ms |
| 4 | `/English /Online /?` | **740** | 122 B | 0 B | 1 ms |
| 5 | `/Online /Get-Packages /?` | **740** | 122 B | 0 B | 0 ms |
| 6 | `/Online /Get-Packages` | **740** | 122 B | 0 B | 0 ms |
| 7 | `/NoSuchSwitch`（不存在的开关） | **740** | 122 B | 0 B | 0 ms |
| 8 | `/Get-WimInfo /WimFile:Z:\nope.wim`（不存在的文件） | **740** | 122 B | 0 B | 3 ms |
| 9 | `/Mount-Image /ImageFile:Z:\nope.wim /Index:1 /MountDir:Z:\nope` | **740** | 122 B | 0 B | 0 ms |
| 10 | `C:\Windows\SysWOW64\Dism.exe /?`（32 位副本） | **740** | 122 B | 0 B | 4 ms |

stdout 原文（122 字节，逐字节就是 ASCII，首 8 字节 `0d 0a 45 72 72 6f 72 3a`）：

```
Error: 740

Elevated permissions are required to run DISM. 
Use an elevated command prompt to complete these tasks.
```

**这段原文本身也是证据**：第 2 行末尾有一个尾随空格，与
`C:\Windows\System32\en-US\Dism.exe.mui` 里那条字符串（UTF-16LE，实测命中 1 处）逐字一致 ——
说明输出确实来自系统分发的本地化资源，而不是硬编码在 exe 里。

### 3.1 由这一条得出的三个结论

1. **`requiresAdmin: true` 是实测出来的，不只是抄文档。** 官方
   `what-is-dism` 页原话是 "DISM has to run from a Command Prompt running as administrator."，
   本机实测把这句话坐实到了「连 `/?` 都不行」的程度。
2. **本机没有任何 DISM 成功路径可实测。** 所以本包**没有一个动作被真冒烟过**（§9 逐条列出）。
   任务要求的「只读动作可以真跑」在这里被环境否掉了 —— 不是没跑，是跑不了。
   清单里所有 `output.resultNote` 都如实写明「成功路径未验证」。
3. **`/English` 也被拒。** 说明提权检查在参数解析之前（`/English` 的有效性都无法验证），
   所以本机连"让它输出英文帮助"这条路都不通。

---

## 4. 实测记录②：输出编码 = oem（cp936）—— 判定依据与诚实边界

**先说边界：本机唯一能拿到的真实 DISM 输出是那句 ASCII 英文提权提示，它判定不出编码。**
所以下面不是"拿中文输出量的"，而是「用能拿到的一切证据推断」+「明确写出哪些是推断」。

### 4.1 能拿到的证据

| 证据 | 实测值 |
|---|---|
| 系统区域 / UI 语言 | `Get-WinSystemLocale` = `zh-CN`（中文（中国）），`Get-Culture` = `zh-CN` |
| 代码页 | `chcp` → `Active code page: 936`；`(Get-Culture).TextInfo.OEMCodePage` = **936**，`ANSICodePage` = 936 |
| 本机消息的语言选择 | 提权提示是**英文** —— 说明本机的 UAC/系统 UI 语言解析到了 `en-US` 资源（`en-US\Dism.exe.mui` 里逐字命中该句） |
| 中文资源是否存在 | `zh-CN\Dism.exe.mui` **存在且含完整中文帮助**。逐字命中：「需要提升权限才能运行 DISM。」「操作成功完成。」「错误: %1!d!」「错误: 0x%1!x!」「DISM 未识别命令行选项"%1"。」「使用提升的命令提示符完成这些任务。」 |
| 资源文件版本 | `C:\Windows\System32\zh-CN\Dism.exe.mui` 内嵌版本串 `10.0.26100.8457` |

### 4.2 判定与理由

**`runtime.encoding: oem`**（宿主把 `oem` 解析成 `CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，
本机 = 936）。理由：

1. `dism.exe` 是**系统分发的非 Unicode（UMB）控制台程序**，它的控制台输出走 OEM 代码页 ——
   与本批 11 个 Windows 自带命令（`ping`/`ipconfig`/`netstat`/`tasklist`/`powercfg`… 实测全是 cp936）
   同一族；同族的两个后来者里唯一的例外是 `schtasks`（UTF-8），那是程序自己选了 UTF-8，
   而 DISM 有**中文 MUI 资源**这件事本身就说明它是按代码页本地化的老式工具。
2. 同一台机器、同一个代码页 936 的事实是实测的。
3. **反证**：如果 DISM 是 UTF-8 程序，它就不需要 MUI 按语言提供 OEM 字符串表；而它确实有（见上表）。

### 4.3 为什么不用 `auto`

`auto` 让宿主按"看起来像什么"猜。本包的动作输出里会有大量中文（功能状态「已启用/已禁用」、
「组件存储(WinSxS)信息:」这类表头），猜错的表现就是整片乱码；而这一族程序的代码页行为是确定的，
写死 `oem` 比 `auto` 更可预测（与第一批 12 个包的做法一致）。

### 4.4 还没验证到的部分（如实写）

- **没有一份中文的真实 DISM 输出被解码验证过。** 因为本机非管理员跑不出任何成功输出。
  等哪天以管理员身份跑通了，第一件事应该是复核这一条（用 `/Get-Features` 的中文表头判）。
- 本机 UAC 语言选到 `en-US`，所以**即使提权成功，这台机器的 DISM 报错文本也可能是英文**。
  清单里所有 `when` 正则都写成「中文 + 英文」两种措辞，不依赖某一种语言。

---

## 5. 实测记录③：字段风格（`attached` vs `separate`）

### 5.1 结论：本包全部用 `attached`，并且**冒号写进 prefix**

清单里所有带值开关都是 `style: attached` + `prefix: "/Switch:"`，
生成的是官方语法里的单 token 形态 `/Switch:Value`（例如 `/Format:Table`、`/ImageFile:D:\a.wim`）。

### 5.2 ⚠ 两种写法**都没能实测**（必须知道）

任务要求「`separate` 与 `attached` 两种写法各跑一次再定」。**本机做不到**，原因就是 §3：
DISM 在任何参数解析之前就因权限退出，`/Format Table`（两 token）与 `/Format:Table`（单 token）
**返回的东西一模一样**（都是 122 字节的 740 提示）。所以本机无法用实验区分 DISM 认哪种写法。

**那么为什么选单 token？** 因为官方文档里**只有**单 token 形态：

- 语法行：`Dism /Get-Packages [/Format:{Table | List}]`、`Dism /Get-ImageInfo /ImageFile:<path_to_image.wim>`
- 所有官方示例：`/Format:Table`、`/ImageFile:C:\test\offline\install.wim`、`/Source:c:\test\mount\windows`

按「参数知识只能来自官方文档」（R1）与「两边对不上时只写都能对应的」，**照官方文档的单 token 写法**
是唯一有据可依的选择。两 token 写法（`/Format Table`）**在任何官方页面里都不存在**，
所以本包不敢用它 —— 万一 DISM 不认，命令会"拼出来能看懂、一跑就报参数错"。

**待办（留给有管理员权限的人）**：以管理员身份各跑一次
`Dism /Online /Get-Packages /Format:Table` 与 `Dism /Online /Get-Packages /Format Table`，
把结果补进这一节。这是本包唯一「按文档而不能按实测」定下来的字段风格决策。

### 5.3 另一个被 schema 挡住的坑：`separator` 没有冒号

第一版清单写的是 `style: attached` + `separator: ":"`，**校验器直接拦下**：

```
schema: actions.1.fields.0.separator: ':' is not one of ['', ' ', '=']
```

`separator` 的枚举只有 `""` / `" "` / `"="`。这与 `wevtutil` 撞过的是**同一个坑**
（playbook §9 记着：wevtutil 只认单 token，做法是把冒号写进 `prefix`）。
改成 `prefix: "/Format:"` 之后 20 处全部通过。这条值得写进规范/playbook 的坑清单：
**Windows 命令的 `/Switch:Value` 语法在 v1 里只能用「冒号进 prefix」表达。**

### 5.4 字段声明顺序 = argv 顺序（本包的顺序依据）

官方语法总览是：

```
DISM.exe {/Image:<path_to_image_directory> | /Online} [dism_global_options] {servicing_option} [<servicing_argument>]
```

本包的处理办法：

- **服务命令（`/Get-Packages` 这种）写进 `command`** —— 它是官方语法里 `{servicing_option}` 那一段；
- **`/Online` 这类映像定位开关写进 `commandArgs`/`examples` 的固定部分**（本包只做 `/Online`，
  因为离线 `/Image:` 需要另一份映像，做成字段会让每个动作多一个几乎不变的参数）；
- **各字段按官方语法行的先后顺序声明**（例如 `/Enable-Feature` 是
  `/FeatureName` → `/PackageName` → `/Source` → `/LimitAccess` → `/All`）。

按规范，argv 会拼成 `dism.exe` + `command` + `commandArgs` + 字段 + `fixedArgs` ⇒
`dism /Online /Enable-Feature /FeatureName:X /All`，与官方示例 `/Online /Enable-Feature /FeatureName:Hearts /All` 同形
（DISM 自己的开关之间顺序不敏感，官方语法行也只是分必需/可选，没有"第一个非开关参数之后原样转交"那种边界 ——
与 uv 的情形不同）。

---

## 6. 实测记录④：退出码

| 退出码 | 含义 | 出处 | 本机实测 |
|---|---|---|---|
| **0** | 成功；DISM 的中文收尾提示是「操作成功完成。」 | 本机 `zh-CN\Dism.exe.mui` 里有该字符串（消息资源） | ❌ 未验证（跑不出成功路径） |
| **50** | `ERROR_NOT_SUPPORTED` —— The request is not supported. | 官方 [System Error Codes (0-499)](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-) 逐字：`50 (0x32)` | ❌ 未验证 |
| **87** | `ERROR_INVALID_PARAMETER` —— The parameter is incorrect. | 官方同上：`87 (0x57)`；另有官方疑难解答页 [DISM command fails with error code 87](https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/dism-error-87-apply-windows-10-image)（原 KB3082581）把 `/Apply-Image` 报 87 当作已知现象 | ❌ 未验证 |
| **740** | `ERROR_ELEVATION_REQUIRED` —— The requested operation requires elevation. | 官方 [System Error Codes (500-999)](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--500-999-) 逐字：`740 (0x2E4)` | ✅ **实测 10 种命令行全部命中**（§3 表） |

**微软的 DISM 各主题页都没有退出码章节**（已逐页核对 6 个页面），所以 50/87 的语义取自通用系统错误码页，
740 取自系统错误码页 + 本机实测。规范要求「`exitCodes` 覆盖文档里列出的所有码」在本包上退化为
「覆盖能查到出处的码」——DISM 的成功返回其实还有一整套 `0x800fXXXX` 形态的 HRESULT
（DISM 的报错形态之一是「错误: 0x%1!x!」，见 `zh-CN\Dism.exe.mui`），
但**本机既跑不出也拿不到一份官方把它们列全的页面**，所以 `exitCodes` 里不编这些码，
只在 `resultNote` 里提示输出可能带 HRESULT。

---

## 7. 与官方文档对不上的地方 / 官方文档自身的不一致

| # | 现象 | 处理 |
|---|---|---|
| 7.1 | **`/Source:` 的写法在同一页里有两种**：`/Cleanup-Image` 与 `/Enable-Feature` 的**语法行**写 `/Source: <filepath>`（**冒号后带一个空格**），而**官方示例**写 `/Source:c:\test\mount\windows`（无空格） | 按无空格的示例形态实现（单 token）；**无法实测判定**（§3），差异记在这里 |
| 7.2 | **`what-is-dism` 页的示例用了 `/DriverName:`**（`DISM.exe /image:"c:\images\Image1" /Add-Driver /ForceUnsigned /DriverName:"C:\Drivers\1.inf" ...`），而**驱动服务页**的语法与示例一律是 **`/Driver:`** | 本包按驱动服务页的 `/Driver:`（那一页是驱动族的正式参考页）；本包未收 `/Add-Driver`，影响面为零，记录备查 |
| 7.3 | 官方**没有**「DISM 退出码」专页；50/87/740 的依据是通用系统错误码页 | 已在 §6 逐条标出处 |
| 7.4 | 官方**没有**「DISM 全局选项（`/LogPath` `/ScratchDir` `/LogLevel` `/English` `/WinDir`…）」的独立页；能抓到的路径 404。这些选项的说明只出现在本机 MUI 的顶层帮助里 | **一个都没写进清单**（只在本机 MUI 有出处，不满足 R1 的"官方文档可定位"要求）。需要它们的用户请用界面的自定义参数输入框 |
| 7.5 | 本机 `dism /?` 与 `dism /Online /?` **都拿不到帮助**（740），无法完成"官方文档 × 本机 `/?` 两边核对" | 用微软随系统分发的 `zh-CN\Dism.exe.mui` 做交叉核对：它内含完整中文顶层帮助与全局选项表，与本包用到的开关**没有冲突**；本包没有从它那里收任何官方页面没有的东西 |
| 7.6 | `dism-image-management` 页里 `/Apply-Image` 的 `/ApplyDir` 是**位置参数形态**（`/ApplyDir:<target_location>`），不是可选开关；官方示例甚至给盘根 `/ApplyDir:D:\` | 本包按 `directory` 字段 + `attached` 实现（有出处） |
| 7.7 | 官方 `what-is-dism` 明说「**不支持通配符**」，而 DISM 自己的 MUI 顶层帮助示例里有 `/swmfile:install*.swm` 这种带 `*` 的写法 | 本包 `apply-image` 的 examples 里照抄了官方示例（含 `install*.swm`），但**没有把通配符作为字段能力暴露**；差异记录在此 |

---

## 8. 覆盖范围

| 动作 | 命令 | 字段 | danger | execution | requiresAdmin | 真跑过 |
|---|---|---|---|---|---|---|
| `cleanup-image` | `/Cleanup-Image` | 4 | overwrite | run | ✅ | ❌ 740 |
| `get-packages` | `/Get-Packages` | 1 | — | run | ✅ | ❌ 740 |
| `get-package-info` | `/Get-PackageInfo` | 2 | — | run | ✅ | ❌ 740 |
| `add-package` | `/Add-Package` | 3 | overwrite | **info** | ✅ | ❌ 740 |
| `get-features` | `/Get-Features` | 2 | — | run | ✅ | ❌ 740 |
| `enable-feature` | `/Enable-Feature` | 5 | overwrite | run | ✅ | ❌ 740 |
| `disable-feature` | `/Disable-Feature` | 3 | **destructive** | **info** | ✅ | ❌ 740 |
| `get-drivers` | `/Get-Drivers` | 2 | — | run | ✅ | ❌ 740 |
| `get-driver-info` | `/Get-DriverInfo` | 1 | — | run | ✅ | ❌ 740 |
| `get-image-info` | `/Get-ImageInfo` | 3 | — | run | ✅ | ❌ 740 |
| `get-mounted-image` | `/Get-MountedImageInfo` | 0 | — | run | ✅ | ❌ 740 |
| `apply-image` | `/Apply-Image` | 4 | **destructive** | **info** | ✅ | ❌ 740 |

合计 **12 动作 / 30 字段 / 字段出处标注 30 个 = 100%**（校验器原文见 §12）。

### 8.1 风险分级的口径（为什么只有 3 条特殊）

按规范 §2.9 的三级风险：

- **低**（什么都不写）：只读查询 —— `get-packages` / `get-package-info` / `get-features` /
  `get-drivers` / `get-driver-info` / `get-image-info` / `get-mounted-image`，
  以及 `cleanup-image` 的 `/CheckHealth` 模式。
- **中**（`danger` + `confirmText`）：`cleanup-image`（动作级取最严的 `overwrite`，因为它包含
  会改系统的模式）、`enable-feature`、`add-package`。执行前要用户点确认。
- **极高**（`execution: info`，宿主不执行，只给命令与解释）：三条「不可逆或整套覆盖」的：
  - `apply-image` —— **往目标位置写整套文件系统内容**，官方示例直接给盘根 `/ApplyDir:D:\`；
  - `disable-feature` + `/Remove` —— 官方说移除后只能靠 `/Enable-Feature /Source` 装回来，
    本机界面上没有撤销入口；
  - `add-package` —— 往组件存储里装包（官方明说不做完整依赖检查），且耗时经常以十分钟计。

`cleanup-image` 的 `/StartComponentCleanup /ResetBase` 也是不可逆（官方原文：
"Installed Windows updates can't be uninstalled after running /StartComponentCleanup with the /ResetBase option."），
但它和三个只读模式挤在同一个动作里；受 v1 限制（`execution` 是动作级、`confirmPhrase` 也无法按 enum 取值分叉），
本包的做法是 **`confirmText` 里逐字写明这条不可逆**，并在 `mode` 字段的 `help` 里重复一次。
这是本包唯一一处「取舍」，记在这里供复核者判断。

---

## 9. 没验的东西 + 为什么（逐条）

**总原因只有一个：当前账号不是管理员，DISM 在解析参数前就返回 740，本机不存在任何 DISM 成功路径。**

| 没验的 | 具体没验到什么 |
|---|---|
| 全部 12 个动作的**成功路径** | 退出码 0、真实 stdout 内容、输出体积、耗时、中文表头 |
| 全部 12 个动作的**失败路径** | 87（参数错）、50（不支持）这两个码在本机一次都没出现过 |
| 字段风格（`attached` vs `separate`） | 两种写法返回值完全相同，见 §5.2 |
| 输出编码 | 只拿到 ASCII 英文提权提示，判定依据是推断 + 本机代码页事实，见 §4 |
| `exitCodes` 里 0 / 50 / 87 的语义 | 只有官方出处，没有本机实测 |
| `nextSteps` 的 `when` 正则 | 没有真实输出可跑。**缓解**：两个 `when` 的关键词都在本机 `zh-CN` 的
`Dism.exe.mui` / `CbsProvider.dll.mui` 里逐字命中，且都没用 `$` 锚点，不受 CRLF 影响。
逐条核对结果（把资源里的真实句子当夹具跑正则）：
`(?m)(组件存储\|component store)` 命中 6/7（含「未检测到组件存储损坏。」「组件存储(WinSxS)信息:」）；
`(?m)(可以修复\|repairable)` 命中「可以修复组件存储。」与 "The component store is repairable."。
**注意第一版写的是 `(?m)(可修复\|repairable)`，对夹具只命中 1/7** —— 中文资源里的实际措辞是
「**可以**修复组件存储。」，中间夹了「以」字，所以关键词必须是「可以修复」。
这条正是 playbook §2.2「`when` 的正则必须对着真实输出验证过」的又一个实例 |
| 官方示例是否真能跑通 | 例如 `Dism /Online /Get-Packages /Format:Table` 在本机也是 740 |
| 耗时预期 | `ScanHealth`/`RestoreHealth`/`Apply-Image` 的"几分钟到一小时"全部抄自官方文字，没实测 |

**没有跑任何会改系统的动作，也没有跑 `/ScanHealth` / `/RestoreHealth`** —— 除了这个任务的红线，
本机权限本身也让它们跑不起来。

---

## 10. 故意没做的部分（给后来者的地图）

### 10.1 没做的动作

| 没做 | 为什么 |
|---|---|
| `/Remove-Package` | 官方限制「只能用 .cab 指定、不能删 .msu」，且离线映像删包**不会缩小体积**；实用面窄、风险高，留给自定义参数 |
| `/Add-Driver` `/Remove-Driver` `/Export-Driver` | 官方只给了**离线映像**的 `/Add-Driver`（运行中系统只列 `/Get-Drivers` `/Get-DriverInfo` `/Export-Driver`）；而官方还警告「删掉引导关键驱动会让映像开不了机」。本包只做两个只读的驱动查询 |
| `/Mount-Image` `/Unmount-Image` `/Commit-Image` `/Remount-Image` `/Cleanup-Mountpoints` `/List-Image` `/Get-WIMBootEntry` | 这一组是"装一个映像 → 改它 → 提交/丢弃"的工作流，要配临时目录与长时间占用，属于下一批；`/Get-WIMBootEntry` 官方明说只适用于 Windows 8.1。**只做了只读的 `/Get-MountedImageInfo`** 与 `/Get-ImageInfo` |
| `/Capture-Image` `/Append-Image` `/Export-Image` `/Delete-Image` `/Split-Image` `/Optimize-Image` `/Optimize-FFU` `/Apply-FFU` `/Split-FFU` `/Capture-FFU` `/Capture-CustomImage` `/Apply-SiloedPackage` `/Update-WIMBootEntry` | 映像制作/转换/部署流水线，日常"修系统"用不到；`/Apply-FFU` 会**往物理磁盘写整套固件镜像**，属于最危险的一类，更不该由 v1 宿主代按回车 |
| `/Get-PackageInfo` 之外的包信息命令、`/Get-AppxPackage` 一族 | 官方 AppX/预配包页面（`dism-app-package--appx-or-appxbundle--servicing-command-line-options`、`dism-provisioning-package-command-line-options`）**本机实测取不到（HTTP 000/404）**，没有权威出处就不写（R1） |
| `/Cleanup-Image` 的 `/RevertPendingActions`、`/SPSuperseded`、`/AnalyzeComponentStore`、`/Defer` | 前两个官方写明了限制（只该在开不了机的映像上用；Service Pack 时代操作）；`/AnalyzeComponentStore` 只是报告、与 `/CheckHealth` 重叠；`/Defer` 官方说只该在工厂里用。**都记在这里，不收进清单** |

### 10.2 没做的字段

- **全局选项**（`/LogPath` `/LogLevel` `/ScratchDir` `/Quiet` `/NoRestart` `/English` `/WinDir`
  `/SysDriveDir` `/Format` 的全局形态）：出处只在本机 `Dism.exe.mui` 的顶层帮助里，
  官方页面上找不到对应页（§7.4）。**不写**，需要就用自定义参数输入框。
- **`/Apply-Image` 的其余开关**（`/Verify` `/NoRpFix` `/SWMFile` `/WIMBoot` `/Compact` `/EA` `/ApplyDrive` `/SFUFile`）：
  官方列了，但本包只做了"目标 + 索引 + 完整性校验"这三个真正要人决定的；其余留给自定义参数。
- **`/Get-ImageInfo` 的 `/Name` 与 `/Index` 二选一约束**：v1 的 `visibleWhen` 宿主尚未实现，
  所以两个字段都摆在界面上，靠 `help` 说明"只能二选一"。这是规范已知限制，不是本包发明。

---

## 11. 这个工具的坑（会咬人的地方）

1. **非管理员 = 零可用。** 连 `/?` 都是 740。想让 DISM 在界面里真的能用，
   必须用宿主的管理员重启路径（或让用户自己提权）。
2. **DISM 先查权限、再解析参数。** 所以"用 `/?` 试探开关对不对"这招在非管理员下完全无效
   （任何 argv 都得到同一个 740）。这一点与 `ping` / `ipconfig` / `schtasks` 都不同。
3. **报错形态有两种**：`Error: 740` 这种十进制，和 `Error: 0x800f081f` 这种 HRESULT
   （形态取自本机 `zh-CN\Dism.exe.mui` 的 `错误: %1!d!` / `错误: 0x%1!x!`）。
   写自动化判定时**不要用中文措辞匹配成败** —— 本机 UAC 语言是 en-US，同一台机器上
   提权失败的提示就是英文（实测）。
4. **一条命令行只能有一个服务命令**（官方 `what-is-dism` 明确写
   "You can specify multiple drivers or packages, but you cannot specify multiple commands"）。
   宿主如果将来做"串起来跑"的组合动作，会直接踩这条。
5. **不支持通配符**（官方 `what-is-dism`），但 DISM 自己的示例里又出现了
   `/swmfile:install*.swm`（§7.7）—— 不要据此以为通配符能用。
6. **版本兼容是单向的**：目标映像的版本不能高于本机 DISM 的版本（官方 `what-is-dism`）。
   本机 DISM 文件版本 `10.0.26100.8875`，拿它去服务更新的映像会失败。
7. **耗时动作不要杀进程**：组件存储操作被中断会留下 pending 状态（官方
   `dism-operating-system-package-servicing-command-line-options` 的 Limitations 一节讲了这个，
   并给了 `/PreventPending` 作为规避手段）。
8. **`/RestoreHealth` 会联网**（不指定 `/Source` 时默认修复源可能是 Windows Update）。
   在断网机器上它会失败或很慢；本包给了 `/Source` + `/LimitAccess` 这条官方绕法。
9. **`/StartComponentCleanup /ResetBase` 之后再也不能卸载已装的更新**（官方原文，§8.1）。
   这是本包唯一一条"看起来像清理、实际不可逆"的操作，已经在 `confirmText` 与字段 `help` 里各写了一次。

---

## 12. 校验与冒烟

### 12.1 校验器（原文）

```
$ uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
[ OK ] plugins\dism\manifest.yaml  (12 动作 / 30 字段 / 字段出处标注 30 个 = 100%)
...
26/26 个 manifest 通过
字段出处覆盖率: 839/839 (100%)
```

> **说明**：输出里那句「开关溯源（启发式，仅提示）」**不含 dism 的任何开关**。
> 原因见 `scripts/validate-plugins.py` 的 `load_reference_corpus`：它只从
> `docs/reference/<以 id 开头>`、`docs/reference/<以 id 开头的文件>` 以及
> **仅对 `windows-server/administration/windows-commands` 出处的包**才并入的 `docs/reference/win-*` 里取语料。
> dism 的出处是 `windows-hardware/manufacture/desktop/...`，且本包**没有**在 `docs/reference/` 下落快照
> （本任务禁止改 `plugins/dism/**` 之外的任何文件），所以本包的开关一个都没被那层检查覆盖。
> **这不是"检查通过"，是"没检查"** —— 每个字段的出处都在 manifest 的 `doc`/`sources` 里逐条可查。

### 12.2 冒烟

**没跑。** 原因与逐条清单见 §9：本机非管理员，DISM 在解析参数之前就以 740 退出，
**没有任何只读动作能真跑**。清单里所有 `output.resultNote` 都据此如实写明「成功路径未验证」。

### 12.3 临时对象申报

**无。** 本次实测没有创建任何临时对象（没有探针任务、没有临时映像、没有临时注册表项）。
唯一创建的文件全在仓库之外的工作目录 `D:\AI\swpj\dism-probe\`（探针脚本、原始字节输出、官方文档 HTML 快照），
`plugins/dism/` 下只新增 `manifest.yaml` 与本文件。

---

## 13. 规范缺口（不改规范、只记录）

| # | 缺口 | 本包怎么绕的 |
|---|---|---|
| G1 | **`separator` 没有冒号**，而 Windows 命令的规范语法是 `/Switch:Value` | 冒号写进 `prefix`（`prefix: "/Format:"`）。这与 `wevtutil` 撞的是同一个坑，建议规范/playbook 把这条写成明文规则 |
| G2 | **`execution` 与 `danger` 都是动作级**，而 `cleanup-image` 一个动作里有"只读"和"不可逆"两种模式 | 动作级取最严，靠 `confirmText` + 字段 `help` 把不可逆那一条讲清楚（§8.1） |
| G3 | **`requiresAdmin` 没有"二进制级"位置** | DISM 是"整个 exe 都要提权"，本包在 12 个动作上逐个写 `requiresAdmin: true`（并同时写了包级），有冗余但没有更干净的表达方式 |
| G4 | **`locate` 没有"从 exe 文件版本取版本"的能力**（与 `windows-commands.md` §7.3 记的同一条） | 不写 `versionArgs`/`versionPattern`/`minVersion`，把 exe 文件版本 `10.0.26100.8875` 记在本文与 manifest 注释里 |
| G5 | **校验器的开关溯源覆盖不到非 `windows-commands` 出处的包**（§12.1） | 如实写在 §12.1，不为了让报告好看而伪造语料 |
