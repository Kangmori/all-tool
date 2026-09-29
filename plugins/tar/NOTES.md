# tar（bsdtar）工具包 —— 实测记录

> 共享事实（Windows 自带命令的通用坑：退出码不是"0 = 成功"、帮助可能写到 stderr、输出编码不统一、
> URL 不能猜规律）见 ``docs/ai/windows-commands.md``。
> 本文件只写 tar 特有的部分。

## 1. 参数知识来源（R1 / R2）

| # | 来源 | 位置 | 取得日期 | 适用版本 |
|---|---|---|---|---|
| 1 | **Microsoft Learn 官方文档** —— "tar on Windows" | https://learn.microsoft.com/en-us/windows/tar/ | 2026-09-29 | Windows 10 1803+ |
| 2 | **bsdtar 官方手册**（FreeBSD `tar(1)`，微软页面亲自指向它） | https://man.freebsd.org/cgi/man.cgi?query=tar&sektion=1&manpath=FreeBSD+14.3-RELEASE | 2026-09-29 | bsdtar 3.8.x |
| 3 | 本机 `tar --help`（1376 字节，退出码 0） | `%SystemRoot%\System32\tar.exe`（**快照不入库**，随时可重取） | 2026-09-29 | bsdtar 3.8.4 |
| 4 | 本机原型命令实跑（16 条 examples + 80 余条原型） | `%TEMP%\tar-smoke*`（**不入库**） | 2026-09-29 | bsdtar 3.8.4 |

### 1.1 官方文档 URL 不能猜规律（这次踩到了）

任务里给的 `https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tar`
**实测返回 404**（同时试了 `.../tar-1`，也是 404）。仍在线的官方页是
`https://learn.microsoft.com/en-us/windows/tar/`（"tar on Windows"，
HTTP 头实测 `Last-Modified: Mon, 24 Aug 2026 14:37:31 GMT`）。
这与 playbook 附 C 记的 `powercfg` 同类：**每个 URL 都必须实际请求确认**。

该页的定位是"发现性文档"而不是选项参考（它只给四个常用示例），并且**它自己写明**：
"For the full list of options, run `tar --help` or the **FreeBSD Tar manual**"。
所以本包的选项语义一律引 FreeBSD 手册，Windows 特有的事实（自带、路径、`-a` 取代 `-z/-j/-J/-Z`）
引微软页面。

### 1.2 两处"官方文档里没有 / 对不上"的地方（如实记录）

**(a) `--mtime` 与 `--clamp-mtime` 不在 FreeBSD 手册的这一版里。**
本机 `tar --help` 明确列了这两个开关，实跑也正常（见 §5 测试 19–20、SS/TT），
但 `man.freebsd.org` 的 FreeBSD 14.3 `tar(1)` 全文搜不到 `mtime` 选项条目。
它们出现在更新的 libarchive 手册里（例如 Debian unstable 的
`libarchive-tools` 3.8.9 `bsdtar(1)`：`--mtime date` / `--clamp-mtime`）。
**处理方式**：不算"两边都有"，所以按最保守的口径**没有写进清单**（`create` 里没有 mtime 字段），
只在 §5 记下实测事实，供以后升级出处时补。

**(b) `-a` 与显式压缩选项同时给出时，手册的说法与本机实测不符。**
FreeBSD 手册 `-a` 一节原文：`tar -a -jcf archive.tgz source.c source.h` **ignores the "-j" option**。
本机实测 `tar -caj g.zip`（bundle 形式）**退出码 1，归档根本没生成**。
**处理方式**：把 `-a` 与 gzip/bzip2/xz/lzma/compress 做成**互斥的枚举选项**，
这样清单永远不可能生成 `-a` 与 `-j` 并存的命令；并把差异记在这里，不按手册写"会忽略"。

## 2. 环境事实（实测，R7）

| 项 | 值 |
|---|---|
| 可执行文件 | `C:\Windows\System32\tar.exe`（`Get-Command tar` → `C:\Windows\system32\tar.exe`） |
| 帮助原文里的自称 | `tar.exe(bsdtar): manipulate archive files` |
| 版本输出 | `bsdtar 3.8.4 - libarchive 3.8.4 zlib/1.2.13.1-motley liblzma/5.8.1 bz2lib/1.0.8 libzstd/1.5.7 cng/2.0 libb2/bundled`（**末尾有一个空格**） |
| 文件版本 | `3.8.4 (WinBuild.160101.0800)`；ProductVersion `10.0.26100.7623`；FileDescription `bsdtar archive tool` |
| 是否随系统提供 | 是。Windows 10 1803（2018-04）起自带 |
| 系统 | Windows 11 26200 / 25H2（`[Environment]::OSVersion` = 10.0.26200.0） |
| 管理员权限 | **不需要**（本包所有动作都在用户目录里读写） |
| `--version` 退出码 | 0；`tar --help` 退出码 0（帮助写到 **stdout**） |
| 支持的后缀（`-a` 实测） | 见 §5 测试 01–14 与 `-a` 后缀表 |

## 3. 覆盖范围

共 **11 个动作 / 37 个字段**；字段出处标注 **37/37 = 100%**。

| 动作 | 命令 | 字段数 | 风险 | 类别 |
|---|---|---|---|---|
| `version` | `--version` | 0 | 低 | 查看 |
| `list` | `-t` | 3 | 低（只读） | 查看 |
| `extract-text` | `-x -O` | 3 | 低（不落盘） | 查看 |
| `extract` | `-x` | 4 | **overwrite** | 解压 |
| `extract-keep` | `-x -k` | 4 | overwrite | 解压 |
| `extract-single` | `-x [patterns]` | 3 | overwrite | 解压 |
| `extract-keep-paths` | `-x -P` | 3 | **destructive + confirmPhrase** | 解压（危险） |
| `create` | `-c` | 5 | overwrite | 创建 |
| `create-exclude` | `-c --exclude/-X` | 6 | overwrite | 创建 |
| `append` | `-r` | 3 | overwrite | 追加/更新 |
| `update` | `-u` | 3 | overwrite | 追加/更新 |

引用的开关共 18 个：`-x -t -c -r -u -f -v -C -O -k -m(未收录) -p(未收录) -P -a -z -j -J -Z --lzma --exclude --exclude-from --strip-components`。
（`-m` 与 `-p` 只做了实测，最终没做成字段——见 §6。）

## 4. 编码：**不是 UTF-8，是 OEM 代码页（本机 936）**（实测，R7）

这是本包最容易写错的一条。用 `cmd /c "tar -tf test.tar > list-raw.bin"` 抓原始字节后：

```
归档里有一个名为「中文名.txt」的成员，tar -tf 输出的原始字节（片段）：
  2e 2f d6 d0 ce c4 c3 fb 2e 74 78 74 0d 0a
       ^^^^^^^^^^^^^^^^^^^ = D6D0 CEC4 C3FB
```

- `D6 D0 CE C4 C3 FB` **是 GBK(936) 的「中文名」**（本机 `chcp` = 936）；
- 用 `UTF8Encoding(throwOnInvalidBytes: true)` 解同一段字节**抛异常**
  （`Unable to translate bytes [D6] at index 32`）——所以它**确定不是 UTF-8**；
- 用 `Encoding.GetEncoding(936).GetString(bytes)` 得到正确的 `./中文名.txt`。

`--version` 的输出是纯 ASCII（无所谓），但 `-tv` 的日期列、以及任何非 ASCII 文件名都会走这个码页。
所以 `runtime.encoding: **oem**`（宿主 `EncodingResolver` 把 `oem` 解析成
`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，本机即 936）。
**这一类 Windows 自带命令不能因为"输出是英文"就默认 utf-8。**

## 5. 真机冒烟测试（R4，2026-09-29）

测试目录：`%TEMP%\tar-smoke`、`tar-smoke2`、`tar-smoke3`、`tar-ext`、`tar-examples`。
**全部只在这些临时目录里操作，没有碰任何用户数据。** 原始 log 不入库。

### 5.1 清单里 16 条 examples 的忠实复跑（一条一条按 manifest 的 args 数组跑）

| # | 动作 | argv | 退出码 |
|---|---|---|---|
| 1 | version | `--version` | 0 |
| 2 | list | `-t -f test.tar.gz` | 0 |
| 3 | list | `-t -f test.tar *.txt` | 0 |
| 4 | extract-text | `-x -O -f test.tar ./a.txt` | 0 |
| 5 | extract | `-C out1 -x -f test.tar.gz` | 0 |
| 6 | extract | `-C out2 -x -f test.tar --strip-components 1` | 0 |
| 7 | extract-keep | `-C kout -x -f test.tar -k` | 0 |
| 8 | extract-single | `-C out3 -x -f test.tar ./a.txt` | 0 |
| 9 | extract-keep-paths | `-C out2p -x -P -f pabs.tar` | 0 |
| 10 | create | `-c -f test.tar -C src .` | 0 |
| 11 | create | `-c -z -f test.tar.gz -C src .` | 0 |
| 12 | create | `-c -a -f test.zip -C src .` | 0 |
| 13 | create-exclude | `-c -f noexcl.tar --exclude *.log -C src .` | 0 |
| 14 | create-exclude | `-c -a -f xexcl.zip --exclude-from ex.txt -C src .` | 0 |
| 15 | append | `-r -f test.tar -C src a.txt` | 0 |
| 16 | update | `-u -f test.tar -C src a.txt` | 0 |

产物复核（全部 PASS）：`test.tar` 存在；`test.tar.gz` 头字节 `1f 8b`；`test.zip` 头字节 `PK`；
`kout\a.txt` 内容仍是 `PRESERVED-BY-K`（`-k` 真的没覆盖）；`out1` 有解压出的目录树；
`out3` 只有 `a.txt`、没有 `b.log`（单成员解压真的只解一个）；`noexcl.tar` / `xexcl.zip` 里都没有 `b.log`。

### 5.2 **第一次跑失败了 4 条**：`-C` 不会替你建目录（本包重要的实测发现）

第一次跑 examples 时，**目标目录不存在的 4 条解压全部退出码 1**：

```
argv: -C out1 -x -f test.tar.gz
  exit: 1
  out : tar.exe: could not chdir to 'out1'
```

`out2` / `out3` / `out2p` 三条同理。**先 `New-Item -ItemType Directory` 建好目录后，同一条命令退出码 0。**
结论与处理：bsdtar 的 `-C` **只 chdir，不创建目录**（手册 `-C` 一节也没承诺创建）。
已在 `extract` / `extract-keep` / `extract-single` / `extract-keep-paths` 四个动作的
「解压到」字段 `help` 里写明"目标目录必须已存在，否则会 `could not chdir` 失败"，
并在清单的 `examples` 说明中体现了前置建目录。这是 **UI 层面会咬人的坑**，不是清单写错。

### 5.3 格式与压缩（`-a` 后缀表）

| 写法 | 退出码 | 文件头字节 | 结论 |
|---|---|---|---|
| `-caf a.zip` | 0 | `50 4b 03 04` | zip |
| `-caf b.tar.gz` / `c.tgz` | 0 | `1f 8b 08 00` | gzip |
| `-caf d.tar.bz2` | 0 | `42 5a 68 39` | bzip2（BZh9） |
| `-caf e.tar.xz` | 0 | `fd 37 7a 58` | xz |
| `-caf k.tar.zst` | 0 | `28 b5 2f fd` | zstd |
| `-caf l.tar.lzma` | 0 | `5d 00 00 80` | lzma |
| `-caf i.7z` | 0 | `37 7a bc af` | 7z（`tar -tf t.7z` 能列出成员） |
| `-caf m.tar.Z` | 0 | `1f 9d 90 78` | compress(LZW) |
| `-caf h.tar` / `f.xxx` | 0 | 第一个条目名开头 | 未压缩 tar（`tar -tf` 能正常列出） |
| `-caj g.zip` | **1** | — | **归档没生成**（`-a` 与 `-j` 并存会失败，见 §1.2(b)） |
| `-c -f fmt.zip --format zip` | 0 | — | 与 `-a` 等效（`--format` 本包未做成字段，见 §6） |
| `-cZf test.tar.Z` | 0 | — | 显式 `-Z` 可用 |

### 5.4 风险相关行为的实测证据

- **`-k` 是"跳过"而不是"报错"**：先在 `out4\a.txt` 写 `PRESERVED-BY-K`，再
  `tar -xf test.tar -C out4 -k` → **退出码 0**，`a.txt` 内容仍是 `PRESERVED-BY-K`；
  不加 `-k` 跑同一条 → `a.txt` 被覆盖成归档里的 `AAA-ORIGINAL`。
- **`-m` 真的是"不恢复 mtime"**：归档里条目 mtime 是 `2001-02-03 04:05:06`，
  不加 `-m` 解出来 `LastWriteTime = 2001-02-03T04:05:06`；
  加 `-m` 解出来 = 当前时间（`2026-09-29T22:43:24`）。
- **`-P` 关掉的是真检查，不是"仅影响绝对路径"**（测试目录 `tar-smoke3`）：
  构造一个成员名为 `c/../c/target.txt` 的归档 →
  `tar -xf dotdot.tar` **退出码 1**，输出 `c/../c/target.txt: Path contains '..': Unknown error`（文件没写出）；
  同一个归档 `tar -xPf dotdot.tar` → **退出码 0**，目标文件被真的写了出来。
  另一条：`tar -cf pabs.tar -P <绝对路径>` 存下的是 `C:/Users/...` 完整路径；
  不加 `-P` 时 tar 打印 `Removing leading drive letter from member names` 并改写成员名。
  这就是清单把它标成 **`danger: destructive` + `confirmPhrase`** 的实测依据。
- **`-r` 会产生重复条目**：`tar -rf test.tar -C src a.txt`（退出码 0）之后
  `tar -tvf test.tar` 里 `a.txt` **出现两次**——与手册"追加不去重"一致，
  已写进 `append` 的 `confirmText`。
- **归档不存在**：`tar -tf definitely-missing.tar` → 退出码 **1**，
  输出 `tar.exe: Error opening archive: Failed to open 'definitely-missing.tar'`。

### 5.5 versionPattern 实测（playbook 要求单独验证）

清单里的正则 `(?m)^bsdtar\s+([0-9.]+)` 对着本机 `--version` 的真实输出跑：
**捕获组 = `3.8.4`**（原始输出 `'bsdtar 3.8.4 - libarchive 3.8.4 … bundled \r\n'`，首行非空，与 7-Zip 的"首行是空行"不同）。
`minVersion: 3.5.0` 由 3.8.4 满足。

### 5.6 没冒烟的动作

**没有"没冒烟的动作"** —— 11 个动作全部有对应命令的实跑记录（§5.1 覆盖 examples，
§5.4 覆盖 `-k`/`-m`/`-P`/`-r` 的风险行为）。但有几条**限定条件**必须说清：

1. 冒烟是在**宿主之外**用 `subprocess` 直接拼 argv 跑的（因为本次不能改宿主、也不该启动 GUI）。
   宿主链路（表单 → ArgvBuilder → ProcessRunner）**没有**跑 tar；
   不过 tar 的字段形状与已验证的 robocopy/curl 包同类（`positional` + `flag` + `separate` + `literal`），
   且 `ArgvBuilder` 的展开规则与 §5.1 用的 argv 完全一致。
2. `extract-keep-paths` 的 examples 里 `args` 只有一条、且**没有 `expectExitCode`**：
   它的行为依赖"归档里是否真有绝对路径/`..` 成员"，无法用固定退出码断言；它跑的是 §5.4 那条
   `-C out2p -x -P -f pabs.tar`，退出码 0。
3. `-p`（保留权限/ACL）与 `-m` 只做了实测、**没做成字段**（见 §6），所以不算"动作"。
4. `update`（`-u`）的"只更新更新的文件"这条语义**没有构造出"文件比归档新"的确定性场景**再跑一遍：
   实测里源文件 mtime 与归档内条目相同，`-uvf` 仍然打印了文件名（即把条目写了一遍）。
   **所以"较旧的条目不写"这一半语义我没有直接观测到**，清单的 `description` 按手册原文描述，
   `confirmText` 也按手册原文写。这是本包最弱的一处实证。

## 6. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| `--format {ustar\|pax\|cpio\|shar}` | 官方有、本机实测可用，但"格式"与"压缩"是两组正交选项，做进同一个枚举会让用户以为是二选一。`-a`/显式压缩已覆盖全部常见用法；`--format` 留给以后的"高级选项"。 |
| `--mtime` / `--clamp-mtime` | **官方 FreeBSD 手册这一版里查不到条目**（见 §1.2(a)），只有本机帮助与实跑有。按"两边都有才写"的口径不收录。 |
| `-m`（不恢复修改时间） | 官方手册有、实测语义清楚，但做成字段会让"解压"这个动作的选项变多而收益很小（用户极少关心解压出来的 mtime）。 |
| `-p` / `--no-same-owner` / `--no-same-permissions` / `--numeric-owner` / `--acls` / `--xattrs` | 官方有，但都是"以 root/管理员身份恢复元数据"类选项；本工具包的用户场景是普通用户解压普通归档，**标 `requiresAdmin` 又会误导**（见规范 §2.5）。 |
| `-w`（逐条交互确认） | 实测是**交互式**的：会把问题打到 stderr 并等 stdin。宿主现在是 CreateProcess + 管道，没有交互回路（规范 §8"交互式提示 v1 不做"）。**本轮冒烟时它把脚本挂住了（120 秒超时进后台），我手工 Kill 掉的** —— 这也是"不要收录交互式开关"的实证。 |
| `-b`（块大小）、`@archive`、`-T/--files-from`、`--include`、`--exclude-vcs`、`--one-file-system`、`--newer*`、`-n/--no-recursion`、`--totals`、`-q`、`-s`、`-S`、`--chroot`、`--options`（`gzip:compression-level` 等） | 官方都有、部分本机也实测过；都不属于"常见解压/打包"路径。按 playbook"每条只暴露最常用的 3–8 个开关"的原则留给"自定义参数"输入框。 |
| `-o`（x 模式 = 用当前用户属主；c 模式 = `--format ustar` 的同义词） | **同一个字母在两种模式下含义不同**，做成字段必然误导。实测 `tar -cof o.tar` 可用，但不收录。 |
| 进度条 | bsdtar 不画进度条，被重定向时也不输出百分比 → 不写 `output.progress`（不凭印象编正则）。 |
| `nextSteps` | 没有一条"输出命中就该推荐下一步"的规则能被真实输出验证（例如列出内容之后推荐解压，无法从输出里判断用户意图）。按规范 §2.2 第 2 条，**不写**好过写一个 0 命中的正则（scoop 的 `Updates are available` 就是这么踩的）。 |
| `quickActions` | tar 没有"某个动作是天然入口"的性质（不像 scoop 的 status→update），不写。 |
| `execution: terminal` / `info` | 没有需要交互或会弹窗的动作；`-P` 虽然是 destructive，但**规范 §2.9 把"极高风险"定义为"不可逆地毁掉现有数据"**，而 `-P` 只是取消安全检查、归档内容本身正常解压。所以按中风险处理：`danger: destructive` + `confirmPhrase`（逐字确认），而不是降级成 `info` 把功能藏起来。 |

## 7. 这个工具的坑（会咬人的地方）

1. **`-C` 不建目录**（§5.2）。这是 UI 上最容易撞的失败。
2. **`-C` 的作用域是"从它开始往后"**：`-C dir` 之后的所有文件名（含 `-f` 的归档路径、含
   `--exclude` 的匹配）都相对新目录。本清单把 `-C` 的字段声明在归档/输入**之前**，
   于是生成的命令是 `-c -f out -C src .`——`-f` 在 `-C` 之前，归档路径仍相对原目录，这是**故意**的。
3. **`-r` 会产生重复条目**（§5.4），解压时后一份覆盖前一份。别指望它去重。
4. **`-r`/`-u` 只对未压缩的普通 `.tar` 有效**（手册明说）。对 `.tar.gz`/`.zip` 用会失败。
5. **输出不是 UTF-8 而是 OEM 码页**（§4）——只要文件名里有中文就能看出来。
6. **`-k` 失败时不报错**（退出码 0，只是跳过）——"解压成功"不等于"所有文件都出来了"。
   要确认请对着「查看归档内容」的清单核数。
7. **`-a` 与显式压缩开关同时给会失败**（`-caj` → 退出码 1）；清单已把两者做成互斥选项。
8. **`--version` 输出末尾有一个空格**（`… libb2/bundled ` + CRLF）。任何"整行相等"的比对都要注意。
9. **参数顺序**：`tar -x <members>` 里 members 是**最后**的位置参数；本清单把 `members` 字段
   声明在 `outputDir`/`archive`/`stripComponents` **之后**，正是为了满足这一点。
10. **Windows 上的绝对路径会被改写**：不加 `-P` 时 tar 打印
    `Removing leading drive letter from member names` 并把 `C:\...` 变成 `Users\...`。
    所以"打包绝对路径再解压"得到的位置与直觉不同（§5.4）。

## 8. 校验结果（R3）

```
[ OK ] plugins\tar\manifest.yaml  (11 动作 / 37 字段 / 字段出处标注 37 个 = 100%)
20/20 个 manifest 通过
字段出处覆盖率: 632/632 (100%)
```

（仓库当时共 20 个工具包通过；`schtasks` 是本次会话之前就在工作区里的未跟踪目录，不是本包产生的。）
