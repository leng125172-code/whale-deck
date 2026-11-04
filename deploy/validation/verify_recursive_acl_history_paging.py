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
from validate_recursive_acl_history_paging import SOURCES,PROBE_SOURCES,LABELS,unchanged
from recursive_acl_history_paging_proof import content,inventory,verify_capture,summary,prove_page
from recursive_acl_history_browser_proof import prove_pages,prove_audits
from validate_container_recursive_acl_history_browser import audit_rows
from validate_container_file_publishing import common
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl_agent import proto_plan,ready_proof

def verify(name,live=False):
    assert re.fullmatch('recursive-acl-history-paging-[a-f0-9]{32}',name) and type(live) is bool
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
    assert browser['revision']==b['revision'] and browser['mutations']==0 and browser['realSnapshotPagination'] is True and browser['inventoryMultipleCaptures'] is False
    signals=browser['signals'];assert len(signals)==4;seen=[]
    pages=browser['pages'];prove_pages(pages,b,{capture:summary(row,metadata)})
    for index,(label,signal) in enumerate(zip(LABELS,signals,strict=True)):
        assert signal==saved('paging-'+label+'-result.json') and signal['subject']==browser['subject'] and signal['pages']==pages
        prove_page(signal,b,row,metadata,index);seen.append(signal)
        prove_audits(saved('paging-'+label+'.audit.json'),browser['subject'],b,pages,seen)
        assert saved('paging-'+label+'-verified.json')==dict(physicalRootHddAuditUnchanged=True)
    observed_requests=browser['requests'];assert observed_requests==[dict(method=p['request']['method'],pathname=p['request']['pathname']) for p in pages]+[dict(method=s['request']['method'],pathname=s['request']['pathname']) for s in signals]
    audits=audit_rows(browser['subject']);prove_audits(audits,browser['subject'],b,pages,signals)
    ordinary=browser['ordinary'];assert ordinary['subject']!=browser['subject'] and ordinary['denied']==[403,403] and ordinary['anonymous']==browser['adminAnonymous']==[401,401] and ordinary['uiRequests']==0 and audit_rows(ordinary['subject'])==[]
    for logout in (browser['adminLogout'],ordinary['logout']):assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['finalLoginPage'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert saved('paging-auth-result.json')==browser and saved('paging-auth-verified.json')==dict(authorityAndSourceVerified=True)
    images=list(evidence.glob('*.png'));assert {p.name for p in images}=={*(f'paging-{n}.png' for n in range(4)),'paging-ordinary.png'}
    for image in images:
        trusted(image);raw=image.read_bytes();assert raw.startswith(b'\x89PNG\r\n\x1a\n') and 1000<len(raw)<4*1024**2
        dimensions=struct.unpack('>II',raw[16:24]);assert dimensions==(1440,600) if image.name in ('paging-2.png','paging-3.png') else dimensions==(320,600)
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
        current=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(current,b['containerId'],b['containerName'],source)
    else:
        assert not os.path.lexists(source);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        assert saved('live-independent.json')['physicalTreeReread'] is True and saved('final-preservation.json')==actual
        accepted=saved('verified.json');assert accepted['objects']==26 and accepted['signedCaptures']==1 and accepted['uiOffsets']==[0,25,0,25] and accepted['historyAudits']==len(audits) and accepted['sourceAndContainerAbsent'] is True
    return dict(revision=b['revision'],independentReadOnly=True,physicalTreeReread=live,sourceBytesRereadAfterCleanup=False,aggregateHdd=1,childHdd=26,signedCaptures=1,uiOffsets=[0,25,0,25],historyAudits=len(audits),oldProtectedFiles=len(b['protectedFiles']),finalProtectedFiles=len(actual),temporaryIdentitiesAbsent=True,sourceAndContainerAbsent=not live,normalDualLogoutOriginalContexts=True,realSnapshotPagination=True,inventoryMultipleCaptures=False)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2] in ('--live','--recheck'));os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
        live=sys.argv[2:]==['--live'];recheck=sys.argv[2:]==['--recheck'];repo=Path(__file__).resolve().parents[2]
        if recheck:
            assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_RECHECK_FIRST'
            revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
            frozen=source_closure(repo,revision,seeds=SOURCES);assert all(hashlib.sha256((repo/'deploy/validation'/name).read_bytes()).hexdigest()==sha for name,sha in frozen.items())
        v=verify(sys.argv[1],live)
        if recheck:v.update(recheckOnly=True,verifierSourceRevision=revision,verifierSourceSha256=frozen)
        path=repo/'deploy/artifacts'/sys.argv[1]/('recheck-independent.json' if recheck else 'live-independent.json' if live else 'final-independent.json')
        with path.open('x') as stream:json.dump(v,stream)
        print(json.dumps(dict(actualRecursiveHistoryPagingIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(actualRecursiveHistoryPagingIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
