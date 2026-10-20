#!/usr/bin/env python3
"""Owned real-browser recycling/restoration fixture and independent observer.

No Engine/API invocations, credentials or broad cleanup. The browser must do
the real public operations; this observer proves complete source/capture/output
bytes separately. Return the raw create JSON unchanged to preserve 64-bit inode
and nanosecond integers. A failed check preserves every object. Permanent
capture catalogue, jobs, audits and Root receipts are never removed here.
"""
import json
import os
from pathlib import Path
import re
import stat
import sys
import tarfile
import uuid
from recycling_browser_fixture import SOURCE, MEMBER, SIZE, ROOT, payload, proof, private_json

BASE = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files')
POLICY = Path('/etc/whaledeck/file-roots.json')
BACKUPS = Path('/etc/whaledeck/file-backups.json')


def identifier(value):
    assert isinstance(value, str) and re.fullmatch('[a-f0-9-]{36}', value)
    parsed = uuid.UUID(value)
    assert parsed.int and str(parsed) == value
    return parsed.hex


def main():
    assert os.geteuid() == 0 and BASE.resolve() == BASE and BASE.is_dir()
    request = json.loads(sys.stdin.buffer.readline(65537))
    if request == {'action': 'create'}:
        marker = 'wd-browser-restoring-' + uuid.uuid4().hex
        folder = BASE / marker
        assert not os.path.lexists(folder)
        folder.mkdir(mode=0o700)
        sources = {}
        for theme in ('light', 'dark'):
            name = SOURCE + '-' + theme
            source = folder / name; source.mkdir(mode=0o750)
            (source / '空目录').mkdir(mode=0o710)
            (source / '代码').mkdir(mode=0o750)
            leaf = source / MEMBER; leaf.write_bytes(payload(marker)); leaf.chmod(0o640)
            os.utime(leaf, ns=(1712345678123456789, 1712345678123456789))
            sources[theme] = {'name': name, 'identity': proof(leaf)}
        print(json.dumps({'marker': marker, 'identity': proof(folder)[:2], 'source': SOURCE,
            'bytes': SIZE, 'sources': sources}, ensure_ascii=False), flush=True)
        return
    assert set(request) == {'action', 'fixture', 'operations'} and request['action'] in ('inspect', 'finish')
    assert isinstance(request['fixture'], str) and len(request['fixture'].encode()) <= 8192
    fixture = json.loads(request['fixture'])
    assert set(fixture) == {'marker', 'identity', 'source', 'bytes', 'sources'}
    marker = fixture['marker']
    assert re.fullmatch('wd-browser-restoring-[a-f0-9]{32}', marker)
    assert fixture['source'] == SOURCE and fixture['bytes'] == SIZE and set(fixture['sources']) == {'light', 'dark'}
    folder = BASE / marker; initial = folder.lstat()
    assert folder.resolve() == folder and proof(folder)[:2] == fixture['identity']
    assert stat.S_ISDIR(initial.st_mode) and initial.st_uid == 0 and stat.S_IMODE(initial.st_mode) == 0o700
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    policy = next(item for item in json.loads(POLICY.read_bytes()) if item['id'] == ROOT)
    assert policy['path'] == str(BASE) and {'files-tree-trash', 'files-tree-restore'} <= set(policy['allowedActions'])
    config = json.loads(BACKUPS.read_bytes())
    store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_absolute() and store.is_dir() and store.lstat().st_uid == 0
    operations = request['operations']
    assert isinstance(operations, list) and len(operations) == 2
    seen, ids, archive_proofs, reports, expected = set(), set(), {}, [], {'.whaledeck-files'}
    for operation in operations:
        assert set(operation) == {'theme', 'captureId', 'restoringOperationId', 'archiveBytes', 'archiveSha256', 'publishedVersion'}
        theme = operation['theme']; assert theme in ('light', 'dark') and theme not in seen; seen.add(theme)
        capture, restored = identifier(operation['captureId']), identifier(operation['restoringOperationId'])
        assert capture not in ids and restored not in ids and capture != restored; ids.update((capture, restored))
        assert re.fullmatch('[A-F0-9]{64}', operation['publishedVersion'])
        source = fixture['sources'][theme]
        assert set(source) == {'name', 'identity'} and source['name'] == SOURCE + '-' + theme
        location = {'rootId': ROOT, 'path': marker + '/' + source['name']}
        recycle = private_json(STATE / 'tree-recycling' / (capture + '.json'))
        assert recycle['operationId'] == capture and recycle['state'] == 'Completed'
        assert recycle['sourceRemoved'] is True and recycle['sourceRetained'] is True
        snapshot = recycle['plan']['source']
        assert set(snapshot) == {'rootId', 'path', 'expectedVersion'}
        assert {key: snapshot[key] for key in ('rootId', 'path')} == location and re.fullmatch('[A-F0-9]{64}', snapshot['expectedVersion'])
        assert recycle['plan']['entryCount'] == 4 and recycle['plan']['totalBytes'] == SIZE
        original = folder / '.whaledeck-files' / (capture + '.tree-trash')
        assert original.resolve() == original and proof(original)[:2] == recycle['retainedIdentity']
        assert original.lstat().st_uid == 0 and stat.S_IMODE(original.lstat().st_mode) == 0o700
        assert {str(path.relative_to(original)) for path in original.rglob('*')} == {'空目录', '代码', MEMBER}
        retained = proof(original / MEMBER)
        assert retained[:8] == source['identity'][:8] and retained[-1] == source['identity'][-1]
        assert (original / MEMBER).read_bytes() == payload(marker)
        saved = private_json(store / (capture + '.json'))
        archive = store / (capture + '.tar.gz'); current = proof(archive)
        assert saved['state'] == 'Verified' and saved['source'] == snapshot and saved['owner'] == recycle['owner']
        assert type(saved['differentSourceDevice']) is bool and saved['archiveIdentity'] == current[:2]
        assert saved['manifestSha256'] == recycle['plan']['manifestSha256']
        assert current[6] == operation['archiveBytes'] == saved['archiveBytes'] == recycle['archiveBytes']
        assert current[-1] == operation['archiveSha256'] == saved['archiveSha256'] == recycle['archiveSha256']
        with tarfile.open(archive, 'r:gz') as reader:
            members = reader.getmembers()
            assert {member.name.rstrip('/') for member in members} == {'.', '空目录', '代码', MEMBER}
            assert all(member.isdir() or member.isfile() for member in members)
            member = next(item for item in members if item.name == MEMBER)
            assert member.isfile() and member.size == SIZE
            with reader.extractfile(member) as stream:
                assert stream.read(SIZE + 1) == payload(marker) and not stream.read(1)
        record = private_json(STATE / 'tree-restoring' / (restored + '.json'))
        assert record['operationId'] == restored and record['state'] == 'Completed' and record['version'] == operation['publishedVersion']
        assert re.fullmatch('[A-F0-9]{64}', record['owner'])
        if theme == 'dark': assert record['owner'] != saved['owner']
        command = record['plan']['command']
        target = source['name'] if theme == 'light' else 'restored-dark'
        assert command['source'] == snapshot and command['target'] == {'rootId': ROOT, 'path': marker + '/' + target}
        assert command['captureId'] == capture and command['captureOwner'] == saved['owner'] and command['captureKind'] == 'Trash'
        assert command['manifestSha256'] == saved['manifestSha256'] and command['format'] == 'tar.gz'
        assert record['plan']['archiveBytes'] == current[6] and record['plan']['archiveSha256'] == current[-1]
        assert record['plan']['entryCount'] == 4 and record['plan']['totalBytes'] == SIZE
        output = folder / target
        assert output.resolve() == output and proof(output)[:2] == record['newIdentity']
        assert {str(path.relative_to(output)) for path in output.rglob('*')} == {'空目录', '代码', MEMBER}
        result = proof(output / MEMBER)
        assert result[:2] != source['identity'][:2] and result[6] == SIZE and result[-1] == retained[-1]
        assert (result[3], result[4], stat.S_IMODE(result[2]), result[7]) == (policy['ownerUid'], policy['ownerGid'], 0o640, source['identity'][7])
        assert (output / MEMBER).read_bytes() == payload(marker)
        for directory, mode in ((output, 0o750), (output / '代码', 0o750), (output / '空目录', 0o710)):
            value = directory.lstat()
            assert stat.S_ISDIR(value.st_mode) and stat.S_IMODE(value.st_mode) == mode and (value.st_uid, value.st_gid) == (policy['ownerUid'], policy['ownerGid'])
        if theme == 'dark': assert not os.path.lexists(folder / source['name'])
        for root in ('.whaledeck-files/' + original.name, target):
            expected.update({root, root + '/空目录', root + '/代码', root + '/' + MEMBER})
        for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
            path = store / (capture + suffix); info = path.lstat()
            assert path.resolve() == path and stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
            archive_proofs[path] = proof(path)
        reports.append({'theme': theme, 'bytes': SIZE, 'fullArchiveOriginalInodeAndNewOutputBytes': True,
            'restoringResourceOwnerDiffersFromCapture': record['owner'] != saved['owner'], 'differentSourceDevice': saved['differentSourceDevice']})
    objects = {path: proof(path) for path in folder.rglob('*')}
    assert {str(path.relative_to(folder)) for path in objects} == expected
    assert all(stat.S_ISREG(value[2]) or stat.S_ISDIR(value[2]) for value in objects.values())
    assert all(proof(path) == value for path, value in {**objects, **archive_proofs}.items())
    if request['action'] == 'finish':
        for path, value in objects.items():
            if stat.S_ISREG(value[2]): path.unlink()
        for path in sorted((path for path, value in objects.items() if stat.S_ISDIR(value[2])), key=lambda path: len(path.parts), reverse=True): path.rmdir()
        folder.rmdir()
        for path in archive_proofs: path.unlink()
    print(json.dumps({'realBrowserDirectoryRestoringDiskObserver': 'passed', 'reports': reports,
        'exactOwnedPayloadsRemoved': request['action'] == 'finish', 'historicalCatalogueJobsAuditAndRootReceiptsRetained': True}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'realBrowserDirectoryRestoringDiskObserver': 'failed', 'failureType': type(error).__name__,
            'unknownObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
