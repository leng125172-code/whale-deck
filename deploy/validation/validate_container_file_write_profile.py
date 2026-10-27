#!/usr/bin/env python3
"""Actual fixed systemd writer profile on owned content; production not installed.

Freeze the helper/modules. Substitute only private QA state/policy/entry paths;
all sandbox/capability flags and invocation-specific stopping stay unchanged.
This is profile+CLI evidence, not installed Agent/SSO/BFF/UI evidence.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load


def main():
    if os.geteuid() != 0: raise RuntimeError('Linux root required')
    host = Path(__file__).resolve().parents[1] / 'host'
    names = ('whaledeck-container-file-write.py', 'whaledeck-container-upload.py', 'whaledeck-container-backups.py',
        'whaledeck-container-publish.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py',
        'whaledeck-files.py', 'whaledeck-file-backups.py', 'whaledeck-privileged')
    frozen = {name: (host / name).read_bytes() for name in names}
    hashes = {name: hashlib.sha256(content).hexdigest() for name, content in frozen.items()}
    with tempfile.TemporaryDirectory(prefix='wdv-write-profile-', dir='/var/lib') as temporary:
        bundle = Path(temporary); library = bundle / 'libexec'; library.mkdir(mode=0o700)
        state = bundle / 'state'; state.mkdir(mode=0o700)
        policy = bundle / 'policy.json'
        for name, content in frozen.items():
            path = library / name; path.write_bytes(content); path.chmod(0o750)
        files = load(library / 'whaledeck-files.py', 'write_profile_files')
        backups = load(library / 'whaledeck-file-backups.py', 'write_profile_backups')
        storage = backups.FileBackups(files); configuration = storage.configuration()
        with storage.target() as (_, parent):
            namespace = 'wdv-container-write-profile-' + uuid.uuid4().hex
            os.mkdir(namespace, 0o700, dir_fd=parent); os.fsync(parent)
            target = Path(configuration['mountPoint']) / configuration['targetRelative'] / namespace
            target_identity = target.stat().st_dev, target.stat().st_ino
        policy.write_text(json.dumps({**configuration, 'targetRelative': configuration['targetRelative'] + '/' + namespace})); policy.chmod(0o600)
        replacements = {
            'whaledeck-container-file-write.py': [("STATE = Path('/var/lib/whaledeck-container-uploads')", "STATE = Path(" + repr(str(state)) + ")")],
            'whaledeck-file-backups.py': [("CONFIGURATION = '/etc/whaledeck/file-backups.json'", "CONFIGURATION = " + repr(str(policy)))],
            'whaledeck-privileged': [('/usr/local/libexec/whaledeck-container-file-write.py', str(library / 'whaledeck-container-file-write.py')),
                ('/var/lib/whaledeck-container-uploads', str(state))]
        }
        for name, changes in replacements.items():
            value = frozen[name].decode()
            for before, after in changes:
                assert value.count(before) == 1; value = value.replace(before, after)
            (library / name).write_text(value)
        helper = library / 'whaledeck-privileged'
        active = []
        def call(action, request, content=None, expected_error=None):
            transport = uuid.uuid4().hex; active.append(transport)
            body = json.dumps(request).encode() + b'\n'
            if content is not None:
                import base64
                for offset in range(0, len(content), 32768):
                    body += (json.dumps({'type': 'chunk', 'offset': offset,
                        'data': base64.b64encode(content[offset:offset+32768]).decode()}) + '\n').encode()
                body += b'{"type":"commit"}\n'
            process = subprocess.Popen([str(helper), 'container-file-write', action, transport], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try: output, _ = process.communicate(body, timeout=70)
            except subprocess.TimeoutExpired:
                unit = 'whaledeck-container-file-write-' + transport + '.service'
                invocation = owned.common.command(['systemctl', 'show', unit, '-p', 'InvocationID', '--value']).decode().strip()
                if invocation:
                    assert re.fullmatch('[a-f0-9]{32}', invocation)
                    owned.common.command([str(helper), 'container-file-write-stop', transport, invocation])
                process.communicate(timeout=10)
                raise RuntimeError('owned profile deadline exceeded')
            rows = [json.loads(line) for line in output.splitlines()]
            assert len(rows) == 2 and rows[0]['type'] == 'driver' and re.fullmatch('[a-f0-9]{32}', rows[0]['invocationId'])
            if expected_error:
                assert process.returncode != 0 and rows[1] == {'type': 'error', 'code': expected_error}
            else: assert process.returncode == 0 and rows[1].get('type') != 'error'
            # Even after normal exit, exact invocation stopping must be safe.
            owned.common.command([str(helper), 'container-file-write-stop', transport, rows[0]['invocationId']])
            return rows[1]
        binding, expected_digest, completed = None, None, False
        try:
            with owned.fixture() as fixture:
                actual = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
                instance = str(actual['State']['Pid']) + ':' + actual['State']['StartedAt']
                location = {'containerId': fixture['identifier'], 'instanceId': instance, 'path': 'work/original.bin'}
                plan = call('target', location)
                replacement = hashlib.shake_256(b'actual-systemd-container-write-profile').digest(owned.common.SIZE)
                request = {**location, 'operationId': uuid.uuid4().hex, 'owner': 'B' * 64,
                    'sizeBytes': len(replacement), 'sha256': hashlib.sha256(replacement).hexdigest().upper(),
                    'expectedVersion': plan['expectedVersion'], 'expectedParentVersion': plan['expectedParentVersion'], 'expectedTargetProof': plan['targetProof']}
                staged = call('stage', request, replacement); assert staged['state'] == 'Staged' and staged['targetProof'] == plan['targetProof']
                binding = {key: request[key] for key in ('containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof')}
                assert call('inspect', binding)['state'] == 'Staged'
                before = (fixture['folder'] / 'original.bin').stat()
                result = call('publish', binding); assert result['state'] == 'Published' and result['bindingCurrent'] and result['previousRetained']
                output = fixture['folder'] / 'original.bin'
                retained = fixture['folder'] / '.whaledeck-container-files-v1' / (binding['operationId'] + '.upload')
                assert output.read_bytes() == replacement and (output.stat().st_uid, output.stat().st_gid, stat.S_IMODE(output.stat().st_mode)) == (1000, 1000, 0o640)
                expected_digest = hashlib.sha256(fixture['old']).hexdigest().upper()
                assert retained.read_bytes() == fixture['old'] and retained.stat().st_ino == before.st_ino
                assert (target / 'container-files-v1' / (binding['operationId'] + '.data')).read_bytes() == fixture['old']
                fixture['proofs']['original.bin'] = owned.common.fingerprint(output), hashlib.sha256(replacement).hexdigest().upper()
                fixture['private'] = '.whaledeck-container-files-v1'
                fixture['privateProofs'][retained.name] = owned.common.fingerprint(retained), expected_digest
                assert call('publish', binding)['replayed'] and call('inspect', binding)['bindingCurrent']
                call('discard', binding, expected_error='CONTAINER_UPLOAD_ALREADY_PUBLISHED')
                call('inspect', {**binding, 'expectedTargetProof': 'F' * 64}, expected_error='CONTAINER_UPLOAD_TARGET_CHANGED')
                missing = {**binding, 'operationId': uuid.uuid4().hex}
                call('inspect', missing, expected_error='CONTAINER_UPLOAD_NOT_FOUND')
                cancel_path = {**location, 'path': 'work/canceled.bin'}
                cancel_plan = call('target', cancel_path)
                cancel = {**request, **cancel_path, 'operationId': uuid.uuid4().hex, 'expectedVersion': 'ABSENT',
                    'expectedParentVersion': cancel_plan['expectedParentVersion'], 'expectedTargetProof': cancel_plan['targetProof']}
                call('stage', cancel, replacement)
                cancel_ref = {key: cancel[key] for key in ('containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof')}
                assert call('discard', cancel_ref)['state'] == 'Discarded' and not (fixture['folder'] / 'canceled.bin').exists()
                # A canceled intent may have no root payload yet. Its durable
                # fence must outlive this RPC and refuse a delayed first body.
                prevented = {**cancel_ref, 'operationId': uuid.uuid4().hex}
                assert call('discard', prevented)['state'] == 'Prevented'
                assert call('inspect', prevented)['state'] == 'Prevented'
                assert call('discard', prevented)['replayed']
                call('stage', {**cancel, 'operationId': prevented['operationId']}, replacement, expected_error='CONTAINER_UPLOAD_CANCELED')
                call('publish', prevented, expected_error='CONTAINER_UPLOAD_CANCELED')
                assert not (state / (prevented['operationId']+'.data')).exists() and not (state / (prevented['operationId']+'.partial')).exists()
                # Stop a genuinely live receiver, not an already-collected
                # unit. A wrong InvocationID must leave it alive; exact stop
                # must release locks and allow explicit interrupted cleanup.
                live_plan = call('target', cancel_path)
                live = {**cancel, 'operationId': uuid.uuid4().hex, 'expectedParentVersion': live_plan['expectedParentVersion'],
                    'expectedTargetProof': live_plan['targetProof']}
                transport = uuid.uuid4().hex; active.append(transport)
                process = subprocess.Popen([str(helper), 'container-file-write', 'stage', transport], stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                invocation = None
                try:
                    process.stdin.write((json.dumps(live)+'\n').encode()); process.stdin.flush()
                    import select
                    assert select.select([process.stdout], [], [], 10)[0]
                    ready = json.loads(process.stdout.readline(65537))
                    invocation = ready['invocationId']; assert ready['type'] == 'driver' and re.fullmatch('[a-f0-9]{32}', invocation)
                    receipt = state / (live['operationId'] + '.json'); deadline = time.monotonic() + 10
                    while not receipt.exists() and time.monotonic() < deadline:
                        assert process.poll() is None; time.sleep(0.01)
                    assert receipt.exists() and json.loads(receipt.read_bytes())['state'] == 'Receiving'
                    denied = subprocess.run([str(helper), 'container-file-write-stop', transport, '0'*32], capture_output=True, timeout=10)
                    assert denied.returncode != 0 and process.poll() is None
                    owned.common.command([str(helper), 'container-file-write-stop', transport, invocation])
                    process.stdin.close(); process.stdin = None
                    stopped_output, _ = process.communicate(timeout=15)
                    # systemd treats a requested SIGTERM as a clean stop, so
                    # systemd-run may exit zero. That is not a committed upload:
                    # require no receipt and verify the durable interrupted
                    # state below, just as the Agent requires a full receipt.
                    assert not stopped_output.strip(), 'stopped receiver emitted a receipt'
                    assert subprocess.run(['systemctl', 'is-active',
                        'whaledeck-container-file-write-' + transport + '.service'],
                        capture_output=True, timeout=10).stdout.strip() not in (b'active', b'activating', b'deactivating')
                    reference = {key: live[key] for key in ('containerId', 'instanceId', 'operationId', 'owner', 'expectedTargetProof')}
                    assert call('inspect', reference)['state'] == 'Interrupted'
                    assert call('discard', reference)['state'] == 'Discarded'
                    assert not (state / (live['operationId'] + '.partial')).exists()
                    assert not (fixture['folder'] / 'canceled.bin').exists()
                finally:
                    if process.poll() is None:
                        if invocation: owned.common.command([str(helper), 'container-file-write-stop', transport, invocation])
                        process.communicate(timeout=15)
                # The retained journal must remain inspectable after a genuine
                # stop/restart, without following the new process namespace.
                journal = state / (binding['operationId'] + '.json')
                original_record = json.loads(journal.read_bytes())
                observed = call('recovery-observe', binding)
                assert observed['rootState'] == 'Published' and not observed['bindingCurrent'] and not observed['sealed']
                assert observed['mayHavePublished'] and observed['retainedObjects']
                owned.common.command(['docker', 'stop', '--time', '1', fixture['identifier']])
                assert json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]['State']['Running'] is False
                call('inspect', binding, expected_error='CONTAINER_NOT_RUNNING')
                assert call('recovery-observe', binding) == observed
                owned.common.command(['docker', 'start', fixture['identifier']])
                restarted = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
                new_instance = str(restarted['State']['Pid']) + ':' + restarted['State']['StartedAt']
                assert restarted['State']['Running'] and new_instance != instance
                call('inspect', binding, expected_error='CONTAINER_FILES_INSTANCE_CHANGED')
                assert call('recovery-observe', binding) == observed
                recovery = {**binding, 'recoveryId': uuid.uuid4().hex, 'actorSubject': 'owned-profile-recovery-admin',
                    'traceId': 'owned-profile-recovery-trace', 'expectedObservationHash': observed['observationHash']}
                sealed = call('recovery-seal', recovery)
                assert sealed['sealed'] and sealed['sealId'] == recovery['recoveryId'] and not sealed['bindingCurrent']
                assert sealed['rootState'] == 'Published' and sealed['originalRecordHash'] == observed['originalRecordHash']
                sealed_bytes = journal.read_bytes()
                assert json.loads(sealed_bytes)['originalRecord'] == original_record
                assert call('recovery-seal', recovery) == sealed and journal.read_bytes() == sealed_bytes
                assert call('recovery-observe', binding) == sealed
                call('recovery-observe', {**binding, 'owner': 'C'*64}, expected_error='CONTAINER_UPLOAD_RECOVERY_SCOPE_CHANGED')
                assert output.read_bytes() == replacement and retained.read_bytes() == fixture['old'] and retained.stat().st_ino == before.st_ino
                assert (target / 'container-files-v1' / (binding['operationId'] + '.data')).read_bytes() == fixture['old']
                # A missing operation for the current real instance can be
                # sealed before its sender arrives. Even a fully valid delayed
                # body must be rejected by the original receipt admission path.
                current_location = {**cancel_path, 'instanceId': new_instance}
                current_plan = call('target', current_location)
                delayed = {**request, **current_location, 'operationId': uuid.uuid4().hex, 'expectedVersion': 'ABSENT',
                    'expectedParentVersion': current_plan['expectedParentVersion'], 'expectedTargetProof': current_plan['targetProof']}
                delayed_ref = {key: delayed[key] for key in ('containerId','instanceId','operationId','owner','expectedTargetProof')}
                missing_observation = call('recovery-observe', delayed_ref)
                assert missing_observation['rootState'] == 'Missing' and not missing_observation['retainedObjects']
                missing_recovery = {**delayed_ref, 'recoveryId': uuid.uuid4().hex, 'actorSubject': 'owned-profile-recovery-admin',
                    'traceId': 'owned-profile-missing-trace', 'expectedObservationHash': missing_observation['observationHash']}
                assert call('recovery-seal', missing_recovery)['sealed']
                call('stage', delayed, replacement, expected_error='CONTAINER_UPLOAD_RECEIPT_INVALID')
                call('publish', delayed_ref, expected_error='CONTAINER_UPLOAD_RECEIPT_INVALID')
                assert not (state / (delayed['operationId']+'.data')).exists() and not (state / (delayed['operationId']+'.partial')).exists()
                assert not (fixture['folder']/'canceled.bin').exists()
            completed = True
            assert all((host / name).read_bytes() == content for name, content in frozen.items())
            print(json.dumps({'actualContainerWriteSystemdProfile': 'passed', 'fullBytes': owned.common.SIZE,
                'targetStagePublishInspectDiscardAndFixedErrors': 'passed', 'stdoutExactReadyAndReceipt': True,
                'exactInvocationStop': True, 'liveWrongInvocationRefusedAndExactCancellationCleaned': True,
                'preReceiveFenceAndDelayedStageRefusal': True,
                'actualStoppedAndRestartedHistoricalObservation': True, 'permanentRecoverySealRetainsAllObjects': True,
                'currentInstanceDelayedSenderAndPublisherRefusedAfterSeal': True,
                'installedAgentRpcOrSsoUi': False, 'sourceSha256': hashes}), flush=True)
        finally:
            for transport in active:
                unit = 'whaledeck-container-file-write-' + transport + '.service'
                state_value = subprocess.run(['systemctl', 'is-active', unit], capture_output=True, timeout=10).stdout.strip()
                assert state_value not in (b'active', b'activating', b'deactivating'), 'owned unit still running'
            assert target.resolve(strict=True) == target and (target.stat().st_dev, target.stat().st_ino) == target_identity
            directory = target / 'container-files-v1'
            if completed and directory.is_dir() and not directory.is_symlink():
                operation = binding['operationId']; assert {x.name for x in directory.iterdir()} == {operation+'.data', operation+'.json'}
                data = directory / (operation+'.data'); metadata = directory / (operation+'.json')
                info = data.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                assert info.st_size == owned.common.SIZE and owned.common.file_digest(data) == expected_digest
                info = metadata.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                saved = json.loads(metadata.read_bytes())
                assert all(saved[key] == binding[key] for key in ('containerId', 'instanceId', 'operationId', 'owner')) and saved['state'] == 'Verified'
                assert saved['sha256'] == expected_digest
                data.unlink(); metadata.unlink(); directory.rmdir()
            removed = not any(target.iterdir())
            if removed: target.rmdir()
            print(json.dumps({'ownedProfileHistoryRemoved': removed, 'unverifiedHistoryPreserved': not removed}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'actualContainerWriteSystemdProfile': 'failed', 'failureType': type(error).__name__,
            'failureLocations': [{'file': Path(frame.filename).name, 'line': frame.lineno}
                for frame in traceback.extract_tb(error.__traceback__)]}), file=sys.stderr)
        raise SystemExit(1) from None
