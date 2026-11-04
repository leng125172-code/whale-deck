#!/usr/bin/env python3
"""Separate read-only process before and after exact source cleanup."""
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys

import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from verify_container_recursive_acl import private_json
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from validate_recursive_acl_restore_paging import SOURCES as BASE_SOURCES,PROBE_SOURCES,unchanged,no_restore_records,restore_database
from recursive_acl_restore_prewrite import SOURCES as EXTRA_SOURCES,LOGOUT_SOURCES
SOURCES=(*BASE_SOURCES,*EXTRA_SOURCES)
from recursive_acl_history_paging_proof import content,inventory,verify_capture,summary,prove_page
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows
from validate_container_file_publishing import common
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl_agent import proto_plan,ready_proof

def verify(name,live=False,*,logout=False):
    assert type(logout) is type(live) is bool
    prefix='recursive-acl-restore-inflight-logout-' if logout else 'recursive-acl-restore-prewrite-cancel-'
    assert re.fullmatch(prefix+'[a-f0-9]{32}',name)
    seeds=(*SOURCES,*LOGOUT_SOURCES) if logout else SOURCES
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    saved=lambda name:private_json(evidence/name)
    b=saved('baseline.json');row=saved('capture.json');browser=saved('browser.json')
    for key in ('revision','probeRevision','validationSourceRevision'):assert re.fullmatch('[a-f0-9]{40}',b[key])
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=seeds)
    for name,sha in b['validationSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['validationSourceRevision']+':deploy/validation/'+name])).hexdigest()==sha
    assert set(b['probeSourceSha256'])==set(PROBE_SOURCES)
    for name,sha in b['probeSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['probeRevision']+':'+name])).hexdigest()==sha
    published=private_json(repo/'deploy/artifacts'/('recursive-acl-history-paging-probe-'+b['probeRevision'][:7])/'published-probe.json')
    binary=Path(published['binary']);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    trusted(binary);trusted(binary.parent,True)
    assert published['probeRevision']==b['probeRevision'][:7] and hashlib.sha256(binary.read_bytes()).hexdigest()==published['binarySha256']==b['probeBinarySha256']
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True,timeout=30)
    for name,sha in b['sourceHashes'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+name]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/name).read_bytes()==raw
    transcript=[json.loads(line) for line in (evidence/'rpc.jsonl').read_bytes().splitlines()];assert [r['kind'] for r in transcript]==['ready','plan','receipt','done']
    ready_proof(transcript[0],b);assert proto_plan(json.loads(transcript[1]['plan']))==row['request']['plan'] and transcript[1]['boundaryRefusals']==0
    assert transcript[2]['operationId']==row['request']['operationId'] and json.loads(transcript[2]['result'])==row['result'] and transcript[3]==dict(kind='done')
    assert (evidence/'rpc.stderr').read_bytes()==b''
    reader=load('/usr/local/libexec/whaledeck-container-files.py','paging_independent_reader');f=reader.trusted_module('whaledeck-files.py','paging_independent_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','paging_independent_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','paging_independent_storage').FileBackups(f)
    data=content(b['containerId']);assert len(data)==b['bytesPerFile'] and hashlib.sha256(data).hexdigest().upper()==b['sha256']
    metadata=verify_capture(evidence,b,row,storage);actual=unchanged(b,storage);capture=row['request']['operationId']
    from recursive_acl_restore_paging_proof import page_proof,ready_proof as ready_business
    from recursive_acl_restore_prewrite_proof import read_early,early_abort_proof,sql_proof
    from recursive_acl_restore_agent_io import installed,read_bundle
    from validate_container_recursive_acl_restore_browser import sql_snapshot,attribute_digest,expected_execution
    from acl_history_browser_proof import typed_equal
    assert b['restorePrewriteCancel'] is (not logout) and b.get('restoreInflightLogout',False) is logout and browser['prewriteCancellation'] is (not logout) and browser['actualUi']
    signal=saved('ready-result.json');initial=saved('ready.sql.json');page_proof(signal,b,row,metadata,0);page_proof(signal['selected'],b,row,metadata,0,selection=True);ready_business(signal,b,row,metadata,initial)
    typed_equal(browser,saved('final-auth-result.json'));typed_equal(signal,browser['readySignal']);typed_equal(browser['pending'],saved('pending-result.json'))
    current=sql_snapshot(signal['ready']['id']);typed_equal(current,saved('terminal.sql.json'));request,receipt=sql_proof(current,browser,initial,b,summary(row,metadata),logout=logout)
    assert current['row']['execution_job_id']==expected_execution(signal['ready']['id'])==browser['pending']['executionJobId']
    pending=saved('pending.sql.json');assert pending['row']['state']=='Pending' and pending['row']['root_started_at_utc'] is None and pending['row']['publish_barrier'] is False and pending['row']['execution_job_id']==current['row']['execution_job_id']
    io=installed();source_bundle,frame=read_bundle(io,row['request'],False,evidence/'bound-source');typed_equal(frame,saved('source-frame.json'));source=dict(**row,metadata=source_bundle['metadata'])
    records=read_early(io,request,evidence/'early-records');proof=early_abort_proof(request,receipt,records,source,frame,row['after'],row['after'],attribute_digest());typed_equal(proof,saved('early-proof.json'))
    assert (evidence/'cut-hdd.json').read_bytes()==(evidence/'early-records/hdd.json').read_bytes()
    cut=saved('cut-root.json');assert cut['state']=='Applying' and cut['startedThrough'] in (-1,0) and cut['finishedThrough']==-1 and cut['plan']==request['plan'] and cut['owner']==request['owner'] and cut['operationId']==request['operationId'] and cut['historyVerified'] is cut['historyMayExist'] is True
    bound=saved('bound-helper.json');assert re.fullmatch(r'whaledeck-container-recursive-acl-restore-[a-f0-9]{32}\.service',bound['unit'])
    assert bound['proof']['twoKernelFdProofs'] and bound['proof']['originalFullTreeUnchanged'] and bound['proof']['exactCgroupAndInvocation'] and bound['proof']['pidfdStartTimePinned'] and type(bound['proof']['processStartTicks']) is int and bound['proof']['processStartTicks']>0 and bound['proof']['mainPid']==bound['pid'] and re.fullmatch('[a-f0-9]{32}',bound['proof']['invocationId'])
    timing=saved('product-helper-exit.json');assert 0<timing['seconds']<15 and timing['validatorKilledHelper'] is False
    assert browser['registrations']==browser['confirmations']==1 and browser['cancellations']==int(not logout) and browser['uploads']==0 and browser['normalDualLogout'] and browser['anonymous']==[401]*6 and browser['hintsCleared']
    if logout:
        from recursive_acl_restore_logout_proof import authority_proof
        authority_proof(browser)
    else:assert browser['dismissedWithoutWrite'] is True
    assert browser['ordinary']['subject']!=browser['subject'] and browser['ordinary']['denied']==[403]*6 and browser['ordinary']['posts']==[403]*2 and browser['ordinary']['anonymous']==[401]*6 and browser['ordinary']['uiRequests']==0
    for session_logout in (browser['logout'],browser['ordinary']['logout']):assert session_logout['normalUiLogout'] and session_logout['providerNavigationObserved'] and session_logout['bffStatus']==401 and session_logout['identityStatus'] in (401,403) and session_logout['finalLoginPage']
    pages=signal['pages'];prove_pages(pages,b,{capture:summary(row,metadata)});audits=audit_rows(signal['subject']);typed_equal(audits,saved('history-audits.json'));prove_audits(audits,signal['subject'],b,pages,[signal['selected'],signal]);assert audit_rows(browser['ordinary']['subject'])==[]
    assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions']+1,active=0)
    before_worker=saved('worker-before.json');after_worker=saved('worker-after.json');assert before_worker['Id']==after_worker['Id'] and before_worker['Image']==after_worker['Image'] and before_worker['State']['StartedAt']!=after_worker['State']['StartedAt'] and after_worker['State']['Running'] and after_worker['State']['Health']['Status']=='healthy'
    images=list(evidence.glob('*.png'));assert {p.name for p in images}==({'selection.png','paging-0.png','inflight.png','terminal.png','ordinary-denied.png'}|({'original-logout.png','new-login-no-old-task.png'} if logout else set()))
    for image in images:
        raw=image.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n' and len(raw)>1000 and struct.unpack('>II',raw[16:24])==(320,600)
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        r=api.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next')
        assert not any(str(u['username']).startswith('whaledeck-upload-qa-') for u in r.json()['results'])
    finally:api.close()
    source_path=Path(b['source']);assert source_path.parent==common.APPS and source_path.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source_path.name)
    if live:
        assert source_path.resolve(strict=True)==source_path and Path(b['root'])==source_path/'recursive-tree' and inventory(Path(b['root']),reader,data,acl)==row['after']
        from validate_container_file_publishing import verify_container
        inspected=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(inspected,b['containerId'],b['containerName'],source_path)
    else:
        assert not os.path.lexists(source_path);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        assert saved('live-independent.json')['physicalTreeReread'] is True and saved('final-preservation.json')==actual
        accepted=saved('verified.json');assert accepted['objects']==26 and accepted['restoreExecutions']==1 and accepted['canceled']==1 and accepted['restorePrewriteCancel'] is (not logout) and accepted.get('restoreInflightLogout',False) is logout and accepted['sourceAndContainerAbsent'] is True
    return dict(revision=b['revision'],independentReadOnly=True,physicalTreeReread=live,sourceBytesRereadAfterCleanup=False,sourceChildHdd=26,restoreAggregateHdd=1,restoreChildHdd=proof['childHdd'],counts=proof['counts'],temporaryIdentitiesAbsent=True,sourceAndContainerAbsent=not live,normalDualLogoutOriginalContext=True,originalWholeTreeUnchanged=True,productStoppedHelperSeconds=timing['seconds'],restoreExecutions=1,prewriteCancel=not logout,inflightLogout=logout,oldProtected=len(b['protectedFiles']),finalProtected=len(actual))

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        live=sys.argv[2:]==['--live'];repo=Path(__file__).resolve().parents[2];v=verify(sys.argv[1],live)
        path=repo/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with path.open('x') as stream:json.dump(v,stream)
        print(json.dumps(dict(actualRecursiveRestorePrewriteIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(actualRecursiveRestorePrewriteIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
