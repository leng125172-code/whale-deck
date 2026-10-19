#!/usr/bin/env python3
"""Actual identity/API/installed UDS/content read. Only exact owned fixtures.

WHALEDECK_VALIDATION_PRIVATE_PIPE=1 enables a supervised cookie IPC protocol;
never invoke that mode with stdout attached to logs. Normal mode emits no cookie.
"""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import secrets
import select
import stat
import subprocess
import sys
import tempfile
import time
import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

ROOT = 'whaledeck.files.shared'
BASE = Path('/data/WhaleDeck/files')
PHASE = 'startup'


class ResponseStatusMismatch(AssertionError):
    """Expose only numeric status diagnostics, never response/cookie contents."""
    def __init__(self, expected, actual):
        super().__init__('content-response-status')
        self.expected = expected
        self.actual = actual


def login(base, name, password):
    with io.StringIO() as hidden, contextlib.redirect_stdout(hidden):
        return authenticate(base, name, password)[0]


def snapshot(path):
    value = path.lstat()
    assert stat.S_ISREG(value.st_mode) and value.st_nlink == 1
    with path.open('rb') as file: digest = hashlib.file_digest(file, 'sha256').hexdigest()
    return (value.st_dev, value.st_ino, value.st_uid, value.st_gid, value.st_mode,
        value.st_size, value.st_mtime_ns, value.st_ctime_ns, digest)


def main():
    global PHASE
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    os.umask(0o077)
    base = 'http://192.168.100.13:8080'
    client = login(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, client)
    fixture = Path(tempfile.mkdtemp(prefix='wdv-content-', dir=BASE)); owned = fixture.stat()
    assert stat.S_IMODE(owned.st_mode) == 0o700
    query = 'Api/V1\\[test]*中文'
    canary = 'content-canary-' + secrets.token_hex(16)
    first = ('\ufeffheader\r\n<script>window.__contentPwned=true</script> ' + query + ' ' + canary + '\r\n').encode()
    tail = (query + ' ' + canary).encode()
    second = b'x' * (2 * 1024 * 1024 + 7 - len(tail)) + tail
    files = {'a-first.txt': first, 'b-second.txt': second, 'c-binary.bin': query.encode() + b'\x00',
        'd-invalid.bin': query.encode() + b'\xff', '.env': query.encode() + b' private-fixture-only'}
    original = {}; ordinary_process = None; ordinary = None
    link = fixture / 'f-link'; fifo = fixture / 'g-fifo'
    passed = False
    try:
        PHASE = 'owned-source-fixtures'
        for name, data in files.items(): (fixture / name).write_bytes(data)
        large = fixture / 'e-oversized.log'
        with large.open('wb') as stream: stream.truncate(17 * 1024 * 1024)
        files['e-oversized.log'] = None
        link.symlink_to(fixture / 'a-first.txt'); os.mkfifo(fifo, 0o600)
        link_info = link.lstat(); fifo_info = fifo.lstat()
        original = {name: snapshot(fixture / name) for name in files}
        def search(expected=200, session=client, headers=None, **extra):
            response = session.post(base + '/api/v1/files/content-search', json={
                'rootId': ROOT, 'path': fixture.name, 'query': query, 'matchCase': False,
                'cursor': '', 'take': 50, 'maxFileBytes': 16777216, **extra},
                headers=api.headers if headers is None else headers, timeout=30)
            if response.status_code != expected:
                raise ResponseStatusMismatch(expected, response.status_code)
            return response.json()
        PHASE = 'actual-current-admin-api-workerless-uds-root'
        cursor = ''; hits = []; pages = []
        for _ in range(10):
            result = search(cursor=cursor, take=1); pages.append(result); hits.extend(result['entries'])
            cursor = result['nextCursor']
            if not cursor: break
        else: raise RuntimeError('content-pagination-did-not-terminate')
        assert pages[-1]['complete'] is True
        assert [row['path'] for row in hits] == [fixture.name + '/a-first.txt', fixture.name + '/b-second.txt']
        assert [row['lineNumber'] for row in hits] == [2, 1]
        assert '<script>' in hits[0]['excerpt'] and query in hits[1]['excerpt']
        assert hits[1]['excerptTruncated'] is True
        assert sum(row['readBytes'] for row in pages) == sum(len(files[name]) for name in ('a-first.txt','b-second.txt','c-binary.bin','d-invalid.bin'))
        assert sum(row['skippedNonTextFiles'] for row in pages) == 2
        assert sum(row['skippedOversizedFiles'] for row in pages) == 1
        for row in hits: assert row['entry']['isProtected'] is False
        assert search(query='api/v1', matchCase=True)['entries'] == []
        assert len(search(query='api/v1')['entries']) == 2
        assert search(query='.*')['entries'] == []
        smaller = search(maxFileBytes=1048576)
        assert len(smaller['entries']) == 1 and smaller['skippedOversizedFiles'] == 2
        PHASE = 'cursor-shape-binding-and-file-limit-denials'
        search(400, cursor='invalid/cursor')
        # Valid cursor alphabet but invalid MAC/binding is the existing Agent
        # failed-precondition contract (409), unlike malformed input (400).
        search(409, cursor='invalid.cursor')
        search(400, maxFileBytes=16777217)
        search(403, path=fixture.name + '/.env')
        PHASE = 'independent-csrf-origin-and-anonymous-denials'
        # Retain same Origin so this isolates CSRF rather than failing the
        # earlier same-origin middleware with 403.
        search(400, headers={'Origin': base, 'Accept': 'application/json'})
        search(403, headers={**api.headers, 'Origin': 'http://foreign.invalid'})
        with requests.Session() as anonymous:
            anonymous.trust_env = False
            response = anonymous.post(base + '/api/v1/files/content-search', json={
                'rootId': ROOT, 'path': fixture.name, 'query': query},
                headers={'Origin': base, 'Accept': 'application/json'},
                allow_redirects=False, timeout=20)
            if response.status_code != 401:
                raise ResponseStatusMismatch(401, response.status_code)
        PHASE = 'real-ordinary-cookie-csrf-denial'
        ordinary_process = subprocess.Popen([sys.executable, '-u', str(Path(__file__).with_name('ephemeral_browser_user.py'))],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        name = 'wd-browser-' + secrets.token_hex(16); password = 'Wd!' + secrets.token_hex(40)
        ordinary_process.stdin.write(json.dumps({'username': name, 'password': password, 'administrator': False}) + '\n'); ordinary_process.stdin.flush()
        assert json.loads(ordinary_process.stdout.readline())['ready'] is True
        ordinary = login(base, name, password); password = None; ordinary_api = Api(base, ordinary)
        search(403, session=ordinary, headers=ordinary_api.headers)
        if os.environ.get('WHALEDECK_VALIDATION_PRIVATE_PIPE') == '1':
            assert not sys.stdout.isatty()
            PHASE = 'private-real-browser-channel'
            cookies = []
            for cookie in client.cookies:
                assert cookie.domain.lstrip('.') == '192.168.100.13'
                value = {'name': cookie.name, 'value': cookie.value, 'domain': cookie.domain.lstrip('.'),
                    'path': cookie.path, 'httpOnly': 'HttpOnly' in cookie._rest, 'secure': cookie.secure, 'sameSite': 'Lax'}
                if cookie.expires is not None and cookie.expires > time.time(): value['expires'] = cookie.expires
                cookies.append(value)
            assert cookies
            print(json.dumps({'privateBrowserSession': True, 'base': base, 'cookies': cookies,
                'rootId': ROOT, 'fixture': fixture.name, 'query': query}), flush=True)
            cookies.clear()
            assert select.select([sys.stdin], [], [], 240)[0]
            assert sys.stdin.readline(64).strip() == 'browser-passed'
        PHASE = 'independent-whole-byte-inode-metadata-proof'
        assert {name: snapshot(fixture / name) for name in files} == original
        passed = True
    finally:
        if ordinary is not None: ordinary.cookies.clear(); ordinary.close()
        if ordinary_process is not None:
            ordinary_process.stdin.close(); output = ordinary_process.stdout.read(); hidden_error = ordinary_process.stderr.read()
            ordinary_process.wait(timeout=30); hidden_error = None
            assert ordinary_process.returncode == 0 and any(json.loads(line).get('temporaryBrowserUserRemoved') for line in output.splitlines())
        client.cookies.clear(); client.close()
        if original:
            assert fixture.resolve() == fixture and (fixture.stat().st_dev, fixture.stat().st_ino) == (owned.st_dev, owned.st_ino)
            assert {value.name for value in fixture.iterdir()} == set(files) | {'f-link', 'g-fifo'}
            # Explicit membership/hash/inode checks; never recurse into unknown data.
            assert link.is_symlink() and link.lstat().st_ino == link_info.st_ino and os.readlink(link) == str(fixture / 'a-first.txt')
            assert stat.S_ISFIFO(fifo.lstat().st_mode) and fifo.lstat().st_ino == fifo_info.st_ino
            assert {name: snapshot(fixture / name) for name in files} == original
            link.unlink(); fifo.unlink()
            for name in files: (fixture / name).unlink()
            assert not any(fixture.iterdir()); fixture.rmdir()
        else:
            # Incomplete setup has no verified cleanup manifest. Preserve for exact inspection.
            print(json.dumps({'ownedContentFixturePreserved': fixture.name}), flush=True)
    if passed:
        print(json.dumps({'realFileContentSearch': 'passed', 'actualOidcApiInstalledUdsRoot': True,
            'utf8LiteralPaginationCaseLineExcerptsAndSkippedBudgets': True, 'realOrdinaryCookieCsrfDenied': True,
            'wholeBytesInodeModeAndTimesUnchanged': True, 'exactOwnedFixtureCleanup': True,
            'productionBrowser': os.environ.get('WHALEDECK_VALIDATION_PRIVATE_PIPE') == '1'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        failure = {'realFileContentSearch': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'privateDetailsWithheld': True}
        if isinstance(error, ResponseStatusMismatch):
            failure.update(expectedStatus=error.expected, actualStatus=error.actual)
        print(json.dumps(failure), file=sys.stderr)
        raise SystemExit(1) from None
