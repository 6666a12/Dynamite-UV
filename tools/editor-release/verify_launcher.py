"""Exercise both native-launcher selection paths without requiring a visible desktop."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    package = args.package.resolve()
    launcher = package / "DynaMakerUV.exe"
    log = Path(os.environ["LOCALAPPDATA"]) / "DynaMakerUV/logs/launcher.log"
    for selection in ([], ["--bundled-runtime"]):
        for command in (["--launcher-check"], ["--launcher-wait", "--headless"]):
            offset = log.stat().st_size if log.exists() else 0
            result = subprocess.run([str(launcher), *selection, *command], cwd=package, timeout=180)
            if result.returncode:
                raise RuntimeError(f"Launcher failed: {selection + command}, exit={result.returncode}")
            with log.open("rb") as stream:
                stream.seek(offset)
                entry = stream.read().decode("utf-8", errors="replace")
            if selection and "Selected Bundled " not in entry:
                raise RuntimeError("Forced bundled selection did not use bundled runtime")
            if not selection and "Selected System " not in entry:
                raise RuntimeError("This two-path verification requires a compatible system .NET installation")
            print("PASS launcher", selection or ["system-first"], command, flush=True)
    # Also capture assertions directly: the GUI bootstrap intentionally doesn't
    # inherit stdout handles, so its exit code alone isn't an assertion transcript.
    fallback = package / "shell-bundled/DynaMakerUv.Editor.Shell.exe"
    if fallback.is_file():
        result = subprocess.run([str(fallback), "--headless"], cwd=package,
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
        print(result.stdout, end="")
        print(result.stderr, end="")
        if result.returncode or "PASS" not in result.stdout:
            raise RuntimeError("Trimmed fallback regression failed")
    print("EDITOR_LAUNCHER_PATHS PASS")


if __name__ == "__main__":
    main()
