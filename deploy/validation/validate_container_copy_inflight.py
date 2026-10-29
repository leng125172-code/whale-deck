#!/usr/bin/env python3
"""Installed BFF cancellation/login revocation during a real partial file copy.

No Root/SQL edits, installed instrumentation, arbitrary PID, validator SIGKILL,
or automatic rollback. Unproved objects and immutable history are preserved.
"""
from concurrent.futures import ThreadPoolExecutor
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import time
import uuid

import validate_container_file_publishing as owned
from bound_container_copy_cut import freeze_partial_copy, record_for, ROOT
from validate_container_copy_browser import snapshot
from validate_container_directory_restart import Worker, command, preflight, agent_identity
from validate_container_upload_bff import login, logout, request
from validate_container_file_staging import load
from validate_databases import Api
from validate_oidc import required_environment
from validate_metric_continuity import query

PHASE = 'startup'


def wait_settled(identifier):
    deadline = time.monotonic() + 120
    while True:
        value = snapshot(identifier)
        if value['row']['state'] in ('Succeeded', 'Canceled', 'Failed', 'Unknown', 'Acknowledged'): return value
        assert time.monotonic() < deadline, 'COPY_TERMINAL_DEADLINE'
        time.sleep(0.5)


def clean(fixture, row, evidence, label):
    receipt = record_for(row); raw = (ROOT / (uuid.UUID(row['execution_job_id']).hex + '.json')).read_bytes()
    (evidence / (label + '-root-final.json')).write_bytes(raw)
    target = fixture['folder'] / row['target_path'].removeprefix('work/')
    assert target.parent == fixture['folder']
    if row['state'] == 'Succeeded':
        assert receipt['state'] == 'Completed' and receipt['version'] == row['published_version']
        reader = load('/usr/local/libexec/whaledeck-container-files.py', 'copy_inflight_cleanup_reader')
        info = target.lstat(); assert list(reader.fingerprint(info)) == receipt['publicationIdentity']
        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and owned.common.file_digest(target) == fixture['proofs']['large.bin'][1]
        assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns) == (1000, 1000, 0o640, (fixture['folder'] / 'large.bin').stat().st_mtime_ns)
        target.unlink()
    else:
        assert row['state'] in ('Failed', 'Canceled') and receipt['state'] == 'Aborted' and not os.path.lexists(target)
    private = fixture['folder'] / '.whaledeck-container-copies-v1'
    if private.exists():
        info = private.lstat(); assert stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700
        assert not list(private.iterdir()); private.rmdir()


def main():
    global PHASE
    assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 3 and sys.argv[2] == '--restarts'); os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    base = os.environ.get('WHALEDECK_URL', 'http://127.0.0.1:8080').rstrip('/')
    username, password = required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD')
    evidence = Path(__file__).resolve().parents[1] / 'artifacts' / ('container-copy-inflight-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    results = []
    kinds = ('worker-crash', 'agent-restart') if len(sys.argv) == 3 else ('cancel', 'logout')
    for kind in kinds:
        PHASE = 'preflight-' + kind; preflight(); worker = Worker(); held = None; intent = None; cleaned = False; signed_out = False
        client, profile = login(base, username, password); api = Api(base, client)
        assert request(client, 'GET', base + '/api/v1/system/status', 200).json()['build']['sourceRevision'] == revision
        with owned.fixture(restartable_core=('whaledeck-worker',)) as fixture:
            try:
                source = fixture['folder'] / 'large.bin'; digest = hashlib.sha256()
                with source.open('xb') as out:
                    for _ in range(64): out.write(fixture['old']); digest.update(fixture['old'])
                    out.flush(); os.fsync(out.fileno())
                os.chown(source, 1000, 1000); source.chmod(0o640)
                fixture['proofs']['large.bin'] = (owned.common.fingerprint(source), digest.hexdigest().upper())
                inspected = json.loads(command('docker', 'inspect', fixture['identifier']))[0]
                instance = str(inspected['State']['Pid']) + ':' + inspected['State']['StartedAt']
                reader = load('/usr/local/libexec/whaledeck-container-files.py', 'copy_inflight_source_reader')
                location = {'containerId': fixture['identifier'], 'instanceId': instance}
                endpoint = base + '/api/v1/containers/' + fixture['identifier'] + '/files/copies'
                key = str(uuid.uuid4()); target = 'work/进行中-' + kind
                intent = request(client, 'POST', endpoint, 202, headers={**api.headers, 'Idempotency-Key': key}, json={
                    'instanceId': instance, 'sourcePath': 'work/large.bin', 'targetPath': target, 'expectedVersion': reader.version(location, 'work/large.bin', source.stat())}).json()
                until = time.monotonic() + 60
                while True:
                    ready = request(client, 'GET', endpoint + '/' + intent['id'], 200).json()
                    if ready['state'] == 'Ready': break
                    assert ready['state'] in ('Queued', 'Planning') and time.monotonic() < until
                    time.sleep(0.5)
                worker.stop()
                accepted = request(client, 'POST', endpoint + '/' + intent['id'] + '/confirm', 202,
                    headers={**api.headers, 'Idempotency-Key': intent['id']}, json={'planHash': ready['confirmation']['planHash'], 'version': ready['version'], 'confirmed': True}).json()
                row = snapshot(intent['id'])['row']; assert row['execution_job_id'] == accepted['executionJobId'] and row['root_started_at_utc'] is None
                baseline = {**location, 'id': intent['id'], 'executionId': row['execution_job_id'], 'source': str(fixture['folder']),
                    'kind': kind, 'target': target, 'bytes': source.stat().st_size, 'sourceSha256': digest.hexdigest().upper(),
                    'sourceProofs':fixture['proofs'],'folderIdentity':[fixture['folder'].stat().st_dev,fixture['folder'].stat().st_ino]}
                (evidence / (kind + '-baseline.json')).write_text(json.dumps(baseline))
                PHASE = 'actual-partial-private-copy-' + kind
                with ThreadPoolExecutor(max_workers=1) as observers:
                    watch = observers.submit(freeze_partial_copy, row, fixture['folder'], inspected['State']['Pid'])
                    worker.start(wait_ready=False); held = watch.result(timeout=30)
                assert held is not None, 'PARTIAL_COPY_CUT_MISSED_NOT_COUNTED'
                current = snapshot(intent['id'])['row']; assert current['state'] == 'Applying' and current['publish_barrier'] and current['root_started_at_utc']
                assert 0 < held.proof['partialBytes'] < held.proof['totalBytes'] == source.stat().st_size
                folder_info=fixture['folder'].stat()
                (evidence / (kind + '-partial-cut.json')).write_text(json.dumps({'outer':held.record,'proof':held.proof,'pid':held.pid,'unit':held.cgroup,'twoKernelFdProofs':True,
                    'folderAtCut':{'uid':folder_info.st_uid,'gid':folder_info.st_gid,'mode':stat.S_IMODE(folder_info.st_mode),'identity':[folder_info.st_dev,folder_info.st_ino]}}))
                began = time.monotonic(); PHASE = 'product-must-stop-partial-copy-' + kind
                service_proof = None; original_lease = None
                with ThreadPoolExecutor(max_workers=1) as exits:
                    stopped = exits.submit(held.wait_exit_seconds, began)
                    if kind == 'cancel':
                        request(client, 'POST', endpoint + '/' + intent['id'] + '/cancel', 202, headers={**api.headers,'Idempotency-Key':intent['id']})
                    elif kind == 'logout':
                        logout(base, client); signed_out = True
                    elif kind == 'worker-crash':
                        before = worker.current(); original_lease = current['lease_expires_at_utc']; assert original_lease is not None
                        worker.stopped = True; command('docker', 'kill', '--signal', 'KILL', worker.id)
                        automatic = worker.current()['State']['Running']; worker.start(wait_ready=False)
                        after = worker.current(); assert after['State']['StartedAt'] != before['State']['StartedAt']
                        service_proof = {'sameContainerAndImage':True,'workerId':worker.id,'signal':'SIGKILL','manuallyStarted':not automatic,'originalLeaseExpiresAtUtc':original_lease}
                        folder_info=fixture['folder'].stat()
                        service_proof['folderAfterWorkerRestart']={'uid':folder_info.st_uid,'gid':folder_info.st_gid,'identity':[folder_info.st_dev,folder_info.st_ino]}
                    else:
                        preflight(exempt=[intent['id'], accepted['executionJobId']])
                        other_copies = query("SELECT json_build_object('otherBarriers',count(*)) FROM container_copy_intents WHERE publish_barrier AND id<>:'id'::uuid;",id=intent['id'])
                        assert other_copies['otherBarriers'] == 0
                        active=[]
                        for path in Path('/run/systemd/transient').glob('whaledeck-*.service'):
                            state=command('systemctl','show',path.name,'-p','ActiveState','--value')
                            if state not in ('inactive','failed'):active.append(path.name)
                        assert set(active) <= {held.cgroup}, 'OTHER_TRANSIENT_WORK_AGENT_RESTART_REFUSED'
                        before=command('systemctl','show','whaledeck-agent.service','-p','MainPID','--value')
                        maintenance=command('systemctl','show','whaledeck-maintenance.service','-p','MainPID','--value')
                        command('systemctl','restart','whaledeck-agent.service'); after=agent_identity(before,revision)
                        assert command('systemctl','show','whaledeck-maintenance.service','-p','MainPID','--value')==maintenance
                        assert request(client,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
                        service_proof={'oldAgentPid':before,'newAgentPid':after,'maintenancePidUnchanged':True,'apiAvailable':True}
                    elapsed = stopped.result(timeout=16)
                assert elapsed is not None, 'PRODUCT_DID_NOT_STOP_PARTIAL_COPY'
                action_seconds = time.monotonic() - began
                held.release(); held = None
                if original_lease is not None:
                    PHASE='original-natural-lease-must-not-be-bypassed'
                    for _ in range(3):
                        waiting=snapshot(intent['id'])['row']
                        assert waiting['state']=='Applying' and waiting['publish_barrier'] and waiting['lease_expires_at_utc']==original_lease
                        assert dt.datetime.fromisoformat(original_lease)>dt.datetime.now(dt.timezone.utc)
                        assert record_for(waiting)['state']=='Running'
                        assert request(client,'GET',base+'/api/v1/system/status',200).json()['build']['sourceRevision']==revision
                        time.sleep(1)
                    (evidence/'worker-crash-lease-wait.json').write_text(json.dumps(service_proof))
                final = wait_settled(intent['id']); row = final['row']
                expected='Failed' if kind in ('worker-crash','agent-restart') else 'Canceled'
                assert row['state'] == final['executionJob']['state'] == expected and row['published_version'] is None
                assert row['execution_job_id'] == accepted['executionJobId'] and row['completed_at_utc'] and row['root_started_at_utc']
                assert not row['publish_barrier'] and row['lease_token'] is None and final['pending'] == 0 and final['accepted'] == final['confirmed'] == 1
                if original_lease is not None:assert dt.datetime.fromisoformat(row['completed_at_utc'])>=dt.datetime.fromisoformat(original_lease)
                clean(fixture, row, evidence, kind); cleaned = True
                result = {'kind':kind,'id':intent['id'],'executionId':row['execution_job_id'],'state':row['state'],'code':row['result_code'],
                    'rootState':'Aborted','productStoppedHelperSeconds':round(elapsed,3),'validatorKilledHelper':False,'targetAbsent':True,'serviceRestart':service_proof,'actionCompletedSeconds':round(action_seconds,3)}
                results.append(result);(evidence/'results.json').write_text(json.dumps({'revision':revision,'cases':results}))
                if not signed_out: logout(base, client); signed_out = True
            finally:
                if held is not None: held.release()
                worker.start()
                if intent is not None and not cleaned:
                    final = wait_settled(intent['id']); row = final['row']
                    if row['execution_job_id'] and row['state'] in ('Succeeded','Failed','Canceled'): clean(fixture,row,evidence,kind+'-failed-gate')
                    # Unknown retains its exact evidence and protection; no SQL edits or blind deletion.
                if not signed_out: logout(base, client)
                client.close()
    preflight();(evidence/'verified.json').write_text(json.dumps({'revision':revision,'cases':results,'partialCopies':2,'rootAndBusinessHistoryRetained':True}))
    print(json.dumps({'installedContainerCopyInflight':'passed','evidence':str(evidence),'cases':results}),flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerCopyInflight':'failed','phase':PHASE,'failureType':type(error).__name__,
            'locations':[{'file':Path(f.filename).name,'line':f.lineno} for f in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved':True}),file=sys.stderr)
        raise SystemExit(1) from None
