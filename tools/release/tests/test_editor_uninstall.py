"""Execute the real portable uninstaller only in disposable, synthetic packages."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


@unittest.skipUnless(shutil.which('powershell.exe'), 'Windows PowerShell required')
class PortableUninstallTests(unittest.TestCase):
    def package(self, root):
        for name in ('Uninstall.cmd', 'uninstall.ps1'):
            shutil.copy2(ROOT / 'tools/editor-release' / name, root / name)
        (root / 'distribution.json').write_text(json.dumps({'product': 'DynaMaker UV'}))
        (root / 'shell').mkdir()
        (root / 'shell/app.dll').write_bytes(b'fixture program')
        manifest = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in root.rglob('*') if p.is_file()}
        (root / 'files.sha256.json').write_text(json.dumps(manifest))
        return manifest

    def run_uninstall(self, root, *args):
        return subprocess.run(['powershell.exe', '-NoProfile',                                '-File', str(root / 'uninstall.ps1'), *args],
                              capture_output=True, text=True, errors='replace', timeout=40)

    def test_check_and_remove_preserve_extra_chart(self):
        with tempfile.TemporaryDirectory(prefix='uninstall-test-') as temp:
            root = Path(temp) / '中文 space'; root.mkdir()
            self.package(root)
            (root / 'shell/my-chart.json').write_text('user chart')
            check = self.run_uninstall(root, '-CheckOnly')
            self.assertEqual(check.returncode, 0, check.stdout + check.stderr)
            self.assertTrue((root / 'shell/app.dll').exists())
            result = self.run_uninstall(root, '-ConfirmRemoval')
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertFalse((root / 'shell/app.dll').exists())
            self.assertFalse((root / 'Uninstall.cmd').exists())
            self.assertEqual((root / 'shell/my-chart.json').read_text(), 'user chart')

    def test_modified_file_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); self.package(root)
            (root / 'shell/app.dll').write_bytes(b'user changed bytes')
            result = self.run_uninstall(root, '-ConfirmRemoval')
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual((root / 'shell/app.dll').read_bytes(), b'user changed bytes')

    def test_traversal_rejected_before_deletion(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest = self.package(root)
            manifest['../outside.txt'] = '0' * 64
            (root / 'files.sha256.json').write_text(json.dumps(manifest))
            result = self.run_uninstall(root, '-ConfirmRemoval')
            self.assertNotEqual(result.returncode, 0)
            self.assertTrue((root / 'shell/app.dll').exists())

    def test_locked_file_rejected_before_deletion(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); self.package(root)
            # A separate PowerShell process holds the fixture file and invokes the uninstaller.
            script = "$h=[IO.File]::Open($args[0], 'Open', 'Read', 'None'); try { & powershell.exe -NoProfile -File $args[1] -ConfirmRemoval; exit $LASTEXITCODE } finally { $h.Dispose() }"
            runner = root / 'lock-test.ps1'; runner.write_text(script)
            result = subprocess.run(['powershell.exe', '-NoProfile', '-File', str(runner),
                                     str(root / 'shell/app.dll'), str(root / 'uninstall.ps1')], capture_output=True, timeout=40)
            self.assertNotEqual(result.returncode, 0)
            self.assertTrue((root / 'distribution.json').exists())
            self.assertTrue((root / 'shell/app.dll').exists())


if __name__ == '__main__':
    unittest.main()
