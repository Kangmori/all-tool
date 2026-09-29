# curl 工具包 —— 实测记录

## 环境与版本（实测）

| 项 | 值 |
|---|---|
| curl 版本 | `curl 8.19.0 (Windows) libcurl/8.19.0 Schannel zlib/1.3.1 WinIDN WinLDAP`（Release-Date 2026-03-11） |
| 程序路径 | `C:\Windows\System32\curl.exe` |
| 是否随系统提供 | 是。Windows 10 1803 起自带（System32 下），不需要额外安装 |
| TLS 后端 | Schannel（走系统证书库，所以企业根证书通常已被信任） |
| 权限 | 不需要管理员 |

## 实测（2026-09-29）

**① 直接跑（验证网络与选项原型）**

```
curl --head --location --silent --show-error https://example.com
→ 退出码 0
   HTTP/1.1 200 OK
   Content-Type: text/html; charset=utf-8
   Server: cloudflare
```

**② 用宿主跑（验证整条链路与编码）**

```
预览：curl.exe --version
输出：curl 8.19.0 (Windows) libcurl/8.19.0 Schannel zlib/1.3.1 WinIDN WinLDAP
      Release-Date: 2026-03-11
      Protocols: dict file ftp ftps gopher ... ws wss
      Features: alt-svc AsynchDNS HSTS HTTPS-proxy IDN IPv6 Kerberos Largefile ... UnixSockets
结论：成功（退出码 0，耗时 0.0 秒）
```

✅ 退出码 0、输出完整、中英文均正常（`runtime.encoding: utf-8` 正确）

**没跑的**：`download` / `download-remote-name`（会写文件）、`upload-file` / `post-json` /
`post-form` / `basic-auth`（会往服务器发数据）、`insecure` / `client-cert`（证书相关）。
这些的选项都对着本机 `curl --help all` 逐条核对过（含短选项），但没有真机执行记录——
发数据类动作请在真机上按需自行验证。

## 选项核对方式（R1/R2）

- 官方手册：`https://curl.se/docs/manpage.html`（每个字段的 `doc` 都指向它）
- **本机 `curl --help all`（8.19.0）逐条核对**：确认了 `-I/--head`、`-H/--header`、
  `-u/--user`、`-k/--insecure`、`-m/--max-time`、`-T/--upload-file`、`-v/--verbose`、
  `-s/--silent`、`-S/--show-error`、`-A/--user-agent`、`-b/--cookie`、`-e/--referer`、
  `-C/--continue-at`、`-F/--form`、`-o/--output`、`-O/--remote-name`、`-L/--location`、
  `--output-dir`、`--retry`、`--limit-rate`、`--create-dirs`、`--json`、`--compressed`、
  `--fail`、`--cert`、`--cacert` 都存在且签名与手册一致。

## 风险分级

| 级别 | 动作 |
|---|---|
| 低（直接执行） | `--version`、`head`、`get`、`user-agent`、`verbose` |
| 中（执行 + 提示/确认） | `download`、`download-remote-name`（写文件，overwrite）、`post-json`、`post-form`、`upload-file`、`basic-auth`（会把数据发出去，overwrite）、`insecure`、`client-cert`、`proxy` |

没有"极高风险"档的动作：curl 不会破坏本机已有数据，最坏情况是"把数据发到了不该发的地方"
或"覆盖了一个文件"，所以用 overwrite + 明确的 confirmText 提醒，而不是降级成 info。
`insecure`（跳过证书校验）的 confirmText 写明了它会让人无法发现中间人攻击，建议改用 `--cacert`。

## 踩到的坑

**YAML 普通标量里不能出现 `": "`**：`description` 里写了 `Content-Type: application/json`，
导致校验器在 `yaml.safe_load` 阶段直接崩掉（不是给出可读的校验错误，而是 Python traceback）。
已给该行加引号。以后写清单：字段值里只要出现 `: `（冒号+空格）就必须加引号。
