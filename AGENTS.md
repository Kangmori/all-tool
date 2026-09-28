# AGENTS.md —— 给 AI 智能体的入口

本文件是本仓库的**唯一入口**。任何 AI 智能体接手本项目，按下面的顺序做，不要凭记忆或猜测行动。

---

## 0. 项目一句话

把 Windows 上已安装的命令行软件，变成可点击的图形界面。一个软件对应一个**工具包**（纯声明的 YAML 清单，零代码），宿主程序读取清单后自动生成表单、拼出 argv、执行并展示结果。

## 1. 接手时按这个顺序做（不要跳）

```
1. 读 docs/ai/project-state.json      ← 机器的可读状态：环境事实、已有产物、未决问题、下一步
2. 跑 scripts/check-env.ps1           ← 用真实探测核对状态文件有没有过期（Windows 更新后会漂移）
3. 读 docs/ai/development.md          ← 架构、硬规则、全部已知陷阱
4. 读 docs/spec/manifest-v1.md        ← 要改动清单规范时才需要通读
5. 要新增工具包 → 读 docs/ai/playbook-tool-package.md（逐步照做）
```

## 2. 硬规则（违反即视为错误，任何理由都不例外）

| # | 规则 |
|---|---|
| R1 | **不发明参数**。清单里的每个命令、开关、位置参数都必须能在官方文档里指出出处。文档没说的一律不写。 |
| R2 | **每个动作必须有 `sources`**；每个字段必须尽量有 `doc`（指向官方 CHM 的页或文档锚点）。CI 统计覆盖率。 |
| R3 | **改完工具包必须跑校验**：`uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py`，必须全绿才能提交。 |
| R4 | **新增或修改工具包必须做真机冒烟测试**，把实测结果（含退出码）记进该工具包的 `NOTES.md`。没跑过的清单不算完成。 |
| R5 | **不在清单里写代码**。manifest v1 没有脚本能力，这是刻意的安全边界；需要新能力时先改规范（见 development.md 的变更流程）。 |
| R6 | **不把命令藏起来**。任何执行动作都必须能展示将运行的完整命令行（`showCommandLine: true`）。 |
| R7 | **环境事实必须实测**，不靠记忆。版本号、路径、可用性都要用命令验证后再写进文档或代码。 |

## 3. 命令速查（每条都验证过）

```powershell
# 校验全部工具包（本地与 CI 共用；必须全绿）
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py

# 核对环境是否漂移（换了机器、Windows 更新后先跑这个）
pwsh -File scripts/check-env.ps1

# 重新抓取参考文档
pwsh -File scripts/fetch-scoop-help.ps1          # scoop 的结构化帮助（28 个命令）
pwsh -File scripts/extract-7zip-matrix.ps1       # 从 7z CHM 提取"命令 x 可用开关"矩阵
pwsh -File scripts/fetch-win-help.ps1            # Windows 自带命令的 /? 快照（带超时保护，快照不入库）

# 问上游"你的版本和文档变了吗"（CI 每周也会跑，见 .github/workflows/doc-drift.yml）
pwsh -File scripts/check-doc-drift.ps1

# 界面验收（UIA 自动点，不需要人看）
pwsh -File scripts/ui-smoke.ps1                  # 填表 → 点执行 → 校验进度/产物；场景二验证「取消」
pwsh -File scripts/ui-verify-grouping.ps1        # 工具包分组：分组… → 新建并移入 → 移回未分组
pwsh -File scripts/smoke-win-cmds.ps1            # Windows 命令集的只读冒烟（含白名单自检）
pwsh -File scripts/capture-app-window.ps1 -ActionIndex 1   # 只截应用窗口

# 产出可交付目录（默认独立模式：目标机器不需要预装任何运行时）
pwsh -File scripts/publish-app.ps1

# 编译 WinUI 3（不需要打开 Visual Studio）
& dotnet build src\AllTool.slnx
& dotnet build src\AllTool.App\AllTool.App.csproj
```

## 4. 环境三条要点（细节见 development.md §4）

1. **`web_fetch` 在本机不可用**。Clash Verge 开了 fake-ip DNS，所有域名解析成 `28.0.0.x`，抓取工具会拒绝非公网 IP。**改用 PowerShell 的 `Invoke-WebRequest`**，它能正常出网（已实测）。`web_search` 仍可用。
2. **当前用户不是管理员**，且**没有 C++ 工具链**。纯 C# + P/Invoke 路线，不要引入需要 MSVC 的依赖。
3. **在 pwsh 里抓中文输出前，先设编码**，否则命令返回的中文是乱码：

   ```powershell
   $OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8
   ```

## 5. 提交约定

- 提交信息用中文，首行形如 `feat(7zip): ...` / `fix(7zip): ...` / `docs: ...` / `chore: ...`。
- 一次提交只做一件事；改工具包时把"校验输出"或"冒烟实测结果"作为依据写进正文。
- 推送前必须：校验全绿 + 工作区干净。

## 6. 当前进度（一句话）

规范 v1 已定稿（含 `category` 分组与 `nextSteps` 推荐下一步）；**宿主（WinUI 3）可工作并经界面冒烟验证**
（UIA 填表 → 点执行 → 真实 7z 解压 → **进度条走到 100** → 输出与产物均正确；另有取消场景）；
界面支持**拖文件填路径、工具包分组（默认折叠；用「分组…」按钮或右键菜单增删改移，不做鼠标拖动）、执行后一键执行推荐、必填校验、记住上次输入**；工具包可**直接拖进来安装**（右键可卸载，移到 .trash 不删除），菜单栏含「关于」；
工具包 **15 个**：7-Zip(11 动作) / Scoop(40) / uv(26) / **Windows 自带命令 12 个**
（ping ipconfig tracert nslookup netstat tasklist systeminfo chkdsk sfc robocopy cleanmgr powercfg，共 70 动作）；
字段出处标注 **419/419 = 100%**；开关溯源 300 个查 298 命中（启发式，只提示不阻断）；
测试 **142 个**全通过；CI 与每周文档漂移检测已接入；
`scripts/publish-app.ps1` 能产出**零预装的独立交付目录**（目标机器不需要 .NET 或 Windows App Runtime）。
**待办以 `docs/ai/project-state.json` 的 `nextActions` 为准**（当前主要是：改名 All Tool、清掉第三方文档快照后转公开、加 MIT 许可、发开发测试版 Release）。

## 7. 文档地图（别在错的地方找东西）

| 想了解 | 看 |
|---|---|
| 硬规则、入口 | `AGENTS.md`（本文件） |
| 机器可读状态、待办、待决、实测记录 | `docs/ai/project-state.json` |
| 架构、环境事实、**全部已知陷阱 P1–P28** | `docs/ai/development.md` |
| 清单规范（改清单格式前必读） | `docs/spec/manifest-v1.md` + `manifest-v1.schema.json` |
| 新增工具包怎么做 | `docs/ai/playbook-tool-package.md` |
| 为什么这么设计（取舍与放弃的方案） | `docs/adr/` |
| Windows 自带命令的实测事实 | `docs/ai/windows-commands.md` |
| 第三方文档快照（**不入库；转公开前会清理**） | `docs/reference/`（见其中的 README） |
