"""Fixed QA service faults, immutable operation and natural lease witnesses."""
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone, timedelta
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import select
import threading
import time

import requests
from validate_container_directory_restart import command, preflight, agent_identity
from validate_container_acl_faults import host_snapshot, require_host_restart, observation_expiry, observe_maintenance, maintenance_sample
from validate_metric_continuity import query
from validate_container_recycle_restore_inflight_cancel import preserved
from validate_container_recycle_restore_browser import sql
from container_recycle_restore_agent_io import STATE
import container_recycle_agent_io as source_io
from recursive_acl_restore_agent_io import read_at
from acl_history_browser_proof import typed_equal

KINDS = ('worker-crash', 'agent-restart')
TABLES = ('container_file_upload_operations', 'container_directory_operations', 'container_copy_intents', 'container_move_intents',
          'container_permission_intents', 'container_ownership_intents', 'container_recursive_permission_intents',
          'container_recursive_ownership_intents', 'container_acl_intents', 'container_recursive_acl_intents', 'container_recursive_acl_restore_intents', 'container_recycle_intents', 'container_recycle_restore_intents')
IMMUTABLE = ('source_intent_id', 'source_proof_hash', 'id', 'idempotency_key', 'actor_subject', 'login_binding', 'owner_hash', 'trace_id', 'container_id', 'instance_id', 'relative_path',
             'command_json', 'request_hash', 'plan_json', 'plan_hash', 'execution_job_id', 'execution_hash', 'confirmed_at_utc', 'execution_expires_at_utc', 'root_started_at_utc')


def same_operation(before, after):
    for key in IMMUTABLE: typed_equal(after[key], before[key])


def lease_held(before, data):
    row = data['row']; same_operation(before, row)
    assert row['state'] == 'Applying' and row['publish_barrier'] and row['completed_at_utc'] is row['cancel_requested_at_utc'] is None
    assert row['lease_token'] == before['lease_token'] and row['lease_expires_at_utc'] == before['lease_expires_at_utc']
    assert data['executionJob']['state'] == 'Running' and data['executionJob']['cancel_requested_at_utc'] is None


def require_transients(entries, helper, owner):
    assert re.fullmatch('whaledeck-container-recycle-restores-[a-f0-9]{32}\\.service', helper)
    assert re.fullmatch('whaledeck-qa-restore-restart-[a-f0-9]{12}\\.service', owner)
    for name, value in entries.items():
        if name in (helper, owner): continue
        # RemainAfterExit units with no processes are receipts, not active work.
        assert value['MainPID'] == value['ControlPID'] == '0' and not value['processes'], 'OTHER_TRANSIENT_WORK_RESTART_REFUSED'


def restart_allowed(held, row):
    checks = preflight([row['id'], row['execution_job_id']])
    parts = []
    for table in TABLES:
        exclude = " AND id<>:'id'::uuid" if table == 'container_recycle_restore_intents' else ''
        parts.append("'" + table + "',(SELECT count(*) FROM " + table + " WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL)" + exclude + ')')
    counts = query('SELECT json_build_object(' + ','.join(parts) + ');', id=row['id'])
    assert set(counts) == set(TABLES) and all(type(v) is int and v == 0 for v in counts.values()), 'OTHER_FILE_WORK_RESTART_REFUSED'
    group = Path('/proc/self/cgroup').read_text().strip(); owner = group.rsplit('/', 1)[1]
    assert group == '0::/system.slice/' + owner and re.fullmatch('whaledeck-qa-restore-restart-[a-f0-9]{12}\\.service', owner)
    props = dict(x.split('=', 1) for x in command('systemctl', 'show', owner, '-p', 'InvocationID,MainPID').splitlines())
    assert props['InvocationID'] == os.environ['INVOCATION_ID'] and re.fullmatch('[a-f0-9]{32}', props['InvocationID']) and props['MainPID'] == str(os.getpid())
    entries = {}
    for path in Path('/run/systemd/transient').glob('*.service'):
        if not path.name.startswith(('whaledeck-', 'wd-qa-')): continue
        p = dict(x.split('=', 1) for x in command('systemctl', 'show', path.name, '-p', 'MainPID,ControlPID,ControlGroup').splitlines())
        procs = Path('/sys/fs/cgroup') / p['ControlGroup'].lstrip('/') / 'cgroup.procs' if p['ControlGroup'] else None
        p['processes'] = procs.read_text().split() if procs and procs.exists() else []
        entries[path.name] = p
    require_transients(entries, held.cgroup, owner)
    return dict(checks=checks, counts=counts, transientScopeVerified=True, ownerUnit=owner)


def restart(kind, held, worker, revision, e):
    assert kind in KINDS
    start = sql(json.loads((e/'armed-intent.json').read_text())['id'])
    row = start['row']; assert row['state'] == 'Applying' and row['publish_barrier'] and row['root_started_at_utc']
    checks = restart_allowed(held, row); hosts = host_snapshot(revision)
    start = sql(row['id']); row = start['row']; lease_held(row, start); observation_expiry(row, datetime.now(timezone.utc))
    (e/'restart-start-sql.json').write_text(json.dumps(start))
    assert not select.select([held.descriptor], [], [], 0)[0], 'ORIGINAL_HELPER_EXITED_BEFORE_SERVICE_FAULT'
    if kind == 'worker-crash':
        before = worker.current(); began = time.monotonic()
        with ThreadPoolExecutor(max_workers=1) as pool:
            exiting = pool.submit(held.wait_exit_seconds, began, 20)
            worker.stopped = True; command('docker', 'kill', '--signal', 'KILL', worker.id)
            crash = sql(row['id']); lease_held(row, crash); (e/'after-worker-crash-sql.json').write_text(json.dumps(crash))
            automatic = worker.current()['State']['Running']; worker.start(wait_ready=False)
            until = time.monotonic() + 10
            while True:
                after = worker.current()
                if after['State']['Running'] and after['State']['StartedAt'] != before['State']['StartedAt']: break
                assert time.monotonic() < until; time.sleep(.05)
            elapsed = exiting.result(timeout=21)
        service = dict(kind=kind, sameWorkerContainerAndImage=True, workerId=worker.id, imageId=after['Image'], signal='SIGKILL', manuallyStarted=not automatic,
                       beforeStartedAt=before['State']['StartedAt'], afterStartedAt=after['State']['StartedAt'], hostsBefore=hosts, hostsAfter=host_snapshot(revision), otherActiveWork=checks)
        assert service['hostsBefore'] == service['hostsAfter']
    else:
        stop, ready = threading.Event(), threading.Event()
        with ThreadPoolExecutor(max_workers=2) as pool:
            monitor = pool.submit(observe_maintenance, stop, ready)
            try:
                assert ready.wait(5), 'MAINTENANCE_BEFORE_RESTART_MISSING'
                began = time.monotonic(); started = datetime.now(timezone.utc)
                exiting = pool.submit(held.wait_exit_seconds, began, 20)
                command('systemctl', 'restart', 'whaledeck-agent.service')
                agent_identity(str(hosts['whaledeck-agent.service']['pid']), revision)
                after = host_snapshot(revision); require_host_restart(hosts, after, revision); ended = datetime.now(timezone.utc)
                with requests.Session() as client:
                    client.trust_env = False; last = maintenance_sample(client)
                elapsed = exiting.result(timeout=21)
            finally: stop.set()
            samples = monitor.result(timeout=5) + [last]; samples.sort(key=lambda v: v['atUtc'])
        (e/'maintenance-observations.json').write_text(json.dumps(samples))
        service = dict(kind=kind, hostsBefore=hosts, hostsAfter=after, restartBeganAtUtc=started.isoformat(), restartEndedAtUtc=ended.isoformat(), maintenanceSamples=len(samples), otherActiveWork=checks)
    assert elapsed is not None and 0 < elapsed < 20, 'ORIGINAL_HELPER_EXIT_NOT_OBSERVED'
    (e/'helper-exit.json').write_text(json.dumps(dict(elapsedSeconds=elapsed, observedWithOriginalPidfd=True, measurementBeginsBeforeServiceFault=True)))
    (e/'service-restart.json').write_text(json.dumps(service))
    return row


def natural_lease(row, fixture, e):
    began=time.monotonic();wall=datetime.now(timezone.utc);expiry=observation_expiry(row,wall)
    source=json.loads((e/'source-proof.json').read_text());source_op=source['request']['operationId']
    path=STATE/(row['execution_job_id'].replace('-','')+'.json');raw,record,_=read_at(None,str(path))
    assert record['state'] in ('Applying','Aborted') and record['claimed'] is record['historyVerified'] is True
    claim_path=STATE/'claims'/(source_op+'.json')
    try:claim_raw,_,_=read_at(None,str(claim_path))
    except FileNotFoundError:claim_raw=b'null'
    (e/'after-helper-exit-root.json').write_bytes(raw);(e/'after-helper-exit-claim.json').write_bytes(claim_raw);samples=[]
    while datetime.now(timezone.utc)<expiry:
        assert time.monotonic()-began<100
        data=sql(row['id']);at=datetime.now(timezone.utc)
        if at>=expiry:break
        lease_held(row,data);assert path.read_bytes()==raw
        try:current_claim,_,_=read_at(None,str(claim_path))
        except FileNotFoundError:current_claim=b'null'
        assert current_claim==claim_raw
        typed_equal(source_io.physical(Path(fixture['folder']),source_op),source['physical'])
        typed_equal(source_io.read_operation(source['request'],source['receipt'],e/'source-originals'),source['records'])
        samples.append(dict(atUtc=at.isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],sourceUnchanged=True,rootSha256=hashlib.sha256(raw).hexdigest(),claimSha256=hashlib.sha256(claim_raw).hexdigest(),hddDataReread=True))
        time.sleep(min(2,max(.001,(expiry-at).total_seconds())))
    proof=dict(waitBeganAtUtc=wall.isoformat(),waitEndedAtUtc=datetime.now(timezone.utc).isoformat(),monotonicSeconds=time.monotonic()-began,originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],samples=samples,clocksOrSqlModified=False)
    assert len(samples)>=3
    (e/'natural-lease.json').write_text(json.dumps(proof))


def verify_lease(row, final, proof, raw):
    expiry = datetime.fromisoformat(row['lease_expires_at_utc']); began = datetime.fromisoformat(proof['waitBeganAtUtc']); ended = datetime.fromisoformat(proof['waitEndedAtUtc'])
    assert timedelta(seconds=25) < expiry-began <= timedelta(seconds=95) and ended >= expiry and datetime.fromisoformat(final['completed_at_utc']) >= expiry
    assert proof['originalLeaseToken'] == row['lease_token'] and proof['originalLeaseExpiresAtUtc'] == row['lease_expires_at_utc'] and proof['clocksOrSqlModified'] is False
    assert abs((ended-began).total_seconds()-proof['monotonicSeconds']) < 3 and proof['monotonicSeconds'] < 100 and len(proof['samples']) >= 3
    previous = began
    for s in proof['samples']:
        at = datetime.fromisoformat(s['atUtc']); assert previous <= at < expiry and (at-previous).total_seconds() < 5; previous = at
        assert s['state'] == 'Applying' and s['barrier'] and s['leaseToken'] == row['lease_token'] and s['leaseExpiresAtUtc'] == row['lease_expires_at_utc'] and s['sourceUnchanged'] and s['rootSha256'] == hashlib.sha256(raw).hexdigest()
    assert (ended-previous).total_seconds() < 5


def preserved_restart(before, after, source_op, restore_op, service, kind):
    assert kind in KINDS and service['kind'] == kind and service['otherActiveWork']['transientScopeVerified']
    assert all(type(v) is int and v == 0 for k in ('checks', 'counts') for v in service['otherActiveWork'][k].values())
    assert set(service['otherActiveWork']['counts']) == set(TABLES)
    expected = copy.deepcopy(before)
    typed_equal(service['hostsBefore'], before['hosts']); typed_equal(service['hostsAfter'], after['hosts'])
    if kind == 'worker-crash':
        a, b = before['core']['/whaledeck-worker'], after['core']['/whaledeck-worker']
        assert a[:3] == b[:3] and a[2] == 'running' and a[3] == service['beforeStartedAt'] and b[3] == service['afterStartedAt'] and a[3] != b[3]
        assert service['sameWorkerContainerAndImage'] and service['workerId'] == a[0] and service['imageId'] == a[1] and service['signal'] == 'SIGKILL'
        expected['core']['/whaledeck-worker'] = b
    else:
        revision = before['hosts']['whaledeck-agent.service']['sourceRevision']; require_host_restart(before['hosts'], after['hosts'], revision)
        expected['hosts'] = after['hosts']
    preserved(expected, after, source_op, restore_op)


def verify_restart(e, fixture, final, before, after):
    read = lambda n: json.loads((e/n).read_text())
    service = read('service-restart.json'); start = read('restart-start-sql.json'); row = start['row']; same_operation(row, final); lease_held(row, start)
    source=read('source-proof.json');preserved_restart(before,after,source['request']['operationId'],final['execution_job_id'].replace('-',''),service,fixture['kind'])
    x = read('helper-exit.json'); assert x['observedWithOriginalPidfd'] and x['measurementBeginsBeforeServiceFault'] and 0 < x['elapsedSeconds'] < 20
    if fixture['kind'] == 'worker-crash':
        lease_held(row, read('after-worker-crash-sql.json'))
        raw=(e/'after-helper-exit-root.json').read_bytes();root=json.loads(raw);cut=read('cut-record.json')
        assert root['operationId']==final['execution_job_id'].replace('-','') and root['owner']==final['owner_hash'] and root['plan']==cut['plan'] and root['historyDigest']==final['history_digest'] and root['state'] in ('Applying','Aborted') and root['historyVerified']
        verify_lease(row, final, read('natural-lease.json'), raw)
        claim=(e/'after-helper-exit-claim.json').read_bytes();assert all(v['claimSha256']==hashlib.sha256(claim).hexdigest() and v['hddDataReread'] is True for v in read('natural-lease.json')['samples'])
    else:
        samples = read('maintenance-observations.json'); began = service['restartBeganAtUtc']; ended = service['restartEndedAtUtc']; times = [v['atUtc'] for v in samples]
        assert len(samples) == service['maintenanceSamples'] >= 3 and times == sorted(times) and times[0] <= began < ended <= times[-1] and any(began < t < ended for t in times)
        assert all(v['httpStatus'] == 200 and v['state'] == 'healthy' and v['phase'] == 'Ready' and v['noStore'] for v in samples)
