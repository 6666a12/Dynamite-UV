from pathlib import Path
import importlib.util
import json
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('du_dev_test', ROOT / 'server/dev.py')
dev = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dev)


class DatabaseLifecycleTests(unittest.TestCase):
    def test_recorded_docker_owner_survives_native_install(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(dev, 'LOCAL', Path(tmp) / 'database.json'), \
                patch.object(dev.local_postgres, 'available', return_value=True), \
                patch.object(dev.shutil, 'which', return_value='docker'):
            self.assertEqual(dev.lifecycle_backend({'backend': 'docker'}), 'docker')
            with self.assertRaises(RuntimeError):
                dev.lifecycle_backend({'backend': 'docker'}, 'native')

    def test_unavailable_owner_does_not_fall_back(self):
        with patch.object(dev.local_postgres, 'available', return_value=False), \
                patch.object(dev.shutil, 'which', return_value='docker'):
            with self.assertRaises(RuntimeError):
                dev.lifecycle_backend({'backend': 'native'})

    def test_legacy_requires_confirmation_and_persists_it(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(dev, 'LOCAL', Path(tmp) / 'database.json'), \
                patch.object(dev.local_postgres, 'available', return_value=True):
            config = {'password': '0' * 64, 'instanceId': '11111111-1111-1111-1111-111111111111'}
            dev.LOCAL.write_text(json.dumps(config), encoding='utf-8')
            with self.assertRaises(RuntimeError):
                dev.lifecycle_backend(config)
            self.assertEqual(dev.lifecycle_backend(config, 'native'), 'native')
            self.assertEqual(json.loads(dev.LOCAL.read_text(encoding='utf-8')), dict(config, backend='native'))

    def test_up_overrides_endpoint_after_interrupted_initialization(self):
        calls = []
        def run(command, **kwargs):
            calls.append(command)
            return SimpleNamespace(returncode=3 if 'status' in command else 0, stdout='1\n')
        with tempfile.TemporaryDirectory() as tmp, patch.object(dev.local_postgres.subprocess, 'run', side_effect=run):
            root = Path(tmp)
            (root / 'pgdata').mkdir()
            (root / 'pgdata/PG_VERSION').write_text('18', encoding='utf-8')
            self.assertEqual(dev.local_postgres.run('up', root, {'password': 'fixture'}), 0)
            start = next(c for c in calls if 'start' in c)
            self.assertEqual(start[start.index('-o') + 1], '-c listen_addresses=127.0.0.1 -c port=55432')

    def test_store_selection_keeps_existing_rows(self):
        source = (ROOT / 'client/scripts/ui/store/StoreScreen.cs').read_text(encoding='utf-8')
        select = source.split('private void Select(StoreRelease release)', 1)[1].split('private void Download', 1)[0]
        self.assertNotIn('Rebuild()', select)
        self.assertIn('row.Select(row.Release.Id == release.Id)', select)
