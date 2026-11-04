#!/usr/bin/env python3
"""Independent permanent Partial/ACK and retained history proof after cleanup."""
import hashlib,json,os,re,struct,subprocess,sys
from pathlib import Path
import requests

from validate_container_recursive_acl_partial import SOURCES,host_identity,no_active_work,preflight,load,preservation_snapshot,owned,snapshot,source_closure
from recursive_acl_partial_proof import require_ack,verify_records,require_case
from verify_container_recursive_permissions import private_json
from validate_container_upload_recovery_agent import trusted
from validate_metric_continuity import query
from run_with_ephemeral_admin import AUTHENTIK_API,container_environment,require


def target(name):
    assert re.fullmatch('container-recursive-acl-partial-[a-f0-9]{32}',name)
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def verify(name):
    p=target(name);trusted(p,True);scope=private_json(p/'scope.json');b=private_json(p/'baseline.json');accepted=private_json(p/'verified.json');facts=private_json(p/'facts.json')
    owner=private_json(p/'owner-browser.json');recovery=private_json(p/'recovery-browser.json');partial=private_json(p/'partial.sql.json');ack=private_json(p/'ack.sql.json')
    kind=b.get('kind','cancel');assert kind in ('cancel','logout') and scope.get('kind','cancel')==accepted.get('kind','cancel')==kind
    repo=Path(__file__).resolve().parents[2];assert scope['validationSourceSha256']==source_closure(repo,scope['validationSourceRevision'],seeds=SOURCES)
    for n,sha in scope['validationSourceSha256'].items():assert hashlib.sha256(subprocess.check_output(['git','-C',str(repo),'show',scope['validationSourceRevision']+':deploy/validation/'+n])).hexdigest()==sha
    for n,sha in scope['sourceHashes'].items():
        raw=subprocess.check_output(['git','-C',str(repo),'show',scope['revision']+':deploy/host/'+n]);assert hashlib.sha256(raw).hexdigest()==sha and (Path('/usr/local/libexec')/n).read_bytes()==raw
    assert host_identity(scope['revision'])==scope['hostIdentity'] and snapshot(accepted['id'])==ack
    assert accepted['actualPartialAndDifferentAdminAck'] and owner['subject']==partial['row']['actor_subject'] and recovery['subject']!=owner['subject']
    for role,ui in (('owner',owner),('recovery',recovery)):
        assert ui['id']==accepted['id'] and ui['actualUi'] and ui['normalDualLogout'] and ui['uploads']==0
        assert ui['registrations']==ui['confirmations']==int(role=='owner') and ui['cancellations']==int(role=='owner' and kind=='cancel') and ui['acknowledgements']==int(role=='recovery')
        logout=ui['logout'];assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['finalLoginPage'] and logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
    if kind=='logout':assert private_json(p/'owner-logout.json')==owner['logout']
    require_case(kind,partial,owner,private_json(p/'logout-authority.json') if kind=='logout' else None)
    require_ack(partial,ack,recovery['subject']);assert private_json(p/'recovery-canceled.json')['acknowledgements']==0
    files=load('/usr/local/libexec/whaledeck-files.py','partial_second_files');storage=load('/usr/local/libexec/whaledeck-file-backups.py','partial_second_hdd').FileBackups(files)
    before=b['original'];after=private_json(p/'physical-partial.json');assert after==private_json(p/'physical-final.json')
    assert verify_records(partial,b,before,after,storage,p,'partial',True)==facts['partial'] and verify_records(ack,b,before,after,storage,p,'ack',True)==facts['ack']
    assert (p/'partial.root.json').read_bytes()==(p/'ack.root.json').read_bytes() and (p/'partial.hdd.json').read_bytes()==(p/'ack.hdd.json').read_bytes()
    audits=query("SELECT json_build_object('n',count(*),'actors',json_agg(actor_subject),'results',json_agg(result)) FROM audit_events WHERE job_id=:'id'::uuid AND action='containers.files.recursive-acl.partial-acknowledged';",id=ack['row']['execution_job_id'])
    assert audits['n']==1 and audits['actors']==[recovery['subject']] and audits['results']==['Partial']
    root=Path(b['source']);assert root.parent==owned.common.APPS and root.name==b['containerName'] and not os.path.lexists(root)
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    observed=preservation_snapshot(storage);assert all(observed.get(k)==v for k,v in scope['preservedFiles'].items())
    core=owned.common.core_snapshot();assert core.keys()==scope['coreBefore'].keys()
    for n,v in scope['coreBefore'].items():assert core[n][:3]==tuple(v[:3]) if n.lstrip('/')=='whaledeck-worker' else core[n]==tuple(v)
    images=list(p.glob('*.png'));assert {v.name for v in images}=={'inflight.png','owner-logged-out.png' if kind=='logout' else 'partial-counts.png','recovery-counts.png','recovery-confirm.png','acknowledged.png'}
    for image in images:
        raw=image.read_bytes();assert len(raw)>1000 and raw[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',raw[16:24])==(320,600)
    api=requests.Session();api.trust_env=False;api.headers['Authorization']='Bearer '+require(container_environment(),'Authentication__Authentik__ApiToken')
    try:
        r=api.get(AUTHENTIK_API+'core/users/',params=dict(search='whaledeck-upload-qa-',page_size=100),timeout=20);assert r.status_code==200 and not r.json().get('pagination',{}).get('next') and not any(u['username'].startswith('whaledeck-upload-qa-') for u in r.json()['results'])
    finally:api.close()
    no_active_work();preflight()
    return dict(independentReadOnlyVerifier=True,actualPartialAndDifferentAdminAck=True,kind=kind,partialCounts=facts['partial']['counts'],rootAndHddRereadAndRetained=True,ackAuditCurrentDifferentAdmin=True,
        normalDualLogoutContexts=2,temporaryIdentitiesAbsent=True,exactSourceAndContainerAbsent=True,physicalBytesCheckedByParentBeforeCleanup=True,sourceBytesRereadAfterCleanup=False)


if __name__=='__main__':
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    p=target(sys.argv[1]);value=verify(sys.argv[1]);(p/'independent.json').write_text(json.dumps(value));print(json.dumps(value))
