from __future__ import annotations

import configparser
import hashlib
import json
import subprocess
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
PRESETS = REPO_ROOT / "client" / "export_presets.cfg"
POLICY = REPO_ROOT / "release" / "apk-policy.json"
PROVENANCE = REPO_ROOT / "release" / "asset-provenance.json"


class ReleaseContractTests(unittest.TestCase):
    def setUp(self) -> None:
        parser = configparser.ConfigParser(interpolation=None, strict=True)
        parser.optionxform = str
        with PRESETS.open("r", encoding="utf-8") as stream:
            parser.read_file(stream)
        self.presets = parser
        self.policy = json.loads(POLICY.read_text(encoding="utf-8"))

    def test_presets_match_apk_policy(self) -> None:
        for mode, index in (("public", 0), ("internal", 1)):
            preset = self.presets[f"preset.{index}"]
            options = self.presets[f"preset.{index}.options"]
            policy = self.policy["modes"][mode]
            self.assertEqual(options["package/unique_name"].strip('"'), policy["application_id"])
            self.assertEqual(options["package/name"].strip('"'), policy["application_label"])
            self.assertEqual(options["version/name"].strip('"'), policy["version_name"])
            self.assertEqual(options["version/code"], policy["version_code"])
            self.assertEqual(Path(preset["export_path"].strip('"')).name, policy["filename"])
            self.assertEqual(
                preset["custom_features"].strip('"'), policy["required_custom_feature"]
            )

    def test_public_preset_is_fail_closed(self) -> None:
        preset = self.presets["preset.0"]
        options = self.presets["preset.0.options"]
        self.assertEqual(preset["export_filter"].strip('"'), "resources")
        exports = preset["export_files"]
        for resource in (
            "res://scenes/main.tscn",
            "res://scenes/song_select.tscn",
            "res://scenes/gameplay.tscn",
            "res://scenes/settings.tscn",
            "res://shaders/note_surface.gdshader",
            "res://assets/fonts/SpaceGrotesk-Regular.woff2",
            "res://assets/fonts/SpaceGrotesk-Bold.woff2",
            "res://assets/models/detonation-orrery-mk2.glb",
            "res://assets/licenses/SpaceGrotesk-OFL-1.1.txt",
            "res://assets/licenses/Godot-LICENSE.txt",
            "res://assets/licenses/DotNet-LICENSE.txt",
            "res://assets/licenses/DotNet-THIRD-PARTY-NOTICES.txt",
        ):
            self.assertIn(f'"{resource}"', exports)
        excludes = preset["exclude_filter"].strip('"').split(",")
        self.assertIn("testdata/*", excludes)
        self.assertNotIn("scenes/dyna_maker_uv.tscn", excludes)
        self.assertNotIn("scripts/editor/*", excludes)
        self.assertEqual(options["architectures/arm64-v8a"], "true")
        for key in (
            "architectures/armeabi-v7a", "architectures/x86", "architectures/x86_64"
        ):
            self.assertEqual(options[key], "false")
        self.assertEqual(options["dotnet/include_scripts_content"], "false")
        self.assertEqual(options["dotnet/include_debug_symbols"], "false")

    def test_internal_preset_has_distinct_identity(self) -> None:
        preset = self.presets["preset.1"]
        options = self.presets["preset.1.options"]
        self.assertEqual(preset["export_filter"].strip('"'), "all_resources")
        self.assertNotIn("testdata", preset["exclude_filter"].casefold())
        self.assertEqual(options["dotnet/include_scripts_content"], "false")
        self.assertEqual(options["package/unique_name"].strip('"'),
                         "com.dynamiteuniverse.game.internaltest")

    def test_provenance_hashes_match(self) -> None:
        provenance = json.loads(PROVENANCE.read_text(encoding="utf-8"))
        for entry in provenance["assets"]:
            path = REPO_ROOT / entry["path"]
            self.assertTrue(path.is_file(), entry["path"])
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(digest, entry["sha256"], entry["path"])
            generator = entry.get("generator")
            if generator:
                generator_path = REPO_ROOT / generator
                self.assertEqual(
                    hashlib.sha256(generator_path.read_bytes()).hexdigest(),
                    entry["generatorSha256"],
                    generator,
                )

    def test_git_tracks_no_private_release_material(self) -> None:
        completed = subprocess.run(
            ["git", "ls-files"], cwd=REPO_ROOT, check=True,
            capture_output=True, text=True, encoding="utf-8",
        )
        tracked = {line.replace("\\", "/") for line in completed.stdout.splitlines()}
        status = subprocess.run(
            ["git", "status", "--porcelain=v1"], cwd=REPO_ROOT, check=True,
            capture_output=True, text=True, encoding="utf-8",
        ).stdout.splitlines()
        deleted = {
            line[3:].replace("\\", "/") for line in status
            if len(line) > 3 and "D" in line[:2]
        }
        effective_tracked = tracked - deleted
        self.assertFalse(any(path.startswith("client/testdata/") for path in effective_tracked))
        forbidden = (
            ".keystore", ".jks", ".p12", ".pfx", ".pem", ".key", ".apk", ".aab", ".apks"
        )
        self.assertFalse(any(path.casefold().endswith(forbidden) for path in effective_tracked))
        # Deletions are visible in the index until committed; require these exact legacy paths
        # to be absent from the working tree as well.
        self.assertFalse((REPO_ROOT / "tools/apktool/apktool.jar").exists())
        self.assertFalse((REPO_ROOT / "tools/apktool/dynamix_debug.keystore").exists())

    def test_game_and_editor_are_separate_godot_projects(self) -> None:
        client = REPO_ROOT / "client"
        editor = REPO_ROOT / "editor"
        self.assertTrue((client / "project.godot").is_file())
        self.assertTrue((editor / "project.godot").is_file())
        self.assertTrue((editor / "scenes/editor_main.tscn").is_file())
        self.assertTrue((editor / "DynaMakerUv.Editor.csproj").is_file())
        self.assertFalse((client / "scenes/dyna_maker_uv.tscn").exists())
        self.assertFalse(any((client / "scripts/editor").glob("**/*")))

        project = (client / "DynamiteUniverse.csproj").read_text(encoding="utf-8")
        self.assertNotIn("ChartEditor.Core.csproj", project)
        self.assertNotIn("scripts/editor", project)
        client_sources = "\n".join(
            path.read_text(encoding="utf-8-sig")
            for path in (client / "scripts").rglob("*.cs")
        )
        self.assertNotIn("DynamiteUniverse.Editor", client_sources)
        self.assertNotIn("DynaMakerUvMain", client_sources)
        self.assertNotIn('HasFeature("editor_runtime")', client_sources)

        editor_project = (editor / "project.godot").read_text(encoding="utf-8")
        editor_csproj = (editor / "DynaMakerUv.Editor.csproj").read_text(encoding="utf-8")
        editor_presets = (editor / "export_presets.cfg").read_text(encoding="utf-8")
        self.assertIn('run/main_scene="res://scenes/editor_main.tscn"', editor_project)
        self.assertIn('project/assembly_name="DynaMakerUv.Editor"', editor_project)
        self.assertNotIn("DynamiteUniverse.csproj", editor_csproj)
        self.assertNotIn("client/DynamiteUniverse.csproj", editor_csproj)
        self.assertNotIn("client/scripts/editor", editor_csproj)
        self.assertIn('name="DynaMaker UV Windows"', editor_presets)
        self.assertIn('export_filter="resources"', editor_presets)
        for resource in (
            "res://scenes/editor_main.tscn",
            "res://shaders/note_surface.gdshader",
            "res://assets/fonts/SpaceGrotesk-Regular.woff2",
            "res://assets/fonts/SpaceGrotesk-Bold.woff2",
        ):
            self.assertIn(f'"{resource}"', editor_presets)
        self.assertIn("testdata/*", editor_presets)
        self.assertIn(".godot/*", editor_presets)

    def test_local_dynamaker_reference_contains_code_but_no_upstream_assets(self) -> None:
        # Optional development reference, ignored by Git and never a build/export dependency.
        reference = REPO_ROOT / "third_party/dynamaker-modified-reference"
        if not reference.exists():
            self.skipTest("Optional local DynaMaker reference snapshot is not installed")
        self.assertTrue(reference.is_dir())
        origin = (reference / "ORIGIN.md").read_text(encoding="utf-8")
        self.assertIn("dynamaker-tool/dynamaker-modified", origin)
        self.assertIn("99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d", origin)
        for source in ("mouse.js", "keyboard.js", "playView.js", "function.js"):
            self.assertTrue((reference / "app/src/Script" / source).is_file(), source)

        forbidden_suffixes = {
            ".png", ".jpg", ".jpeg", ".webp", ".wav", ".mp3",
            ".ogg", ".ttf", ".otf", ".ico", ".icns",
        }
        binaries = [
            path.relative_to(reference).as_posix()
            for path in reference.rglob("*")
            if path.is_file() and path.suffix.casefold() in forbidden_suffixes
        ]
        self.assertEqual([], binaries)


if __name__ == "__main__":
    unittest.main()
