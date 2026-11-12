"""Measure unpacked and per-file ZIP/DEFLATE payload size (without writing archives)."""
from __future__ import annotations

import argparse
from collections import defaultdict
import json
from pathlib import Path
import zlib


def measure(root: Path) -> dict:
    groups = defaultdict(lambda: {"bytes": 0, "deflateBytes": 0, "files": 0})
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        group = groups[path.relative_to(root).parts[0]]
        compressor = zlib.compressobj(6, zlib.DEFLATED, -15)
        with path.open("rb") as stream:
            while chunk := stream.read(1024 * 1024):
                group["bytes"] += len(chunk)
                group["deflateBytes"] += len(compressor.compress(chunk))
        group["deflateBytes"] += len(compressor.flush())
        group["files"] += 1
    return {"path": str(root.resolve()), "groups": dict(groups),
            "total": {key: sum(g[key] for g in groups.values()) for key in ("bytes", "deflateBytes", "files")},
            "compressionNote": "DEFLATE level 6 payload only; ZIP headers not included"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", nargs="+", type=Path)
    args = parser.parse_args()
    for root in args.directories:
        if not root.is_dir():
            parser.error(f"Missing directory: {root}")
    print(json.dumps([measure(root) for root in args.directories], indent=2))


if __name__ == "__main__":
    main()
