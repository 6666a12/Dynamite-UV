"""Interactive operator CLI. No credential arguments or persistence; HTTPS except local loopback."""
import argparse
import getpass
import http.client
import ipaddress
import json
from pathlib import Path
import subprocess
import sys
from urllib.parse import urlsplit, urlencode
import uuid

MAX_JSON = 1024 * 1024


def origin(value):
    parsed = urlsplit(value)
    if (parsed.username is not None or parsed.password is not None or parsed.query or parsed.fragment
            or parsed.path not in ('', '/') or not parsed.hostname or any(ord(c) < 32 for c in value)):
        raise ValueError('Use a root server address without credentials, query or fragment')
    local = parsed.hostname == 'localhost'
    try:
        local |= ipaddress.ip_address(parsed.hostname).is_loopback
    except ValueError:
        pass
    if parsed.scheme != 'https' and not (parsed.scheme == 'http' and local):
        raise ValueError('Remote servers require HTTPS')
    return parsed


def request(server, method, path, token=None, payload=None, archive=None, headers=None):
    cls = http.client.HTTPSConnection if server.scheme == 'https' else http.client.HTTPConnection
    connection = cls(server.hostname, server.port, timeout=180)
    outgoing = dict(headers or {})
    if token:
        outgoing['Authorization'] = 'Bearer ' + token
    body = None
    try:
        if archive:
            body = Path(archive).open('rb')
            size = Path(archive).stat().st_size
            if size > 128 * 1024 * 1024:
                raise ValueError('Archive exceeds 128 MiB')
            outgoing.update({'Content-Type': 'application/zip', 'Content-Length': str(size)})
        elif payload is not None:
            body = json.dumps(payload).encode('utf-8')
            outgoing['Content-Type'] = 'application/json'
        connection.request(method, path, body, outgoing)
        response = connection.getresponse()  # http.client never follows redirects.
        data = response.read(MAX_JSON + 1)
        if len(data) > MAX_JSON:
            raise ValueError('Response exceeds limit')
        if not 200 <= response.status < 300:
            raise ValueError('Request refused, HTTP ' + str(response.status))
        return json.loads(data) if data else None
    finally:
        if archive and body is not None:
            body.close()
        connection.close()


def safe(value):
    return ''.join(c if c.isprintable() else '?' for c in str(value))


def preflight(path):
    repo = Path(__file__).resolve().parent.parent
    return subprocess.run(['dotnet', 'run', '--no-build', '--project', str(repo / 'server'), '--',
                           '--inspect-package', str(Path(path).resolve())], check=False).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['check', 'list', 'publish', 'withdraw'])
    parser.add_argument('--server', default='http://127.0.0.1:5080')
    parser.add_argument('--file')
    parser.add_argument('--offset', type=int, default=0)
    args = parser.parse_args()
    if args.action in ('check', 'publish'):
        if not args.file:
            parser.error('--file is required')
        code = preflight(args.file)
        if code or args.action == 'check':
            return code
    server = origin(args.server)
    discovery = request(server, 'GET', '/.well-known/dynamite-universe')
    if (discovery.get('protocol') != 'dynamite-universe' or discovery.get('protocolVersion') != 1
            or discovery.get('contracts', {}).get('accounts') != 1):
        raise ValueError('Incompatible server')
    print('Server:', safe(server.netloc), 'Instance:', safe(discovery.get('instanceId')))
    if input('Send credentials to this server? Type YES: ') != 'YES':
        return 1
    username = input('Administrator username: ').strip()
    password = getpass.getpass('Password (not saved): ')
    login = request(server, 'POST', '/api/v1/accounts/login', payload={'username': username, 'password': password})
    password = None
    token = login.get('accessToken')
    if not isinstance(token, str) or len(token) != 64 or any(c not in '0123456789abcdefABCDEF' for c in token):
        raise ValueError('Invalid login response')
    try:
        if args.action == 'list':
            if not 0 <= args.offset <= 10000:
                raise ValueError('Offset out of range')
            rows = request(server, 'GET', '/api/v1/admin/catalog/releases?' + urlencode({'offset': args.offset}), token)
            for row in rows:
                r = row['release']
                print(safe(r['id']), 'PUBLISHED' if row['published'] else 'WITHDRAWN',
                      safe(r['packId']), safe(r['version']), safe(r['title']))
        elif args.action == 'publish':
            version = input('Immutable release version: ').strip()
            rights = input('Authors and redistribution permission (ASCII description): ').strip()
            if not rights.isascii() or not rights or len(rights) > 2000 or any(ord(c) < 32 for c in rights):
                raise ValueError('Rights header must contain printable ASCII, 1..2000 characters')
            if input('Publish this archive? Type PUBLISH: ') != 'PUBLISH':
                return 1
            result = request(server, 'POST', '/api/v1/admin/catalog/releases?' + urlencode({'version': version}),
                             token, archive=args.file, headers={'X-Content-Rights': rights})
            print('Published:', safe(result['id']))
        else:
            release = str(uuid.UUID(input('Exact release UUID to withdraw: ').strip()))
            reason = input('Withdrawal reason: ').strip()
            if not reason or len(reason) > 2000:
                raise ValueError('Reason is required, at most 2000 characters')
            if input('Confirm withdrawal by typing the UUID again: ').strip() != release:
                return 1
            request(server, 'POST', '/api/v1/admin/catalog/releases/' + release + '/withdraw', token,
                    payload={'reason': reason})
            print('Withdrawn:', release)
    finally:
        try:
            request(server, 'POST', '/api/v1/accounts/logout', token)
        except (OSError, ValueError, http.client.HTTPException):
            print('Warning: remote logout not confirmed; session expires automatically.', file=sys.stderr)
        token = None
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError, http.client.HTTPException):
        print('Operation failed. Check inputs, server availability and administrator permissions; no secrets logged.', file=sys.stderr)
        raise SystemExit(1)
