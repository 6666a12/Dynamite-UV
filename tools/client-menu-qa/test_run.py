"""Offline checks for the menu QA runner; no engine or encoder is launched."""
import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import run


class RunChecks(unittest.TestCase):
    def test_invalid_engine_fails_before_launch(self):
        for name, is_directory in [('Player.exe', False), ('GodotMono.bin', False), ('GodotMono.exe', True)]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                engine = root / name
                if is_directory:
                    engine.mkdir()
                else:
                    engine.touch()
                args = SimpleNamespace(godot=engine, prepare_only=False)
                with patch.object(run, 'stage', return_value=(root, root, root)), patch.object(run.subprocess, 'Popen') as launch:
                    with self.assertRaisesRegex(RuntimeError, 'Godot Mono console engine'):
                        run.go(args, 'library')
                    launch.assert_not_called()

    def run_with_recording(self, root, record, payload, expected_error=None):
        # Simulate a client QA pass independently of the recording; ffmpeg is unavailable.
        engine = root / 'GodotMono.exe'
        engine.touch()
        (root / 'result.json').write_text('{"pass": true}', encoding='utf8')
        (root / 'player.log').write_text('MIGRATION QA PASS library', encoding='utf8')
        if payload is not None:
            (root / 'capture.avi').write_bytes(payload)
        args = SimpleNamespace(godot=engine, prepare_only=False, record=record, fps=30, timeout=2)
        output = io.StringIO()
        with patch.object(run, 'stage', return_value=(root, root, root)), \
                patch.object(run.subprocess, 'Popen') as launch, \
                patch.object(run.shutil, 'which', return_value=None) as find_encoder, \
                patch.object(run.subprocess, 'run') as encode, contextlib.redirect_stdout(output):
            launch.return_value.wait.return_value = launch.return_value.poll.return_value = 0
            if expected_error:
                with self.assertRaisesRegex(RuntimeError, expected_error):
                    run.go(args, 'library')
                self.assertNotIn('PASS library', output.getvalue())
                find_encoder.assert_not_called()
            else:
                run.go(args, 'library')
                self.assertIn('PASS library', output.getvalue())
                if record:
                    find_encoder.assert_called_once_with('ffmpeg')
                else:
                    find_encoder.assert_not_called()
            launch.assert_called_once()
            encode.assert_not_called()

    @staticmethod
    def avi_bytes(size):
        return b'RIFF' + (size - 8).to_bytes(4, 'little') + b'AVI ' + bytes(size - 12)

    def test_missing_or_invalid_recording_never_prints_pass(self):
        for kind, payload, error in [
            ('absent', None, 'larger than 64 KiB'),
            ('empty', b'', 'larger than 64 KiB'),
            ('exact minimum', self.avi_bytes(65536), 'larger than 64 KiB'),
            ('non-AVI', b'x' * 65537, 'lacks an AVI RIFF header'),
        ]:
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as tmp:
                self.run_with_recording(Path(tmp), True, payload, error)

    def test_valid_avi_header_can_pass_without_optional_encoder(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.run_with_recording(Path(tmp), True, self.avi_bytes(65537))

    def test_non_recording_run_does_not_require_avi(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.run_with_recording(Path(tmp), False, None)


class StagingChecks(unittest.TestCase):
    CONFIG = ('[application]\n\n[autoload]\n'
              'TransitionDirector="*res://scripts/ui/transitions/TransitionDirector.cs"\n\n[display]\n')

    def test_staging_rejects_missing_or_duplicate_configuration(self):
        for section in ('[application]', '[autoload]', '[display]'):
            for text in (self.CONFIG.replace(section, ''), self.CONFIG + section):
                with self.subTest(section=section):
                    with self.assertRaisesRegex(RuntimeError, 'Expected exactly one'):
                        run.staged_project_text(text, 'isolated-test')
        with self.assertRaisesRegex(RuntimeError, 'formal TransitionDirector autoload'):
            run.staged_project_text(self.CONFIG.replace('TransitionDirector=', 'Other='), 'isolated-test')

    def test_staging_preserves_formal_autoload_and_sets_isolated_user_dir(self):
        text = run.staged_project_text(self.CONFIG, 'isolated-test')
        self.assertIn('config/use_custom_user_dir=true', text)
        self.assertIn('config/custom_user_dir_name="isolated-test"', text)
        self.assertIn('TransitionDirector="*res://scripts/ui/transitions/TransitionDirector.cs"', text)
        self.assertIn('MigrationQA="*res://qa_driver.gd"\n\n[display]', text)


if __name__ == '__main__':
    unittest.main()
