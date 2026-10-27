#!/usr/bin/env python3
"""Independent signed protobuf/UDS probe of installed recovery on an owned fixture.

No human-authorization claim. Capabilities stay in memory/private curl stdin;
the only seal cleaned is this invocation's independently proven Missing seal.
Unknown or mismatched data is preserved. No product SQL or HDD history writes.
"""
import fcntl
import hashlib
import hmac
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
from urllib.parse import unquote

import validate_container_file_publishing as owned
from validate_container_file_write_agent import trusted

DOMAIN = b'whaledeck-container-upload-recovery-v1\n'
PREFIX = '/whaledeck.agent.v1.ContainerFileWriteService/'
STATE = Path('/var/lib/whaledeck-container-uploads')
PHASE = 'startup'
RPC_STATUS = None


def digest(kind, value):
    return hashlib.sha256(DOMAIN + kind.encode() + b'\n' + json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest().upper()


def varint(value):
    output = bytearray()
    while value > 127: output.append((value & 127) | 128); value >>= 7
    output.append(value); return bytes(output)


def message(fields):
    return b''.join(varint(number * 8 + 2) + varint(len(value)) + value for number, value in fields if value)


def text(value): return value.encode('utf-8')


def parse(data):
    position = 0; result = {}
    def number():
        nonlocal position
        output = shift = 0
        while position < len(data) and shift < 70:
            byte = data[position]; position += 1; output |= (byte & 127) << shift
            if byte < 128: return output
            shift += 7
        raise AssertionError('RECOVERY_PROBE_INVALID_VARINT')
    while position < len(data):
        tag = number(); wire = tag & 7; field = tag >> 3
        assert field > 0 and field not in result
        if wire == 0: value = number()
        else:
            assert wire == 2
            length = number(); assert position + length <= len(data)
            value = data[position:position+length]; position += length
        result[field] = value
    return result


def observation(data, scope):
    fields = parse(data); assert set(fields) <= set(range(1, 13))
    names = {1: 'containerId', 2: 'instanceId', 3: 'operationId', 4: 'targetProof', 5: 'rootState',
             6: 'originalRecordHash', 8: 'sealId', 12: 'observationHash'}
    value = {name: fields.get(index, b'').decode() for index, name in names.items()}
    for index, name in ((7, 'sealed'), (9, 'bindingCurrent'), (10, 'mayHavePublished'), (11, 'retainedObjects')):
        assert fields.get(index, 0) in (0, 1); value[name] = bool(fields.get(index, 0))
    assert value['containerId'] == scope['containerId'] and value['instanceId'] == scope['instanceId']
    assert value['operationId'] == scope['operationId'] and value['targetProof'] == scope['expectedTargetProof']
    facts = {key: item for key, item in value.items() if key != 'observationHash'}
    facts['sealId'] = value['sealId'] or None; facts['owner'] = scope['owner']
    assert value['observationHash'] == digest('observation', facts)
    assert value['rootState'] == 'Missing' and value['originalRecordHash'] == digest('original', None)
    assert not value['bindingCurrent'] and not value['mayHavePublished'] and not value['retainedObjects']
    return value


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    for component, application in (('agent', 'WhaleDeck.Agent'), ('maintenance', 'WhaleDeck.MaintenanceHost')):
        info = json.loads(owned.common.command([f'/opt/whaledeck-{component}/{application}', '--build-info']))
        assert info['sourceRevision'] == revision
    assert stat.S_IMODE(trusted(STATE, True).st_mode) == 0o700
    key = Path('/etc/whaledeck/agent-capability.key').read_bytes(); assert len(key) == 32
    operation, recovery = uuid.uuid4().hex, uuid.uuid4().hex
    assert not any(path.name.startswith(operation + '.') for path in STATE.iterdir())
    actor = 'native-recovery-current-owned-validation'; trace = 'native-recovery-current-owned-validation'
    context = message([(1, text('native-recovery-original-owned-validation')), (2, text('native-recovery-original-owned-validation')),
                       (3, text(operation)), (4, text(operation))])
    current = message([(1, text(trace)), (2, text(actor)), (3, text(recovery)), (4, text(recovery))])
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-recovery-evidence-' + uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    with tempfile.TemporaryDirectory(prefix='wdv-recovery-wire-') as temporary, owned.fixture() as fixture:
        private = Path(temporary); sequence = 0
        def call(action, kind, body, expected=0, anonymous=False, signed_body=None, expected_detail=None):
            nonlocal sequence
            global RPC_STATUS
            sequence += 1; payload = private / f'{sequence}.body'; headers = private / f'{sequence}.headers'
            payload.write_bytes(b'\0' + len(body).to_bytes(4, 'big') + body); payload.chmod(0o600)
            request_hash = hashlib.sha256(b'whaledeck-container-file-request-v1\nwhaledeck.agent.v1.' + kind.encode() + b'\n' +
                                          (body if signed_body is None else signed_body)).hexdigest().upper()
            expires, nonce = str(int(time.time()) + 45), uuid.uuid4().hex
            signed = f'whaledeck-container-file-capability-v1\n{expires}.{nonce}.{PREFIX}{action}.{request_hash}'
            capability = expires + '.' + nonce + '.' + hmac.new(key, signed.encode(), hashlib.sha256).hexdigest().upper()
            config = ['silent', 'show-error', 'noproxy = "*"', 'http2-prior-knowledge', 'max-time = 20',
                      'unix-socket = "/run/whaledeck/agent.sock"', 'header = "Content-Type: application/grpc"', 'header = "TE: trailers"',
                      f'url = "http://localhost{PREFIX}{action}"', f'data-binary = "@{payload}"', f'dump-header = "{headers}"']
            if not anonymous: config.extend([f'header = "x-whaledeck-capability: {capability}"', f'header = "x-whaledeck-request-sha256: {request_hash}"'])
            result = subprocess.run(['curl', '--config', '-'], input=('\n'.join(config)+'\n').encode(), capture_output=True, check=True, timeout=25)
            assert len(result.stdout) <= 65536 and len(result.stderr) <= 65536
            statuses = re.findall(rb'(?im)^grpc-status:\s*([0-9]+)\s*$', headers.read_bytes())
            RPC_STATUS = {'method': action, 'expected': expected, 'actual': None if not statuses else int(statuses[-1])}
            assert statuses and int(statuses[-1]) == expected
            if expected_detail is not None:
                details = re.findall(rb'(?im)^grpc-message:[ \t]*(.*?)\r?$', headers.read_bytes())
                assert details and unquote(details[-1].decode()).strip() == expected_detail
            if expected: return None
            raw = result.stdout; assert len(raw) >= 5 and raw[0] == 0 and len(raw) == 5 + int.from_bytes(raw[1:5], 'big')
            return raw[5:]

        container = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        instance = str(container['State']['Pid']) + ':' + container['State']['StartedAt']
        location = message([(1, text(fixture['identifier'])), (2, text(instance)), (3, text('work/original.bin'))])
        PHASE = 'installed-target-and-immutable-original'
        target = parse(call('InspectTarget', 'ContainerFileLocation', location)); proof = target[2].decode()
        scope = {'containerId': fixture['identifier'], 'instanceId': instance, 'operationId': operation,
                 'owner': 'B'*64, 'expectedTargetProof': proof}
        baseline = evidence / 'baseline.json'
        baseline.write_text(json.dumps({'scope': scope, 'recoveryId': recovery, 'actor': actor, 'trace': trace,
                                       'hostRevision': revision, 'probeSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}))
        baseline.chmod(0o600)
        original = message([(1, text(scope['containerId'])), (2, text(instance)), (3, text(operation)),
                            (4, text(scope['owner'])), (5, context), (6, text(proof))])
        PHASE = 'installed-historical-observation-and-method-binding'
        call('ObserveRecovery', 'ContainerUploadReference', original, expected=7, anonymous=True)
        changed = original.replace(b'B'*64, b'C'*64)
        call('ObserveRecovery', 'ContainerUploadReference', changed, expected=7, signed_body=original)
        before = observation(call('ObserveRecovery', 'ContainerUploadReference', original), scope); assert not before['sealed']
        PHASE = 'actual-stop-restart-with-original-instance'
        owned.common.command(['docker', 'stop', '--time', '2', fixture['identifier']])
        assert observation(call('ObserveRecovery', 'ContainerUploadReference', original), scope) == before
        owned.common.command(['docker', 'start', fixture['identifier']])
        restarted = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        assert str(restarted['State']['Pid']) + ':' + restarted['State']['StartedAt'] != instance
        assert observation(call('ObserveRecovery', 'ContainerUploadReference', original), scope) == before
        call('Inspect', 'ContainerUploadReference', original, expected=9)
        PHASE = 'installed-seal-approval-and-durable-replay'
        def seal_body(expected_hash):
            return message([(1, original), (2, text(recovery)), (3, text(expected_hash)), (4, current)])
        call('SealRecovery', 'ContainerUploadRecoveryRequest', seal_body('F'*64), expected=10,
             expected_detail='CONTAINER_UPLOAD_RECOVERY_OBSERVATION_CHANGED')
        assert not (STATE / (operation+'.json')).exists()
        sealed = observation(call('SealRecovery', 'ContainerUploadRecoveryRequest', seal_body(before['observationHash'])), scope)
        assert sealed['sealed'] and sealed['sealId'] == recovery
        assert observation(call('SealRecovery', 'ContainerUploadRecoveryRequest', seal_body(before['observationHash'])), scope) == sealed
        assert observation(call('ObserveRecovery', 'ContainerUploadReference', original), scope) == sealed
        call('ObserveRecovery', 'ContainerUploadReference', changed, expected=7,
             expected_detail='CONTAINER_UPLOAD_RECOVERY_SCOPE_CHANGED')
        PHASE = 'independent-quiescent-owned-missing-seal-proof-and-cleanup'
        lock = STATE / (operation+'.lock'); metadata = STATE / (operation+'.json')
        for path in (lock, metadata): assert stat.S_IMODE(trusted(path).st_mode) == 0o600
        assert lock.stat().st_size == 0 and metadata.stat().st_size <= 16384
        descriptor = os.open(lock, os.O_RDONLY | os.O_NOFOLLOW)
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            saved = json.loads(metadata.read_bytes())
            material = {'recoveryId': recovery, 'actorHash': digest('actor', actor), 'traceHash': digest('trace', trace),
                        'expectedObservationHash': before['observationHash']}
            expected = {'schemaVersion': 3, 'resourceType': 'ContainerUploadRecoverySeal', 'state': 'Sealed',
                        **{key: value for key, value in scope.items() if key != 'expectedTargetProof'}, 'targetProof': proof,
                        'originalRecord': None, 'originalRecordHash': before['originalRecordHash'], **material,
                        'requestHash': digest('seal-request', {**scope, **material})}
            assert set(saved) == set(expected) | {'sealedAtUnixMs'} and all(saved[key] == value for key, value in expected.items())
            assert type(saved['sealedAtUnixMs']) is int and 0 <= saved['sealedAtUnixMs'] <= int(time.time()*1000)
            assert {path.name for path in STATE.iterdir() if path.name.startswith(operation+'.')} == {lock.name, metadata.name}
            # Never apply this QA cleanup to Published/Unknown/data-bearing seals.
            metadata.unlink(); lock.unlink()
            parent = os.open(STATE, os.O_RDONLY | os.O_DIRECTORY)
            try: os.fsync(parent)
            finally: os.close(parent)
        finally: os.close(descriptor)
        verified = evidence / 'verified.json'; verified.write_text(json.dumps({'sealed': sealed, 'ownedMissingSealCleaned': True})); verified.chmod(0o600)
    print(json.dumps({'installedContainerRecoveryRpc': 'passed', 'hostRevision': revision,
                      'independentlySignedObserveAndSeal': True, 'actualStoppedAndRestartedObservation': True,
                      'approvalAndRequestBinding': True, 'durableSameRequestReplay': True,
                      'onlyOwnedMissingSealCleaned': True, 'humanBffOrUi': False}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerRecoveryRpc': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
                          'rpcStatus': RPC_STATUS,
                          'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
                          'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
