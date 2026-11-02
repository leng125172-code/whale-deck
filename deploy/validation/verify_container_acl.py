#!/usr/bin/env python3
"""Second read-only check of retained candidate ACL Root/HDD evidence.

Source bytes were fully read before exact cleanup; deleted bytes are not read
again. Candidate journal and mount attestation remain real, not RPC/UI proof.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_acl import NAMES, PATHS, unchanged_except_target
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    assert re.fullmatch('container-acl-[a-f0-9]{32}',sys.argv[1])
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/sys.argv[1]
    info=evidence.lstat();assert stat.S_ISDIR(info.st_mode) and info.st_uid==0 and stat.S_IMODE(info.st_mode)==0o700 and not evidence.is_symlink()
    def saved(name):
        p=evidence/name;st=p.lstat();assert stat.S_ISREG(st.st_mode) and st.st_uid==0 and st.st_nlink==1 and stat.S_IMODE(st.st_mode)==0o600 and st.st_size<=16*1024**2
        return json.loads(p.read_bytes())
    baseline=saved('baseline.json');rows=saved('results.json');verified=saved('verified.json');assert len(rows)==verified['operations']==9 and verified['candidateOnly'] and not verified['signedRpcOrHumanUi']
    frozen={n:subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+n]) for n in NAMES}
    assert {n:hashlib.sha256(raw).hexdigest() for n,raw in frozen.items()}==baseline['sourceSha256']
    assert [r['label'] for r in rows]==['access','noop','mask','base','default','clear-default','cancel','before-syscall','after-syscall']
    previous=baseline['original'];hdd_count=0
    with tempfile.TemporaryDirectory(prefix='wd-acl-readback-') as tmp:
        bundle=Path(tmp)
        for n,raw in frozen.items():p=bundle/n;p.write_bytes(raw);p.chmod(0o750)
        reader=load(bundle/NAMES[1],'acl_verify_reader');files=reader.trusted_module(NAMES[0],'acl_verify_files')
        storage=load(bundle/NAMES[5],'acl_verify_storage').FileBackups(files);permissions=load(bundle/NAMES[7],'acl_verify_permission');module=load(bundle/NAMES[8],'acl_verify_module')
        history=module.acl_history_type(permissions)(storage)
        for row in rows:
            request=row['request'];plan=request['plan'];operation=request['operationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
            assert row['before']==previous and set(row['after'])==PATHS.keys();previous=row['after']
            path=evidence/'root-state'/(operation+'.json');info=path.lstat();assert stat.S_ISREG(info.st_mode) and info.st_uid==0 and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600
            raw=path.read_bytes();assert hashlib.sha256(raw).hexdigest()==row['rootSha256'];record=json.loads(raw)
            assert record['operationId']==operation and record['owner']==request['owner'] and record['plan']==plan
            assert plan['fingerprint']==hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+module.encode({k:v for k,v in plan.items() if k!='fingerprint'})).hexdigest().upper()
            command=plan['command'];assert command['containerId']==baseline['containerId'] and command['instanceId']==baseline['instanceId']
            relative=command['path'].removeprefix('work/');assert relative in PATHS and command['aclType'] in ('access','default')
            module.validate_entries(command['entries'],empty=command['aclType']=='default');module.validate_entries(plan['accessAcl']);module.validate_entries(plan['defaultAcl'],empty=True)
            assert plan['sourceIdentity']==row['before'][relative]['identity'] and plan['accessAcl']==row['before'][relative]['access'] and plan['defaultAcl']==row['before'][relative]['default']
            expected='Aborted' if row['label'] in ('cancel','before-syscall') else 'Completed';assert record['state']==row['result']['state']==expected
            unchanged_except_target(row['before'],row['after'],relative,plan['targetMode'] if expected=='Completed' else row['before'][relative]['identity'][2]&0o777)
            if expected=='Completed':
                assert record['actualIdentity']==row['after'][relative]['identity'] and record['aclConfirmed'] is True
                assert row['after'][relative][command['aclType']]==command['entries']
                assert row['after'][relative]['default' if command['aclType']=='access' else 'access']==row['before'][relative]['default' if command['aclType']=='access' else 'access']
                assert record['version']==reader.version(command,command['path'],type('Stat',(),dict(zip(('st_dev','st_ino','st_mode','st_uid','st_gid','st_nlink','st_size','st_mtime_ns','st_ctime_ns'),record['actualIdentity'])))())
            if row['label'] in ('noop','cancel','before-syscall'):assert row['before']==row['after']
            assert record['historyVerified']==row['history']
            if record['historyVerified']:
                with history.target(False) as (_,fd):
                    history.read(fd,request);actual=storage.read_json(fd,operation+'.json')
                assert actual==saved(operation+'.hdd.json') and actual['historyDigest']==record['historyDigest'];hdd_count+=1
            else:assert row['label']=='cancel' and not row['history']
        with storage.target(create_namespace=False,read_only=True) as (_,fd):hdd_device=os.fstat(fd).st_dev
    assert hdd_count==verified['hddMetadata']==8 and saved('physical-final.json')==previous
    assert not Path(baseline['source']).exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15).returncode!=0
    assert {k:list(v) for k,v in owned.common.core_snapshot().items()}==baseline['coreBefore']
    containers=json.loads(owned.common.command(['docker','inspect',*owned.common.CORE_NAMES]));assert len(containers)==11 and all(c['State']['Health']['Status']=='healthy' for c in containers)
    assert host_identity('b1fafedddfa1767d2d573bd94d2748f3cb5e7b44')==baseline['hostBefore'];no_active_work()
    proof={'independentAclVerification':'passed','operations':9,'completed':7,'aborted':2,'hddMetadata':hdd_count,'exactSourceAndContainerRemoved':True,
           'all11HealthyUnchanged':True,'hostPidsUnchanged':True,'deletedSourceBytesReRead':False,'candidateOnly':True,'sourceDevice':baseline['original']['acl-file.bin']['identity'][0],'hddDevice':hdd_device}
    (evidence/'independent-final.json').write_text(json.dumps(proof));print(json.dumps(proof))


if __name__=='__main__':main()
