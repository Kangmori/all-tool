# 工具包开发手册（Playbook）

> 给 AI 智能体。目标：**从零产出一个通过校验、且真机冒烟过的工具包**。
> 前置：已读 `AGENTS.md`，已跑过 `scripts/check-env.ps1`，基线校验是绿的。

---

## 阶段 0 — 判断该不该做，以及文档在哪

### 0.1 先确认这个软件值得做成工具包

| 判据 | 说明 |
|---|---|
| 已安装且能在 PATH 里找到 | `Get-Command <name>` |
| 命令面有限且语义清晰 | 命令数 ≲ 30、开关有官方文档。反例：`ffmpeg` 有几百个选项，要挑着做 |
| 非纯交互式 | 全屏 TUI（如 `vim`）、必须回答 y/n 的向导，v1 不做 |
| 官方文档存在且可定位 | 没有权威文档就不能做（R1） |

### 0.2 找到权威文档 —— 按优先级，只有这三类算数

| 优先级 | 类型 | 如何拿到 | 例子 |
|---|---|---|---|
| 1 | 随软件分发的帮助文件（CHM / man / 本地 HTML） | 在安装目录里找 `*.chm`、`man\`、`docs\` | `C:\Users\Steve\scoop\apps\7zip\26.03\7-zip.chm` |
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

**冒烟不通过的常见原因**：字段顺序与文档要求不符；把只对其它命令有效的开关挂错了；默认值选错（如把 `-aos` 写成 `-aoa` 导致误覆盖）。

---

## 阶段 6 — 写 NOTES.md

用 `plugins/7zip/NOTES.md` 作模板，必须包含：

1. **参数知识来源**（文档绝对路径 + 抓取日期 + 适用版本）
2. **覆盖范围表**（动作 / 命令 / 字段数 / 危险级别）
3. **故意没做的部分与原因**（这是给后来者的地图，不是省略）
4. **这个工具的坑**（至少写通配符/编码/默认行为这类会咬人的地方）
5. **真机冒烟测试结果**（含退出码，以及冒烟中发现的问题）

---

## 阶段 7 — 收尾

```powershell
# 1. 更新 docs/ai/project-state.json 的 plugins 段（加一条记录）
# 2. 校验必须全绿
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
# 3. 提交
git add -A
git commit -m "feat(<id>): 新增 <软件名> 工具包（N 个命令 / M 个字段）" -m "<校验输出或冒烟结果摘要>"
git push
```

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
