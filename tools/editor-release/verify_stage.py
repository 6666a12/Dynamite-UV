"""Validate the exported stage's ordinary startup, stdin IPC and error-free shutdown."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess


def verify(stage: Path) -> None:
    result = subprocess.run([str(stage.resolve()), "--headless", "--quit-after", "180"],
                            cwd=stage.resolve().parent,
                            input=json.dumps({"command": "audioSnapshot", "requestId": 771}) + "\n",
                            capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
    print(result.stdout, end="", flush=True)
    print(result.stderr, end="", flush=True)
    events = [json.loads(line[len("DYNA_STAGE "):]) for line in result.stdout.splitlines()
              if line.startswith("DYNA_STAGE ")]
    if (result.returncode or "ERROR:" in result.stderr or "ERROR:" in result.stdout
            or not any(e.get("event") == "ready" for e in events)
            or not any(e.get("event") == "audioSnapshot" and e.get("requestId") == 771 for e in events)):
        raise RuntimeError("Stage ordinary startup/IPC regression failed")
    print("EDITOR_STAGE_STARTUP_IPC PASS")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", type=Path)
    verify(parser.parse_args().stage)
