# 参考数据（`docs/reference/`）

**这个目录现在只放"我们自己提取的事实性数据"，不放别人的文档原文。**

## 为什么只剩这些

仓库转公开前做过一次清理：把从第三方软件/文档抓下来的**原文快照**全部从历史里移除了
（7-Zip CHM 的反编译文本、Scoop wiki 全站克隆、`scoop help` / `uv --help` 的输出、
Microsoft Learn 的页面 HTML）。原因是版权：那些是别人的作品，公开仓库里不能整篇转载。

保留规则（完整口径见 `本机专属库的 host-dev/development.md` §9.2、§9.3）：

| 内容 | 能不能入库 | 理由 |
|---|---|---|
| **我们自己提取的事实性数据** | ✅ | "某命令有哪些开关"是接口事实，且提取工作是我们做的 |
| 官方文档的链接与短引用 | ✅ | 合理引用，便于复核 |
| 第三方文档的**整篇原文** | ❌ | 等于发布别人的作品 → 放本机专属库 |
| 软件自带的帮助文件本身（`.chm`） | ❌ | 同上 |

判断口径一句话：**"这是我做的，还是我抄的？"**

## 现在目录里有什么

| 路径 | 是什么 | 谁在用 |
|---|---|---|
| `7zip-switch-matrix.json` | 从 7-Zip 官方 CHM 提取的"命令 × 可用开关"矩阵 | 校验器第 3 层：把清单里的开关与这份白名单比对，能抓住"把 `-o` 挂到 `a` 命令上"这类错误 |
| `win-switches/_switches.json` | 从 Microsoft Learn 各命令页提取的"开关表 + 小节锚点" | 校验器的开关溯源（启发式，只提示不阻断） |

两者都是**提取结果**，不含原文段落，所以可以公开。

## 第三方原文快照放哪

放在**本机专属库**里（仓库之外，不会推送）：

```
<本机专属库>/snapshots/
├── 7zip-chm/      7-zip.chm 的反编译结果（原始 HTML）
└── win-help/      Windows 命令的 /? 输出快照（含 stdout/stderr、编码与退出码实测）
```

指针文件是仓库根目录的 `.local-vault`（**不入库**），里面写着本机专属库的路径。
详见 `本机专属库的 host-dev/development.md` §9.3。

## 怎么重新生成（都是本机操作，产物默认落到本机专属库）

```powershell
# Windows 命令的 /? 快照（每个命令 8 秒超时保护——cleanmgr /? 会弹 GUI 且进程不退出）
pwsh -File scripts/fetch-win-help.ps1
# 只抓某几个：pwsh -File scripts/fetch-win-help.ps1 -Commands ping,netstat

# 真实输出（不是 /?）的编码与退出码实测
pwsh -File scripts/probe-win-output.ps1

# 7-Zip：反编译 CHM → 提取命令×开关矩阵
pwsh -File scripts/extract-7zip-matrix.ps1
```

**抓官方网页时**：三条通道（harness 的 `web_fetch` / PowerShell `Invoke-WebRequest` / `web_search`）
**一条不通就换下一条**，别在一条上反复试；三条都不通就改用程序自带帮助。细节见
`本机专属库的 host-dev/development.md` §4.4。

## 抓取时实测出来的坑（这些是知识，留在公开仓库）

- `cleanmgr /?` 是 GUI 对话框，**没有控制台输出、进程也不退出** → 抓取脚本必须带超时。
- `sfc` 的**所有**输出都是 **UTF-16LE**，其余命令用 OEM 代码页；判定 UTF-16LE 不能只看
  "ASCII 后跟 `00`"（汉字对的第二个字节不是 0）。
- 微软文档里的开关写 `/xxx`，**程序实际接受的是 `-xxx`**；清单里写程序接受的形态，
  官方写法保留在字段的 `doc` 里。
- 官方帮助里每个命令页都有一节 "Switches that can be used with this command"——
  这是**逐命令的开关白名单**，正是 `7zip-switch-matrix.json` / `_switches.json` 的来源。
  白名单只列"有独立帮助页"的开关（如 `-ba` 没有独立页面，只在示例里出现），
  所以提取时要同时收"白名单"与"页面任何位置提到过的开关"两类。

---

---

## 相关文档

| 文档 | 讲什么 |
|---|---|
| ``AGENTS.md`` | 硬规则 R1–R7、接手顺序、命令速查、文档地图（**入口**） |
| [`docs/spec/manifest-v1.md`](../spec/manifest-v1.md) + ``schema.json`` | 清单规范（字段、风格、会话型、三级风险、kind） |
| [`docs/ai/playbook-tool-package.md`](../ai/playbook-tool-package.md) | 做工具包的逐步流程 + **§10 坑清单 + §11 检查单** |
| [`docs/ai/windows-commands.md`](../ai/windows-commands.md) | Windows 自带命令的公共实测结论 |
| [`docs/reference/README.md`](README.md) | 我们提取的事实性数据放哪、第三方原文去哪 |
| `plugins/<id>/NOTES.md` | 该包自己的实测记录（验了什么 / 没验什么） |
