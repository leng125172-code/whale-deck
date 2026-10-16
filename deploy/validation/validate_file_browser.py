#!/usr/bin/env python3
"""Actual OIDC/API/UDS/root tree and search, exact owned fixtures only."""
import hashlib
import json
import os
from pathlib import Path
import stat
import tempfile
import traceback
import requests
from validate_databases import Api
from validate_oidc import authenticate, required_environment

PHASE = 'startup'
ROOT = 'whaledeck.files.shared'
DIRECTORY = Path('/data/WhaleDeck/files')


def main():
    global PHASE
    assert os.geteuid() == 0 and DIRECTORY.resolve() == DIRECTORY and DIRECTORY.is_dir()
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    fixture = Path(tempfile.mkdtemp(prefix='wdv-bff-browser-', dir=DIRECTORY))
    original = fixture.stat(); name = fixture.name
    directories = ['目录', 'report-branch', 'secrets']
    files = {'Report-A.txt': b'ordinary-A', '目录/report-B.bin': b'binary\x00\xff', 'report-branch/other.txt': b'ordinary-B', 'secrets/report-private.txt': b'isolated-test-secret'}
    link = fixture / 'report-link'; fifo = fixture / 'report-fifo'
    def get(route, expected=200, **params):
        response = session.get(base + '/api/v1/files/' + route, params={'rootId': ROOT, **params}, timeout=30)
        assert response.status_code == expected, 'read-status'; return response.json()
    def search(expected=200, **values):
        response = session.post(base + '/api/v1/files/search', json={'rootId': ROOT, 'path': name, 'query': 'report', 'kind': 'All', 'matchCase': False, 'cursor': '', 'take': 1, **values}, headers=api.headers, timeout=30)
        assert response.status_code == expected, 'search-status'; return response.json()
    def collect(**values):
        cursor = ''; hits = []
        for _ in range(20):
            result = search(cursor=cursor, **values); hits.extend(hit['path'] for hit in result['entries']); cursor = result['nextCursor']
            if not cursor:
                assert result['complete'], 'incomplete-search'; return hits
        raise RuntimeError('search-pagination-did-not-terminate')
    try:
        for directory in directories: (fixture / directory).mkdir(mode=0o700)
        for path, content in files.items(): (fixture / path).write_bytes(content)
        link.symlink_to(fixture / 'secrets', target_is_directory=True); os.mkfifo(fifo)
        PHASE = 'real-contract-and-protection'
        schema = session.get(base + '/openapi/v1.json', timeout=30); assert schema.status_code == 200
        assert all('/api/v1/files/' + route in schema.json()['paths'] for route in ('directories', 'search'))
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + '/api/v1/files/directories', params={'rootId': ROOT, 'path': name}, timeout=15).status_code == 401
        body = {'rootId': ROOT, 'path': name, 'query': 'report'}
        assert session.post(base + '/api/v1/files/search', json=body, timeout=15).status_code in (400, 403)
        assert session.post(base + '/api/v1/files/search', json=body, headers={**api.headers, 'Origin': 'http://foreign.invalid'}, timeout=15).status_code == 403
        search(400, query='../escape'); search(400, cursor='bad/invalid'); search(403, path=name + '/secrets')
        PHASE = 'lazy-tree-and-recursive-name-pages'
        first = get('directories', path=name, take=1, after='')
        second = get('directories', path=name, take=1, after=first['nextCursor'])
        assert [entry['name'] for entry in first['entries'] + second['entries']] == ['report-branch', '目录']
        assert first['version'] == second['version'] and not second['nextCursor']
        expected = {name + '/' + path for path in ('Report-A.txt', 'report-branch', '目录/report-B.bin')}
        hits = collect(); assert len(hits) == len(set(hits)) and set(hits) == expected
        assert collect(kind='Directory') == [name + '/report-branch']
        assert set(collect(kind='File')) == expected - {name + '/report-branch'}
        assert collect(query='Report', matchCase=True) == [name + '/Report-A.txt']
        assert collect(query='report-private') == []
        PHASE = 'query-binding-and-version-conflict'
        cursor = search()['nextCursor']; assert cursor
        search(409, cursor=cursor, query='other')
        files['late-name.txt'] = b'owned-directory-change'; (fixture / 'late-name.txt').write_bytes(files['late-name.txt'])
        search(409, cursor=cursor)
        print(json.dumps({'realFileBrowserBff': 'passed', 'oidcApiUdsRoot': 'passed', 'recursiveNamePaginationAndFilters': 'passed',
            'protectedLinkFifoCsrfOriginAndCursorVersion': 'passed', 'fileContentSearchHddAndDirectoryMutation': 'not-covered'}))
    finally:
        current = fixture.lstat(); assert stat.S_ISDIR(current.st_mode) and (current.st_dev, current.st_ino) == (original.st_dev, original.st_ino)
        if os.path.lexists(link):
            assert link.is_symlink() and os.readlink(link) == str(fixture / 'secrets'); link.unlink()
        if os.path.lexists(fifo):
            assert stat.S_ISFIFO(fifo.lstat().st_mode); fifo.unlink()
        for path, content in files.items():
            target = fixture / path
            if target.exists():
                info = target.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(content).digest()
                target.unlink()
        for path in reversed(directories):
            target = fixture / path
            if target.exists(): assert not target.is_symlink() and not any(target.iterdir()); target.rmdir()
        assert fixture.name.startswith('wdv-bff-browser-') and not any(fixture.iterdir()); fixture.rmdir()
        print(json.dumps({'ownedBrowserSearchFixturesRemoved': True}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        frames = traceback.extract_tb(error.__traceback__)
        print(json.dumps({'fileBrowserAcceptance': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None}))
        raise SystemExit(1) from None
