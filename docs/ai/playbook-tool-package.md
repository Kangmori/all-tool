# 工具包开发手册（Playbook）

> 给 AI 智能体。目标：**从零产出一个通过校验、且真机冒烟过的工具包**。
> 前置：已读 `AGENTS.md`，已跑过 `scripts/check-env.ps1`，基线校验是绿的。

---

## 阶段 0 — 判断该不该做，以及文档在哪

### 0.1 先确认这个软件值得做成工具包

| 判据 | 说明 |
|---|---|
| 已安装且能在 PATH 里找到 | `Get-Command <name>` |
| **你打算覆盖的那条路径上的开关数有限** | 判据**不是**"这个软件有多少命令"。子命令树型工具（uv 顶层 21 个命令、帮助页 49 个、单 `uv run` 就 60+ 开关）按"命令数 ≲ 30"会被误判成不该做——实际做法是**只覆盖常用命令 + 每条只暴露最常用的 3–8 个开关**，其余写进 NOTES 的"故意没做"。反例：`ffmpeg` 选项几百个且彼此耦合，要非常挑着做 |
| 非纯交互式 | 全屏 TUI（如 `vim`）、必须回答 y/n 的向导、会弹 GUI 并等待的工具（实测 `cleanmgr /?` 会弹窗永久等待），v1 不做或只做"打开它" |
| 官方文档存在且可定位 | 没有权威文档就不能做（R1） |

**多来源冲突时听谁的**（uv 与 Windows 命令集都撞到过）：

- **字段依据以官方文档为准**（R1 要的是可引用的出处）；
- **行为以实测为准**（默认值、退出码、输出流、编码）；
- **两者不一致时**：按官方文档写字段，把差异**逐条记进 NOTES.md**。
  实例：本机 `netstat /?` 多出 7 个开关且 `-d` 的说明自相矛盾、`tasklist /?` 多一个 `/APPS`
  ——都按"官方为准、差异记录"处理，不收录来源只有一侧的开关。

### 0.2 找到权威文档 —— 按优先级，只有这三类算数

| 优先级 | 类型 | 如何拿到 | 例子 |
|---|---|---|---|
| 1 | 随软件分发的帮助文件（CHM / man / 本地 HTML） | 在安装目录里找 `*.chm`、`man\`、`docs\` | `%USERPROFILE%\scoop\apps\7zip\26.03\7-zip.chm` |
| 2 | 官方文档站 / 官方仓库 wiki | **用 pwsh 抓**（`web_fetch` 在本机不可用，见 development.md §4.4） | `https://github.com/ScoopInstaller/Scoop/wiki` |
| 3 | 程序自身输出的帮助 | `--help` / `help <cmd>`。**注意**：它可作为补充，但不能作为唯一来源，因为它随语言变化、且不能说明默认值与互斥关系 | `scoop help install` |

**不算数的来源**：博客、论坛问答、第三方教程、AI 的记忆。

### 0.3 采集命令模板

```powershell
# 找到安装位置与帮助文件
Get-Command <name> | Select-Object Source,Version
Get-ChildItem (Split-Path (Get-Command <name>).Source) -Recurse -Include *.chm,*.html,*.md -EA SilentlyContinue |
  Select-Object FullName,Length
```

---

## 阶段 1 — 把文档落盘成快照

任何抓取都要落进 `docs/reference/`，并在 `docs/reference/README.md` 里补上复现步骤。理由：可溯源、可 diff、离线可用。

### 情况 A：有 CHM

```powershell
# hh.exe 是 Windows 自带的
$chm = '<CHM 绝对路径>'
$out = 'docs\reference\<tool>-chm'
hh.exe -decompile $out $chm
Start-Sleep -Seconds 3     # hh.exe 是异步的，等它写完

# 转 Markdown（需要 pandoc）
Get-ChildItem "$out" -Recurse -Filter *.htm | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $out).Path.Length).TrimStart('\')
    $dst = Join-Path 'docs\reference\<tool>-md' ($rel -replace '\.htm$','.md')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    pandoc -f html -t gfm --wrap=none $_.FullName -o $dst
}
```

反编译出的原始 HTML 目录要加进 `.gitignore`（派生数据、体积大），转换后的 Markdown 提交。

### 情况 B：有 wiki 仓库

wiki 本身就是 git 仓库，整站克隆：

```powershell
git clone --depth 1 https://github.com/<owner>/<repo>.wiki.git docs\reference\<tool>-wiki
Remove-Item -Recurse -Force docs\reference\<tool>-wiki\.git   # 必须删，否则会被当成 gitlink（见 development.md P10）
```

### 情况 C：只有网页文档或命令行帮助

```powershell
# 网页：用 Invoke-WebRequest（web_fetch 不可用）
Invoke-WebRequest '<URL>' -UseBasicParsing -OutFile 'docs\reference\<tool>\index.html'
pandoc -f html -t gfm --wrap=none 'docs\reference\<tool>\index.html' -o 'docs\reference\<tool>\index.md'

# 命令行帮助：抓下来存盘，并记录版本
& <name> help                       # 或 <name> --help / <name> help <cmd>
```

**若帮助输出是结构化对象**（PowerShell 模块类工具，如 scoop），优先按对象抓取并序列化成 JSON，
比解析文本可靠。参考 `scripts/fetch-scoop-help.ps1`。

---

## 阶段 2 — 提取事实（这一步不能跳）

在写清单之前，必须先把下面五类事实**写下来**（写进 `docs/reference/<tool>/FACTS.md` 或直接进脑子里的表格）：

| # | 事实 | 从哪来 |
|---|---|---|
| F1 | 命令清单（每个命令的字母/名称 + 一句话） | 命令索引页 |
| F2 | **每个命令各自允许哪些开关**（关键！） | 命令页的"可用开关"一节；没有该节时看语法行与示例 |
| F3 | 全局语法：位置参数的顺序要求、通配符规则、引号规则 | 语法总览页 |
| F4 | 退出码语义 | 专门的退出码页 |
| F5 | 版本输出格式（用于 `versionPattern`） | 实跑一次版本命令，看**首行是不是空行**（见 P4） |

### 若文档结构规整，把 F2 变成机器可读的矩阵

`scripts/extract-7zip-matrix.ps1` 是模板。产出的矩阵 JSON 会被 CI 用来校验清单，价值极高：

- 能自动发现"把 `-o` 挂到 `a` 命令上"这类错误
- 这类错误靠人读文档极难发现，但会让用户看到莫名其妙的失败

**矩阵必须同时记录两处信息**（原因见 development.md §6 第 3 层）：

- `switches`：命令页白名单一节列出的开关
- `mentioned`：该页任何位置出现过的开关（语法行、示例里的也算）

写提取脚本时注意：正则匹配开关前一个字符不能是字母数字，否则 `7-Zip` 里的 `-Zip` 会被误判。

---

## 阶段 3 — 写 manifest.yaml

从 `docs/ai/templates/manifest.template.yaml` 复制骨架，逐段填。

### 3.1 顺序铁律

```
spec → id → name → manifestVersion → appVersion → description → homepage → license → tags
→ sources → locate → runtime → exitCodes → actions
```

`id` 必须等于目录名。动作里 **`archive` 这类"必须是第一个位置参数"的字段必须声明在最前**（字段声明顺序 = argv 顺序）。

**但"位置参数在前"只是这半边规则。** 字段顺序被赋予了超出"显示顺序"的语义负担——
**作者必须知道被包装程序对 argv 的边界要求**，而边界有两种相反的情形：

| 情形 | 例子 | 字段声明顺序 |
|---|---|---|
| 某个位置参数必须最先出现 | 7z 要求归档名是命令后的第一个文件名 | 那个位置参数放最前，其余随意 |
| **该程序自己的开关必须排在位置参数之前** | **uv `run` / `tool run`**：第一个非开关参数会被当成"要运行的程序"，**其后的参数原样转交给它** | 把开关字段全部声明在位置参数字段**之前** |

第二种情形很容易踩错，而且**错了不会报错、只会静默传错参数**。uv 的实证：

```yaml
# 正确
- id: noProject   # --no-project  ← uv 自己的开关在前
- id: command     # python        ← 第一个非开关参数：uv 从这里开始不再解析自己的选项
- id: commandArgs # -c print(1)   ← 原样转交给 python
```

若按"位置参数放最前"的常规思路把 `command` 声明在第一位，会生成 `uv run python --no-project`，
于是 `--no-project` 被当作 **python 的参数**传下去，uv 反而收不到。

**判断方法**：先看文档里"第一个非开关参数之后的内容会被如何处理"。若答案是"原样转交/不再解析"，
那么这个位置参数**之前**的所有开关都必须排在它前面。
（规范 §9 的 D3 就是"字段顺序即 argv 顺序是否足够"，uv 是它的第一个实证。）

### 3.2 字段风格选择（决策表）

| 文档里的写法 | 用哪种 style | 写法 |
|---|---|---|
| 独立的参数值（文件名、路径） | `positional` | 直接声明；可重复的加 `repeatable: true` |
| `-o{dir}`、`-t{type}`、`-p{pwd}` | `attached` | `prefix` 含减号，`separator` 默认空 |
| `--output <dir>` | `separate` | `prefix` + 值两个 token |
| `-y`、`-sfx`（无参数开关） | `flag` | `prefix: "-y"`，`default: false` |
| `-ao[a\|s\|t\|u]`、`-r[- \|0]`、`-slf[h][s][n]` | `literal` | 每个选项写 `args: [...]`；**必须**写 `switchBase`；默认项 `args: []` 表示不输出 |
| `-x{a}`、`-i{a}`、`-v{size}`（可多次） | `repeated` | `prefix: "-x!"`，`repeatable: true` |
| 成对/多值位置参数（如 `rn 旧 新 旧 新`） | `positional` + `positionalMode: perLine` | 用 `textarea`，每行一对 |

**空值规则**：`null` / `""` / `[]` 不产生任何 token。所以"可选文本字段留空"天然等于"不加该开关"。
需要"默认不输出任何东西"的枚举项就写 `value: ""` + `args: []`。

### 3.3 每个字段必须问自己的三个问题

1. 这个开关/参数**在文档哪一页**？（写进 `doc:`）
2. 它**默认是什么**？（写进 `default`，并在 `values` 里把默认项标 `isDefault: true`）
3. 它**对哪些命令有效**？（若矩阵显示当前命令不支持它，那就**不要写**）

### 3.4 别忘的三件事

- `danger: destructive` 给会不可逆破坏数据的动作（删除、覆盖、格式化），并写 `confirmText`
- `workingDirectory: outputDir` 给"默认写到当前目录"的解压/导出类动作（P9）
- `output.progress.pattern` 给有进度输出的动作，正则从官方文档或实跑输出里抄

---

## 阶段 4 — 校验循环

```powershell
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
```

**循环直到全绿。** 常见报错与对策：

| 报错 | 原因 | 对策 |
|---|---|---|
| `Additional properties are not allowed ('xxx' was unexpected)` | 字段名拼错，或用了规范里没有的键 | schema 是严格的，这是故意的。改对名字或改规范（走变更流程） |
| `'<value>' is not one of [...]` | 枚举值不在允许集合 | 检查 `type` / `style` / `severity` 的合法值 |
| `'retrieved' is not of type 'string'` | YAML 裸写日期被隐式解析 | 写成 `"2026-09-27"`（校验器也会自动规整，但仍应加引号） |
| `'prefix' is a required property` | 用了 `attached`/`flag`/`repeated` 却没给 `prefix` | 补上，或检查 style 是否选错 |
| `'values' is a required property` | 用了 `literal` 却没给 `values` | 补上每个选项及其 `args` |
| `开关 -o 不在官方允许列表内` | 把该命令不支持的开关挂上了 | **这条几乎总是真的错误**（不要绕过校验器，去核对文档） |
| `id 'x' 与目录名 'y' 不一致` | 目录名与 id 不匹配 | 改一个 |

**禁止**为了过校验而放宽 schema 或删除白名单规则——那两条规则是踩坑换来的。

---

## 阶段 5 — 真机冒烟测试（R4，不可跳）

```powershell
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
$tmp = Join-Path $env:TEMP '<tool>-smoke'
Remove-Item -Recurse -Force $tmp -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content "$tmp\a.txt" 'hello' -Encoding ascii
cd $tmp

# 把 manifest 里 examples 的命令逐条实跑，记录退出码
foreach ($c in $cases) {
  $out = & <name> @($c.args) 2>&1 | Out-String
  "{0} exit={1,-3} {2}" -f ($(if ($LASTEXITCODE -eq 0) {'OK  '} else {'FAIL'})), $LASTEXITCODE, $c.name
}
```

**必须额外验证的两件事**：

1. **`versionPattern` 真的能匹配**（实跑 `locate.versionArgs`，用清单里的正则去匹配，打印捕获组）。
   已有一次前车之鉴：`7z i` 首行是空行，导致 `^7-Zip` 匹配失败（P4）。
2. **行为差异符合文档**（如 7z 的 `x` 保留目录结构、`e` 抹平；`-aoa` 覆盖、`-aos` 跳过）。

### 5.1 冒烟脚本自己必须有自检（血泪教训，务必照做）

工具类软件常常"读操作安全、写操作危险"。如果你用"白名单判定哪些命令可以跑"，那么
**这个判定逻辑本身就是风险点**：它一旦判错，红线就静默失效。

真实发生过：某次 smoke 脚本的白名单把**数组**拿去做 `-eq` 比较。
PowerShell 里 `-eq` 用在数组上不是"相等"而是**过滤**——
`@('pip','list') -eq 'pip'` 返回 `'pip'`（真值），`... -eq 'nope'` 返回空数组（假值）。
于是判定静默通过，`uv pip install --requirements requirements.txt` **被真的执行了一次**
（万幸该命令在"读文件"阶段就以 exit=2 失败，未联网、未写入）。

因此，冒烟脚本必须满足这三条：

1. **判定逻辑不要用 `-eq` 比数组**。整条命令的 token 序列两边都用分隔符连接后比字符串，
   例如 `($argv[0..($n-1)] -join "`0") -eq ($prefix -join "`0")`。
2. **加一段"绝不允许"的自检**：列出所有红线命令，若其中任何一条被判为"可跑"，
   脚本立刻 `throw` 中止——而不是"跑着看"。
3. **判不准的一律算"未跑"**：宁可少测几条，也不要赌。

**冒烟不通过的常见原因**：字段顺序与文档要求不符；把只对其它命令有效的开关挂错了；默认值选错（如把 `-aos` 写成 `-aoa` 导致误覆盖）。

**顺带一条**：如果工具会改动用户环境（装包、下文件、改配置），冒烟时**只跑只读命令**，
写操作的动作"清单照写、字段照定义，但不实跑"，并在 NOTES.md 里逐条写明未跑原因。

### 5.2 两种"标准做法用不了"的情况（必须知道怎么退）

**① 这个工具没有版本命令** —— Windows 自带命令全是这样（`ping --version` 不存在，而无参数运行又有副作用）：

1. 先找有没有专门的版本开关（`self version`、`--version`）；
2. 没有就读**可执行文件的版本信息**：`(Get-Item $exe).VersionInfo.FileVersion`
   （实测 `ping.exe` → `10.0.26100.8115`）——这也是"实测得到的环境事实"，符合 R7；
3. 这种情况下 `versionPattern` 没法用 `versionArgs` 验证，就把**结论与取得方式写进 NOTES.md**，
   **不要**为了凑格式编一个不存在的版本命令。

**② 内置帮助拿不到** —— `sfc /?` 只打印一句权限提示、一个参数都不列；`cleanmgr /?` 会弹 GUI 并永久等待：

1. 抓帮助的脚本**必须带超时**：超时后先抓 `MainWindowTitle` 当证据再 `Kill(true)`
   （不设超时会让抓取卡住几分钟甚至更久，实测踩过 300 秒）；
2. 以官方文档作为字段依据，并在 NOTES 里**如实写明"没能用内置帮助交叉验证"**——
   这是诚实记录，不是缺陷。

**"只读但带参数"的命令怎么办**（`robocopy <src> <dst> /L` 这种没法逐字列举的）：
不要因此放弃检查，改用**结构判定**，并且**自检与执行用同一套判定**（否则就是"自检通过、执行时换另一套逻辑"的假安全）：

```powershell
# 例子：只允许 robocopy 的"预览"用法
function Test-ReadOnly([string[]]$argv) {
    $argv.Count -eq 4 -and $argv[3] -eq '/L' `
        -and (Test-Path $argv[1]) -and (Test-Path $argv[2])
}
```

---

## 阶段 6 — 写 NOTES.md

用 `plugins/7zip/NOTES.md` 作模板，必须包含：

1. **参数知识来源**（文档绝对路径 + 抓取日期 + 适用版本）
2. **覆盖范围表**（动作 / 命令 / 字段数 / 危险级别）
3. **故意没做的部分与原因**（这是给后来者的地图，不是省略）
4. **这个工具的坑**（至少写通配符/编码/默认行为这类会咬人的地方）
5. **真机冒烟测试结果**（含退出码，以及冒烟中发现的问题）

**批量做同一个家族的包时（例如十几个 Windows 自带命令）**：把公共事实抽成一份共享文档
（`docs/ai/windows-commands.md` 就是这么做出来的），每个包的 NOTES 顶部指向它、
只写自己特有的部分——否则十几个包会互相抄一大段重复内容，改一处要改十几处。

---

## 阶段 7 — 收尾

```powershell
# 1. 更新 本机专属库的 host-dev/project-state.json 的 plugins 段（加一条记录）
# 2. 校验必须全绿
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
# 3. 提交（**仅当你被授权提交时**；上级若说"别提交"，就跳过这一步并把改动留在工作区）
git add -A
git commit -m "feat(<id>): 新增 <软件名> 工具包（N 个命令 / M 个字段）" -m "<校验输出或冒烟结果摘要>"
git push
```

**宿主与 schema 不一致时怎么办**（Windows 命令集撞到过：宿主校验层拒绝 `command: ""`，
而执行层与 schema 都允许）：

> 先按** schema 与语义写正确**。如果宿主拒绝，**不要为了过测而编造参数**——
> 反过来把"宿主的校验点、复现命令、一行修法"写进 NOTES 与交接报告，让宿主那边去修。
> 本次就是这么发现并修掉宿主一个真 bug 的。

---

## 附 A：完整路径速览（照这个顺序做就不会漏）

```
0. 值得做吗 + 文档在哪            → 三类权威来源
1. 落盘快照                       → docs/reference/，并补 README 的复现步骤
2. 提取事实 F1..F5                → 命令清单 / 逐命令可用开关 / 全局语法 / 退出码 / 版本输出格式
   （结构规整则生成矩阵 JSON）
3. 写 manifest                    → 从模板复制，字段风格查决策表，每字段问三个问题
4. 校验循环直到全绿               → 报错查速查表；不要放宽 schema
5. 真机冒烟                       → examples 全跑 + versionPattern 实测 + 行为差异核对
6. 写 NOTES.md                    → 来源 / 覆盖 / 没做的 / 坑 / 实测结果
7. 更新 state + 提交推送           → project-state.json 的 plugins 段
```

## 附 B：工具包作者的自我检查清单

提交前逐条确认：

- [ ] `id` == 目录名
- [ ] 每个动作都有 `sources`，每条 source 都有 `retrieved` 日期
- [ ] 字段出处覆盖率报告的百分比已看过；没有 `doc` 的字段都是明显的直传位置参数
- [ ] `versionPattern` 用清单里的正则实测匹配成功，并把捕获组值抄进 NOTES.md
- [ ] `minVersion` 是真实存在且当前满足的版本
- [ ] `exitCodes` 覆盖了文档里列出的所有码
- [ ] 破坏性动作标了 `danger` 并写了 `confirmText`
- [ ] 会写文件到当前目录的动作覆盖了 `workingDirectory`
- [ ] `examples` 里的每条命令都真跑过且记录退出码
- [ ] NOTES.md 写清了"故意没做什么"和"这个工具的坑"
- [ ] `validate-plugins.py` 全绿
- [ ] `project-state.json` 里的 `plugins` 段已更新
- [ ] 无子命令的工具：动作写了 `command: ""`（**这是有意为之**，不是漏写——宿主校验层允许空串）
- [ ] 需要管理员权限的动作标了 `requiresAdmin: true` 并写清了依据（官方文字 / 实测）

---

## 附 C：做 Windows 自带命令集时的特殊性（12 个包的实测总结）

| 现象 | 怎么做 |
|---|---|
| **开关写成 `/xxx`**（官方文档用斜杠，程序也接受 `-xxx`） | 按官方文档写 `/xxx`；校验器的开关溯源已同时认 `-` 与 `/` |
| **退出码几乎都不是"0 = 成功"** | 逐个实测并写进 `exitCodes`。`robocopy` 最要命：**0–7 全是"没有失败"**（1 = 全部复制成功），≥8 才算失败；`ping` 是 0 = 通了、1 = 全丢；各命令 `/?` 的退出码还互不相同（chkdsk 3、robocopy 16、ipconfig 1…） |
| **帮助写到 stderr** | `nslookup` / `netstat` 的帮助在 stderr（stdout 0 字节）。抓快照时必须两个流都收，否则快照几乎是空的（实测 nslookup 快照只有 481 字节） |
| **输出编码不统一** | 多数是 `oem`(936)，但 `sfc` 是 **UTF-16LE**。判 UTF-16LE 不能用"ASCII 后跟 00"（汉字第二字节不为 0 会误判），要用"字节长度偶数 + 高字节为 0 的比例 > 30%"；也不要指望 `UTF8Encoding(throwOnInvalidBytes)` 能识别 GBK（它对 `d3 c3` 静默替换而不是抛错） |
| **有的会弹 GUI** | `cleanmgr`（`/?` 就弹）、`diskpart` 是交互式 → 不做，或只做"打开它"；抓帮助要带超时 |
| **很多动作需要管理员** | 标 `requiresAdmin: true`（动作级）。当前账号不是管理员时，实测 `chkdsk` 全部、`sfc` 全部、`netstat -b`、`powercfg /waketimers` 都会失败——这些不要实跑 |
| **没有版本命令** | 读 exe 的 `VersionInfo.FileVersion`（见 §5.2） |
| **官方文档 URL 不能猜规律** | `powercfg` 在 `.../windows-server/administration/windows-commands/powercfg` 是 404，真实位置在 `.../windows-hardware/design/device-experiences/powercfg-command-line-options`。**每个 URL 都要实际请求确认** |
| **cmd 内建命令不是 exe** | `dir` / `echo` / `copy` / `del` / `cls` / `type` 必须走 `locate.executable: cmd.exe` + `command: /c` + `commandArgs: [原命令]`（不要在清单里指望 `useShell`——宿主尚未实现它） |

## 8. 把工具包开发委派给子智能体（并行提效）

一个工具包是**完全独立**的工作（一个目录、一份清单、一份 NOTES），适合并行委派。
实测：两个智能体同时做 tar 与 schtasks，各自校验 + 只读冒烟，最后由负责人复核、分提交入库。

**派活时必须写清的边界**（少写一条就会踩坑）：

1. **只准碰自己那个目录**（`plugins/<id>/**`），不许改 `scripts/**`、`docs/**`、其它包。
2. **不要提交、不要推送**：由负责人复核后统一入库（避免未经复核的内容进公开仓库）。
3. **必须自己把校验跑绿**，并在答复里贴出校验输出（动作数 / 字段数 / 出处覆盖率）。
4. **冒烟的红线要写得比"不要改动系统"更具体**。实测教训：一句"不要创建/删除/修改任何真实计划任务"
   被理解成"不要动**用户既有的**任务"，于是子智能体为了量化权限矩阵**自建了 3 个探针任务**
   （带自己的前缀、收尾前删除、并主动申报）。
   正确写法是给条件而不是给禁令：
   > 优先只用只读手段测量。**如果**某些结论必须靠"创建一个临时对象"才能得到，
   > 只允许创建**名字带 `__<项目>Probe` 前缀**的临时对象，且必须在收尾前删除，
   > 并在答复里列出：建了什么、什么时候删的、删干净的复核命令与结果。
5. **要求如实列出"没冒烟的动作 + 原因"和"与官方文档对不上的地方"**——这两项比"全绿"更有价值。
6. **负责人的复核不能省**：子智能体的自述要独立验证（校验重跑、危险动作的残留检查、
   抽查一条它宣称的实测结果）。这次复核的三件事：校验 20/20、计划任务零残留、亲自跑一条 `/Query`。

## 9. 字段风格：先确认程序认不认「两个 token」的写法

`style: separate` 展开成**两个 token**（`/q` + 值），`style: attached` 展开成**一个 token**（`/q:值`）。
**有些程序只认其中一种**，写错的表现是「命令拼出来能看懂、一跑就报参数错」：

| 程序 | 实测结论（2026-09-29） |
|---|---|
| `wevtutil` | **只认单 token**：`/q:值` 可以；`/q 值` 一律 exit 87（Too many arguments are specified）。所以它的每个字段用 `style: attached` + `prefix: "/q:"`（冒号写进 prefix） |
| `curl` | 两 token 合法：`curl --head --max-time 20 --user-agent AllTool/1.0 …` 实测 exit 0 |
| `schtasks` | 两 token 合法：`schtasks /Query /TN <名> /FO LIST` 实测 exit 0 |
| `diskpart` | 用 `/开关`（无值）与字段代入会话命令模板，不涉及这个问题 |

**做法**：定字段风格之前，**用同一条真实命令把两种写法各试一次**，把结果写进 NOTES，
不要按「看起来像哪一类」选。这类错误**校验器查不出来**（开关名是对的、出处也对），只有真机能发现——
所以它属于「必须真机冒烟」的理由之一。

## 10. 坑清单（跨 23 个工具包归纳，动手前先扫一遍）

> 这一节是**踩过的坑**的汇总，全部有实测出处。校验器只能拦住其中一部分
> （清单结构能拦，argv 风格/顺序/编码/退出码**拦不住**，只有真机能发现）。

### 10.1 清单结构（校验器会拦，但报错可能很吓人）

1. **YAML 普通标量里出现 `: `（冒号+空格）会让校验器直接抛 Python traceback 崩掉**，不是可读的校验错误。
   例：`description: 官方原文：/q "..."`。→ 含冒号空格的值必须加引号。
2. **单引号标量里再出现单引号 → 同样崩**，而且更难发现。→ 含引号的长文本用 `>-` 折叠块标量最省事。
3. 顶层必需键：`spec: 1`、`id`、`name`、`manifestVersion: 0.1.0`、`locate`、`actions`
   （注意是 `manifestVersion` 且必须是 `x.y.z` 形式，不是 `version`）。
4. 动作级**没有** `args`；多词子命令用 `command` + `commandArgs: [...]`。`command: ""` 合法（没有子命令时）。
5. `type: path` **不存在** → 用 `file` / `files` / `directory` / `directories` / `paths`。
6. `style: attached` **强制要求** `prefix`（冒号可以写进 prefix）；没有前缀的字段用 `style: positional`。
7. enum 的 `values` 必须是**对象数组** `{ value, label }`，默认项写 `isDefault: true`（不是 `default:`）。
8. **`switchBase` 只用于 CI 白名单校验，不产生 argv**。要生成 `/deny` 这种固定 token，
   用 `style: literal` 的字段（每个取值带 `args`）。把它当成"生成开关的字段"是常见误解。

### 10.2 字段与 argv（校验器查不出来，只有真机能发现）

9. **`separate` 展开成两个 token、`attached` 展开成一个 token，不同程序认的不一样**：

   | 程序 | 实测结论（2026-09） |
   |---|---|
   | `wevtutil` | **只认单 token**：`/q:值` ✔；`/q 值` → exit 87 |
   | `certutil` | **不存在单 token**：`-hashfile:<file>` → 「未知参数」；用两 token |
   | `icacls` | 开关必须是**独立 token**：`<路径>:/T` → exit 123 |
   | `curl` / `schtasks` | 两 token 合法（实测 exit 0） |

   → 定风格前**用同一条真实命令把两种写法各试一次**，把结果写进 NOTES。

10. **开关与位置参数的顺序也因程序而异**，而**字段声明顺序就是 argv 顺序**：
    - `certutil`：flag 必须在前（`-encode -f <in> <out>` ✔；`-encode <in> -f <out>` ✗）
    - `icacls`：**路径必须永远在最前**（`icacls /T <路径>` → 87「First parameter must be a file name pattern」）
    - `wevtutil` / `schtasks`：开关在路径/任务名之前

11. **小心「不报错但行为不同」的写法**：`certutil -store My -user` 不报参数错，
    而是把 `-user` 当成 CertId 去匹配、**静默给出另一个结果**。这类问题只有把两种写法都跑一遍才会发现。
12. **枚举的默认值等于替用户做决定**：winget 的 `--scope` 默认 `user` 会把**整机范围安装的包全部过滤掉**，
    用户只会觉得「少了很多软件」。拿不准就默认「不指定」（空值 + `isDefault: true`）。

### 10.3 输出与编码（每个包都要实测，包之间不能互抄）

13. **编码必须逐包实测**：同一台机器上 `tar`=oem、`schtasks`=utf-8、`wevtutil`=oem、`certutil`=oem、`icacls`=oem。
14. **判编码必须用带中文（非 ASCII）的输出**：`wevtutil el` / `icacls /?` 全是纯 ASCII，拿它们判会得出错误结论。
    正确做法是造一个中文样本（中文路径名、中文任务名），再看**原始字节**：
    严格 UTF-8 解码抛异常 = 不是 UTF-8；`0x00` 间隔 = 可能是 UTF-16LE。
15. **退出码不是「0 = 成功」**：`ipconfig /?` = 1；`icacls /findsid` 零命中 = 1332（与账号名写错同码）；
    `icacls /restore` 部分失败仍返回 0；`schtasks /End` 对没在跑的任务也报 SUCCESS。
16. **失败常常写 stderr、汇总写 stdout**；帮助可能写 stdout 也可能写 stderr（`nslookup /?` 是 stderr，
    `tar --help` 是 stdout）。抓快照时要两个流都留。
17. **本地化**：中文系统上程序输出中文（diskpart 全是中文，连版本横幅都是）。
    **绝不要用「成功提示的措辞」判断成败**——要么用退出码，要么声明多语言正则，
    要么像会话型那样「只判有没有命中错误」。
18. `(?m)^...$` 在多行输出的 CRLF 上会因 `\r` 失配 → 写 `\r?$`
    （wevtutil 的输出就是 CRLF，踩过）。

### 10.4 会话型（有状态的）程序

19. **状态值从字段值取**（`session.state[].captureField`），不要从输出里正则捕获——
    后者在中文系统上必然失效（中文提示匹配不上英文正则）。
20. **只有声明了 `establishes` 的动作才写状态，且只写它声明的那个**。
    否则 `list` / `detail` 这类动作也会去写「第一个状态」，把「已选中磁盘 0」覆盖成 `yes`（实测过的 bug）。
21. 失败判定写**多条语言**的 `errorPatterns`；命中任一条就不确立状态。
22. 不可逆毁数据的动作（清盘 / 删分区 / 格式化）用 `execution: info`：**宿主不执行**，只给解释与命令。

### 10.5 工具与流程

23. **改了清单必须跑校验 + 真机冒烟**，一条都不能省：校验器查不出 argv 风格、顺序、编码、退出码。
24. **子智能体的产出必须等它完全结束再读**。不要在它还在写文件时 `git add -A`——
    会把半成品提交进去（表现为应用报「YAML 解析失败」）。正确顺序：确认它 inactive → 校验 → 复核 → 提交。
25. **脚本里的 `str.replace` 锚点不匹配时不报错**（Python/PowerShell 都是）。改完必须用编译或 grep 复核，
    别信脚本自己打印的「已完成」。同理：**按「起点+终点」切代码块很危险**，块内可能有别的东西
    （我删弹窗代码时连带删掉了自动化逻辑，编译才发现）。
26. 在 pwsh 里读中文输出前先设 `$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8`，
    否则中文标签会乱码——**零残留自检因此误判过**（把 21 个证书数成 0 个）。
27. 写中文文档时**别在字符串里混用 ASCII 引号**（写脚本时踩过多次，表现为语法错误或 shell 解析错）。

## 11. 新增一个工具包的检查单

- [ ] 官方文档 + 本机 `--help` / `/?` 都读了；**两边对不上的只写都能对应的**，差异记进 NOTES
- [ ] 每个动作有 `sources`（title / url / retrieved / note），每个字段有 `doc`
- [ ] 字段风格（`separate` / `attached` / `positional`）与**字段顺序**都各试两种写法再定
- [ ] `runtime.encoding` 用**带中文的输出**实测，并写清判定依据
- [ ] 退出码语义逐条实测（含失败路径），写进 `exitCodes`
- [ ] 是否需要管理员：**实测确认**，不要照文档抄（官方常常一个字都不写）
- [ ] 只读动作全部真跑并记录退出码；危险动作只在自建临时对象上跑，或干脆不跑并说明
- [ ] 风险分级按 §2.9；`confirmPhrase` 里带**真实目标**（例如「清空磁盘 0」）
- [ ] `uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py` 全绿
- [ ] NOTES 写全：验了什么 / 没验什么 / **为什么没验** / 与官方文档对不上的地方
- [ ] 临时对象申报：建了什么、何时删的、**怎么复核**

---

---

## 相关文档

| 文档 | 讲什么 |
|---|---|
| ``AGENTS.md`` | 硬规则 R1–R7、接手顺序、命令速查、文档地图（**入口**） |
| [`docs/spec/manifest-v1.md`](../spec/manifest-v1.md) + ``schema.json`` | 清单规范（字段、风格、会话型、三级风险、kind） |
| [`docs/ai/playbook-tool-package.md`](playbook-tool-package.md) | 做工具包的逐步流程 + **§10 坑清单 + §11 检查单** |
| [`docs/ai/windows-commands.md`](windows-commands.md) | Windows 自带命令的公共实测结论 |
| [`docs/reference/README.md`](../reference/README.md) | 我们提取的事实性数据放哪、第三方原文去哪 |
| `plugins/<id>/NOTES.md` | 该包自己的实测记录（验了什么 / 没验什么） |
