#!/usr/bin/env python3
"""Fresh installed signed restore, exact physical boundaries and owned cleanup.

Only the original instance and retained inode. No business/Auth/UI acceptance.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
import validate_container_recycle_agent as recycling
import container_recycle_agent_io as source_io
import container_recycle_restore_agent_io as restore_io
import container_recycle_restore_rpc_proof as proof
from container_recycle_restore_workflow import source_plan, source_receipt, RESTORE_ACTIONS
from recursive_acl_restore_agent_io import snapshot
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure

SOURCES = ('validate_container_recycle_restore_agent.py', 'verify_container_recycle_restore_agent.py', 'test_container_recycle_restore_workflow.py', 'test_container_recycle_restore_agent_io.py')
PROBE_SOURCES = ('deploy/validation/TerminalProbe/Program.cs', 'deploy/validation/TerminalProbe/ContainerRecycleRestoreProbe.cs')
HOSTS = (*recycling.HOSTS, 'whaledeck-container-recycle-restores.py', 'whaledeck-container-recycle-restore-transport.py')


class Probe(recycling.Probe):
    def __init__(self, binary, evidence, header):
        self.error = (evidence/'rpc.stderr').open('xb'); self.transcript = evidence/'rpc.jsonl'; self.buffer = b''
        self.process = subprocess.Popen([str(binary), 'container-recycle-restores'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.error)
        try: self.send(header); proof.ready_proof(self.receive(), header)
        except BaseException: self.close(False); raise


def baseline(revision):
    return {**recycling.baseline(revision), 'recycleRestoreRoot': snapshot(restore_io.STATE)}


def preserved(before, after, source_id, operations):
    assert type(operations) in (list, tuple) and len(operations) == 2
    for op in (source_id, *operations): proof.identity(op)
    assert len({source_id, *operations}) == 3
    recycling.preserved({k: v for k, v in before.items() if k != 'recycleRestoreRoot'}, {k: v for k, v in after.items() if k != 'recycleRestoreRoot'}, (source_id,))
    old = before['recycleRestoreRoot']; new = after['recycleRestoreRoot']
    assert all(new.get(k) == v for k, v in old.items())
    allowed = {op+suffix for op in operations for suffix in ('.json', '.lock')} | {'claims/'+source_id+'.json'}
    assert set(new)-set(old) <= allowed


def run_independent(repo, e, live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as log:
        subprocess.run([sys.executable, str(repo/'deploy/validation/verify_container_recycle_restore_agent.py'), e.name] + (['--live'] if live else []), stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)


def main(revision, binary_name, probe_revision):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{40}', revision) and re.fullmatch('[a-f0-9]{40}', probe_revision)
    os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'; repo = Path(__file__).resolve().parents[2]
    git = lambda *args: subprocess.check_output(['git', '-c', 'safe.directory='+str(repo), '-C', str(repo), *args])
    assert not git('status', '--porcelain').strip(); sha = git('rev-parse', 'HEAD').decode().strip(); closure = source_closure(repo, sha, seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest() == h for n, h in closure.items())
    for name in HOSTS: assert (Path('/usr/local/libexec')/name).read_bytes() == git('show', revision+':deploy/host/'+name)
    probe_sources = {}
    for name in PROBE_SOURCES:
        raw = git('show', probe_revision+':'+name); assert (repo/name).read_bytes() == raw; probe_sources[name] = hashlib.sha256(raw).hexdigest()
    binary = Path(binary_name)
    assert binary.parent.parent == repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    recycling.trusted(binary); recycling.trusted(binary.parent, True)
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=binary.parent, check=True, capture_output=True)
    recycling.preflight(); before = baseline(revision); assert before['restoreDatabase']['active'] == 0
    e = repo/'deploy/artifacts'/('container-recycle-restore-agent-'+uuid.uuid4().hex); e.mkdir(mode=0o700)
    save = lambda name, value: (e/name).write_text(json.dumps(value, ensure_ascii=False, indent=2))
    save('baseline.json', before); save('source.json', dict(revision=revision, probeRevision=probe_revision, validationRevision=sha, validationSourceSha256=closure, probeSourceSha256=probe_sources, binary=str(binary), binarySha256=hashlib.sha256(binary.read_bytes()).hexdigest()))
    # Before creating any resource, require the actual installation to accept the
    # new capability and prove this exact binary can connect and quit cleanly.
    check = e/'capability'; check.mkdir(mode=0o700)
    header = dict(revision=revision, probeRevision=probe_revision, containerId='a'*64, instanceId='777:2026-11-06T01:02:03Z', coreId=before['core']['/whaledeck-api'][0])
    probe = None; steps = []
    print(json.dumps(dict(installedRecycleRestore='started', evidence=e.name)), flush=True)
    try:
        pre = Probe(binary, check, header); pre.close(True)
        with recycling.owned.fixture() as fixture:
            fixture['retain_unsettled'] = True; folder = fixture['folder']; leaf = folder/'recycle-rpc.bin'
            leaf.write_bytes(hashlib.shake_256(('installed-independent-restore:'+fixture['identifier']).encode()).digest(1024**2+7))
            os.chown(leaf, 1000, 1000); leaf.chmod(0o640); os.setxattr(leaf, 'user.recycle', b'original independent restore bytes'); os.utime(leaf, ns=(1700000000123456789, 1700000000123456789))
            c = json.loads(recycling.owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
            header = {**header, 'containerId': fixture['identifier'], 'instanceId': str(c['State']['Pid'])+':'+c['State']['StartedAt']}
            original = source_io.file_snapshot(leaf); assert original is not None
            save('fixture.json', dict(header=header, folder=str(folder), name=c['Name'].lstrip('/'), original=original))
            reader = recycling.load('/usr/local/libexec/whaledeck-container-files.py', 'independent_restore_reader')
            version = reader.version(header, 'work/recycle-rpc.bin', leaf.stat()); probe = Probe(binary, e, header)
            wire = probe.call(dict(action='source-plan', version=version)); old_plan = source_plan(wire, header); save('source-plan-frame.json', wire)
            typed_equal(old_plan['sourceIdentity'], original['identity']); typed_equal(old_plan['attributes'], original['attributes']); assert old_plan['sha256'] == original['sha256']
            source_id, cancel, normal = (uuid.uuid4().hex for _ in range(3)); assert len({source_id, cancel, normal}) == 3
            source_request = dict(operationId=source_id, owner='B'*64, plan=old_plan)
            for op in (cancel, normal): assert not (restore_io.STATE/(op+'.json')).exists()
            assert not (source_io.STATE/(source_id+'.json')).exists() and not (restore_io.STATE/'claims'/(source_id+'.json')).exists()
            def capture_source(action):
                frame = probe.call(dict(action=action, operationId=source_id, plan=wire['plan'])); receipt = source_receipt(frame, source_request)
                assert receipt['state'] == ('Restored' if steps and steps[-1]['phase'] == 'restore' else 'Trashed')
                records = source_io.read_operation(source_request, receipt, e/('records-'+str(len(steps))), create=True)
                actual = source_io.physical(folder, source_id); source_io.physical_proof(actual, original, records['root'], receipt)
                steps.append(dict(phase='source', action=action, request=source_request, frame=frame, receipt=receipt, records=records, physical=actual)); save('steps.json', steps)
            capture_source('source-apply'); capture_source('source-inspect')
            retained = source_io.physical(folder, source_id); roots = snapshot(restore_io.STATE)
            planned = probe.call(dict(action='plan', sourceOperationId=source_id, boundaries=True)); plan = proof.plan_frame(planned, header, boundaries=True); save('plan-frame.json', planned)
            typed_equal(plan['command']['source'], source_request); typed_equal(source_io.physical(folder, source_id), retained); typed_equal(snapshot(restore_io.STATE), roots)
            typed_equal(source_io.read_operation(source_request, steps[-1]['receipt'], e/'records-1'), steps[-1]['records'])
            refusal = probe.call(dict(action='reject', operationId=cancel, plan=planned['plan'])); assert refusal == dict(kind='rejected', operationId=cancel, refusals=proof.OPERATION_REFUSALS); save('refusals.json', refusal)
            assert not (restore_io.STATE/(cancel+'.json')).exists() and not (restore_io.STATE/'claims'/(source_id+'.json')).exists()
            typed_equal(source_io.physical(folder, source_id), retained)
            for key, action, state in RESTORE_ACTIONS:
                op = cancel if key == 'cancel' else normal; request = dict(operationId=op, owner='C'*64, plan=plan)
                frame = probe.call(dict(action=action, operationId=op, plan=planned['plan'])); receipt = proof.receipt_frame(frame, request); assert receipt['state'] == state
                records = restore_io.read_operation(request, receipt, e/('records-'+str(len(steps))), create=True)
                actual = source_io.physical(folder, source_id); restore_io.physical_proof(actual, original, records, receipt)
                steps.append(dict(phase='restore', action=action, request=request, frame=frame, receipt=receipt, records=records, physical=actual)); save('steps.json', steps)
            capture_source('source-inspect'); closing = probe; probe = None; closing.close(True)
            preserved(before, baseline(revision), source_id, (cancel, normal)); run_independent(repo, e, True)
            typed_equal(source_io.file_snapshot(leaf), steps[-1]['physical']['source']); leaf.unlink()
            private = folder/source_io.PRIVATE; i = private.lstat()
            assert not private.is_symlink() and [i.st_dev, i.st_ino] == steps[-1]['records']['root']['privateIdentity'] and not list(private.iterdir()); private.rmdir(); fixture['retain_unsettled'] = False
        after = baseline(revision); preserved(before, after, source_id, (cancel, normal)); save('after.json', after)
        save('verified.json', dict(revision=revision, signedRpc=True, sourceOperations=1, restoreOperations=2, newReceiptFrames=11, sourceReceiptFrames=3, sourceAndContainerAbsent=not folder.exists(), historiesRetained=True, humanUi=False))
        run_independent(repo, e, False)
        print(json.dumps(dict(installedRecycleRestore='passed', evidence=e.name, newReceiptFrames=11, sourceReceiptFrames=3, humanUi=False)), flush=True)
    except BaseException as error:
        import traceback
        save('failure.json', dict(type=type(error).__name__, frames=[dict(file=Path(f.filename).name, line=f.lineno, function=f.name) for f in traceback.extract_tb(error.__traceback__)])); raise
    finally:
        if probe is not None: probe.close(False)


if __name__ == '__main__':
    try: assert len(sys.argv) == 4; main(*sys.argv[1:])
    except BaseException as error: print(json.dumps(dict(installedRecycleRestore='failed', type=type(error).__name__)), flush=True); raise SystemExit(1) from None
