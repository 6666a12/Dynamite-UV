"""Exercise both native-launcher selection paths without requiring a visible desktop."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    package = args.package.resolve()
    launcher = package / "DynaMakerUV.exe"
    manifest = json.loads((package / "distribution.json").read_text(encoding="utf-8"))
    # Older untrimmed candidates predate fallbackKind.
    fallback_kind = manifest["shellRuntime"].get("fallbackKind", "shared-runtime")
    fallback = package / "shell-bundled/DynaMakerUv.Editor.Shell.exe"
    if fallback_kind == "trimmed-application":
        bundled_root, bundled_exe = fallback.parent, fallback
    elif fallback_kind == "shared-runtime":
        bundled_root, bundled_exe = package / "runtime", package / "runtime/dotnet.exe"
    else:
        raise RuntimeError(f"Unknown bundled runtime kind: {fallback_kind}")
    if not (package / "shell/DynaMakerUv.Editor.Shell.dll").is_file() or not bundled_exe.is_file():
        raise RuntimeError("Candidate is missing the system Shell or declared bundled executable")

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
            selected = [line.removeprefix("Selected ") for line in entry.splitlines() if line.startswith("Selected ")]
            if len(selected) != 1:
                raise RuntimeError("Launcher did not log exactly one runtime selection")
            kind, _, directory = selected[0].partition(" ")
            selected_root = Path(directory).resolve() if directory else None
            if selection and (kind != "Bundled" or selected_root != bundled_root.resolve()):
                raise RuntimeError(f"Forced selection did not use the declared {fallback_kind} runtime: {selected[0]}")
            if not selection and (kind != "System" or selected_root is None or selected_root.is_relative_to(package)):
                raise RuntimeError("This two-path verification requires a compatible system .NET installation outside the package")
            print("PASS launcher", selection or ["system-first"], command, flush=True)
    # Also capture assertions directly: the GUI bootstrap intentionally doesn't
    # inherit stdout handles, so its exit code alone isn't an assertion transcript.
    if fallback_kind == "trimmed-application":
        result = subprocess.run([str(fallback), "--headless"], cwd=package,
                                capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=180)
        print(result.stdout, end="")
        print(result.stderr, end="")
        if result.returncode or "PASS" not in result.stdout:
            raise RuntimeError("Trimmed fallback regression failed")
    print("EDITOR_LAUNCHER_PATHS PASS")


if __name__ == "__main__":
    main()
