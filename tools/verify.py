"""Run the repository's non-graphical checks; never starts Godot or exports an APK."""
from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Debug")
    parser.add_argument("--no-restore", action="store_true", help="use already restored NuGet dependencies")
    args = parser.parse_args()
    restore = ["--no-restore"] if args.no_restore else []
    commands = [[sys.executable, "-B", "tools/architecture/check.py"]]
    for project in ("client/DynamiteUniverse.csproj", "editor/DynaMakerUv.Editor.csproj",
                    "editor/shell/DynaMakerUv.Editor.Shell.csproj"):
        commands.append(["dotnet", "build", project, "--configuration", args.configuration, *restore])
    for project in ("tools/core-tests/CoreTests.csproj", "tools/chart-editor-tests/ChartEditorTests.csproj"):
        commands.append(["dotnet", "run", "--project", project, "--configuration", args.configuration, *restore])
    commands.append(["dotnet", "run", "--project", "editor/shell/DynaMakerUv.Editor.Shell.csproj",
                     "--configuration", args.configuration, *restore, "--", "--headless"])
    commands.extend([
        [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools/release/tests", "-p", "test_*.py"],
        ["git", "diff", "--check"],
    ])
    for command in commands:
        print("Running: " + " ".join(command), flush=True)
        try:
            completed = subprocess.run(command, cwd=ROOT, check=False)
        except OSError as exception:
            print(f"Cannot run {command[0]}: {exception}", file=sys.stderr)
            return 1
        if completed.returncode:
            return completed.returncode
    print("VERIFICATION PASS", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
