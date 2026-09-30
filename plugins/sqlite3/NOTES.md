# sqlite3 工具包 —— 实测记录

> 这是**会话型机制（N35）的第二个用例**，目的不是把 sqlite3 做全，
> 而是验证「脚本重放 + 状态门控」这套机制离开 DiskPart 之后还成不成立。

## 1. 会话机制怎么落在这上面

| 机制要素 | DiskPart（第一个用例） | sqlite3（本包） |
|---|---|---|
| 喂脚本的方式 | `diskpart /s {script}` | `sqlite3 -init {script}` |
| 脚本结尾 | `exit` | `.quit` |
| "状态"是什么 | `select disk N` | `.open "路径"` |
| 失败了怎么知道 | 中英双语 errorPatterns | **纯英文** errorPatterns（sqlite3 不做本地化） |
| 状态值从哪来 | 字段 `index` | 字段 `path` |

**结论：宿主一行代码都没改就能套上** —— 说明这套机制是按"脚本 + 状态"抽象出来的，
不是照着 DiskPart 硬编码的。

## 2. 实测踩到的坑（值得写进 playbook）

1. **脚本里不能用反斜杠路径**：`.open "C:\\Users\\..."` 会被 dot-command 当转义符吃掉，
   错误信息里路径变成 `C:UsersSteve...`。**必须用正斜杠**（`C:/Users/...`）或写两个反斜杠。
   已在字段 help 里提醒用户。
2. **出错时退出码仍是 0**：`Parse error`、`unable to open database` 全都返回 0，
   错误只写 stderr。→ **反过来证明机制的判断规则是对的**：只能"没命中 errorPattern 才算成功"，
   靠退出码会把失败全判成成功。
3. **错误消息全英文**（sqlite3 不做本地化）→ errorPatterns 只需英文，比 DiskPart 简单。
4. 脚本末尾没有 `.quit` 也不会挂住：宿主的子进程没有可读 stdin，读到 EOF 就结束。
   `exitCommand` 仍然保留（对别的程序是必需的）。

## 3. 实测数据

- 版本：`3.44.3 2024-03-24 21:15:01 d68fb8b5…`（`--version` 走 stdout、退出码 0）
- 编码：用含中文表名的输出判 —— 严格 UTF-8 解码成功 → `runtime.encoding: utf-8`
- 失败消息样本（原文，将用于 errorPatterns）：
  - `Error: unable to open database "…": unable to open database file`
  - `Parse error near line 1: no such table: 没有这张表`
  - `Parse error near line 1: near "selec": syntax error`

## 4. 临时对象

探测用的临时目录 `%TEMP%\at-sqlite*`（含 demo.db 与脚本）由脚本创建；
**不涉及任何用户数据**，测试用的库是当场新建的。

## 5. 一处需要写清楚的设计细节：successPattern 在这里只是占位

规范目前要求 `session.state[]` 必须写 `successPattern`，但 sqlite3 与 DiskPart 不同：
- DiskPart 成功时有独特文案（`Disk 0 is now the selected disk`），可以用它确认状态确立；
- **sqlite3 成功时没有独特文案**（输出就是查询结果本身），而且**出错时退出码仍然是 0**
  （只有 stderr 上有 `Error:` / `Parse error`）。

所以本包把 `successPattern` 写成永远匹配的 `(?s).*`，**真正的成败判定完全交给
`session.errorPatterns`**（命中任一条就不确立状态）。这与"成功 = 没命中错误"的机制设计一致，
也说明：**规范里把 successPattern 设成必填，对这类程序是个多余的约束**（登记为规范观察）。
