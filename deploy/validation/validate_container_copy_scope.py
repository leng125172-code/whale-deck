#!/usr/bin/env python3
"""Frozen copy adapter against a disposable actual Docker ContainerRoot.

No installed helper, RPC, BFF or UI change. No core service restart. Exact
full-content/metadata/inode proofs precede removal of known fixture objects;
unrecognized data remains on disk. Uses an existing immutable image offline.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    names = ('whaledeck-container-copy-scope.py', 'whaledeck-container-files.py', 'whaledeck-files.py',
        'whaledeck-container-terminal.py', 'whaledeck-container-upload.py', 'whaledeck-container-publish.py',
        'whaledeck-file-operations.py', 'whaledeck-tree-copies.py', 'whaledeck-file-tree.py')
    frozen = {name: owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name]) for name in names}
    for name, content in frozen.items(): assert (repository / 'deploy/host' / name).read_bytes() == content
    assert Path(__file__).read_bytes() == owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/validation/' + Path(__file__).name])
    evidence = repository / 'deploy/artifacts' / ('container-copy-scope-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); state = evidence / 'root-state'; state.mkdir(mode=0o700)
    before = owned.common.core_snapshot(); results = []
    with tempfile.TemporaryDirectory(prefix='wd-native-copy-scope-') as temporary:
        bundle = Path(temporary)
        for name, content in frozen.items():
            path = bundle / name; path.write_bytes(content); path.chmod(0o750)
        reader = load(bundle / names[1], 'native_copy_reader')
        files = reader.trusted_module(names[2], 'native_copy_files')
        docker = reader.trusted_module(names[3], 'native_copy_docker')
        upload_type = load(bundle / names[4], 'native_copy_upload').ContainerUploads
        publisher_type = load(bundle / names[5], 'native_copy_publisher').ContainerPublisher
        scope_type = load(bundle / names[0], 'native_copy_scope').ContainerCopyScope
        operations = load(bundle / names[6], 'native_copy_operations').FileOperations
        trees = load(bundle / names[7], 'native_copy_trees').FileTreeCopies
        manifest = load(bundle / names[8], 'native_copy_manifest').FileTreeManifest
        unused = bundle / 'unused-upload'; unused.mkdir(mode=0o700)
        upload = upload_type(reader, files, unused)
        publisher = publisher_type(upload, type('Backups', (), {'u': upload})())
        with owned.fixture() as fixture:
            folder, identifier = fixture['folder'], fixture['identifier']
            target = docker.target(docker.api('GET', '/containers/' + identifier + '/json'), identifier, docker.registry())
            location = {'containerId': identifier, 'instanceId': target['instanceId'], 'path': 'work'}
            baseline = {'containerId': identifier, 'source': str(folder), 'instanceId': target['instanceId'], 'revision': revision}
            (evidence / 'baseline.json').write_text(json.dumps(baseline))
            original = owned.common.fingerprint(folder / 'original.bin')
            with reader.ContainerRoot(location, files, docker) as session:
                private = folder / scope_type.PRIVATE
                for kind, source, destination in (('file', 'original.bin', '复制.bin'), ('tree', 'source-tree', '副本目录')):
                    if kind == 'tree':
                        source_root = folder / source; source_root.mkdir(mode=0o750)
                        (source_root / '空目录').mkdir(mode=0o700)
                        (source_root / '代码.bin').write_bytes(fixture['old']); (source_root / '代码.bin').chmod(0o640)
                        for item in (source_root / '代码.bin', source_root / '空目录', source_root):
                            os.chown(item, 1000, 1000); os.utime(item, ns=(1700000000123456789, 1700000000123456789))
                    source_path = folder / source
                    command = {**location, 'sourcePath': 'work/' + source, 'targetPath': 'work/' + destination,
                        'expectedVersion': reader.version(location, 'work/' + source, source_path.stat())}
                    command.pop('path')
                    scope = scope_type(upload, publisher, session, command, state)
                    assert scope.owner == (1000, 1000)
                    engine = operations(scope) if kind == 'file' else trees(scope, manifest)
                    plan = engine.plan(scope.file_command() if kind == 'file' else scope.tree_command())
                    request = {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': plan}
                    result = engine.apply(request) if kind == 'file' else engine.copy(request)
                    assert result['state'] == 'Completed'
                    record_path = state / ('operations' if kind == 'file' else 'tree-copies') / (request['operationId'] + '.json')
                    record = json.loads(record_path.read_bytes())
                    destination_path = folder / destination; actual = destination_path.lstat()
                    assert [actual.st_dev, actual.st_ino] == record['newIdentity']
                    relative_paths = ('',) if kind == 'file' else ('', '空目录', '代码.bin')
                    expected = {}; source_expected = {}
                    for relative in relative_paths:
                        old, new = source_path / relative if relative else source_path, destination_path / relative if relative else destination_path
                        a, b = old.lstat(), new.lstat()
                        assert stat.S_IFMT(a.st_mode) == stat.S_IFMT(b.st_mode)
                        assert (b.st_uid, b.st_gid, stat.S_IMODE(b.st_mode), b.st_mtime_ns) == (1000, 1000, stat.S_IMODE(a.st_mode), a.st_mtime_ns)
                        assert not new.is_symlink()
                        if stat.S_ISREG(a.st_mode):
                            assert a.st_nlink == b.st_nlink == 1 and old.read_bytes() == new.read_bytes() == fixture['old']
                        else: assert set(p.name for p in old.iterdir()) == set(p.name for p in new.iterdir())
                        expected[relative] = reader.fingerprint(b)
                        source_expected[relative] = reader.fingerprint(a)
                    again = engine.apply(request) if kind == 'file' else engine.copy(request)
                    assert again == result and reader.fingerprint(destination_path.lstat()) == expected['']
                    assert owned.common.fingerprint(folder / 'original.bin') == original
                    assert private.is_dir() and not list(private.iterdir()) and private.stat().st_uid == 0 and stat.S_IMODE(private.stat().st_mode) == 0o700
                    results.append({'kind': kind, 'operationId': request['operationId'], 'state': result['state'],
                        'bytes': len(fixture['old']), 'fullContentVerified': True, 'owner': [1000, 1000], 'replayedSameInode': True,
                        'recordSha256': hashlib.sha256(record_path.read_bytes()).hexdigest()})
                    # Prove all entries before changing membership. A parent
                    # mtime changes after known child removal, so final rmdir
                    # binds its original inode/type/mode/owner and emptiness.
                    targets = [(destination_path, expected)]
                    if kind == 'tree': targets.append((source_path, source_expected))
                    for base, proofs in targets:
                        for relative in relative_paths:
                            item = base / relative if relative else base
                            assert reader.fingerprint(item.lstat()) == proofs[relative]
                        if kind == 'file': base.unlink()
                        else:
                            (base / '代码.bin').unlink(); (base / '空目录').rmdir()
                            assert reader.fingerprint(base.lstat())[:6] == proofs[''][:6] and not list(base.iterdir())
                            base.rmdir()
                # Real Docker read-only root and bind mount root guards.
                for destination in ('tmp/forbidden', 'work'):
                    cmd = {k: v for k, v in location.items() if k != 'path'}
                    cmd.update(sourcePath='work/original.bin', targetPath=destination,
                        expectedVersion=reader.version(location, 'work/original.bin', (folder / 'original.bin').stat()))
                    scope = scope_type(upload, publisher, session, cmd, state)
                    try: operations(scope).plan(scope.file_command())
                    except reader.Rejected as error: assert str(error) in ('CONTAINER_UPLOAD_PUBLISH_READ_ONLY', 'CONTAINER_UPLOAD_PUBLISH_MOUNT_ROOT')
                    else: raise AssertionError('read-only target accepted')
                assert private.stat().st_uid == 0 and stat.S_IMODE(private.stat().st_mode) == 0o700 and not list(private.iterdir()); private.rmdir()
                assert owned.common.fingerprint(folder / 'original.bin') == original
    assert owned.common.core_snapshot() == before
    result = {'revision': revision, 'engineOnly': True, 'installedOrRpcOrUi': False, 'checks': results,
        'sourceSha256': {name: hashlib.sha256(content).hexdigest() for name, content in frozen.items()},
        'coreUnchanged': True, 'ownedContainerAndSourceRemoved': True}
    (evidence / 'verified.json').write_text(json.dumps(result)); (evidence / 'verified.json').chmod(0o600)
    print(json.dumps({'evidence': str(evidence), 'cases': len(results), 'engineOnly': True}), flush=True)


if __name__ == '__main__': main()
