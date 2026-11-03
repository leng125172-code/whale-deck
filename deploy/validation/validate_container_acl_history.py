#!/usr/bin/env python3
"""Candidate reads of real ACL HDD metadata in one exactly owned container.

Only the existing ACL engine creates the five preimages. No installed change,
RPC, human session, business job, restoration or mutation by the new reader.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid

import validate_container_acl as existing
from validate_container_file_staging import load
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity

NAMES = (*existing.NAMES, 'whaledeck-container-acl-history.py')


def save(root, name, value):
    p = root / name
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')
    p.chmod(0o600)


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 3
    source, runtime = sys.argv[1:]
    assert all(re.fullmatch('[a-f0-9]{40}', value) for value in (source, runtime))
    os.umask(0o077)
    os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    repo = Path(__file__).resolve().parents[2]
    assert not subprocess.check_output(['git', '-C', str(repo), 'status', '--porcelain']).strip()
    blobs = {name: subprocess.check_output(['git', '-C', str(repo), 'show', source + ':deploy/host/' + name]) for name in NAMES}
    assert all((repo / 'deploy/host' / name).read_bytes() == raw for name, raw in blobs.items())
    assert Path(__file__).read_bytes() == subprocess.check_output(['git', '-C', str(repo), 'show', source + ':deploy/validation/' + Path(__file__).name])
    assert not Path('/usr/local/libexec/whaledeck-container-acl-history.py').exists()
    no_active_work()
    core, hosts = existing.owned.common.core_snapshot(), host_identity(runtime)
    evidence = repo / 'deploy/artifacts' / ('container-acl-history-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    state = evidence / 'candidate-state'
    state.mkdir(mode=0o700)
    rows = []
    save(evidence, 'source.json', dict(sourceRevision=source, runtimeRevision=runtime,
        modules={name: hashlib.sha256(raw).hexdigest() for name, raw in blobs.items()}, core=core, hosts=hosts))
    try:
        with tempfile.TemporaryDirectory(prefix='wd-acl-history-') as tmp:
            bundle = Path(tmp)
            for name, raw in blobs.items():
                p = bundle / name; p.write_bytes(raw); p.chmod(0o750)
            reader = load(bundle / NAMES[1], 'history_native_container')
            files = reader.trusted_module(NAMES[0], 'history_native_files')
            docker = reader.trusted_module(NAMES[2], 'history_native_docker')
            uploads = load(bundle / NAMES[3], 'history_native_upload').ContainerUploads(reader, files, state)
            publisher = load(bundle / NAMES[4], 'history_native_publish').ContainerPublisher(uploads, type('UnusedContentHistory', (), {'u': uploads})())
            storage = load(bundle / NAMES[5], 'history_native_storage').FileBackups(files)
            directories = load(bundle / NAMES[6], 'history_native_directories').ContainerDirectories
            permission = load(bundle / NAMES[7], 'history_native_permission')
            acl = load(bundle / NAMES[8], 'history_native_acl')
            history = acl.acl_history_type(permission)(storage)
            engine = acl.container_acl_type(directories)(uploads, publisher, state, history)
            catalog = load(bundle / NAMES[9], 'history_native_catalog').ContainerAclHistoryReader(engine)
            with existing.owned.fixture() as fixture:
                folder = fixture['folder']; (folder / 'acl-directory').mkdir(mode=0o750)
                content = hashlib.shake_256(('history-candidate:' + fixture['identifier']).encode()).digest(2 * 1024**2 + 7)
                for relative, kind in existing.PATHS.items():
                    p = folder / relative
                    if kind == 'File':
                        p.write_bytes(content); os.setxattr(p, 'user.whaledeck-acl', b'preserve ordinary attribute')
                    os.chown(p, 2100, 3100); p.chmod(0o640 if kind == 'File' else 0o750)
                for relative in existing.PATHS: os.utime(folder / relative, ns=(1700000000123456789, 1700000000123456789))
                target = docker.target(docker.api('GET', '/containers/' + fixture['identifier'] + '/json'), fixture['identifier'], docker.registry())
                location = dict(containerId=fixture['identifier'], instanceId=target['instanceId'])
                baseline = dict(source=str(folder), **location, physical=existing.inventory(folder, reader, acl, content))
                with storage.target(create_namespace=False, read_only=True) as (_, fd): baseline['hddDevice'] = os.fstat(fd).st_dev
                save(evidence, 'baseline.json', baseline)
                operations = [('file-original', 'acl-file.bin', 'access', existing.entries()),
                    ('file-noop', 'acl-file.bin', 'access', existing.entries()),
                    ('file-mask', 'acl-file.bin', 'access', existing.entries(6)),
                    ('directory-default', 'acl-directory', 'default', existing.entries()),
                    ('directory-clear', 'acl-directory', 'default', [])]
                with reader.ContainerRoot(location, files, docker) as session:
                    for label, relative, kind, desired in operations:
                        path = 'work/' + relative
                        command = {**location, 'path': path, 'aclType': kind, 'entries': desired,
                            'expectedVersion': reader.version(location, path, (folder / relative).stat())}
                        request = dict(operationId=uuid.uuid4().hex, owner='A' * 64, plan=engine.plan(command, session))
                        result = engine.apply(request, session)
                        assert result['state'] == 'Completed' and result['historyVerified']
                        with history.target(False) as (_, fd):
                            history.read(fd, request)
                            metadata = storage.read_json(fd, request['operationId'] + '.json')
                        save(evidence, request['operationId'] + '.hdd.json', metadata)
                        physical = existing.inventory(folder, reader, acl, content)
                        raw = (state / (request['operationId'] + '.json')).read_bytes()
                        query = {**location, 'path': path, 'captureId': request['operationId'], 'expectedHistoryDigest': result['historyDigest']}
                        observed = catalog.read(query, session)
                        assert observed['accessAcl'] == request['plan']['accessAcl'] and observed['defaultAcl'] == request['plan']['defaultAcl']
                        assert observed['historyDigest'] == metadata['historyDigest']
                        for forbidden in ('plan', 'owner', 'sourceIdentity', 'targetProof'): assert forbidden not in observed
                        try: catalog.read({**query, 'expectedHistoryDigest': '0' * 64}, session)
                        except reader.Rejected: pass
                        else: raise AssertionError('BAD_HISTORY_DIGEST_ACCEPTED')
                        assert existing.inventory(folder, reader, acl, content) == physical
                        assert (state / (request['operationId'] + '.json')).read_bytes() == raw
                        rows.append(dict(label=label, request=request, result=result, read=observed, physical=physical,
                            rootSha256=hashlib.sha256(raw).hexdigest()))
                        save(evidence, 'results.json', rows)
                    # Actual HDD pagination contains other prior fixtures, but
                    # only this CID and exact path may appear in the response.
                    pages = []
                    for relative, expected in (('acl-file.bin', 3), ('acl-directory', 2)):
                        after, version, captures = '', '', []
                        for _ in range(100):
                            page = catalog.listing({**location, 'path': 'work/' + relative, 'after': after, 'take': 1, 'expectedVersion': version}, session)
                            pages.append(page); captures += [v['captureId'] for v in page['entries']]
                            if page['complete']: break
                            assert page['nextAfter'] > after
                            after, version = page['nextAfter'], page['version']
                        else: raise AssertionError('PAGINATION_DID_NOT_COMPLETE')
                        assert len(captures) == len(set(captures)) == expected
                        assert set(captures) == {r['request']['operationId'] for r in rows if r['request']['plan']['command']['path'] == 'work/' + relative}
                    save(evidence, 'pages.json', pages)
                final = existing.inventory(folder, reader, acl, content)
                assert final == rows[-1]['physical']
                save(evidence, 'physical-final.json', final)
                for relative in ('acl-directory/existing.bin', 'acl-file.bin'):
                    p = folder / relative
                    assert list(reader.fingerprint(p.lstat())) == final[relative]['identity']
                    p.unlink()
                p = folder / 'acl-directory'
                assert not list(p.iterdir()) and list(reader.fingerprint(p.lstat()))[:5] == final['acl-directory']['identity'][:5]
                p.rmdir()
            assert not folder.exists() and existing.owned.common.core_snapshot() == core and host_identity(runtime) == hosts
        no_active_work()
        save(evidence, 'accepted.json', dict(passed=True, sourceRevision=source, runtimeRevision=runtime,
            candidateOnly=True, signedRpcOrUi=False, restorationExecuted=False, captures=5, readonlyReads=5,
            pages=len(pages), exactCleanup=True, coreAndHostsUnchanged=True))
        print(json.dumps(dict(passed=True, candidateOnly=True, captures=5, readonlyReads=5, evidence=str(evidence))))
    except BaseException as error:
        save(evidence, 'failed.json', dict(passed=False, errorType=type(error).__name__, completedCaptures=len(rows)))
        raise


if __name__ == '__main__': main()
