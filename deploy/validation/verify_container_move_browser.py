#!/usr/bin/env python3
"""Read-only post-cleanup verification, never physical bytes-after-deletion proof.

The parent browser acceptance already read all real published/retained bytes.
This separate process checks immutable SQL/Root and exact resource/identity
cleanup. It cannot create users, operate containers, write SQL or remove files.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid

import requests

from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require
from validate_container_directory_restart import preflight
from validate_container_move_browser import ROOT, snapshot
from validate_container_file_publishing import common
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query


def verify(directory):
    repo = Path(__file__).resolve().parents[2]
    assert directory.parent == repo / 'deploy/artifacts' and re.fullmatch('container-move-browser-[a-f0-9]{32}',directory.name)
    trusted(directory,True)
    baseline = json.loads((directory/'baseline.json').read_bytes()); results = json.loads((directory/'results.json').read_bytes())
    accepted = json.loads((directory/'verified.json').read_bytes())
    assert results['revision'] == baseline['revision'] == accepted['revision'] and results['fullSsoLogout']
    assert results['registrations'] == 5 and results['confirmations'] == results['exactHttpRetries'] == 4 and results['bodyRequests'] == 0
    expected_sha = hashlib.sha256(hashlib.shake_256(('owned-container-original:'+baseline['containerName']).encode()).digest(common.SIZE)).hexdigest().upper()
    records = []
    for item in results['moves']:
        state = snapshot(item['id']); row = state['row']; op = uuid.UUID(row['execution_job_id']).hex
        assert row['actor_subject'] == results['subject'] and row['container_id'] == baseline['containerId'] and row['instance_id'] == baseline['instanceId']
        assert row['state'] == state['planJob']['state'] == state['executionJob']['state'] == 'Succeeded'
        assert state['accepted'] == state['confirmed'] == 1 and state['pending'] == 0 and row['lease_token'] is None and not row['publish_barrier']
        assert all(row[k] is True for k in ('destination_published','source_removed','source_retained'))
        file = ROOT/(op+'.json');trusted(file);raw=file.read_bytes();receipt=json.loads(raw)
        assert raw == (directory/(op+'.root.json')).read_bytes() and receipt['operationId'] == op and receipt['owner'] == row['owner_hash']
        assert receipt['state'] == 'Completed' and receipt['version'] == row['published_version'] == item['publishedVersion']
        assert receipt['plan']['operationKind'] == 'Move' and all(receipt[k] is True for k in ('destinationPublished','sourceRemoved','sourceRetained'))
        source = next(s for s in baseline['sources'] if 'work/'+s['name'] == row['source_path'])
        assert receipt['plan']['sourceIdentity'] == source['metadata']['']
        plan=json.loads(row['plan_json']);assert plan['Fingerprint'] == receipt['plan']['fingerprint'] and plan['OperationKind'] == 'Move'
        inner=ROOT/'engines'/('operations' if item['kind']=='File' else 'tree-moves')/(op+'.json');trusted(inner);journal=json.loads(inner.read_bytes())
        canonical=json.dumps(journal,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
        assert hashlib.sha256(canonical).hexdigest().upper() == receipt['innerReceiptHash'] and journal['plan'] == receipt['plan']['innerPlan'] and journal['state']=='Completed'
        if item['kind']=='File':assert journal['plan']['sha256'] == expected_sha
        else:
            manifest_path=inner.with_name(op+'.manifest.json');trusted(manifest_path);manifest=json.loads(manifest_path.read_bytes())
            manifest_payload=json.dumps(manifest,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode()
            assert hashlib.sha256(b'whaledeck-tree-move-v1\n'+manifest_payload).hexdigest().upper()==journal['manifestDigest']
            assert manifest['manifestSha256']==journal['plan']['copyPlan']['manifestSha256']
            files=[entry for entry in manifest['entries'] if entry['kind']=='File'];assert len(files)==1 and files[0]['sha256']==expected_sha
        records.append({'id':row['id'],'execution':row['execution_job_id'],'kind':item['kind'],'rootSha256':hashlib.sha256(raw).hexdigest()})
    assert len(records)==4 and len({r['id'] for r in records})==4
    canceled=snapshot(results['canceled']['id']);assert canceled['row']['state']=='Canceled' and canceled['row']['execution_job_id'] is None and canceled['pending']==canceled['confirmed']==0
    assert not Path(baseline['source']).exists()
    missing=subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=20);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    assert all(v==0 for v in preflight().values())
    active=query("SELECT json_build_object('active',(SELECT count(*) FROM container_move_intents WHERE completed_at_utc IS NULL),'barriers',(SELECT count(*) FROM container_move_intents WHERE publish_barrier),'leases',(SELECT count(*) FROM container_move_intents WHERE lease_token IS NOT NULL));")
    assert all(v==0 for v in active.values())
    core=common.core_snapshot();assert len(core)==11
    health=subprocess.check_output(['docker','ps','--format','{{.Names}}|{{.Status}}'],text=True,timeout=20).splitlines()
    assert len([r for r in health if r.startswith(('whaledeck-','database-platform-')) and '(healthy)' in r])==11
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        for prefix in ('whaledeck-validation-','whaledeck-upload-qa-'):
            users=api.get(AUTHENTIK_API+'core/users/',params={'search':prefix,'page_size':100},timeout=20);assert users.status_code==200
            assert not users.json().get('pagination',{}).get('next') and not any(str(u['username']).startswith(prefix) for u in users.json()['results'])
    finally:api.close()
    return {'revision':baseline['revision'],'moves':records,'canceled':1,'exactSourceAndContainerAbsent':True,'activeMoveWork':active,
        'temporaryIdentitiesAbsent':True,'coreHealthy':11,'physicalBytesVerifiedByParentBeforeCleanup':True,'readOnlyVerifier':True}


if __name__=='__main__':
    assert os.geteuid()==0 and len(sys.argv)==2
    result=verify(Path(sys.argv[1]).resolve(strict=True))
    print(json.dumps(result),flush=True)
