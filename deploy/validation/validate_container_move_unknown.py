#!/usr/bin/env python3
"""Actual partial directory retention -> Unknown -> another admin's real page.

No Root/SQL/lease edits or installed instrumentation. The only injected fault
is SIGKILL through a pidfd bound to this owned operation's installed helper.
Published bytes and split originals are verified before exact fixture cleanup.
"""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from bound_container_move_cut import ROOT, record_for, receipt_json
from bound_container_move_retention_cut import freeze_retention
from validate_container_move_browser import snapshot
from validate_container_directory_restart import Worker, clean_success, command, preflight
from validate_container_directory_unknown import register as register_waiting, wait_settled as wait_directory
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_file_staging import load
from validate_databases import Api
from validate_oidc import required_environment

PHASE = 'startup'


def wait_move(identifier, states):
    deadline = time.monotonic() + 110
    while True:
        value = snapshot(identifier)
        if value['row']['state'] in states: return value
        assert value['row']['state'] in ('Queued', 'Planning', 'Ready', 'Pending', 'Applying')
        assert time.monotonic() < deadline, 'MOVE_STATE_DEADLINE'
        time.sleep(0.5)


def create_source(fixture, reader):
    root = fixture['folder'] / 'source-tree'; root.mkdir(mode=0o750)
    (root / 'empty').mkdir(mode=0o700); metadata = {}; digests = {}
    for index in range(16):
        name = 'part-' + format(index, '02') + '.bin'; path = root / name
        content = hashlib.shake_256(('owned-move-tree:' + fixture['folder'].name + ':' + str(index)).encode()).digest(2 * 1024**2 + 7)
        path.write_bytes(content); os.chown(path, 2100 + index, 3100 + index); path.chmod(0o640 if index % 2 else 0o600)
        os.utime(path, ns=(1700000000123456789 + index, 1700000000123456789 + index))
        metadata[name] = list(reader.fingerprint(path.lstat())); digests[name] = hashlib.sha256(content).hexdigest().upper()
    for index, name in enumerate(('', 'empty')):
        path = root / name; os.chown(path, 2200 + index, 3200 + index)
        os.utime(path, ns=(1700000000123456789 + index, 1700000000123456789 + index))
        metadata[name] = list(reader.fingerprint(path.lstat()))
    return root, metadata, digests


def inspect_physical(fixture, record, baseline, reader, *, split):
    """Enumerate only this synthetic flat tree, validate all bytes before unlink."""
    folder = fixture['folder']; operation = record['operationId']
    source = folder / 'source-tree'; target = folder / '目标已发布-源处理中'
    private = folder / '.whaledeck-container-moves-v1'; kept = private / (operation + '.tree-move')
    assert list((folder.stat().st_dev, folder.stat().st_ino)) == record['plan']['sourceParent'] == record['plan']['targetParent']
    assert record['plan']['command']['sourcePath'] == 'work/' + source.name and record['plan']['command']['targetPath'] == 'work/' + target.name
    copied = receipt_json(ROOT / 'engines/tree-move-copies' / (operation + '.json'))
    assert copied['state'] == 'Completed' and copied['owner'] == record['owner'] and copied['plan'] == record['plan']['innerPlan']['copyPlan']
    target_info = target.lstat(); assert stat.S_ISDIR(target_info.st_mode) and [target_info.st_dev, target_info.st_ino] == copied['newIdentity']
    journal = receipt_json(ROOT / 'engines/tree-moves' / (operation + '.json'))
    assert journal['plan'] == record['plan']['innerPlan'] and journal['owner'] == record['owner'] and journal['operationId'] == operation
    assert [kept.stat().st_dev, kept.stat().st_ino] == journal['retainedIdentity']
    assert not private.is_symlink() and private.stat().st_uid == private.stat().st_gid == 0 and stat.S_IMODE(private.stat().st_mode) == 0o700
    assert {p.name for p in private.iterdir()} == {kept.name}
    expected = set(baseline['digests']) | {'empty'}
    assert {p.name for p in target.iterdir()} == expected
    assert {p.name for p in kept.iterdir()} <= expected
    if source.exists():
        assert not source.is_symlink() and list(reader.fingerprint(source.stat()))[:5] == baseline['metadata'][''][:5]
        assert {p.name for p in source.iterdir()} <= expected
    else: assert not split
    observed = {'target': {}, 'source': {}, 'retained': {}, 'sha256': {}}
    observed['target'][''] = list(reader.fingerprint(target.lstat()))
    observed['retained'][''] = list(reader.fingerprint(kept.lstat()))
    if source.exists(): observed['source'][''] = list(reader.fingerprint(source.lstat()))
    for name, old in baseline['metadata'].items():
        output = target / name; current = output.lstat()
        assert not output.is_symlink() and (stat.S_IFMT(current.st_mode), current.st_uid, current.st_gid, stat.S_IMODE(current.st_mode), current.st_mtime_ns) == (stat.S_IFMT(old[2]), old[3], old[4], old[2] & 0o777, old[7])
        observed['target'][name] = list(reader.fingerprint(current))
        if name in baseline['digests']:
            assert current.st_nlink == 1 and owned.common.file_digest(output) == baseline['digests'][name]
            original, retained = source / name, kept / name
            assert os.path.lexists(original) != os.path.lexists(retained)
            selected = original if os.path.lexists(original) else retained; info = selected.lstat()
            assert not selected.is_symlink() and list(reader.fingerprint(info))[:8] == old[:8]
            assert owned.common.file_digest(selected) == baseline['digests'][name]
            which = 'source' if selected == original else 'retained'
            observed[which][name] = list(reader.fingerprint(info)); observed['sha256'][name] = baseline['digests'][name]
        elif name:
            assert stat.S_ISDIR((kept / name).lstat().st_mode) and not (kept / name).is_symlink() and not list((kept / name).iterdir())
            assert (kept / name).stat().st_uid == (kept / name).stat().st_gid == 0 and stat.S_IMODE((kept / name).stat().st_mode) == 0o700
            observed['retained'][name] = list(reader.fingerprint((kept / name).lstat()))
            if source.exists():
                assert not (source / name).is_symlink() and not list((source / name).iterdir())
                observed['source'][name] = list(reader.fingerprint((source / name).lstat()))
    if split:
        assert 0 < len(set(observed['retained']) & set(baseline['digests'])) < len(baseline['digests'])
        assert 0 < len(set(observed['source']) & set(baseline['digests'])) < len(baseline['digests'])
    return observed


def clean_physical(fixture, record, baseline, reader, evidence):
    proof = inspect_physical(fixture, record, baseline, reader, split=record['state'] == 'Unknown')
    (evidence / 'physical-final.json').write_text(json.dumps(proof))
    roots = {'target': fixture['folder'] / '目标已发布-源处理中', 'source': fixture['folder'] / 'source-tree',
             'retained': fixture['folder'] / '.whaledeck-container-moves-v1' / (record['operationId'] + '.tree-move')}
    for kind, root in roots.items():
        entries = proof[kind]
        if not entries: assert not os.path.lexists(root); continue
        assert {p.name for p in root.iterdir()} == set(entries) - {''}
        for name in set(entries) - {'', 'empty'}:
            path = root / name; assert list(reader.fingerprint(path.lstat())) == entries[name]; path.unlink()
        if 'empty' in entries:
            empty = root / 'empty'; assert list(reader.fingerprint(empty.lstat())) == entries['empty'] and not list(empty.iterdir()); empty.rmdir()
        assert list(reader.fingerprint(root.lstat()))[:5] == entries[''][:5] and not list(root.iterdir()); root.rmdir()
    private = fixture['folder'] / '.whaledeck-container-moves-v1'; assert not list(private.iterdir()); private.rmdir()


def acknowledge(base, client, api, identifier):
    path = base + '/api/v1/files/container-moves/recovery/' + identifier
    result = request(client, 'GET', path, 200).json()
    if result['state'] == 'Unknown':
        request(client, 'POST', path + '/acknowledge', 202, headers={**api.headers, 'Idempotency-Key': identifier},
            json={'version': result['version'], 'confirmed': True})
    value = snapshot(identifier)
    assert value['row']['state'] == 'Acknowledged' and value['executionJob']['state'] == 'Failed'
    assert not value['row']['publish_barrier'] and value['row']['lease_token'] is None
    return value


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://127.0.0.1:8080').rstrip('/')
    username, password = required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')
    preflight(); worker = Worker(); administrator, profile = login(base, username, password); api = Api(base, administrator)
    assert request(administrator, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-move-unknown-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    reader = load('/usr/local/libexec/whaledeck-container-files.py', 'move_unknown_source_reader')
    intent = waiting = held = frozen = baseline = None; cleaned = waiting_clean = False
    with owned.fixture(restartable_core=('whaledeck-worker',)) as original, owned.fixture(restartable_core=('whaledeck-worker',)) as other:
        try:
            with temporary_user(True) as (name, secret):
                owner, owner_profile = login(base, name, secret); owner_api = Api(base, owner)
                root, metadata, digests = create_source(original, reader)
                inspect = json.loads(command('docker', 'inspect', original['identifier']))[0]
                instance = str(inspect['State']['Pid']) + ':' + inspect['State']['StartedAt']
                endpoint = base + '/api/v1/containers/' + original['identifier'] + '/files/moves'
                intent = request(owner, 'POST', endpoint, 202, headers={**owner_api.headers, 'Idempotency-Key': str(uuid.uuid4())},
                    json={'instanceId': instance, 'sourcePath': 'work/source-tree', 'targetPath': 'work/目标已发布-源处理中',
                          'expectedVersion': reader.version({'containerId': original['identifier'], 'instanceId': instance}, 'work/source-tree', root.stat())}).json()
                ready = wait_move(intent['id'], ('Ready',))['row']
                ready_dto = request(owner, 'GET', endpoint + '/' + intent['id'], 200).json()
                PHASE = 'actual-confirmed-move-with-worker-stopped'; worker.stop()
                accepted = request(owner, 'POST', endpoint + '/' + intent['id'] + '/confirm', 202,
                    headers={**owner_api.headers, 'Idempotency-Key': intent['id']},
                    json={'planHash': ready_dto['confirmation']['planHash'], 'version': ready['version'], 'confirmed': True}).json()
                row = snapshot(intent['id'])['row']; assert row['root_started_at_utc'] is None and row['execution_job_id'] == accepted['executionJobId']
                baseline = {'revision': revision, 'intent': intent['id'], 'executionId': row['execution_job_id'], 'containerId': original['identifier'],
                    'instanceId': instance, 'source': str(original['folder']), 'containerName': original['folder'].name,
                    'originalSubject': owner_profile['subject'], 'metadata': metadata, 'digests': digests, 'evidence': str(evidence)}
                (evidence / 'baseline.json').write_text(json.dumps(baseline))
                PHASE = 'exact-published-target-partial-retention-cut'
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watch = observers.submit(freeze_retention, row, original['folder'], inspect['State']['Pid'])
                    worker.start(wait_ready=False); held = watch.result(timeout=45)
                assert held is not None, 'PARTIAL_RETENTION_CUT_MISSED_NOT_COUNTED'
                frozen = held.record; physical = inspect_physical(original, frozen, baseline, reader, split=True)
                (evidence / 'bound-cut.json').write_text(json.dumps({'pid': held.pid, 'unit': held.cgroup, 'outer': frozen, 'proof': held.proof, 'twoKernelFdProofs': True}))
                (evidence / 'physical-at-cut.json').write_text(json.dumps(physical))
                PHASE = 'kill-only-bound-move-helper-then-original-unknown'
                held.kill(); held = None
                unknown = wait_move(intent['id'], ('Unknown',)); row = unknown['row']; receipt = record_for(row)
                assert row['publish_barrier'] and row['completed_at_utc'] is None and row['destination_published'] is True
                assert row['source_removed'] is row['source_retained'] is None and row['private_may_exist']
                assert receipt['state'] == 'Unknown' and receipt['destinationPublished'] is True and receipt['sourceRemoved'] is receipt['sourceRetained'] is None
                raw = (ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')).read_bytes(); (evidence / 'unknown-root.json').write_bytes(raw)
                assert inspect_physical(original, receipt, baseline, reader, split=True) == physical
                immutable = {key: row[key] for key in ('actor_subject', 'login_binding', 'request_hash', 'plan_json', 'execution_hash', 'owner_hash', 'execution_job_id')}
                logout(base, owner); owner.close()
            PHASE = 'another-container-directory-waits-behind-move-fence'
            waiting, waiting_baseline = register_waiting(base, administrator, api, other, 'work/移动处置后才发布')
            waiting_baseline['revision'] = revision
            baseline.update(waitingIntent=waiting['id'], waiting=waiting_baseline, originalIdentityDeleted=True)
            (evidence / 'baseline.json').write_text(json.dumps(baseline))
            from validate_container_directory_browser import snapshot as directory_snapshot
            deadline = time.monotonic() + 20
            while True:
                blocked = directory_snapshot(waiting['id'])['row']
                assert blocked['state'] == 'Pending' and blocked['root_started_at_utc'] is None and not blocked['publish_barrier']
                assert snapshot(intent['id'])['row']['publish_barrier'] and not (other['folder'] / '移动处置后才发布').exists()
                if blocked['version'] >= 4: break
                assert time.monotonic() < deadline; time.sleep(0.5)
            (evidence / 'blocked.json').write_text(json.dumps({'waitingVersion': blocked['version'], 'originalBarrier': True, 'rootStarted': False}))
            PHASE = 'different-current-admin-real-mobile-unknown-confirmation'
            browser = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline), capture_output=True, text=True, timeout=240)
            assert len(browser.stdout.encode()) <= 32768 and len(browser.stderr.encode()) <= 32768
            (evidence / 'browser.stdout').write_text(browser.stdout); (evidence / 'browser.stderr').write_text(browser.stderr)
            assert browser.returncode == 0, 'REAL_MOVE_UNKNOWN_UI_NOT_VERIFIED'
            ui = json.loads((evidence / 'browser-result.json').read_bytes())
            assert ui['subject'] == profile['subject'] != baseline['originalSubject'] and ui['fullSsoLogout']
            assert ui['confirmations'] == 1 and ui['registrations'] == ui['bodies'] == 0
            final = acknowledge(base, administrator, api, intent['id']); row = final['row']
            assert {key: row[key] for key in immutable} == immutable and row['acknowledged_by'] == profile['subject']
            assert row['published_version'] is None and final['pending'] == 0 and final['accepted'] == final['confirmed'] == 1
            assert row['destination_published'] is True and row['source_removed'] is row['source_retained'] is None
            assert (ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')).read_bytes() == raw
            PHASE = 'late-original-root-apply-cannot-resume-retention'
            replay = subprocess.run(['/usr/local/libexec/whaledeck-privileged', 'container-file-moves', 'apply', uuid.uuid4().hex],
                input=json.dumps({'operationId': receipt['operationId'], 'owner': receipt['owner'], 'plan': receipt['plan']}) + '\n', capture_output=True, text=True, timeout=40)
            (evidence / 'late-root.stdout').write_text(replay.stdout); (evidence / 'late-root.stderr').write_text(replay.stderr)
            assert replay.returncode == 0 and len(replay.stdout.encode()) <= 32768
            late = [json.loads(line) for line in replay.stdout.splitlines()][-1]
            assert late['state'] == 'Unknown' and late['destinationPublished'] is True and late['sourceRemoved'] is late['sourceRetained'] is None
            assert (ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')).read_bytes() == raw
            assert inspect_physical(original, receipt, baseline, reader, split=True) == physical
            (evidence / 'acknowledged.json').write_text(json.dumps({'intent': intent['id'], 'executionId': row['execution_job_id'], 'state': row['state'],
                'acknowledgedBy': row['acknowledged_by'], 'rootUnchanged': True, 'physicalUnchanged': True, 'originalBindingUnchanged': True, 'lateApplyStillUnknown': True}))
            clean_success(waiting, waiting_baseline, profile, evidence, other); waiting_clean = True
            clean_physical(original, receipt, baseline, reader, evidence); cleaned = True
            logout(base, administrator)
        finally:
            if held is not None: held.release()
            worker.start()
            # Release deliberate test fences only through the real authorized
            # endpoint. A fallback is recorded as a failed UI gate, never pass.
            if intent is not None and not cleaned:
                current = snapshot(intent['id'])
                if current['row']['execution_job_id'] is not None:
                    current = wait_move(intent['id'], ('Unknown', 'Acknowledged', 'Succeeded', 'Failed', 'Canceled'))
                if current['row']['state'] == 'Unknown':
                    acknowledge(base, administrator, api, intent['id'])
                    (evidence / 'fallback-acknowledgement.json').write_text(json.dumps({'realAuthorizedBff': True, 'uiGatePassed': False}))
                if baseline is not None and current['row']['execution_job_id'] is not None:
                    root = record_for(snapshot(intent['id'])['row'])
                    if root['state'] in ('Unknown', 'Completed'): clean_physical(original, root, baseline, reader, evidence)
            if waiting is not None and not waiting_clean:
                value = wait_directory(waiting['id'])
                if value['row']['state'] == 'Succeeded': clean_success(waiting, waiting_baseline, profile, evidence, other)
            administrator.close()
    preflight()
    (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'intent': intent['id'], 'waitingIntent': waiting['id'],
        'originalAccountDeleted': True, 'differentAdminUi': True, 'realPartialRetention': True, 'oldJobFailedNotMoveSucceeded': True,
        'sourceAndTargetContentVerified': True, 'exactSourcesAndContainersCleaned': True, 'rootAndBusinessHistoryRetained': True}))
    print(json.dumps({'installedContainerMoveUnknown': 'passed', 'evidence': str(evidence), 'intent': intent['id']}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerMoveUnknown': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)],
            'unverifiedObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
