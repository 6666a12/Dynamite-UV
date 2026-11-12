from __future__ import annotations

import os
import re
import tempfile
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
    ("docs/archive/2026-10-07-cleanup/original-judgement-analysis.before.md", "DUX-Community"): 3,
})
FROZEN_WIRE_LITERALS = {
    "dynamite-uv-pack",
    "dynamite-uv-chart",
    "dynamite-uv-ruleset-2.0",
    "dynamite-uv-score-store",
    "gameplay-v1",
}


def scan_legacy_identity(root: Path) -> Counter[tuple[str, str]]:
    actual: Counter[tuple[str, str]] = Counter()
    # Local agent worktrees are separate checkouts, not this product's source tree.
    # Exclude only this root-relative container; a product folder named "worktrees"
    # (or any other .claude content) must remain subject to the same identity rules.
    local_worktrees = root / ".claude" / "worktrees"
    for directory, dirs, files in os.walk(root):
        parent = Path(directory)
        dirs[:] = [name for name in dirs if name not in EXCLUDED_PARTS and
                   parent / name != local_worktrees]
        for name in files:
            path = parent / name
            if (name in EXCLUDED_PARTS or path.suffix.casefold() not in TEXT_SUFFIXES or
                    path.resolve() == Path(__file__).resolve()):
                continue
            try:
                text = path.read_text(encoding="utf-8-sig")
            except UnicodeDecodeError:
                continue
            relative = path.relative_to(root).as_posix()
            for match in LEGACY_PATTERN.finditer(text):
                actual[(relative, match.group(0))] += 1
    return actual


class BrandIdentityTests(unittest.TestCase):
    def test_legacy_identity_is_limited_to_explicit_compatibility_and_history(self) -> None:
        self.assertEqual(scan_legacy_identity(REPO_ROOT), ALLOWED_LEGACY_MATCHES)

    def test_scan_skips_only_local_worktree_container(self) -> None:
        with tempfile.TemporaryDirectory(prefix="brand-scope-") as directory:
            root = Path(directory)
            paths = [
                ".claude/worktrees/old-checkout/client/source.cs",
                "client/source.cs",
                "client/worktrees/source.cs",
                "client/.claude/worktrees/source.cs",
                ".claude/instructions.md",
                "docs/archive/history.md",
                "client/new-untracked-source.cs",
            ]
            for relative in paths:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("DuxCommunity", encoding="utf-8")
            self.assertEqual(scan_legacy_identity(root), Counter({
                (relative, "DuxCommunity"): 1 for relative in paths[1:]
            }))

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
