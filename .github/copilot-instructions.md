# Copilot 指令

**本仓库的唯一入口是 [`AGENTS.md`](../AGENTS.md)，请先完整读它。**

本文件只做指路，不复述内容，以免多处文档不一致。

接手顺序：

1. `AGENTS.md` —— 硬规则、必读顺序、命令速查
2. 运行 `pwsh -File scripts/check-env.ps1` 核对环境
3. `本机专属库的 host-dev/project-state.json` —— 项目状态、未决问题、下一步动作
4. `本机专属库的 host-dev/development.md` —— 架构、环境事实、已知陷阱
5. 新增工具包 → `docs/ai/playbook-tool-package.md`

改完工具包必须跑：

```powershell
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
```
