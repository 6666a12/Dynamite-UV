"""Packaging-profile contracts; runtime behavior is tested against built candidates."""
from contextlib import redirect_stdout
import importlib.util
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zlib

ROOT = Path(__file__).resolve().parents[3]


def load_tool(name):
    spec = importlib.util.spec_from_file_location(f"editor_release_{name}", ROOT / f"tools/editor-release/{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class EditorSlimTests(unittest.TestCase):
    def test_measure_counts_files_and_compressed_payload(self):
        module = load_tool("measure")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "shell").mkdir()
            data = b"original test payload" * 100
            (root / "shell/example.bin").write_bytes(data)
            result = module.measure(root)
            compressor = zlib.compressobj(6, zlib.DEFLATED, -15)
            expected = len(compressor.compress(data) + compressor.flush())
            self.assertEqual(result["total"], {"bytes": len(data), "deflateBytes": expected, "files": 1})
            self.assertEqual(result["groups"]["shell"], result["total"])

    def test_stage_trim_preserves_native_roots(self):
        profile = ET.parse(ROOT / "tools/editor-release/stage-trim.targets").getroot()
        roots = {node.get("Include") for node in profile.iter("TrimmerRootAssembly")}
        self.assertEqual(roots, {"GodotSharp", "DynaMakerUv.Editor", "DynamiteUniverse.Presentation",
                                 "DynamiteUniverse.Shared", "ChartEditor.Core"})
        self.assertEqual(profile.find(".//_EnableConsumingManagedCodeFromNativeHosting").text, "true")
        paths = ET.parse(ROOT / "tools/editor-release/stage-trim.props").getroot()
        for key in ("BaseIntermediateOutputPath", "IntermediateOutputPath", "MSBuildProjectExtensionsPath"):
            self.assertIn("$(MSBuildProjectName)", paths.find(f".//{key}").text)

    def test_trimmed_publish_preserves_feature_switches_and_isolation(self):
        build = load_tool("build")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            stage, output = root / "source-stage", root / "candidate"
            managed = stage / "data_DynaMakerUv.Editor_windows_x86_64"
            managed.mkdir(parents=True)
            for file in (stage / "DynaMakerStage.exe", stage / "DynaMakerStage.pck",
                         managed / "DynaMakerUv.Editor.dll", managed / "coreclr.dll",
                         root / "LICENSE.txt", root / "ThirdPartyNotices.txt"):
                file.touch()
            argv = ["build.py", "--stage-from", str(stage), "--trimmed-stage", "--trimmed-fallback",
                    "--output", str(output), "--dotnet-root", str(root),
                    "--godot", str(root / "godot.exe"), "--cxx", str(root / "c++.exe")]

            def fake_process(command, **kwargs):
                if list(command[:2]) == ["dotnet", "publish"]:
                    target = Path(command[command.index("-o") + 1])
                    target.mkdir(parents=True)
                    if target == output / "shell-bundled":
                        (target / "DynaMakerUv.Editor.Shell.runtimeconfig.json").write_text(
                            '{"runtimeOptions":{"includedFrameworks":[{"version":"10.0.10"}]}}', encoding="utf-8")
                return subprocess.CompletedProcess(command, 0, "EDITOR_PACK_RESOURCES PASS\nEDITOR_HUD_OUTLINE PASS\n", "")

            with patch.object(sys, "argv", argv), patch.object(build, "collect_notices", return_value=[]), \
                 patch.object(build.subprocess, "run", side_effect=fake_process) as process, redirect_stdout(io.StringIO()):
                build.main()
            commands = [list(call.args[0]) for call in process.call_args_list]
            publishes = [cmd for cmd in commands if cmd[:2] == ["dotnet", "publish"]]
            system, = [cmd for cmd in publishes if cmd[cmd.index("-o") + 1] == str(output / "shell")]
            self.assertEqual(system[system.index("--self-contained") + 1], "false")
            trimmed = [cmd for cmd in publishes if cmd[cmd.index("--self-contained") + 1] == "true"]
            self.assertEqual(len(trimmed), 2)
            scratch_paths = set()
            for cmd in trimmed:
                switches = dict(arg.removeprefix("-p:").split("=", 1) for arg in cmd if arg.startswith("-p:"))
                for name, expected in {"PublishTrimmed": "true", "TrimMode": "partial",
                                       "JsonSerializerIsReflectionEnabledByDefault": "true",
                                       "BuiltInComInteropSupport": "true"}.items():
                    self.assertEqual(switches.get(name), expected)
                scratch = Path(cmd[cmd.index("--artifacts-path") + 1]).resolve()
                self.assertFalse(scratch.is_relative_to(build.ROOT))
                self.assertFalse(scratch.is_relative_to(output))
                self.assertFalse(scratch.exists(), "temporary build artifacts persist after packaging")
                scratch_paths.add(scratch)
            self.assertEqual(len(scratch_paths), 2)
            self.assertIn([sys.executable, "-I", str(ROOT / "tools/editor-release/verify_stage.py"),
                           str(output / "stage/DynaMakerStage.exe")], commands)

    def test_custom_template_build_keeps_3d_support(self):
        template = load_tool("build_template")
        with patch.object(sys, "argv", ["build_template.py", str(ROOT)]), \
             patch.object(template.subprocess, "check_output", side_effect=[template.REVISION, ""]), \
             patch.object(template.subprocess, "run") as run:
            template.main()
        run.assert_called_once()
        options = dict(arg.split("=", 1) for arg in run.call_args.args[0] if "=" in arg)
        self.assertEqual(options.get("disable_3d"), "no")


if __name__ == "__main__":
    unittest.main()
