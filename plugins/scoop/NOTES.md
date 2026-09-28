# Scoop 工具包说明（plugins/scoop）

## 1. 参数知识来源

全部来自本仓库里的两份快照，没有用任何记忆或第三方教程：

| # | 来源 | 路径 | 适用版本 / 抓取日期 |
|---|---|---|---|
| 1 | **本机 scoop 的结构化帮助**（28 个命令，首要依据） | `docs/reference/scoop-help/`（`index.json` 是命令清单，`<command>.txt` 是逐命令帮助，`_meta.json` 记了版本与可执行文件路径） | scoop **0.5.3**，2026-09-27 |
| 2 | **官方 wiki 全站克隆**（36 个页面） | `docs/reference/scoop-wiki/` | wiki 快照 2026-09-27 |

重新生成的命令：`pwsh -File scripts/fetch-scoop-help.ps1`（帮助输出是结构化对象，脚本直接序列化，比解析文本可靠）。

wiki 里被本工具包引用的页面：`Buckets.md`、`Global-Installs.md`、`Persistent-data.md`、`Dependencies.md`、`FAQ.md`、`Quick-Start.md`、`Scoop-Folder-Layout.md`、`Uninstalling-Scoop.md`、`Using-Scoop-behind-a-proxy.md`、`Commands.md`、`App-Manifest-Autoupdate.md`、`Creating-an-app-manifest.md`、`The-'Current'-Version-Alias.md`、`SSH-on-Windows.md`。

**只用于核对环境事实（不作为参数来源）** 的第三处：本机已安装的 scoop 源码 `C:\Users\Steve\scoop\apps\scoop\current\`。用途仅限于"实测/确认"——例如确认 `--version` 的输出形态、确认 `getopt` 接受哪种写法、确认某命令有没有副作用。文中每条此类引用都标了文件与行号。

## 2. 覆盖范围

**28 个命令全部覆盖，共 40 个动作 / 80 个字段 / 字段出处标注 100%。**
`config` / `alias` / `bucket` / `cache` / `shim` 的子命令拆成独立动作（`command` + `commandArgs`），所以动作数多于命令数。

| 动作 | 命令 | 字段数 | 危险级别 |
|---|---|---|---|
| `help` 查看帮助 | `help` | 1 | none |
| `alias-list` 别名——列出 | `alias list` | 1 | none |
| `alias-add` 别名——新增 | `alias add` | 3 | none |
| `alias-rm` 别名——删除 | `alias rm` | 1 | **destructive** |
| `bucket-list` Bucket——列出 | `bucket list` | 0 | none |
| `bucket-known` Bucket——已知列表 | `bucket known` | 0 | none |
| `bucket-add` Bucket——添加 | `bucket add` | 2 | none |
| `bucket-rm` Bucket——删除 | `bucket rm` | 1 | **destructive** |
| `cache-show` 缓存——查看 | `cache show` | 1 | none |
| `cache-rm` 缓存——删除 | `cache rm` | 2 | **destructive** |
| `cat` 查看应用清单 | `cat` | 1 | none |
| `checkup` 环境体检 | `checkup` | 0 | none |
| `cleanup` 清理旧版本 | `cleanup` | 4 | **destructive** |
| `config-get` 配置——读取 | `config` | 1 | none |
| `config-set` 配置——写入 | `config` | 2 | overwrite |
| `config-rm` 配置——删除 | `config rm` | 1 | **destructive** |
| `create` 生成清单骨架 | `create` | 1 | overwrite |
| `depends` 查看依赖 | `depends` | 1 | none |
| `download` 只下载不安装 | `download` | 5 | overwrite |
| `export` 导出安装清单 | `export` | 1 | none |
| `hold` 锁定版本 | `hold` | 2 | overwrite |
| `unhold` 解除锁定 | `unhold` | 2 | overwrite |
| `home` 打开主页 | `home` | 1 | none |
| `import` 导入安装清单 | `import` | 1 | overwrite |
| `info` 应用信息 | `info` | 2 | none |
| `install` 安装应用 | `install` | 7 | overwrite |
| `list` 已安装列表 | `list` | 1 | none |
| `prefix` 应用安装路径 | `prefix` | 1 | none |
| `reset` 重置（切版本） | `reset` | 2 | overwrite |
| `search` 搜索应用 | `search` | 1 | none |
| `shim-list` Shim——列出 | `shim list` | 2 | none |
| `shim-info` Shim——信息 | `shim info` | 2 | none |
| `shim-add` Shim——新增 | `shim add` | 5 | overwrite |
| `shim-alter` Shim——切来源 | `shim alter` | 2 | overwrite |
| `shim-rm` Shim——删除 | `shim rm` | 2 | **destructive** |
| `status` 状态检查 | `status` | 1 | none |
| `uninstall` 卸载应用 | `uninstall` | 3 | **destructive** |
| `update` 更新应用/scoop | `update` | 8 | overwrite |
| `virustotal` VirusTotal 查询 | `virustotal` | 5 | none |
| `which` 定位程序 | `which` | 1 | none |

危险级别的判定规则（本工具包统一按这条规则标，写在字段级别之上）：

- `destructive` —— **会删东西**：删除文件、目录、shim、缓存、配置项、已安装状态。全部写了 `confirmText`。
  共 7 个：`alias-rm`、`bucket-rm`、`cache-rm`、`cleanup`、`config-rm`、`shim-rm`、`uninstall`。
- `overwrite` —— **会替换已有内容但不删**：重装、更新、改写配置、重建 shim、写同名文件。共 11 个。
- `none`（不写 `danger`）—— 只读，或只做"创建新的、且已存在时会报错"的操作（`bucket-add` / `alias-add` 都属于后者：
  已存在同名 bucket / 别名时 scoop 是报错退出，不会覆盖，见 `lib/buckets.ps1:130-133`、`lib/commands.ps1:53-61`）。

## 3. 启动方式：实测结论（本机 scoop 0.5.3）

这是本工具包最需要实测的一件事，因为 scoop 的入口是 **PowerShell 脚本**。

本机 `%USERPROFILE%\scoop\shims\` 下有三个同名文件：

| 文件 | 大小 | 内容 |
|---|---|---|
| `scoop.ps1` | 235 B | `$path = Join-Path $PSScriptRoot "..\apps\scoop\current\bin\scoop.ps1"` → `& $path @args` |
| `scoop.cmd` | 342 B | `where /q pwsh.exe` → `pwsh -noprofile -ex unrestricted -file "<真 scoop.ps1>" %*`（没有 pwsh 时退回 powershell.exe） |
| `scoop` | 343 B | `#!/bin/sh` 脚本（给 bash/zsh 用的） |

用 `System.Diagnostics.Process` + `UseShellExecute = false`（等价于宿主要用的 `CreateProcess`）逐个实测：

| 试验 | 结果 |
|---|---|
| A：直接启动 `scoop.ps1` | **失败**：`The specified executable is not a valid application for this OS platform.`（.ps1 不是可执行映像，CreateProcess 不认） |
| B：直接启动 `scoop.cmd` | **成功**：`--version` / `list` / `prefix 7zip` 输出正确，退出码正确（0） |
| C：直接启动扩展名缺失的 `scoop` | **失败**：与 A 同样的错误（sh 脚本同样不是可执行映像） |
| D：`scoop.cmd info 7zip`（带参数的多次试验） | **成功**：参数正确传到真 scoop，输出是 `info` 的结果而不是用法文本 |

**结论与清单写法**

- `locate.executable: scoop.cmd` —— 这是本机唯一能被 `CreateProcess` 直接启动、且参数/退出码都正确的入口。
- `locate.alternativeNames` **故意留空**：把 `scoop.ps1` 或 `scoop` 写成备选，会让宿主在 `.cmd` 缺失时选中一个**启动不了**的文件（实测 A/C）。`alternativeNames` 的语义是"同族可执行名"，这里没有合格的备选。
- `runtime.useShell: false` —— 不需要经 shell：`.cmd` 由 Windows 自己交给 `cmd.exe`，实测（B/D）参数与退出码都对。写成 `true` 反而会走 `ShellExecute`，丢掉标准输出与退出码。
- `locate.searchPaths` 用 wiki `Scoop-Folder-Layout.md` 记录的四个位置（`%SCOOP%\shims`、`%USERPROFILE%\scoop\shims`、`%SCOOP_GLOBAL%\shims`、`%ProgramData%\scoop\shims`）。本机实测：`%SCOOP%` / `%SCOOP_GLOBAL%` 未设置，`%USERPROFILE%\scoop\shims` 存在，`C:\ProgramData\scoop\shims` 不存在。

**代价（必须在宿主实现时知道）**：走 `.cmd` 意味着参数要经过 `cmd.exe` 重新分词，其中 `%NAME%` 会被 **cmd 展开成环境变量**。实测：

```
CreateProcess(scoop.cmd, 'cat "a%PATH%b"')  →  Couldn't find manifest for 'aC:\Program Files\PowerShell\7;C:\WINDOWS\...b'.
CreateProcess(scoop.cmd, 'cat "foo bar"')   →  Couldn't find manifest for 'foo bar'.   （引号内的空格正常）
CreateProcess(scoop.cmd, 'cat a$b')         →  Couldn't find manifest for 'a$b'.        （$ 正常）
CreateProcess(scoop.cmd, 'cat "a&b"')       →  Couldn't find manifest for 'a&b'.        （& 正常）
```

也就是说：**带空格、`$`、`&` 的参数没问题，带 `%` 的参数会被改掉。** 影响面：`alias add` 的命令体、`config set` 的值（例如 proxy 字符串）、`shim add` 的参数。根治办法只能靠规范支持"解释器 + 脚本"（见 §8 规范缺口第 1 条）。

## 4. `versionPattern` 实测

`locate.versionArgs: ["--version"]`，`locate.versionPattern`：

```
(?m)Current Scoop version:\s*\r?\n.*?v?([0-9]+\.[0-9]+\.[0-9]+)
```

**为什么必须跨行 + `(?m)`**（与 7z 的 P4 同类坑）：`scoop --version` 的第一行固定是 `Current Scoop version:`，**版本号在第二行**。实测第二行有两种形态，取决于 git 状态（源码 `bin/scoop.ps1:21-29`）：

| 形态 | 第二行实际内容 | 正则捕获组 |
|---|---|---|
| git 可用、`scoop_branch` 非 master | `b588a06e chore(release): Bump to version 0.5.3 (resync) (#6436)` | `0.5.3` |
| 否则读 `CHANGELOG.md` | `v0.5.3 - Released at 2025-08-11` | `0.5.3` |

实测方法：用与宿主相同的启动方式（`CreateProcess` + 重定向 stdout）捕获 `scoop.cmd --version` 的**真实输出**，再拿清单里的正则去匹配，两种形态各测一遍：

```
真实输出（19 行）匹配: success=True  group1=[0.5.3]
git-oneline 形态:        success=True  group1=[0.5.3]
CHANGELOG 形态:          success=True  group1=[0.5.3]
```

**`minVersion: "0.5.3"`** —— 等于 `appVersion`。理由：全部参数依据都是 0.5.3 的帮助输出，没有逐一考证每个开关的引入版本，所以不声称兼容更早的版本。（`CHANGELOG.md` 里查不到可靠的"某开关从哪个版本开始有"。）

**这个版本探测本身是脆的**（记在这里以免后来者困惑）：版本号藏在第二行的自由文本里，靠 `Bump to version X.Y.Z` 或 CHANGELOG 行。如果哪天 scoop 换掉这个输出，正则失配会让宿主判不出版本。见 §8 规范缺口第 8 条。

## 5. 退出码：实测结论（重要，宿主要读）

**scoop 在绝大多数失败情形下也返回 0。** 根因（源码依据）：

- `lib/commands.ps1:35-39`：`exec` 用调用运算符执行子命令脚本 —— `& $cmd_path @arguments`；`exit N` 只结束那个脚本，写进 `$LASTEXITCODE`，**不会**变成进程退出码。
- `bin/scoop.ps1` 结尾没有 `exit $LASTEXITCODE`。
- 因此只有 `bin/scoop.ps1` 自己直接 `exit 1` 的那条路径（"顶层命令名不认识"）能让调用方看到非 0。

实测（全部经 `CreateProcess` + `scoop.cmd`，都是"在动手之前就退出"的失败路径，无副作用）：

| 命令 | 退出码 | 输出首行 |
|---|---|---|
| `scoop nosuchcommand` | **1** | `WARN  scoop: 'nosuchcommand' isn't a scoop command. See 'scoop help'.` |
| `scoop help nosuchcommand` | 0 | `WARN  scoop help: no such command 'nosuchcommand'` |
| `scoop info definitely-not-an-app-xyz` | 0 | `Could not find manifest for '…' in local buckets.` |
| `scoop shim info definitely-not-a-shim` | 0 | `ERROR: Local shim not found: definitely-not-a-shim` |
| `scoop cache rm`（缺应用） | 0 | `ERROR: <app(s)> missing` |
| `scoop uninstall`（缺应用） | 0 | `ERROR <app> missing` |
| `scoop alias rm` / `bucket rm` / `bucket add` / `shim rm` / `depends` / `hold`（均缺必填参数） | 0 | 各自报错或打印用法 |

所以 `exitCodes` 段只能这样写，并且明确写上"不能只靠退出码判断成败"：`0` = 命令跑完了（不代表成功）、`1` = 顶层命令名不认识、`2/4/8/16` = `scoop help virustotal` 文档里承诺的码（本机 **未实跑**，而且按上面的原理，这些码**很可能到不了调用方** —— `scoop-virustotal.ps1:385` 是 `exit $exit_code`，同样被子命令调用方式吞掉）。

**给宿主的建议**：判定成败要看输出文本（`ERROR` / `WARN` / `Could not find` / `not found`），退出码只能当辅助。`output.mode` 因此对写操作一律用 `stream`，把原始输出完整展示。

## 6. 输出编码：实测结论

`runtime.encoding` 实测为 **GBK（cp936）**，不是 UTF-8。测量方法（与宿主条件一致：`CreateProcess` + 重定向 stdio + `CreateNoWindow`）：

```
真实 scoop 路径（scoop.cmd info 7zip -v） stderr 原始字节：
  cf b5 cd b3 ce de b7 a8 b1 e6 ca b6 …
  GBK  解码 → 系统无法辨识文件
  UTF-8 解码 → ϵͳ�޷���ʶ�ļ�        ← 乱码
```

用子进程写固定中文字符串再取原始字节（`StandardOutput.BaseStream`）复核，得到同样的结论：`pwsh.exe` 被 `CreateProcess` + 重定向启动时，stdout **和** stderr 都按系统 OEM 代码页（本机 936 = GBK）输出。

**坑**：这个结论依赖"宿主不给子进程改输出编码"。反例实测：如果经 `cmd.exe` 且控制台代码页被设成 65001，同一份输出会变成 UTF-8 字节（`d6 d0 ce c4` → `e4 b8 ad e6 96 87`）。宿主实现 `encoding: gbk` 时请**只做解码**，不要顺手把子进程的输出编码设成 UTF-8，否则会与清单冲突。这一条已记为规范缺口（§8 第 4 条）。

首次踩到的表现：`scoop info 7zip -v` 的 stderr 里那句中文，用 UTF-8 解出来是 `ϵͳ�޷���ʶ�ļ�����`。这也说明 **stderr 必须一起解码展示**——本机 7zip 的 `persist` 链接是坏的，`info --verbose` 每次都会往 stderr 写 PowerShell 错误记录。

## 7. 参数映射上的取舍（照着决策表做的，逐条说明）

- **长开关优先**：帮助里每个选项都是 `-x, --long` 成对给的，清单统一用**长形式**（`--global`、`--all`、`--quiet` …），短形式写进 `help` 文本。原因：命令行更自解释，符合"把命令摆明白"的取向。已实测长形式全部被接受（见 §9 探针表）。
- **`--arch` 用 `style: separate`**：文档写 `-a, --arch <32bit|64bit|arm64>`，而 `lib/getopt.ps1:46-54` 显示长选项带 `=` 时是"下一个 token 才是值"。实测对照最能说明问题：
  `install --arch 64bit` → `ERROR <app> missing`（选项被正确解析）；`install --arch=64bit` → `Option --arch=64bit not recognized.`。
  这也是 7zip 工具包里那句"`separate` 留给 `--output dir` 这类写法"的第一个真实用例。
- **`literal` 风格一次没用上**：scoop 的枚举（arch、config 名）都是"前缀 + 值"，没有 7z 那种 `-aoa`/`-r-` 的复合开关，所以 80 个字段里没有一个是 `literal`，也就不需要 `switchBase`。
- **`default: true` 的用法**：只有 `status.local` 用（默认勾选 `--local`）。理由：不带 `-l` 时 `scoop status` 会对 scoop 自身和每个 bucket 执行 `git fetch`（源码 `libexec/scoop-status.ps1:25`），属于"网络 + 写 `.git`"的副作用；文档给这个开关就是为了关掉远端抓取（`scoop-help/status.txt`）。
- **`config` 的配置项做成 `enum`**：32 个名字全部来自 `scoop-help/config.txt` 的 Settings 一节（含 `aria2-*` 一族）。好处是界面不会拼错；代价是"文档没列的配置名"在这个界面里传不了（记在 §8 第 9 条）。
- **`shim add` 的 `--` 终止符做成了 `flag`**：帮助里的 HINT 明确说第一个 `--` 会被当成 POSIX 选项终止符且不进入参数。字段顺序是"shim 名 → 目标程序 → `--global` → `--` → 透传参数"，这样 `--global` 一定在 `--` 之前被 scoop 自己消费，透传参数一定在 `--` 之后原样交给目标程序。
- **40 个动作全部用工具包级的 `workingDirectory: inherit`**，一个动作级覆盖都没有。唯一的候选是 `create`（它把 `<应用名>.json` 写进**进程当前目录**，源码 `libexec/scoop-create.ps1:27-29` 用 `$pwd`），但见 §8 第 6 条：清单表达不了"选一个目录且不产生任何 argv token"，所以这里不给它声明 `userSelected`（声明了也不会生效），改为在字段的 `help` 里写明"文件落在宿主进程的当前目录"。
- **没有声明任何 `progress.pattern`**：写操作（install/update/download）按安全红线没有实跑，拿不到真实的进度输出，按 R1 不能凭空写正则。宿主按纯流式展示即可。
- **多值字段的 `type` 用 `multiselect` 而不是 `text`**：`install` / `update` / `uninstall` / `download` / `cleanup` / `reset` / `hold` / `unhold` / `virustotal` / `cache show` / `cache rm` 的应用名、`shim rm` 的 shim 名、`shim list` 的正则、`shim add` 的透传参数，都是"自由填写、可以多个"的值，一共 14 个字段。
  `style` 一律 `positional` + `repeatable: true`（这是手册的决策表要求的）；`type` 用 `multiselect` 是**跟着 7zip 工具包的既有做法**（它的 `exclude` / `includeOnly` 同样是 `multiselect` + `repeatable` 而没有 `values`）。原因是实测了宿主的表单构建器：只有 `multiselect` / `files` / `paths` / `directories` 会产出多值控件（一个文本框、每行一项），`text` 类型即使写了 `repeatable: true` 也只能填一个值。详见 §13。

## 8. 规范缺口（v1 表达不了的能力，未擅自改规范）

1. **无法表达"用解释器启动脚本"**。scoop 的真入口是 `scoop.ps1`，但 `locate` 只能给一个可执行文件名、`runtime` 没有 `interpreter`/`script` 字段，所以只能退而声明包装脚本 `scoop.cmd`。代价已经实测出来：参数经 `cmd.exe` 重新分词，`%NAME%` 被展开。
   建议：`locate` 增加 `interpreter`（如 `pwsh.exe`）+ `scriptArgs`（如 `["-NoProfile","-ExecutionPolicy","Unrestricted","-File","%SCOOP%\\apps\\scoop\\current\\bin\\scoop.ps1"]`），让宿主直接起解释器。
2. **`requiresAdmin` 只有工具包级**。`-g/--global` 需要管理员权限（wiki `Global-Installs.md`；源码 `libexec/scoop-install.ps1:62-64` 会 `abort 'you need admin rights...'`），但 `runtime.requiresAdmin` 一写就是整包生效，表达不了"只有勾了 `--global` 的动作需要提权"。
   建议：字段级 `requiresAdmin` / 动作级 `requiresAdmin`。
3. **`exitCodes` 只有工具包级**，而 scoop 是"同一个数字在不同命令下含义不同"：virustotal 的 `2` = 有不安全的包，`shim` 的 `2` = 另一个作用域存在同名 shim（源码 `libexec/scoop-shim.ps1:180`）。本清单只能把各命令的含义都写进同一条 `meaning` 里。
   建议：动作级 `exitCodes`（覆盖工具包级）。
4. **`encoding` 是单值，且语义里不含"宿主该不该动子进程的编码"**。实测同一份 scoop 输出，宿主改不改子进程输出编码会得到 GBK 或 UTF-8 两种字节。规范只说"按 runtime.encoding 解码"，没说"不要给子进程设置输出编码"。建议在规范里写明宿主的责任，或允许声明 `childEncoding: utf-8`（即由宿主强制）。
5. **无法表达交互式输入**。`scoop create` 用 `Read-Host` 问应用名与版本、`scoop shim alter` 用 `$Host.UI.PromptForChoice` 选来源（源码 `libexec/scoop-create.ps1:52`、`libexec/scoop-shim.ps1:200`）。v1 刻意不做交互（规范 §8），但清单里也**没有地方标记"这个动作是交互式的、宿主别放出来"**，只能整包禁用或写进 `help` 文本。建议加 `interactive: true`（宿主默认不展示，或提示需要 stdin）。
6. **表达不了"选一个工作目录，且不产生 argv token"**。`scoop create` 就是这种：它把 JSON 写到进程当前目录，而命令行没有任何参数能指定目录。
   规范里 `workingDirectory: userSelected` 的语义没有定义；已实现的宿主把它解析为"取名为 `workingDir` 或 `outputDir` 的**字段**的值"（`src/AllTool.App/MainWindow.xaml.cs:688`），而**任何字段都会展开成 argv token**（`src/AllTool.Core/Execution/ArgvBuilder.cs`），所以对"不接受目录参数却要选目录"的命令根本无法表达。
   本清单因此对 `create` 用默认的 `inherit`，把限制写进字段 `help`。建议：规范定义 `userSelected` 的语义，并允许一个"只作为宿主输入、不参与 argv"的字段（例如 `argStyle: none` / `hostOnly: true`）。
7. **无法表达 shell 重定向**。`scoop export` 的用法行是 `scoop export > scoopfile.json`，而 v1 刻意不含重定向/管道。所以"导出到文件"只能由宿主提供（本清单用 `output.mode: capture` + `resultNote` 说明）。
8. **`versionPattern` 只有一个，取不到时的处置也没定义**。scoop 的版本号在 `--version` 输出的第二行自由文本里（两种形态），正则一旦失配，宿主就只能"判不出 minVersion"，而规范没写这时该禁用还是放行。建议允许 `versionPattern` 是数组，并定义"全部失配"时的策略。
9. **没有字段级"取值来自外部数据"的能力**。`config` 的配置项、`shim` 的名字、应用名这些理想情况下应该从 `scoop config` / `scoop shim list` / `scoop list` 的实际结果里生成下拉框，v1 只能写死一份文档枚举或退化成自由文本。

## 9. 真机冒烟测试结果（2026-09-27，scoop 0.5.3，Windows 11 26200）

测试方式与宿主一致：`System.Diagnostics.Process` + `UseShellExecute = false` + 重定向 stdout/stderr + `CreateNoWindow`，可执行文件用清单里的 `scoop.cmd`；命令来自 `manifest.yaml` 里 `examples` 的 `args`（用 pyyaml 从清单直接抽出来跑，保证"清单写的"与"跑的"是同一条）。

### 9.1 `examples` 全跑：22 条，22 条与 `expectExitCode: 0` 一致

| 动作 | 命令（= 清单 examples 的 argv） | 实际退出码 | 首行输出（截断） |
|---|---|---|---|
| `help` | `scoop help` | 0 | `Usage: scoop <command> [<args>]` |
| `help` | `scoop help install` | 0 | `Usage: scoop install <app> [options]` |
| `alias-list` | `scoop alias list --verbose` | 0 | `INFO  No alias found.` |
| `bucket-list` | `scoop bucket list` | 0 | `Name  Source  Updated  Manifests` |
| `bucket-known` | `scoop bucket known` | 0 | `main` |
| `cache-show` | `scoop cache show` | 0 | `Total: 2 files, 54.6 MB` |
| `cat` | `scoop cat 7zip` | 0 | `{`（manifest JSON） |
| `checkup` | `scoop checkup` | 0 | `No problems identified!` |
| `config-get` | `scoop config` | 0 | `last_update : 2026/9/27 21:19:20` |
| `config-get` | `scoop config scoop_branch` | 0 | `master` |
| `depends` | `scoop depends git` | 0 | `Source  Name` |
| `export` | `scoop export` | 0 | `{`（scoopfile JSON） |
| `export` | `scoop export --config` | 0 | `{`（含配置的 JSON） |
| `info` | `scoop info 7zip` | 0 | `Name : 7zip` |
| `list` | `scoop list` | 0 | `Installed apps:` |
| `prefix` | `scoop prefix 7zip` | 0 | `C:\Users\Steve\scoop\apps\7zip\current` |
| `search` | `scoop search git` | 0 | `Results from local buckets...` |
| `search` | `scoop search hg` | 0 | `Results from local buckets...` |
| `shim-list` | `scoop shim list` | 0 | `Name  Source  Alternatives  IsGlobal  IsHidden` |
| `shim-info` | `scoop shim info 7z` | 0 | `Name : 7z` |
| `status` | `scoop status --local` | 0 | `Name  Installed Version  Latest Version  Missing Dependencies  Info` |
| `which` | `scoop which 7z` | 0 | `~\scoop\apps\7zip\current\7z.exe` |

顺带核对的行为差异：`info 7zip --verbose` 首行变成 `Name           : 7zip`（多了目录大小等字段），符合 `-v` 的文档描述；
`status --local` 直接出表（没有"远端抓取中"之类的输出），符合 `-l` 的文档描述。

### 9.2 长开关接受性探针（都在"动手之前"退出，无副作用）

只读命令直接跑；写命令用一个"缺必填参数会先报错退出"的调用来证明选项被解析（不是我在猜）：

| 探针命令 | 实际退出码 | 首行输出 | 判断 |
|---|---|---|---|
| `scoop info 7zip --verbose` | 0 | `Name : 7zip`（stderr 有 PowerShell 错误记录） | `--verbose` 接受 |
| `scoop alias list --verbose` | 0 | `INFO  No alias found.` | `--verbose` 接受 |
| `scoop status --local` | 0 | 状态表 | `--local` 接受 |
| `scoop export --config` | 0 | `{` | `--config` 接受 |
| `scoop shim list --global` | 0 | （空：没有全局 shim） | `--global` 接受 |
| `scoop shim info 7z --global` | 0 | `ERROR: Global shim not found: 7z` | `--global` 接受（不是 "Option not recognized"） |
| `scoop install --global` | 0 | `ERROR <app> missing` | `--global` 接受，且**没有安装任何东西** |
| `scoop install --arch 64bit` | 0 | `ERROR <app> missing` | `--arch <值>` 两个 token 的写法正确 |
| `scoop install --arch=64bit` | 0 | `scoop install: Option --arch=64bit not recognized.` | **错误写法对照**：证明必须用 `separate` |
| `scoop download --force` | 0 | `ERROR <app> missing` | `--force` 接受，无下载 |
| `scoop uninstall --purge` | 0 | `ERROR <app> missing` | `--purge` 接受，无卸载 |
| `scoop hold --global` / `unhold --global` | 0 | `Usage: scoop hold <apps>` / `Usage: scoop unhold <app>` | `--global` 接受，无副作用 |
| `scoop shim add --global` | 0 | `ERROR <shim_name> must be specified for subcommand 'add'` | `--global` 接受，无写入 |

### 9.3 未实跑的清单（安全红线：只跑只读/无副作用的命令）

清单里定义了这些动作，但**没有实跑**，因为它们会改动系统。逐个给原因：

| 动作 | 未实跑的原因 |
|---|---|
| `install` | 会真的安装软件（且会先自动更新 scoop 自身） |
| `uninstall` | 卸载并删除安装目录；`--purge` 永久删除 persist 数据 |
| `update` | 更新应用或 scoop 自身（联网 + 改文件） |
| `cleanup` | 删除保留的旧版本目录 |
| `reset` | 重建 junction / shim / 环境变量 |
| `import` | 改配置、加 bucket、装应用 |
| `download` | 联网下载并写缓存 |
| `hold` / `unhold` | 改写应用的锁定状态 |
| `bucket add` | `git clone`（联网 + 写 bucket 目录） |
| `bucket rm` | 递归删除 bucket 目录 |
| `cache rm` | 删除缓存文件 |
| `alias add` | 往 shims 目录写 `scoop-<name>.ps1` 并改配置 |
| `alias rm` | 删除上面那个文件与配置项 |
| `shim add` | 写 shim 文件 |
| `shim rm` | 删 shim 文件 |
| `shim alter` | 交互式选择 + 重命名 shim 文件 |
| `config set` / `config rm` | 改写 `~/.config/scoop/config.json`（本机该文件里没有 token，但仍属写操作） |
| `create` | 交互式（`Read-Host` 两次）+ 往当前目录写 JSON |
| `home` | 会拉起默认浏览器（对外部程序有副作用） |
| `virustotal` | 联网访问 virustotal.com，需要 API key；并且源码显示它在 scoop 过期时会**先执行 scoop 自身更新**（`libexec/scoop-virustotal.ps1:44-50`），风险超出只读范围 |

因此这 22 个动作**没有 `examples`**（`examples` 在本仓库里兼作冒烟夹具，按 R4"examples 全跑"的要求，写进去就必须跑）。这些动作的**选项接受性**用 §9.2 的探针单独验证过了；字段本身的出处见每条动作的 `sources` / 每个字段的 `doc`。

### 9.4 冒烟中发现的三个问题

1. **退出码不可靠**（§5）：失败路径实测也返回 0，宿主必须看输出。
2. **`info --verbose` 会往 stderr 写 PowerShell 错误记录**：本机 7zip 的 `persist` 链接是坏的，所以 `Get-ChildItem` 报 `系统无法辨识文件名。: 'C:\Users\Steve\scoop\persist\7zip\Codecs'`。不是 scoop 的 bug，但说明 stderr 要一起展示，而且 stderr 是 GBK。
3. **`scoop which 7z` 输出的是 `~\scoop\apps\7zip\current\7z.exe`**（带 `~` 未展开），与本机 `prefix 7zip` 输出完整路径不同。展示时不要自己拼路径。

## 10. 这个工具的坑（给后来者和宿主实现者）

1. **退出码几乎恒为 0**（§5）。这是最容易写错宿主逻辑的地方。
2. **装个软件会先更新 scoop 自身**：`scoop install` / `update <app>` / `download` / `virustotal` 在 scoop 过期时会先执行自身更新（wiki `Quick-Start.md`；源码 `libexec/scoop-install.ps1:66-72`）。界面上如果不说清楚，用户会以为只是装了一个包，实际还动了 scoop 的 git 仓库。`--no-update-scoop` 是文档给的关闭方式。
3. **`%NAME%` 陷阱**（§3）：走 `scoop.cmd` 时参数里的 `%...%` 会被 `cmd.exe` 展开成环境变量。
4. **需要管理员权限的动作**：所有 `--global` 相关字段（install / update / uninstall / cleanup / hold / unhold / shim 各子命令）。wiki `Global-Installs.md` 明确写 "Global installs require admin permissions"，因为要写 `%ProgramData%\scoop` 并设置系统环境变量。**本机当前用户不是管理员**（`check-env.ps1` 一致项 `environment.user.isAdmin = False`），这些字段在本机勾了必然失败。规范目前只能整包声明 `requiresAdmin`，所以本工具包写 `false`，把这些限制写在各自的 `help` 里。
5. **`scoop status` 默认会 `git fetch`**：对 scoop 自身与每个 bucket 各来一次（源码 `libexec/scoop-status.ps1:23-33`）。所以 `status.local` 默认勾选。
6. **全局操作的路径不是 `%USERPROFILE%`**：`%ProgramData%\scoop`（wiki `Scoop-Folder-Layout.md`）。
7. **`scoop search` 的 query 在未开启 `use_sqlite_cache` 时是正则**（`scoop-help/search.txt`）——用户输入 `.` 或 `*` 会得到意外结果；开启后是"部分匹配"语义。同一个界面上语义会随配置变，`help` 文本里写明了。
8. **`scoop export --config` 会把 `gh_token` / `virustotal_api_key` 打进输出**（`scoop-help/config.txt` 的这两个设置项）。展示/保存日志时要注意。
9. **`alias add` 的命令体不要加引号**：帮助里的示例 `scoop alias add rm 'scoop uninstall $args[0]' 'Uninstall an app'` 中的引号是给 PowerShell 用的；我们以 argv 数组传参，引号会成为命令体的一部分。再叠加第 3 条的 `%` 限制。
10. **交互式命令**：`create`（`Read-Host`）、`shim alter`（选择框）、`uninstall scoop`（wiki 说要输 y 确认）。宿主没有 stdin 交互能力时它们会挂住等待输入 —— 这是"界面卡死"而不是"报错"，宿主应把它们标成不可用或加超时。
11. **`scoop bucket list` 在没有 bucket 时返回 2**（源码 `libexec/scoop-bucket.ps1:61-70`），但那个 2 同样到不了调用方（§5）；本机有 bucket，实测 0。

## 11. 关于"规范说不了、但 scoop 有"的东西

- `scoop depends` 的实现里有 `-a/--arch`（源码 `libexec/scoop-depends.ps1:10`），但**帮助快照里没有**，按 R1 没有写进清单。
- `scoop bucket rm <name>`、`scoop cache rm <app>` 这类子命令的详细用法，帮助快照只给了用法行（`bucket.txt` 的 `add|list|known|rm`、`cache.txt` 的 `show|rm`），命令实现自身还打印了 `usage: scoop bucket rm <name>` 之类的字符串。清单按用法行的范围写，没有额外发明。
- `-h` / `--help` / `/?` 作为顶层"子命令"、以及 `-v` / `--version`（源码 `bin/scoop.ps1:18-21`）在帮助快照里都没有。`--version` 只用于 `locate.versionArgs`（这是工具包发现工具的必需能力，playbook F5 也要求实跑版本命令）；顶层 `-v` / `-h` 没有做成动作，因为 `command` 字段的语义是"首个非开关参数"。

## 12. 还没做的

- **`examples` 只覆盖只读命令**（22 条 / 18 个动作）。写操作的"官方示例"（例如 `scoop install git`、`scoop uninstall git`、`scoop reset terraform@0.11.14`、`scoop config proxy currentuser@default`）都留在上面 §9.3 的说明里，没有写进 `examples`——原因见 §9.3。
- **`progress.pattern` 一个都没写**：写操作没跑过，没有可靠的进度输出样本（R1）。
- **`minVersion` 只敢写 0.5.3**：没有考证每个开关的引入版本。
- **`preventConcurrentRuns` 写 `false`**：scoop 的帮助与 wiki 都没提锁或并发限制（源码里只有 `isFileLocked`，那是判断目标文件是否被占用，不是 scoop 自己的锁）。规范里这个键是宿主提示而非工具事实，所以没有写 `true`；建议宿主自己把写操作串行化（两个 `scoop install` 同时跑会争抢 bucket git 仓库与 shims）。
- **全局（`--global`）路径没有实际验证**：本机没有 `C:\ProgramData\scoop\shims`，当前用户也不是管理员。

## 13. 与已实现的宿主核对（本清单不是纸上设计）

写这份清单的过程中，仓库里已经有了可用的 WinUI 3 宿主与 `AllTool.Core`。因此额外做了一次"清单 vs 真实实现"的核对，逐条确认本清单用的写法宿主真的支持：

| 清单里的写法 | 宿主实现 | 核对结果 |
|---|---|---|
| `locate.executable: scoop.cmd`（带扩展名） | `ToolLocator.FindExecutable`：带扩展名的名字**原样试**，否则才依次补 `.exe/.cmd/.bat/.com` | 通过；`ToolLocator` 的类注释本身就以 scoop 为例说明"不要返回 .ps1" |
| `runtime.encoding: gbk` | `EncodingResolver.Resolve("gbk")` → `Encoding.GetEncoding(936)`，并已注册 `CodePagesEncodingProvider` | 通过；宿主测试里就有"gbk 解析为代码页 936"的断言 |
| `style: separate`（`--arch`） | `ArgvBuilder.Append` 有 `FieldStyle.Separate` 分支 → 先 prefix 再值两个 token | 通过；本工具包是 `separate` 的第一个真实用例 |
| `style: flag` + `default: true`（`status.local`） | 界面用 `field.Default is bool b ? b : ...`，`ArgvBuilder.AppendFlag` 同时接受 bool 与 "true" 字符串 | 通过 |
| `values[].isDefault` + `value: ""`（`config` 名、`--arch` 默认项） | 界面先按 `isDefault` 预选；空值不产生 token（`ArgvBuilder.IsEmpty`） | 通过：默认项不会输出多余的 `--arch`/配置名 token |
| 字段 `advanced` / `group` / `repeatable` / `accept` / `required` | `ManifestField` 模型与表单构建器都有对应处理 | 通过（`advanced`/`group`/`accept`/`required` 都生效；`repeatable` 例外，见下） |
| `examples[].expectExitCode` | `ManifestExample.ExpectExitCode` | 通过 |
| 动作 `danger`/`confirmText` | `MainWindow`：`destructive` 时弹二次确认，用 `ConfirmText` 作文案 | 通过；本工具包 7 个 destructive 动作都写了 `confirmText` |

**一个发现：`repeatable` 只有在部分 `type` 上才生效。** 表单构建器（`MainWindow.xaml.cs:294-345`）只对
`multiselect` / `files` / `paths` / `directories` 渲染"每行一项"的多值文本框（取值器返回 `List<string>`）；
`text` / `textarea` / `file` / `directory` 一律返回单个字符串，**完全不看 `repeatable`**。
所以同一个"自由多值"概念，写 `type: text` + `repeatable: true` 在现有宿主上只能填一个值。
本清单因此对 14 个自由多值字段用了 `multiselect`（与 7zip 的 `exclude` / `includeOnly` 一致），多应用安装/卸载在界面上才真的可用。
建议宿主让 `repeatable: true` 对 `text` / `textarea` 也渲染成多值（每行一项），这样规范里的 `repeatable` 才名副其实；这条同时记进 `project-state.json` 的 `openDecisions` 更合适（属于宿主实现取舍，不是 scoop 特有的问题）。

整合验证：`dotnet test src\AllTool.slnx` → **69/69 通过**。其中的 `RealManifestTests` 会调用 `ManifestLoader.LoadAll("plugins")` 遍历加载**所有**工具包（含本清单），因此"清单能被真实加载器反序列化并通过 `ManifestValidation`"是被测试覆盖的，不只是 python 校验器认可。

一个反向结论：`workingDirectory: userSelected` 在已实现的宿主里等于"取名为 `workingDir` 或 `outputDir` 的字段值"（`MainWindow.xaml.cs:688`）。`scoop create` 不接受任何目录参数，而**任何**字段都会展开成 argv token，所以给它声明 `userSelected` 是无效的 —— 这就是本清单没有用这个值、并把它记成规范缺口的原因（§8 第 6 条）。
