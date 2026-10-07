from __future__ import annotations

import re
import unittest
from collections import Counter
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
EXCLUDED_PARTS = {
    ".git", ".godot", ".venv", ".zcode", "bin", "local-generated", "node_modules",
    "obj", "testdata", "tmp", "tmp_shots",
}
TEXT_SUFFIXES = {
    ".axaml", ".cfg", ".cs", ".csproj", ".html", ".json", ".md", ".py",
    ".resx", ".sln", ".svg", ".txt",
}
LEGACY_PATTERN = re.compile(
    r"DuxCommunity|DuxShared|DUX[-·_ ]?Community|DUX Chart Editor|"
    r"DUX 谱面编辑器|org\.duxcommunity|dux-community|DUX_[A-Z0-9_]+",
    re.IGNORECASE,
)
ALLOWED_LEGACY_MATCHES = Counter({
    ("docs/original-judgement-analysis.md", "DUX-Community"): 3,
    ("docs/releasing.md", "org.duxcommunity"): 1,
})
FROZEN_WIRE_LITERALS = {
    "dynamite-uv-pack",
    "dynamite-uv-chart",
    "dynamite-uv-ruleset-2.0",
    "dynamite-uv-score-store",
    "gameplay-v1",
}


class BrandIdentityTests(unittest.TestCase):
    def test_legacy_identity_is_limited_to_explicit_compatibility_and_history(self) -> None:
        actual: Counter[tuple[str, str]] = Counter()
        for path in REPO_ROOT.rglob("*"):
            if path.resolve() == Path(__file__).resolve():
                continue
            if (not path.is_file() or path.suffix.casefold() not in TEXT_SUFFIXES or
                    any(part in EXCLUDED_PARTS for part in path.relative_to(REPO_ROOT).parts)):
                continue
            try:
                text = path.read_text(encoding="utf-8-sig")
            except UnicodeDecodeError:
                continue
            relative = path.relative_to(REPO_ROOT).as_posix()
            for match in LEGACY_PATTERN.finditer(text):
                actual[(relative, match.group(0))] += 1
        self.assertEqual(actual, ALLOWED_LEGACY_MATCHES)

    def test_project_and_android_identity_are_final(self) -> None:
        self.assertFalse((REPO_ROOT / "client" / "DuxCommunity.csproj").exists())
        self.assertFalse((REPO_ROOT / "client" / "DuxCommunity.sln").exists())
        self.assertFalse((REPO_ROOT / "shared" / "DuxShared.csproj").exists())
        self.assertTrue((REPO_ROOT / "client" / "DynamiteUniverse.csproj").is_file())
        self.assertTrue((REPO_ROOT / "client" / "DynamiteUniverse.sln").is_file())
        self.assertTrue((REPO_ROOT / "shared" / "DynamiteUniverse.Shared.csproj").is_file())

        project = (REPO_ROOT / "client" / "project.godot").read_text(encoding="utf-8")
        self.assertIn('config/name="Dynamite Universe"', project)
        self.assertIn('project/assembly_name="DynamiteUniverse"', project)

        presets = (REPO_ROOT / "client" / "export_presets.cfg").read_text(encoding="utf-8")
        self.assertIn('package/unique_name="com.dynamiteuniverse.game"', presets)
        self.assertIn(
            'package/unique_name="com.dynamiteuniverse.game.internaltest"', presets
        )

    def test_frozen_wire_literals_remain_present(self) -> None:
        sources = (
            (REPO_ROOT / "shared" / "Chart" / "V2" / "Models.cs").read_text(
                encoding="utf-8"
            )
            + (REPO_ROOT / "shared" / "Score" / "ScoreStoreCore.cs").read_text(
                encoding="utf-8"
            )
        )
        for literal in FROZEN_WIRE_LITERALS:
            self.assertIn(literal, sources)


if __name__ == "__main__":
    unittest.main()
