# 设计提案：会话型工具包（以 DiskPart 为例）

- 状态：**待产品负责人确认**（未实现；确认后再动规范与宿主）
- 提出日期：2026-09-29
- 相关：规范 §2.6（`execution`）、`AllTool.Core/Execution/ConPtyProcessRunner.cs`

## 1. 想要的体验（产品负责人的原话）

> 以 DiskPart 命令为例：包含的动作应该含有所有可能的动作，一些动作会显灰不能执行，
> 用户执行 `select disk` 或者 `select volume` 后才允许选中。

也就是说：**动作是否可用，取决于这次"会话"里已经做到哪一步**。
`clean` / `create partition` 在没选磁盘之前应该灰掉，并且要能告诉用户"先选磁盘"。

## 2. 为什么不能靠现有机制实现

现有模型是"**一个动作 = 一条命令 = 一次独立的进程**"（`ArgvBuilder` → `CreateProcess` → 收集输出 → 退出）。
而 DiskPart 是**交互式解释器**：`select disk 0` 改变的是**那个正在运行的进程内部的状态**，
进程一退出，选择就没了。所以：

- 靠"每个动作单起一次 diskpart" → 每次都得重新 `select`，用户感受不到"有状态"；
- 靠"把 select 拼进每条命令"（例如 `clean` → `select disk 0` + `clean`）→ **做不到灰显**，
  而且更重要的是：**用户看不到自己即将对哪块磁盘动手**，这在擦盘场景里是危险的。

结论：要实现这个体验，宿主必须支持**长驻会话**。

## 3. 方案：会话（session）+ 声明式状态门控

### 3.1 会话

工具包声明它是一个会话型程序，宿主**只启动一个进程并保持它活着**，用户点动作 = 往这个进程喂一行命令。

好消息：**底层已经具备**。伪控制台执行器（`ConPtyProcessRunner`）本来就创建了 stdin 写入流
（现在只用来回答光标位置查询）。需要的改动是"别在命令结束后杀进程"，并暴露 `SendLine()`。

```yaml
session:
  start: diskpart.exe            # 需要管理员；宿主已有 requiresAdmin 提示与提权重启
  workingDirectory: inherit
  exitCommand: exit              # 用户点"结束会话"时发这条
```

### 3.2 状态：由**输出**推导，声明式写正则

状态不写在宿主里，而是清单声明"看到什么样的输出，就说明处于什么状态"——
与 `nextSteps.when`、`output.progress` 完全同一套思路（宿主不懂语义，只做正则匹配）。

```yaml
  state:
    - key: disk-selected
      title: 已选中磁盘
      detect: '(?m)^Disk (\d+) is now the selected disk'
      capture: disk                # 把捕获组存进状态变量，供后续命令与提示引用
    - key: volume-selected
      title: 已选中卷
      detect: '(?m)^Volume (\d+) is now the selected volume'
      capture: volume
```

### 3.3 动作：发什么、需要什么状态、成功后会确立什么状态

```yaml
actions:
  - id: list-disk
    title: 列出磁盘
    category: 查看
    sessionCommand: "list disk"      # 只读动作：任何时候都能点
    sources: [...]

  - id: select-disk
    title: 选择磁盘
    category: 选择
    sessionCommand: "select disk {index}"
    establishes: disk-selected       # 这条成功（无错误输出）后，该状态成立
    fields:
      - id: index
        label: 磁盘号
        type: number
        style: attached
        prefix: ""                  # 直接拼在 "select disk " 之后
        required: true

  - id: clean
    title: 清除磁盘（清空分区表）
    category: 危险操作
    sessionCommand: "clean"
    requires: [disk-selected]        # ← 没这个状态时**灰显**
    requiresHint: "先执行「选择磁盘」"  # ← 灰显时鼠标悬浮显示这句
    danger: destructive
    confirmPhrase: "清空磁盘 {disk}"  # ← 必须逐字输入这句话才执行（见 §4）
    confirmText: "这会不可逆地清空 {disk} 号磁盘上的全部分区与数据，无法撤销。"
    sources: [...]
```

### 3.4 宿主怎么表现

1. **会话状态条**：明确显示"当前：磁盘 0 已选中 / 未选中任何磁盘 / 未选中卷"。
   破坏性动作上永远标出**作用目标**（`{disk}` 会被替换成真实值）——这是安全设计，不是装饰。
2. **动作列表**：`requires` 不满足的动作**灰显且点不动**，鼠标悬浮显示 `requiresHint`。
   状态一变（宿主检测到输出里的标志），列表立即重算。
3. **显式开始/结束会话**：不自动启动。用户点「开始会话」才启动进程，并在状态栏显示进程号；
   点「结束会话」发 `exit` 并回收进程。**会话没启动时，灰显规则照旧生效**（都不可用），
   提示"先开始会话"。
4. **所有命令仍然先显示再执行**（R6），并且会话里发出去的每一行都进日志（含时间戳）。

## 4. 安全规则（我认为不可省略的几条）

DiskPart 的 `clean` / `format` / `delete partition` **不可逆、且没有撤销**；选错磁盘就是数据没了。
所以：

1. **破坏性动作必须逐字输入确认短语**（`confirmPhrase`，例如"清空磁盘 0"），
   而不是点一下"确定"。理由：点按钮和"意识到自己在擦哪块盘"之间没有认知负担，
   打字才有。宿主已有的"二次确认"对这类操作**不够**。
2. **破坏性动作的确认文本必须带真实目标**（`{disk}` 替换后的值），
   不能出现"确定要清除吗"这种没有对象的问句。
3. **只读动作随便点，破坏性动作全部落日志**（谁、什么时候、对哪个目标）。
4. **默认会话里不放 `exit` 之外的自动命令**；会话启动后不自动执行任何东西。
5. `requiresAdmin: true`：diskpart 需要管理员，宿主已实现提示与「以管理员身份重新启动」。
6. **冒烟测试绝不发送破坏性命令**（沿用 `scripts/smoke-*.ps1` 的"白名单 + 自检"模式）：
   只跑 `list disk` / `list volume` / `select disk N`（select 只改会话状态，不动磁盘）。

## 5. 这套机制还能用在别处

会话模型不是为 DiskPart 单独开的口子，同样能覆盖：

| 程序 | 用法 |
|---|---|
| `netsh` | 上下文切换（`netsh interface` → 子命令），状态就是"当前在哪个上下文" |
| `sqlite3` / `python` / `node` | 交互式 REPL，`requires` 可用来看 `.open` 是否已执行 |
| `openssl s_client` | 连接建立后才允许发请求 |
| `gdb` / `lldb` | 附加到进程后才允许查看栈 |

## 6. 实现分期（每期都能单独验收）

| 期 | 内容 | 验收 |
|---|---|---|
| S1 | 规范：`session` / `state` / `sessionCommand` / `establishes` / `requires` / `requiresHint` / `confirmPhrase` + schema | 校验器全绿；用一个**无害的会话程序**（`cmd.exe`）写测试工具包 |
| S2 | Core：会话执行器（保持进程、SendLine、持续读输出、正则推导状态） | 单测：状态检测、多状态、状态被后续输出改变、进程退出后的表现 |
| S3 | 宿主：会话 UI（状态条、灰显 + 悬浮原因、开始/结束、确认短语输入框） | 界面验收脚本：灰 → 满足条件后可点；确认短语打错不执行 |
| S4 | 工具包：`diskpart`（先做只读 + 选择类，破坏性动作单独走查） | 冒烟只跑只读命令；破坏性动作逐条人工核对 `confirmPhrase` 文案 |

**S1 之前不动任何清单**——避免出现"规范里写了、宿主没实现"的那种空承诺（本项目已经吃过几次亏）。

## 7. 需要产品负责人确认的三点

1. **破坏性会话动作要不要"逐字输入确认短语"**？我认为必须（理由见 §4.1）。
   如果你觉得太啰嗦，退一步的方案是：只对 `clean` / `format` / `delete partition` 这类要求输入，
   其它 `danger: destructive` 仍用点确认。
2. **要不要真的做 `diskpart`？** 它是本机唯一"能一键毁掉数据"的工具。
   也可以先只做 §3.4 的机制 + 用 `cmd.exe` 验证，等你想清楚再放 diskpart 清单进来。
3. **会话是否要求显式开始**？（我倾向"是"，避免后台挂着一个已提权的解释器。）
