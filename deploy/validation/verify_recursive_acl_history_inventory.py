#!/usr/bin/env python3
"""Independent actual 26-capture SQL/Root/HDD/HTTP observer, before/after cleanup."""
import hashlib,json,os,re,struct,subprocess,sys
from pathlib import Path
import requests
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require
from validate_container_file_staging import load
from validate_container_upload_recovery_agent import trusted
from verify_container_recursive_acl import private_json
from verify_container_recursive_acl_agent import ready_proof,proto_plan
from validate_recursive_acl_history_inventory import SOURCES,PROBE_SOURCES,content,unchanged
from recursive_acl_inventory_proof import LABELS,verify_chain,prove_signal
from validate_container_recursive_acl import inventory
from validate_container_recursive_acl_history_browser import audit_rows
from recursive_acl_history_browser_proof import prove_audits
from validate_container_file_publishing import common,verify_container
from acl_source_apply_missing_proof import source_closure

def verify(name,live=False):
    assert re.fullmatch('recursive-acl-history-inventory-[a-f0-9]{32}',name) and type(live) is bool
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;trusted(evidence,True);saved=lambda name:private_json(evidence/name)
    b=saved('baseline.json');rows=saved('captures.json');browser=saved('browser.json')
    for key in ('revision','probeRevision','validationSourceRevision'):assert re.fullmatch('[a-f0-9]{40}',b[key])
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=SOURCES)
    for n,sha in b['validationSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['validationSourceRevision']+':deploy/validation/'+n])).hexdigest()==sha
    assert set(b['probeSourceSha256'])==set(PROBE_SOURCES)
    for n,sha in b['probeSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',b['probeRevision']+':'+n])).hexdigest()==sha
    binary=Path(b['probeBinaryPath']);assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';trusted(binary);trusted(binary.parent,True)
    assert hashlib.sha256(binary.read_bytes()).hexdigest()==b['probeBinarySha256'];subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    for n,sha in b['sourceHashes'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',b['revision']+':deploy/host/'+n]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/n).read_bytes()==raw
    transcript=[json.loads(v) for v in (evidence/'rpc.jsonl').read_bytes().splitlines()];assert len(transcript)==54 and transcript[-1]==dict(kind='done');ready_proof(transcript[0],b);assert (evidence/'rpc.stderr').read_bytes()==b''
    assert len(rows)==26
    for index,row in enumerate(rows):
        planned=transcript[1+2*index];actual=transcript[2+2*index];assert planned['kind']=='plan' and planned['boundaryRefusals']==0 and proto_plan(json.loads(planned['plan']))==row['request']['plan']
        assert actual['kind']=='receipt' and actual['operationId']==row['request']['operationId'] and json.loads(actual['result'])==row['result']
    r=load('/usr/local/libexec/whaledeck-container-files.py','inventory_independent_reader');f=r.trusted_module('whaledeck-files.py','inventory_independent_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','inventory_independent_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','inventory_independent_hdd').FileBackups(f)
    data=content(b['containerId']);assert len(data)==b['bytesPerFile'] and hashlib.sha256(data).hexdigest().upper()==b['sha256'];metadata=verify_chain(evidence,b,rows,storage);protected=unchanged(evidence,b,storage)
    assert browser['revision']==b['revision'] and browser['inventoryMultipleCaptures'] is True and browser['realSnapshotPagination'] is False and browser['mutations']==0
    signals=browser['signals'];assert len(signals)==5;pages=browser['pages'];assert len(pages)==2;selected=[]
    for label,signal in zip(LABELS,signals,strict=True):
        assert signal==saved('inventory-'+label+'-result.json') and signal['subject']==browser['subject']
        prove_signal(signal,b,rows,metadata,label);assert signal['pages']==pages[:len(signal['pages'])]
        if label=='selected':selected.append(signal)
        prove_audits(saved('inventory-'+label+'.audit.json'),browser['subject'],b,signal['pages'],selected)
        assert saved('inventory-'+label+'-verified.json')==dict(originalsAndAuditVerified=True)
    audits=audit_rows(browser['subject']);prove_audits(audits,browser['subject'],b,pages,selected);assert len(audits)==3
    observed=browser['requests'];assert observed==[dict(method=p['request']['method'],pathname=p['request']['pathname']) for p in pages]+[dict(method=selected[0]['request']['method'],pathname=selected[0]['request']['pathname'])]
    other=browser['ordinary'];assert other['subject']!=browser['subject'] and other['denied']==[403,403] and other['anonymous']==browser['adminAnonymous']==[401,401] and other['uiRequests']==0 and audit_rows(other['subject'])==[]
    for logout in (browser['adminLogout'],other['logout']):assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['finalLoginPage'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    assert saved('inventory-auth-result.json')==browser and saved('inventory-auth-verified.json')==dict(authorityAndOriginalsVerified=True)
    images=list(evidence.glob('*.png'));assert {p.name for p in images}=={'inventory-'+v+'.png' for v in (*LABELS,'ordinary')}
    for p in images:
        trusted(p);raw=p.read_bytes();assert raw.startswith(b'\x89PNG\r\n\x1a\n') and 1000<len(raw)<4*1024**2;dimensions=struct.unpack('>II',raw[16:24]);assert dimensions==((320,600) if p.name in ('inventory-first.png','inventory-last.png','inventory-ordinary.png') else (1440,600))
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        response=api.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20);assert response.status_code==200 and not response.json().get('pagination',{}).get('next');assert not any(str(u['username']).startswith('whaledeck-upload-qa-') for u in response.json()['results'])
    finally:api.close()
    source=Path(b['source']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    if live:
        assert source.resolve(strict=True)==source and Path(b['root'])==source/'recursive-tree' and inventory(Path(b['root']),r,data,acl)==rows[-1]['after'];current=json.loads(common.command(['docker','inspect',b['containerId']]))[0];verify_container(current,b['containerId'],b['containerName'],source)
    else:
        assert not os.path.lexists(source);missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
        accepted=saved('verified.json');assert accepted['captures']==26 and accepted['historyAudits']==3 and accepted['exactSourceAndContainerAbsent'] is True and saved('final-preservation.json')==protected and saved('live-independent.json')['physicalTreeReread'] is True
    return dict(revision=b['revision'],independentReadOnly=True,physicalTreeReread=live,sourceBytesRereadAfterCleanup=False,captures=26,aggregateHdd=26,childHdd=104,inventoryPageSizes=[25,1],cachedPageSizes=[25,1],networkListRequests=2,snapshotReads=1,historyAudits=3,oldProtectedFiles=len(b['protectedFiles']),finalProtectedFiles=len(protected),sourceAndContainerAbsent=not live,temporaryIdentitiesAbsent=True,normalDualLogoutOriginalContexts=True,realInventoryPagination=True,realSnapshotPagination=False)

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live');os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=len(sys.argv)==3;v=verify(sys.argv[1],live)
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as stream:json.dump(v,stream)
        print(json.dumps(dict(actualRecursiveInventoryIndependent='passed',**v)))
    except BaseException as error:
        import traceback
        last=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(actualRecursiveInventoryIndependent='failed',type=type(error).__name__,file=Path(last.filename).name,line=last.lineno)));raise SystemExit(1) from None
