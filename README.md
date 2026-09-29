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

## 已经带的工具包

| 工具包 | 动作数 | 说明 |
|---|---|---|
| 7-Zip | 11 | 压缩 / 解压 / 查看 / 校验 / 哈希 / 维护归档 |
| Scoop | 40 | 应用、桶、缓存、Shim、配置、别名的查询与管理 |
| uv | 26 | Python 项目、环境、包、版本、全局工具 |
| **Windows 自带命令 12 个** | 70 | ping、ipconfig、tracert、nslookup、netstat、tasklist、systeminfo、chkdsk、sfc、robocopy、cleanmgr、powercfg |

合计 **147 个动作 / 419 个参数字段**，每个字段都标注了官方文档出处（覆盖率 100%）。

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

本仓库的**唯一入口是 [`AGENTS.md`](AGENTS.md)**：硬规则、接手顺序、文档地图。
机器可读状态在 [`本机专属库的 host-dev/project-state.json`](本机专属库的 host-dev/project-state.json)，
已知陷阱（P1–P28）在 [`本机专属库的 host-dev/development.md`](本机专属库的 host-dev/development.md)。
设计目标是：**任何一个不了解本项目的 AI 智能体，读完就能安全地继续开发。**

## 许可

MIT，见 [`LICENSE`](LICENSE)。作者：Kangmori。
