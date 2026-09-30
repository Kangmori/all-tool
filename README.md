# All Tool

**把 Windows 上已安装的命令行软件，变成可以点的界面。**

不用记参数、不用翻文档：选一个工具包 → 选一个动作 → 表单填好 → 点执行。
工具包是**纯声明式的 YAML 清单，不含任何代码**，宿主只按声明调用你机器上已装的程序。

> 目标平台：Windows 10 / 11（x64）。

---

## 快速开始

### 方式一：下载发布包（不需要装任何东西）

到 [Releases](https://github.com/Kangmori/all-tool/releases) 下载最新的开发测试版 zip，解压后双击 `AllTool.exe`。
发布包是**独立版**：目标机器不需要预装 .NET，也不需要 Windows App Runtime。

### 方式二：从源码构建

```powershell
git clone https://github.com/Kangmori/all-tool
cd all-tool
dotnet build src\AllTool.slnx
# 产出可交付目录（默认独立模式，零预装）
pwsh -File scripts/publish-app.ps1
```

## 已经带的工具包（27 个 / 385 个动作 / 918 个参数字段）

| ⚪ 你自己装的工具 | 动作 | 字段 |
|---|---|---|
| 7-Zip | 11 | 64 |
| Scoop | 40 | 80 |
| uv | 26 | 125 |

| 🔵 Windows 自带（开箱即用） | 动作 | 字段 |
|---|---|---|
| certutil | 10 | 23 |
| chkdsk | 5 | 11 |
| cleanmgr | 7 | 7 |
| curl | 14 | 50 |
| DISM | 12 | 30 |
| fsutil | 42 | 58 |
| icacls | 16 | 57 |
| ipconfig | 11 | 12 |
| netsh | 39 | 32 |
| netstat | 8 | 19 |
| nslookup | 3 | 11 |
| OpenSSH 客户端 | 10 | 37 |
| ping | 3 | 13 |
| powercfg | 14 | 23 |
| robocopy | 4 | 20 |
| schtasks | 12 | 41 |
| sfc | 3 | 4 |
| systeminfo | 3 | 6 |
| tar | 11 | 37 |
| tasklist | 5 | 11 |
| tracert | 4 | 13 |
| wevtutil | 18 | 49 |
| winget | 25 | 73 |

| 🔴 高危工具（含不可逆操作） | 动作 | 字段 |
|---|---|---|
| DiskPart | 29 | 12 |

每个参数字段都标注了官方文档出处（**覆盖率 100%**）。
工具包按类型在界面上有图标区分：⚪ 你装的 / 🔵 系统自带 / 🟡 交互式 / 🔴 高危。

## 它做了哪些"应该有的"事

- **执行前一定显示完整命令行**（只读、可复制）——不存在"背着你跑什么"的情况
- **危险动作会二次确认**（覆盖 / 删除 / 不可逆操作都有标记）
- **需要管理员权限的动作会提前标出来**，并提供「文件 → 以管理员身份重新启动」
- **拖文件到输入框即填路径**（多值字段会自动一行一项）
- **执行完给出下一步建议**并支持一键执行（例如 `scoop status` 之后可以一键更新）
- **记住上次输入**、工具包可分组、动作可筛选

## 工具包（插件）

一个软件一个工具包，放在 `plugins/<id>/manifest.yaml`。工具包只能**声明**，不能执行代码——
这是刻意的安全边界：你随时可以打开清单，看清每个动作到底会跑什么命令。

- 规范：[`docs/spec/manifest-v1.md`](docs/spec/manifest-v1.md)
- 开发手册（怎么给一个软件做工具包）：[`docs/ai/playbook-tool-package.md`](docs/ai/playbook-tool-package.md)
- 界面里可直接把工具包文件夹拖进「工具包」区域安装；右键可卸载（移到 `plugins/.trash`，不是删除）

## 给 AI 智能体看的文档

本仓库的**唯一入口是 [`AGENTS.md`](AGENTS.md)**：硬规则 R1–R7、接手顺序、命令速查、文档地图。
如果你的工具是 Claude Code 或 GitHub Copilot，它们各自有指路文件（[`CLAUDE.md`](CLAUDE.md)、
[`.github/copilot-instructions.md`](.github/copilot-instructions.md)），内容都指向 `AGENTS.md`，不要在那里找项目说明。

设计目标是：**任何一个不了解本项目的 AI 智能体，读完就能安全地继续开发。**

## 文档地图

| 想了解 | 看 |
|---|---|
| 硬规则、接手顺序、命令速查 | [`AGENTS.md`](AGENTS.md) |
| 清单规范（字段、风格、会话型、三级风险、kind） | [`docs/spec/manifest-v1.md`](docs/spec/manifest-v1.md) + [`manifest-v1.schema.json`](docs/spec/manifest-v1.schema.json) |
| **怎么给一个软件做工具包**（逐步流程） | [`docs/ai/playbook-tool-package.md`](docs/ai/playbook-tool-package.md) |
| **踩过的坑汇总**（27 条）与新增工具包检查单 | 同上 **§10 / §11** |
| Windows 自带命令的公共实测结论 | [`docs/ai/windows-commands.md`](docs/ai/windows-commands.md) |
| 我们提取的事实性数据（开关矩阵/清单） | [`docs/reference/README.md`](docs/reference/README.md) |
| 每个工具包自己的实测记录 | `plugins/<id>/NOTES.md` |

> 宿主程序（WinUI 3 那套 C#）的开发手册、项目状态与架构决策**不在本仓库**（见 `AGENTS.md` §5）。

## 许可

MIT，见 [`LICENSE`](LICENSE)。作者：Kangmori。
