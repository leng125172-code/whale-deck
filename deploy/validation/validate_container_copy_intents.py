#!/usr/bin/env python3
"""Frozen container copy receipts against actual disposable Docker roots.

No installed Agent/RPC/business/UI changes. SIGKILL is restricted to a child
created by this validator. Cleanup proves exact receipt-owned inode/content and
only removes enumerated synthetic data; permanent Root evidence remains.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import select
import signal
import stat
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load


def kill_at(engine, invoke, point):
    ready_read, ready_write = os.pipe(); continue_read, continue_write = os.pipe(); pid = os.fork()
    if pid == 0:
        os.close(ready_read); os.close(continue_write)
        def pause(): os.write(ready_write, b'R'); os.read(continue_read, 1)
        engine.checkpoint = lambda phase: pause() if phase == point else None
        factory = engine.engine
        def instrument(scope, kind):
            inner = factory(scope, kind)
            if point == 'FilePrepared':
                save = inner.save
                def saved(directory, record):
                    save(directory, record)
                    if record['state'] == 'DestinationApplying': pause()
                inner.save = saved
            elif point.startswith('Tree'):
                inner.checkpoint = lambda phase: pause() if point == 'Tree' + phase else None
            return inner
        engine.engine = instrument
        try: invoke()
        except BaseException: os._exit(2)
        os._exit(3)
    os.close(ready_write); os.close(continue_read)
    try:
        assert select.select([ready_read], [], [], 20)[0] and os.read(ready_read, 1) == b'R', 'EXACT_CANDIDATE_FENCE_NOT_REACHED'
        os.kill(pid, signal.SIGKILL); _, status = os.waitpid(pid, 0); pid = None
        assert os.WIFSIGNALED(status) and os.WTERMSIG(status) == signal.SIGKILL
    finally:
        os.close(ready_read); os.close(continue_write)
        if pid is not None:
            with contextlib.suppress(ProcessLookupError): os.kill(pid, signal.SIGKILL)
            os.waitpid(pid, 0)


def remove_tree(path, identity, expected, reader, content):
    assert path.resolve(strict=True) == path and not path.is_symlink()
    assert [path.stat().st_dev, path.stat().st_ino] == identity
    assert {p.name for p in path.iterdir()} == set(expected)
    proofs = {'': reader.fingerprint(path.lstat())}
    for name, kind in expected.items():
        child = path / name; info = child.lstat(); assert not child.is_symlink()
        if kind == 'File': assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and child.read_bytes() == content
        else: assert stat.S_ISDIR(info.st_mode) and not list(child.iterdir())
        proofs[name] = reader.fingerprint(info)
    for name, kind in expected.items():
        child = path / name; assert reader.fingerprint(child.lstat()) == proofs[name]
        if kind == 'File': child.unlink()
        else: child.rmdir()
    assert reader.fingerprint(path.lstat())[:5] == proofs[''][:5] and not list(path.iterdir())
    path.rmdir()


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repository = Path(__file__).resolve().parents[2]
    names = ('whaledeck-container-copies.py', 'whaledeck-container-copy-scope.py', 'whaledeck-container-copy-transport.py',
        'whaledeck-container-files.py', 'whaledeck-files.py', 'whaledeck-container-terminal.py', 'whaledeck-container-upload.py',
        'whaledeck-container-publish.py', 'whaledeck-file-operations.py', 'whaledeck-tree-copies.py', 'whaledeck-file-tree.py')
    frozen = {name: owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/host/' + name]) for name in names}
    for name, raw in frozen.items(): assert (repository / 'deploy/host' / name).read_bytes() == raw
    assert Path(__file__).read_bytes() == owned.common.command(['git', '-C', str(repository), 'show', revision + ':deploy/validation/' + Path(__file__).name])
    evidence = repository / 'deploy/artifacts' / ('container-copy-intents-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700); state = evidence / 'root-state'; state.mkdir(mode=0o700)
    before = owned.common.core_snapshot(); results = []
    with tempfile.TemporaryDirectory(prefix='wd-native-copy-intents-') as temporary:
        bundle = Path(temporary)
        for name, raw in frozen.items():
            path = bundle / name; path.write_bytes(raw); path.chmod(0o750)
        reader = load(bundle / names[3], 'native_copy_intent_reader')
        files = reader.trusted_module(names[4], 'native_copy_intent_files')
        docker = reader.trusted_module(names[5], 'native_copy_intent_docker')
        upload_path = bundle / 'unused-upload'; upload_path.mkdir(mode=0o700)
        upload = load(bundle / names[6], 'native_copy_intent_upload').ContainerUploads(reader, files, upload_path)
        publisher = load(bundle / names[7], 'native_copy_intent_publish').ContainerPublisher(upload, type('Backups', (), {'u': upload})())
        scope_type = load(bundle / names[1], 'native_copy_intent_scope').ContainerCopyScope
        engine = load(bundle / names[0], 'native_copy_intents').ContainerCopies(upload, publisher, scope_type,
            load(bundle / names[8], 'native_copy_intent_operations').FileOperations,
            load(bundle / names[9], 'native_copy_intent_trees').FileTreeCopies,
            load(bundle / names[10], 'native_copy_intent_manifest').FileTreeManifest, state)
        transport = load(bundle / names[2], 'native_copy_intent_transport')
        def execute(action, request): return transport.execute(action, request, reader, files, docker, engine)
        def baseline(fixture, label):
            cid = fixture['identifier']; target = docker.target(docker.api('GET', '/containers/' + cid + '/json'), cid, docker.registry())
            value = {'containerId': cid, 'instanceId': target['instanceId']}
            (evidence / (label + '-baseline.json')).write_text(json.dumps({**value, 'source': str(fixture['folder']),
                'sourceDirectoryProof': reader.fingerprint(fixture['folder'].stat()), 'originalFileProof': fixture['proofs']['original.bin']}))
            return value
        def planned(location, source, target):
            with reader.ContainerRoot(location, files, docker) as session:
                version = reader.inspect({**location, 'path': source}, session, files)['version']
            command = {**location, 'sourcePath': source, 'targetPath': target, 'expectedVersion': version}
            return {'operationId': uuid.uuid4().hex, 'owner': 'B' * 64, 'plan': execute('plan', command)}
        with owned.fixture() as fixture:
            folder = fixture['folder']; location = baseline(fixture, 'live')
            source = folder / 'source-tree'; source.mkdir(mode=0o750); (source / 'empty').mkdir(mode=0o700)
            (source / 'code.bin').write_bytes(fixture['old']); (source / 'code.bin').chmod(0o640)
            for child in (source / 'code.bin', source / 'empty', source):
                os.chown(child, 1000, 1000); os.utime(child, ns=(1700000000123456789, 1700000000123456789))
            source_proof = list(reader.fingerprint(source.stat()))
            private = folder / scope_type.PRIVATE; private_inode = None
            cases = (('file-normal', 'File', None, 'Completed'), ('tree-normal', 'Directory', None, 'Completed'),
                ('outer-preparing', 'File', 'Preparing', 'Aborted'), ('outer-running', 'File', 'Running', 'Unknown'),
                ('file-prepared', 'File', 'FilePrepared', 'Aborted'), ('inner-finished', 'File', 'InnerFinished', 'Completed'),
                ('tree-ready', 'Directory', 'TreeReady', 'Aborted'), ('tree-published', 'Directory', 'TreePublished', 'Completed'))
            for label, kind, cut, expected in cases:
                target_path = 'work/output-' + label; output = folder / ('output-' + label)
                request = planned(location, 'work/original.bin' if kind == 'File' else 'work/source-tree', target_path)
                assert request['plan']['kind'] == kind and (request['plan']['uid'], request['plan']['gid']) == (1000, 1000)
                (evidence / (label + '-request.json')).write_text(json.dumps(request))
                if cut is not None: kill_at(engine, lambda: execute('apply', request), cut)
                else: assert execute('apply', request)['state'] == expected
                result = execute('inspect', request); assert result['state'] == expected
                root_path = state / (request['operationId'] + '.json'); raw = root_path.read_bytes(); record = json.loads(raw)
                assert record['state'] == expected and record['plan'] == request['plan']
                assert execute('apply', request)['state'] == expected and root_path.read_bytes() == raw
                if expected == 'Completed':
                    info = output.lstat(); assert list(reader.fingerprint(info)) == record['publicationIdentity']
                    assert result['bindingCurrent'] and result['version'] == reader.version(location, target_path, info)
                    if kind == 'File':
                        assert output.read_bytes() == fixture['old'] and stat.S_IMODE(info.st_mode) == 0o640
                        assert (info.st_uid, info.st_gid) == (1000, 1000); output.unlink()
                    else: remove_tree(output, [info.st_dev, info.st_ino], {'code.bin': 'File', 'empty': 'Directory'}, reader, fixture['old'])
                else: assert not os.path.lexists(output)
                if kind == 'Directory' and expected == 'Aborted':
                    inner = json.loads((state / 'engines/tree-copies' / (request['operationId'] + '.json')).read_text())
                    remove_tree(private / (request['operationId'] + '.tree-copy'), inner['newIdentity'],
                        {'code.bin': 'File', 'empty': 'Directory'}, reader, fixture['old'])
                if private.exists():
                    info = private.lstat(); assert info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700 and not list(private.iterdir())
                    if private_inode is None: private_inode = [info.st_dev, info.st_ino]
                    else: assert private_inode == [info.st_dev, info.st_ino]
                assert owned.common.fingerprint(folder / 'original.bin') == fixture['proofs']['original.bin'][0]
                results.append({'gate': label, 'operationId': request['operationId'], 'kind': kind, 'state': expected,
                    'realCandidateSigkill': cut is not None, 'rootSha256': hashlib.sha256(raw).hexdigest(),
                    'sourceUnchanged': True, 'replayDidNotApply': True})
                (evidence / 'results.json').write_text(json.dumps(results))
            assert list(reader.fingerprint(source.stat())) == source_proof
            remove_tree(source, source_proof[:2], {'code.bin': 'File', 'empty': 'Directory'}, reader, fixture['old'])
            assert private_inode == [private.stat().st_dev, private.stat().st_ino] and not list(private.iterdir()); private.rmdir()
        with owned.fixture(removable=True) as fixture:
            location = baseline(fixture, 'missing'); folder = fixture['folder']
            request = planned(location, 'work/original.bin', 'work/not-published.bin')
            (evidence / 'missing-request.json').write_text(json.dumps(request))
            kill_at(engine, lambda: execute('apply', request), 'FilePrepared'); fixture['remove_owned']()
            result = execute('inspect', request); assert result['state'] == 'Unknown' and not result['bindingCurrent']
            root_path = state / (request['operationId'] + '.json'); raw = root_path.read_bytes()
            assert execute('apply', request)['state'] == 'Unknown' and root_path.read_bytes() == raw
            inner = json.loads((state / 'engines/operations' / (request['operationId'] + '.json')).read_text())
            private = folder / scope_type.PRIVATE; leaf = request['operationId'] + '.mutation'; pending = private / leaf
            assert inner['state'] == 'Unknown' and files.publication_identity(pending.lstat()) == inner['newMetadata']
            assert pending.read_bytes() == fixture['old'] and not (folder / 'not-published.bin').exists()
            fixture['private'] = scope_type.PRIVATE
            fixture['privateProofs'][leaf] = (owned.common.fingerprint(pending), owned.common.file_digest(pending))
            results.append({'gate': 'actual-target-missing', 'operationId': request['operationId'], 'state': 'Unknown',
                'realCandidateSigkill': True, 'rootSha256': hashlib.sha256(raw).hexdigest(), 'sourceUnchanged': True,
                'fullPrivateBytes': len(fixture['old']), 'replayDidNotApply': True})
    assert owned.common.core_snapshot() == before
    result = {'revision': revision, 'engineOnly': True, 'installedOrRpcOrUi': False, 'checks': results,
        'sourceSha256': {name: hashlib.sha256(raw).hexdigest() for name, raw in frozen.items()},
        'coreUnchanged': True, 'ownedContainersAndSourcesRemoved': True}
    (evidence / 'verified.json').write_text(json.dumps(result)); (evidence / 'verified.json').chmod(0o600)
    print(json.dumps({'evidence': str(evidence), 'gates': len(results), 'engineOnly': True}), flush=True)


if __name__ == '__main__': main()
