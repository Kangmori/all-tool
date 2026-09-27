#!/usr/bin/env python3
"""用 JSON Schema 校验所有工具包 manifest。

本地与 CI（GitHub Actions）共用同一个入口，避免"本地能过、CI 报错"。

依赖通过 uv 临时提供，不污染全局环境：
    uv run --with pyyaml --with jsonschema scripts/validate-plugins.py

除 schema 校验外，还做三项交叉检查：
  1) id 必须与所在目录名一致；
  2) doc 覆盖率（每个字段是否标注了 CHM 里的出处）—— 只统计，不阻断；
  3) 7-Zip 专用：字段挂的开关必须在该命令的官方可用开关内。判定用文档里的两处信息：
       switches  = 命令页 "Switches that can be used with this command" 一节列出的白名单
       mentioned = 该命令页里任何位置出现过的开关（语法行、示例里的也算）
     之所以要合并两者：官方白名单只列"有独立帮助页"的开关，像 -ba 这种没有独立页面、
     但在 hash 命令示例中确实使用的开关只会出现在 mentioned 里。
"""
import datetime
import json
import pathlib
import re
import sys

try:
    import yaml
    from jsonschema import Draft202012Validator
except ImportError as exc:  # pragma: no cover
    sys.exit(
        f"缺少依赖: {exc}\n请用: uv run --with pyyaml --with jsonschema scripts/validate-plugins.py"
    )

ROOT = pathlib.Path(__file__).resolve().parent.parent


def load_json(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8"))


def coerce_yaml_dates(node):
    """YAML 1.1 会把裸写的 2026-09-27 隐式解析成 date 对象，而 schema 要求字符串。

    这是 YAML 的语法特性而非作者的笔误，所以这里统一规整为 ISO 字符串，
    免得每个工具包作者都被同一个坑绊一次。（规范里仍建议写成 "2026-09-27"。）
    """
    if isinstance(node, dict):
        return {key: coerce_yaml_dates(value) for key, value in node.items()}
    if isinstance(node, list):
        return [coerce_yaml_dates(item) for item in node]
    if isinstance(node, datetime.datetime):
        return node.date().isoformat()
    if isinstance(node, datetime.date):
        return node.isoformat()
    return node


def normalize_base(token: str) -> str:
    """-mx9 -> -mx，-bb3 -> -bb，-x! -> -x，-mmt= -> -mmt"""
    base = re.sub(r"[!=:.,/\\]+$", "", token)
    while len(base) > 2 and base[-1].isdigit():
        base = base[:-1]
    return base


def field_switches(field: dict) -> list[str]:
    """列出该字段会用到的开关（用于白名单校验）。"""
    explicit = field.get("switchBase")
    if explicit:
        return [explicit]

    style = field.get("style")
    if style in ("attached", "separate", "flag", "repeated"):
        prefix = field.get("prefix") or ""
        return [normalize_base(prefix)] if prefix.startswith("-") else []

    if style == "literal":
        tokens = []
        for value in field.get("values") or []:
            for arg in value.get("args") or []:
                if isinstance(arg, str) and arg.startswith("-"):
                    tokens.append(normalize_base(arg))
        return tokens

    return []


def switch_allowed(base: str, allowed: set[str]) -> bool:
    if base in allowed:
        return True
    # 7-Zip 把 -mx / -mhe / -mmt / -m0=... 全部归在 -m (Method) 之下
    if base.startswith("-m") and "-m" in allowed:
        return True
    # 带后缀修饰的写法（-aoa 之于 -ao、-slfh 之于 -slf）
    return len(base) >= 3 and any(base.startswith(a) for a in allowed)


def check_switch_whitelist(manifest: dict, matrix: dict) -> list[str]:
    problems: list[str] = []
    if manifest.get("id") != "7zip":
        return problems

    commands = matrix.get("commands", {})
    for action in manifest.get("actions", []):
        cmd = str(action.get("command", ""))
        entry = commands.get(cmd)
        if entry is None:
            problems.append(f"动作 {action.get('id')}: 命令 '{cmd}' 不在官方命令矩阵中")
            continue

        allowed = set(entry.get("switches") or []) | set(entry.get("mentioned") or [])
        for field in action.get("fields", []):
            for sw in field_switches(field):
                if not switch_allowed(sw, allowed):
                    problems.append(
                        f"动作 {action.get('id')} (7z {cmd}) 字段 {field.get('id')}: "
                        f"开关 {sw} 不在官方允许列表内 {sorted(allowed)}"
                    )
    return problems


def doc_coverage(manifest: dict) -> tuple[int, int]:
    total = documented = 0
    for action in manifest.get("actions", []):
        for field in action.get("fields", []):
            total += 1
            if field.get("doc"):
                documented += 1
    return documented, total


def main() -> int:
    schema_path = ROOT / "docs" / "spec" / "manifest-v1.schema.json"
    if not schema_path.exists():
        sys.exit(f"找不到 schema: {schema_path}")

    validator = Draft202012Validator(load_json(schema_path))

    manifests = sorted(
        list((ROOT / "plugins").glob("*/manifest.yaml"))
        + list((ROOT / "plugins").glob("*/manifest.yml"))
    )
    if not manifests:
        print("没有找到任何 manifest，跳过。")
        return 0

    matrix_path = ROOT / "docs" / "reference" / "7zip-switch-matrix.json"
    matrix = load_json(matrix_path) if matrix_path.exists() else {}

    failed = 0
    total_doc = total_fields = 0

    for path in manifests:
        rel = path.relative_to(ROOT)
        data = coerce_yaml_dates(yaml.safe_load(path.read_text(encoding="utf-8")))
        errors = sorted(validator.iter_errors(data), key=lambda e: list(e.path))
        extra: list[str] = []

        if isinstance(data, dict):
            if data.get("id") and data["id"] != path.parent.name:
                extra.append(f"id '{data['id']}' 与目录名 '{path.parent.name}' 不一致")
            if matrix:
                extra += check_switch_whitelist(data, matrix)

        if errors or extra:
            failed += 1
            print(f"[FAIL] {rel}")
            for err in errors[:25]:
                loc = ".".join(str(p) for p in err.path) or "<root>"
                print(f"    schema: {loc}: {err.message}")
            for msg in extra:
                print(f"    check : {msg}")
        else:
            actions = len(data.get("actions", []))
            documented, fields = doc_coverage(data)
            total_doc += documented
            total_fields += fields
            pct = f"{documented * 100 // fields}%" if fields else "n/a"
            print(
                f"[ OK ] {rel}  ({actions} 动作 / {fields} 字段 / 字段出处标注 {documented} 个 = {pct})"
            )

    print(f"\n{len(manifests) - failed}/{len(manifests)} 个 manifest 通过")
    if total_fields:
        print(f"字段出处覆盖率: {total_doc}/{total_fields} ({total_doc * 100 // total_fields}%)")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
