#!/usr/bin/env python3
"""Opt-in installed signed restore gate, NEW owned fixture only. No human/UI.

Freeze this gate before use. Live independent verification precedes exact source
cleanup. Source and restore transcripts/archives never overwrite one another.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_recursive_acl_agent import Probe as SourceProbe, host_identity, NAMES
from validate_container_recursive_acl import inventory, preserved, remove_proved, PATHS, file_acl
from validate_container_recursive_acl_browser import no_active_work
from validate_container_recursive_acl_faults import preservation_snapshot
from validate_container_upload_recovery_agent import trusted
from verify_container_recursive_acl_agent import proto_plan as source_plan
from recursive_acl_restore_rpc_proof import proto_plan, ready_proof, digest
from recursive_acl_restore_record_proof import records_proof, receipt_proof
from recursive_acl_restore_agent_io import installed, read_bundle, snapshot, hdd_snapshot
from acl_source_apply_missing_proof import source_closure

PHASE = 'startup'
HOST_NAMES = tuple(dict.fromkeys((*NAMES, 'whaledeck-container-backups.py', 'whaledeck-container-acl-transport.py',
    'whaledeck-container-recursive-acl-transport.py', 'whaledeck-container-acl-history.py', 'whaledeck-container-recursive-acl-history.py',
    'whaledeck-container-recursive-acl-history-transport.py', 'whaledeck-container-recursive-acl-restore.py',
    'whaledeck-container-recursive-acl-restore-transport.py', 'whaledeck-privileged')))
PROBE_SOURCES = ('deploy/validation/TerminalProbe/Program.cs', 'deploy/validation/TerminalProbe/ContainerRecursiveAclRestoreProbe.cs', 'deploy/validation/TerminalProbe/ContainerRecursiveAclProbe.cs')
SEEDS = ('validate_container_recursive_acl_restore_agent.py', 'verify_container_recursive_acl_restore_agent.py', 'test_recursive_acl_restore_record_probe_scope.py', 'test_recursive_acl_restore_agent_probe_scope.py', 'test_recursive_acl_restore_gate_probe_scope.py')


class RestoreProbe(SourceProbe):
    def __init__(self, binary, evidence, header):
        self.error = (evidence/'rpc.stderr').open('xb'); self.transcript = evidence/'rpc.jsonl'; self.buffer = b''
        self.process = subprocess.Popen([str(binary), 'container-recursive-acl-restore'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.error)
        try: self.send(header); ready_proof(self.receive(), header)
        except BaseException: self.close(False); raise


def fixture_bytes(identifier):
    assert re.fullmatch('[a-f0-9]{64}', identifier)
    return hashlib.shake_256(('recursive-acl-restore-installed:' + identifier).encode()).digest(2*1024**2 + 7)


def attrs_digest():
    return digest(b'whaledeck-container-acl-attributes-v1\n', [['user.whaledeck-recursive', base64.b64encode(b'preserve ordinary attribute').decode()]])


def keep_old(current, previous):
    assert all(current.get(name) == value for name, value in previous.items()), 'PREVIOUS_PRIVATE_FILES_CHANGED'


def core_health(core):
    value = owned.common.command(['docker', 'inspect', '--format', '{{.Name}} {{.Id}} {{.State.Health.Status}}', *owned.common.CORE_NAMES])
    result = {}
    for line in value.splitlines():
        name, identifier, health = line.split(); assert name in core and identifier == core[name][0] and health == 'healthy' and core[name][2] == 'running'
        assert name not in result; result[name] = health
    assert set(result) == set(core); return result


def idle_helpers():
    deadline = time.monotonic() + 10
    while True:
        value = subprocess.check_output(['systemctl', 'list-units', '--all', '--plain', '--no-legend', 'whaledeck-container-recursive-acl-*'], text=True, timeout=10)
        if not value.strip(): return 0
        assert time.monotonic() < deadline, 'RECURSIVE_ACL_HELPERS_NOT_IDLE'
        time.sleep(0.2)


def check_sources(repo, revision, probe_revision, binary):
    for value in (revision, probe_revision): assert re.fullmatch('[a-f0-9]{40}', value)
    assert binary.parent.parent == repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    trusted(binary); trusted(binary.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=binary.parent, check=True, capture_output=True, timeout=30)
    host_hashes = {}; probe_hashes = {}
    for name in HOST_NAMES:
        path = Path('/usr/local/libexec')/name; trusted(path)
        raw = subprocess.check_output(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + name])
        assert path.read_bytes() == raw; host_hashes[name] = hashlib.sha256(raw).hexdigest()
    for name in PROBE_SOURCES:
        raw = subprocess.check_output(['git', '-C', str(repo), 'show', probe_revision + ':' + name]); assert (repo/name).read_bytes() == raw
        probe_hashes[name] = hashlib.sha256(raw).hexdigest()
    return host_hashes, probe_hashes


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 4; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    revision, binary, probe_revision = sys.argv[1], Path(sys.argv[2]), sys.argv[3]; repo = Path(__file__).resolve().parents[2]
    validation_revision = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
    assert not subprocess.check_output(['git', '-C', str(repo), 'status', '--porcelain']).strip(), 'FREEZE_RESTORE_GATE_FIRST'
    validation_hashes = source_closure(repo, validation_revision, seeds=SEEDS)
    assert all(hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest() == sha for name, sha in validation_hashes.items())
    host_hashes, probe_hashes = check_sources(repo, revision, probe_revision, binary)
    io = installed(); trusted(io.sourceState, True); trusted(io.restoreState, True)
    core = owned.common.core_snapshot(); assert len(core) == 11; health = core_health(core); idle_helpers(); hosts = host_identity(revision); no_active_work()
    evidence = repo/'deploy/artifacts'/('container-recursive-acl-restore-agent-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    for name in ('source-rpc', 'restore-rpc', 'records'): (evidence/name).mkdir(mode=0o700)
    print(json.dumps(dict(installedRestoreGate='started', evidence=evidence.name)), flush=True)
    sources = []; rows = []; source_probe = restore_probe = None
    try:
        old = preservation_snapshot(io.storage); old_source = snapshot(io.sourceState); old_restore = snapshot(io.restoreState)
        old_hdd = hdd_snapshot(io, True); directories = snapshot(Path('/var/lib/whaledeck-container-directories'))
        with owned.fixture() as fixture:
            PHASE = 'prepare-new-owned-fixture'; root = fixture['folder']/'recursive-tree'; root.mkdir(mode=0o750); (root/'sub').mkdir(mode=0o700)
            content = fixture_bytes(fixture['identifier'])
            for index, (relative, kind) in enumerate(PATHS.items()):
                path = root/relative
                if kind == 'File': path.write_bytes(content)
                os.chown(path, 2100+index, 3100+index); path.chmod(0o750 if kind == 'Directory' else 0o600 if '/' in relative else 0o640)
                os.setxattr(path, 'user.whaledeck-recursive', b'preserve ordinary attribute')
            def write(relative, kind, entries):
                fd = os.open(root/relative, os.O_RDONLY | os.O_NOFOLLOW)
                try: io.acl.write_acl(fd, kind, entries)
                finally: os.close(fd)
            named = file_acl(2); named[1].update(id=0, permissions=0)
            write('one.bin', 'access', named); write('sub/中文.bin', 'access', io.acl.base_acl(0o600)); write('sub', 'default', named)
            for relative in PATHS: os.utime(root/relative, ns=(1700000000123456789, 1700000000123456789))
            inspected = io.docker.api('GET', '/containers/' + fixture['identifier'] + '/json')
            location = dict(containerId=fixture['identifier'], instanceId=io.docker.target(inspected, fixture['identifier'], io.docker.registry())['instanceId'])
            b = dict(revision=revision, probeRevision=probe_revision, validationSourceRevision=validation_revision, validationSourceSha256=validation_hashes,
                sourceHashes=host_hashes, probeSourceSha256=probe_hashes, probeBinarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(), **location,
                source=str(fixture['folder']), root=str(root), name=inspected['Name'].lstrip('/'), bytesPerFile=len(content), sha256=hashlib.sha256(content).hexdigest(), original=inventory(root, io.reader, content, io.acl),
                core=core, coreHealth=health, hosts=hosts, oldProtected=old, oldSourceRoot=old_source, oldRestoreRoot=old_restore, oldRestoreHdd=old_hdd, directoryRoot=directories)
            with io.storage.target(create_namespace=False, read_only=True) as (_, fd): b.update(sourceDevice=root.stat().st_dev, hddDevice=os.fstat(fd).st_dev)
            (evidence/'baseline.json').write_text(json.dumps(b)); header = dict(revision=revision, probeRevision=probe_revision, **location, coreId=core['/whaledeck-api'][0])
            source_probe = SourceProbe(binary, evidence/'source-rpc', header); restore_probe = RestoreProbe(binary, evidence/'restore-rpc', header)
            def save_rows():
                (evidence/'sources.json').write_text(json.dumps(sources)); (evidence/'results.json').write_text(json.dumps(rows))
            def capture(label, kind, profile='named'):
                global PHASE
                PHASE = label; before = inventory(root, io.reader, content, io.acl)
                response = source_probe.call(dict(action='plan', aclType=kind, profile=profile, version=io.reader.version(location, 'work/recursive-tree', root.stat()), boundaries=False))
                assert response['kind'] == 'plan' and response['boundaryRefusals'] == 0
                wire = json.loads(response['plan']); plan = source_plan(wire); operation = uuid.uuid4().hex
                assert plan['rootIdentity'] == before['']['identity'] and plan['changeCount'] > 0 and not (io.sourceState/(operation+'.json')).exists()
                reply = source_probe.call(dict(action='apply', operationId=operation, plan=wire)); assert reply['kind'] == 'receipt'
                row = dict(label=label, request=dict(operationId=operation, owner='B'*64, plan=plan), result=json.loads(reply['result']), before=before, after=inventory(root, io.reader, content, io.acl), wirePlan=wire)
                preserved(before, row['after'], kind); bundle, frame = read_bundle(io, row['request'], False, evidence/'records'/operation, create=True)
                row.update(metadata=bundle['metadata'], sourceFrame=frame); assert records_proof(row, bundle, restore=False, attribute_digest=attrs_digest()) == 4
                sources.append(row); save_rows(); return row
            def restore(label, source, discard=False, boundaries=False):
                global PHASE
                PHASE = label; before = inventory(root, io.reader, content, io.acl); operation = uuid.uuid4().hex
                response = restore_probe.call(dict(action='plan', captureId=source['request']['operationId'], historyDigest=source['result']['historyDigest'], version=io.reader.version(location, 'work/recursive-tree', root.stat()), boundaries=boundaries))
                assert response['kind'] == 'plan' and response['boundaryRefusals'] == (22 if boundaries else 0)
                wire = json.loads(response['plan']); plan = proto_plan(wire); reference = dict(operationId=operation, plan=wire)
                assert inventory(root, io.reader, content, io.acl) == before and not (io.restoreState/(operation+'.json')).exists()
                if boundaries:
                    previous = hdd_snapshot(io, True); denied = restore_probe.call(dict(action='reject', **reference))
                    assert denied == dict(kind='rejected', operationId=operation, refusals=23)
                    assert hdd_snapshot(io, True) == previous and inventory(root, io.reader, content, io.acl) == before and not (io.restoreState/(operation+'.json')).exists()
                result = restore_probe.call(dict(action='discard' if discard else 'apply', **reference)); assert result['kind'] == 'receipt'
                row = dict(label=label, sourceId=source['request']['operationId'], request=dict(operationId=operation, owner='B'*64, plan=plan), result=json.loads(result['result']), before=before, after=inventory(root, io.reader, content, io.acl), wirePlan=wire)
                bundle, _ = read_bundle(io, row['request'], True, evidence/'records'/operation, create=True)
                assert records_proof(row, bundle, restore=True, source=source, frame=source['sourceFrame'], attribute_digest=attrs_digest()) == (0 if discard else 4)
                for action in ('inspect', 'apply', 'discard'):
                    response = restore_probe.call(dict(action=action, **reference)); assert response['kind'] == 'receipt' and response['operationId'] == operation
                    receipt_proof(json.loads(response['result']), row['request'], bundle['root'], restore=True, binding=not discard, replayed=action != 'discard')
                    assert inventory(root, io.reader, content, io.acl) == row['after']; read_bundle(io, row['request'], True, evidence/'records'/operation)
                rows.append(row); save_rows(); return row
            access = capture('source-access', 'access'); first = restore('access-complete', access, boundaries=True)
            assert 'system.posix_acl_access' not in os.listxattr(root/'sub/中文.bin')
            noop = restore('access-noop', access); assert noop['request']['plan']['changeCount'] == 0 and noop['before'] == noop['after']
            default = capture('source-default', 'default'); restore('default-complete', default)
            newer = capture('source-newer-access', 'access', 'mask'); assert newer['before'] != newer['after']
            canceled = restore('discard', access, discard=True); assert canceled['request']['plan']['changeCount'] > 0
            PHASE = 'late-old-apply-after-new-write'; before = inventory(root, io.reader, content, io.acl)
            old_bundle, _ = read_bundle(io, first['request'], True, evidence/'records'/first['request']['operationId'])
            late = restore_probe.call(dict(action='apply', operationId=first['request']['operationId'], plan=first['wirePlan'])); assert late['kind'] == 'receipt'
            receipt_proof(json.loads(late['result']), first['request'], old_bundle['root'], restore=True, binding=False, replayed=True)
            assert inventory(root, io.reader, content, io.acl) == before; read_bundle(io, first['request'], True, evidence/'records'/first['request']['operationId'])
            (evidence/'late.json').write_text(json.dumps(dict(before=before, after=inventory(root, io.reader, content, io.acl), response=late)))
            closing = source_probe; source_probe = None; closing.close(True)
            closing = restore_probe; restore_probe = None; closing.close(True)
            (evidence/'physical-final.json').write_text(json.dumps(before)); (evidence/'accepted.json').write_text(json.dumps(dict(normalSourceCaptures=3, distinctRestoreOperations=4, restoreReceipts=17, completedRestores=3, discardedRestores=1, planRefusals=22, operationRefusals=23, humanOrUi=False)))
            for filename, value in [('protected-final.json', preservation_snapshot(io.storage)), ('source-root-final.json', snapshot(io.sourceState)), ('restore-root-final.json', snapshot(io.restoreState)), ('restore-hdd-final.json', hdd_snapshot(io, True))]: (evidence/filename).write_text(json.dumps(value))
            PHASE = 'independent-live-before-cleanup'
            with (evidence/'independent-live.log').open('x') as log: subprocess.run([sys.executable, str(repo/'deploy/validation/verify_container_recursive_acl_restore_agent.py'), '--live', evidence.name], stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
            remove_proved(root, io.reader, content, before, io.acl)
        PHASE = 'independent-final-after-cleanup'
        with (evidence/'independent-final.log').open('x') as log: subprocess.run([sys.executable, str(repo/'deploy/validation/verify_container_recursive_acl_restore_agent.py'), evidence.name], stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
        (evidence/'verified.json').write_text(json.dumps(dict(installedRestoreGate='passed', distinctRestoreOperations=4, normalSourceCaptures=3, restoreReceipts=17, sourceAndContainerAbsent=True, humanOrUi=False)))
        print(json.dumps(dict(installedRestoreGate='passed', evidence=evidence.name)), flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps(dict(phase=PHASE, type=type(error).__name__, completedSources=len(sources), completedRestores=len(rows))))
        print(json.dumps(dict(installedRestoreGate='failed', evidence=evidence.name, phase=PHASE, type=type(error).__name__)), flush=True); raise
    finally:
        if source_probe is not None: source_probe.close(False)
        if restore_probe is not None: restore_probe.close(False)


if __name__ == '__main__': main()
