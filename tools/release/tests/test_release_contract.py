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
            "res://assets/fonts/Orbitron.woff2",
            "res://assets/licenses/Orbitron-OFL-1.1.txt",
            "res://assets/licenses/Godot-LICENSE.txt",
            "res://assets/licenses/DotNet-LICENSE.txt",
            "res://assets/licenses/DotNet-THIRD-PARTY-NOTICES.txt",
        ):
            self.assertIn(f'"{resource}"', exports)
        excludes = preset["exclude_filter"].strip('"').split(",")
        self.assertIn("testdata/*", excludes)
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
                         "org.duxcommunity.game.internaltest")

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


if __name__ == "__main__":
    unittest.main()
