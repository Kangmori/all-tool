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


def is_switch(token: str) -> bool:
    """是不是一个开关。

    **同时认 `-` 与 `/`**：7-Zip / scoop / uv 用 `-foo`，而 Windows 自带命令用 `/foo`
    （`/all` `/svc` `/fo`）。原先只认 `-`，导致 12 个 Windows 工具包在这层里
    一个开关都没被检查——报告里那句"195 个开关全部可溯源"其实是假完整（实测发现）。
    """
    return token.startswith(("-", "/"))


def field_switches(field: dict) -> list[str]:
    """列出该字段会用到的开关（用于白名单校验）。"""
    explicit = field.get("switchBase")
    if explicit:
        return [explicit]

    style = field.get("style")
    if style in ("attached", "separate", "flag", "repeated"):
        prefix = field.get("prefix") or ""
        return [normalize_base(prefix)] if is_switch(prefix) else []

    if style == "literal":
        tokens = []
        for value in field.get("values") or []:
            for arg in value.get("args") or []:
                if isinstance(arg, str) and is_switch(arg):
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


def load_reference_corpus(plugin_id: str, manifest: dict) -> tuple[str, int]:
    """把该工具包的参考文档快照拼成一份语料（用于 R1 的启发式检查）。

    语料来源有三类，**都必须是仓库里真实存在的东西**：

    1. `docs/reference/<以工具包 id 开头>/` —— 该工具包自己的文档快照目录；
    2. `docs/reference/<以工具包 id 开头的文件>` —— 我们**自己提取的派生物**
       （例如 `7zip-switch-matrix.json`：从官方 CHM 提取的"命令 × 可用开关"矩阵。
       它是事实性数据、是我们自己的成果，转公开时保留）；
    3. `docs/reference/win-*/` —— **只给那些出处指向 Microsoft Learn 的工具包**用
       （12 个 Windows 自带命令包各自太小，不值得每包一份快照）。
       不能无条件共享：早先无条件并入时，7-Zip 的开关被拿去和 Windows 开关清单比对，
       一下冒出 185 条假告警。

    第三方文档快照在转公开前会被清掉，那之后没有语料的包会直接跳过这层检查
    （它是启发式、不阻断构建），**不要**为了让报告好看而伪造语料。
    """
    reference = ROOT / "docs" / "reference"
    if not reference.exists():
        return "", 0

    # **本机专属库**（仓库之外，不入库）：第三方文档原文快照放那里。
    # 指针文件 `.local-vault` 在仓库根目录。CI / 干净机器上没有它 → 这里的语料为空，
    # 这层启发式检查自然跳过（它本来就不阻断构建）。
    vault_snapshots = None
    pointer = ROOT / ".local-vault"
    if pointer.exists():
        try:
            vault = pathlib.Path(pointer.read_text(encoding="utf-8").strip())
            if (vault / "snapshots").is_dir():
                vault_snapshots = vault / "snapshots"
        except OSError:
            vault_snapshots = None

    text = json.dumps(manifest, ensure_ascii=False)

    # `win-*` 语料只适用于**出自 windows-commands 文档集**的包（那 12 个 Windows 自带命令）。
    # 光看 "learn.microsoft.com" 不够：winget / 其它微软文档也在 learn 上，
    # 把它们套进 Windows 命令的开关清单会制造大量假告警（实测 49 条里大半是这么来的）。
    uses_learn = "windows-server/administration/windows-commands" in text

    chunks: list[str] = []
    files = 0

    def collect(path: pathlib.Path) -> None:
        nonlocal files
        if path.is_file():
            candidates = [path]
        else:
            candidates = [f for f in sorted(path.rglob("*")) if f.is_file()]
        for candidate in candidates:
            if candidate.suffix.lower() in {".md", ".txt", ".json", ".html"}:
                try:
                    chunks.append(candidate.read_text(encoding="utf-8", errors="ignore"))
                    files += 1
                except OSError:
                    continue

    prefix = plugin_id.lower()

    def scan(base: pathlib.Path) -> None:
        for entry in sorted(base.iterdir()):
            name = entry.name.lower()

            if entry.is_dir():
                if name.startswith(prefix) or (uses_learn and name.startswith("win-")):
                    collect(entry)
            elif name.startswith(prefix):
                collect(entry)

    scan(reference)

    if vault_snapshots is not None:
        scan(vault_snapshots)

    return "\n".join(chunks), files


def switch_source_report(manifest: dict) -> tuple[list[str], int, int]:
    """R1（不发明参数）的启发式检查：清单里用到的开关，是否能在该工具包的文档快照里找到。

    **刻意只做提示、不做门禁**：语料可能不全（例如某个开关只写在官网而非已抓取的页面里），
    短开关（-n）也容易在正文里偶然命中。它的价值是"给审阅者一个信号"，
    而不是"自动判作者有罪"。真正的门禁是 7z 那种从官方文档逐命令提取出的白名单。
    """
    corpus, files = load_reference_corpus(str(manifest.get("id", "")), manifest)
    if not corpus:
        return [], 0, 0

    missing: list[str] = []
    checked = 0

    for action in manifest.get("actions", []):
        for field in action.get("fields", []):
            for sw in field_switches(field):
                checked += 1
                # 前后要求是非标识符字符，避免 "-n" 命中 "-no-progress" 这类误判
                pattern = r"(?<![\w-])" + re.escape(sw) + r"(?![\w])"
                if not re.search(pattern, corpus):
                    missing.append(f"动作 {action.get('id')} 字段 {field.get('id')}: 开关 {sw}")

    return missing, checked, files


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
    total_switches = total_missing = 0
    missing_report: list[str] = []

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

            # R1 的启发式检查（不阻断，只报告）
            missing, checked, corpus_files = switch_source_report(data)
            total_switches += checked
            total_missing += len(missing)
            missing_report += [
                f"{data.get('id')} → {item}（参考快照 {corpus_files} 个文件）" for item in missing
            ]

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

    # R1（不发明参数）的启发式报告。只提示、不阻断——语料可能不全，短开关也可能偶然命中。
    if total_switches:
        print(
            f"开关溯源（启发式，仅提示）: 检查 {total_switches} 个开关，"
            f"{total_switches - total_missing} 个能在参考文档快照里找到"
        )
        for item in missing_report[:20]:
            print(f"    [待确认] {item}")
        if len(missing_report) > 20:
            print(f"    …另有 {len(missing_report) - 20} 条，未逐条列出")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
