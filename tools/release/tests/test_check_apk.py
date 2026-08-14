from __future__ import annotations

import hashlib
import json
import os
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECKER = REPO_ROOT / "tools" / "release" / "check_apk.py"
POLICY = REPO_ROOT / "release" / "apk-policy.json"

PUBLIC_IDENTITY = {
    "application_id": "org.duxcommunity.game",
    "application_label": "DUX-Community",
    "version_name": "0.1.2-dev",
    "version_code": "4",
    "debuggable": "false",
}
INTERNAL_IDENTITY = {
    "application_id": "org.duxcommunity.game.internaltest",
    "application_label": "DUX-Community Internal Test",
    "version_name": "0.1.2-internal-testdata",
    "version_code": "4",
    "debuggable": "true",
}


def import_text(source: str, destination: str) -> str:
    return (
        '[remap]\n\npath="%s"\n\n[deps]\n\n'
        'source_file="%s"\n'
        'dest_files=["%s"]\n'
    ) % (destination, source, destination)


class ApkGateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        self.fake_apkanalyzer = self._create_fake_apkanalyzer()
        self.fake_apksigner = self._create_fake_apksigner()

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def _create_fake_apkanalyzer(self) -> Path:
        if os.name == "nt":
            script = self.root / "fake_apkanalyzer.py"
            launcher = self.root / "fake_apkanalyzer.cmd"
            script.write_text(
                """import json
import os
import sys
identity = json.loads(os.environ["FAKE_APK_IDENTITY"])
args = sys.argv[1:]
if args[:2] == ["manifest", "application-id"]:
    key = "application_id"
elif args[:2] == ["manifest", "version-name"]:
    key = "version_name"
elif args[:2] == ["manifest", "version-code"]:
    key = "version_code"
elif args[:2] == ["manifest", "debuggable"]:
    key = "debuggable"
elif args[:2] == ["resources", "value"] and "godot_project_name_string" in args:
    key = "application_label"
else:
    print("unsupported fake apkanalyzer invocation: " + repr(args), file=sys.stderr)
    raise SystemExit(2)
value = identity.get(key)
if value is None:
    print("missing fake value: " + key, file=sys.stderr)
    raise SystemExit(3)
print(value)
""",
                encoding="utf-8",
            )
            launcher.write_text(
                f'@"{sys.executable}" "{script}" %*\n',
                encoding="utf-8",
            )
            return launcher

        launcher = self.root / "fake_apkanalyzer"
        launcher.write_text(
            f"""#!{sys.executable}
import json
import os
import sys
identity = json.loads(os.environ["FAKE_APK_IDENTITY"])
args = sys.argv[1:]
if args[:2] == ["manifest", "application-id"]:
    key = "application_id"
elif args[:2] == ["manifest", "version-name"]:
    key = "version_name"
elif args[:2] == ["manifest", "version-code"]:
    key = "version_code"
elif args[:2] == ["manifest", "debuggable"]:
    key = "debuggable"
elif args[:2] == ["resources", "value"] and "godot_project_name_string" in args:
    key = "application_label"
else:
    print("unsupported fake apkanalyzer invocation: " + repr(args), file=sys.stderr)
    raise SystemExit(2)
value = identity.get(key)
if value is None:
    print("missing fake value: " + key, file=sys.stderr)
    raise SystemExit(3)
print(value)
""",
            encoding="utf-8",
        )
        launcher.chmod(launcher.stat().st_mode | stat.S_IXUSR)
        return launcher

    def _create_fake_apksigner(self) -> Path:
        if os.name == "nt":
            script = self.root / "fake_apksigner.py"
            launcher = self.root / "fake_apksigner.cmd"
            script.write_text(
                """import os
print("Verifies")
print("Verified using v2 scheme (APK Signature Scheme v2): true")
print("Verified using v3 scheme (APK Signature Scheme v3): true")
print("Number of signers: 1")
print("V3.0 Signer: certificate SHA-256 digest: " + os.environ["DUX_PUBLIC_SIGNER_SHA256"])
""",
                encoding="utf-8",
            )
            launcher.write_text(f'@"{sys.executable}" "{script}" %*\n', encoding="utf-8")
            return launcher
        launcher = self.root / "fake_apksigner"
        launcher.write_text(
            f"""#!{sys.executable}
import os
print("Verifies")
print("Verified using v2 scheme (APK Signature Scheme v2): true")
print("Verified using v3 scheme (APK Signature Scheme v3): true")
print("Number of signers: 1")
print("V3.0 Signer: certificate SHA-256 digest: " + os.environ["DUX_PUBLIC_SIGNER_SHA256"])
""",
            encoding="utf-8",
        )
        launcher.chmod(launcher.stat().st_mode | stat.S_IXUSR)
        return launcher

    def make_apk(
        self,
        filename: str,
        entries: dict[str, bytes | str] | None = None,
        include_public_license: bool = True,
    ) -> Path:
        apk = self.root / filename
        deps = {
            "runtimeTarget": {"name": ".NETCoreApp,Version=v9.0/android-arm64"},
            "targets": {
                ".NETCoreApp,Version=v9.0/android-arm64": {
                    "DuxCommunity/1.0.0": {
                        "dependencies": {"DuxShared": "1.0.0", "GodotSharp": "4.7.1"}
                    }
                }
            },
        }
        files: dict[str, bytes | str] = {
            "AndroidManifest.xml": b"synthetic manifest",
            "classes.dex": b"synthetic dex",
            "assets/project.binary": (
                b"ECFG synthetic internal_testdata" if "internal" in filename
                else b"ECFG synthetic public_release"
            ),
            "assets/assets.sparsepck": b"GDPCsynthetic",
            "assets/scenes/main.tscn.remap": b"[remap]\npath=\"res://.godot/exported/main.scn\"\n",
            "assets/scenes/song_select.tscn.remap": b"[remap]\npath=\"res://.godot/exported/select.scn\"\n",
            "assets/scenes/gameplay.tscn.remap": b"[remap]\npath=\"res://.godot/exported/game.scn\"\n",
            "assets/scenes/settings.tscn.remap": b"[remap]\npath=\"res://.godot/exported/settings.scn\"\n",
            "assets/shaders/note_surface.gdshader": b"shader_type canvas_item;",
            "assets/assets/fonts/Orbitron.woff2.import": b"[remap]\n",
            "assets/.godot/imported/Orbitron.woff2-235b0a6662c224041d47d6f8fba9dfa1.fontdata": bytes.fromhex("4744464401000000030000000800000053796e7468657469634f72626974726f6e5061796c6f6164"),
            "assets/.godot/mono/publish/arm64/DuxCommunity.deps.json": json.dumps(deps),
            "assets/.godot/mono/publish/arm64/DuxCommunity.dll": b"dll",
            "assets/.godot/mono/publish/arm64/DuxCommunity.runtimeconfig.json": b"{}",
            "assets/.godot/mono/publish/arm64/DuxShared.dll": b"dll",
            "assets/.godot/mono/publish/arm64/GodotSharp.dll": b"dll",
            "assets/.godot/mono/publish/arm64/.dotnet-publish-manifest": b"manifest",
            "lib/arm64-v8a/libgodot_android.so": b"so",
        }
        if include_public_license:
            for relative in (
                "Orbitron-OFL-1.1.txt",
                "Godot-LICENSE.txt",
                "DotNet-LICENSE.txt",
                "DotNet-THIRD-PARTY-NOTICES.txt",
            ):
                files[f"assets/assets/licenses/{relative}"] = (
                    REPO_ROOT / "client" / "assets" / "licenses" / relative
                ).read_bytes()
        if entries:
            files.update(entries)
        with zipfile.ZipFile(apk, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in files.items():
                archive.writestr(name, data)
        return apk

    def run_gate(
        self,
        mode: str,
        apk: Path,
        identity: dict[str, str] | None = None,
        apkanalyzer: Path | None = None,
    ) -> subprocess.CompletedProcess[str]:
        environment = os.environ.copy()
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        font_payload = (
            self.root / "approved-font-payload.bin"
        )
        font_payload.write_bytes(bytes.fromhex(
            "4744464401000000030000000800000053796e7468657469634f72626974726f6e5061796c6f6164"
        ))
        policy["modes"]["public"]["allowed_imported_payloads"] = {
            "Orbitron.woff2-235b0a6662c224041d47d6f8fba9dfa1.fontdata":
                hashlib.sha256(font_payload.read_bytes()).hexdigest()
        }
        policy_path = self.root / "policy.json"
        policy_path.write_text(json.dumps(policy), encoding="utf-8")
        environment["FAKE_APK_IDENTITY"] = json.dumps(
            identity or (PUBLIC_IDENTITY if mode == "public" else INTERNAL_IDENTITY)
        )
        environment["DUX_PUBLIC_SIGNER_SHA256"] = "a" * 64
        environment["DUX_APKSIGNER_PATH"] = str(self.fake_apksigner)
        return subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--mode",
                mode,
                "--apk",
                str(apk),
                "--policy",
                str(policy_path),
                "--apkanalyzer",
                str(apkanalyzer or self.fake_apkanalyzer),
            ],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=environment,
        )

    def assert_rejected(self, completed: subprocess.CompletedProcess[str], phrase: str) -> None:
        self.assertNotEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertIn(phrase.casefold(), (completed.stdout + completed.stderr).casefold())

    def test_clean_public_passes_and_reports_sha256(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        completed = self.run_gate("public", apk)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertIn("APK gate PASS", completed.stdout)
        self.assertRegex(completed.stdout, r"sha256: [0-9a-f]{64}")
        self.assertIn(PUBLIC_IDENTITY["application_id"], completed.stdout)
        self.assertIn(PUBLIC_IDENTITY["application_label"], completed.stdout)
        self.assertIn(PUBLIC_IDENTITY["version_name"], completed.stdout)

    def test_public_rejects_nested_testdata_component(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/some/deep/testdata/packs/chart.json": "{}"},
        )
        self.assert_rejected(self.run_gate("public", apk), "testdata path")

    def test_public_rejects_unapproved_orphaned_imported_payload(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {
                "assets/.godot/imported/unknown.wav-ffffffffffffffffffffffffffffffff.sample": b"unknown",
            },
        )
        self.assert_rejected(self.run_gate("public", apk), "unapproved imported payload")

    def test_public_allows_only_policy_approved_imported_payload(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        completed = self.run_gate("public", apk)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)

    def test_public_rejects_testdata_source_mapping_and_payload(self) -> None:
        destination = "res://.godot/imported/secret.wav-0123456789abcdef0123456789abcdef.sample"
        apk = self.make_apk(
            "dux-community-public.apk",
            {
                "assets/metadata/secret.wav.import": import_text(
                    "res://private/testdata/packs/secret.wav", destination
                ),
                "assets/.godot/imported/secret.wav-0123456789abcdef0123456789abcdef.sample": b"payload",
            },
        )
        completed = self.run_gate("public", apk)
        self.assert_rejected(completed, "maps testdata source")
        self.assertIn("forbidden import payload", completed.stderr.casefold())

    def test_public_rejects_orphaned_quarantined_testdata_payload(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {
                "assets/.godot/imported/song_tablear.wav-51f928fa8ffdc4b0c30e755caf063f6e.sample": b"orphan",
            },
        )
        self.assert_rejected(self.run_gate("public", apk), "quarantined testdata imported payload")

    def test_rejects_legacy_ai_cover_path(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/assets/covers/cover_style_navy_03.png.import": "[remap]\n"},
        )
        self.assert_rejected(self.run_gate("public", apk), "legacy AI cover path")

    def test_rejects_legacy_ai_cover_source_mapping_and_payload(self) -> None:
        destination = "res://.godot/imported/cover_style_purple_04.png-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ctex"
        apk = self.make_apk(
            "dux-community-public.apk",
            {
                "assets/renamed/cover.import": import_text(
                    "res://assets/covers/cover_style_purple_04.png", destination
                ),
                "assets/.godot/imported/cover_style_purple_04.png-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ctex": b"cover",
            },
        )
        completed = self.run_gate("public", apk)
        self.assert_rejected(completed, "legacy AI cover source")
        self.assertIn("forbidden import payload", completed.stderr.casefold())

    def test_rejects_legacy_ai_cover_orphaned_payload(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {
                "assets/.godot/imported/cover_style_navy_02.png-adbe7e7cf03178e80b3547b92caee653.ctex": b"orphan",
            },
        )
        self.assert_rejected(self.run_gate("public", apk), "legacy AI cover imported payload")

    def test_rejects_identity_cross_wiring(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        completed = self.run_gate("public", apk, INTERNAL_IDENTITY)
        self.assert_rejected(completed, "identity mismatch for application_id")
        self.assertIn("identity mismatch for application_label", completed.stderr)
        self.assertIn("identity mismatch for version_name", completed.stderr)

    def test_internal_with_testdata_passes_and_warns(self) -> None:
        apk = self.make_apk(
            "dux-community-internal-testdata.apk",
            {"assets/testdata/packs/synthetic/chart.json": "{}"},
        )
        completed = self.run_gate("internal", apk)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertIn("DO NOT DISTRIBUTE", completed.stderr)
        self.assertIn("APK gate PASS", completed.stdout)

    def test_internal_without_testdata_is_rejected_and_warns(self) -> None:
        apk = self.make_apk("dux-community-internal-testdata.apk")
        completed = self.run_gate("internal", apk)
        self.assert_rejected(completed, "must contain at least one file under a testdata path")
        self.assertIn("DO NOT DISTRIBUTE", completed.stderr)

    def test_public_missing_font_license_is_rejected(self) -> None:
        apk = self.make_apk("dux-community-public.apk", include_public_license=False)
        self.assert_rejected(self.run_gate("public", apk), "missing required entry suffix")

    def test_public_filename_error(self) -> None:
        apk = self.make_apk("dux-community.apk")
        self.assert_rejected(self.run_gate("public", apk), "filename mismatch")

    def test_internal_filename_error(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/testdata/chart.json": "{}"},
        )
        self.assert_rejected(self.run_gate("internal", apk), "filename mismatch")

    def test_missing_identity_label_value_fails_closed(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        incomplete_identity = dict(PUBLIC_IDENTITY)
        del incomplete_identity["application_label"]
        self.assert_rejected(
            self.run_gate("public", apk, incomplete_identity),
            "critical identity verification failed",
        )

    def test_missing_identity_tool_fails_closed(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        missing = self.root / "does-not-exist" / "apkanalyzer"
        self.assert_rejected(self.run_gate("public", apk, apkanalyzer=missing), "critical identity")

    def test_public_missing_runtime_is_rejected(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/.godot/mono/publish/arm64/DuxShared.dll": b""},
        )
        self.assert_rejected(self.run_gate("public", apk), "required entry is empty")

    def test_public_rejects_debug_symbols(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/.godot/mono/publish/arm64/DuxCommunity.pdb": b"pdb"},
        )
        self.assert_rejected(self.run_gate("public", apk), "forbidden entry")

    def test_public_rejects_wrong_abi(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"lib/x86_64/libgodot_android.so": b"so"},
        )
        self.assert_rejected(self.run_gate("public", apk), "unapproved ABI")

    def test_public_rejects_tampered_notice(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/assets/licenses/Godot-LICENSE.txt": b"tampered"},
        )
        self.assert_rejected(self.run_gate("public", apk), "hash mismatch")

    def test_public_rejects_missing_custom_feature(self) -> None:
        apk = self.make_apk(
            "dux-community-public.apk",
            {"assets/project.binary": b"ECFG unclassified"},
        )
        self.assert_rejected(self.run_gate("public", apk), "missing custom feature")

    def test_public_rejects_debuggable_identity(self) -> None:
        apk = self.make_apk("dux-community-public.apk")
        identity = dict(PUBLIC_IDENTITY)
        identity["debuggable"] = "true"
        self.assert_rejected(
            self.run_gate("public", apk, identity), "identity mismatch for debuggable"
        )

    def test_policy_is_versioned_and_names_are_final(self) -> None:
        policy = json.loads(POLICY.read_text(encoding="utf-8"))
        self.assertEqual(policy["schema_version"], 1)
        self.assertTrue(policy["policy_revision"])
        self.assertEqual(policy["modes"]["public"]["filename"], "dux-community-public.apk")
        self.assertIn(
            "assets/licenses/Orbitron-OFL-1.1.txt",
            policy["modes"]["public"]["required_entry_suffixes"],
        )
        self.assertIn(
            "Orbitron.woff2-235b0a6662c224041d47d6f8fba9dfa1.fontdata",
            policy["modes"]["public"]["allowed_imported_payloads"],
        )
        self.assertEqual(
            policy["modes"]["internal"]["filename"],
            "dux-community-internal-testdata.apk",
        )


if __name__ == "__main__":
    unittest.main()
