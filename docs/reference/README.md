# 参考文档快照

这个目录存放**参数知识的原始依据**。规范要求 `manifest.yaml` 里出现的每个开关都能在这里找到出处。

## 为什么要落盘

1. **可溯源**：工具包里的字段能对回具体文档位置。
2. **可复审**：文档更新后能 diff 出"哪些参数可能变了"。
3. **离线**：本机开了 fake-ip DNS，一部分工具抓不到网页，落盘后就不依赖网络。
4. **可追溯版本**：`_meta.json` 记下抓取时的软件版本与日期。

## 目录结构

```
docs/reference/
  scoop-help/                 本机 scoop 的帮助输出（版本匹配的权威来源）
    _meta.json                抓取时的版本、可执行文件路径、日期
    index.json                28 个命令及其一句话说明
    <command>.txt             每个命令的详细帮助
  scoop-wiki/                 scoop 官方 wiki 全站（36 个页面，克隆自 Scoop.wiki.git）
  uv-help/                    本机 uv 0.11.15 的帮助输出（49 个命令）
    _meta.json                抓取时的版本、可执行文件路径、日期、抓取失败的命令
    index.json                49 个命令 / 子命令及其一句话说明
    <command>.txt             每个命令的简洁帮助（`uv <cmd> --help` 的输出）
    pip-install.txt 等        二级命令把空格换成 '-'（`uv pip install --help`）
    _uv.txt                   `uv --help` 的输出（含全局开关）
  uv-docs/                    uv 官方 CLI 参考（https://docs.astral.sh/uv/reference/cli/）
    cli-reference.html        原始 HTML（1 165 371 字节，mintlify/mdBook 风格的整页）
    cli-reference.md          pandoc 转出的 Markdown（832 826 字节，含整页侧栏导航）
  7zip-md/                    7-zip.chm 反编译+转换后的 Markdown
    syntax.md                 命令行语法总览
    commands/*.md             11 个命令
    switches/*.md             37 个开关
    exit_codes.md             退出码
  7zip-switch-matrix.json     从上述文档提取的"命令 × 可用开关"矩阵（供 CI 校验）
  7zip-chm/                   反编译的原始 HTML（已被 .gitignore 忽略，可重新生成）
```

## 如何重新生成

### scoop

```powershell
pwsh -File scripts/fetch-scoop-help.ps1
```

之所以不用 wiki 上的命令列表：wiki 的 `Commands` 页面本身就是 `scoop help` 的转抄，
而本机帮助与已安装版本严格对应，且 `scoop help` 返回的是**结构化对象**（`Command` / `Summary`），比解析文本可靠。

### scoop wiki

wiki 本身是个 git 仓库，能整站克隆下来：

```powershell
git clone --depth 1 https://github.com/ScoopInstaller/Scoop.wiki.git docs\reference\scoop-wiki
```

注意：克隆下来后要**删掉其中的 `.git` 目录**再提交，
否则 git 会把它当成内嵌仓库（gitlink）而不是普通文件，克隆本仓库的人拿不到内容。
刷新方式就是删掉整个目录重新克隆一次。

### 7-Zip

```powershell
# 1. 从 scoop 安装目录反编译 CHM（hh.exe 是 Windows 自带的）
$chm = 'C:\Users\Steve\scoop\apps\7zip\26.03\7-zip.chm'
hh.exe -decompile docs\reference\7zip-chm $chm

# 2. 转成 Markdown（需要 pandoc）
Get-ChildItem docs\reference\7zip-chm\cmdline -Recurse -Filter *.htm | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path docs\reference\7zip-chm\cmdline).Path.Length).TrimStart('\')
    $dst = Join-Path 'docs\reference\7zip-md' ($rel -replace '\.htm$','.md')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    pandoc -f html -t gfm --wrap=none $_.FullName -o $dst
}

# 3. 提取命令 × 开关矩阵
pwsh -File scripts/extract-7zip-matrix.ps1
```

### uv

两类来源都要抓：官方 CLI 参考（长文档，字段的 `doc` 指它的锚点）+ 本机二进制自带帮助
（与安装版本严格对应，含每个开关的默认值）。

```powershell
# 1. 官方 CLI 参考。**必须用 Invoke-WebRequest**：本机 web_fetch 因 fake-ip DNS 不可用（P2）
$out = 'docs/reference/uv-docs'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Invoke-WebRequest 'https://docs.astral.sh/uv/reference/cli/' -UseBasicParsing -OutFile "$out\cli-reference.html"
# 实测：HTTP 200，1 165 368 字节，689 ms

# 2. 转 Markdown（需要 pandoc）
pandoc -f html -t gfm --wrap=none "$out\cli-reference.html" -o "$out\cli-reference.md"

# 3. 本机帮助快照（49 个命令）
pwsh -File scripts/fetch-uv-help.ps1
```

**注意 `uv help <cmd>` 与 `uv <cmd> --help` 内容不同**，不是同一条帮助：
前者是 docs 站上的长文档（`uv help sync` 首行 `Update the project's environment.`，带句号），
后者是二进制内置的简洁帮助（`uv sync --help` 首行 `Update the project's environment`）。
`fetch-uv-help.ps1` 抓的是**后者**，工具包的参数依据也以后者为准
（离线、与安装版本严格对应）。理由与差异见 `plugins/uv/NOTES.md` §1.2。

**另一个坑**：`cli-reference.md` 是整页转换的产物，头部上百行都是站点的侧栏导航。
真正的命令节从 `## <a href="#uv-...">` 这种标题开始（例如 `uv sync` 在第 3393 行）。
用锚点定位，别整篇读。

**第三个坑**：`uv --help` 与 `uv help` 列出的命令表**不完全一致**——
0.11.15 上前者是 21 个、后者是 22 个（多了 `generate-shell-completion`）。
判断"有哪些命令"时两个都看一下。

### 为什么需要矩阵

官方帮助里，每个命令页都有一节 "Switches that can be used with this command"，
这是官方的**逐命令开关白名单**。把它变成 JSON 后，CI 就能自动发现
"把 `-o` 挂到 `a` 命令上"这类错误——这种错误光靠人读文档很难发现，但会让用户看到莫名其妙的失败。

矩阵同时记录 `switches`（白名单一节）和 `mentioned`（该页面任何位置提到过的开关）。
合并两者是必要的：官方白名单只列"有独立帮助页"的开关，像 `-ba`（禁用表头）没有独立页面，
只在 `hash` 命令的示例里出现，只能从 `mentioned` 里得到。

## 关于 copyright

这些快照来自第三方软件的官方文档，仅用于本机个人开发与查阅，不对外分发。
如果将来这个仓库要公开，`docs/reference/` 整个目录应当先移除（改用文档链接）。
