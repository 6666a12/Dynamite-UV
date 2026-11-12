"""Build a local Windows x64 editor candidate; never publishes or installs anything."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
import sys

ROOT = Path(__file__).resolve().parents[2]


def run(*args: str) -> None:
    print("Running:", *args, flush=True)
    subprocess.run(args, cwd=ROOT, check=True, timeout=600)


def verify_resources(godot: Path, pack: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="editor-pack-resource-check-") as directory:
        project = Path(directory)
        (project / "project.godot").write_text('config_version=5\n', encoding="utf-8")
        shutil.copy2(ROOT / "tools/editor-release/verify_resources.gd", project / "verify.gd")
        result = subprocess.run(
            [str(godot.resolve()), "--headless", "--path", str(project), "--script", "verify.gd", "--", str(pack.resolve())],
            cwd=project, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
        print(result.stdout, end="", flush=True)
        print(result.stderr, end="", flush=True)
        if result.returncode or "EDITOR_PACK_RESOURCES PASS" not in result.stdout or "ERROR:" in result.stderr:
            raise RuntimeError("Exported stage resource verification failed")


def copy_runtime(source: Path, destination: Path) -> str:
    versions = []
    for path in (source / "shared/Microsoft.NETCore.App").iterdir():
        parts = path.name.split(".")
        if len(parts) == 3 and all(p.isdigit() for p in parts) and parts[:2] == ["10", "0"]:
            versions.append((tuple(map(int, parts)), path.name))
    if not versions:
        raise RuntimeError("A stable .NET 10.0 x64 installation is required for the bundled runtime")
    version = max(versions)[1]
    destination.mkdir()
    shutil.copy2(source / "dotnet.exe", destination)
    for relative in [f"host/fxr/{version}", f"shared/Microsoft.NETCore.App/{version}"]:
        shutil.copytree(source / relative, destination / relative)
    for name in ["LICENSE.txt", "ThirdPartyNotices.txt"]:
        shutil.copy2(source / name, destination)
    return version


def collect_notices(output: Path) -> list[dict]:
    licenses = output / "licenses"
    licenses.mkdir()
    shutil.copy2(ROOT / "THIRD_PARTY_NOTICES.md", licenses)
    for name in ["godot", "dotnet", "space-grotesk"]:
        shutil.copytree(ROOT / "third_party" / name, licenses / name)
    assets = json.loads((ROOT / "editor/shell/obj/project.assets.json").read_text(encoding="utf-8"))
    folders = [Path(p) for p in assets["packageFolders"]]
    inventory = []
    for identity, library in assets["libraries"].items():
        if library["type"] != "package":
            continue
        directory = next(p / library["path"] for p in folders if (p / library["path"]).is_dir())
        spec = ET.parse(next(directory.glob("*.nuspec"))).getroot()
        metadata = next(n for n in spec if n.tag.split("}")[-1] == "metadata")
        fields = {n.tag.split("}")[-1]: n for n in metadata}
        license_node = fields.get("license")
        record = {"package": identity, "license": None if license_node is None else license_node.text,
                  "licenseType": None if license_node is None else license_node.get("type"), "noticeFiles": []}
        target = licenses / "nuget" / identity.replace("/", "-")
        target.mkdir(parents=True)
        shutil.copy2(next(directory.glob("*.nuspec")), target)
        # Preserve upstream notice bytes. Do not synthesize copyright statements from license IDs.
        with zipfile.ZipFile(next(directory.glob("*.nupkg"))) as archive:
            for entry in archive.infolist():
                path = Path(entry.filename)
                lower = path.name.lower()
                if not entry.is_dir() and any(word in lower for word in ("license", "notice", "copying", "copyright")):
                    if path.is_absolute() or ".." in path.parts or ":" in entry.filename:
                        raise RuntimeError("Unsafe notice path in NuGet archive")
                    dest = target / path
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(archive.read(entry))
                    record["noticeFiles"].append(path.as_posix())
        inventory.append(record)
    (licenses / "nuget-inventory.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
    return inventory


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--dotnet-root", required=True, type=Path)
    parser.add_argument("--cxx", required=True, type=Path, help="MinGW-w64 x64 g++")
    parser.add_argument("--output", required=True, type=Path, help="Must not exist")
    parser.add_argument("--trimmed-fallback", action="store_true", help="Experimental app-specific trimmed Shell fallback")
    parser.add_argument("--trimmed-stage", action="store_true", help="Experimental rooted Godot managed runtime trimming")
    parser.add_argument("--stage-template", type=Path, help="Matching custom Godot release template; replaces native EXE only")
    parser.add_argument("--stage-from", type=Path, help="Explicitly reuse stage files from a prior candidate (recorded in manifest)")
    parser.add_argument("--profile", choices=["windows-x64-test-v1"], help="Pinned user-approved test profile")
    args = parser.parse_args()
    profile = None
    if args.profile:
        profile = json.loads((ROOT / "tools/editor-release/test-profile.json").read_text())
        if not args.stage_template or hashlib.sha256(args.stage_template.read_bytes()).hexdigest() != profile["templateSha256"]:
            parser.error("Pinned test profile requires the verified custom template hash")
        args.trimmed_fallback = args.trimmed_stage = True
    output = args.output.resolve()
    if output.exists():
        parser.error("Output already exists; use a new candidate directory")
    if args.stage_template:
        template = args.stage_template.resolve()
        info = subprocess.run([str(template), "--version"], capture_output=True, text=True, timeout=30, check=True)
        if not info.stdout.strip().startswith("4.7.1.") or "a13da4feb" not in info.stdout:
            parser.error("Custom stage template must match Godot 4.7.1 revision a13da4feb")
    output.mkdir(parents=True)
    stage = output / "stage"
    if args.stage_from:
        shutil.copytree(args.stage_from.resolve(), stage)
    else:
        stage.mkdir()
        run(str(args.godot.resolve()), "--headless", "--quit", "--path", str(ROOT / "editor"), "--export-release",
            "DynaMaker UV Windows", str(stage / "DynaMakerStage.exe"))
    # Godot may exit zero after export errors; require both the resource pack and managed payload.
    required = [stage / "DynaMakerStage.exe", stage / "DynaMakerStage.pck",
                stage / "data_DynaMakerUv.Editor_windows_x86_64/DynaMakerUv.Editor.dll",
                stage / "data_DynaMakerUv.Editor_windows_x86_64/coreclr.dll"]
    if not all(p.is_file() for p in required):
        raise RuntimeError("Godot export is incomplete")
    if args.trimmed_stage:
        with tempfile.TemporaryDirectory(prefix="editor-stage-trim-") as temporary:
            scratch = Path(temporary)
            payload = scratch / "publish"
            run("dotnet", "publish", "editor/DynaMakerUv.Editor.csproj", "-c", "ExportRelease",
                "-r", "win-x64", "--self-contained", "true", "--artifacts-path", str(scratch / "artifacts"),
                f"-p:DirectoryBuildPropsPath={ROOT.as_posix()}/tools/editor-release/stage-trim.props",
                f"-p:EditorTrimScratch={scratch.as_posix()}",
                f"-p:CustomAfterMicrosoftCommonTargets={ROOT.as_posix()}/tools/editor-release/stage-trim.targets",
                "-p:EditorTrimProbe=true", "-p:PublishTrimmed=true", "-p:TrimMode=partial",
                "-p:JsonSerializerIsReflectionEnabledByDefault=true",
                "-p:_EnableConsumingManagedCodeFromNativeHosting=true", "-p:BuiltInComInteropSupport=true",
                "-o", str(payload))
            managed = stage / "data_DynaMakerUv.Editor_windows_x86_64"
            # This is the freshly generated candidate's payload, never a source or prior package.
            shutil.rmtree(managed)
            shutil.copytree(payload, managed)
    if args.stage_template:
        # The official export first creates managed payload/PCK. Preserve its matching
        # resource pack; only use a custom native template built from the same revision.
        shutil.copy2(args.stage_template.resolve(), stage / "DynaMakerStage.exe")
    verify_resources(args.godot, stage / "DynaMakerStage.pck")
    if args.trimmed_stage or args.stage_template:
        run(sys.executable, "-I", str(ROOT / "tools/editor-release/verify_stage.py"), str(stage / "DynaMakerStage.exe"))
        result = subprocess.run([str(stage / "DynaMakerStage.exe"), "--headless", "--", "--verify-hud"],
                                cwd=stage, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
        print(result.stdout, end="", flush=True)
        print(result.stderr, end="", flush=True)
        if result.returncode or "EDITOR_HUD_OUTLINE PASS" not in result.stdout or "ERROR:" in result.stderr:
            raise RuntimeError("Customized stage native HUD/resource regression failed")
    run("dotnet", "publish", "editor/shell/DynaMakerUv.Editor.Shell.csproj", "-c", "Release",
        "-r", "win-x64", "--self-contained", "false", "-p:UseAppHost=false",
        "-p:RollForward=LatestPatch", "-o", str(output / "shell"))
    # Native bootstrap stays small and independent of the runtime it is selecting.
    run(str(args.cxx.resolve()), "-std=c++17", "-Os", "-s", "-ffunction-sections", "-fdata-sections",
        "-Wl,--gc-sections", "-Wall", "-Wextra", "-municode", "-mwindows", "-static",
        str(ROOT / "tools/editor-release/launcher.cpp"), "-o", str(output / "DynaMakerUV.exe"), "-lshell32", "-ladvapi32")
    if args.trimmed_fallback:
        # Never reuse ILLink intermediates across different feature switches: stale
        # linked assemblies can disagree with the generated runtime configuration.
        with tempfile.TemporaryDirectory(prefix="editor-trim-artifacts-") as artifacts:
            run("dotnet", "publish", "editor/shell/DynaMakerUv.Editor.Shell.csproj", "-c", "Release",
                "-r", "win-x64", "--self-contained", "true", "--artifacts-path", artifacts,
                "-p:PublishTrimmed=true", "-p:TrimMode=partial",
                "-p:JsonSerializerIsReflectionEnabledByDefault=true", "-p:BuiltInComInteropSupport=true",
                "-o", str(output / "shell-bundled"))
        config = json.loads((output / "shell-bundled/DynaMakerUv.Editor.Shell.runtimeconfig.json").read_text())
        version = config["runtimeOptions"]["includedFrameworks"][0]["version"]
    else:
        version = copy_runtime(args.dotnet_root.resolve(), output / "runtime")
    for path in output.rglob("*.pdb"):
        path.unlink()
    for path in output.rglob("*.xml"):
        # XML documentation is not needed at runtime; notices are collected below, after this step.
        path.unlink()
    if args.trimmed_fallback:
        # The two Shell publications must use exactly the same dependency graph.
        # Collect notices from the normal publish's resolved dependencies, not a
        # subsequent probe's restore state.
        for name in ["LICENSE.txt", "ThirdPartyNotices.txt"]:
            shutil.copy2(args.dotnet_root / name, output / "shell-bundled" / name)
    if profile:
        stage_config = json.loads((stage / "data_DynaMakerUv.Editor_windows_x86_64/DynaMakerUv.Editor.runtimeconfig.json").read_text())
        stage_version = stage_config["runtimeOptions"]["includedFrameworks"][0]["version"]
        if version != profile["shellRuntimeVersion"] or stage_version != profile["stageRuntimeVersion"]:
            raise RuntimeError("Runtime version drifted from pinned test profile; review before updating profile")
    for name in ["Uninstall.cmd", "uninstall.ps1"]:
        shutil.copy2(ROOT / "tools/editor-release" / name, output / name)
    inventory = collect_notices(output)
    missing = [r["package"] for r in inventory if not r["noticeFiles"]]
    manifest = {"product": "DynaMaker UV", "version": "0.1.0-preview", "rid": "win-x64",
                "buildProfile": args.profile,
                "shellRuntime": {"policy": "system-compatible-first-bundled-fallback", "bundled": version,
                                 "fallbackKind": "trimmed-application" if args.trimmed_fallback else "shared-runtime"},
                "stageRuntime": "Godot rooted trimmed self-contained" if args.trimmed_stage else "Godot self-contained export",
                "stageResourceSource": str(args.stage_from.resolve()) if args.stage_from else "fresh-export",
                "stagePckSha256": hashlib.sha256((stage / "DynaMakerStage.pck").read_bytes()).hexdigest(),
                "customStageTemplateSha256": hashlib.sha256(args.stage_template.read_bytes()).hexdigest() if args.stage_template else None,
                "experimentalTrimming": args.trimmed_fallback or args.trimmed_stage,
                "customTemplateLimitations": ["OpenGL-only", "no ANGLE fallback", "no WinRT TTS", "no AccessKit", "native icon/version resources not reapplied"] if args.stage_template else [],
                "distributionStatus": "local-candidate-not-cleared-for-publication",
                "noticeReviewRequired": missing}
    (output / "distribution.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "README.txt").write_text(
        "DynaMaker UV - local candidate, not yet approved for public redistribution.\n"
        "Start DynaMakerUV.exe. Extract the whole directory; do not move the EXE alone.\n"
        "Shell: compatible system .NET 10 x64 first; bundled runtime fallback.\n"
        "Godot stage: its exported self-contained runtime. No SDK or installer required.\n"
        "Preferences: %LOCALAPPDATA%/DynaMakerUV/. Keep chart packages outside this directory.\n"
        "Uninstall: close the editor and run Uninstall.cmd. Confirmed program files only; charts, extra/modified files and preferences stay.\n"
        "Diagnostics: --launcher-check, --bundled-runtime, --launcher-wait.\n"
        "Unsigned preview; no automatic update. See licenses and distribution.json.\n"
        "Experimental size profiles, if enabled, require additional compatibility testing.\n", encoding="utf-8")
    hashes = {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted(output.rglob("*")) if p.is_file()}
    (output / "files.sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")
    print("CANDIDATE BUILT:", output)
    print("Packages requiring license/notice review:", len(missing))


if __name__ == "__main__":
    main()
