from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]


class EditorExportResourceTests(unittest.TestCase):
    def test_dynamic_note_shaders_are_explicit_export_roots(self) -> None:
        preset = (ROOT / "editor/export_presets.cfg").read_text(encoding="utf-8")
        export_line = next(line for line in preset.splitlines() if line.startswith("export_files="))
        for name in ("note_material.gdshader", "note_surface.gdshader", "bleed_hud.gdshader"):
            with self.subTest(shader=name):
                self.assertIn(f'"res://shaders/{name}"', export_line)
                self.assertTrue((ROOT / "editor/shaders" / name).is_file())


if __name__ == "__main__":
    unittest.main()
