# win-switches —— 开关溯源语料（入库的是派生事实，不是微软的正文）

本目录是校验器"开关溯源"（R1：不发明参数）所用的语料。

## 两个文件族

- `<工具包 id>.json` —— **从本机 `/?` 快照派生出的开关名清单**（入库 ✔）。
  只保留命令名、抓取日期、开关名列表；**不含微软帮助正文**。
- `_switches.json` —— 早期从 Microsoft Learn 页面提取的"命令 × 开关"索引（保留原样）。

## 为什么正文不入库（既有决定，我先前违反过一次）

`scripts/fetch-win-help.ps1` 的头部写明：`docs/reference/win-help/` 里是**微软的文本**，
本项目转为公开仓库后**不能随仓库发布**，所以该目录在 `.gitignore` 里，只留在本机。
我曾在不知情时把它提交进仓库（commit `18ea9b6`），已在 `2b71cbc` 撤出并恢复忽略；
**入库的替代物就是本目录的派生清单**。若将来判定再分发条款没问题，再考虑把正文入库。

## 两份抓取脚本的分工（**并存，互不改动**）

| 脚本 | 负责 | 特点 |
|---|---|---|
| `scripts/fetch-win-help.ps1` | 最初那 12 个 Windows 自带命令的快照 | 分开记录 stdout/stderr；产出 `_meta.json`；对会弹对话框的 GUI 程序有超时。**不要改它。** |
| `scripts/capture-command-help.py` | 按**每个工具包自己的 `locate`** 抓其余包 | PATH/别名/常见目录定位；`diskpart` 走提权通路 |

## 怎么重新生成

1. 先按分工抓快照到 `docs/reference/win-help/`（本机目录，不入库）：
   `pwsh -File scripts/fetch-win-help.ps1`（那 12 个）与 `uv run --with pyyaml python scripts/capture-command-help.py`（其余包）。
2. 再从快照派生本目录的 `<id>.json`（只抽开关名）——
   `diskpart` 的 `/?` **本身需要提权**：做法是把重定向写进一个临时 `.cmd`，再
   `Start-Process -Verb RunAs -Wait` 运行该文件（引号只有一层；直接塞 `cmd /c "..."` 会因多层引号写坏而挂住）。
3. 快照随 Windows 版本变化：换版本后**重新抓一次**，并刷新派生清单的 `capturedAt`。

## 抓不到的

命令的 `/?` 需要提升、或按名字定位不到时，该包会**没有语料**，校验器会如实报 `[未覆盖]`
（这比拿别的命令的共享语料冒充要好 —— 那曾造成过"假完整"）。
