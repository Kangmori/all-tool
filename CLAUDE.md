# 给 Claude Code 等读取 CLAUDE.md 的工具

**本仓库的唯一入口是 [`AGENTS.md`](AGENTS.md)，请先完整读它。**

不要在本文件里寻找项目说明——那样会造成多处文档不一致。本文件只做指路。

接手顺序：

1. `AGENTS.md` —— 硬规则（7 条）、必读顺序、命令速查
2. `pwsh -File scripts/check-env.ps1` —— 核对环境是否与记录一致
3. `本机专属库的 host-dev/project-state.json` —— 机器可读的项目状态、未决问题、下一步动作
4. `本机专属库的 host-dev/development.md` —— 架构、环境事实、已知陷阱（**动手前必读陷阱清单**）
5. 若要新增工具包 → `docs/ai/playbook-tool-package.md`
6. 若要改清单规范 → `docs/spec/manifest-v1.md`

---

**本仓库的文档地图**见 ``AGENTS.md``（Claude Code 用 `CLAUDE.md`）的 §1 与 §4；
工具包开发看 ``docs/ai/playbook-tool-package.md``，
清单规范看 ``docs/spec/manifest-v1.md``。
