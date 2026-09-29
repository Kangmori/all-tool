# All Tool —— 给 AI 智能体的入口（工具包开发）

## 0. 这个仓库是什么

把 Windows 上已安装的命令行软件，变成可以点的图形界面。
**一个软件 = 一个工具包 = 一份纯声明式的 YAML 清单，零代码。**

本仓库面向**工具包开发**：给一个新软件做支持、修某个工具包的参数、补实测记录，
都只需要改 `plugins/<id>/manifest.yaml` 与 `plugins/<id>/NOTES.md`。

> ⚠ **宿主程序（WinUI 3 那套 C# 代码）的开发手册、项目状态、架构决策不在本仓库。**
> 它们是"本机专属内容"，放在仓库之外（见 §5）。本仓库的文档**只讲工具包**。

## 1. 接手时按这个顺序做（不要跳）

```
0. 看仓库根目录有没有 .local-vault    ← 不入库的指针文件
                                        有 → 先读它指向的本机专属库（环境事实 / 开发流水 / 宿主手册）
1. 读 docs/spec/manifest-v1.md        ← 清单规范（含 §2.6 execution、§2.9 三级风险、§2.10 kind）
2. 读 docs/ai/playbook-tool-package.md ← 做工具包的逐步流程 + **§10 坑清单 + §11 检查单**（动手前必读）
3. 要碰 Windows 自带命令 → 先读 docs/ai/windows-commands.md（前人实测的公共结论，能省很多重复摸索）
4. 改完 → 跑校验（§3）→ 真机冒烟 → 写进 NOTES
```

没有 `.local-vault` 说明这是一台干净的机器：环境事实按 `scripts/check-env.ps1` 现测，不要凭记忆。

## 2. 硬规则（违反即视为错误，任何理由都不例外）

| # | 规则 |
|---|---|
| R1 | **不发明参数**。清单里的每个命令、开关、位置参数都必须能在官方文档里指出出处。文档没说的一律不写。 |
| R2 | **每个动作必须有 `sources`**；每个字段必须有 `doc`。CI 统计覆盖率，必须 100%。 |
| R3 | **改完工具包必须跑校验**：`uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py`，全绿才能提交。 |
| R4 | **必须真机冒烟**：只读动作真跑并记录退出码，结果写进该工具包的 `NOTES.md`。没跑过的清单不算完成。 |
| R5 | **不在清单里写代码**。manifest 没有脚本能力，这是刻意的安全边界；要新能力先改规范。 |
| R6 | **不把命令藏起来**：任何执行动作都必须能展示将运行的完整命令行（`showCommandLine: true`）。 |
| R7 | **环境事实必须实测**（版本、路径、编码、退出码语义），不靠记忆，也不靠抄别的包。 |

## 3. 命令速查

```powershell
# 校验全部工具包（本地与 CI 共用；必须全绿）
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py

# 核对环境是否漂移（换机器 / Windows 更新后先跑）
pwsh -File scripts/check-env.ps1

# Windows 自带命令的 /? 快照（带超时保护；快照放本机专属库，不入库）
pwsh -File scripts/fetch-win-help.ps1

# 真实输出（不是 /?）的编码与退出码实测
pwsh -File scripts/probe-win-output.ps1

# 7-Zip 的"命令 × 开关"矩阵（校验器第 3 层的输入）
pwsh -File scripts/extract-7zip-matrix.ps1

# 问上游"你的版本和文档变了吗"（CI 每周也会跑）
pwsh -File scripts/check-doc-drift.ps1

# Windows 命令集的只读冒烟（白名单自检）
pwsh -File scripts/smoke-win-cmds.ps1
```

## 4. 目录结构

```
plugins/<id>/manifest.yaml     工具包（唯一需要写的文件）
plugins/<id>/NOTES.md          实测记录：验了什么、没验什么、为什么
plugins/<id>/smoke.ps1         可选：该包自己的冒烟脚本
docs/spec/                     清单规范 + JSON Schema
docs/ai/playbook-tool-package.md   做工具包的标准流程（含子智能体委派规矩、字段风格规则）
docs/ai/windows-commands.md    Windows 自带命令的公共实测结论
docs/reference/                **我们提取的事实性数据**（开关矩阵/开关清单），第三方原文不在这里
scripts/                       校验器、抓取脚本、冒烟脚本、发布脚本
.github/workflows/             CI（校验 + 每周文档漂移检测）
```

## 5. 本机专属内容（不入库）

仓库之外有一个**本机专属库**（存放本机环境事实、第三方文档原文快照、开发流水、宿主开发手册）。
仓库根目录的 `.local-vault`（**不入库**）指向它；**从那里往本仓库搬内容必须先脱敏**。

判断口径：**"换一台机器还成立吗？"** 成立 → 进本仓库（这是知识）；不成立 → 进本机专属库（这是环境事实）。

## 6. 其它指路文件

| 文件 | 作用 |
|---|---|
| `CLAUDE.md` / `.github/copilot-instructions.md` | Claude Code / GitHub Copilot 的指路文件，**内容都只是指向本文件**，不要在那里找项目说明 |
| `README.md` | 面向使用者：怎么下载/构建、带了哪些工具包、文档地图 |
| `docs/spec/manifest-v1.schema.json` | 清单的 JSON Schema（校验器与编辑器都用它） |
