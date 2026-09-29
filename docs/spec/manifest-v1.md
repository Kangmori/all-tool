# 工具包清单规范 v1（manifest-v1）

> 状态：草案，等着被真实工具检验。第一个样板是 `plugins/7zip/manifest.yaml`。
> 机读定义见 `manifest-v1.schema.json`（JSON Schema Draft 2020-12），CI 用它校验。

## 0. 这份规范解决什么问题

一条命令行调用，本质上是这些东西：

```
可执行文件 + 命令 + 开关 + 位置参数  →  子进程  →  标准输出/错误 + 退出码
```

"可视化一个命令行软件"就是把这四段都变成**有类型、有取值范围、有默认值**的结构化数据，
让宿主能自动生成表单、自动拼出正确的 argv、自动解读输出。

关键约束：**参数知识必须来自官方文档，作者不得发明参数**。规范里为此专门设计了可溯源的字段。

## 1. 设计原则

1. **v1 零代码**。工具包只有声明，没有任何脚本能力。这让人身安全边界一目了然：一个工具包能做的事，只有"按声明的规则调用某个已存在的程序"。
2. **可溯源**。每个动作必须带 `sources`；每个字段应带 `doc` 指向官方文档里的具体位置。CI 会统计字段出处覆盖率。
3. **不发明参数**。只写官方文档里存在的命令、开关与位置参数。文档说不清的，不写进清单，而是记进 `NOTES.md`。
4. **argv 是数组，不是字符串**。字段映射成 token 序列后直接传给 `CreateProcess`，任何情况下都不做 shell 字符串拼接（`useShell` 默认为 false）。
5. **规范宁可小**。表达不了的能力先进待办，不偷偷加魔法。v1 的目标是能干净地表达 7z，然后接受 scoop 的检验。

## 2. 文件布局

```
plugins/<id>/
  manifest.yaml      必需。id 必须与目录名一致（CI 校验）
  NOTES.md           可选但强烈建议：覆盖了哪些、故意没覆盖哪些、有哪些坑
  icon.png           可选
```

### 2.1 界面分组：`category`（工具包级与动作级）

宿主左侧列表按分组展示，`category` 声明的是**默认分组**：

```yaml
category: 包管理            # 工具包级：这个软件归到哪一类
actions:
  - id: install
    category: 应用管理      # 动作级：这个动作归到哪一组
```

- **两级的定位不同**（产品负责人明确过）：**动作级 `category` 是作者的事**——自己的命令该怎么归类，作者最清楚，界面不提供改动作分组的功能；**工具包级的 `category` 只是可选的默认值**，「这个工具包算哪一类」是用户自己的事，界面提供新建/重命名/删除分组与移动工具包。
- **用户可以自己改工具包的分组**：宿主把用户的调整存在 `%APPDATA%\All Tool\grouping.json`，用户的覆盖优先于清单。
  所以清单里写得合适能省用户的事，但写得不合适也不致命。
- 不写 `category` → 归入「未分组」。
- **不要与字段级的 `group` 混了**：`group` 是表单内部的小节标题，`category` 是左侧列表的分组。

### 2.2 推荐下一步：`nextSteps`（动作级）

执行完之后，宿主可以按声明推荐"下一步"并让用户一键执行：

```yaml
  - id: status
    nextSteps:
      - title: 一键更新全部       # 按钮文字（必填）
        action: update            # 目标动作 id（必填，必须存在于同一工具包）
        when: '(?m)^\S+\s+\d\S*\s+\d'   # 可选：在刚跑完的输出里命中才推荐
        reason: 表格里有"已装版本 ≠ 最新版本"的应用   # 可选：给用户看的理由
        values: { apps: git }     # 可选：执行前预填的字段值
```

宿主只做**正则匹配 + 按钮呈现**，不理解任何具体软件的语义。这么设计有三个理由：
可审计（推荐规则和参数一样有出处、可评审）、不需要往宿主里塞特例、符合"清单里不写代码"的底线。

写 `nextSteps` 时的三条硬性要求：

1. **`action` 必须真的存在**。宿主对不存在的目标会静默忽略（避免"点了才报错"），
   所以写错 id 不会报错、只会永远不出现——`RealManifestTests` 里有一条测试专门守这个。
2. **`when` 的正则必须对着真实输出验证过**。凭印象写是最容易犯的错：
   scoop 的 `status` 最初写了 `Updates are available`，而它实际打印的是表格，真实输出里 0 命中。
   拿真实输出跑一遍再写进去。
3. **`when` 不写 = 总是推荐**。只在该动作跑完确实该有下一步时才这么写。

**从推荐一键执行时，宿主会更谨慎**：只要目标动作的 `danger` 不是 `none`，就先弹一次确认
（手点执行时 `overwrite` 不额外确认）。因为推荐只是"顺手一点"，不该让一次误触就去改系统。

### 2.3 工具包右键菜单：`quickActions`

右键左侧的工具包时，宿主提供一批**通用项**（打开所在目录 / 打开官网 / 版本信息 / 重新载入 / 卸载），
另外还可以按清单声明显示**软件专有**的操作：

```yaml
quickActions:
  - title: 检查可用更新     # 菜单项文字
    action: status          # 点它等于切到这个动作（用户再点执行）
  - title: 更新全部应用
    action: update
```

`action` 必须存在于同一工具包；写错则该项不显示（避免"点了才报错"）。

### 2.4 两个"规范里有、宿主还没实现"的字段（动手前请知悉）

诚实记录，避免作者以为写了就有效：

| 字段 | 现状 |
|---|---|
| `runtime.useShell` | **宿主尚未实现**。当前所有命令都是**直接 CreateProcess**（argv 数组，不做 shell 字符串拼接）。需要 cmd 内建命令（`dir` / `echo` / `copy`…）时，正确做法是 `locate.executable: cmd.exe` + `command: /c` + `commandArgs: [dir]`，不必依赖 `useShell`。 |
| `requiresAdmin` | **宿主尚未实现**（既不提示也不拦截）。需要管理员权限的动作（如 `chkdsk /f`、`sfc /scannow`）目前只会以"权限不足"失败告终。 |

`visibleWhen` 同样属于这一类（见 §4 的说明）。


### 2.5 两条容易写错的约定（Windows 自带命令集撞出来的）

**① `command` 允许为空字符串。** 有一类程序根本没有子命令——Windows 自带命令是典型：
`ping 8.8.8.8`、`ipconfig`、`netstat -an` 里没有"命令"这一段。这种情况下
`command: ""` 是**唯一诚实的写法**；强迫作者填一个非空值等于编造参数（违反 R1）。
schema 仍然把 `command` 列为必需键，是为了强制作者**显式决定**"这个动作有没有子命令"
（7z 那种必须写 `command: a` 的地方漏写会生成一条静默错误的命令）。
宿主执行层本来就是这么支持的，之前只有校验层多拦了一道，属于校验与执行自相矛盾。

**② `requiresAdmin` 的标记规则：只在有依据时标。**
- 官方文档明确写了需要管理员（例如 `chkdsk /f`、`sfc /scannow`）→ 标；
- 实测确认非管理员会失败（例如 `powercfg /waketimers`、`netstat -b`）→ 标，并把实测记进 NOTES；
- **不确定就不标**。宿主只做提示、不拦截：标错会让用户以为"必须提权"而放弃本来能用的功能，
  漏标最多是失败后看到一句权限错误——两害相权，宁可漏标。
- 动作级优先于工具包级（同一个软件常常只有部分动作要提权：`chkdsk` 只读检查不需要、`chkdsk /f` 需要）。


### 2.6 两种"不该由宿主直接跑"的动作：`execution`

```yaml
  - id: release
    execution: info      # 宿主不执行，只显示命令行 + 复制 + 在终端中打开
  - id: clean
    execution: terminal  # 需要交互或会弹窗，直接在真终端里打开
```

| 值 | 宿主行为 | 什么时候用 |
|---|---|---|
| `run`（默认） | 直接执行并捕获输出 | 绝大多数动作 |
| `info` | **不执行**：展示完整命令行，提供「复制命令」与「在终端中打开」 | 风险明显大于收益的系统级命令。典型：`ipconfig /release`（会切断网络，用户很可能是在远程桌面上点它）、`shutdown` 这类 |
| `terminal` | 不捕获输出，直接在真终端（`cmd /k`）里打开 | 需要交互或会弹窗的程序：`cleanmgr`、`diskpart`、向导式安装器 |

**为什么要有 `info`**：宿主能做的就是"帮人少记参数 + 看清命令"。
对会切断网络、会改系统状态、又没有可读输出的命令，替用户按下回车并不比让他自己按更好——
但把命令拼好、摊开给他看、一键复制/丢进终端，仍然是实打实的价值。
判断标准是"**这一步该不该由工具替你做决定**"，不是"命令危不危险"。

### 2.7 工具包的一句话说明：`summary`

界面上鼠标悬停在工具包条目上时显示**最短**的说明（例如 `ping` → `测试网络连通`）。
不写就退回用 `description`。`description` 则用于"选中工具包但还没选动作"时展示的详细介绍。

### 2.8 会话型程序（例如 diskpart）：`session` + 状态门控

有些程序是**交互式解释器**：`select disk 0` 改变的是**那个进程内部的状态**，
后面的命令依赖它。要让界面做到"没选磁盘时 `clean` 是灰的"，就要有"会话"的概念。

**这里的会话 = 脚本重放**，不是后台上挂一个进程：

```yaml
session:
  scriptArgs: ["/s", "{script}"]        # 怎么把脚本交给它（{script} → 临时脚本路径）
  exitCommand: exit
  errorPattern: '(?im)^(Virtual Disk Service error|.*\bnot found\b)'
  state:
    - key: disk
      title: 已选中磁盘
      capture: disk                      # 捕获组存成变量，界面提示里会用到
      successPattern: '(?im)^Disk (\d+) is now the selected disk'
    - key: volume
      title: 已选中卷
      capture: volume
      successPattern: '(?im)^Volume (\d+) is now the selected volume'

actions:
  - id: select-disk
    sessionCommand: "select disk {index}"
    establishes: disk                    # 成功后确立状态
  - id: create-partition
    sessionCommand: "create partition primary"
    requires: [disk]                     # 不满足 → 灰显
    requiresHint: "先执行「选择磁盘」"
    danger: overwrite
    confirmPhrase: "创建分区"              # 中风险：逐字确认
  - id: clean
    sessionCommand: "clean"
    execution: info                      # 极高风险：只解释 + 复制 + 在终端中打开
```

**宿主的执行方式是重放**：跑 `create partition primary` 时，实际生成的脚本是
`select disk 0` + `create partition primary` + `exit`（同一个状态键只重放最后一次选择）。
所以每条命令的**完整脚本都会显示在命令行预览里**（R6），用户看得到"它到底跑了什么"。

为什么不用长驻进程：效果等价（选择类命令只改状态）、**更安全**（不必在后台留一个已提权的解释器）、
更好验收（每一步都能看到完整脚本），而且逻辑变成纯函数、可以单测。
真正需要来回交互（带提示符、要回答问题）的程序仍走 `execution: terminal`。

### 2.9 三级风险与"让用户知道有这个功能"

本软件的目标是**让用户知道系统里、以及自己装的工具能做什么**——所以
**再危险的操作也要在界面上留一条**，只是执行方式不同：

| 级别 | 例子（diskpart） | 声明方式 | 用户体验 |
|---|---|---|---|
| 低 | `list disk` / `detail disk` / `select disk 0` | 什么都不用写 | 直接执行 |
| 中 | `create partition` / `assign` / `format` | `danger` + `confirmPhrase` | 执行前要**逐字输入确认短语** |
| **极高** | `clean` / `clean all` / `delete partition` | **`execution: info`** | **宿主不执行**：给解释、给完整命令、可复制、可在终端中打开（用户自己按下回车） |

判断"极高"的口径：**不可逆地毁掉现有数据**（清盘、删分区、格式化）。
"创建"类操作不动已有数据，属于中风险，正常加入。

### 2.10 工具包类型 `kind`：让左栏一眼看出"这是什么"

```yaml
kind: system        # user（默认）/ system / interactive / dangerous
```

**只影响显示方式，不影响任何功能。** 左栏的规矩是：

| kind | 含义 | 显示 |
|---|---|---|
| `user`（默认） | 用户自己装的工具（7-Zip、Scoop、uv） | 普通字重，无标记 |
| `system` | Windows 自带（ping、chkdsk…） | 🔵 + 细体 |
| `interactive` | 交互式 / GUI 程序（cleanmgr） | 🟡 + 粗体 |
| `dangerous` | 含不可逆的高危操作（diskpart） | 🔴 + 斜体 |

为什么要区分：左栏一眼扫过去，用户应该能分清"这是我装的"还是"系统本来就有的"，
以及**哪些点了会弹窗、哪些点了要格外小心**。悬停提示里也会补一句类型说明。

判断口径：一个工具包同时符合多条时，取**更需要注意**的那条
（dangerous > interactive > system > user）。

## 3. 执行模型（宿主怎么用这份清单）

1. **发现**：用 `locate.executable` 在 PATH 中查找，找不到再依次试 `alternativeNames` 与 `searchPaths`（支持 `%ENV%` 展开）。找到后按 `versionArgs` 取版本，用 `versionPattern` 提取版本号，与 `minVersion` 比较；不满足则禁用该工具包并提示。

   `versionPattern` 的匹配语义（必须实现成这样）：**逐行（`RegexOptions.Multiline`）匹配，取第一个捕获组作为版本号**。
   这条规则是被真实测量逼出来的：`7z i` 的输出**第一行是空行**，所以 `^7-Zip\s+([0-9.]+)` 在不加多行标志时永远匹配不到。
   工具包作者最好在正则里显式写上 `(?m)`——多余一点，但换个宿主实现也不会失效。
2. **组装 argv**：

   ```
   [可执行文件] + [command] + commandArgs + 各字段展开的 token（按字段声明顺序） + fixedArgs
   ```

   **字段声明顺序 = argv 顺序**，这是硬规则。以 7z 为例，官方语法要求"归档名必须是命令之后的第一个文件名"，所以 `archive` 字段必须声明在所有其他字段之前。

3. **空值规则**：字段值为 `null`、空字符串、空数组时**不产生任何 token**。因此"密码留空"天然等于"不加密"，"格式选自动"天然等于"不加 `-t`"。
4. **执行**：直接创建子进程（不经 shell），参数以数组传递。工作目录由 `workingDirectory` 决定（工具包级 + 动作级覆盖）。

   `runtime.usePseudoConsole`（默认 false）：是否为子进程分配一个伪控制台（ConPTY）。
   给**只在真控制台里画进度**的程序用。7-Zip 实测如此——被重定向时 40 MB 输入的完整输出只有
   354 字节、一个 `%` 都没有；接上伪控制台才输出百分比。
   代价：输出里会混入 ANSI 转义序列（宿主用 `AnsiText` 清理），且程序会认为自己在一台真终端里
   （有的程序会因此强制彩色输出）。所以这是**按工具包选择**的开关，不是默认行为。
5. **读取输出**：按 `runtime.encoding` 解码 stdout/stderr；按 `output.progress.pattern` 提取进度；`mode: capture` 表示结果需要留在内存里展示。
6. **收尾**：用 `exitCodes` 把裸数字翻译成人能看懂的结果；`danger` 为 `destructive` 的动作必须先弹二次确认。

## 4. 字段到 argv 的六种映射

| `style` | 生成规则 | 7z 实例 |
|---|---|---|
| `positional` | 值本身即一个 token；`positionalMode: perLine` 时按行拆成多个 token | `archive` → `archive.zip`；`rn` 的映射表 → `old.txt new.txt` |
| `attached` | `prefix + separator + value`（`separator` 默认空） | `-o` + `D:\out` → `-oD:\out` |
| `separate` | `prefix` 与 `value` 两个独立 token | 本工具包未用到（留给 `--output dir` 这类写法） |
| `flag` | `true` → 输出 `prefix`；`false` → 不输出 | `-sfx`、`-y` |
| `literal` | 把所选选项的 `args` 数组原样展开 | `-r-` / `-r0` / `-aoa` / `-slfh`；`args: []` 表示该选项不产生参数 |
| `repeated` | 每个值各生成一个 token（值可重复） | `-x!*.tmp`、`-v100m`、`-i!*.cpp` |

补充规则：

- `switchBase`：声明"这个字段对应**文档里**的哪个开关"，**只用于 CI 白名单校验，不产生 argv**。
  ⚠ 它不是「生成开关的字段」：要生成 `/deny` 这种固定 token，用 `style: literal` 的字段（每个取值带 `args`）。`literal` 风格与带后缀修饰的写法**必须**显式声明，例如 `-r-`/`-r0` 要写 `switchBase: "-r"`。
- `group`：界面分组标题。宿主在分组名变化时插入一个小标题（普通区与"高级"区各自按顺序分组）。
- `advanced`：默认折叠进"高级选项"。
- `save`：记住上次输入（输出目录、压缩包路径这类反复用到的值）。宿主**只在真正执行时**记录
  ——记录"用户真的用过"的值，而不是他随手点过的每个中间状态；下次打开同一工具包/动作时回填，
  回填优先于清单里的 `default`。
  **`type: password` 的字段永不落盘**：这是刻意的决定，不是遗漏。
- `repeatable`：该字段可以产出多个参数，**每个值一个 token**。对 `multiselect` / `files` / `paths` /
  `directories` 是天然的；对 `text` / `textarea` 表示**一个值一行**（界面给多行输入框）。
  例：7z 的 `-v`（分卷大小）文档明确支持多值，所以 `volumeSize` 是 `text` + `repeatable: true`，
  填三行就生成 `-v10k -v15k -v2m`。
- `visibleWhen`：条件显示（字段名 + `==` / `!=` / `&&` / `||`）。
  **注意：规范保留了这个字段，但 v1 宿主尚未实现**（现有清单里的使用次数为 0，所以暂无实际影响）。
  要用它之前请先实现宿主逻辑并补测试。

## 5. 输出与结果

```yaml
output:
  mode: progress | stream | capture
  showCommandLine: true          # 执行前展示将要运行的完整命令行
  progress: { pattern: "(\\d+)%", unit: percent, group: 1 }
  openOnFinish: explorer         # 完成后打开产物所在目录
  resultNote: ...
```

`showCommandLine` 是刻意做成默认开启的：用户能一眼核对"界面上的选择变成了一条什么命令"，
这既是信任来源，也是最好的排错手段——本工具的存在意义不是把命令藏起来，而是把命令**摆明白**。

## 6. 退出码

`exitCodes` 把程序的退出码映射为 `ok` / `warning` / `error` 三档，宿主据此决定用绿色还是红色呈现结果、是否提示"部分文件被跳过"这类非致命情况。

7z 的六个码已完整录入（0 / 1 / 2 / 7 / 8 / 255，见 `plugins/7zip/manifest.yaml`）。

## 7. 校验与 CI

```powershell
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
```

校验分四层：

1. **JSON Schema**：字段类型、必填项、枚举值、`additionalProperties: false`（拼错字段名会被抓住）。
2. **目录一致性**：`id` 必须等于所在目录名。
3. **开关白名单**（7z 专用）：把 `docs/reference/7zip-switch-matrix.json` 当作权威，
   校验每个字段挂的开关确实属于该命令。这份矩阵直接从官方 CHM 的命令页提取，包含两处信息：
   `switches`（该命令页的"可用开关"一节）与 `mentioned`（该命令页任何位置提到过的开关）。
   合并两者是因为官方白名单只列"有独立帮助页"的开关，像 `-ba` 这种无独立页面但确有使用的开关只在 `mentioned` 里。
4. **出处覆盖率**：统计有多少字段标注了 `doc`。这是质量指标，不阻断构建。
5. **冒烟测试（半自动）**：`examples` 里的命令标上 `expectExitCode` 后就是真机冒烟夹具——
   在临时目录里真跑一遍，验证"清单描述的行为"和"程序实际行为"一致。
   这一层不进 CI（会真的读写文件系统），实测结果记入工具包的 `NOTES.md`。

当前实测结果：

```
[ OK ] plugins\7zip\manifest.yaml  (11 动作 / 64 字段 / 字段出处标注 64 个 = 100%)
字段出处覆盖率: 64/64 (100%)
```

反向验证（把只允许用于解压的 `-o` 故意挂到 `a` 命令上）确认能拦住：

```
注入后的问题数: 1
  -> 动作 add (7z a) 字段 outdir: 开关 -o 不在官方允许列表内 [...]
```

## 8. v1 明确不做的事

| 不做 | 原因 |
|---|---|
| 脚本钩子、任意表达式求值 | 安全边界会立刻消失；先证明纯声明够用到什么程度 |
| 自动解析 `--help` 生成表单 | 输出格式千差万别且随语言变化，不可靠。工具包的参数定义应由人依据官方文档编写 |
| 管道、重定向、多命令串联 | 7z 用不到。真需要时（如 `ffmpeg | ...`）再设计，避免过早复杂化 |
| 交互式提示（密码、y/n、分页） | 用 `-p{密码}`、`-y`、`-ao` 等开关**前置规避**；确实绕不开时再上 ConPTY 半终端视图 |
| 覆盖某个工具的全部参数 | 只覆盖真实常用项，其余留给"自定义参数"输入框。7z 有 33 个开关 / 11 个命令，我们做了 64 个字段，`-m` 家族的格式专属参数刻意没做 |
| 非 Windows 平台 | 目标就是 Windows |

## 9. 待你拍板的问题

1. **空值语义**：统一为"空 = 不输出"是否可接受？（代价是没法传一个真正的空字符串参数，目前看没有工具需要）
2. **`visibleWhen` 的边界**：只做比较和与或，是否够用？还是干脆 v1 不做条件显示？
3. **字段顺序即 argv 顺序**：是否足够？有些工具要求开关必须排在位置参数之前，届时可能要引入 `argOrder`。
4. **`fixedArgs`（隐藏参数）**：目前没有任何工具包用到。保留还是删掉？留着容易被滥用来藏参数，与"把命令摆明白"的原则冲突。
5. **工具包依赖声明**：例如 `-sfx` 需要 `7zCon.sfx` 与 `7z.exe` 同目录。要不要在规范里加 `requirements`？
6. **历史记录 / 收藏命令**：属于工具包还是宿主？我倾向宿主统一做，工具包只提供 `save` 提示。

## 10. 下一步

- **scoop 工具包**：第二个样板，用来检验这套规范能否表达 28 个命令、子命令、以及需要提权的操作。scoop 的帮助输出是结构化的（`scoop help` 返回对象），且参数依据可直接来自本机安装版本。
- **宿主壳（WinUI 3）**：进程执行引擎 → 表单生成器 → 流式日志与进度。先打通"7z 解压一个包，有进度、能取消"这条垂直切片。
- **GitHub Actions**：把上面那个校验命令接进 CI（`windows-latest` 已实测可编译 WinUI 3）。

---

---

## 相关文档

| 文档 | 讲什么 |
|---|---|
| ``AGENTS.md`` | 硬规则 R1–R7、接手顺序、命令速查、文档地图（**入口**） |
| [`docs/spec/manifest-v1.md`](manifest-v1.md) + ``schema.json`` | 清单规范（字段、风格、会话型、三级风险、kind） |
| [`docs/ai/playbook-tool-package.md`](../ai/playbook-tool-package.md) | 做工具包的逐步流程 + **§10 坑清单 + §11 检查单** |
| [`docs/ai/windows-commands.md`](../ai/windows-commands.md) | Windows 自带命令的公共实测结论 |
| [`docs/reference/README.md`](../reference/README.md) | 我们提取的事实性数据放哪、第三方原文去哪 |
| `plugins/<id>/NOTES.md` | 该包自己的实测记录（验了什么 / 没验什么） |
