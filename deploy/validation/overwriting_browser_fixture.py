#!/usr/bin/env python3
"""Independent real-browser overwrite fixture; never invokes API or engines.

Creates only a new exact private fixture. Ready/inspect are read-only. Finish
requires both complete byte/identity/archive proofs before removing only those
payloads. Permanent captures, jobs, audit and root receipts remain. Failure
preserves objects; no uncertain browser mutation is retried here.
"""
import hashlib
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import tarfile
import traceback
import uuid
from validate_tree_backup_engine import proof
from validate_file_tree_recycling_bff import plain_file

BASE = Path('/data/WhaleDeck/files')
STATE = Path('/var/lib/whaledeck-files')
POLICY = Path('/etc/whaledeck/file-roots.json')
BACKUPS = Path('/etc/whaledeck/file-backups.json')
ARTIFACTS = Path('/data/GitRepos/whale-deck/deploy/artifacts')
ROOT = 'whaledeck.files.shared'
SIZE = 2 * 1024**2 + 7
MEMBER = '中文.bin'
THEMES = ('light', 'dark')
PHASE = 'startup'


def encode(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'), allow_nan=False).encode()


def hash_value(value):
    assert type(value) is str and re.fullmatch('[A-F0-9]{64}', value)
    return value


def identifier(value):
    assert type(value) is str and re.fullmatch('[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}', value)
    parsed = uuid.UUID(value); assert parsed.int
    return parsed.hex


def payload(marker, theme, kind):
    assert theme in THEMES and kind in ('source', 'target')
    return hashlib.shake_256(('browser-overwrite-v1:' + marker + ':' + theme + ':' + kind).encode()).digest(SIZE)


def paths(marker):
    assert type(marker) is str and re.fullmatch('wdv-browser-overwrite-[a-f0-9]{16}', marker)
    return BASE / marker, ARTIFACTS / ('browser-overwrite-evidence-' + marker[-16:])


def private_json(path, maximum=65536):
    plain_file(path, maximum)
    return json.loads(path.read_bytes())


def save_evidence(folder, name, value):
    assert folder.parent == ARTIFACTS and re.fullmatch('browser-overwrite-evidence-[a-f0-9]{16}', folder.name)
    assert folder.resolve() == folder and folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    assert name in ('baseline.json', 'result.json')
    raw = encode(value); assert len(raw) <= 65536
    descriptor = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(descriptor, 'wb', closefd=False) as stream:
            stream.write(raw); stream.flush(); os.fsync(descriptor)
    finally: os.close(descriptor)
    descriptor = os.open(folder, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try: os.fsync(descriptor)
    finally: os.close(descriptor)


def policy_and_store():
    assert BASE.resolve() == BASE and BASE.is_dir()
    policy = next(item for item in json.loads(POLICY.read_bytes()) if item['id'] == ROOT)
    assert policy['path'] == str(BASE) and {'files-list', 'files-download', 'files-history', 'files-mutate', 'files-upload', 'files-archive', 'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore'} <= set(policy['allowedActions'])
    config = json.loads(BACKUPS.read_bytes())
    store = Path(config['mountPoint']) / config['targetRelative'] / 'directory-archives'
    assert store.resolve() == store and store.is_dir() and store.lstat().st_uid == 0
    return policy, store


def fixture(marker):
    folder, evidence = paths(marker)
    assert folder.resolve() == folder and folder.is_dir() and folder.lstat().st_uid == 0 and stat.S_IMODE(folder.lstat().st_mode) == 0o700
    assert evidence.resolve() == evidence and evidence.lstat().st_uid == 0 and stat.S_IMODE(evidence.lstat().st_mode) == 0o700
    baseline = private_json(evidence / 'baseline.json')
    assert set(baseline) == {'schemaVersion', 'marker', 'identity', 'bytes', 'trees'}
    assert baseline['schemaVersion'] == 1 and baseline['marker'] == marker and baseline['bytes'] == SIZE
    assert proof(folder)[:2] == tuple(baseline['identity'])
    assert set(baseline['trees']) == {kind + '-' + theme for theme in THEMES for kind in ('source', 'target')}
    for line in Path('/proc/self/mountinfo').read_text().splitlines():
        mount = line.split()[4]; assert mount != str(folder) and not mount.startswith(str(folder) + '/')
    return folder, evidence, baseline


def create_fixture():
    policy_and_store()
    capacity = shutil.disk_usage(BASE)
    assert capacity.free >= max(128 * 1024**2, capacity.total // 20 + SIZE * 4)
    marker = 'wdv-browser-overwrite-' + uuid.uuid4().hex[:16]
    folder, evidence = paths(marker)
    assert not os.path.lexists(folder) and not os.path.lexists(evidence)
    os.umask(0o077); folder.mkdir(mode=0o700); evidence.mkdir(mode=0o700)
    print(json.dumps({'ownedBrowserOverwriteFixture': marker}), flush=True)
    trees = {}
    for theme in THEMES:
        for kind in ('source', 'target'):
            name = kind + '-' + theme; tree = folder / name
            tree.mkdir(mode=0o750); tree.chmod(0o750)
            (tree / '空目录').mkdir(mode=0o710); (tree / '空目录').chmod(0o710)
            data = tree / MEMBER
            with data.open('xb') as stream:
                stream.write(payload(marker, theme, kind)); stream.flush(); os.fsync(stream.fileno())
            data.chmod(0o640); os.utime(data, ns=(1712345678123456789, 1712345678123456789))
            trees[name] = {'root': proof(tree), 'file': proof(data), 'empty': proof(tree / '空目录')}
    save_evidence(evidence, 'baseline.json', {'schemaVersion': 1, 'marker': marker, 'identity': proof(folder)[:2], 'bytes': SIZE, 'trees': trees})
    return {'marker': marker, 'rootId': ROOT, 'bytes': SIZE,
        'sources': {theme: 'source-' + theme for theme in THEMES}, 'targets': {theme: 'target-' + theme for theme in THEMES}}


def attest_capture(store, identifier, kind, owner, location, manifest_hash, before, marker, theme, original_kind):
    saved = private_json(store / (identifier + '.json'), 32768)
    assert saved['state'] == 'Verified' and saved['operationId'] == identifier and saved['owner'] == owner
    assert saved.get('captureKind', 'Trash') == kind and saved['source'] == location
    assert saved['manifestSha256'] == manifest_hash and saved['entryCount'] == 3 and saved['totalBytes'] == SIZE
    manifest_path = store / (identifier + '.manifest.json')
    manifest = private_json(manifest_path, 16 * 1024**2)
    assert proof(manifest_path)[-1] == saved['manifestFileSha256']
    assert (manifest['manifestSha256'], manifest['rootId'], manifest['path'], manifest['rootVersion'], manifest['entryCount'], manifest['totalBytes']) == (manifest_hash, ROOT, location['path'], location['expectedVersion'], 3, SIZE)
    assert {entry['path'] for entry in manifest['entries']} == {'', '空目录', MEMBER}
    expected = {'': before['root'], '空目录': before['empty'], MEMBER: before['file']}
    archive_rows, tree_rows = [], []
    for entry in manifest['entries']:
        identity = entry['sourceIdentity']; assert tuple(identity) == tuple(expected[entry['path']][:8])
        assert entry['kind'] == ('File' if entry['path'] == MEMBER else 'Directory')
        assert entry['sizeBytes'] == (SIZE if entry['path'] == MEMBER else 0)
        assert entry['sha256'] == (before['file'][-1] if entry['path'] == MEMBER else '')
        archive_rows.append({key: entry[key] for key in ('path', 'kind', 'sizeBytes', 'sha256')} | {'mode': identity[2] & 0o777, 'uid': identity[3], 'gid': identity[4], 'mtimeNs': identity[7]})
        tree_rows.append({key: entry[key] for key in ('path', 'kind', 'sourceIdentity', 'sizeBytes', 'sha256')})
    content_hash = hashlib.sha256(b'whaledeck-tree-archive-content-v1\n' + encode(archive_rows)).hexdigest().upper()
    assert content_hash == saved['contentSha256']
    tree_hash = hashlib.sha256(b'whaledeck-tree-overwrite-restoring-v1\n' + encode(tree_rows)).hexdigest().upper()
    archive = store / (identifier + '.tar.gz'); plain_file(archive)
    current = proof(archive)
    assert current[:2] == tuple(saved['archiveIdentity']) and current[6] == saved['archiveBytes'] and current[-1] == saved['archiveSha256']
    assert saved['differentSourceDevice'] == (before['file'][0] != current[0])
    with tarfile.open(archive, 'r:gz') as reader:
        members = reader.getmembers()
        assert {member.name.rstrip('/') for member in members} == {'.', '空目录', MEMBER} and all(member.isdir() or member.isfile() for member in members)
        member = next(item for item in members if item.name == MEMBER); assert member.size == SIZE
        with reader.extractfile(member) as stream:
            assert stream.read(SIZE + 1) == payload(marker, theme, original_kind) and not stream.read(1)
    proofs = {}
    for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'):
        path = store / (identifier + suffix); plain_file(path, None if suffix == '.tar.gz' else 16 * 1024**2); proofs[path] = proof(path)
    return saved, content_hash, tree_hash, proofs


def attest_source(folder, baseline, store, theme, capture):
    marker = baseline['marker']; before = baseline['trees']['source-' + theme]
    record = private_json(STATE / 'tree-recycling' / (capture + '.json'), 32768)
    assert record['operationId'] == capture and record['state'] == 'Completed' and all(record[key] for key in ('captureVerified', 'sourceRemoved', 'sourceRetained'))
    location = record['plan']['source']
    assert set(location) == {'rootId', 'path', 'expectedVersion'} and location['rootId'] == ROOT and location['path'] == marker + '/source-' + theme
    hash_value(location['expectedVersion']); hash_value(record['owner'])
    assert not os.path.lexists(folder / ('source-' + theme))
    retained = folder / '.whaledeck-files' / (capture + '.tree-trash')
    assert retained.resolve() == retained and proof(retained)[:2] == tuple(record['retainedIdentity'])
    assert retained.lstat().st_uid == 0 and stat.S_IMODE(retained.lstat().st_mode) == 0o700
    assert {str(path.relative_to(retained)) for path in retained.rglob('*')} == {'空目录', MEMBER}
    data = proof(retained / MEMBER); assert data[:8] == tuple(before['file'][:8]) and data[-1] == before['file'][-1]
    assert (retained / MEMBER).read_bytes() == payload(marker, theme, 'source')
    saved, content, _, proofs = attest_capture(store, capture, 'Trash', record['owner'], location, record['plan']['manifestSha256'], before, marker, theme, 'source')
    assert (saved['archiveBytes'], saved['archiveSha256']) == (record['archiveBytes'], record['archiveSha256'])
    return record, saved, content, retained, proofs


def inspect_ready(marker, theme, capture_id):
    assert theme in THEMES; capture = identifier(capture_id)
    folder, _, baseline = fixture(marker); _, store = policy_and_store()
    attest_source(folder, baseline, store, theme, capture)
    target = folder / ('target-' + theme); before = baseline['trees'][target.name]
    assert proof(target) == tuple(before['root']) and proof(target / MEMBER) == tuple(before['file'])
    assert proof(target / '空目录') == tuple(before['empty'])
    assert {str(path.relative_to(target)) for path in target.rglob('*')} == {'空目录', MEMBER}
    assert (target / MEMBER).read_bytes() == payload(marker, theme, 'target')
    return {'browserOverwriteReadyDiskProof': 'passed', 'theme': theme, 'targetUnchangedAndSourceArchiveVerified': True, 'mutations': 0}


def inspect_completed(marker, operations, finish=False):
    folder, evidence, baseline = fixture(marker); policy, store = policy_and_store()
    assert type(operations) is list and 1 <= len(operations) <= 2
    if finish: assert len(operations) == 2
    themes, identifiers, proofs, expected, files, reports = set(), set(), {}, {'.whaledeck-files'}, {}, []
    for operation in operations:
        assert type(operation) is dict and set(operation) == {'theme', 'captureId', 'overwritingOperationId', 'publishedVersion'}
        theme = operation['theme']; assert theme in THEMES and theme not in themes; themes.add(theme)
        capture, changed = identifier(operation['captureId']), identifier(operation['overwritingOperationId'])
        assert capture != changed and capture not in identifiers and changed not in identifiers; identifiers.update((capture, changed))
        hash_value(operation['publishedVersion'])
        recycled, source_saved, source_content, retained_source, source_proofs = attest_source(folder, baseline, store, theme, capture)
        proofs.update(source_proofs)
        record = private_json(STATE / 'tree-overwrite-restoring' / (changed + '.json'), 32768)
        assert record['operationId'] == changed and record['state'] == 'Completed' and all(record[key] for key in ('backupStarted', 'backupVerified', 'swapStarted', 'originalsRetained'))
        assert record['version'] == operation['publishedVersion']; hash_value(record['owner'])
        plan, command = record['plan'], record['plan']['command']
        assert command['source'] == recycled['plan']['source'] and command['captureId'] == capture and command['captureOwner'] == recycled['owner'] and command['captureKind'] == 'Trash' and command['format'] == 'tar.gz'
        target = command['target']; assert set(target) == {'rootId', 'path', 'expectedVersion'} and target['rootId'] == ROOT and target['path'] == marker + '/target-' + theme
        hash_value(target['expectedVersion'])
        assert (plan['archiveBytes'], plan['archiveSha256'], plan['contentSha256'], plan['entryCount'], plan['totalBytes']) == (source_saved['archiveBytes'], source_saved['archiveSha256'], source_content, 3, SIZE)
        before = baseline['trees']['target-' + theme]
        old = folder / '.whaledeck-files' / (changed + '.tree-overwrite-restore')
        assert old.resolve() == old and proof(old)[:2] == tuple(before['root'][:2]) == tuple(plan['targetIdentity'])
        assert proof(old / MEMBER)[:8] == tuple(before['file'][:8]) and proof(old / MEMBER)[-1] == before['file'][-1]
        assert proof(old / '空目录')[:8] == tuple(before['empty'][:8])
        assert (old / MEMBER).read_bytes() == payload(marker, theme, 'target')
        saved, content, tree, target_proofs = attest_capture(store, changed, 'Overwrite', record['owner'], target, plan['targetManifestSha256'], before, marker, theme, 'target')
        proofs.update(target_proofs)
        assert (saved['archiveBytes'], saved['archiveSha256']) == (record['backupArchiveBytes'], record['backupArchiveSha256'])
        assert (content, tree) == (plan['targetContentSha256'], plan['targetTreeSha256']) and content != tree
        output = folder / ('target-' + theme); current = proof(output / MEMBER); source = baseline['trees']['source-' + theme]
        assert output.resolve() == output and proof(output)[:2] == tuple(record['newIdentity'])
        assert current[:2] not in (tuple(source['file'][:2]), tuple(before['file'][:2])) and current[6] == SIZE and current[-1] == source['file'][-1]
        assert (current[3], current[4], stat.S_IMODE(current[2]), current[7]) == (policy['ownerUid'], policy['ownerGid'], 0o640, source['file'][7])
        assert (output / MEMBER).read_bytes() == payload(marker, theme, 'source')
        for path, mode in ((output, 0o750), (output / '空目录', 0o710)):
            info = path.lstat(); assert stat.S_ISDIR(info.st_mode) and stat.S_IMODE(info.st_mode) == mode and (info.st_uid, info.st_gid) == (policy['ownerUid'], policy['ownerGid'])
        for root in (output, retained_source, old):
            relative = str(root.relative_to(folder)); expected.update((relative, relative + '/空目录', relative + '/' + MEMBER))
            assert {str(path.relative_to(root)) for path in root.rglob('*')} == {'空目录', MEMBER}
            files[root / MEMBER] = proof(root / MEMBER)
        reports.append({'theme': theme, 'bytes': SIZE, 'fullOriginalDisplacedArchivesContentDomainAndOutput': True, 'differentSourceDevice': saved['differentSourceDevice']})
    # An inspect of one completed theme must also account for the other untouched
    # fixture trees, never treat them as permission to delete an unknown object.
    for theme in set(THEMES) - themes:
        for kind in ('source', 'target'):
            root = folder / (kind + '-' + theme); before = baseline['trees'][root.name]
            assert proof(root) == tuple(before['root']) and proof(root / MEMBER) == tuple(before['file'])
            relative = root.name; expected.update((relative, relative + '/空目录', relative + '/' + MEMBER))
    members = list(itertools.islice(folder.rglob('*'), 65)); assert len(members) <= 64
    assert {str(path.relative_to(folder)) for path in members} == expected
    observed = {path: proof(path) for path in members}
    assert all(stat.S_ISDIR(value[2]) or stat.S_ISREG(value[2]) for value in observed.values())
    assert all(proof(path) == value for path, value in {**observed, **proofs, **files}.items())
    if finish:
        assert themes == set(THEMES) and set(files) == {path for path, value in observed.items() if stat.S_ISREG(value[2])}
        save_evidence(evidence, 'result.json', {'marker': marker, 'operations': operations, 'reports': reports, 'fullProofBeforeCleanup': True})
        for path in files: path.unlink()
        for path in sorted((path for path, value in observed.items() if stat.S_ISDIR(value[2])), key=lambda item: len(item.parts), reverse=True): path.rmdir()
        folder.rmdir(); assert not folder.exists()
        assert all(proof(path) == value for path, value in proofs.items())
    return {'realBrowserDirectoryOverwritingDiskObserver': 'passed', 'reports': reports, 'exactOwnedPayloadsRemoved': finish, 'permanentCapturesArchivesJobsAuditAndRootReceiptsRetained': True}


def main():
    global PHASE
    assert os.geteuid() == 0
    raw = sys.stdin.buffer.readline(65537); assert 1 <= len(raw) <= 65536
    request = json.loads(raw); assert type(request) is dict
    PHASE = request.get('action', 'invalid')
    if request == {'action': 'create'}: result = create_fixture()
    elif set(request) == {'action', 'marker', 'theme', 'captureId'} and request['action'] == 'ready': result = inspect_ready(request['marker'], request['theme'], request['captureId'])
    else:
        assert set(request) == {'action', 'marker', 'operations'} and request['action'] in ('inspect', 'finish')
        result = inspect_completed(request['marker'], request['operations'], request['action'] == 'finish')
    print(json.dumps(result, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        frames = [frame for frame in traceback.extract_tb(error.__traceback__) if Path(frame.filename).resolve() == Path(__file__).resolve()]
        print(json.dumps({'browserOverwriteObserver': 'failed', 'phase': PHASE, 'failureType': type(error).__name__, 'sourceLine': frames[-1].lineno if frames else None, 'unknownObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
