"""Packaging-profile contracts; runtime behavior is tested against built candidates."""
from pathlib import Path
import importlib.util
import tempfile
import unittest
import zlib

ROOT = Path(__file__).resolve().parents[3]


class EditorSlimTests(unittest.TestCase):
    def test_measure_counts_files_and_compressed_payload(self):
        spec = importlib.util.spec_from_file_location("editor_measure", ROOT / "tools/editor-release/measure.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
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

    def test_trimmed_fallback_keeps_separate_system_path(self):
        source = (ROOT / "tools/editor-release/launcher.cpp").read_text(encoding="utf-8")
        self.assertIn('root / L"shell" / L"DynaMakerUv.Editor.Shell.dll"', source)
        self.assertIn('root / L"shell-bundled" / L"DynaMakerUv.Editor.Shell.exe"', source)
        self.assertIn('if (chosen.empty() && slimFallback && x64Executable(slimShell))', source)
        self.assertIn('launch(slimShell, forwarded, root, false, wait)', source)
        self.assertIn('if (!bundledOnly)', source)

    def test_stage_trim_preserves_native_roots(self):
        import xml.etree.ElementTree as ET
        profile = ET.parse(ROOT / "tools/editor-release/stage-trim.targets").getroot()
        roots = {node.get("Include") for node in profile.iter("TrimmerRootAssembly")}
        self.assertEqual(roots, {"GodotSharp", "DynaMakerUv.Editor", "DynamiteUniverse.Presentation",
                                 "DynamiteUniverse.Shared", "ChartEditor.Core"})
        self.assertEqual(profile.find(".//_EnableConsumingManagedCodeFromNativeHosting").text, "true")
        paths = ET.parse(ROOT / "tools/editor-release/stage-trim.props").getroot()
        for key in ("BaseIntermediateOutputPath", "IntermediateOutputPath", "MSBuildProjectExtensionsPath"):
            self.assertIn("$(MSBuildProjectName)", paths.find(f".//{key}").text)

    def test_trimmed_publish_preserves_feature_switches_and_isolation(self):
        source = (ROOT / "tools/editor-release/build.py").read_text(encoding="utf-8")
        for flag in ("--trimmed-fallback", "--artifacts-path", "-p:TrimMode=partial",
                     "-p:JsonSerializerIsReflectionEnabledByDefault=true", "-p:BuiltInComInteropSupport=true"):
            self.assertIn(flag, source)
        self.assertIn('tempfile.TemporaryDirectory(prefix="editor-trim-artifacts-")', source)
        self.assertIn("tools/editor-release/verify_stage.py", source)
        template = (ROOT / "tools/editor-release/build_template.py").read_text(encoding="utf-8")
        self.assertIn('"disable_3d=no"', template)


if __name__ == "__main__":
    unittest.main()
