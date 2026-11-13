"""Offline regressions for the reviewed helper-tool boundaries; no downloads or asset generation."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]


def load(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReviewedToolTests(unittest.TestCase):
    def test_architecture_boundary_is_recursive(self):
        checker = load('architecture_check', 'tools/architecture/check.py')
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for relative in ['presentation/Direct.cs', 'presentation/game/One.cs', 'presentation/game/hud/Deep.cs']:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('GameSession', encoding='utf-8')
            policy = dict(projects={}, source_roots=['presentation'], pure_roots=[], file_limits={},
                          partial_class_limits={}, default_file_limit=100,
                          source_boundaries=[dict(files=['presentation/**/*.cs'], forbidden='GameSession', reason='forbidden')])
            errors, _, _ = checker.inspect(root, policy)
            self.assertEqual(len(errors), 3)

    def test_pack_media_cannot_overwrite_chart(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            chart = root / 'chart.json'
            audio = root / 'audio.wav'
            cover = root / 'chart_hard.json'
            chart.write_text('{}')
            audio.write_bytes(b'audio')
            cover.write_bytes(b'cover')
            output = root / 'out'
            result = subprocess.run([sys.executable, '-I', str(ROOT / 'tools/pack_chart.py'),
                '--id', 'fixture', '--title', 'Fixture', '--diff', 'hard', '--level', '1',
                '--chart', str(chart), '--audio', str(audio), '--cover', str(cover), '--out', str(output)],
                capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(output.exists())

    def test_range_validation_and_source_bound_resume(self):
        downloader = load('pardl_checked', 'tools/pardl.py')

        class Response:
            def __init__(self, status=206, content_range='bytes 4-7/8', data=b'DATA'):
                self.status, self.headers, self.data = status, {'Content-Range': content_range}, data
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, size):
                value, self.data = self.data[:size], self.data[size:]
                return value

        with tempfile.TemporaryDirectory() as folder, patch.object(downloader.time, 'sleep'), patch('builtins.print'):
            destination = Path(folder) / 'part'
            for status, header in [(200, 'bytes 4-7/8'), (206, 'bytes 0-3/8')]:
                with patch.object(downloader.urllib.request, 'urlopen', side_effect=lambda *a, **k: Response(status, header)):
                    with self.assertRaises(RuntimeError):
                        downloader.fetch_range('https://example.invalid/first', 4, 7, destination, [0], 0)
            with patch.object(downloader.urllib.request, 'urlopen', side_effect=lambda *a, **k: Response()) as network:
                downloader.fetch_range('https://example.invalid/first', 4, 7, destination, [0], 0, '"v1"')
                downloader.fetch_range('https://example.invalid/first', 4, 7, destination, [0], 0, '"v1"')
                self.assertEqual(network.call_count, 1)
                downloader.fetch_range('https://example.invalid/second', 4, 7, destination, [0], 0, '"v1"')
                self.assertEqual(network.call_count, 2)
                downloader.fetch_range('https://example.invalid/second', 4, 7, destination, [0], 0, '"v2"')
                self.assertEqual(network.call_count, 3)

    def test_asset_allowlist_excludes_stale_and_outcomes(self):
        # Only load the pure output-list function, without importing Pillow or running generation.
        import ast
        source = ast.parse((ROOT / 'tools/grade-assets/build_runtime_results.py').read_text(encoding='utf-8'))
        function = next(node for node in source.body if isinstance(node, ast.FunctionDef) and node.name == 'runtime_assets')
        namespace = {'DEST': Path('results'), 'SOURCES': dict.fromkeys(['omega', 'X', 'Y', 'S', 'A', 'B', 'C', 'D'])}
        exec(compile(ast.Module(body=[function], type_ignores=[]), '<runtime_assets>', 'exec'), namespace)
        assets = namespace['runtime_assets']()
        self.assertEqual(len(assets), 29)
        self.assertEqual(len(set(assets)), 29)
        self.assertNotIn(Path('results/outcomes/frame.png'), assets)
        self.assertNotIn(Path('results/stale.svg'), assets)


if __name__ == '__main__':
    unittest.main()
