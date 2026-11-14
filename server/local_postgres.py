"""Optional user-local Windows PostgreSQL installation; no service or system PATH changes."""
import os
from pathlib import Path
import subprocess


def binary_directory():
    configured = os.environ.get('DYNA_POSTGRES_BIN')
    if configured:
        return Path(configured).resolve()
    return Path(os.environ.get('LOCALAPPDATA', '')) / 'DynamiteUniverse' / 'postgresql-18.7-1' / 'pgsql' / 'bin'


def available():
    return (binary_directory() / 'pg_ctl.exe').is_file()


def run(action, local, config):
    binaries = binary_directory()
    data = local / 'pgdata'
    environment = os.environ.copy()
    environment['PGPASSWORD'] = config['password']
    environment['PGCONNECT_TIMEOUT'] = '5'
    environment.pop('PGOPTIONS', None)

    def execute(tool, *args, **kwargs):
        return subprocess.run([str(binaries / (tool + '.exe')), *map(str, args)],
                              env=environment, check=False, **kwargs)

    if action == 'stop':
        if not (data / 'PG_VERSION').is_file():
            return 0
        return execute('pg_ctl', '-D', data, 'stop', '-m', 'fast', '-w').returncode
    if not data.exists():
        password = local / 'init-password'
        try:
            fd = os.open(password, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            with os.fdopen(fd, 'w', encoding='utf-8') as stream:
                stream.write(config['password'])
            result = execute('initdb', '-D', data, '-U', 'dynamite_dev',
                             '--pwfile', password, '--auth=scram-sha-256', '--encoding=UTF8', '--locale=C')
            if result.returncode:
                return result.returncode
        finally:
            password.unlink(missing_ok=True)
    if not (data / 'PG_VERSION').is_file():
        raise RuntimeError('Incomplete PostgreSQL initialization; inspect the local data directory without deleting it.')
    status = execute('pg_ctl', '-D', data, 'status', stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if status.returncode:
        # Startup options override defaults even if initdb was interrupted before configuration.
        result = execute('pg_ctl', '-D', data, '-l', local / 'postgres.log',
                         '-o', "-c listen_addresses=127.0.0.1 -c port=55432", 'start', '-w', '-t', '60')
        if result.returncode:
            return result.returncode
    # Fixed database name, fixed SQL, explicit endpoint, no untrusted interpolation.
    common = ['-h', '127.0.0.1', '-p', '55432', '-U', 'dynamite_dev', '-w']
    exists = execute('psql', *common, '-d', 'postgres', '-X', '-tAc',
                     "SELECT 1 FROM pg_database WHERE datname = 'dynamite'", capture_output=True, text=True)
    if exists.returncode:
        raise RuntimeError('Unable to authenticate the project-local cluster.')
    if exists.stdout.strip() != '1':
        return execute('createdb', *common, 'dynamite').returncode
    return 0
