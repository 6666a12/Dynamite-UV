"""Build an experimental editor-only 2D Godot template from the pinned upstream source.

Fetch source separately into a new directory. This invokes upstream build scripts;
never use an untrusted source tree. It doesn't install or replace official templates.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys

REVISION = "a13da4feb8d8aefc283c3763d33a2f170a18d541"
OPTIONS = [
    "platform=windows", "target=template_release", "arch=x86_64", "use_mingw=yes",
    "module_mono_enabled=yes", "disable_3d=no", "vulkan=no", "d3d12=no", "opengl3=yes",
    "optimize=size", "lto=none", "debug_symbols=no",
    "module_gltf_enabled=no", "module_fbx_enabled=no", "module_assimp_enabled=no",
    "module_openxr_enabled=no", "module_navigation_enabled=no", "module_navigation_2d_enabled=no",
    "module_navigation_3d_enabled=no", "module_jolt_physics_enabled=no",
    "module_godot_physics_3d_enabled=no", "module_csg_enabled=no",
    "module_gridmap_enabled=no", "module_lightmapper_rd_enabled=no",
]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--python", type=Path, default=Path(sys.executable), help="Python environment with SCons installed")
    parser.add_argument("--jobs", type=int, default=12)
    args = parser.parse_args()
    source = args.source.resolve()
    revision = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
    if revision != REVISION:
        parser.error(f"Expected Godot revision {REVISION}, found {revision}")
    changes = subprocess.check_output(["git", "-C", str(source), "diff", "HEAD", "--name-only"], text=True)
    if changes.strip():
        parser.error("Godot source has tracked modifications")
    # Python isolated mode prevents cwd/user-site shadowing before SCons loads the
    # explicitly requested upstream build scripts. Project scripts live elsewhere.
    subprocess.run([str(args.python.resolve()), "-I", "-m", "SCons", "-C", str(source),
                    *OPTIONS, f"-j{args.jobs}"], check=True)


if __name__ == "__main__":
    main()
