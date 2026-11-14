"""Local PostgreSQL lifecycle (user-local Windows binaries or Docker); never deletes stored data."""
import importlib.util
import argparse
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parent
LOCAL = ROOT / '.local' / 'database.json'
_spec = importlib.util.spec_from_file_location('local_postgres', ROOT / 'local_postgres.py')
local_postgres = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(local_postgres)


def configuration(create=False, backend=None):
    if not LOCAL.exists():
        if not create:
            raise RuntimeError('Run python server/dev.py up first.')
        LOCAL.parent.mkdir(parents=True, exist_ok=True)
        if os.name == 'nt':
            # Restrict inherited Windows ACLs before writing any password or cluster data.
            sid = subprocess.check_output(['powershell', '-NoProfile', '-Command',
                '[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value'], text=True).strip()
            subprocess.run(['icacls', str(LOCAL.parent), '/inheritance:r', '/grant:r',
                f'*{sid}:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F'], check=True, stdout=subprocess.DEVNULL)
        # No reset/regeneration on existing installations: losing this file requires recovery.
        value = {'password': secrets.token_hex(32), 'instanceId': str(uuid.uuid4()),
                 'backend': backend or ('native' if local_postgres.available() else 'docker')}
        fd = os.open(LOCAL, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            json.dump(value, stream)
    value = json.loads(LOCAL.read_text(encoding='utf-8'))
    if len(value['password']) != 64 or any(c not in '0123456789abcdef' for c in value['password']):
        raise RuntimeError('Invalid local password configuration; restore the original file.')
    uuid.UUID(value['instanceId'])
    return value


def lifecycle_backend(config, requested=None):
    stored = config.get('backend')
    if stored not in (None, 'native', 'docker'):
        raise RuntimeError('Invalid stored database backend.')
    if stored and requested and stored != requested:
        raise RuntimeError('Database backend cannot be switched; use the existing installation.')
    backend = stored or requested
    if backend is None:
        raise RuntimeError('Legacy configuration: specify --backend native or --backend docker to confirm the existing database owner.')
    if backend == 'native' and not local_postgres.available():
        raise RuntimeError('The selected native PostgreSQL binaries are unavailable; backend was not switched.')
    if backend == 'docker' and not shutil.which('docker'):
        raise RuntimeError('The selected Docker backend is unavailable; backend was not switched.')
    if backend == 'docker' and (LOCAL.parent / 'pgdata').exists():
        raise RuntimeError('Native cluster data exists; refusing to operate a different backend.')
    if stored is None:
        updated = dict(config, backend=backend)
        temporary = LOCAL.with_name('database-' + uuid.uuid4().hex + '.tmp')
        try:
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                json.dump(updated, stream)
            os.replace(temporary, LOCAL)
        finally:
            temporary.unlink(missing_ok=True)
    return backend


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['up', 'stop', 'migrate', 'run', 'test', 'grant-admin', 'mock-download'])
    parser.add_argument('--backend', choices=['native', 'docker'], help='Confirm the owner of a legacy database configuration (up/stop only)')
    parser.add_argument('--username', help='Existing account to promote with the local operator command')
    args = parser.parse_args()
    if args.action == 'grant-admin' and not args.username:
        parser.error('--username is required for grant-admin')
    if args.backend and args.action not in ('up', 'stop'):
        parser.error('--backend applies only to up/stop')
    config = configuration(create=args.action == 'up', backend=args.backend)
    backend = lifecycle_backend(config, args.backend) if args.action in ('up', 'stop') else None
    environment = os.environ.copy()
    environment['POSTGRES_PASSWORD'] = config['password']
    environment['Storage__CatalogRoot'] = str(LOCAL.parent / 'catalog')
    environment['Instance__Id'] = config['instanceId']
    environment['ConnectionStrings__Postgres'] = (
        'Host=127.0.0.1;Port=55432;Database=dynamite;Username=dynamite_dev;'
        f"Password={config['password']};SSL Mode=Disable")
    compose = ['docker', 'compose', '--project-name', 'dynamite-universe-dev', '-f', str(ROOT / 'compose.yaml')]
    if args.action == 'stop':
        if backend == 'native':
            return local_postgres.run('stop', LOCAL.parent, config)
        return subprocess.run(compose + ['stop'], env=environment, check=False).returncode
    if args.action == 'up':
        result = (local_postgres.run('up', LOCAL.parent, config) if backend == 'native' else
                  subprocess.run(compose + ['up', '-d', '--wait'], env=environment, check=False).returncode)
        if result:
            return result
    if args.action in ('test', 'mock-download'):
        environment['DYNA_TEST_POSTGRES'] = environment['ConnectionStrings__Postgres']
        mode = '--postgres' if args.action == 'test' else '--local-mock-download'
        command = ['dotnet', 'run', '--project', str(ROOT.parent / 'tools/server-tests/ServerTests.csproj'), '--', mode]
    else:
        command = ['dotnet', 'run', '--project', str(ROOT / 'DynamiteUniverse.Server.csproj')]
        if args.action == 'grant-admin':
            command += ['--', '--grant-admin', args.username]
        elif args.action != 'run':
            command += ['--', '--migrate']
    return subprocess.run(command, env=environment, check=False).returncode


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
    except (OSError, ValueError, KeyError):
        print('Local database operation failed. Check the selected backend and server/.local/database.json; credentials were not printed.', file=sys.stderr)
        raise SystemExit(1)
