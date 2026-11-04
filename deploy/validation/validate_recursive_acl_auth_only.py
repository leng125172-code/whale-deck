#!/usr/bin/env python3
"""Two fresh visible identity contexts; no new intent or file operation.

Original cancelled gate remains failed. Read permanent snapshot before/after,
keep credentials only in stdin, remove only the two freshly owned identities.
"""
from contextlib import ExitStack
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_container_upload_bff import temporary_user
from validate_container_recursive_acl_browser import snapshot,no_active_work
from validate_container_file_publishing import common
from verify_container_recursive_permissions import private_json
from validate_container_upload_recovery_agent import trusted
from acl_source_apply_missing_proof import source_closure


def target(name):
    assert re.fullmatch('container-recursive-acl-browser-[a-f0-9]{32}',name)
    return Path(__file__).resolve().parents[2]/'deploy/artifacts'/name


def main(name):
    assert os.geteuid()==0;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];p=target(name);trusted(p,True);b=private_json(p/'baseline.json');signal=private_json(p/'cancel-result.json');r=signal['result']
    assert r['state']=='Canceled' and r['executionJobId'] is None and r['receipt'] is None and private_json(p/'recovered-first-default.json')['exactSourceAndContainerAbsent']
    assert not (p/'verified.json').exists() and not (p/'auth-only.json').exists()
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip()
    hashes=source_closure(repo,revision,seeds=('validate_recursive_acl_auth_only.py','validate_recursive_acl_auth_only.mjs'))
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in hashes.items())
    before=snapshot(r['id']);no_active_work();core=common.core_snapshot();command=r['command'];location=command['location']
    fixture={'containerId':b['containerId'],'id':r['id'],'version':r['version'],'evidence':str(p),'command':{'instanceId':b['instanceId'],'path':location['path'],'aclType':command['aclType'],'fileEntries':command['fileEntries'],'directoryEntries':command['directoryEntries'],'expectedVersion':command['expectedVersion']}}
    with ExitStack() as users:
        admin=users.enter_context(temporary_user(True));ordinary=users.enter_context(temporary_user(False));packet={'fixture':fixture,'credentials':{'admin':dict(zip(('username','password'),admin)),'ordinary':dict(zip(('username','password'),ordinary))}}
        environment={**os.environ};environment.pop('VALIDATION_USERNAME',None);environment.pop('VALIDATION_PASSWORD',None)
        with (p/'auth-only.stdout').open('xb') as out,(p/'auth-only.stderr').open('xb') as err:
            subprocess.run(['node',str(repo/'deploy/validation/validate_recursive_acl_auth_only.mjs')],input=json.dumps(packet).encode(),stdout=out,stderr=err,env=environment,timeout=150,check=True)
    assert snapshot(r['id'])==before and common.core_snapshot()==core;no_active_work()
    result=private_json(p/'auth-only.json');assert len(result['roles'])==2 and all(v['logout']['normalUiLogout'] and v['anonymousGet401']==4 for v in result['roles'])
    proof={'validationRevision':revision,'validationSources':hashes,'originalCancelGatePassed':False,'newIdentityContextsOnly':True,'permanentCancelSqlUnchanged':True,'fileMutations':0,'newIntents':0,'coreUnchanged':11}
    (p/'auth-only-verified.json').write_text(json.dumps(proof,indent=2)+'\n');print(json.dumps({'recursiveAclAuthOnly':'passed','normalDualLogoutContexts':2,'fileMutations':0,'originalCancelGatePassed':False}))

if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps({'recursiveAclAuthOnly':'failed','failureType':type(error).__name__}));raise SystemExit(1) from None
