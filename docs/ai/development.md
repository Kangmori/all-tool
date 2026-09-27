# AI 开发文档

> 本文档的读者是 AI 智能体，不是人。追求**无歧义、可执行、可验证**，不追求可读性。
> 入口在仓库根目录 `AGENTS.md`。机器可读状态在 `docs/ai/project-state.json`。

---

## 1. 项目定义与边界

**是什么**：Windows 桌面工具。用户机器上已安装的命令行软件（scoop / 7-Zip / ffmpeg / uv …），通过本工具以图形界面调用，点按钮替代敲命令。

**核心机制**：一个软件 = 一个工具包 = `plugins/<id>/manifest.yaml`（纯声明 YAML）。宿主按规范解读清单 → 生成表单 → 用户填选 → 组装 argv 数组 → 创建子进程 → 流式展示输出。

**不是什么**（避免做错方向）：

- 不是命令行的包装脚本集合。工具包不含代码。
- 不是 shell。v1 不支持管道、重定向、命令串联。
- 不做"自动解析 `--help` 生成界面"。参数定义由人依据官方文档编写，因为 `--help` 输出格式千差万别且随语言变化。
- 不追求覆盖某工具的全部参数，只覆盖真实常用项。

**已确定的宿主技术栈**：.NET 10 + WinUI 3（Windows App SDK），unpackaged 部署。理由见 `docs/adr/0001-宿主技术栈.md`。

---

## 2. 架构与数据流

```
manifest.yaml ──┐
                ├─→ [宿主: 发现] locate → PATH/searchPaths → 取版本 → 与 minVersion 比对
project-state   │
                ├─→ [宿主: 表单] fields → 控件（类型 + values + visibleWhen + group/advanced）
                │
                ├─→ [宿主: 组装] argv = [exe] + [command] + commandArgs
                │                        + Σ 字段展开的 token（按字段声明顺序）+ fixedArgs
                │
                ├─→ [宿主: 执行] CreateProcess（useShell 默认 false，参数数组，绝不拼字符串）
                │                        CWD 由 workingDirectory（工具包级 / 动作级覆盖）决定
                │
                └─→ [宿主: 呈现] 解码 stdout/stderr（runtime.encoding）
                                 progress 正则提取进度；exitCodes 翻译结果；danger 决定是否二次确认
```

**宿主的组件划分**（尚未实现，作为实现指引）：

| 组件 | 职责 | 关键约束 |
|---|---|---|
| ManifestLoader | 读 YAML、用 JSON Schema 校验、报错定位到字段 | 校验失败必须拒绝加载并显示原因，不能静默降级 |
| ToolLocator | 找可执行文件、取版本、与 `minVersion` 比对 | 找不到时展示 `notFoundHint` |
| ArgvBuilder | 字段 → token 序列 | 纯函数、可单测；**这是最该先写测试的组件** |
| ProcessRunner | 创建进程、流式读输出、取消时杀进程树 | 两条路径：默认走管道（`Kill(entireProcessTree: true)`）；清单声明 `runtime.usePseudoConsole: true` 时走 `ConPtyProcessRunner`（伪控制台 + Job Object；挂起创建 → 入 job → 恢复运行，保证后代都在 job 里） |
| OutputInterpreter | 编码解码、进度解析、退出码翻译 | 中文环境下编码是主要坑，见 §8 |
| PluginStore | 扫描 `plugins/` 与用户目录、安装/卸载工具包 | 见规范 §2 的布局约定 |

**规范是唯一事实来源**：`docs/spec/manifest-v1.md`（人读）+ `manifest-v1.schema.json`（机读）。
两者必须同步修改，不准只改一个。

---

## 3. 不可违反的规则（Invariants）

与 `AGENTS.md` §2 同源，此处给出**可检验的判据**：

| # | 规则 | 如何检验 |
|---|---|---|
| R1 | 不发明参数 | 每个字段的 `doc` 能在 `docs/reference/` 对应文档里找到该开关 |
| R2 | 动作必须有 sources，字段尽量有 doc | `validate-plugins.py` 的 schema + 覆盖率输出 |
| R3 | 改完必须校验全绿 | `validate-plugins.py` 退出码 0 |
| R4 | 新工具包必须真机冒烟，结果记入 NOTES.md | NOTES.md 里有"实测结果"小节，含退出码 |
| R5 | 清单里没有代码 | schema `additionalProperties: false`；不存在脚本字段 |
| R6 | 不隐藏命令 | 动作的 `output.showCommandLine` 不为 false |
| R7 | 环境事实必须实测 | 文档里的版本/路径可在 `env-report`（check-env 输出）里对上 |

**CI 校验的第 3 层"开关白名单"（7z 专用）是最有价值的一层**，见 §6。

---

## 4. 环境事实（2026-09-27 实测，用 `scripts/check-env.ps1` 复核）

### 4.1 机器与账号

| 项 | 值 |
|---|---|
| OS | Windows 11 专业工作站版，`10.0.26200`，x64 |
| 用户 | `KANGMORI\Steve`，**非管理员** |
| 工作区 | `D:\AI\swpj`（NTFS，已授予当前用户完全控制；此前 ACL 缺失曾导致沙箱无法写） |
| 开发者模式 | 已开启（WinUI 打包应用调试需要） |
| 长路径 | 已开启 |

### 4.2 工具链（均已实测可用）

| 工具 | 版本 | 路径 |
|---|---|---|
| .NET SDK | `10.0.401` | `C:\Program Files\dotnet\dotnet.exe` |
| Visual Studio | Community **2026** `18.10.12217.157` | `C:\Program Files\Microsoft Visual Studio\18\Community` |
| Windows SDK | `10.0.26100.0` | `C:\Program Files (x86)\Windows Kits\10` |
| Windows App Runtime | `2.5.1` | （与 NuGet 上 `Microsoft.WindowsAppSDK` 最新稳定版一致） |
| git / git-lfs | `2.55.0` / `3.7.1` | scoop |
| PowerShell | 7.x | `C:\Program Files\PowerShell\7\pwsh.exe` |
| pandoc | `3.11` | scoop（HTML→Markdown 转换用） |
| gh | 已登录 `Kangmori`（keyring） | scoop；已执行 `gh auth setup-git` |
| uv | `0.11.15` | winget |
| scoop | `0.5.3` | `C:\Users\Steve\scoop` |
| 7-Zip | `26.03` | `C:\Users\Steve\scoop\apps\7zip\26.03\` |
| python / node | `3.14.7` / `26.8.2` | scoop |

### 4.3 VS 工作负载（重要，容易误判）

已装工作负载 id：`Microsoft.VisualStudio.Workload.Universal`。
**它的显示名是"WinUI 应用程序开发"（WinUI application development）**，不是 UWP。
这是历史沿革：UWP 工作负载经两次改名演进而来，id 未变。用 `vswhere -requires` 检查时**必须写这个 id**，不存在叫 WinUI 的 id。

已装关键组件：`Component.WindowsAppSdkSupport.CSharp`、`Component.Windows11SDK.26100`。

**不需要装**（已确认）：C++ WinUI 应用开发工具、"使用 C++ 的桌面开发"、".NET 桌面开发"、Windows 11 SDK 28000。
纯 C# + P/Invoke 路线用不到 MSVC。

### 4.4 网络限制（会直接影响工作方式）

Clash Verge（`verge-mihomo`）开着 **fake-ip DNS**，所有域名解析为 `28.0.0.x`（实测：`ffmpeg.org→28.0.0.30`、`github.com→28.0.0.27`）。

后果与对策：

| 通道 | 状态 |
|---|---|
| harness 的 `web_fetch` | **不可用**（拒绝非公网 IP），对所有站点都失败 |
| harness 的 `web_search` | 可用（返回结果摘要） |
| PowerShell `Invoke-WebRequest` | **可用**，这是抓官方文档的唯一通道。实测 7 个 URL 中 6 个成功，`dev.7-zip.org` TLS 握手失败 |

因此：**任何文档抓取都必须走 PowerShell + 落盘**，不能指望实时联网读文档。

### 4.5 已验证的技术事实（不要重复验证，除非怀疑环境变了）

| 事实 | 验证方式 | 结果 |
|---|---|---|
| `dotnet build` 能在**不开 VS** 的情况下编译 WinUI 3 | 编译 `spike/winui3-smoke` | 通过，0 警告 0 错误，24 秒 |
| unpackaged WinUI 3 应用能启动 | `Start-Process` + 检查 `MainWindowTitle` | 窗口标题正确，进程存活 |
| → 推论：GitHub Actions 的 `windows-latest` 可以做构建 CI | 由上一行推出 | 可行 |
| `hh.exe -decompile` 能反编译 CHM | 反编译 `7-zip.chm` | 80 个文件 |
| 7z 的 10 条 examples 行为正确 | 临时目录实跑 | 全部 exit 0，`x`/`e` 行为差异符合文档 |

**pwsh 输出中文的坑**：捕获命令输出时中文会变乱码，需先执行
`$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8`。

---

## 5. 目录职责

```
AGENTS.md                      AI 入口；硬规则与必读顺序
README.md                      给人看的项目简介
docs/
  ai/                          AI 专用文档（本文件、工具包手册、状态文件、模板）
  spec/                        规范与 JSON Schema —— 唯一事实来源
  adr/                         架构决策记录（含放弃的方案与原因）
  reference/                   官方文档快照 + 提取出的事实 + 生成方法
plugins/<id>/                  工具包。一个子目录一个软件
scripts/                       抓取、提取、校验、环境核对
spike/winui3-smoke/            一次性验证项目（WinUI 3 环境冒烟），Phase 1 后可删
src/                           宿主程序源码（待开始）
```

---

## 6. 校验流水线（改工具包后必须走）

```powershell
uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py
```

四层 + 一层半自动：

1. **JSON Schema**（Draft 2020-12）：类型、必填、枚举、`additionalProperties: false`。
   注意：schema 是**严格**的，拼错字段名会直接失败，这是故意的。
2. **目录一致性**：`id` 必须等于所在目录名。
3. **7z 开关白名单**：依据 `docs/reference/7zip-switch-matrix.json`，
   校验每个字段挂的开关确实属于该命令。判定用 `switches ∪ mentioned`：
   - `switches`：命令页 "Switches that can be used with this command" 一节
   - `mentioned`：该页任何位置出现过的开关
   - 为什么取并集：官方白名单只列**有独立帮助页**的开关。`-ba`（禁用表头）没有独立页面，
     只在 `hash` 示例里出现，只看 `switches` 会误报。这条不搞清楚就会把校验器写坏。
   - 另有两条家族规则：`-m*` 归 `-m`；`base` 长度 ≥3 时允许前缀匹配（`-aoa` 之于 `-ao`）。
   字段可用 `switchBase` 显式声明对应开关——`literal` 风格和带后缀的写法**必须**写。
4. **出处覆盖率**：统计带 `doc` 的字段比例（质量指标，不阻断）。
5. **开关溯源（启发式，只提示不阻断）**：把该工具包的参考文档快照拼成语料
   （约定：`docs/reference/` 下**目录名以工具包 id 开头**的都算它的文档，
   例如 `uv-help` / `uv-docs` 之于 `uv`），检查清单里用到的每个开关能否在语料里找到。
   当前 **195/195 全部命中**（7zip 64 字段 / scoop 80 / uv 125）。
   不做成门禁的原因：语料可能不全，短开关（`-n`）也容易在正文里偶然命中；
   真正的门禁是第 3 层那种**从官方文档逐命令提取的白名单**。
6. **冒烟测试（手动）**：把 `examples` 在临时目录里真跑一遍，结果记入 `NOTES.md`。

**当前结果**：三个工具包 `269/269 字段出处标注 100%`，开关溯源 195/195 命中。
**反向验证**（把只允许用于解压的 `-o` 挂到 `a` 命令上）确认白名单能拦住。

---

## 7. 关键设计决定（改动前先读）

| 决定 | 理由 / 出处 |
|---|---|
| 清单零代码 | 安全边界清晰：工具包只能"按声明调用已存在的程序"。要加能力先改规范 |
| 字段声明顺序 = argv 顺序 | 7z 要求归档名是命令后第一个文件名，所以 `archive` 必须声明在最前 |
| 空值（null / "" / []）不产生 token | 于是"密码留空 = 不加密"天然成立，不需要特殊开关 |
| `switchBase` 显式声明 | 让 CI 能校验 `literal` 风格与带后缀写法（`-r-`、`-aoa`）对应的官方开关 |
| `versionPattern` 按多行匹配 | 被实测逼出来的：`7z i` 输出**首行是空行**，`^` 不加多行标志永远匹配不到 |
| `showCommandLine` 默认开 | 工具的价值是把命令**摆明白**，不是藏起来。也是最好的排错手段 |
| unpackaged 而非 MSIX | 个人自用，避免证书与打包复杂度；代价是依赖已装的 Windows App Runtime |
| 不做远程插件仓库 | 工具包放主仓库 `plugins/`，更新 = `git pull`。省掉签名/鉴权/更新服务器一整块 |

---

## 8. 已知陷阱清单（踩过的，逐条记下）

| # | 现象 | 原因 | 对策 |
|---|---|---|---|
| P1 | 中文输出变乱码 | pwsh 捕获输出的编码 | 命令开头设 `[Console]::OutputEncoding = UTF8` |
| P2 | `web_fetch` 对所有站点失败 | fake-ip DNS → 非公网 IP | 改用 `Invoke-WebRequest` |
| P3 | YAML 里裸写日期导致 schema 报 "not of type string" | YAML 1.1 隐式类型 | 写成 `"2026-09-27"`；校验器也做了自动规整 |
| P4 | `7z i` 的版本正则匹配不到 | 输出首行是空行 | `versionPattern` 用 `(?m)^` |
| P5 | 官方逐命令白名单漏掉 `-ba` 之类的开关 | 白名单只列有独立帮助页的开关 | 校验时用 `switches ∪ mentioned` |
| P6 | 7-Zip **不使用系统通配符**：`*.*` ≠ 所有文件 | 官方语法页明确说明 | 要处理全部文件必须用 `*`；写进 NOTES.md 提醒用户 |
| P7 | `-o` / `-y` 只能用于解压命令 | 官方逐命令白名单 | CI 强制；把 `-o` 挂到 `a` 上会被拦 |
| P8 | `-v`（分卷）只能用于 `a` 命令 | 同上 | 同上 |
| P9 | `x` / `e` 默认解压到**当前目录** | 7z 的行为 | 这两个动作用 `workingDirectory: outputDir` 覆盖 |
| P10 | 目录里 clone 的 git 仓库被当成 gitlink 提交，别人克隆后拿不到内容 | git 把含 `.git` 的子目录视作内嵌仓库 | 提交前删掉内层 `.git`，或改用 submodule |
| P11 | 沙箱无法给工作区授写权限，所有命令被拦 | `D:\AI\swpj` 的 ACL 缺显式 WRITE_DAC | `icacls "D:\AI\swpj" /grant "Steve:(OI)(CI)F"`（已修复） |
| P12 | VS 工作负载 id 看起来像 UWP | 历史改名，id 未变 | 认 id `Microsoft.VisualStudio.Workload.Universal` |
| P13 | 脚本拿到了"另一个同名文件"的内容，逻辑却看不出错 | **PowerShell 变量名大小写不敏感**：局部变量 `$stateFile` 会覆盖参数 `$StateFile` | 局部变量加前缀区分（`$vsStateFile`）；关键路径用 Write-Host 打印出来，出错时一眼可见 |
| P14 | 错误被静默吞掉，只看到下游的空值判断走了 else 分支 | 脚本开头设了 `$ErrorActionPreference = 'SilentlyContinue'` | 探测脚本需要容错，但当"期望有值却为空"时要把实际路径/来源打印出来，否则排查成本极高 |
| P15 | 子进程的进度条不动、界面像是卡住 | **7z 之类的程序在被重定向输出时不画进度**，只在真控制台里画（实测 40 MB 输入仅 354 字节输出、无 `%`） | 界面先显示"不确定"进度，解析到真实百分比再切成确定进度；彻底解决要上 ConPTY（伪终端），见 N5 |
| P16 | 启动进程报 `目录名称无效`（Win32Exception） | `ProcessStartInfo.WorkingDirectory` 指向一个**还不存在**的目录时，CreateProcess 直接失败 | `ProcessRunner` 会先创建该目录——"解压到还不存在的目录"是常见合理意图。见 `ProcessRunner.RunAsync` 的注释 |
| P17 | 截图里混进了桌面上别的窗口（浏览器、聊天工具） | 全屏截取，或 `SetForegroundWindow` 被前台限制挡下导致目标窗口仍在底层 | 用 `PrintWindow(..., PW_RENDERFULLCONTENT)` 让窗口画自己（`scripts/capture-app-window.ps1`）；并约定 `spike/*.png` 不入库 |
| P18 | 测试偶发失败（同一段代码时过时不过） | 测试里用了 `System.Progress<T>`，它把回调投递到同步上下文/线程池，断言时可能还没落地 | 测试用同步收集器（`SyncProgress<T>`）；`Progress<T>` 只留给有 DispatcherQueue 的界面层 |
| P19 | **ConPTY（伪控制台）路径拿不到任何子进程输出**（已解决，根因值得记住） | 根因：**子进程的标准句柄是从父进程复制过去的**——即使 `bInheritHandles = FALSE`，句柄"值"照样被填进子进程的标准句柄槽，而 ConPTY 只负责提供控制台、**不会覆盖**这些继承来的句柄。于是构成三种截然不同的表现：父进程是控制台程序 → 子进程直接写到父进程的控制台（绕过 ConPTY）；父进程的 stdout 被重定向（如 `dotnet test`）→ 子进程写到那个看不见的管道；父进程是无控制台的 GUI 程序（真实的 WinUI 宿主）→ 没有可继承的句柄，ConPTY 正常工作 | 修法：创建子进程时加 `STARTF_USESTDHANDLES`，并把三个标准句柄显式置为 NULL（见 `ConPtyProcessRunner`）。定位过程同样值得记：先逐一排除 8 项假设（结构体尺寸 / 继承标志 / 管道安全属性 / PTY 句柄关闭时机 / `ResizePseudoConsole` / 渲染时机 / 读循环吞异常 / 字段错位），再用**独立 GUI 子系统探针**（`spike/conpty-probe`）复现出"无控制台父进程下 ConPTY 正常"，最后用 `GetFileType` 在子进程内部确认句柄类型，才锁定"继承"这一条 |
| P20 | 代码改了、也"构建成功"了，但运行的程序行为没变 | 两个原因各踩过一次：① 项目声明了 `<Platforms>x64</Platforms>`，**经解决方案构建**的产物落在 `bin\x64\Debug\` 而不是 `bin\Debug\`，脚本与启动器却指向后者（旧 exe 一直在被启动）；② `Swpj.App` 曾经不在 `src/Swpj.slnx` 里，于是 `dotnet build src\Swpj.slnx` **从不重建宿主**，清单加了新字段后宿主还在用旧版 `Swpj.Core`，表现为"载入工具包失败：Property 'xxx' not found on type ..." | 已修：加 `<AppendPlatformToOutputPath>false</AppendPlatformToOutputPath>` 固定输出路径，并把 `Swpj.App` 加进解决方案。**通用做法**：改完代码后确认产物时间戳变了再测；"构建成功"不等于"跑的是新代码" |
| P21 | 界面上填了多个值，命令行里却只出现一个参数 | **WinUI 的 `TextBox` 用 `\r` 表示换行**（不是 `\r\n`，也不是 `\n`）。所有"按行拆成多个值"的地方如果只按 `\n` 拆，整段文本会被当成一行，多值就退化成一个参数（实测：给 7z 的「分卷大小」填三行，生成的是 `"-v10k15k2m"` 一个被引号包住的参数） | 拆分一律写成 `text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)`。`ArgvBuilder` 的 `perLine` 与宿主的三个多值取值器都已按此修，并有专门的单测（`positional_perLine_也能处理只用回车换行的文本`） |

---

## 9. 未决问题（影响后续实现，改规范前需要拍板）

详见 `docs/spec/manifest-v1.md` §9。摘要：

1. 空值语义统一为"空 = 不输出"是否可接受（代价：无法传真正的空字符串参数）
2. `visibleWhen` 的表达能力边界（v1 只有 `==`/`!=`/`&&`/`||`）
3. 字段顺序即 argv 顺序是否足够（有些工具要求开关在位置参数之前 → 可能要 `argOrder`）
4. `fixedArgs`（隐藏参数）保留还是删除（倾向删除，与 R6 冲突）
5. 是否要 `requirements`（如 `-sfx` 需要 `7zCon.sfx` 与主程序同目录）
6. 历史记录 / 收藏命令属于工具包还是宿主（倾向宿主）

---

## 10. 路线图与下一步

| 阶段 | 内容 | 完成判据 |
|---|---|---|
| P0 ✅ | 环境就绪、规范 v1、第一个工具包、CI 可用的校验 | 已达成 |
| P1 | **scoop 工具包** | 28 个命令可界面化；校验与冒烟通过 |
| P2 | **宿主壳（WinUI 3）** | 垂直切片：7z 解压一个包，有实时进度、能取消、能看日志 |
| P3 | GitHub Actions | `windows-latest` 上跑校验 + 构建；文档漂移检测 |
| P4 | 工具包 + 其它软件（uv / aria2 / ffmpeg / git …） | 按 playbook 逐个产出 |

**下一步动作以 `project-state.json` 的 `nextActions` 为准**，那里有具体的做法与验收方式。

---

## 11. 变更流程（Definition of Done）

**改工具包**：

1. 按 `playbook-tool-package.md` 采集文档 → 提取事实 → 写清单
2. `validate-plugins.py` 全绿
3. 冒烟测试实跑，结果写进 `NOTES.md`
4. 更新 `project-state.json` 的 `plugins` 段
5. 提交：`feat(<id>): ...`，正文附校验输出或冒烟结果

**改规范**（危险，影响所有工具包）：

1. 先改 `manifest-v1.schema.json`（机读），再同步 `manifest-v1.md`（人读）——两者不准不同步
2. 若属破坏性变更，必须升 `spec` 版本号并在规范里写迁移说明
3. 重跑校验，确认**所有**已有工具包仍然通过；不通过则修工具包或回退规范
4. 提交：`docs(spec): ...` 或 `feat(spec)!: ...`（破坏性加 `!`）

**改代码（Phase 2 起）**：

1. `ArgvBuilder` 必须带单元测试（它是最容易错、最该被测的组件）
2. 提交前 `dotnet build` 零警告
3. UI 变更无法自动验证，验收靠"运行 + 截图反馈"

---

## 12. 上下文丢失后的恢复流程

Token 额度重置、会话中断、换机器之后，照这个顺序，15 分钟内可重建完整上下文：

```
1. cd D:\AI\swpj && git pull                    # 取最新状态
2. 读 AGENTS.md                                  # 硬规则（1 分钟）
3. pwsh -File scripts/check-env.ps1              # 环境漂移报告；有 DRIFT 就先处理
4. 读 docs/ai/project-state.json                 # 已有产物、未决问题、下一步
5. 读本文档 §8（陷阱）与 §7（关键决定）           # 避免重犯已解决的错误
6. 若任务是新增工具包 → 读 playbook-tool-package.md
7. 动手前先跑一次校验，确认基线是绿的
```

**不要做的事**：不要凭记忆断言某个版本号或路径（跑 `check-env.ps1`）；不要跳过校验就提交；不要在没读过 §8 的情况下改动 `scripts/validate-plugins.py`（那里面几条看似多余的规则都是踩坑换来的）。
