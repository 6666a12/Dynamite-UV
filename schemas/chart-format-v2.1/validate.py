"""Validate the two schemas and recompute both mixed-pack digest vectors from the source files.

python -B schemas/chart-format-v2.1/validate.py [--chart-tool path/to/ChartTool.dll]
"""

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[1]


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def load_validator(path):
    schema = read(path)
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chart-tool", type=Path, help="Use an already-built chart tool DLL")
    opt = parser.parse_args()
    tool = (["dotnet", str(opt.chart_tool.resolve())] if opt.chart_tool else
            ["dotnet", "run", "--project", str(REPO / "tools/chart-tool/ChartTool.csproj"), "--"])
    vector = read(ROOT / "vectors/gameplay-digest-mixed.json")
    package = ROOT / vector["package"]
    meta = read(package / "meta.json")
    load_validator(ROOT / "pack.schema.json").validate(meta)
    schemas = {0: load_validator(ROOT.parent / "chart-format-v2/chart.schema.json"),
               1: load_validator(ROOT / "chart.schema.json")}
    if {entry["id"] for entry in meta["charts"]} != {item["chartId"] for item in vector["charts"]}:
        raise ValueError("mixed package must cover both known-answer difficulties")
    for known in vector["charts"]:
        entry = next(x for x in meta["charts"] if x["id"] == known["chartId"])
        chart = read(package / entry["file"])
        schemas[chart.get("formatMinor", 0)].validate(chart)
        audio = package / (entry.get("audio") or meta["audio"])
        if hashlib.sha256(audio.read_bytes()).hexdigest() != vector["audioSha256"]:
            raise ValueError(f"unexpected audio bytes: {audio}")
        result = subprocess.run([*tool, "digest", str(package), "--chart", known["chartId"]],
                                cwd=REPO, check=True, text=True, stdout=subprocess.PIPE)
        actual = dict(line.split(": ", 1) for line in result.stdout.splitlines() if ": " in line)
        expected = {key: vector[key] for key in ("packId", "algorithm", "audioSha256")}
        for key, value in {**known, **expected}.items():
            if actual.get(key) != value:
                raise ValueError(f"{known['chartId']}: {key}: expected {value}, got {actual.get(key)}")
        print(f"PASS {known['chartId']}: {known['rulesetId']} {known['gameplayDigest']}")


if __name__ == "__main__":
    main()
