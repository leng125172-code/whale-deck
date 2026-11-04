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
from recursive_acl_restore_ready_logout import SOURCES as EXTRA_SOURCES,expired_proof,samples_proof,authority_proof
SOURCES=(*BASE_SOURCES,*EXTRA_SOURCES)
from recursive_acl_history_paging_proof import content,inventory,verify_capture,summary,prove_page
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows
from validate_container_file_publishing import common
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl_agent import proto_plan,ready_proof

def verify(name,live=False):
    assert re.fullmatch('recursive-acl-restore-ready-logout-[a-f0-9]{32}',name) and type(live) is bool
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True)
    saved=lambda name:private_json(evidence/name)
    b=saved('baseline.json');row=saved('capture.json');browser=saved('browser.json')
    for key in ('revision','probeRevision','validationSourceRevision'):assert re.fullmatch('[a-f0-9]{40}',b[key])
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=SOURCES)
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
    from validate_container_recursive_acl_restore_browser import sql_snapshot
    assert b['restoreReadyLogout'] is True and browser['realReadyLogout'] is True and browser['restoreExecutions']==0
    signal=saved('ready-result.json');logout=saved('logout-result.json');final=saved('final-auth-result.json')
    from acl_history_browser_proof import typed_equal
    typed_equal(final,browser);typed_equal(browser['readySignal'],signal);typed_equal(browser['logoutSignal'],logout)
    page_proof(signal,b,row,metadata,0);page_proof(signal['selected'],b,row,metadata,0,selection=True)
    initial=saved('ready.sql.json');ready_business(signal,b,row,metadata,initial)
    typed_equal(saved('post-logout.sql.json'),initial)
    current=sql_snapshot(signal['ready']['id']);typed_equal(current,saved('expired.sql.json'));expired_proof(current,initial,b,row,metadata)
    samples_proof(saved('natural-expiry-observations.json'),initial,current);no_restore_records(signal['ready']['id'])
    assert final['result']['id']==signal['ready']['id'] and final['result']['version']==current['row']['version']
    typed_equal(final['result']['restoreSource'],summary(row,metadata))
    authority_proof(logout,final,signal['subject'])
    pages=signal['pages'];prove_pages(pages,b,{capture:summary(row,metadata)})
    audits=audit_rows(signal['subject']);typed_equal(audits,saved('history-audits.json'));prove_audits(audits,signal['subject'],b,pages,[signal['selected'],signal])
    assert browser['requests']==[dict(method='GET',pathname=p['request']['pathname']) for p in pages]+[dict(method='GET',pathname=signal['request']['pathname'])]*2
    assert saved('ready-verified.json')==dict(readySqlPhysicalSourceVerified=True) and saved('expired-verified.json')==dict(naturalExpiryWithoutExecution=True) and saved('final-auth-verified.json')==dict(identitiesAndNaturalExpiryVerified=True)
    assert audit_rows(browser['ordinary']['subject'])==[]
    from validate_metric_continuity import query
    assert query("SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.recursive-acl-restore.%';",subject=browser['ordinary']['subject'])==0
    assert restore_database()==dict(intents=b['restoreDatabase']['intents']+1,executions=b['restoreDatabase']['executions'],active=0)
    images=list(evidence.glob('*.png'));assert {p.name for p in images}=={'selection.png','paging-0.png','original-logout.png','new-login-no-old-task.png','ordinary-denied.png'}
    for image in images:
        raw=image.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n' and len(raw)>1000
        assert struct.unpack('>II',raw[16:24])==(320,600)
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        r=api.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next')
        assert not any(str(u['username']).startswith('whaledeck-upload-qa-') for u in r.json()['results'])
    finally:api.close()
    source=Path(b['source']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    if live:
        assert source.resolve(strict=True)==source and Path(b['root'])==source/'recursive-tree'
        assert inventory(Path(b['root']),reader,data,acl)==row['after']
        from validate_container_file_publishing import verify_container
        inspected=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(inspected,b['containerId'],b['containerName'],source)
    else:
        assert not os.path.lexists(source);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        assert saved('live-independent.json')['physicalTreeReread'] is True and saved('final-preservation.json')==actual
        accepted=saved('verified.json');assert accepted['objects']==26 and accepted['signedCaptures']==1 and accepted['restorePreviewOffsets']==[0] and accepted['restoreExecutions']==0 and accepted['canceled']==0 and accepted['readyLogoutNaturalExpiry'] is True and accepted['sourceAndContainerAbsent'] is True
    return dict(revision=b['revision'],independentReadOnly=True,physicalTreeReread=live,sourceBytesRereadAfterCleanup=False,aggregateSourceHdd=1,childSourceHdd=26,signedCaptures=1,historyAudits=len(audits),oldProtectedFiles=len(b['protectedFiles']),finalProtectedFiles=len(actual),temporaryIdentitiesAbsent=True,sourceAndContainerAbsent=not live,normalDualLogoutOriginalContext=True,sameSubjectNewLoginIsolated=True,naturalReadyExpiry=True,restoreExecutions=0,cancellationPosts=0,expirySamples=len(saved('natural-expiry-observations.json')))

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        live=sys.argv[2:]==['--live'];repo=Path(__file__).resolve().parents[2];v=verify(sys.argv[1],live)
        path=repo/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with path.open('x') as stream:json.dump(v,stream)
        print(json.dumps(dict(actualRecursiveRestoreReadyLogoutIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(actualRecursiveRestoreReadyLogoutIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
