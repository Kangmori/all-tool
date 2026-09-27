# swpj

把本机已安装的命令行软件变成可点击的界面。一个软件对应一个"工具包"（插件），
工具包是纯声明式的清单，不含任何代码。

> 项目代号 `swpj` 是临时占位，正式名称待定。

## 现在有什么

- **规范**：`docs/spec/manifest-v1.md` + `manifest-v1.schema.json`
  —— 描述如何把官方文档里的参数知识变成"能自动生成表单、能自动拼出 argv"的结构化数据。
- **第一个工具包**：`plugins/7zip/manifest.yaml`
  —— 7-Zip 的 11 个命令、64 个字段，全部标注了官方 CHM 里的出处，已通过 schema 与开关白名单校验。
- **文档流水线**：`scripts/` + `docs/reference/`
  —— 把官方文档抓下来、转成可检索文本、提取出机器可校验的事实。
- **技术栈决定**：`docs/adr/0001-宿主技术栈.md`（.NET 10 + WinUI 3，已做编译与启动冒烟验证）。
- **AI 协作文档**：`AGENTS.md` + `docs/ai/`
  —— 入口规则、开发文档、工具包开发手册、清单模板、机器可读状态文件、环境漂移核对脚本。
  设计目标是：**任何一个不了解本项目的 AI 智能体，读完就能安全地继续开发**（含上下文丢失后的恢复流程）。

宿主程序（WinUI 3 应用）**尚未开始编写**。

## 目录结构

```
AGENTS.md        AI 智能体入口：硬规则、必读顺序、命令速查
docs/
  ai/            AI 专用文档：开发文档、工具包手册、模板、项目状态（机读）
  spec/          规范与 JSON Schema
  adr/           架构决策记录
  reference/     官方文档快照与提取出的事实（含生成方法说明）
plugins/         工具包；一个子目录一个软件
scripts/         文档抓取、事实提取、清单校验、环境核对
spike/           一次性验证项目（WinUI 3 环境冒烟测试）
src/             宿主程序源码（待开始）
```

## 常用命令

```powershell
# 校验所有工具包（本地与 CI 共用同一入口）
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py

# 重新抓取参考文档
pwsh -File scripts/fetch-scoop-help.ps1
pwsh -File scripts/extract-7zip-matrix.ps1
```

## 设计上的两条底线

1. **不发明参数**：清单里出现的每个开关与位置参数，都必须能在官方文档里指出出处；
   CI 会统计出处覆盖率，并把开关和官方逐命令白名单做交叉校验。
2. **不把命令藏起来**：每次执行前都展示将要运行的完整命令行。
   这个工具的价值是把命令**摆明白**，而不是让用户再也看不懂自己在做什么。
