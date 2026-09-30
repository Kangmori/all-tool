# openssh 工具包实测记录

> **交付状态（2026-09-30，验收时补充）**
> 本包只交付**由 `ssh.exe` 自己执行**的动作（version / config-dump / algorithms / connect / run-command）。
> 另有 5 个动作（keygen-generate / keygen-public / keygen-fingerprint / keygen-passphrase / scp-copy）
> 需要 `ssh-keygen.exe` / `scp.exe`，但**当前规范只有包级 `locate.executable`、没有动作级覆盖**，
> 宿主会拿 `ssh.exe` 去跑它们（结果是用法错误）。按项目标准「做了但用不了的不许上线」，
> 这 5 个动作暂时移出交付清单，完整原文存档在本机专属库
> `host-dev/pending/openssh-full-10-actions/`，等 **N47**（动作级 `executable` 规范增强）落地后还原。
> 本文件下面关于它们的实测记录全部保留，是还原时的依据。


> 实测环境：Windows 11 build `10.0.26200`，账号 `<机器名>\steve（**标准用户，不是管理员**）`，
> 实测日期 **2026-09-30**，工具版本 **OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2**。
> 抓中文输出前统一设了 `$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8`，
> 跑 Python 前设了 `$env:PYTHONUTF8 = '1'`。

---

## 1. 参数知识来源

| # | 来源 | 用于 |
|---|---|---|
| 1 | OpenSSH 官方手册页 [`ssh(1)`](https://man.openbsd.org/ssh)、[`scp(1)`](https://man.openbsd.org/scp)、[`ssh-keygen(1)`](https://man.openbsd.org/ssh-keygen) | 每个开关的语义、语法行、退出状态；抓取日期 2026-09-30 |
| 2 | Microsoft Learn [OpenSSH for Windows 概览](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh-overview) / [安装与首次使用](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse) / [密钥管理](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_keymanagement) | Windows 端的安装位置、自带组件、密钥默认位置 |
| 3 | 本机 `C:\Windows\System32\OpenSSH\*.exe` 的用法输出 | 交叉验证开关是否存在（快照见 §2） |

**收录口径（R1）**：一个开关只有在**官方手册页**与**本机 usage 行**里都能找到时才写进清单；
两边对不上的记进 §6，两边都有的才收录。

> ⚠ 注意 `web_fetch` 在本机是可用的（与 `playbook §0.2` 里"web_fetch 不可用"的备注不一致），
> 本次的三份手册页就是用 `web_fetch` 抓的，返回 HTTP 200。**这只是一次实测观察**，未改文档。

---

## 2. 本机实际存在的可执行文件（`Get-ChildItem C:\Windows\System32\OpenSSH` 实测）

9 个 `.exe` + 2 个文本文件，文件版本**全部是 `9.5.5.1`**，
但 `ssh -V` 自报 `OpenSSH_for_Windows_9.5p2`（**两者不一致，见 §6**）。

| 文件 | 大小(B) | 在 PATH 里 | 本包是否覆盖 |
|---|---|---|---|
| `ssh.exe` | 1253888 | ✔ | **是（主可执行文件）** |
| `ssh-keygen.exe` | 862208 | ✔ | **是** |
| `scp.exe` | 431616 | ✔ | **是** |
| `sftp.exe` | 459264 | ✔ | 否（交互式会话程序，见 §7） |
| `ssh-add.exe` | 603648 | ✔ | 否（依赖 ssh-agent，本机没有，见 §7） |
| `ssh-agent.exe` | 554496 | ✔ | 否（常驻服务，本机未安装该服务，见 §7） |
| `ssh-keyscan.exe` | 667648 | ✔ | 否（只做联网采集，无远程主机可验，见 §7） |
| `ssh-pkcs11-helper.exe` | 513536 | ✔ | 否（被 ssh 调用的内部辅助程序） |
| `ssh-sk-helper.exe` | 652288 | ✔ | 否（被 ssh 调用的内部辅助程序） |
| `LICENSE.txt` / `NOTICE.txt` | 18934 / 36008 | — | — |

**声明**：本任务只核对并记录实际存在的文件，未改动、未安装、未删除该目录里的任何东西。

---

## 3. 输出编码：实测判定为 `utf-8`

**判定依据（用带中文的输出，看原始字节）**：

`ssh` 自身的帮助与版本输出**全是纯 ASCII**（`unknown option -- h`、`usage: ssh …`、
`OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2`），拿它们判编码会得出错误结论（playbook §10 坑 14）。
所以**必须构造中文样本**再按字节判定。做法：用 `ssh-keygen -C` 把中文写进密钥备注，
再用 `-l` / `-y` 把它读出来 —— 这两条命令会原样打印密钥文件里的备注字节。

```
$ ssh-keygen -t ed25519 -C 中文注释测试 -N "" -f %TEMP%\openssh-smoke\id_ed25519 -q
$ ssh-keygen -l -f %TEMP%\openssh-smoke\id_ed25519.pub
256 SHA256:Moidu0m75qhMWLhqSX+SaWz9CxMkbZIgH25PrsG1Fk8 中文注释测试 (ED25519)
```

公钥文件里首个非 ASCII 字节在第 81 字节，其后 12 个字节：

```
e4 b8 ad e6 96 87 e6 b3 a8 e9 87 8a e6 b5 8b e8 af 95
```

`e4 b8 ad` = U+4E2D「中」、`e6 96 87` =「文」……即**严格合法的 UTF-8**，
不是 cp936（「中」在 GBK 里是 `d6 d0`）。在 CMD/PowerShell 控制台按 UTF-8 解码也得到正确的
`中文注释测试`。故 `runtime.encoding: utf-8`。（未设 `$OutputEncoding` 时控制台会显示成乱码，
那是控制台解码代码页的问题，不是程序输出编码的问题。）

**顺带一条坑（写进 NOTES 而不是清单）**：文件路径里的非 ASCII 字符**不会**按原字节回显，
OpenSSH 会把它转义成八进制。实测：

```
$ ssh-keygen -l -f C:\...\中文密钥目录\不存在.pub
ssh-keygen: C:\\...\\openssh-probe\\\344\270\255\346\226\207\345\257\206\351\222\245\347\233\256\345\275\225\\\344\270\215\345\255\230\345\234\250.pub: No such file or directory
```

`\344\270\255` 是 `0o344`=228=`0xE4`、`0o270`=184=`0xB8`、`0o255`=173=`0xAD`，
合起来正是 UTF-8 的「中」。也就是说**字节仍是 UTF-8，只是路径被转义显示**；
另外行内路径里的反斜杠被写成了双反斜杠。做错误信息正则时要小心这两点。

---

## 4. 退出码实测（含失败路径）

`ssh` 官方文档的语义是「返回远端命令的退出码；出错时 255」，`scp` 是「成功 0，出错 >0」。
本机实测（数字都是真跑出来的）：

| 命令 | 退出码 | 输出 |
|---|---|---|
| `ssh -V` | **0** | 版本串走 **stderr**，stdout **0 字节** |
| `ssh -Q help` / `cipher` / `key` / `mac` / `kex` / `sig` | **0** | 走 stdout |
| `ssh -G localhost` | **0** | 走 stdout，4050 字节 |
| `ssh -h` | **255** | `unknown option -- h` + usage（走 stderr，519 字节） |
| `ssh`（不带参数） | **255** | usage（stderr，498 字节） |
| `ssh -G`（缺 destination） | **255** | usage |
| `ssh -G -p notanumber localhost` | **255** | `Bad port 'notanumber'` |
| `ssh -G -o NoSuchOption=1 localhost` | **255** | `command-line: line 0: Bad configuration option: nosuchoption` |
| `ssh -Q nosuchquery` | **255** | `Unsupported query "nosuchquery"` |
| `ssh -G -F C:\no\such\config x` | **255** | `Can't open user config file …` |
| `ssh -G -F none x` | **0** | 正常（`none` = 不读配置文件） |
| `ssh -p 2222 -o ConnectTimeout=1 192.0.2.1` | **255** | `ssh: connect to host 192.0.2.1 port 2222: Connection timed out` |
| `scp -h` | **1** | usage（stderr，283 字节） |
| `scp`（不带参数） | **1** | usage（stderr，225 字节） |
| `scp onefile`（只给一个路径） | **1** | usage |
| `scp -P2222 文件 192.0.2.1:/tmp/` | **255** | `ssh: connect to host … port 2222: Connection timed out` |
| `scp -P 2222 文件 192.0.2.1:/tmp/` | **255** | 同上 |
| `ssh-keygen -t ed25519 -N "" -f <路径> -q` | **0** | 无输出 |
| `ssh-keygen -y -f <私钥>` | **0** | 公钥一行 |
| `ssh-keygen -l -f <公钥>` | **0** | 指纹一行 |
| `ssh-keygen -l -v -f <公钥>` | **0** | 指纹 + ASCII 随机图 |
| `ssh-keygen -t bogustype …` | **255** | `unknown key type bogustype` |
| `ssh-keygen -l -f <不存在的文件>` | **255** | `No such file or directory` |
| `ssh-keygen -f <路径> -t ed25519 -N "" -q` | **0** | **键序无关**，见 §5 |
| `ssh-keygen -t ed25519 -q -N "" -C x <裸路径>` | **1** | `Too many arguments.` + usage |
| `ssh-keygen -h` / `-?` / `--help` | **1** | **都不打印参数表**（见 §6） |
| `ssh-add -l` | **2** | `Error connecting to agent: No such file or directory`（stderr，54 字节） |
| `ssh-agent -h` | **255** | `unable to start ssh-agent service, error :1058` |

对应的 `exitCodes` 只写了实际能区分语义的三个码：`0` / `1`（scp 用法错误）/ `255`（其余错误）。
**没有编造**官方文档里没写、本机也没测到的码。

**注意 `ssh -V` 的版本串写 stderr**：宿主按 stdout 取版本会拿到空串。本清单靠宿主同时合并两个流
（`docs/ai/windows-commands.md` §6.2 说明了宿主是合并读取的），故 `versionPattern` 能匹配上。

---

## 5. 字段风格与字段顺序（每个都两种写法试过）

### 5.1 字段风格

| 开关 | `attached`（单 token） | `separate`（双 token） | 本包选择 | 证据 |
|---|---|---|---|---|
| `ssh -p` | `-p2222` → `port 2222` ✔ | `-p 2222` → `port 2222` ✔ | **attached** | `ssh -G` 的 `port` 行两种写法都变成 2222，输出字节数完全相同（各 4052 B） |
| `ssh -l` | `-llocaluser` → `user localuser` ✔ | `-l someuser` → `user someuser` ✔ | **attached** | 同上，靠 `ssh -G` 的 `user` 行判定 |
| `ssh -i` | 无单 token 写法 | `-i <路径>` ✔ | **attached**（前缀紧贴值，即 `-iC:\...`）| 实测 `-i C:\nonexistent\id` 触发 `Warning: Identity file … not accessible`，说明值被认到 |
| `ssh -o` | `-oPort=2222` → `port 2222` ✔ | `-o Port=2222` → `port 2222` ✔ | **separate** | 两种都认，但值的形态是 `Key=Value`（含 `=`），用双 token 更清楚、也不会和开关注解混淆，故选 separate |
| `scp -P` | `-P2222` → `port 2222` ✔ | `-P 2222` → `port 2222` ✔ | **attached** | 两种写法都真的去连了 2222 端口（`connect to host 192.0.2.1 port 2222: Connection timed out`） |
| `scp -l` | `-l100` ✔ | `-l 100` ✔ | **attached** | 两种都能进入连接阶段（退出码同为 255、报文相同） |
| `ssh-keygen -t` | `-ted25519` → 生成成功 ✔ | `-t ed25519` ✔ | **attached** | 两种都生成了合法密钥（退出码 0） |
| `ssh-keygen -N` | `-N-f …` **✗** | `-N ""` ✔ | **separate（必须）** | 实测 `-t ed25519 -N -f <路径> -q` → `Too many arguments.`，**退出码 1**；改成 `-N ""` 就成功。这是本包唯一一个"只有一种写法可用"的字段 |

> `-N` 那条特别值得记：它看起来像"-N 的值可以紧贴"，但实测**不行**，
> 会把后面的 `-f` 当成密码、把路径当成多余参数。

### 5.2 字段顺序（这个坑很致命，会静默错传）

**铁律：所有开关字段必须声明在位置参数之前。** 手册页写明"第一个非开关参数之后的内容
一律作为远端命令传给服务器"，所以顺序写反**不会报错**，而是把 `-p 2222` 当成远端命令的一部分。

自检脚本（我自己写的离线 argv 展开器，按 `manifest-v1.md` §3/§4 的规则把字段展开成 argv，
再检查"位置参数之后是否还出现开关"）**第一版就抓到了这个错误**：当时 `config-dump` / `connect` /
`run-command` / `scp-copy` 四个动作都把 `destination` / `host` / `source` 声明在了最前面。
修正后自检通过，最终生成的命令行是：

```
version            ssh.exe -V
config-dump        ssh.exe -G -Fnone -p2222 -lsomeuser -iC:\nonexistent\k -o "Port=2222" localhost
algorithms         ssh.exe -Q key
connect            ssh.exe -v -v 192.0.2.1
run-command        ssh.exe -p2222 -ltestuser -iC:\nonexistent\id -o "BatchMode=yes" 192.0.2.1 uname -a -r
keygen-generate    ssh-keygen.exe -ted25519 -b3072 -Ctest@alltool -N "" -fC:\Temp\smoke\id_ed25519 -q
keygen-public      ssh-keygen.exe -y -fC:\Temp\smoke\id_ed25519
keygen-fingerprint ssh-keygen.exe -l -fC:\Temp\smoke\id_ed25519.pub -v
keygen-passphrase  ssh-keygen.exe -p -fC:\Temp\smoke\id_ed25519
scp-copy           scp.exe -P2222 -iC:\Temp\smoke\id_ed25519 -Fnone -o "ConnectTimeout=10" -l1000 -p -q C:\a.txt C:\b.txt user@example.com:/home/
```

**`ssh-keygen` 的一个额外硬要求**：`-f <路径>` 必须排在所有"无值开关"之后。
实测 `ssh-keygen -t ed25519 -q -N "" -C 注释 <裸路径>` → `Too many arguments.`（退出码 1），
而 `ssh-keygen -t ed25519 -C 注释 -N "" -f <路径> -q` → 退出码 0。
所以 `outputKeyfile` 字段被声明在该动作字段列表的**最后**。

**另一个实测发现**：同一个配置项给两次时**后者生效** ——
`ssh -G -o Port=2222 -p 99 localhost` 得到 `port 2222`，
`ssh -G -p 99 -o Port=2222 localhost` 得到 `port 99`。清单里各字段独立，没有写死冲突顺序。

---

## 6. 与官方文档/本机输出对不上的地方（如实记录）

| # | 现象 | 官方文档 | 本机实测 | 处理 |
|---|---|---|---|---|
| 1 | 文件版本 vs 自报版本 | — | 9 个 exe 的 `VersionInfo.FileVersion` 全是 **9.5.5.1**，而 `ssh -V` 自报 **9.5p2** | 清单 `appVersion` 两个都写；`versionPattern` 取 `ssh -V` 的 `9.5` |
| 2 | `ssh -Q` 的查询项少一个 | 手册页列了 `cipher-auth`、`key-ca-sign` 等 12 项；`ssh -Q help` 直译是"支持的查询项列表" | `ssh -Q help` 只输出 **11 行**，**没有 `key-ca-sign`** | 本清单只收录本机确实认的 11 项（少一个不报错、多一个会 exit 255） |
| 3 | `ssh -h` 不是帮助 | 手册页说 `-h` 没有定义 | `ssh -h` → `unknown option -- h` + usage，退出码 **255**（不是 0/1） | 清单不收录 `-h`；usage 行仍作为交叉验证来源 |
| 4 | `ssh-keygen` 拿不到参数表 | — | `-h` / `-?` / `--help` **三种都不打印 usage**：`-h` 直接进入"生成密钥"交互（打印 `Generating public/private ed25519 key pair.` 后停在 `Enter file in which to save the key`），`-?` 与 `--help` 打印 `unknown option` + usage | 参数表靠"故意给错参数"逼出来的 usage 行；见 §6.1 的⚠ |
| 5 | `ssh -R`（远程转发）本机 help 有、usage 有 | 手册页有 `-R` | 本机 usage 行 `[-46AaCfGgKkMNnqsTtVvXxYy]` **没有 `R`**，但 `ssh -Q help` 与手册页一致 | 本包**没有**收录 `-R`（本机 usage 无、且无远程主机可验） |
| 6 | `ssh -Z` | 手册页有 `-Z` | 本机 usage 行没有 `Z`；`ssh -Q help` 也没有对应项 | 不收录 |
| 7 | `scp` 的 usage 行与手册页 | 手册页 `scp [-346ABCOpqRrTv] …` | 本机 usage 行**逐字相同** | 无差异，正常收录 |
| 8 | `ssh-add` 能否离线工作 | 手册页把 `-l` 描述为"列出已加载的身份" | 本机没有 ssh-agent，`ssh-add -l` / `-L` 全部 → `Error connecting to agent: No such file or directory`，退出码 **2** | 不收录 ssh-add（见 §7） |
| 9 | `ssh-agent` 是服务 | Microsoft Learn 说 Windows 上 ssh-agent 是可选服务 | 本机未安装该服务：`ssh-agent -h` → `unable to start ssh-agent service, error :1058`，退出码 **255** | 不收录（见 §7） |

### 6.1 ⚠ 必须申报的一次意外交互

第一次探路时执行了 `ssh-keygen.exe -h`，以为它和别的 OpenSSH 程序一样打印 usage，
**结果它直接开始生成密钥对**，打印：

```
Generating public/private ed25519 key pair.
Enter file in which to save the key (C:\Users\Steve/.ssh/id_ed25519):
```

因为它是在被重定向 stdio 的环境里启动的（读不到输入），它没有等到答案就退出了（退出码 1）。
**立刻核查了是否落盘**：`Test-Path $env:USERPROFILE\.ssh` → `False`（该目录**根本不存在**），
`$env:USERPROFILE` 下也没有任何今天的 `.ssh` 内容。**确认没有写入任何文件**。
之后所有探测都改用 `ssh-keygen -?` / `--help`，并在 §6 第 4 条记下了这个差异。

---

## 7. 覆盖范围与"故意没做"

| 动作 | 可执行文件 | 类型 | 字段数 | 危险级别 | execution |
|---|---|---|---|---|---|
| `version` | ssh.exe | 本地只读 | 0 | — | run |
| `config-dump` | ssh.exe | 本地只读（不联网） | 6 | — | run |
| `algorithms` | ssh.exe | 本地只读 | 1 | — | run |
| `connect` | ssh.exe | 联网 + 交互 | 7 | — | **terminal** |
| `run-command` | ssh.exe | 联网 + 交互 | 7 | — | **terminal** |
| `keygen-generate` | ssh-keygen.exe | 写文件 | 6 | overwrite | **terminal** |
| `keygen-public` | ssh-keygen.exe | 本地只读 | 1 | — | run |
| `keygen-fingerprint` | ssh-keygen.exe | 本地只读 | 2 | — | run |
| `keygen-passphrase` | ssh-keygen.exe | 改文件 | 1 | overwrite | run |
| `scp-copy` | scp.exe | 联网 + 交互 + 覆盖 | 11 | overwrite | **terminal** |

合计 **10 个动作 / 37 个字段 / 字段出处 37 个 = 100%**。

### 7.1 哪些动作是 `execution: terminal`，为什么

| 动作 | 为什么不能用 `run` |
|---|---|
| `connect` | 会问 `Are you sure you want to continue connecting (yes/no)?`、会提示 `user@host's password:`，成功后进入**交互式 shell** —— 宿主捕获输出的模式下这些提示没人回答，stdin 一关就退出，等于跑了个废物。官方文档 VERIFYING HOST KEYS 一节写明首次连接要用户确认指纹。 |
| `run-command` | 认证环节同样要输密码/确认指纹；远端命令本身也可能要交互输入。 |
| `scp-copy` | 官方文档原文：`scp will ask for passwords or passphrases if they are needed for authentication.` 另外 `scp` 的进度表只在真终端里才有意义。 |
| `keygen-generate` | 目标路径已有密钥时 ssh-keygen 会问 `Overwrite (y/n)?`；不填 `-N` 时它要交互读口令。**实测证据**：在没有可读 stdin 的环境里跑 `ssh-keygen -h`（等价于不带 `-f` 直接跑），它打印完第一个提示就退出了（退出码 1）—— 宿主捕获模式的真实表现就是这样。 |

`keygen-passphrase`（改口令）保留在 `run`：本机实测 `-p -f <不存在的文件>` 会**立刻报错退出 255、
不进入口令交互**，所以它在"文件存在"的正常路径上确实需要交互，但清单里无法据此改成 terminal
而不影响"给错路径就想看报错"这个真实用法；**这一条是本次的取舍**，见 §9 未验清单。

### 7.2 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| `sftp.exe` | 它是**交互式会话程序**（`sftp> ` 提示符、内部命令 `get`/`put`/`ls`）。v1 的 `session` 机制是"脚本重放"，而 sftp 的会话必须保持长连接、还要回显进度，属于规范 §2.8 明确留给 `execution: terminal` 的那类；但把它做成包又需要 `locate.executable: sftp.exe`（一个包只能有一个可执行文件），所以本包不带它。 |
| `ssh-add.exe` | 本机没有 ssh-agent（实测 `-l` → 退出码 2），**无法验证**任何一条正常路径；`ssh-add` 的价值几乎全在"配合 agent"，离线没有可验的行为。 |
| `ssh-agent.exe` | 它是常驻服务（Windows 上是可选服务 `ssh-agent`，本机未安装：`error :1058`）。把它当"跑一下就结束"的命令来包装是错的，且无法验证。 |
| `ssh-keyscan.exe` | 唯一用途是**联网采集主机公钥**。本机没有可用的远程主机，且任务明确要求"不要真的连任何外部主机"，所以**无从验证**。它的参数（`-p` / `-T` / `-t` / `-f`）本机 usage 行里都有，将来有远程主机时很容易补上。 |
| `ssh-pkcs11-helper.exe` / `ssh-sk-helper.exe` | 不是给用户直接调用的：它们被 `ssh` 按需拉起，命令行协议是内部的（`-O` / `-P` 之类的私有参数），不是"用户想点一下"的东西。 |
| 端口转发族（`-L` / `-R` / `-D` / `-W` / `-w` / `-g` / `-N` / `-f`） | **全部需要真实远端才能验证**（本地 `-L` 也只能验证"监听起来了"，而 `-R` 更依赖远端 sshd 的 `GatewayPorts`）。按 R1 与"验不了的宁可不写"的口径，本包一个都没收录。其中 `-R` 还有 §6 第 5 条的版本差异。 |
| `-A` / `-a`（agent 转发）、`-K` / `-k`（GSSAPI）、`-X` / `-x` / `-Y`（X11 转发） | 依赖 agent / Kerberos / X 服务器，本机都没有；Windows 上 X11 转发的实际价值也很低。 |
| `-O`（scp 用老 SCP 协议）、`-3` / `-R`（双远程互拷）、`-D`（本地 sftp-server 调试） | `-3` / `-R` 要两台远程主机；`-O` 与 `-D` 是排障用途，且 Windows 版 `ssh-keygen`/`scp` 的兼容行为未验。 |
| `ssh-keygen` 的证书族（`-s` / `-I` / `-n` / `-V` / `-z` / `-L` / `-M` / `-Y` / `-Q` / `-k` / `-A`）、`-i` / `-e` / `-m`（格式互转）、`-B` / `-D` / `-F` / `-H` / `-R` / `-r`（known_hosts 维护）、`-a` / `-w` / `-Z` | 都是真实的开关（本机 usage 行与手册页都有），但**大多要么需要 CA/证书基础设施、要么需要真实的 known_hosts 文件、要么是"改系统"的操作**。本包优先做"一个人第一次配 SSH 会用到的全部动作"，其余留给"自定义参数"输入框。其中 `-B`（bubblebabble 指纹）我用 `-B -f <密钥>` 试过，**退出码 0 但没有任何输出**，行为与手册页描述对不上，故不收录。 |
| `ssh -Q` 的"配置项别名"用法 | 手册页说 `ssh_config(5)` 里任何接算法列表的关键字都能当查询项（如 `Ciphers`）。这等于开了一个无穷的取值域，无法在表单里枚举，故只收录 `ssh -Q help` 列出的 11 项。 |

---

## 8. 真机冒烟测试结果（R4）

冒烟脚本用与 `validate-plugins.py` 相同的 `Process` + `ArgumentList` 方式启动（不经 shell、
argv 以数组传递），逐条比对退出码。**26 条全部 PASS**：

```
===== 只读 / 本地动作 =====
PASS ssh.exe        exit=0    expect=0    | -V OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2
PASS ssh.exe        exit=0    expect=0    | -G localhost
PASS ssh.exe        exit=255  expect=255  | -G
PASS ssh.exe        exit=255  expect=255  | -G -p notanumber localhost
PASS ssh.exe        exit=255  expect=255  | -G -o NoSuchOption=1 localhost
PASS ssh.exe        exit=0    expect=0    | -Q help / -Q key / -Q mac / -Q kex / -Q sig
PASS ssh.exe        exit=255  expect=255  | -Q nosuchquery
PASS ssh.exe        exit=0    expect=0    | -G -F none localhost
PASS ssh.exe        exit=255  expect=255  | -G -F <不存在的配置文件> x
PASS ssh-keygen.exe exit=255  expect=255  | -t bogustype -N "" -f <路径> -q
PASS ssh-keygen.exe exit=255  expect=255  | -l / -y / -p / -F 对不存在的文件
PASS scp.exe        exit=1    expect=1    | （不带参数）/（只给一个路径）
PASS ssh-add.exe    exit=2    expect=2    | -l（没有 agent）

===== 生成临时密钥 =====
PASS ssh-keygen.exe exit=0    | -t ed25519 -C 中文注释测试 -N "" -f <临时目录>\id_ed25519 -q
PASS ssh-keygen.exe exit=0    | -t rsa -b 3072 -N "" -f <临时目录>\id_rsa -q
PASS ssh-keygen.exe exit=0    | -l -f <临时目录>\id_ed25519.pub
PASS ssh-keygen.exe exit=0    | -l -v -f <临时目录>\id_ed25519.pub
PASS ssh-keygen.exe exit=0    | -y -f <临时目录>\id_ed25519
生成的：id_ed25519 (411 B) / id_ed25519.pub (101 B) / id_rsa (2602 B) / id_rsa.pub (569 B)
私钥首行：-----BEGIN OPENSSH PRIVATE KEY-----（即 -N "" 确实生成了未加密私钥）

===== argv 能到达网络层（只打保留地址 192.0.2.1）=====
PASS scp.exe exit=255 | -P 2222 <密钥> 192.0.2.1:/tmp/   → connect to host 192.0.2.1 port 2222: timed out
PASS scp.exe exit=255 | -P2222 <密钥> 192.0.2.1:/tmp/    → 同上
PASS ssh.exe exit=255 | -p 2222 -o ConnectTimeout=1 192.0.2.1
PASS ssh.exe exit=255 | 192.0.2.1 -p 2222
```

### 8.1 冒烟里**唯一**真实联网的部分，与安全边界

为了证明 `-P` / `-p` 的值真的被程序认到了端口上（而不是只在 `ssh -G` 的配置回显里生效），
我允许自己连了一个地址：**`192.0.2.1`，即 RFC 5737 的 TEST-NET-1 保留网段**，
专门用于文档与测试，**在互联网上不存在、也不可路由**，所以连接必然失败
（实测 `Connection timed out`，退出码 255），**没有触及任何真实主机**。

> 中途因为默认超时是 75 秒，`scp` 的两条各等了 75 秒；后续 `ssh` 的探测都加了 `ConnectTimeout=1`。
> 所有涉及 192.0.2.1 的命令都**只**出现在冒烟脚本与 NOTES 里，清单一律用 `ConnectTimeout`
> 保护，且 `connect` / `run-command` / `scp-copy` 的 `examples` 里显式标了"仅演示 argv"。

另外 `ssh 192.0.2.1 -p 2222` 这条**没能在真实身份认证前验证"参数是否被当成远端命令"** ——
它在 TCP 阶段就超时了，还没走到远端命令。所以"顺序写反 = 静默错传"这个结论的**权威依据是官方文档**
（手册页 DESCRIPTION 一节：第一个非开关参数之后的内容作为远端命令），本机实测只证明了
"两种顺序都会去连 2222 端口、退出码同为 255"，两者一起构成结论。

### 8.2 复现方式

本次冒烟脚本是临时文件（跑完即删，未入库）。复现时按 §8 的清单逐条跑即可，判据是退出码；
其中"生成临时密钥"那几条请**务必**写到 `%TEMP%` 下的临时目录并当场删除，不要写 `~/.ssh`。
命令行预览的自检可以离线做：按 `docs/spec/manifest-v1.md` §3/§4 的规则把字段展开成 token 序列，
再检查"位置参数之后是否还有开关"——本次就是靠这个自检抓到 §5.2 那个错序 bug 的。

---

## 9. 没验的部分与原因

| 没验 | 原因 |
|---|---|
| **`connect`（交互式登录）** | **本机没有可用的远程主机。** 没有任何目标可以连，也没有密码/密钥能通过认证。**未端到端验证。** |
| **`run-command`（远端执行命令）** | 同上，无远程主机。**未端到端验证。** |
| **`scp-copy`** | 同上。只验证到"argv 让 scp 去连 192.0.2.1:2222 并超时失败"这一层，**真正的传输、覆盖行为、进度表都没验**。 |
| **`ssh-keygen -p` 换口令的正常路径** | 需要在真终端里回答三次口令提示。用 `-P` / `-N` 预置口令倒是可以非交互，但那就变成了"验宿主拼的 argv"，而清单里这两个开关**没有**收录（`keygen-passphrase` 只暴露了 `-f`），所以没有对应的 argv 可验。只验证了"给不存在的私钥会立刻退出 255"。 |
| **`keygen-generate` 的 `Overwrite (y/n)?` 分支** | 需要真终端回答。实测在没有可读 stdin 时 ssh-keygen 直接退出（退出码 1），无法在宿主捕获模式下验证覆盖询问。 |
| **`-v` / `-vv` / `-vvv`（`connect` 的详细模式）** | `ssh -G` 不产生调试输出（配置回显不是连接过程），所以没有可离线验证的路径；开关本身在本机 usage 与手册页都有，且手册页明确写了"最多三级"。**只验证了"参数被接受"（不报 unknown option），没验证输出内容**。 |
| **`ssh -G` 对非本机主机的解析** | 试了 `nonexistent.invalid`，得到与 `localhost` 同样形状的输出（exit 0），但**没试过任何真实远端**，所以"真实环境下的配置求值"未验。 |
| **`scp -r` / `-p` / `-q` / `-v` 的实际效果** | 只有连上真实主机才能看效果。argv 层面已验（不报 unknown option）。 |
| **宿主对 `execution: terminal` 的实际实现** | 本仓库只有清单与校验器；宿主（WinUI 3）代码不在这个工作目录里，**无法验证宿主是否真的用 `cmd /k` 打开真终端**。规范 §2.6 是这么规定的，清单按规范写。 |
| **`requiresAdmin`** | 见 §10。 |

---

## 10. `requiresAdmin`：按实测不标

**结论：整个包不标 `requiresAdmin`（既不写包级、也不写动作级）。**

依据：

1. OpenSSH **官方文档没有任何一处**说 ssh / scp / ssh-keygen 需要管理员权限
   （`ssh(1)` / `scp(1)` / `ssh-keygen(1)` 的手册页通篇没有提权限要求）。
2. 本机当前账号是**标准用户、不是管理员**，实测：`ssh -V`（0）、`ssh -G`（0）、`ssh -Q`（0）、
   `ssh-keygen` 在 `%TEMP%` 下生成/读取/删除密钥（全 0）、`scp` 进入网络连接阶段（255 = 连不通，
   不是权限错）—— **没有一条因为权限被拒绝**。
3. `ssh-keygen` 唯一可能触发权限问题的情形是**写到系统目录**
   （例如 `-f C:\ProgramData\ssh\ssh_host_rsa_key`，那是给 sshd 生成主机密钥的用法）。
   清单把 `outputKeyfile` 设计成用户填的**文本**字段（不是固定路径），
   所以"要不要提权"取决于用户填什么，**不能作为动作的静态属性**。
   按 `manifest-v1.md` §2.5 的口径「不确定就不标」——标错会让用户以为必须提权而放弃本来能用的功能。
4. `keygen-generate` 与 `keygen-passphrase` 标的是 `danger: overwrite`（会就地覆写/改写密钥文件），
   与提权无关。

> 若将来要给"生成 sshd 主机密钥到 `C:\ProgramData\ssh`"单独做一个动作，
> 那个动作应当标 `requiresAdmin: true`（`C:\ProgramData\ssh` 默认只有管理员可写）。
> 本包没有这个动作。

---

## 11. 临时对象申报

| 建了什么 | 位置 | 何时删的 | 怎么复核 |
|---|---|---|---|
| `openssh-probe` / `openssh-probe2` / `openssh-probe3` / `openssh-probe4` 四个探测目录 | `%TEMP%` 下 | 见下 | `Test-Path` |
| `openssh-smoke` 冒烟目录（含 `id_ed25519` / `id_ed25519.pub` / `id_rsa` / `id_rsa.pub` 四个临时密钥文件） | `%TEMP%\openssh-smoke` | 冒烟脚本最后一步 `Remove-Item -Recurse -Force` | 脚本自报 `临时目录还在吗: False` |
| 探测用的 `ssh_config_test` 配置文件 | `%TEMP%\openssh-probe2` 内 | 随目录一起删 | — |
| **生成的密钥有没有落到 `~/.ssh`？** | — | — | **没有**。`Test-Path $env:USERPROFILE\.ssh` → `False`（该目录自始至终不存在）。见 §6.1 的意外交互核查。 |

**复核命令与结果**（收尾时跑，已通过）：

```powershell
Test-Path "$env:TEMP\openssh-smoke"                    # False
Test-Path "$env:TEMP\openssh-probe"                    # False
Test-Path "$env:TEMP\openssh-probe2"                   # False
Test-Path "$env:TEMP\openssh-probe3"                   # False
Test-Path "$env:TEMP\openssh-probe4"                   # False
Get-ChildItem $env:USERPROFILE -Force -Filter '.ssh'   # 无输出
```

`plugins/openssh/` 目录下最终只留 `manifest.yaml` 与 `NOTES.md` 两个文件
（探测脚本与自检脚本都在收尾时删除，见 §12）。

---

## 12. 规范/宿主层面的观察（不改规范，只记录）

1. **一个包只能有一个可执行文件，动作级没有覆盖键。** 本包要同时覆盖 `ssh.exe`、`ssh-keygen.exe`、
   `scp.exe` 三个程序，但 `locate` 是包级的（`docs/ai/windows-commands.md` §1 也是这么说的：
   "一个 exe = 一个工具包"）。我的处理：`locate.executable: ssh.exe`（主），
   `alternativeNames: [ssh-keygen.exe, scp.exe]`。
   **代价要说清**：`alternativeNames` 的语义是"主可执行文件找不到时才退而求其次"，
   所以**正常情况下宿主只会用 `ssh.exe`**，那么 `keygen-*` 三个动作与 `scp-copy` 生成的命令
   在正常环境里**会由 `ssh.exe` 来执行**，结果当然是 ssh 报用法错误。
   **这是 v1 的真实局限，不是清单写错了。** 需要的规范增强是"动作级可执行文件覆盖"
   （例如给 action 加 `executable` 键）。本清单已把这个代价写进 `scp-copy` 的 `description`。
2. **动作级没有 `executeWith` 之类的键**，所以 `execution: terminal` 的具体实现
   （是 `cmd /k` 还是 `wt.exe`）完全由宿主决定，清单无法干预。
3. **校验器的"开关溯源"启发式对本包完全不生效**：它只拿 `docs/reference/<以包 id 开头>*/`
   与（仅当出处指向 `learn.microsoft.com/.../windows-commands` 时）`win-*` 目录当语料，
   `openssh` 两者都没有，所以 `load_reference_corpus` 返回空、直接跳过。
   本次报告里那句"检查 305 个开关，191 个能找到"**一个 openssh 开关都没包含**。
   本包的开关出处是靠人肉对照"官方手册页 + 本机 usage 行"得出的（§1），
   没有依赖那层启发式检查。
4. **`web_fetch` 在本机可用**（与 `playbook-tool-package.md` §0.2 的备注"web_fetch 在本机不可用"
   不一致）。本次三份手册页都是 `web_fetch` 抓的（HTTP 200）。这条备注建议更新，
   但不属于本次可改范围（`docs/**` 禁改）。
