# 7-Zip 工具包说明（plugins/7zip）

## 参数知识来源

全部来自官方帮助文件 `7-zip.chm`（7-Zip 26.03）中的 **Command Line Version** 章节：

- 本机路径：`%USERPROFILE%\scoop\apps\7zip\26.03\7-zip.chm`
- 反编译 + 转 Markdown 的产物：`docs/reference/7zip-md/`（生成方法见 `docs/reference/README.md`）
- 逐命令开关矩阵：`docs/reference/7zip-switch-matrix.json`

## 覆盖范围

11 个命令全部覆盖，共 64 个字段：

| 动作 | 命令 | 字段数 | 危险级别 |
|---|---|---|---|
| `add` 添加到压缩包 | `a` | 14 | overwrite |
| `extract` 解压（保留路径） | `x` | 11 | overwrite |
| `extract-flat` 解压（平铺） | `e` | 6 | overwrite |
| `list` 查看内容 | `l` | 6 | none |
| `test` 测试完整性 | `t` | 4 | none |
| `hash` 计算哈希 | `h` | 5 | none |
| `delete` 从压缩包删除 | `d` | 4 | **destructive** |
| `rename` 压缩包内重命名 | `rn` | 3 | none |
| `update` 更新压缩包 | `u` | 7 | overwrite |
| `benchmark` CPU 性能测试 | `b` | 4 | none |
| `info` 查看支持的格式 | `i` | 0 | none |

## 故意没做的部分（以及原因）

1. **`-m` 家族的格式专属参数**：`-m0=BCJ2`、`-mf=Delta`、`-mb0:1`、`-md25` 这类写法区分格式、区分层级，
   文档篇幅最大（`switches/method.htm` 52 KB），而且绝大多数用户一辈子用不到。
   目前只暴露了通用且常用的 `-mx`（压缩级别）与 `-mhe`（加密文件名）。
2. **`-si` / `-so`（标准输入输出）**：需要宿主支持流式管道，v1 的规范刻意不含管道能力。
   涉及的动作是 `a -si` / `x -so`。
3. **冷门开关**：`-stx`（排除归档类型）、`-stl`、`-ssw`、`-spm`、`-spf`、`-scc`、`-scs`、`-sa`、`-sni` 之外的 NTFS 细节、
   `-slp`、`-sse`、`-snl/-snh/-snld` 等。它们已在白名单里，需要时加字段即可。
4. **`-t*` 与 `-t#` 这两个特殊归档类型**：`*` 表示"打开顶层归档并自动探测子文件"，`#` 表示"只打开顶层归档"。
   它们只在处理嵌套归档时有意义，`-t` 的下拉里没暴露。
5. **`-p` 不带密码的交互模式**：文档说"只写 `-p` 不给密码时 7-Zip 会提示输入"。
   这个模式会让子进程挂在等待输入上，本工具包明确不使用——密码要么在界面上填，要么不加 `-p`。

## 需要注意的坑（重要）

0. **7-Zip 只在真控制台里画进度**（实测：被重定向时 40 MB 输入的完整输出只有 354 字节，一个 `%` 都没有）。
   因此本清单设了 **`usePseudoConsole: true`**：宿主给子进程分配一个伪控制台（ConPTY），
   7z 才会输出百分比，界面进度条才会走动。已实测验证：界面冒烟里 48 MB 样本解压时进度条走到 100。
   代价与注意点：
   - 输出里会混入 ANSI 转义序列（宿主用 `AnsiText` 清理后再显示）
   - 任务太小（秒级）时 7z 来不及画进度，界面会提示"没有解析到百分比进度"，这是正常的
   - 定位这个行为的完整过程（包括为什么在测试宿主里 ConPTY 拿不到输出）见 docs/ai/development.md 的 P19

1. **7-Zip 不使用系统通配符规则**。`*.txt` 匹配所有 txt，但 `*.*` **不等于**"所有文件"——
   它只匹配"有扩展名的文件"。要处理全部文件必须用 `*`。这是最容易让人误判行为的地方。
2. **`e` 命令会平铺所有文件到同一目录**，同名文件互相覆盖。所以它的默认覆盖策略设为 `-aos`（跳过），而不是 `x` 用的 `-aoa`。
3. **`-o` 与 `-y` 只能用于解压命令**（`x` / `e`）。这条约束由 CI 的白名单校验强制，写错会直接失败。
4. **`-v`（分卷）只能用于 `a` 命令**，而且文档明确警告：分卷未完成前不要移动或复制它们。
5. **`x` / `e` 默认解压到当前目录**，因此这两个动作用 `workingDirectory: outputDir` 覆盖工具包级的 `inherit`。
6. **`-aoa` 会无提示覆盖**。界面里"已存在文件时"的默认值对 `x` 取 `-aoa`、对 `e` 取 `-aos`，是刻意的差异。

## 真机冒烟测试结果（2026-09-27，7-Zip 26.03）

`manifest.yaml` 里 `examples` 的 10 条命令已在本机逐条实跑，全部 `exit=0`：

| 动作 | 命令 | 结果 |
|---|---|---|
| `a` 打包子目录（保留前缀） | `7z a archive1.zip sub\` | exit 0，归档 279 字节 |
| `a` 只存内容 | `7z a archive2.zip .\sub\` | exit 0 |
| `a` 递归收集 txt | `7z a Files.7z *.txt -r` | exit 0 |
| `a` 仅存储 jpg | `7z a archive.zip *.jpg -mx0` | exit 0 |
| `l` 查看内容 | `7z l archive1.zip` | exit 0 |
| `t` 测试完整性 | `7z t archive1.zip` | exit 0 |
| `h` 只输出 SHA256 | `7z h -scrcsha256 -slfh -ba a.txt` | exit 0 |
| `x` 解压 | `7z x archive1.zip -oout -aoa` | exit 0，得到 `out\sub\b.txt`（**目录结构保留**，符合预期） |
| `e` 平铺解压 | `7z e archive1.zip -oflat -aos` | exit 0，得到 `flat\b.txt`（**结构被抹平**，符合预期） |
| `d` 删除归档内文件 | `7z d archive.zip *.jpg -r` | exit 0，归档缩到 22 字节 |

顺带确认了 `x` 与 `e` 的关键差异在真实文件系统上成立，以及默认覆盖策略（`x` 用 `-aoa`、`e` 用 `-aos`）能跑通。

### 冒烟测试发现的坑

**`7z i` 的输出第一行是空行**：

```
(空行)
7-Zip 26.03 (x64) : Copyright (c) 1999-2026 Igor Pavlov : 2026-09-03
(空行)
(空行)
Libs:
 0 : 26.03 : %USERPROFILE%\scoop\apps\7zip\current\7z.dll
```

所以 `versionPattern` 写成 `^7-Zip\s+([0-9.]+)` 时匹配不到任何东西（`^` 只在整段文本开头生效），
必须写成 `(?m)^7-Zip\s+([0-9.]+)`。这条经验已经反馈进规范：`versionPattern` 明确要求按多行语义匹配。

## 还没做的

- `b`（基准测试）的 examples 没跑——`7z b` 会持续压测 CPU 十几秒以上，不适合放进快速冒烟，留给手工验证。
- `-v` 分卷、`-sfx` 自解压需要 `7zCon.sfx` 等辅助模块与 `7z.exe` 同目录，是否要在规范里加 `requirements` 待定。
- `rn` 命令的成对参数目前用 `textarea` + `positionalMode: perLine`（每行一对）表达，界面上是一张映射表。
  如果以后觉得别扭，可以考虑给规范加一个真正的 `pairs` 字段类型。
