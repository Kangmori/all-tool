# uv 工具包说明（plugins/uv）

> 本文件是给后来者的地图：**覆盖了什么、故意没覆盖什么、哪些坑会咬人、以及全部实测结论**。
> 工具包本身在 [`manifest.yaml`](manifest.yaml)。规范见 `docs/spec/manifest-v1.md`。
>
> 实测环境：Windows 11 26200 / x64，用户非管理员，uv **0.11.15**（winget 装的 `uv.exe`），
> 全部实测日期 **2026-09-27**。命令一律用 `pwsh`，抓中文输出前设
> `$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8`。

---

## 1. 参数知识来源

两类来源，优先级与用法：

| 优先级 | 来源 | 落盘位置 | 说明 |
|---|---|---|---|
| 1 | **官方 CLI 参考文档** <https://docs.astral.sh/uv/reference/cli/> | `docs/reference/uv-docs/cli-reference.html`（原始 HTML，1 165 371 字节）+ `cli-reference.md`（pandoc 3.11 转出，832 826 字节） | 每个命令都有独立锚点（`#uv-sync`、`#uv-pip-install` …）。清单里每个字段的 `doc` 都指向该锚点。 |
| 2 | **本机 uv 0.11.15 的内置帮助** | `docs/reference/uv-help/`（49 个命令的 `.txt` + `index.json` + `_meta.json`） | 由 [`scripts/fetch-uv-help.ps1`](../../scripts/fetch-uv-help.ps1) 抓取。与已安装版本严格对应，且比官方长文档紧凑（每个开关都带默认值与取值说明）。 |

抓取方式（可复现）：

```powershell
# 1) 官方文档（web_fetch 在本机不可用，必须用 Invoke-WebRequest，见 development.md §4.4 / P2）
$url = 'https://docs.astral.sh/uv/reference/cli/'
Invoke-WebRequest $url -UseBasicParsing -OutFile docs\reference\uv-docs\cli-reference.html
pandoc -f html -t gfm --wrap=none docs\reference\uv-docs\cli-reference.html -o docs\reference\uv-docs\cli-reference.md

# 2) 本机帮助快照
pwsh -File scripts/fetch-uv-help.ps1
```

抓取结果：官方文档 HTTP 200，689 ms，1 165 368 字节，一次成功（与 7-Zip 的 `dev.7-zip.org` TLS 失败不同，astral.sh 出网正常）。

### 1.1 关于官方文档快照的一个提醒（**重要，别被它误导**）

`cli-reference.md` 是**整页**转换的产物，包含 mdBook 的整个侧栏导航，头部上百行都是导航链接。
真正的正文从 `## <a href="#uv-...">…` 这种标题开始（例如 `uv sync` 在第 3393 行）。
**不要**把它当成"一份干净的逐命令文档"来读；用锚点定位（`#uv-sync` = `## <a href="#uv-sync" ...>uv sync</a>`）。

已实测确认清单里用到的 **31 个锚点全部存在**（`uv`、`uv-run`、`uv-sync`、`uv-add`、`uv-remove`、
`uv-lock`、`uv-export`、`uv-tree`、`uv-venv`、`uv-pip`、`uv-pip-install`、`uv-pip-uninstall`、
`uv-pip-list`、`uv-pip-freeze`、`uv-python`、`uv-python-install`、`uv-python-list`、`uv-python-pin`、
`uv-tool`、`uv-tool-install`、`uv-tool-run`、`uv-tool-list`、`uv-cache`、`uv-cache-clean`、
`uv-cache-dir`、`uv-init`、`uv-build`、`uv-version`、`uv-self`、`uv-self-version`、`uv-help`）。

### 1.2 为什么用了 `uv <cmd> --help` 而不是 `uv help <cmd>`

两者**内容不同**，不是同一条帮助：

| 调用 | 输出 | 用途 |
|---|---|---|
| `uv <cmd> --help` | 该二进制内置的简洁帮助，每个开关带默认值与 `[possible values: …]` | 本工具包以此为准（离线、与安装版本严格对应） |
| `uv help <cmd>` | docs 站上的长文档（等同官方 CLI 参考的一节） | 需要长解释时看它 |

例：`uv help sync` 首行是 `Update the project's environment.`（带句号，来自长文档），
而 `uv sync --help` 首行是 `Update the project's environment`（无句号）。
`scripts/fetch-uv-help.ps1` 抓的是后者。

---

## 2. 覆盖范围

**26 个动作 / 125 个字段 / 字段出处标注 125 个 = 100%**（校验器输出见 §10）。

| 动作 | 命令 | 字段 | danger | 说明 |
|---|---|---|---|---|
| `run` | `uv run` | 8 | overwrite | 运行命令/脚本；**字段顺序关键**，见 §7.1 |
| `init` | `uv init` | 6 | overwrite | 新建项目骨架 |
| `add` | `uv add` | 7 | overwrite | 添加依赖 |
| `remove` | `uv remove` | 4 | overwrite | 移除依赖 |
| `sync` | `uv sync` | 8 | overwrite | 同步环境 |
| `lock` | `uv lock` | 4 | overwrite | 更新锁文件 |
| `export` | `uv export` | 7 | overwrite | 导出锁文件 |
| `tree` | `uv tree` | 6 | — | 依赖树 |
| `venv` | `uv venv` | 7 | overwrite | 创建虚拟环境 |
| `pip-list` | `uv pip list` | 5 | — | 列出包 |
| `pip-freeze` | `uv pip freeze` | 4 | — | requirements 格式列出 |
| `pip-install` | `uv pip install` | 7 | overwrite | 装包 |
| `pip-uninstall` | `uv pip uninstall` | 5 | destructive | 卸包 |
| `python-list` | `uv python list` | 5 | — | 列出 Python |
| `python-install` | `uv python install` | 5 | overwrite | 下载安装 Python |
| `python-pin` | `uv python pin` | 4 | overwrite | 钉定 Python 版本 |
| `tool-list` | `uv tool list` | 4 | — | 列出全局工具 |
| `tool-install` | `uv tool install` | 5 | overwrite | 装全局工具 |
| `tool-run` | `uv tool run` | 6 | overwrite | 临时运行工具 |
| `cache-dir` | `uv cache dir` | 0 | — | 打印缓存目录 |
| `cache-clean` | `uv cache clean` | 2 | destructive | 清缓存 |
| `build` | `uv build` | 7 | overwrite | 构建 sdist/wheel |
| `version-show` | `uv version` | 2 | — | 读项目版本 |
| `version-set` | `uv version <值>` | 4 | overwrite | 改项目版本 |
| `self-version` | `uv self version` | 2 | — | 读 uv 自身版本 |
| `help` | `uv help` | 1 | — | 查看帮助 |

`danger` 分配：`destructive` 2 个（`pip-uninstall`、`cache-clean`），`overwrite` 13 个，
其余 11 个不动环境。每个 `danger != none` 的动作都写了 `confirmText`。

### 2.1 二级命令怎么表达

`uv` 的命令树是两级的（`uv pip install`、`uv python pin`、`uv tool run`…）。
规范里 `action.command` 是**单个字符串**，所以二级命令用 `command` + `commandArgs` 表达
（与 scoop 工具包同一做法）：

```yaml
command: pip
commandArgs: ["install"]
```

宿主的组装规则 `[exe] + [command] + commandArgs + Σ字段 + fixedArgs`
（`docs/spec/manifest-v1.md` §3.2）正好给出 `uv pip install …`。本清单 11 个动作用了这个形式。

---

## 3. `versionPattern` 实测

`locate.versionArgs: ["--version"]`，
`locate.versionPattern: "(?m)^uvx?\\s+([0-9]+\\.[0-9]+\\.[0-9]+)"`。

实测（`scripts/smoke-uv.ps1` 输出原样抄录）：

```
== versionPattern 实测 ==
  命令        : uv --version
  退出码      : 0
  原始输出    : uv 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)
  正则        : (?m)^uvx?\s+([0-9]+\.[0-9]+\.[0-9]+)
  匹配成功    : True
  捕获组 1    : 0.11.15
```

**结论：捕获组 = `0.11.15`，与 `check-env.ps1` 记录的 `environment.tools.uv` 一致。**

与 P4（scoop/7z 的版本在第二行、首行是空行）不同：**`uv --version` 首行就是版本行，非空**。
即便如此仍然显式写了 `(?m)`，因为规范 §3 规定宿主按多行语义匹配，显式写出来在换宿主实现时也不会失效
（`docs/spec/manifest-v1.md` §3 的原文建议）。

### 3.1 为什么正则写成 `uvx?` 而不是 `uv`（**实测抓出来的真缺陷**）

`locate.alternativeNames` 里列了 `uv` 与 `uvx`。宿主 `ToolLocator.FindExecutable`
会**先试 `executable`，再按顺序试 `alternativeNames`**（`src/AllTool.Core/Discovery/ToolLocator.cs` 第 40–67 行），
所以 `uv.exe` 不在搜索目录里时会回退到 `uvx.exe`。

winget 的安装目录里**两个 exe 都有**：

```
uv.exe   C:\Users\Steve\AppData\Local\Microsoft\WinGet\Packages\astral-sh.uv_...\uv.exe
uvx.exe  C:\Users\Steve\AppData\Local\Microsoft\WinGet\Packages\astral-sh.uv_...\uvx.exe
```

而 **`uvx --version` 打印的是 `uvx 0.11.15 (…)`，不是 `uv 0.11.15 (…)`**。实测对比：

```
$ uv  --version   → uv 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)   exit=0
$ uvx --version   → uvx 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)  exit=0
```

**旧正则 `(?m)^uv\s+([0-9.]+)` 匹配 `uvx` 的输出 → False**（实测），
也就是"回退到 uvx 时取不到版本号"。改成 `(?m)^uvx?\s+…` 后两者都匹配、捕获组都是 `0.11.15`（实测）：

```
uv   --version -> 匹配=True  捕获=[0.11.15]
uvx  --version -> 匹配=True  捕获=[0.11.15]
```

`uvx` 是 uv 官方文档明确提供的别名，不是"另一个程序"：官方 CLI 参考
（`docs/reference/uv-docs/cli-reference.md` 第 6732 行）写着
> `uvx` is provided as a convenient alias for `uv tool run`, their behavior is identical.

帮助快照 `docs/reference/uv-help/tool-run.txt` 第 193 行也有
`Use \`uvx\` as a shortcut for \`uv tool run\`.`，
所以把 `uvx` 当作备用可执行名是有出处的。

**顺带学到的通用教训**（值得进 playbook）：
`alternativeNames` 里的可执行文件**可能打印与主程序不同的名字**，
于是同一个 `versionPattern` 不一定对它们都成立。
playbook §5 只说"验证 `versionPattern` 真的能匹配"，
没说要**对 `executable` 与每个 `alternativeNames` 分别验证一遍**。见 §11。

其余同族输出（只读、已实测）：

| 命令 | 退出码 | 首行 |
|---|---|---|
| `uv --version` | 0 | `uv 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)` |
| `uv self version` | 0 | 同上 |
| `uv self version --short` | 0 | `0.11.15` |
| `uv self version --output-format json` | 0 | `{`（含 `"version": "0.11.15"`） |

---

## 4. 退出码：实测结论（**官方没有文档**）

**重要发现：uv 没有退出码文档。** 已实测确认：

- `docs/reference/uv-docs/cli-reference.html` 里搜 `exit code`，**0 处**；
  搜 `id="…exit…"` 的锚点，**0 个**。
- `https://docs.astral.sh/uv/reference/`、`/reference/policies/`、`/reference/environment/`
  三页都能 200，但 `/reference/` 页面里搜 `exit code` 也是 **0 处**。

所以 `exitCodes` 的三条**全部来自真机实测**，不是抄文档：

| 码 | 含义 | 证据 |
|---|---|---|
| 0 | 成功 | 本节所有正常命令 |
| 1 | 运行期错误 | Rust 惯例；未构造出干净样例（本机触发的都是 2），**属于推断，已在清单注释里标明** |
| 2 | 命令行用法错误 / 项目上下文缺失 | `uv pip install --nosuch-flag` → 2；`uv run --nosuch-flag` → 2；`uv nosuchcommand` → 2；`uv help nosuchcmd` → 2；空目录里 `uv tree` → 2；空目录里 `uv version` → 2（`No pyproject.toml found`） |

实测样例（原样）：

```
uv pip install --nosuch-flag   exit=2  error: unexpected argument '--nosuch-flag' found
uv run --nosuch-flag           exit=2  error: unexpected argument '--nosuch-flag' found
uv nosuchcmd                   exit=2  error: unrecognized subcommand 'nosuchcmd'
uv help nosuchcmd              exit=2  error: There is no command `nosuchcmd` for `uv`. Did you mean one of:
uv tree        (空目录)         exit=2  error: No `pyproject.toml` found in current directory or any parent directory
uv version     (空目录)         exit=2  error: No `pyproject.toml` found in current directory or any parent directory
```

**给宿主的提醒**：`uv version` 不是"看 uv 版本"。在空目录里它会以退出码 2 失败，
并专门提示 `hint: If you meant to view uv's version, use uv self version instead`。
清单里因此把两者分成不同动作，并在 `version-show` 的 `resultNote` 里写明了这件事。

---

## 5. 输出编码：实测结论 = **UTF-8**（与 scoop 的 GBK 相反）

### 5.1 结论

清单写 `runtime.encoding: utf-8`（**不是 `auto`**）。
理由：uv 是 Rust 写的，stdout / stderr 都是 UTF-8；而宿主 `EncodingResolver.DefaultForConsoleApps()`
在中文 Windows 上默认按 cp936 解码（`src/AllTool.Core/Execution/ProcessRunner.cs`）。

宿主代码里本来就有这条判断（`ProcessRunner.cs` 第 301–303 行的注释原文）：
> 理由：Windows 上的传统命令行程序在被重定向时，通常仍按控制台输出码页（中文系统上是 936）写字节，
> 现代工具（uv、node 等）多写 UTF-8，这类工具包应显式写 encoding: utf-8。

`EncodingResolver` 已支持 `"utf-8"`（第 290 行），所以本清单的选择有宿主实现支撑。

### 5.2 字节证据（可复现）

**证据 A —— 让 uv 回显一段中文。** 造一个 `pyproject.toml`，把 `name` 写成中文，
uv 解析失败时会把这段中文原样打进 stderr。捕获原始字节（`cmd /c … > 文件`，再 `ReadAllBytes`）：

```
$ pyproject.toml 的内容：  name = "中文项目名"
uv tree  → exit=2，捕获到 314 字节，其中非 ASCII 字节 30 个

原始 hex（截取包含中文的那一段）：
  ... 6e 61 6d 65 20 3d 20 22 e4 b8 ad e6 96 87 e9 a1 b9 e7 9b ae e5 90 8d 22 0a ...

按 UTF-8 解码：
  error: Failed to parse: `pyproject.toml`
    Caused by: TOML parse error at line 2, column 8
    |
  2 | name = "中文项目名"
    |        ^^^^^^^^^^^^^^^^^
  Not a valid package or extra name: "中文项目名". Names must start ...
  → 中文完全正确

按 GBK(cp936) 解码：
  ... name = "涓枃椤圭洰鍚? ...
  → 乱码（正是 GBK 解 UTF-8 字节的典型症状）
```

关键字节：`中文项目名` = `e4 b8 ad e6 96 87 e9 a1 b9 e7 9b ae e5 90 8d`（15 字节）。
这是 UTF-8 的 3 字节序列（每字 3 字节 × 5 字 = 15 字节）；GBK 下每字 2 字节，
这 15 字节会被切成 7.5 个 GBK 字符，于是出现 `涓枃…` 与结尾的 `鍚?`。

**证据 B —— 版本行本身。** `uv --version` 的 57 个字节：

```
75 76 20 30 2e 31 31 2e 31 35 20 28 33 63 66 66 65 39 37 63 32 20 32 30 32 36 2d 30 35 2d 31 38
20 78 38 36 5f 36 34 2d 70 63 2d 77 69 6e 64 6f 77 73 2d 6d 73 76 63 29 0a
```
= `uv 0.11.15 (3cffe97c2 2026-05-18 x86_64-pc-windows-msvc)\n`，**全 ASCII，非 ASCII 字节 0 个**。
这条本身不能区分编码，列在这里是为了说明"版本行不含任何编码证据"，编码结论靠证据 A。

### 5.3 一个容易误判的坑（**务必看清**）

**`uv run python` 打印的中文是 GBK，不是 UTF-8** —— 因为那是 **Python 自己**在写 stdout，
不是 uv 在写。复核校验器输出时也踩到同一个坑：`python scripts/validate-plugins.py` 的中文
按管道出来是 cp936，按 UTF-8 解码成 `[ OK ] plugins\uv\manifest.yaml (26 ���� / 125 �ֶ� …)`。

`runtime.encoding` 管的是**被调用的那个可执行文件自己**的输出。对 `uv run <python>` 来说，
uv 只是转发子进程的字节，真正决定编码的是 Python。这类"uv 转发别人输出"的动作
（`run`、`tool-run`）**不要指望 `runtime.encoding: utf-8` 一定对**，见 §8 的规范缺口 G4。

---

## 6. 为什么不开伪控制台（`usePseudoConsole: false`）

7-Zip 需要 ConPTY 才画进度；uv 不需要：

1. uv 的进度信息本来就走 stderr 的普通文本（如 `Resolved 12 packages in 345ms`），不依赖真终端；
2. 开 ConPTY 会让 uv 认为自己在真终端里，从而**强制彩色并做原地重绘**，输出里会混入 ANSI 序列
   （宿主虽有 `AnsiText` 清理，但重绘会让日志难以阅读）；
3. uv 还提供 `--no-progress` 用来关掉进度（本清单未暴露，见 §8）。

代价：本清单**没有为任何动作写 `output.progress.pattern`**。uv 的进度不是百分比形式
（是"正在下载/正在解析"这类行），我没有实跑过任何会产出进度的写操作（安全红线），
**不能凭空编一个正则**（R1）。这一条如实记为"没做"，见 §9。

---

## 7. 参数映射上的取舍（照着 playbook §3.2 的决策表做的，逐条说明）

### 7.1 `uv run` 的字段顺序是**功能正确性**问题，不是风格问题（本次最大的坑）

uv 把**第一个非开关参数**当作"要运行的程序"，其后的参数**原样转交给它**。所以：

```yaml
# 正确：uv 自己的开关在 command 之前
- id: with      # --with
- id: python    # --python
- id: noProject # --no-project
- id: command   # ← 第一个非开关参数，uv 从这里开始不再解析自己的选项
- id: commandArgs
```

如果按"位置参数放最前"的常规思路把 `command` 声明在第一位，生成的命令会变成
`uv run pytest --with requests`，而 `--with requests` 会被**当成 pytest 的参数**传下去，
uv 那边反而收不到。实测确认了边界：

```
uv run --no-project -- python -c print(1)              exit=0  输出 1
uv run --no-project python -c print(2)                 exit=0  输出 2
uv run --no-project --python 3.14 python --version     exit=0  输出 Python 3.14.7
uv run --with x --help                                 exit=0  输出的是"uv run 自己的帮助"（--help 没被转交）
```
（最后一条说明 `--help` 是 uv 自己消费的；普通参数则会被转交。）

**这是 playbook §3.1 只说"`archive` 这类必须是第一个位置参数的字段要声明在最前"的盲点**：
它只覆盖了"位置参数必须在前"的情形，没有覆盖**反过来的情形——开关必须在位置参数之前**。
而 `docs/spec/manifest-v1.md` §9 的第 3 条（D3）恰好把"字段顺序即 argv 顺序是否足够"列为**待拍板**，
uv 就是那个"有些工具要求开关排在位置参数之前"的真实例子。见 §8 G1。

### 7.2 具体的风格选择

| 文档里的写法 | 用的 style | 例子 |
|---|---|---|
| `--with <WITH>`、`--python <PYTHON>`、`--format <FORMAT>` | `separate` | `["--with", "requests"]` |
| `--all-extras`、`--no-dev`、`--force`（无参数开关） | `flag` | `["--no-dev"]` |
| `[PACKAGES]...`、`[TARGETS]...`、`[PACKAGE]...` | `positional` + `repeatable: true` + `type: text` | 每行一个 |
| `[COMMAND]` 之后的透传参数 | `positional` + `positionalMode: perLine` + `textarea` | 每行一个 token |
| `--output-format` 的 `text/json`、`--format` 的三种取值 | `enum` + `values` 且把默认项标 `isDefault: true` | 见 `python-list` |
| 单行 flow 写法的 examples | `- { title: …, args: [...] }` | 与 7zip/scoop 一致 |

**空值规则用满**：`--vcs` 的默认项 `value: ""`、`--bump` 的默认项 `value: ""`、
`--format`（export）的默认项 `value: ""` —— 都是"留空就不产生 token"，
所以"用 uv 自己的默认行为"不需要额外开关（规范 §3.3）。

**没有一处使用 `attached`**：uv 的开关全部是 `--long` 形式，没有 `-o{值}` 那种紧贴写法，
所以决策表里 `attached` 一行在 uv 上完全用不到（这与 7z 相反）。
短开关（`-p`、`-r`、`-e`、`-c`、`-w`、`-t`、`-o`）一律**只用长形式**
（`--python`、`--requirements`…），因为长形式更自解释、且 `uv --help` 里两种都列出。

### 7.3 `short` 与 `long` 的取舍

uv 的短开关大多有一对一的长形式。清单**只暴露长形式**，理由：
界面上 `-p` 不如 `--python` 自解释；而 `showCommandLine: true` 会把完整命令摆出来给用户核对。
未暴露的短形式不影响用户——因为它们是**同一个开关**的别名，不是缺失的能力。

---

## 8. 规范缺口（v1 表达不了的能力，**未擅自改规范**）

按 R5 与硬性要求 8，下面这些只记录，没有动 `docs/spec/**`。

**G1（重要）字段顺序即 argv 顺序，无法表达"开关必须排在位置参数之前"。**
uv 的 `run` / `tool run` 强制要求 uv 自己的开关写在被运行命令之前。
本次是靠**手工把开关字段声明在 command 之前**绕过（可行，因为顺序完全由我控制）。
但这意味着：**字段顺序被赋予了超出"显示顺序"的语义负担**——作者必须懂被包装程序的 argv 边界，
而规范与 playbook 都没把这一点写成规则。见 §11 对 playbook 的反馈。
规范 §9 的 D3 正是这个问题，uv 是它的第一个实证。

**G2 一个动作无法同时表达"互斥的一对开关"。**
`uv sync` 有 `--only-dev` 与 `--no-dev`，`--all-extras` 与 `--no-extra`，
`uv build` 有 `--sdist` / `--wheel`（不互斥，可同时给）。
v1 没有互斥表达（`visibleWhen` 只做条件显示，不做互斥），用户可能同时勾上 `--sdist --wheel`
（这是合法的）或同时用 `--only-dev --no-dev`（这是矛盾的，uv 会自己报错）。
本次只收敛了 `--only-dev` / `--no-dev` 里更常用的那个，没有强行加互斥。

**G3 `--exclude` / `--include` / `--prune` / `--no-install-package` 这类"否定式多值开关"没有合适的类型。**
它们语义是"排除集合"，用 `text` + `repeatable` 可以生成正确的 argv，
但界面上无法提示"这是排除项"。属于观感问题，不阻断。

**G4 `runtime.encoding` 只是工具包级的，但 uv 会转发子进程输出。**
`uv run python -c "print('中文')"` 的中文由 Python 写出（cp936），而 uv 自己的输出是 UTF-8。
同一个工具包内**两种编码并存**，v1 无法按动作覆盖。
本次选了 `utf-8`（覆盖 uv 自己的输出，占绝大多数），并把这条写进 §5.3。
可能需要的是**动作级 encoding**，或者让宿主按"这是转发型动作"另做处理。

**G5 `output.progress` 无法表达"不确定进度 / 只显示文本进度"。**
uv 的下载进度是"正在装 X"这类文本行，不是百分比。宿主有"先显示不确定进度"的做法
（P15 的对策），但规范里 `progress.unit: unknown` 只有枚举值，没有配套语义说明。
本次一个 progress 都没写（见 §9.2）。

**G6 `exitCodes` 依赖被包装程序的退出码文档，而 uv 没有这种文档。**
规范 §6 与 development.md §6 都把"退出码语义"当作从文档提取的事实（F4）。
uv 这条事实**不存在**，只能实测。规范没有说明"文档没写时该怎么办"
（本次做法：实测 + 在清单注释里标明哪一条是推断）。

**G7 `alternativeNames` 与 `versionPattern` 的耦合没有被规范或 playbook 点明。**
实测教训（详见 §3.1）：`alternativeNames` 里的备用可执行文件**可能打印与主程序不同的名字**
（`uvx --version` → `uvx 0.11.15 …`），于是同一个 `versionPattern` 不一定对所有名字都成立。
规范 §3.1 只说"找不到 `executable` 时依次试 `alternativeNames`"，随后就拿 `versionArgs`/`versionPattern`
取版本，**没有说明这两步用的是同一个正则、也没有要求作者对每个替代名分别验证**。
本次靠实测发现并把正则放宽成 `uvx?` 解决。
建议规范补一句：**`versionPattern` 必须能匹配 `executable` 与每个 `alternativeNames` 的输出**
（或者允许为替代名单独写正则）。

---

## 9. 故意没做的部分与原因（给后来者的地图）

### 9.1 故意没覆盖的命令

按任务给定的范围，以下命令**只记在这里，没有进清单**：

| 命令 | 为什么不做 |
|---|---|
| `uv publish` | 上传发行包到索引，属于发布流程；需要凭据，且与"本机常用"无关 |
| `uv auth`（`login` / `logout` / `token` / `dir`） | 云服务凭据管理，同上 |
| `uv workspace` | 该版本 `uv --help` 里**根本没有 workspace 这个顶层命令**（实测：0.11.15 的顶层命令列表见下），只有散落在各命令里的 `--package` / `--all-packages`；未做 |
| `uv pip compile` | 有自己的一整棵子选项树（`--generate-hashes`、`--no-strip-extras`、`--emit-index-url`… 12 056 字节的帮助），值得单独一轮 |
| `uv pip sync` | 与 `pip compile` 成对使用，同上（10 032 字节帮助） |
| `uv pip show` / `uv pip tree` / `uv pip check` | 更细的只读子命令，本次只做了 `list` / `freeze` 两个最常用的 |
| `uv python uninstall` / `uv python find` / `uv python update-shell` | 前者是破坏性且不常用；后两者偏环境配置 |
| `uv tool upgrade` / `uv tool uninstall` / `uv tool dir` | 未做；`tool install` + `tool list` 已覆盖主要用途 |
| `uv cache prune` / `uv cache size` | 未做；`cache clean` 与 `cache dir` 是更常用的两个 |
| `uv format` / `uv audit` / `uv check` | 偏 CI / 代码质量，不属于"包与环境管理"的核心 |
| `uv generate-shell-completion` | 一次性 shell 配置动作 |
| `uv self update` | **会替换正在运行的 exe**，风险最高，且它自己更新自己与"包管理"无关 |

`uv 0.11.15` 的**完整顶层命令表**（`uv --help` 实测，共 21 个，供后来者对照）：
`auth` `run` `init` `add` `remove` `version` `sync` `lock` `export` `tree` `format` `audit`
`tool` `python` `pip` `venv` `build` `publish` `cache` `self` `help`。

### 9.2 每个动作只暴露了最常用的 3–8 个开关，以下故意没暴露

统一列在这里，**全部在帮助快照里有出处**，需要时照 `doc` 指的位置加：

- **全局开关**（几乎每个命令都有）：`-q/--quiet`、`-v/--verbose`、`--color`、`--offline`、
  `--no-progress`、`--directory`、`--project`、`--config-file`、`--no-config`、
  `--system-certs`、`--allow-insecure-host`、`-n/--no-cache`、`--cache-dir`。
  **理由**：宿主已经有 `workingDirectory` 概念，`--directory` 与之重复；
  `--color never` 与 `--no-progress` 更适合让宿主统一控制；其余属于排错用的开关。
- **索引类**：`--index`、`--default-index`、`-i/--index-url`（已废弃）、`--extra-index-url`（已废弃）、
  `-f/--find-links`、`--no-index`、`--index-strategy`、`--keyring-provider`。
  **理由**：绝大多数用户用默认 PyPI；做了反而让人误配。已废弃的两个刻意不暴露。
- **解析器类**：`--resolution`、`--prerelease`、`--fork-strategy`、`--exclude-newer`、
  `--no-sources`、`-U/--upgrade`、`-P/--upgrade-package`、`--upgrade-group`。
  **理由**：本次只在 `lock` 上暴露了 `--upgrade`（最常用），其余留给将来。
- **安装器类**：`--reinstall`、`--reinstall-package`、`--link-mode`、`--compile-bytecode`。
- **构建类**：`-C/--config-setting`、`--no-build-isolation`、`--no-build`、`--no-binary` 等。
- **缓存类**：`--refresh`、`--refresh-package`。
- **Python 类**：`--managed-python`、`--no-managed-python`、`--no-python-downloads`。
- **依赖组类**：`--no-group`、`--no-default-groups`、`--only-group`、`--all-groups`、`--no-extra`。
  本次只暴露了 `--extra` / `--all-extras` / `--no-dev` 这几个最常用的。
- **`uv run` 专有**：`--env-file`、`--no-env-file`、`--isolated`、`--active`、`--frozen`、
  `--exact`、`--no-editable`、`-s/--script`、`--gui-script`、`--all-packages`、`--package`、
  `--python-platform`、`--with-editable`、`--with-requirements`、`--all-extras`、`--only-dev` 等。
- **`--python-platform`**：取值清单极长（46 个平台标签，帮助里占了 20 多行）。
  做成 `enum` 会让界面出现一个 46 项的下拉框，收益极低，**故意不做**。
- **未暴露 `--no-progress`**：因为清单也没写 `progress.pattern`（§6），
  宿主暂时没有进度可关，等有了再一起加。

### 9.3 顺带发现的、值得记一笔的事

- `uv pip download` **在 0.11.15 里不存在**。`scripts/fetch-uv-help.ps1` 抓它时返回 `exit=2`，
  脚本把它记进了 `_meta.json` 的 `missing` 字段（而不是静默跳过——P14 的教训）。
  对照 `uv pip --help` 的子命令表，pip 侧只有 `compile/sync/install/uninstall/freeze/list/show/tree/check`。
- `uv --help` 与 `uv help` 命令表**不一致**：`uv help` 多一个 `generate-shell-completion`
  （21 个 vs 22 个）。判断"有哪些命令"时不能只看其中一个。
- 本机 `uv tool list` 以退出码 0 结束，但 **stderr 里有警告**：
  `warning: Tool 'astrbot' environment not found (run 'uv tool install astrbot --reinstall' to reinstall)`。
  说明本机有一个**已损坏的已装工具**。宿主如果只看退出码会认为一切正常——
  这与 scoop 的"退出码不可靠"（D8）是**相反方向**的问题：uv 退出码 0 但输出有警告。
  这正是 D8 里提到的"是否要引入 `output.successPattern` / `failurePattern`"的真实案例。

---

## 10. 真机冒烟测试结果

测试脚本：[`scripts/smoke-uv.ps1`](../../scripts/smoke-uv.ps1)（可重跑一次复现本节全部数字）。
它做两件事：逐条实跑清单里的只读 examples，以及用清单里的正则实测 `versionPattern`。

### 10.1 校验器（先跑，必须全绿）

```
[ OK ] plugins\7zip\manifest.yaml  (11 动作 / 64 字段 / 字段出处标注 64 个 = 100%)
[ OK ] plugins\scoop\manifest.yaml  (40 动作 / 80 字段 / 字段出处标注 80 个 = 100%)
[ OK ] plugins\uv\manifest.yaml  (26 动作 / 125 字段 / 字段出处标注 125 个 = 100%)

3/3 个 manifest 通过
字段出处覆盖率: 269/269 (100%)
开关溯源（启发式，仅提示）: 检查 195 个开关，195 个能在参考文档快照里找到
```

退出码 **0**，全绿。

**注意运行时机的巧合**：这个"开关溯源"检查是我开始工作**之后**才由另一个提交加进校验器的
（`d922436 feat(validate): 新增「开关溯源」检查 —— 把 R1（不发明参数）变成自动信号`）。
该提交**当时还没有 uv 清单**（实测 `git ls-tree -r --name-only d922436 -- plugins/`
只列出 7zip 与 scoop），所以它当时报的数只覆盖那两个工具包。
**最终**（uv 快照落盘、清单写完之后）校验器报的总数是 195 个开关、0 个找不到。
**逐工具包拆分（实测）**如下：

| 工具包 | 语料文件数 | 检查的开关数 | 找不到 |
|---|---|---|---|
| 7zip | 52 | 48 | 0 |
| scoop | 67 | 40 | 0 |
| **uv** | **54**（uv-help 52 + uv-docs 2） | **107** | **0** |
| 合计 | — | **195** | **0** |

（48 + 40 + 107 = 195，与校验器总数行一致。）

它按约定"把 `docs/reference/` 下以工具包 id 开头的目录拼成语料"
（`load_reference_corpus`，第 130–156 行），所以 `uv-help/` 与 `uv-docs/` **恰好都被它自动收录**——
这也是遵守那条命名约定（`docs/reference/<id>-*`）的额外收益。
它报的 **uv 107/107 全部可溯源**，等于用机器确认了"清单里没有一个开关是凭空发明的"。
（作为对照：如果当初把快照目录命名成 `uvhelp/` 而不是 `uv-help/`，这条检查就会静默失效——
因为 `load_reference_corpus` 要求目录名以插件 id 开头。）

（注意：上面这几行是**按 cp936 解码**得到的。校验器本身是 Python，它的中文按系统代码页输出，
按 UTF-8 解会是乱码——与 §5.3 是同一个坑。）

**宿主侧核对**：`dotnet test src\AllTool.slnx` → **99/99 通过**，
其中 `RealManifestTests.加载全部工具包都不应抛异常` 会遍历 `plugins/` 下的**全部**清单
（`ManifestLoader.LoadAll`），所以 uv 清单**已被宿主的真实加载器 + 结构校验接受**，
不只是过了 Python 校验器。

### 10.2 `examples` 实跑：21 条，实跑 14 条，全部 exit=0

| # | 命令 | 实测退出码 | 结果 | 耗时 |
|---|---|---|---|---|
| 1 | `uv run --no-project --python 3.14 python --version` | **0** | 输出 `Python 3.14.7` | 81 ms |
| 2 | `uv run --no-project python -c print(1)` | **0** | 输出 `1` | 119 ms |
| 3 | `uv sync --check` | 未跑 | 安全红线 | — |
| 4 | `uv lock --check` | 未跑 | 安全红线 | — |
| 5 | `uv lock --dry-run` | 未跑 | 安全红线 | — |
| 6 | `uv export --format requirements.txt` | 未跑 | 安全红线 | — |
| 7 | `uv tree --depth 2` | 未跑 | 安全红线 | — |
| 8 | `uv venv .venv` | 未跑 | 安全红线 | — |
| 9 | `uv pip list` | **0** | 输出 `Using Python 3.14.7 environment at: C:\Users\Steve\scoop\apps\python\current` | 107 ms |
| 10 | `uv pip list --format json` | **0** | 同上（JSON 体） | 82 ms |
| 11 | `uv pip freeze` | **0** | 同上 | 83 ms |
| 12 | `uv pip install --requirements requirements.txt` | 未跑 | 安全红线 | — |
| 13 | `uv python list --only-installed` | **0** | 输出 `cpython-3.14.7-windows-x86_64-none  C:\Users\Steve\scoop\shims\python3.exe` | 324 ms |
| 14 | `uv tool list` | **0** | stderr 有 `astrbot` 警告（见 §9.3） | 80 ms |
| 15 | `uv tool list --show-paths` | **0** | 同上 | 79 ms |
| 16 | `uv cache dir` | **0** | 输出 `C:\Users\Steve\AppData\Local\uv\cache` | 54 ms |
| 17 | `uv self version --short` | **0** | 输出 `0.11.15` | 55 ms |
| 18 | `uv self version` | **0** | 输出完整版本行 | 54 ms |
| 19 | `uv help` | **0** | 输出总览 | 57 ms |
| 20 | `uv help sync` | **0** | 输出 `Update the project's environment.` | 58 ms |
| 21 | `uv help pip install` | **0** | 输出 `Install packages into an environment` | 67 ms |

**实跑 14 条，14 条 exit=0，与清单里"这些是只读命令"的定位一致。**
（第 3–8、12 条的动作本身在清单里是 `overwrite` / `destructive`，标题里也标了"未实跑"。）

### 10.3 **诚实记录：冒烟脚本第一版漏跑了一条红线命令**

第一版 `smoke-uv.ps1` 的只读白名单判定是这样写的：

```powershell
$ReadOnlyPrefixes = @( @('pip','list'), @('help') )   # ← PowerShell 会把它摊平成 3 个字符串
...
if ($Argv[0..($p.Count - 1)] -eq $p) { return $true }  # ← 错
```

这里**同时踩了两个 PowerShell 的坑**：

1. **嵌套数组字面量会被摊平**：`@( @('pip','list'), @('help') )` 实际得到 `@('pip','list','help')`
   三个字符串，`$p` 变成单个字符串，`$p.Count` = 1。
2. **`-eq` 用在数组上不是"相等"，而是"过滤"**：`@('pip','list') -eq 'pip'` 返回的是
   **元素数组 `@('pip')`**，不是 `$true`/`$false`。而 PowerShell 在 `if` 里把非空数组当 `$true`。
   实测：`@('pip','install')[0..0] -eq 'pip'` → 返回 `'pip'` → 判为**真**。

两个坑叠在一起，判定退化成"第一个 token 是不是 pip 之类"，
结果 **`uv pip install --requirements requirements.txt` 被真的执行了一次**。

实际后果（已核对）：该次运行以 **exit=2** 结束，唯一输出是
`error: File not found: 'requirements.txt'`——uv 在读取文件阶段就拒绝了，
**没有联网、没有下载、没有写入任何环境**；临时目录里除测试报告外没有别的产物，
仓库里也没有多出 `.venv` 或任何 uv 造的文件

修法（两处，都在最终版本里）：

- 判定改成**两边各自用 NUL 连接成字符串再比字符串**（`($left -join "`0") -eq ($right -join "`0")`），
  语义明确，不再依赖 `-eq` 的数组行为；同时用**一元逗号** `,@(...)` 保住数组边界，让 `$p.Count` 正确。
- **加了一段白名单自检**（`$MustNeverRun` 列出 17 条绝不允许被判为可跑的命令，
  任何一条漏过去就 `throw` 中止脚本，而不是"跑着看"）。修好后重跑，
  上表的"未跑"判定才是可信的。

**这条值得写进 playbook**：冒烟脚本判"哪些命令可以跑"的逻辑本身必须有自检，
否则一个静默的判定错误（尤其是 PowerShell 这种 `-eq` 语义反直觉的语言）就会让安全红线失效。
见 §11。

### 10.4 未实跑的命令清单及原因（安全红线）

以下动作**清单写了、字段定义了，但没有实跑**，原因一律是"会改动用户环境 / 项目文件 / 下载东西"：

`sync`、`add`、`remove`、`lock`（其 `--check` 与 `--dry-run` 也一并未跑——它们**看起来**只读，
但规范里这两个动作整体是 `overwrite`，我没有为单条 example 破例）、`export`（不带 `-o` 时只打印，
但同动作带 `-o` 会写文件，为保守起见整条未跑）、`tree`（空目录里只会报错，无信息量；在真项目里跑
又会触发 lock 行为）、`venv`、`pip-install`、`pip-uninstall`、`python-install`、`python-pin`、
`tool-install`、`tool-run`、`cache-clean`、`build`、`init`、`version-set`。

**唯一跑过的 `uv run` 两条已在上表列出**，它们的作用是打印解释器版本 / 打印常量 `1`，
不下载包、不写文件（`--no-project` 让 uv 不去找项目，实测 81–119 ms，
没有出现任何下载行为）。

---

## 11. 对 playbook 的反馈（本次任务的产出之一）

按"第二个作者"的真实体验逐条记，**没有改 playbook**（不在允许改动范围内）。

### 11.1 说不清的地方

1. **§0.1 的"命令数 ≲ 30"判据对子命令树型工具没有指导意义。**
   uv 顶层 21 个命令、带子命令共 49 个帮助页、单是 `uv run` 就有 60 多个开关。
   按字面读会得出"uv 不适合做工具包"的错误结论。
   实际可用的判据是"**你打算覆盖的那条路径上的开关数**"，而不是总命令数。
   建议 playbook 改成"顶层命令数 ≲ 30 **或** 你能明确圈出一个 ≲ 30 个动作的常用子集"。

2. **§0.2 的三类权威来源没有说"多个来源不一致时听谁的"。**
   uv 的 `uv help <cmd>`（官方长文档）与 `uv <cmd> --help`（内置帮助）内容不同，
   `uv --help` 与 `uv help` 的命令表还差一个。我按"内置帮助优先（与安装版本严格对应）"处理，
   但这是我自己的判断，playbook 没写。
   建议补一条：**同一次抓取里发现来源不一致时，以"随本机二进制分发的帮助"为准，
   并在 NOTES 里记下差异**（本次记在 §9.3）。

3. **§2 的 F4（退出码语义）默认"存在一个退出码页"。**
   uv **完全没有**退出码文档。playbook 与规范（§6）都没说这时该怎么办，
   而这直接决定 `exitCodes` 怎么写、以及 `exitCodes` 到底算不算"有出处的知识"。
   建议补一句："文档没有退出码章节时，用实测的退出码并在 NOTES 里标明哪几条是推断。"

4. **§3.1 的"顺序铁律"只讲了位置参数必须在前，没讲反过来的情形。**
   `uv run` / `uv tool run` 要求开关写在位置参数（被运行的命令）之前，
   写反了会静默地把开关转交给子进程（见 §7.1）。这是**功能性错误**，不是风格问题。
   建议：把这一条升格为"**先确认被包装程序的 argv 边界**，再决定字段声明顺序"，
   并把"第一个非开关参数之后一切都透传"这类程序（uv、cargo、git 的部分子命令）列为典型。

5. **§5 的冒烟模板假设"examples 都能跑"。**
   模板直接 `foreach ($c in $cases) { & <name> @($c.args) }`，没有"如何安全地筛出只读命令"的指引。
   对本项目（安全红线明确要求不跑写操作）来说这是最大的实操缺口。
   建议提供一个**白名单 + 自检**的最小骨架（本次的 `scripts/smoke-uv.ps1` 可直接拿来当模板）。

6. **§6 要求 NOTES 含 5 项，但没说"编码/退出码/版本正则"这三项实测结论该放哪。**
   我是照 `plugins/scoop/NOTES.md`（一个 AI 写的、更细的样板）补上的 §3/§4/§5。
   建议 playbook §6 直接把 scoop NOTES 的目录结构列为推荐骨架——毕竟现阶段"看样板"比看规则有效。

### 11.2 与实际不符的地方

1. **§1 情况 A 的"反编译出的原始 HTML 目录要加进 `.gitignore`"没有说"网页快照的 HTML 原件怎么办"。**
   7-Zip 那条有明确规则（`.gitignore` 第 15 行：`docs/reference/7zip-chm/`，
   注释写着"可由脚本重新生成，体积大且是派生数据"；实测 `git check-ignore` 确认被忽略）。
   **但情况 C（网页）没有对应规则**，`.gitignore` 里对 `docs/reference/uv-docs/` 没有任何规约
   （实测 `git check-ignore docs/reference/uv-docs/cli-reference.html` 无输出 = 不被忽略）。
   我据此选择**把 HTML 原件一并提交**（1 165 371 字节），理由：只有一份、是 HTML→Markdown 转换的输入，
   留着才能复核 pandoc 有没有丢内容。
   建议 playbook 明确三种情况的保留策略，别让作者靠猜。

2. **§7 收尾让执行 `git push`。**
   本次任务明确要求**不 commit、不 push**（改动留在工作区待人工审阅）。
   playbook §7 的脚本块建议加一句"（若上级指令禁止提交，跳过本节，只做 1–2 步）"。

3. **§5 第 1 条说"必须额外验证 `versionPattern` 真的能匹配"。**
   实际做的时候发现两件更值得测的事：
   - **要跑清单里真正的 `locate.versionArgs`**，不是随手找个版本命令
     （容易出的错是正则写对了但 `versionArgs` 填错，例如填 `version` 而不是 `self version`，
     在空目录里会 exit=2）。
   - **要对 `locate.executable` 与每个 `alternativeNames` 分别验证**。
     本次实测抓到：`uvx --version` 打印的是 `uvx 0.11.15 (…)` 而不是 `uv 0.11.15 (…)`，
     于是锚定 `^uv\s` 的正则在回退到 `uvx.exe` 时**匹配失败**（详见 §3.1）。
     playbook 完全没提这一层，而 `alternativeNames` 正是宿主会真的走到的分支。
   建议把验证点写成"对 `executable` 和**每一个** `alternativeNames` 各跑一次 `versionArgs`，
   确认正则都能捕获到同一个版本号"。

### 11.3 希望它补充的

1. **一个"只读 examples 白名单 + 自检"的骨架**（对应 §11.1 第 5 条），
   并写明"冒烟脚本的判定逻辑本身要自检"这条教训。本次的
   [`scripts/smoke-uv.ps1`](../../scripts/smoke-uv.ps1) 可以直接拿来当模板：
   白名单用一元逗号 `,@(...)` 保住数组边界，判定用"NUL 连接后比字符串"而不是 `-eq`，
   外加 `$MustNeverRun` 自检（漏一条就中止）。
   **特别提醒**：PowerShell 的 `-eq` 用在数组上是**过滤**而不是相等
   （`@('pip','list') -eq 'pip'` 返回 `@('pip')`，在 `if` 里算真），
   用它比对 token 序列会静默失效——本次就是这样把一条写操作放跑了一次（详见 §10.3）。
2. **一张"什么时候必须把字段声明在位置参数之前 / 之后"的判定表**（对应 §11.1 第 4 条）。
3. **"来源冲突以谁为准"的一条规则**（对应 §11.1 第 2 条）。
4. **"退出码文档不存在时"的处理约定**（对应 §11.1 第 3 条）。
5. ** NOTES 的推荐目录结构**（对应 §11.1 第 6 条）。

---

## 12. 还没做的 / 不确定的

1. **动作级的 `expectExitCode` 只给"应当成功"的 example 用了默认（不写）**。
   21 条 examples 里，14 条实跑都是 0；另外 7 条按安全红线未跑，
   因此**没有写 `expectExitCode`**——写了就是没验证过的断言，与 R1 的精神冲突。
2. **`locate.alternativeNames` 里的 `uvx` 已确认合理，但有实现层面的前提。**（见 §3.1 / §8 G7）
   实测：`uvx.exe` 与 `uv.exe` 同目录、同版本，`uvx --version` exit=0 并打印 `uvx 0.11.15 (…)`；
   官方文档明确说 `uvx` 就是 `uv tool run` 的别名（行为完全相同）。
   所以把它列为备用可执行名是有出处的，`versionPattern` 也已放宽成 `uvx?` 来兼容它。
   **仍请宿主实现者确认一件事**：回退到 `uvx.exe` 后，用户从界面发起的所有动作
   （例如 `uvx sync`）还是不是原来的语义？按文档"`uvx` = `uv tool run`"来说，
   `uvx sync` 会去**运行一个名叫 sync 的命令**，而不是 `uv sync`。
   也就是说 `uvx` 只适合**取版本**，不适合当 `uv` 的替代品去执行动作。
   如果宿主对 `alternativeNames` 的用途不止取版本，那 `uvx` 应当从清单里删掉。
   这一条我**没有能力在清单层面解决**（规范没有"仅用于取版本的替代名"这种表达），如实记录。
3. **`minVersion: "0.8.0"` 是保守取值，不是实测出来的边界。**
   实测只证明了 0.11.15 满足。清单里用到的开关（`--output-format`、`--only-installed`、
   `--show-version-specifiers` 等）**没有逐个回溯是哪个版本引入的**。
   如果宿主会真的用它禁用工具包，建议改成实测确认过的版本。
4. **`uv version` 动作在空目录里必然失败**（exit=2）。这是 uv 的设计，不是清单的错误，
   但界面上会表现为"填了表单一点就报错"。可能需要宿主支持 `requirements` / 前置校验
   （规范 §9 的 D5），或者作者在 `resultNote` 里提前说明（已做）。
5. **`tool-run` 的 `--from` 与 `command` 的关系没实测。** 我只验证了帮助文本
   （`--from` 用指定包提供命令），没有实跑（会下载包）。
6. **没有为 uv 加进 `docs/ai/drift-watch.json` 与 `scripts/check-doc-drift.ps1`。**
   那属于"漂移检测"这个独立能力（N4），且 `scripts/**` 在本次的禁改范围内。
   建议下一步加一个观测点：uv 的版本探测可以用
   `git ls-remote --tags https://github.com/astral-sh/uv.git`，
   文档快照比对 `docs/reference/uv-docs/cli-reference.html` 的归一化哈希。
7. **`uv help` 的 `command` 字段用 `repeatable: true` + `positional`（每行一个）表达多级命令**，
   例如两行 `pip` / `install` 生成 `uv help pip install`（实测该形式 exit=0）。
   这依赖"`text` + `repeatable` 每行一个 token"的语义（规范 §4 与 D9），
   宿主已实现（N6 的证据），但**我没有在界面上点过 uv 的这一条**。
