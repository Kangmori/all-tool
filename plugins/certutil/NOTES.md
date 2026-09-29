# certutil 工具包 —— 实测记录

> **本批共用事实**（Windows 自带命令的一般性质、OEM 编码、`/?` 的退出码与流向差异等）见
> [`docs/ai/windows-commands.md`](../../docs/ai/windows-commands.md)。
> **但 certutil 有几处与最早那批 12 个包、也与 wevtutil 不同，别照抄它们的结论**：
> ① 官方文档是**一页到底**（每个动词一个 `### -verb` 小节，没有子页）；
> ② 输出编码实测是 **OEM(936)**，但**纯 ASCII 的输出判不出来**，必须用带中文标签的
> `-hashfile` / `-store` 才能判定；
> ③ **`-verb:<值>` 这种单 token 写法一律不认**（`exit 1` 未知参数）——
> 与 wevtutil 的"/开关:取值 只认单 token"**正好相反**，所以本包全部用两 token 的
> `separate` / `positional`；
> ④ **开关必须排在位置参数之前**：`-encode <in> -f <out>` → exit 1「参数太多」；
> ⑤ 失败**不是退出码 1**，而是 HRESULT（`0x80070002` = `-2147024894` 等），
> 而**动词帮助页本身返回 1**。
>
> 实测环境：Windows 11 `10.0.26200` x64，账号 `kangmori\steve`，**非管理员**
> （`chkdsk` / `powercfg` 那批已实测 `IsInRole(Administrator)` = `False`）。控制台代码页 `936`。
> 实测日期 **2026-09-29**。

---

## 1. 这个包为什么值得做

三件事是普通用户**真的会用、但不知道系统里已经有**：

| 想干的事 | 别人怎么做 | 其实一条命令 |
|---|---|---|
| 确认下载的文件没坏/没被换 | 装第三方哈希工具 | `certutil -hashfile <文件> SHA256` |
| 看看这台机器到底信任哪些根证书 | 点半天"证书管理器" | `certutil -store Root` |
| Base64 文本 ↔ 二进制 | 上网页在线转换（把内容发给陌生人） | `certutil -encode` / `-decode` |

都是零安装、零联网。`certutil` 另外还是整个 PKI 的管理入口（CA 配置、注册、备份、吊销…），
那些面向**证书服务器**的部分不是本包的目标，见 §9。

---

## 2. 参数知识来源（R1 / R2）

| # | 来源 | 位置 | 取得日期 | 适用版本 |
|---|---|---|---|---|
| 1 | **Microsoft Learn 官方文档（单页）** | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/certutil | 2026-09-29 | Windows 10/11、Server 2016–2025、Azure Local 2311.2+ |
| 2 | 本机帮助：`certutil -?` + 16 个动词的 `certutil <动词> -?`（hashfile / store / dump / decode / encode / addstore / delstore / asn / verify / verifystore / error / URLCache / viewstore / enumstore / encodehex / decodehex） | `C:\Windows\System32\certutil.exe`（快照**未入库**，随时可重取） | 2026-09-29 | certutil.exe 10.0.26100.8875 |
| 3 | 本包真机冒烟（36 条 argv + 28 条红线自检） | `plugins/certutil/smoke.ps1`（**本包自带，可复跑**） | 2026-09-29 | 同上 |

### 2.1 官方文档的形态

- `Invoke-WebRequest` 实测 **HTTP 200、正文 162 502 字节、`Last-Modified: Tue, 25 Aug 2026`**。
- **没有子页**：页内一节到底，每个动词一个 `### -verb` 小节，含「语法行 + Options + Where 说明」。
  所以本清单每个动作的 `sources` 都指这一页，靠 `note` 说明"该动作查的是哪一节"。
  （与 wevtutil 的形态相同，与 schtasks 的"主页只是索引 + 每个开关在子页"相反。）
- 页面顶部两条值得抄下来的官方原话：
  - Caution：`Certutil isn't recommended to be used in any production code and doesn't provide any
    guarantees of live site support or application compatibilities.`
  - `If certutil is run on a non-certification authority without other parameters, the command
    defaults to running the certutil -dump command.`（本机实测：`certutil -dump` → exit 0、32 字节）
- 官方还提到一个**本包没用到**的入口：
  `certutil -v -uSAGE`（大写 SAGE 区分大小写）能看到 `-?` 里隐藏的动词与选项。
  本包**没有**用它列出的隐藏项（§9 说明理由）。

### 2.2 每个动作 / 字段的两边核对方式

字段依据一律取官方页面的语法行与 Options 行；**行为**（默认值、退出码、输出格式）
一律以实测为准。两边对不上的逐条记在 §8。

---

## 3. 环境与版本（实测，R7）

| 项 | 值 | 怎么得到的 |
|---|---|---|
| 程序路径 | `C:\Windows\system32\certutil.exe` | `Get-Command certutil` → `Source` |
| 文件版本 | `10.0.26100.8875 (WinBuild.160101.0800)` | `(Get-Item ...).VersionInfo.FileVersion` |
| 产品版本 | `10.0.26100.8875` | 同上 |
| FileDescription | **`CertUtil.exe`**（不像 wevtutil 那样给中文描述） | 同上 |
| 帮助语言 | **中文**（官方页面是英文） | 实跑 |
| 动词总表 | `certutil -?` → **退出码 0**、4 454 字节、写 stdout | 实测 |
| 逐动词帮助 | 抽测 16 个动词的 `-?` → **全部退出码 1**、全部写 stdout（stderr 0 字节） | 实测 |
| 版本开关 | **没有**（`-?` 的动词表里没有版本项） | 见 §10 |
| 运行身份 | `kangmori\steve`，**非管理员** | `-addstore` 的提权报错反证（§5.6） |
| 输出编码 | **OEM 代码页 936** | 见 §4 |
| 选项写法 | **两 token**（`-store My`），单 token（`-store:My`）一律不认 | 见 §7.1 |

---

## 4. 编码：**OEM 代码页（本机 936）**（实测判定，R7）

判据来自 `-hashfile`（最容易复现，不需要任何证书）：

```
argv: -hashfile <probe.txt> SHA256
stdout 178 字节，其中 >0x7F 的字节 20 个，第一处在偏移 7
  原始字节（偏移 3..12）: 32 35 36 20 b5 c4 20 43 3a 5c
  按 cp936 解: "256 的 C:\"        ← 正确
  按 utf-8  解: "256 �� C:\"       ← 乱码
  严格 UTF-8 解码（UTF8Encoding(throwOnInvalidBytes: true)）:
    → 抛异常 "Unable to translate bytes [B5] at index 7 from specified code page to Unicode."
```

- `b5 c4` 是 GBK(936) 的「的」；同一行末尾的 `b9 fe cf a3` 是「哈希」。
- **严格 UTF-8 解码抛异常 → 确定不是 UTF-8**（对照：本批 schtasks 是 UTF-8、tar 与 wevtutil 是 oem）。
- 不是 UTF-16LE：整段输出里 **0 个 0x00 字节**，且按 UTF-16LE 解出来的首行是乱码。

**结论：`runtime.encoding: oem`**（宿主把 `oem` 解析成
`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`，本机 = 936；用 `oem` 而不是写死 `gbk`，
换英文 Windows（437）或日文 Windows（932）时同一份清单仍然正确）。

**顺带一个反面教训**：`-enumstore`、`-error`、纯 ASCII 路径的 `-hashfile` 输出**全是 ASCII**，
拿它们判编码会得出"随便哪个都行"的错误结论——**必须用带中文标签的输出判**（`-store` 也行，
它的段标签是「序列号 / 颁发者 / NotBefore / 使用者」）。

---

## 5. 真机冒烟测试（R4，2026-09-29）

复跑入口：`pwsh -NoProfile -File plugins/certutil/smoke.ps1`。

脚本自带**三层自检**（playbook §5.1）：

1. **红线自检**：28 条"改证书存储 / 改系统状态"的 argv（`-addstore` ×3、`-delstore` ×3、
   `-viewdelstore`、`-repairstore`、`-TPMInfo`、`-syncWithWU`、`-generateSSTFromWU`、
   `-URLCache * delete`、`-flushCache`、`-delkey`、`-DeleteHelloContainer`、`-setreg`、`-delreg`、
   `-importPFX`、`-exportPFX`、`-shutdown`、`-deleteEccCurve`、`-pulse`、`-dspublish`、
   `-deleterow`、`-backup`、`-restore`、`-UI`）**任一条被判为可跑就 `throw` 中止**；
2. **词法自检**：只读白名单里不许出现 `-addstore` / `-delstore` / `-viewdelstore` /
   `-repairstore` / `-importPFX` / `-exportPFX` 这些动词；
3. **执行前二次确认**：每条在真正启动前用**同一套判定**再确认一次。

判定用 `($argv -join "`0")` 做字符串比较，**不用 `[array] -eq`**（playbook §5.1 的血泪教训）。

### 5.1 只读动作逐条实测（**argv 按清单字段声明顺序展开**）

展开规则与宿主 `ArgvBuilder` 一致：`command` + 各字段按声明顺序
（`flag` → `prefix`；`positional` → 值本身）。

| # | 动作 / 形态 | argv（`certutil` 之后） | 退出码 | stdout | stderr | 首行 |
|---|---|---|---|---|---|---|
| 1 | `hash` | `-hashfile <probe.txt> SHA256` | 0 | 178 B | 0 | `SHA256 的 <路径> 哈希:` |
| 2 | `hash` | `-hashfile <probe.txt> SHA1` | 0 | 152 B | 0 | `SHA1 的 …` |
| 3 | `hash` | `-hashfile <probe.txt> MD5` | 0 | 143 B | 0 | `MD5 的 …` |
| 4 | `hash` | `-hashfile <probe.txt> SHA512` | 0 | 242 B | 0 | `SHA512 的 …` |
| 5 | `hash` | `-hashfile <probe.txt> SHA384` | 0 | 210 B | 0 | `SHA384 的 …` |
| 6 | `hash`（不填算法） | `-hashfile <probe.txt>` | 0 | 152 B | 0 | **`SHA1 的 …` → 默认算法是 SHA1** |
| 7 | `hash`（真实系统文件） | `-hashfile C:\WINDOWS\System32\msvcrt.dll SHA256` | 0 | 150 B | 0 | `SHA256 的 … 哈希:` |
| 8 | `error` | `-error 0x80070002` | 0 | 143 B | 0 | `0x80070002 (WIN32: 2 ERROR_FILE_NOT_FOUND) -- 2147942402 (-2147024894)` |
| 9 | `error` | `-error 5` | 0 | 104 B | 0 | `0x5 (WIN32: 5 ERROR_ACCESS_DENIED) -- 5 (5)` |
| 10 | `list-store` | `-store` | 0 | 3 619 B | 0 | `CA "中间证书颁发机构"` |
| 11 | `list-store` | `-store My` | 0 | 449 B | 0 | `My "个人"` |
| 12 | `list-store` | `-store Root` | 0 | 9 963 B | 0 | `Root "受信任的根证书颁发机构"` |
| 13 | `list-store` | `-store CA` | 0 | 3 619 B | 0 | `CA "中间证书颁发机构"` |
| 14 | `list-store` | `-store TrustedPublisher` | 0 | 2 032 B | 0 | `TrustedPublisher "受信任的发布者"` |
| 15 | `list-store` | `-store Root 0` | 0 | 471 B | 0 | `Root "受信任的根证书颁发机构"` |
| 16 | `list-store` | **`-user -store My`** | 0 | 4 561 B | 0 | `My "个人"`（当前用户存储） |
| 17 | `list-store` | **`-store -user My`** | 0 | 4 561 B | 0 | 同上（**两种位置都能用**） |
| 18 | （未收录） | `-enumstore` | 0 | 1 127 B | 0 | `  (CurrentUser: -user)` |
| 19 | （未收录） | `-user -enumstore` | 0 | 893 B | 0 | `CurrentUser: -user` |
| 20 | （未收录） | `-verifystore Root` | 0 | 11 203 B | 0 | `Root "受信任的根证书颁发机构"` |
| 21 | （未收录） | `-verifystore Root 0` | 0 | 572 B | 0 | 同上 |
| 22 | `dump`（.cer） | `-dump <导出的 probe.cer>` | 0 | 4 205 B | 0 | `X509 证书:` |
| 23 | `dump`（带签名的 PE） | `-dump C:\WINDOWS\System32\msvcrt.dll` | 0 | **155 423 B** | 0 | `…msvcrt.dll: 语言 04b00409 (1200.1033)  文件 7.0:26100.8246  产品 10.0:26100.8246` |
| 24 | `asn` | `-asn <导出的 probe.cer>` | 0 | 8 933 B | 0 | `0000: 30 82 03 6e  ; SEQUENCE (36e 字节)` |
| 25 | `verify` | `-verify <导出的 probe.cer>` | 0 | 1 723 B | 0 | `颁发者:` |
| 26 | `verify` | `-verify -user <导出的 probe.cer>` | 0 | 1 722 B | 0 | `颁发者:` |
| 27 | `decode` | `-decode <plain.b64> <decoded.bin>` | 0 | 64 B | 0 | `输入长度 = 22` |
| 28 | `encode` | `-encode <probe.txt> <encoded-new.b64>` | 0 | 64 B | 0 | `输入长度 = 13` |
| 29 | `encode`（覆盖自己的上一轮产物） | `-encode -f <probe.txt> <encoded.b64>` | 0 | 64 B | 0 | `输入长度 = 13` |
| 30 | `decode`（带 PEM 包裹的输入 + `-f`） | `-decode -f <wrapped.b64> <decoded-wrapped.bin>` | 0 | 64 B | 0 | `输入长度 = 78` |

**30 条只读/自建文件 argv 全部通过，stderr 全部 0 字节。**

> 表中的 `probe.cer` 是**脚本自己**用 `certutil -store Root 0 <probe.cer>` 从当前用户的 Root 存储
> **导出**的一个证书文件（只读操作），用来给 `-dump` / `-asn` / `-verify` 当输入。

### 5.2 失败路径的退出码（全部实跑，全部只读）

| argv | 退出码 | 无符号 | stdout 首行 |
|---|---|---|---|
| `-hashfile <不存在的文件> SHA256` | -2147024894 | 0x80070002 | `CertUtil: -hashfile 失败: 0x80070002 (WIN32: 2 ERROR_FILE_NOT_FOUND)` |
| `-hashfile <probe.txt> BOGUS` | **-805305819** | 0xD0000225 | `CertUtil: -hashfile 失败: 0xd0000225 (NT: 0xc0000225 STATUS_NOT_FOUND)` |
| `-store NoSuchStoreXYZ` | -2147024894 | 0x80070002 | `NoSuchStoreXYZ` 然后 `CertUtil: -store 失败: 0x80070002` |
| `-asn C:\WINDOWS\System32\msvcrt.dll`（PE 不是 DER） | **-2147024883** | 0x8007000D | `0000: 4d 5a  ; ??? (5a 字节)` 然后失败（**输出 3 364 453 字节！**） |
| `-verify C:\WINDOWS\System32\msvcrt.dll` | **-2146881269** | 0x8009310B | `LoadCert(Cert)返回了 ASN1 遇到了不正确的标记值。 0x8009310b (ASN: 267 CRYPT_E_ASN1_BADTAG)` |
| `-error zzz` | -2147024809 | 0x80070057 | `CertUtil: -error 失败: 0x80070057 (WIN32: 87 ERROR_INVALID_PARAMETER)` |
| `-hashfile`（缺参数） | **1** | 0x00000001 | `要求至少 1 个参数，但收到了 0 个` / `CertUtil: 找不到参数` |
| `-hashfile:<文件>`（不存在的写法） | **1** | 0x00000001 | `CertUtil: 未知参数: -hashfile:<文件>` |
| `-store:My` | **1** | 0x00000001 | `CertUtil: 未知参数: -store:My` |
| `-encode:foo bar` | **1** | 0x00000001 | `CertUtil: 未知参数: -encode:foo` |
| `-encode <in> -f <out>`（开关在后） | **1** | 0x00000001 | `要求不多于 2 个参数，但收到了 3 个` / `CertUtil: 参数太多` |
| `-encode <in> <已存在的 out>`（不加 `-f`） | -2147024816 | 0x80070050 | `EncodeToFile 返回了 文件存在。 0x80070050 (WIN32: 80 ERROR_FILE_EXISTS)` |
| `-decode <in> <已存在的 out>`（不加 `-f`） | -2147024816 | 0x80070050 | `EncodeToFile 返回了 文件存在。 0x80070050`（**注意它打的是 EncodeToFile**） |
| `-decode <不存在的输入> <out>` | -2147024894 | 0x80070002 | `DecodeFile 返回了 系统找不到指定的文件。 0x80070002` |
| `-encode <不存在的输入> <out>` | -2147024894 | 0x80070002 | `DecodeFile 返回了 系统找不到指定的文件。 0x80070002`（**同样是 DecodeFile**） |
| `-dump <不存在的文件>` | -2147024894 | 0x80070002 | `CertUtil: -dump 失败: 0x80070002` |
| `-asn <不存在的文件>` | -2147024894 | 0x80070002 | `DecodeFile 返回了 系统找不到指定的文件。 0x80070002` |
| `-verify <不存在的文件>` | -2147024894 | 0x80070002 | `DecodeFile 返回了 …` + `LoadCert(Cert)返回了 …` |
| `-store My -user`（**开关错位**） | **-2146893807** | 0x80090011 | `My "个人"` 然后 `CertUtil: -store 失败: 0x80090011 (-2146893807 NTE_NOT_FOUND)` |
| `-oid`（缺参数） | **1** | 0x00000001 | `要求至少 1 个参数，但收到了 0 个` |
| `-TPMInfo`（非管理员） | **-2147024156** | 0x800702E4 | `使用选择的选项需要管理员权限。使用管理员命令提示来完成这些任务。` / `CertUtil: 请求的操作需要提升。` |

**码的语义不是猜的**：清单的 `exitCodes` 每条都用 `certutil -error <码>` 复核过，例如

```
certutil -error 0x80070002
0x80070002 (WIN32: 2 ERROR_FILE_NOT_FOUND) -- 2147942402 (-2147024894)
错误消息文本: 系统找不到指定的文件。
certutil -error 0x80070050
0x80070050 (WIN32: 80 ERROR_FILE_EXISTS) -- 2147942480 (-2147024816)
```

**三条容易误判的行为**（已写进清单的字段 help 与 exitCodes）：

1. **`-hashfile` 不填算法 = SHA1**（实测），不是"随便挑一个"。
   清单把默认值显式写成 SHA256，并在 help 里写明"certutil 自己的默认是 SHA1"。
2. **`-asn` 传非 DER 文件会先吐一大堆十六进制再失败**（实测 3.3 MB）。
   界面上看到几兆输出之后才看到失败，不要以为是成功。
3. **`-store My -user` 不报"参数太多"，而是静默把 `-user` 当第二个位置参数**，
   于是拿 `-user` 去当 CertId 匹配 → 0x80090011 NTE_NOT_FOUND。
   这类"不报错、结果却是另一个东西"的错法最危险，所以本包所有 flag 字段都声明在位置参数**之前**（§7.2）。

### 5.3 `versionPattern` 的验证（playbook §5.2 情形①）

certutil **没有版本开关**（`-?` 的动词总表与各动词帮助里都没有版本项），
所以按 R1 **不写 `versionArgs` / `versionPattern`**（编一个不存在的版本命令等于违反 R1）。
替代方案是读 exe 的文件版本，实测：

```
(Get-Item C:\Windows\System32\certutil.exe).VersionInfo.FileVersion
→ 10.0.26100.8875 (WinBuild.160101.0800)
```

这个值写进了工具包级 `appVersion`。**代价**（与那批 12 个包相同）：`versionPattern` 为空时，
宿主 `ToolLocator` 会返回"未能解析出版本号"、界面执行时会在输出区打一句提示。
这是清单的诚实代价，不是写错了。

### 5.4 编码判定的可复跑证据

`smoke.ps1` 末尾会自动重跑 §4 那一段（原始字节 + cp936/utf-8 双解 + 严格 UTF-8 解码），
不需要手工记。输出样例：

```
-hashfile SHA256: 178 字节，>0x7F 的字节 20 个，第一处在偏移 7
  原始字节: 32 35 36 20 b5 c4 20 43 3a 5c
  按 cp936 解: 256 的 C:\
  按 utf-8 解: 256 �� C:\
  严格 UTF-8 解码：抛异常 => 确定不是 UTF-8（结论：runtime.encoding: oem）
```

### 5.5 `nextSteps.when` 正则实测

`hash` 动作写了一条 `nextSteps`（"校验这个文件的数字签名？先看它的证书" → `dump`），
正则用**宿主的语义**核对过：`Regex.Matches(text, pattern, RegexOptions.Multiline)`，
对着真实 `-hashfile msvcrt.dll SHA256` 的 OEM 解码文本跑：

```
命中 1 行；捕获组 path = [C:\WINDOWS\System32\msvcrt.dll]
```

**顺带一个坑（与 wevtutil 那条同类）**：certutil 的输出是 **CRLF**，
所以捕获组写成 `[^\r\n]+?` 而不是 `.`，末尾用 `\s+哈希:` 收口——`.+?` 也能跑通，
但 `[^\r\n]` 不依赖 `.` 是否匹配 `\r`，跨引擎更稳。

其它动作**不写** `nextSteps`：`-store` 的输出里序列号/使用者都能当锚点，
但"下一步该干什么"从输出里判断不出来（用户可能是来核对指纹的，也可能是来准备删除的），
按规范 §2.2 的口径——**不写 = 不推荐**，比猜一条要好。

### 5.6 改动类动作：只有"必定失败"的反例被实跑过

| argv | 退出码 | 说明 |
|---|---|---|
| `-addstore NoSuchStoreXYZ <probe.cer>` | **-2147024156** (0x800702E4) | `使用选择的选项需要管理员权限…` —— **在碰存储之前**就因为提权检查失败 |
| `-addstore My <probe.txt>`（内容不是证书） | **-2147024156** | 同上，提权检查优先于内容校验 |
| `-delstore My 0` | **-2147024156** | 同上 |
| `-delstore NoSuchStoreXYZ 0` | **-2147024156** | 同上（存储名不存在也一样先撞提权） |

这四条**只读性质的反例**说明两件事：

1. `-addstore` / `-delstore` 在**当前非管理员账号下必然失败**，且失败发生在真正改动之前
   → 清单里这两个动作的 `requiresAdmin: true` **有实测依据**（不是照文档抄的）；
2. **成功路径本轮没有实测**（红线：不碰系统证书存储）。两者在清单里的字段依据全部来自官方页面。

---

## 6. 危险动作的处理与理由（规范 §2.9）

| 动作 | 分级 | 理由 |
|---|---|---|
| `addstore` | `danger: destructive` + `requiresAdmin: true` | 它改的是**信任链本身**：往 Root 加一个 CA，等于让那个 CA 能为本机任意域名签出被信任的证书；这不是"覆盖一个文件"，而是改变机器对"谁可信"的判断。加了 `confirmPhrase: "加入证书存储 {storeName}"`（逐字确认）。 |
| `delstore` | `danger: destructive` + `requiresAdmin: true` | 与 `addstore` 对称的不可逆操作（certutil 没有"撤销删除"）。删掉根证书会让依赖它的站点/程序立刻报不受信任，删掉个人证书可能丢掉某个软件的签名身份。`confirmPhrase: "从证书存储 {storeName} 删除证书"`。 |
| `encode` / `decode` | `danger: overwrite` + `confirmText` | 只写**用户指定**的一个文件，不碰系统状态。默认**不加** `-f`（certutil 自己也会拒绝覆盖已存在的输出），要覆盖必须显式勾选；`confirmText` 里点名了"不要把输出路径填成输入文件本身"。 |

**为什么 store 改动不降级成 `execution: info`**：规范 §2.9 要求"再危险的操作也要在界面上留一条"，
`info` 是留给"**这一步该不该由工具替你做决定**"的情形（典型是 `ipconfig /release`：会切断网络）。
装/删证书的风险可以靠"逐字确认短语 + 明确写出后果"来消解，而且它**确实是用户想点的那一下**
（装内网根证书是最常见的诉求），所以本包按 `destructive` 处理，保留可执行。

**关于"静默提权"**：本轮实测本机可以 `Start-Process -Verb RunAs` 静默提权，
但**任务红线明确要求"提权只允许跑只读命令、绝不允许提权改证书存储"**，
所以这两个动作**没有走提权路径**，也没有实跑。
另外宿主当前**不实现**自动提权（规范 §2.4），用户要真正执行它们需要以管理员身份启动 All Tool。

---

## 7. 这个工具的坑（会咬人的地方）

### 7.1 certutil **不认** `/开关:取值` 这种单 token 写法（与 wevtutil 正好相反）

playbook §9 要求"先用真实命令各试一次两种写法"。实测（`smoke.ps1` 里会自动重跑这一段）：

```
两 token：-hashfile <file> SHA256      exit=0
两 token：-store My                    exit=0
两 token：-error 0x80070002            exit=0
单 token：-hashfile:<file>             exit=1   CertUtil: 未知参数: -hashfile:<文件>
单 token：-hashfile=<file>             exit=1   CertUtil: 未知参数: -hashfile=<文件>
单 token：-store:My                    exit=1   CertUtil: 未知参数: -store:My
单 token：-encode:foo                  exit=1   CertUtil: 未知参数: -encode:foo
```

**结论**：certutil 的动词与位置参数之间**只能用空白分隔**，
所以本清单**全部用 `positional`**（文件、存储名、CertId、错误码），
一个 `attached` / `separate` 字段都没有——不是"用不上"，是**用了就会坏**。
（对照 wevtutil：它只认单 token，所以那边全是 `attached` + 冒号写进 prefix。
**同一个仓库里的两个 Windows 包结论完全相反**，这就是为什么必须逐包实测。）

### 7.2 **开关必须排在位置参数之前** —— 这一条会静默传错参数

```
✓ 开关在前：certutil -encode -f <in> <out>      exit=0
✗ 开关在后：certutil -encode <in> -f <out>      exit=1  要求不多于 2 个参数，但收到了 3 个
✗ 开关在后：certutil -hashfile <file> SHA256 -v exit=1  要求不多于 2 个参数，但收到了 3 个
✗ 开关在后：certutil -dump <file> -user         exit=1  要求不多于 1 个参数，但收到了 2 个
✗ 开关在后：certutil -store My -user            exit=0x80090011 NTE_NOT_FOUND  ← **不报参数错，结果是另一个东西**
```

最后一条最危险：`-store My -user` 不报"参数太多"，而是把 `-user` 当成第二个位置参数（CertId）
去匹配，于是拿着字符串 `-user` 去找证书 → NTE_NOT_FOUND。
**用户看到的是一条"看起来对"的命令和一个莫名其妙的错误。**

**处理方式**：本包每个动作都把 flag 字段（`-f`、`-user`）**声明在位置参数之前**
（字段声明顺序 = argv 顺序）。这与 playbook §3.1 的"情形二"同类
（uv 的 `run` 也是"开关必须排在最前面"），也与那批 12 个包里的 `robocopy` 不同
（robocopy 是位置参数在前）。**判断方法**：拿一条带开关和位置参数的完整命令，
把开关挪到末尾再跑一次——报"参数太多"或静默给出别的结果，就说明开关必须在前。

### 7.3 退出码是 HRESULT，而且**动词帮助页自己返回 1**

- 成功 = 0；失败 = `0x8007xxxx` / `0x8009xxxx` / `0xD0000225` 这类 HRESULT，
  宿主看到的是**有符号**的负数（`0x80070002` → `-2147024894`）。
- **`certutil -?` 退出码 0，但每个 `certutil <动词> -?` 退出码 1**（抽测 16 个动词全部如此）。
  所以"看到过 1 就当成参数错"在 verb 帮助这条路径上是对的、在别处不一定。
- 参数校验失败给的是 **1**（`未知参数` / `参数太多` / `找不到参数`），
  文件不存在给的是 **0x80070002**。**同为"命令失败"，码完全不同**，
  清单的 `exitCodes` 两种都收了，并在 meaning 里写清哪一种是哪一种。

### 7.4 拿输出大小判断"成没成"会翻车

`-asn` 传一个**非 DER 文件**（例如 exe/dll）时，它会先按 ASN.1 一路打印
**3 364 453 字节**的十六进制内容，然后才以 `0x8007000D` 失败。
`-dump` 传一个带签名的大 DLL 会打印 **155 423 字节**（那份 Authenticode 签名很长）。
所以这两个动作要么别对随手拖进来的大文件点，要么有心理准备。

### 7.5 `-store` 官方自己承认有性能问题

官方 `-store` 一节的 Note 原文：证书超过 10 个、**或指定了 CertId** 时会有性能问题
（因为"填了 CertId 就会拿它去匹配列出的每一种类型"），建议改用 PowerShell 命令。
本包的 `certId` 字段 help 里原样写了这一条。
实测本机 `-store Root`（21 个证书）是**瞬时**返回的（9 963 字节），没有可测量的慢，
但官方既然点名了，就把警告留在字段上。

### 7.6 `-encode` / `-decode` 的失败消息互相串门

`-encode` 读不到输入文件时打的是 **`DecodeFile 返回了 系统找不到指定的文件。`**；
`-decode` 的目标文件已存在时打的是 **`EncodeToFile 返回了 文件存在。`**。
**两个动词的名字在错误路径上是反的**（certutil 内部共用同一个"读入 → 写出"流程）。
排错时不要被这两个名字骗了，看 `0x` 码更可靠。

### 7.7 `-encode` 的输出是 PEM 外壳，跟内容是不是证书无关

实测：把一个 13 字节的文本文件 `-encode` 出来，结果是

```
-----BEGIN CERTIFICATE-----
aGVsbG8gQWxsVG9vbA==
-----END CERTIFICATE-----
```

外壳是**固定**的。所以"看到 BEGIN CERTIFICATE 就以为是证书"是错的；
反过来，从别处拿来的一段带 PEM 外壳的 Base64，`-decode` 能正确还原（实测 30：78 → 13 字节）。

### 7.8 路径回显会被"规范化"

实测 `-hashfile C:\Windows\System32\msvcrt.dll` 的回显是 `C:\WINDOWS\System32\msvcrt.dll`
（`Windows` → `WINDOWS`）。这不是 certutil 改的路径，是它打印时用了系统里存的短/规范形式。
做"输出里回显的路径 vs 用户输入的路径"比对时要注意这一点。

---

## 8. 与官方文档对不上的地方（如实记录）

### 8.1 官方 `-hashfile` 一节**没有** Options 段、也没有 HashAlgorithm 取值表

- 官方全文只有两行：一句说明 + `certutil [options] -hashfile InFile [HashAlgorithm]`。
  **既没列可用开关，也没写 HashAlgorithm 能取什么值、默认是什么。**
- 本机 `certutil -hashfile -?` 给出了取值表：`哈希算法: MD2 MD4 MD5 SHA1 SHA256 SHA384 SHA512`，
  以及一段"通用选项"（`-Unicode -gmt -seconds -v -privatekey -pin -sid`）。
- **处理方式**：字段依据写"官方语法行的 `[HashAlgorithm]`"，
  取值表在 help 里注明取自本机帮助；**默认值「不填 = SHA1」是实测**（§5.1 第 6 行），
  写进了 help，而清单自己的默认值是 SHA256（并明确说了这是清单的选择，不是 certutil 的默认）。
- **MD2 / MD4 没有收进枚举**（本机实测能跑、官方连名字都没提）：它们早已不安全，
  暴露在界面里只会诱导人选。见 §9。

### 8.2 官方 `-encodehex` 列了 `-nocr` / `-nocrlf`，本机帮助里没有

- 官方 `### -encodehex` 的 Options：`[-f] [-nocr] [-nocrlf] [-UnicodeText]`。
- 本机 `certutil -encodehex -?` 的选项：只有 `-f` 与 `-UnicodeText`（+ 通用选项），**没有 `-nocr` / `-nocrlf`**。
- **处理方式**：`-encodehex` / `-decodehex` **整个不收**（§9），所以这条差异没有影响；
  如实记在这里，供以后要加这两个动词时参考。

### 8.3 官方 `-addstore` / `-delstore` 没有"需要管理员"的说明

- 官方 `### -addstore` / `### -delstore` 的 Options 与正文**通篇没有提权限**，
  只在 `-store` 一节说 `-user` 访问用户存储、`-Enterprise` 访问机器企业存储。
- 本机实测（非管理员）：这两个动词**一律以 `0x800702E4`（请求的操作需要提升）失败**，
  连"存储名不存在""文件不是证书"都没轮到（§5.6）。
- **处理方式**：`requiresAdmin: true` **按实测标**，并在 NOTES 与动作的 `sources.note` 里
  注明依据是实测而非官方文字（规范 §2.5：官方写了或实测确认才标）。

### 8.4 官方 `-asn` / `-decodehex` 的 `[type]` 参数没有取值表

- 官方只写 `numeric CRYPT_STRING_* decoding/encoding type`，**没给数字表**。
- 本机 `certutil -asn -?` 也只写 `类型 -- 数值 CRYPT_STRING_* 解码类型`。
- **处理方式**：`-asn` 的 `[type]` **不暴露成字段**（取值不可知 = 无法写 `doc`）；
  实测 `-asn <cer> 1` 直接 `0x8007000D ERROR_INVALID_DATA` 失败，说明猜数字是没用的。

### 8.5 官方 `-viewstore` / `-store` 的 OutputFile 行为

- 官方把 `-store` / `-viewstore` 都描述成"Dumps the certificate store"，第三个位置参数是
  "用于保存匹配证书的文件"。
- 实测**只有加了这个位置参数时才会写文件**：`certutil -store Root 0 <路径>` 会导出一个
  681 字节的证书文件；不加就纯打印。本包的 `list-store` **没有暴露这个位置参数**
  （理由是"只读动作里藏一个写文件的参数"会误导），见 §9。

### 8.6 官方 `-encode` 的 `-unicodetext` 与 `-decode` 的选项差异

- 官方 `-encode` 的 Options 是 `[-f] [-unicodetext]`；`-decode` 的 Options 只有 `[-f]`。
- 本机帮助一致（`-decode -?` 里确实没有 `-UnicodeText`）。
- **处理方式**：两边一致，本包两者都只暴露 `-f`，`-UnicodeText` 没做（§9）。

---

## 9. 故意没做的部分（给后来者的地图）

| 没做 | 原因 |
|---|---|
| **面向 CA / AD 的动词**（`-CAInfo`、`-CA`、`-Policy`、`-ping`、`-pingadmin`、`-revoke`、`-resubmit`、`-deny`、`-setattributes`、`-setextension`、`-schema`、`-view`、`-db`、`-deleterow`、`-backup*`、`-restore*`、`-shutdown`、`-renewCert`、`-installCert`、`-ds*`、`-AD*`、`-Template*`、`-SetCATemplates`、`-SetCASites`、`-enrollmentServerURL`、`-CredStore`、`-PolicyCache`、`-InstallDefaultTemplates`、`-MachineInfo`、`-DCInfo`、`-EntInfo`、`-TCAInfo`、`-SCInfo`、`-SCRoots`、`-attest`、`-GetCRL`、`-CRL`、`-ca.cert`、`-ca.chain`、`-dynamicfilelist`、`-databaselocations`、`-ImportKMS`、`-ImportCert`、`-GetKey`、`-RecoverKey`、`-MergePFX`、`-ConvertEPF`） | 这些是**证书服务器管理员的活**：要一台装了 AD CS 的机器 + 域账号才能跑，在家用机上要么失败要么做不了任何有意义的事。它们绝大多数是 `destructive`（吊销、删库行、还原密钥），做成界面按钮的收益远小于风险。**普通用户路径上一个都用不到。** |
| `-add-chain` / `-get-sth` 等 CT 日志相关动词 | Windows 11 的 `certutil -?` 总表里能列出（`-add-chain`、`-add-pre-chain`、`-get-sth`…），但**官方页面里没有对应小节**（官方页面在此处被截断，本包只按官方有说明的动词收）。**来源只有一侧 → 不收**（AGENTS.md R1 与 playbook §0.1 的口径）。 |
| `-encodehex` / `-decodehex` | ① 它们的 `[type]` 参数官方与本机都只写"数值 CRYPT_STRING_*"，**没有取值表**，做出来只能让用户猜数字；② 官方与本机对 `-nocr` / `-nocrlf` 的支持不一致（§8.2）。**十六进制转换不是普通用户的诉求**，收益也不高。 |
| `-URLCache`（含 `delete`） | 它列的是**证书/CRL 的下载缓存 URL**（PKI 排错用），对普通用户几乎没有可读价值；而带 `delete` 的形态是**改当前用户缓存**的动作。要做的话应该是"只列不删"，但连"只列"的实际输出本包都没有实跑过（怕命令名与形态拿不准），所以整体不做，留在这里。 |
| `-viewstore` / `-viewdelstore` | 两者都会**弹出 CryptUI 图形界面**（官方说 `-UI` 是 Invokes the certutil interface，`-viewstore`/`-viewdelstore` 走同一套 UI）。宿主捕获不到 UI 内容，而且会等用户关窗口——属于"会弹 GUI 并等待"的一类（playbook §0.1 明确说 v1 不做）。 |
| `-repairstore`（修复密钥关联） | 官方说是修 key association 或更新证书属性/密钥安全描述符。它会**改动证书对象的属性**，而且需要"哪些证书需要修"这种诊断结论——普通用户没有这个需求，判断门槛又高。 |
| `-key` / `-delkey` / `-verifykeys` / `-DeleteHelloContainer` | 前三个是**密钥容器**层面的操作（`-delkey` 不可逆地删掉密钥容器），最后一个是删 Windows Hello 容器（官方明说"用户需要注销才能完成"）。**风险与收益明显不成比例**，不做。 |
| `-syncWithWU` / `-generateSSTFromWU` / `-generatePinRulesCTL` / `-downloadOcsp` / `-verifyCTL` | 这些是"从 Windows Update 同步根证书/CTL"的批量动作：会**联网下载并往目录里写多个文件**，`-syncWithWU` 官方还专门列了"文件已存在会报 183"与"不再受信任的根需要删除"的注意事项。属于系统维护脚本的范畴，不是"点一下看个结果"。 |
| `-dumpPFX` / `-importPFX` / `-exportPFX` / `-MergePFX` / `-ConvertEPF` | PFX 是**带私钥**的证书包。`-dumpPFX` 只读但需要密码（`-p`）；`-importPFX` / `-exportPFX` 会**移动私钥**，`-exportPFX` 还能生成无密码保护的私钥文件（`NoEncryptCert`）。私钥相关的操作不该由一个"点一下"的界面来做。 |
| `-TPMInfo` | 读 TPM 信息本身无害，但实测**非管理员直接失败**（`0x800702E4` 请求的操作需要提升）。做一个"点了就报权限错"的按钮没有价值（与 wevtutil 不收 `/lf` 同一口径）。 |
| `-UI` | 会拉起 CryptUI 图形界面（见 `-viewstore` 一行）。 |
| `-f`（`-dump` / `-store` 的强制覆盖） | `-dump` 的 `-f` 只在配合 `-split`（把嵌入的 ASN.1 拆成文件）时才有意义，而 `-split` 本包没做；`-store` 的 `-f` 同理。**没有暴露一个"看起来有用、实际上什么都不影响"的勾选框。** |
| `-Unicode` / `-gmt` / `-seconds` / `-v` / `-privatekey` / `-pin` / `-sid` 等通用选项 | `-Unicode` 会让输出变成 UTF-16LE，与清单声明的 `runtime.encoding: oem` 打架；`-gmt` / `-seconds` 只改时间显示格式；`-privatekey` 会**打印私钥数据**；`-pin` / `-sid` 是智能卡/服务身份相关的。都不做。 |
| `-Enterprise` / `-GroupPolicy` / `-service` / `-dc` | 企业/组策略存储与远程域控相关：家用机上要么没有这些存储、要么需要域环境；`-dc` 还需要域管理员。与"移除远程相关开关"的既有口径一致（参考 wevtutil / tasklist 包）。 |
| `-split`（分离嵌入的 ASN.1 元素并保存到文件） | 官方说它会把嵌入元素**写成一堆文件**（`-f` 就是给它用的）。这是"一个勾选框生成多个文件"的动作，输出不可预览、容易在用户没注意的地方留下文件。 |
| `-silent` / `-Silent` | 官方定义为"用无声标志获得 crypt 上下文"，实测对 `-store` / `-delstore` 的输出**没有可解释的影响**（`-store -Silent My` 与 `-store My` 都是 449/416 字节，时间戳细节不同）。**无法向用户解释它做了什么 → 不做。** |
| `-store` 的第三个位置参数（导出证书到文件） | 见 §8.5：它会让一个"查看"动作写文件。要导出证书时，官方推荐的做法是另一个动词或 PowerShell。 |
| `-v -uSAGE` 列出的隐藏动词 | 官方 Tip 说能借此看到 `-?` 里隐藏的动词；本包**只按官方页面有独立小节的动词收**——隐藏动词在官方页面里没有对应说明，没有可引用的出处（R1）。 |
| `output.progress` | certutil 不画进度条（哈希也没有"百分比"输出）→ 不写（不凭印象编正则）。 |
| `workingDirectory` | 所有动作的文件路径都是用户显式选的（`type: file`），没有"默认写到当前目录"的行为 → 不需要覆盖。 |
| `requiresAdmin`（除 `addstore` / `delstore` 之外） | `-TPMInfo` 也实测需要管理员，但本包没收录它。其余收录的动作在本机非管理员下**全部实测通过**，所以不标。 |

---

## 10. 校验结果（R3）

本包单独复跑（**因为同时有别的智能体在写 `plugins/icacls/`，整仓校验器会在读到那个半成品时抛
`ScannerError` 崩掉**；这是并行开发的临时状态，不是 certutil 的问题）：

```
[ OK ] plugins/certutil/manifest.yaml  (10 动作 / 23 字段 / 字段出处标注 23 个 = 100%)
开关溯源（启发式，仅提示）: 检查 7 个开关，2 个能在参考文档快照里找到
    [未确认] 动作 list-store 字段 userStore: 开关 -user
    [未确认] 动作 dump 字段 userStore: 开关 -user
    [未确认] 动作 verify 字段 userStore: 开关 -user
    [未确认] 动作 addstore 字段 userStore: 开关 -user
    [未确认] 动作 delstore 字段 userStore: 开关 -user
```

那 5 条 `[未确认]` **是预期的**：这一层启发式检查用的语料是 `docs/reference/7zip-switch-matrix.json`
（7-Zip 的开关矩阵）+ 其它参考快照，**不含 certutil 的官方页面**，所以 `-user` 找不到出处。
`-user` 的出处是官方 `-store` / `-dump` / `-addstore` / `-delstore` 各节的 Options 行
（已逐条写进对应字段的 `doc` 与动作的 `sources.note`）。

`plugins/certutil/smoke.ps1` 是本包自带的冒烟脚本，
`pwsh -NoProfile -File plugins/certutil/smoke.ps1` 可随时复跑；
它不影响校验器（校验器只读 `manifest.yaml` / `manifest.yml`）。

---

## 11. 本轮创建的临时对象与清理

**系统层面：零个。** 没有创建/删除任何证书、没有改动任何存储——
`smoke.ps1` 的自检就是为了保证这一点（28 条红线 argv + 6 个改动类动词的词法检查）。

**文件系统层面**（全部在 `%TEMP%` 下，前缀 `__AllToolProbe` 或集中在专用目录里）：

| 位置 | 内容 | 用途 | 复核 / 清理 |
|---|---|---|---|
| `%TEMP%\certutil-probe\` | `__AllToolProbe_a.txt`、`__AllToolProbe_enc.bin`、`__AllToolProbe_enc.b64`、`__AllToolProbe_enc.hex`、`__AllToolProbe_dec.bin`、`__AllToolProbe_dec2.bin`、`__AllToolProbe_dec3.bin`、`__AllToolProbe_plain.b64`、`__AllToolProbe_plainout.bin`、`__AllToolProbe_cert.cer`（**从当前用户 Root 存储只读导出的一个证书**）、`__AllToolProbe_exported.cer`、`__AllToolProbe_out1.bin`、`__AllToolProbe_out2.b64`、`certutil-help.txt`、`hashout.bin`、`certutil-official.html`（官方页面快照，**未入库**）、`validate*.txt`、`check_certutil.py`、`regex_test.py` | 第一批字段风格 / 编码 / 编码解码往返的测量 | `Remove-Item -Recurse -Force "$env:TEMP\certutil-probe"` |
| `%TEMP%\certutil-smoke\` | `probe.txt`、`plain.b64`、`wrapped.b64`、`probe.cer`、`decoded.bin`、`decoded-wrapped.bin`、`encoded.b64`、`encoded-new.b64` | `smoke.ps1` 的复跑工作目录 | `Remove-Item -Recurse -Force "$env:TEMP\certutil-smoke"` |

**收尾状态**：两个目录都已在提交本报告前删除（见下）。

**证书存储零残留自检（可复跑）**：`smoke.ps1` 会在**开始前**与**结束后**各取一次
`Root` / `My` / `CA` 三个存储的指纹（退出码 + stdout 字节数 + 「证书 N」段计数），
逐字比对是否变化。**连续复跑两次的实测结果**：

```
证书存储基线指纹（开始）：          证书存储指纹（结束）：
  Root|exit=0|bytes=9963|certs=21     Root|exit=0|bytes=9963|certs=21
  My  |exit=0|bytes=449 |certs=1      My  |exit=0|bytes=449 |certs=1
  CA  |exit=0|bytes=3619|certs=6      CA  |exit=0|bytes=3619|certs=6
  ✔ 与本轮开始前逐字相同（字节数、证书数、退出码都没变）
```

（取指纹**必须**走原始字节 + cp936 解码。第一版图省事写成 `& certutil -store Root | Out-String`，
正则数出 **0** 个证书——那是 PowerShell 输出代码页把中文标签打乱导致的假象，
不是存储空了。这个坑本身也值得记：**在 pwsh 里读中文输出必须显式定编码**，
或用 `ProcessStartInfo` 拿原始字节。）

**复核命令**（复制即用）：

```powershell
# 1) 包是否还在、校验是否绿（整仓；若别的智能体正在写其它包，用 NOTES §10 的单包脚本）
cd "D:\AI\All Tool"; uv run --with pyyaml --with jsonschema python scripts/validate-plugins.py

# 2) 临时对象是否清净（下面两条应当都返回 False）
Test-Path "$env:TEMP\certutil-probe"
Test-Path "$env:TEMP\certutil-smoke"

# 3) 证书存储是否被动过（三个存储的证书数应当与本 NOTES 记录一致：21 / 1 / 6）
& certutil -store Root | Select-String '================ 证书' | Measure-Object | Select-Object -ExpandProperty Count

# 4) 冒烟可复跑（脚本自建自用文件，跑完用 Remove-Item 删掉 %TEMP%\certutil-smoke）
pwsh -NoProfile -File "D:\AI\All Tool\plugins\certutil\smoke.ps1"
```
---

## 12. 给复核者的两条提示

1. **本包没有 `attached` / `separate` 字段是刻意的**（§7.1）。看到这里"一个带前缀的字段都没有"
   不要以为是漏写——certutil 对 `-verb:值` 一律返回 exit 1。
2. **`-addstore` / `-delstore` 是唯一两个"清单照写、字段照定义、但没有成功路径实测"的动作**
   （§5.6、§6）。如果复核时想验证 API 面，**必须在虚拟机/快照里**
   用自己生成的测试证书做，**不要在真机上动 Root 存储**——那会改这台机器的信任链。
