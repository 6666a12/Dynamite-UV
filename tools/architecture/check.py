"""Check project and source dependency boundaries without building or launching Godot."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
POLICY = Path(__file__).with_name("policy.json")
SKIP = {"bin", "obj", ".godot"}


def inspect(root: Path, policy: dict) -> tuple[list[str], list[tuple[int, str]], dict[str, int]]:
    errors: list[str] = []
    sizes: list[tuple[int, str]] = []
    report_families = policy.get("report_class_families", {})
    totals = {name: 0 for name in report_families}
    for project, expected in policy["projects"].items():
        path = root / project
        if not path.is_file():
            errors.append(f"missing project: {project}")
            continue
        xml = ET.parse(path).getroot()
        references = sorted(node.attrib["Include"].replace("\\", "/")
                            for node in xml.iter("ProjectReference"))
        links = sorted(node.attrib["Include"].replace("\\", "/")
                       for node in xml.iter("Compile") if "Include" in node.attrib)
        packages = sorted(node.attrib["Include"] for node in xml.iter("PackageReference"))
        if references != sorted(expected["references"]):
            errors.append(f"{project}: project references changed: {references}")
        if links != sorted(expected["source_links"]):
            errors.append(f"{project}: linked source boundary changed: {links}")
        if packages != sorted(expected["packages"]):
            errors.append(f"{project}: package dependencies changed: {packages}")

    boundaries = [(set(root.glob(pattern)), boundary) for boundary in policy.get("source_boundaries", [])
                  for pattern in boundary["files"]]
    for directory in policy["source_roots"]:
        source_root = root / directory
        if not source_root.is_dir():
            errors.append(f"missing source root: {directory}")
            continue
        for path in sorted(source_root.rglob("*.cs")):
            if SKIP.intersection(path.relative_to(source_root).parts):
                continue
            name = path.relative_to(root).as_posix()
            source = path.read_text(encoding="utf-8-sig")
            lines = len(source.splitlines())
            sizes.append((lines, name))
            for matches, boundary in boundaries:
                if path in matches:
                    if re.search(boundary["forbidden"], source):
                        errors.append(f"{name}: {boundary['reason']}")
            for family, definition in report_families.items():
                declaration = rf"(?m)^\s*(?:(?:public|internal|sealed|abstract|static|partial)\s+)+class\s+{re.escape(definition['class'])}\b"
                if name.startswith(definition["root"] + "/") and re.search(declaration, source):
                    totals[family] += lines
            if directory in policy["pure_roots"]:
                # The project graph is the compile-time boundary; this catches accidental
                # adapter imports early, including aliases, fully-qualified and static imports.
                forbidden = r"(?m)^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?(?:Godot\b|DynamiteUniverse\.(?:Game|Ui|Audio|Editor)\b)"
                if re.search(forbidden, source):
                    errors.append(f"{name}: engine/UI import in a pure core project")

    return errors, sorted(sizes, reverse=True), totals


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", action="store_true", help="also show source sizes and selected class totals (informational)")
    args = parser.parse_args()
    policy = json.loads(POLICY.read_text(encoding="utf-8"))
    errors, sizes, totals = inspect(ROOT, policy)
    if args.report:
        for lines, path in sizes[:20]:
            print(f"{lines:5} {path}")
        print("Selected class totals (informational; includes nested controls and comments):")
        for name, lines in totals.items():
            print(f"{lines:5} {name}")
    for error in errors:
        print("FAIL: " + error)
    if errors:
        return 1
    print(f"ARCHITECTURE PASS: {len(sizes)} source files; project dependencies, source links and source boundaries")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
