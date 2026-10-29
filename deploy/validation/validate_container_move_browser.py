#!/usr/bin/env python3
"""Actual visible Authentik -> BFF/SQL/Worker/Agent move acceptance.

Only an exact disposable non-root container is written. Immutable history stays.
Unproved fixture objects are preserved; no broad or recursive cleanup.
"""
import json
import hashlib
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import uuid

import requests
import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_copy_intents import remove_tree
from validate_container_upload_bff import login, logout, request, temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_databases import Api
from validate_metric_continuity import query

PHASE = 'startup'
ROOT = Path('/var/lib/whaledeck-container-moves')


def snapshot(identifier):
    assert str(uuid.UUID(identifier)) == identifier
    return query('''SELECT json_build_object('row',to_jsonb(c),'planJob',to_jsonb(p),'executionJob',to_jsonb(e),
      'pending',(SELECT count(*) FROM outbox_messages o WHERE o.payload_json->>'jobId' IN (c.id::text,c.execution_job_id::text) AND o.processed_at_utc IS NULL),
      'accepted',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.id AND a.action='containers.files.move.plan-accepted'),
      'confirmed',(SELECT count(*) FROM audit_events a WHERE a.job_id=c.execution_job_id AND a.action='containers.files.move.confirmed'),
      'events',(SELECT count(*) FROM operation_job_events j WHERE j.job_id IN (c.id,c.execution_job_id)))
      FROM container_move_intents c JOIN operation_jobs p ON p.id=c.id LEFT JOIN operation_jobs e ON e.id=c.execution_job_id WHERE c.id=:'id'::uuid;''', id=identifier)


def verify(result, baseline, subject, evidence, reader, content):
    value = snapshot(result['id']); row = value['row']
    assert row['actor_subject'] == subject and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
    assert row['state'] == value['planJob']['state'] == value['executionJob']['state'] == 'Succeeded'
    assert row['execution_job_id'] == result['executionJobId'] and row['id'] != row['execution_job_id']
    assert row['completed_at_utc'] and row['root_started_at_utc'] and not row['publish_barrier'] and not row['private_may_exist']
    assert row['lease_token'] is None and row['lease_expires_at_utc'] is None and result['operationKind'] == 'Move'
    assert all(row[key] is True for key in ('destination_published','source_removed','source_retained'))
    assert all(result[key] is True for key in ('destinationPublished','sourceRemoved','sourceRetained'))
    assert value['pending'] == 0 and value['accepted'] == value['confirmed'] == 1 and value['events'] >= 4
    assert row['source_path'] == result['command']['source']['path'] and row['target_path'] == result['command']['targetPath']
    assert row['published_version'] == result['publishedVersion']
    plan = json.loads(row['plan_json']); assert plan['Kind'] == result['kind'] and plan['OperationKind'] == 'Move'
    operation = uuid.UUID(row['execution_job_id']).hex; path = ROOT / (operation + '.json'); info = trusted(path)
    assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_size <= 65536
    raw = path.read_bytes(); receipt = json.loads(raw)
    assert receipt['operationId'] == operation and receipt['state'] == 'Completed' and receipt['owner'] == row['owner_hash']
    assert receipt['plan']['fingerprint'] == plan['Fingerprint'] and receipt['version'] == row['published_version']
    assert receipt['plan']['operationKind'] == 'Move' and all(receipt[key] is True for key in ('destinationPublished','sourceRemoved','sourceRetained'))
    assert receipt['plan']['command'] == {'containerId':row['container_id'],'instanceId':row['instance_id'],'sourcePath':row['source_path'],'targetPath':row['target_path'],'expectedVersion':result['command']['expectedVersion']}
    selected = [item for item in baseline['sources'] if 'work/' + item['name'] == row['source_path']]
    assert len(selected) == 1 and selected[0]['kind'] == result['kind']; original = selected[0]
    source = Path(baseline['source']) / original['name']; assert not os.path.lexists(source)
    leaf = row['target_path'].removeprefix('work/'); assert leaf and '/' not in leaf and row['target_path'] == 'work/' + leaf
    output = source.parent / leaf; info = output.lstat(); assert list(reader.fingerprint(info)) == receipt['publicationIdentity']
    location = {'containerId':row['container_id'],'instanceId':row['instance_id']}
    assert reader.version(location,row['target_path'],info) == row['published_version']
    inner = ROOT / 'engines' / ('operations' if result['kind'] == 'File' else 'tree-moves') / (operation + '.json')
    trusted(inner); journal = json.loads(inner.read_bytes())
    canonical = json.dumps(journal,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
    assert hashlib.sha256(canonical).hexdigest().upper() == receipt['innerReceiptHash']
    assert journal['plan'] == receipt['plan']['innerPlan'] and journal['owner'] == row['owner_hash'] and journal['state'] == 'Completed'
    kept = source.parent / '.whaledeck-container-moves-v1' / (operation + ('.mutation' if result['kind'] == 'File' else '.tree-move'))
    for relative, old in original['metadata'].items():
        dst = output / relative if relative else output; retained = kept / relative if relative else kept
        a,b = dst.lstat(),retained.lstat(); assert not dst.is_symlink() and not retained.is_symlink()
        assert (stat.S_IFMT(a.st_mode),a.st_uid,a.st_gid,stat.S_IMODE(a.st_mode),a.st_mtime_ns) == (stat.S_IFMT(old[2]),old[3],old[4],old[2]&0o777,old[7])
        if stat.S_ISREG(a.st_mode):
            assert reader.fingerprint(b)[:8] == tuple(old[:8]) and a.st_nlink == b.st_nlink == 1 and dst.read_bytes() == retained.read_bytes() == content
        else: assert stat.S_ISDIR(b.st_mode) and (b.st_uid,b.st_gid,stat.S_IMODE(b.st_mode)) == (0,0,0o700)
    if result['kind'] == 'Directory': assert [kept.stat().st_dev,kept.stat().st_ino] == journal['retainedIdentity']
    (evidence / (operation + '.root.json')).write_bytes(raw)
    return output, receipt['publicationIdentity'], kept, list(reader.fingerprint(kept.lstat()))


def deny_unprivileged(base, baseline, completed):
    endpoint = base + '/api/v1/containers/' + baseline['containerId'] + '/files/moves'
    recovery = base + '/api/v1/files/container-moves/recovery'
    paths = [endpoint + '/' + completed['id'], endpoint + '/by-key/' + str(uuid.uuid4()), recovery, recovery + '/' + completed['id']]
    anonymous = requests.Session(); anonymous.trust_env = False
    for path in paths: request(anonymous, 'GET', path, 401)
    anonymous.close()
    with temporary_user(False) as (username, password):
        session, profile = login(base, username, password, admin=False); assert not profile['isAdministrator']
        headers = {**Api(base, session).headers, 'Idempotency-Key': str(uuid.uuid4())}
        for path in paths: request(session, 'GET', path, 403)
        for path, body in [(endpoint, {'instanceId': baseline['instanceId'], 'sourcePath': 'work/original.bin', 'targetPath': 'work/禁止移动', 'expectedVersion': baseline['sourceVersion']}),
                           (endpoint + '/' + completed['id'] + '/confirm', {'planHash': 'denied', 'version': 0, 'confirmed': True}),
                           (endpoint + '/' + completed['id'] + '/cancel', {}),
                           (recovery + '/' + completed['id'] + '/acknowledge', {'version': 0, 'confirmed': True})]:
            request(session, 'POST', path, 403, headers=headers, json=body)
        logout(base, session); session.close()


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]; evidence = repo / 'deploy/artifacts' / ('container-move-browser-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    reader = load('/usr/local/libexec/whaledeck-container-files.py', 'installed_move_browser_reader')
    with owned.fixture() as fixture:
        folder = fixture['folder']; sources = []
        for index, kind in enumerate(('File','Directory','Directory','File')):
            name = 'move-source-' + str(index) + ('.bin' if kind == 'File' else '')
            root = folder / name
            if kind == 'File': root.write_bytes(fixture['old']); os.chown(root,1000,1000);root.chmod(0o640)
            else:
                root.mkdir(mode=0o750);(root/'empty').mkdir(mode=0o700);(root/'code.bin').write_bytes(fixture['old']);(root/'code.bin').chmod(0o640)
                for i,relative in enumerate(('', 'code.bin','empty')):os.chown(root/relative,2100+index+i,3100+index+i)
            metadata = {}
            for relative in (('',) if kind == 'File' else ('','code.bin','empty')):
                path = root / relative if relative else root;os.utime(path,ns=(1700000000123456789,1700000000123456789));metadata[relative] = list(reader.fingerprint(path.lstat()))
            sources.append({'name':name,'kind':kind,'metadata':metadata})
        container = json.loads(owned.common.command(['docker', 'inspect', fixture['identifier']]))[0]
        location = {'containerId': fixture['identifier'], 'instanceId': str(container['State']['Pid']) + ':' + container['State']['StartedAt']}
        baseline = {**location, 'revision': revision, 'containerName': folder.name, 'source': str(folder), 'evidence': str(evidence),
            'sourceVersion': reader.version(location, 'work/original.bin', (folder / 'original.bin').stat()), 'sources': sources}
        for item in sources:item['version'] = reader.version(location,'work/'+item['name'],(folder/item['name']).stat())
        (evidence / 'baseline.json').write_text(json.dumps(baseline))
        PHASE = 'visible-authentik-bff-worker-move'
        child = subprocess.run(['node', str(Path(__file__).with_suffix('.mjs')), revision], input=json.dumps(baseline), text=True, capture_output=True, timeout=420)
        assert len(child.stdout.encode()) <= 32768 and len(child.stderr.encode()) <= 32768
        (evidence / 'browser.stdout').write_text(child.stdout); (evidence / 'browser.stderr').write_text(child.stderr)
        if child.returncode:
            for line in child.stderr.splitlines():
                try:
                    item = json.loads(line)
                    if item.get('installedContainerMoveBrowser') == 'failed': print(json.dumps(item), flush=True)
                except ValueError: pass
            raise AssertionError('MOVE_BROWSER_NOT_VERIFIED')
        results = json.loads((evidence / 'results.json').read_text())
        assert results['revision'] == revision and results['fullSsoLogout'] and results['registrations'] == 5 and results['confirmations'] == 4
        assert results['bodyRequests'] == 0 and results['exactHttpRetries'] == 4 and results['csrfAndOriginDenials'] == 2
        assert len(results['moves']) == 4 and len({item['id'] for item in results['moves']}) == 4
        PHASE = 'independent-sql-root-complete-content-and-attributes'
        verified = [verify(item, baseline, results['subject'], evidence, reader, fixture['old']) for item in results['moves']]
        canceled = snapshot(results['canceled']['id']); row = canceled['row']
        assert row['state'] == 'Canceled' and row['execution_job_id'] is None and row['root_started_at_utc'] is None and row['completed_at_utc']
        assert all(row[key] is None for key in ('destination_published','source_removed','source_retained'))
        assert canceled['accepted'] == 1 and canceled['confirmed'] == canceled['pending'] == 0
        assert not (folder / '取消的移动').exists()
        PHASE = 'real-ordinary-and-anonymous-denials'
        deny_unprivileged(os.environ.get('WHALEDECK_URL', 'http://127.0.0.1:8080').rstrip('/'), baseline, results['moves'][0])
        assert not (folder / '禁止移动').exists()
        PHASE = 'receipt-bound-exact-fixture-cleanup'
        for (output, proof, kept, kept_proof), result in zip(verified, results['moves']):
            assert list(reader.fingerprint(output.lstat())) == proof and list(reader.fingerprint(kept.lstat())) == kept_proof
            if result['kind'] == 'File':
                assert output.read_bytes() == kept.read_bytes() == fixture['old']; output.unlink(); kept.unlink()
            else:
                remove_tree(output, proof[:2], {'code.bin':'File','empty':'Directory'}, reader, fixture['old'])
                remove_tree(kept, kept_proof[:2], {'code.bin':'File','empty':'Directory'}, reader, fixture['old'])
        private = folder / '.whaledeck-container-moves-v1'; info = trusted(private, True)
        assert stat.S_IMODE(info.st_mode) == 0o700 and not list(private.iterdir()); private.rmdir()
        (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'moved': 4, 'mixedOwnersAndRetainedOriginalInodes': True, 'canceledBeforeExecution': 1,
            'independentSqlRootBytesAndAttributes': True, 'ordinaryDeniedRoutes': 8, 'anonymousDeniedRoutes': 4,
            'normalTwoPageSsoLogout': True, 'rootAndBusinessHistoryRetained': True}))
    print(json.dumps({'installedContainerMoveBrowser': 'passed', 'evidence': str(evidence), 'revision': revision}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'installedContainerMoveBrowser': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(frame.filename).name, 'line': frame.lineno} for frame in traceback.extract_tb(error.__traceback__)],
            'unverifiedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
