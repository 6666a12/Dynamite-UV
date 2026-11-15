"""Keep the independently published website within its curated asset boundary."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


def load_tool(name: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools/site' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class WebsiteTests(unittest.TestCase):
    def test_curated_payload_and_links(self) -> None:
        self.assertEqual([], load_tool('check').check())

    def test_practice_audio_is_reproducible(self) -> None:
        with tempfile.TemporaryDirectory(prefix='du-site-') as directory:
            output = Path(directory) / 'practice.wav'
            load_tool('generate_practice').generate(output)
            self.assertEqual((ROOT / 'site/assets/practice-120bpm.wav').read_bytes(), output.read_bytes())

    def test_license_line_endings_are_portable_but_content_is_checked(self) -> None:
        check = load_tool('check')
        with tempfile.TemporaryDirectory(prefix='du-site-eol-') as directory:
            path = Path(directory) / 'license.txt'
            path.write_bytes(b'Copyright\r\nLicense\r\n')
            self.assertEqual(b'Copyright\nLicense\n', check.asset_bytes(path))
            path.write_bytes(b'Copyright\nLicense\n')
            self.assertEqual(b'Copyright\nLicense\n', check.asset_bytes(path))
            path.write_bytes(b'Changed license\n')
            self.assertNotEqual(b'Copyright\nLicense\n', check.asset_bytes(path))
            binary = path.with_suffix('.woff2')
            binary.write_bytes(b'Binary\r\n')
            self.assertEqual(b'Binary\r\n', check.asset_bytes(binary))

    def test_pages_publishes_only_site(self) -> None:
        workflow = (ROOT / '.github/workflows/pages.yml').read_text(encoding='utf-8')
        self.assertIn('path: site\n', workflow)
        self.assertNotIn('path: .\n', workflow)
        self.assertIn('python -B tools/site/check.py', workflow)
        self.assertIn('pages: write', workflow)


if __name__ == '__main__':
    unittest.main()
