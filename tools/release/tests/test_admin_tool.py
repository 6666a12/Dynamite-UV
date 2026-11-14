from pathlib import Path
import importlib.util
import io
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('du_admin', ROOT / 'server/admin.py')
admin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(admin)


class AdminToolTests(unittest.TestCase):
    def test_origin_rejects_unsafe_addresses(self):
        for address in ['http://remote.example/', 'https://user:secret@example.org/',
                        'https://example.org/path', 'https://example.org/?token=secret']:
            with self.assertRaises(ValueError):
                admin.origin(address)
        self.assertEqual(admin.origin('http://127.0.0.1:5080').hostname, '127.0.0.1')

    def test_list_logs_out_without_printing_token(self):
        responses = [
            {'protocol': 'dynamite-universe', 'protocolVersion': 1, 'contracts': {'accounts': 1}, 'instanceId': 'fixture'},
            {'accessToken': 'a' * 64},
            [{'release': {'id': 'release', 'packId': 'pack', 'version': 'v1', 'title': 'Mock'}, 'published': False}],
            None,
        ]
        with patch.object(admin.sys, 'argv', ['admin.py', 'list']), \
                patch('builtins.input', side_effect=['YES', 'operator']), \
                patch.object(admin.getpass, 'getpass', return_value='not-logged-password'), \
                patch.object(admin, 'request', side_effect=responses) as request, \
                patch('sys.stdout', new_callable=io.StringIO) as output:
            self.assertEqual(admin.main(), 0)
            self.assertEqual(request.call_args.args[2], '/api/v1/accounts/logout')
            self.assertIn('WITHDRAWN', output.getvalue())
            self.assertNotIn('a' * 64, output.getvalue())
            self.assertNotIn('not-logged-password', output.getvalue())

    def test_withdraw_requires_exact_confirmation(self):
        responses = [
            {'protocol': 'dynamite-universe', 'protocolVersion': 1, 'contracts': {'accounts': 1}, 'instanceId': 'fixture'},
            {'accessToken': 'a' * 64}, None,
        ]
        with patch.object(admin.sys, 'argv', ['admin.py', 'withdraw']), \
                patch('builtins.input', side_effect=['YES', 'operator', '11111111-1111-1111-1111-111111111111', 'test', 'NO']), \
                patch.object(admin.getpass, 'getpass', return_value='secret'), \
                patch.object(admin, 'request', side_effect=responses) as request, \
                patch('sys.stdout', new_callable=io.StringIO):
            self.assertEqual(admin.main(), 1)
            self.assertFalse(any('/withdraw' in call.args[2] for call in request.call_args_list))
            self.assertEqual(request.call_args.args[2], '/api/v1/accounts/logout')
