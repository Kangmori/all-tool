# winget 工具包 —— 实测记录

## 环境与版本（实测）

| 项 | 值 |
|---|---|
| winget 版本 | `v1.29.380`（`winget --version`） |
| 系统 | Windows 11 build 26200 |
| 程序路径 | `C:\Users\<用户名>\AppData\Local\Microsoft\WindowsApps\winget.exe` |
| 是否随系统提供 | 是。官方：WinGet 随 **App Installer** 提供，Windows 11 / 较新 Windows 10 / Server 2025 自带；**只支持 Windows 10 1809（build 17763）或更高**，且需登录过一次 Windows 让 Store 完成注册 |
| 权限 | 只读命令（`list` / `search` / `show` / `features` / `--version`）**不需要管理员**；安装类命令在需要时会弹 UAC（官方"管理员注意事项"一节有说明） |

## 耗时实测（这台机器，直接跑）

| 命令 | 耗时 |
|---|---|
| `winget --version` | 0.6 s |
| `winget list --scope user` | 1.3 s |
| `winget list` | 1.7 s |
| `winget features` | 0.6 s |

## 用宿主跑通的（2026-09-29）

在界面里选中 winget → 「列出已安装的程序包」→ 执行：

```
日志：开始执行：list（winget）
日志：  命令：C:\Users\<用户名>\AppData\Local\Microsoft\WindowsApps\winget.exe list
日志：执行结束：list 退出码=0 耗时=1.0s 行数=141 截断=False 取消=False 结论=成功
```

- **退出码 0、141 行、1.0 秒、无截断** ✅
- **编码正确**：输出里的中文（`名称 / ID / 版本 / 可用 / 来源`）在界面与日志里都正常
  → 所以 `runtime.encoding: utf-8` 是对的（winget 重定向输出用 UTF-8；用 OEM/GBK 会变乱码）

**没跑的**（会改动系统，按规范 §2.9 刻意不冒烟）：`install` / `upgrade` / `uninstall` /
`repair` / `import` / `download` / `export`（写文件）/ `configure`（`execution: info`，宿主不执行）。
它们的参数来自官方文档 + 本机 `--help` 双向核对，但**没有真机执行记录**。

## 踩到的坑（已修）

**枚举默认值会静默改变语义**：`--scope` 的枚举本来把 `user` 标成 `isDefault: true`，
界面就会默认填上 `--scope user`。实测生成的是 `winget list --scope user`——
这会把**整机范围安装的包过滤掉**，用户看到"少了很多软件"却不知道为什么。
现在三个 `--scope` 枚举（show / list / install）的默认值都改成"**不指定**"，
想限定范围要自己选。教训写进手册：**枚举的默认值等于替用户做决定，拿不准就默认"不指定"**。

## 参数出处（R1/R2）

- 官方命令表与全局选项表：`https://learn.microsoft.com/en-us/windows/package-manager/winget/`
  （本次用 `web_fetch` 直接抓的——DNS 从 fake-ip 改成 redir-host 之后这个工具可用了）
- 各子命令页：`.../winget/install`、`/upgrade`、`/uninstall`、`/search`、`/show`、`/list`、
  `/source`、`/pinning`、`/export`、`/import`、`/download`、`/hash`、`/validate`、`/features`、
  `/settings`、`/repair`、`/configure`
- **本机 `winget <子命令> --help` 逐条核对**（v1.29.380）：选项名以本机为准，
  官方页没写而本机有的（如 `source add --trust-level`、`pin add --installed`）**没有写进清单**，
  只收录官方与本机都能对应上的选项

`args:` 在 manifest v1 里不存在——多词子命令（`source list`、`pin add`）要用
`command: source` + `commandArgs: [list]`（校验器直接拦下了我第一版的写法）。

## 三级风险的应用

| 级别 | 动作 |
|---|---|
| 低（直接执行） | `--version`、`--info`、`search`、`show`、`list`、`features`、`source list`、`source update`、`pin list`、`pin remove`、`hash`、`validate` |
| 中（执行 + 逐字确认短语） | `install`（安装 X）、`upgrade-one`（升级 X）、`upgrade-all`（升级全部应用）、`uninstall`（卸载 X，另标 destructive）、`repair`（修复 X）、`import`（按文件导入并安装）、`source add`、`source remove`（删除来源 X） |
| 极高（`execution: info`，宿主不执行） | `settings`（会拉起外部编辑器）、`configure`（按 YAML 批量改系统状态） |

> `uninstall` 标成 `destructive` + 逐字确认（输入"卸载 Git.Git"这类），因为卸载会移除软件；
> 但它不属于"不可逆毁数据"那一档（可以重装），所以**没有**降级成 info——否则这个工具包就没什么用了。
> 另外提供了 `--purge`（连包数据一起删）字段，它的 help 里明确写了不可恢复，请谨慎勾选。
