"""CLI integration against synthetic excerpt metadata and PNGs; no Godot or licensed assets."""
import json
from pathlib import Path
import shutil
import subprocess
from tempfile import TemporaryDirectory
import unittest

from PIL import Image

DOTNET = shutil.which('dotnet')
PROJECT = Path(__file__).resolve().parent / 'uvsb-local-check'


@unittest.skipUnless(DOTNET, 'dotnet SDK is required for the UVSB checker integration tests')
class UvsbLocalCheckTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        build = subprocess.run([DOTNET, 'build', '-c', 'Release', str(PROJECT)],
                               capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=120)
        if build.returncode:
            raise AssertionError(build.stdout + build.stderr)
        cls.dll = PROJECT / 'bin/Release/net9.0/UvsbLocalCheck.dll'

    def setUp(self):
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.pack = Path(temporary.name)
        self.root = self.pack / 'storyboard'
        (self.root / 'assets').mkdir(parents=True)
        self.script = self.root / 'official-excerpt.uvsb'
        (self.root / 'index.json').write_text(json.dumps({'version': 0, 'charts': {'ramification': self.script.name}}),
                                            encoding='utf-8')

    def fixture(self, count, size=(2, 2)):
        with Image.new('1', size, color=1) as image:
            image.save(self.root / 'assets/a0.png')
        image = (self.root / 'assets/a0.png').read_bytes()
        for i in range(1, count):
            (self.root / f'assets/a{i}.png').write_bytes(image)
        script = 'storyboard 0\n' + ''.join(f'asset "a{i}" file="assets/a{i}.png"\n' for i in range(count))
        script += 'sprite "visible" asset="a0" clock="media-ms" {\n    active from=0 until=10\n}\n'
        self.script.write_text(script, encoding='utf-8')
        (self.root / 'conversion-report.json').write_text(json.dumps({
            'objects': 1, 'texture_pixels': count * size[0] * size[1], 'range_ms': [0, 10],
        }), encoding='utf-8')

    def checker(self, *args):
        return subprocess.run([DOTNET, str(self.dll), str(self.pack), *args],
                              capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=15)

    def assert_accepted(self, *args):
        result = self.checker(*args)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('UVSB parse+compile OK', result.stdout)

    def assert_rejected(self, expected, *args):
        result = self.checker(*args)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(expected, result.stderr)

    def test_strict_utf8_and_bounded_raw_source(self):
        self.fixture(1)
        self.assert_accepted('5')
        valid = self.script.read_bytes()
        self.script.write_bytes(b'// malformed comment \xff\n' + valid)
        self.assert_rejected('Invalid UVSB UTF-8')
        self.script.write_bytes(valid + b' ' * (2 << 20))
        self.assert_rejected("Invalid size for 'official-excerpt.uvsb'")

    def test_preview_compatibility_does_not_cap_streamed_declarations(self):
        self.fixture(3, (4096, 2048))  # Above the old 64 MiB ceiling; exactly 96 MiB RGBA8.
        self.assert_accepted('5', '--preview-budget')
        self.assert_accepted()
        self.fixture(4, (4096, 2048))
        self.assert_accepted()
        self.assert_rejected('Preview texture pixel limit', '--preview-budget')
        self.fixture(129)
        self.assert_accepted()
        self.assert_rejected('Preview asset count limit', '--preview-budget')

    def test_streamed_declarations_keep_per_image_limits(self):
        self.fixture(1, (4097, 1))
        self.assert_rejected('Invalid PNG dimensions')
        self.fixture(1)
        with (self.root / 'assets/a0.png').open('ab') as stream:
            stream.truncate((16 << 20) + 1)
        self.assert_rejected('Invalid PNG byte count')


if __name__ == '__main__':
    unittest.main()
