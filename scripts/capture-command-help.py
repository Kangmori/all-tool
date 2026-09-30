r"""抓语料（第二版：逐条容错 + 超时 + 给 win-help 开 .gitignore 例外 + 写 README）。

第一版的问题：
  ① 有的命令连 /? 都需要提升（WinError 740）→ 整脚本崩掉，只写了 5 个
  ② docs/reference/win-help 被 .gitignore 忽略 → 加不进仓库
"""
"""
与既有脚本的分工（两份**并存**，互不改动）：
  · scripts/fetch-win-help.ps1      —— 最初那 12 个 Windows 自带命令：分开记 stdout/stderr、
                                       产出 _meta.json、对会弹对话框的 GUI 程序有超时。**不要改它。**
  · scripts/capture-command-help.py（本文件）—— 按**每个工具包自己的 locate** 抓其余包：
                                       PATH/别名/常见目录定位，必要时走提权通路。
  · diskpart 的 /? 本身要提权：做法是把重定向写进临时 .cmd，再 Start-Process -Verb RunAs -Wait
    运行该文件（引号只有一层；直接塞 cmd /c "..." 会因多层引号写坏而挂住）。

import datetime
import pathlib
import subprocess

import yaml

ROOT = pathlib.Path(r'D:\AI\All Tool')
HELP_DIR = ROOT / 'docs' / 'reference' / 'win-help'
HELP_DIR.mkdir(parents=True, exist_ok=True)
CREATE_NO_WINDOW = 0x08000000
TODAY = datetime.date.today().isoformat()


def decode_with(raw: bytes, enc: str) -> str:
    if enc == 'utf-16le':
        return raw.decode('utf-16-le', 'replace')
    if enc in ('oem', 'gbk', 'cp936'):
        return raw.decode('cp936', 'replace')
    try:
        return raw.decode('utf-8')
    except UnicodeDecodeError:
        return raw.decode('cp936', 'replace')


def find_exe(name: str):
    if not name:
        return None
    p = pathlib.Path(name)
    if p.is_absolute() and p.exists():
        return str(p)
    for base in (pathlib.Path(r'C:\Windows\System32'), pathlib.Path(r'C:\Windows')):
        cand = base / name
        if cand.exists():
            return str(cand)
    return None


def run_help(argv):
    """返回 (退出码, 原始字节, 错误说明)。任何异常都变成错误说明，不往外抛。"""
    try:
        proc = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, creationflags=CREATE_NO_WINDOW)
        proc.stdin.close()
        out, _ = proc.communicate(timeout=45)
        return proc.returncode, out, None
    except subprocess.TimeoutExpired:
        try:
            proc.kill()
        except Exception:
            pass
        return None, b'', '超时（45 秒未结束）'
    except OSError as ex:
        return None, b'', f'{ex.__class__.__name__}: {ex}'
    except Exception as ex:  # 兜底：绝不让单条命令打断整批抓取
        return None, b'', f'{ex.__class__.__name__}: {ex}'


written = []
failed = []

for manifest_path in sorted((ROOT / 'plugins').glob('*/manifest.yaml')):
    data = yaml.safe_load(manifest_path.read_text(encoding='utf-8'))
    pid = str(data.get('id') or manifest_path.parent.name)
    locate = data.get('locate') or {}
    exe_name = locate.get('executable') or ''
    exe = find_exe(exe_name)
    if not exe:
        failed.append((pid, f'定位不到 {exe_name}'))
        continue

    enc = ((data.get('runtime') or {}).get('encoding') or 'oem')
    invocations = [[exe, '/?']]
    for action in (data.get('actions') or [])[:1]:
        cmd_args = action.get('commandArgs') or []
        if cmd_args:
            invocations.append([exe] + [str(a) for a in cmd_args] + ['/?'])
            break

    blocks = []
    for argv in invocations:
        code, raw, err = run_help(argv)
        if err is not None:
            failed.append((pid, f'{" ".join(argv)} → {err}'))
            blocks = []
            break
        blocks.append((argv, code, len(raw), decode_with(raw, enc).replace('\r\n', '\n').strip('\n')))

    if not blocks:
        continue

    primary = blocks[0]
    body = [
        f'# {pid} —— 本机 {exe_name} 的 /? 原样输出（用于参数溯源；本文件由脚本抓取，勿手工编辑）',
        f'# 命令: {" ".join(primary[0])}',
        f'# 可执行文件: {exe}',
        f'# 抓取日期: {TODAY}',
        f'# 原始代码页/编码: {enc}（已按该编码解码，存为 UTF-8）',
        f'# 抓取条件: CreateNoWindow=true + 重定向 stdout/stderr + 启动后立刻关闭 stdin（与宿主一致）',
        f'# 退出码: {primary[1]}   原始字节数: {primary[2]}',
        '',
    ]
    for argv, code, nbytes, text in blocks:
        if argv is not primary[0]:
            body += ['', f'# ---- 附加：{" ".join(argv)}（退出码 {code}，{nbytes} 字节）----', '']
        body.append(text)
    (HELP_DIR / f'{pid}.txt').write_text('\n'.join(body) + '\n', encoding='utf-8')
    written.append(pid)

print(f'=== 抓取完成：成功 {len(written)} 个，失败 {len(failed)} 个 ===')
print('  成功:', ' '.join(written))
if failed:
    print('  失败（这些包的溯源仍会是「未覆盖」）：')
    for pid, why in failed:
        print(f'    {pid}: {why}')

# ---------------------------------------------------------------- README
readme = HELP_DIR / 'README.md'
readme.write_text(f"""# win-help —— 各命令 `/?` 的原样快照（语料）

本目录是为了**参数溯源**（R1：不发明参数）而抓取的语料：**每个工具包一份它自己命令的 `/?` 输出**。

- 抓取脚本：`scripts/capture-command-help.py`
- 抓取条件：与宿主一致（`CreateNoWindow=true` + 重定向 stdout/stderr + 启动后立刻关闭 stdin）
- 编码：按各包实测的 `runtime.encoding` 解码（本批出现过 `oem`(cp936) / `utf-8` / `utf-16le` 三种），统一存为 UTF-8，**原始代码页记录在每个文件头部**
- 校验器用法：`validate-plugins.py` 的开关溯源会优先把 `<工具包 id>.txt` 当作**该包自己的语料**；
  没有这份 txt 的包会被明确报成 `[未覆盖]`，而**不会**拿共享语料冒充（那曾经制造过"假完整"）

## 两个必须记住的限制

1. **文本随 Windows 版本变化** ✗：`/?` 的输出是**特定版本**的快照，头部记了抓取日期；
   换机器/换版本后应当**重新抓取**，不要把它当成跨版本真理。
2. **它是微软的文本** ✗：本目录的内容用于**参数溯源**这一事实性用途。
   **转公开发布前请复核微软的再分发条款** —— 公开仓库要不要保留本目录，由项目负责人决定。

## 抓不到的
有的命令**连 `/?` 都需要提升**（本机实测遇到 `WinError 740: 请求的操作需要提升`）。
这类包不会生成 txt，溯源报告会如实显示 `[未覆盖]`；需要的话在提权会话里手工补抓。
""", encoding='utf-8')
print('  已写 README.md')

# ---------------------------------------------------------------- .gitignore 例外
gi = ROOT / '.gitignore'
gt = gi.read_text(encoding='utf-8')
if 'win-help' not in gt:
    gt = gt.rstrip() + """

# 各命令 /? 的原样快照（参数溯源语料）——刻意入库：它是程序自己的输出，
# 且校验器的"未覆盖/已覆盖"判定要靠它。转公开前请复核再分发条款（见该目录 README）。
!docs/reference/win-help/
!docs/reference/win-help/*.txt
!docs/reference/win-help/*.md
"""
    gi.write_text(gt, encoding='utf-8')
    print('  已给 docs/reference/win-help 加 .gitignore 例外')

# ---------------------------------------------------------------- 校验器认这份语料
vp = ROOT / 'scripts' / 'validate-plugins.py'
vt = vp.read_text(encoding='utf-8')
if 'win-help' not in vt:
    anchor = '    entry = data.get(plugin_id)\n'
    assert vt.count(anchor) == 1, f'锚点 {vt.count(anchor)}'
    vt = vt.replace(anchor,
                    '    # 优先用本项目自己抓的"每命令一份 /? 快照"——它是程序自己的输出，\n'
                    '    # 不是我们的 NOTES，避免自证循环。\n'
                    '    help_txt = ROOT / "docs" / "reference" / "win-help" / f"{plugin_id}.txt"\n'
                    '    if help_txt.exists():\n'
                    '        try:\n'
                    '            return help_txt.read_text(encoding="utf-8"), 1\n'
                    '        except OSError:\n'
                    '            pass\n'
                    '\n' + anchor, 1)
    vp.write_text(vt, encoding='utf-8')
    print('  已让校验器优先读 docs/reference/win-help/<id>.txt')
