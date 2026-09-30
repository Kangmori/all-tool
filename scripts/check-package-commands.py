#!/usr/bin/env python
"""能力回归：核对"清单里声明的子命令"在**当前安装的版本**上是否还认得。

为什么需要它：文档漂移检测（`check-doc-drift.ps1`）只能告诉你"上游发新版了"，
但回答不了**"我们的清单会不会因此坏掉"**。这个脚本直接问程序本身：
把清单里每个动作声明的 `command` / `commandArgs` 拼成一次 `<exe> <子命令> --help`，
退出码非 0 就说明这个子命令不见了或改名了 —— 于是得到一份**必须重测/修清单**的清单。

用法：
    uv run --with pyyaml python scripts/check-package-commands.py            # 全部包
    uv run --with pyyaml python scripts/check-package-commands.py uv scoop   # 指定包

另外它对 `scoop` 会额外做一件事：把 `scoop status --local` 的真实输出喂给清单里那条
「逐应用更新」正则（N34），报告命中数 —— 这条正则是照着真实输出写的，最容易被上游改表格改坏。

局限（要如实知道）：
  - 只证明"子命令认不认"，**不证明参数语义没变**（那要逐条重测）。
  - 有些程序用 `?` 而不是 `--help`，有些子命令帮助本身返回非 0（本项目已实测过 netsh `?` 就是 1）；
    这类会出现在报告里，需要人工判读，不是自动失败。
"""
import pathlib
import re
import subprocess
import sys

import yaml

ROOT = pathlib.Path(__file__).resolve().parent.parent


def run(argv, timeout=90):
    try:
        p = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=timeout)
        return p.returncode, (p.stdout or "") + (p.stderr or "")
    except FileNotFoundError:
        return -1, "找不到可执行文件"
    except Exception as e:                      # noqa: BLE001 - 报告出来即可
        return -1, str(e)


def check(plugin_id: str) -> int:
    manifest_path = ROOT / "plugins" / plugin_id / "manifest.yaml"
    if not manifest_path.exists():
        print(f"跳过 {plugin_id}：没有清单")
        return 0

    manifest = yaml.safe_load(manifest_path.read_text(encoding="utf-8"))
    exe = manifest["locate"]["executable"]
    print(f"=== {plugin_id}（exe={exe}）===")

    code, out = run([exe, "--version"])
    version = (out.strip().splitlines() or ["?"])[0][:80] if out.strip() else "?"
    print(f"  当前版本: {version}")

    seen, ok, suspicious = set(), 0, []
    for action in manifest["actions"]:
        cmd = (action.get("command") or "").strip()
        extra = action.get("commandArgs") or []
        key = " ".join([cmd, *extra]).strip()
        if not key or key in seen:
            continue
        seen.add(key)
        argv = [exe, cmd, *extra, "--help"] if cmd else [exe, "--help"]
        code, out = run(argv, timeout=60)
        if code == 0:
            ok += 1
        else:
            suspicious.append((action["id"], key, code,
                               (out.strip().splitlines() or [""])[0][:90]))

    print(f"  子命令回归：检查 {len(seen)} 个，认得 {ok} 个，可疑 {len(suspicious)} 个")
    for action_id, key, code, first in suspicious:
        print(f"     [待判读] {action_id}: `{key}` 退出码 {code} | {first}")

    # ---- 额外：重测 nextSteps 里带占位符的正则（照着真实输出写的那种）
    for action in manifest["actions"]:
        for step in action.get("nextSteps") or []:
            if "{1}" not in (step.get("title") or ""):
                continue
            if action["id"] != "status":
                continue
            out_args = (action.get("command") or "").split()
            extra = action.get("commandArgs") or []
            if action.get("fixedArgs"):
                extra = [*extra, *action["fixedArgs"]]
            code, out = run([exe, *out_args, *extra], timeout=240)
            hits = re.findall(step["when"], out)
            print(f"  下一步正则 `{step['when']}`：对真实输出命中 {len(hits)} 处 {hits[:12]}")

    return len(suspicious)


def main() -> int:
    ids = sys.argv[1:] or sorted(p.name for p in (ROOT / "plugins").iterdir() if p.is_dir())
    total = sum(check(i) for i in ids)
    print()
    print(f"合计待判读项: {total}")
    print("提示：'待判读'不等于失败——有些程序的子命令帮助本身返回非 0（例如 netsh 的 ?）。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
