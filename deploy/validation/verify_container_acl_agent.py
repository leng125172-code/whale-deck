#!/usr/bin/env python3
"""Read-only second observer of installed signed ACL gate Root/HDD/proofs.

Deleted bytes are not re-read. Every live byte/ACL comparison occurred before
exact cleanup in the retained signed probe transcript and physical snapshots.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
from types import SimpleNamespace

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted
from validate_container_acl_agent import STATE, PATHS, NAMES, number
from validate_container_acl import unchanged_except_target
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity


def main():
    assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
    assert re.fullmatch('container-acl-agent-[a-f0-9]{32}',sys.argv[1]);repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/sys.argv[1]
    assert stat.S_IMODE(trusted(evidence,True).st_mode)==0o700
    def saved(name):
        p=evidence/name;info=trusted(p);assert stat.S_IMODE(info.st_mode)==0o600 and info.st_size<=16*1024**2;return json.loads(p.read_bytes())
    baseline=saved('baseline.json');verified=saved('verified.json');rows=saved('results.json');assert verified['installedAclRpc']=='passed' and not verified['humanBffOrUi']
    assert len(rows)==verified['operations']==9 and [r['label'] for r in rows]==['access','noop','mask','root','base','default','default-noop','clear-default','cancel']
    names=(*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged');assert set(baseline['installedSourceSha256'])==set(names)
    for name,sha in baseline['installedSourceSha256'].items():
        path=Path('/usr/local/libexec')/name;assert stat.S_IMODE(trusted(path).st_mode)==0o750
        raw=subprocess.check_output(['git','-C',str(repo),'show',baseline['revision']+':deploy/host/'+name]);assert raw==path.read_bytes() and hashlib.sha256(raw).hexdigest()==sha
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_rpc_verify_reader');files=reader.trusted_module('whaledeck-files.py','acl_rpc_verify_files')
    storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_rpc_verify_storage').FileBackups(files);module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_rpc_verify_module')
    previous=baseline['original'];count=0
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        hdd_device=os.fstat(base).st_dev;fd=storage.f.open_at(base,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            for row in rows:
                request=row['request'];plan=request['plan'];operation=request['operationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
                path=STATE/(operation+'.json');assert stat.S_IMODE(trusted(path).st_mode)==0o600;raw=path.read_bytes();record=json.loads(raw)
                assert raw==(evidence/(operation+'.root.json')).read_bytes() and hashlib.sha256(raw).hexdigest()==row['rootSha256']
                assert record['plan']==plan and record['owner']==request['owner']=='B'*64 and record['operationId']==operation
                command=plan['command'];relative=command['path'].removeprefix('work/');assert relative in PATHS
                assert command['containerId']==baseline['containerId'] and command['instanceId']==baseline['instanceId'] and plan['uid']==plan['gid']==1000
                assert row['before']==previous and plan['sourceIdentity']==previous[relative]['identity'] and plan['accessAcl']==previous[relative]['access'] and plan['defaultAcl']==previous[relative]['default'];previous=row['after']
                assert plan['fingerprint']==hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+module.encode({k:v for k,v in plan.items() if k!='fingerprint'})).hexdigest().upper()
                module.validate_entries(command['entries'],empty=command['aclType']=='default');module.validate_entries(plan['accessAcl']);module.validate_entries(plan['defaultAcl'],empty=True)
                expected='Aborted' if row['label']=='cancel' else 'Completed';result=row['result']
                assert record['state']==result['state']==expected and record['aclConfirmed']==result['aclConfirmed']==(expected=='Completed')
                assert result['operationId']==operation and result['containerId']==baseline['containerId'] and result['instanceId']==baseline['instanceId'] and result['fingerprint']==plan['fingerprint']
                assert result['aclType']==command['aclType'] and number(result['targetMode'])==plan['targetMode'] and result['targetProof']==plan['targetProof'] and result['kind']==plan['kind']
                mode=plan['targetMode'] if expected=='Completed' else row['before'][relative]['identity'][2]&0o777
                unchanged_except_target(row['before'],row['after'],relative,mode)
                if expected=='Completed':
                    assert record['actualIdentity']==row['after'][relative]['identity'] and row['after'][relative][command['aclType']]==command['entries'] and result['bindingCurrent']
                    other='default' if command['aclType']=='access' else 'access';assert row['after'][relative][other]==row['before'][relative][other]
                    info=SimpleNamespace(**dict(zip(('st_dev','st_ino','st_mode','st_uid','st_gid','st_nlink','st_size','st_mtime_ns','st_ctime_ns'),record['actualIdentity'])))
                    assert result['version']==record['version']==reader.version(command,command['path'],info)
                    actual=storage.read_json(fd,operation+'.json');assert actual==saved(operation+'.hdd.json')
                    assert actual['resourceType']=='ContainerAclMetadata' and actual['storageId']=='database-platform.backup.hdd' and actual['plan']==plan and actual['owner']==record['owner']
                    assert actual['historyDigest']==record['historyDigest']==result['historyDigest']==hashlib.sha256(b'whaledeck-container-acl-history-v1\n'+module.encode({k:v for k,v in actual.items() if k!='historyDigest'})).hexdigest().upper();count+=1
                else:
                    assert not record['historyMayExist'] and not record['historyVerified'] and record['actualIdentity'] is None and result['version']==record['version']==''
                    assert not (evidence/(operation+'.hdd.json')).exists() and not (STATE/(operation+'.entries')).exists()
                    try:os.stat(operation+'.json',dir_fd=fd,follow_symlinks=False)
                    except FileNotFoundError:pass
                    else:raise AssertionError('CANCELED_HISTORY_EXISTS')
                if row['label'] in ('noop','default-noop','cancel'):assert row['before']==row['after']
        finally:os.close(fd)
    assert count==verified['hddMetadata']==8 and saved('physical-final.json')==previous
    late=saved('late-signed-rpc.json');assert late['operationId']==rows[0]['request']['operationId'] and late['state']=='Completed' and late['replayed'] and not late['bindingCurrent']
    assert late['version']==rows[0]['result']['version'] and late['historyDigest']==rows[0]['result']['historyDigest']
    assert not Path(baseline['source']).exists() and subprocess.run(['docker','inspect',baseline['containerId']],capture_output=True,timeout=15).returncode!=0
    assert {k:list(v) for k,v in owned.common.core_snapshot().items()}==baseline['coreBefore'] and host_identity(baseline['revision'])==baseline['hostBefore']
    containers=json.loads(owned.common.command(['docker','inspect',*owned.common.CORE_NAMES]));assert len(containers)==11 and all(c['State']['Health']['Status']=='healthy' for c in containers)
    no_active_work();units=subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-acl-*'],text=True,timeout=10);assert not units.strip()
    observed={'independentInstalledAclVerification':'passed','operations':9,'completed':8,'aborted':1,'hddMetadata':count,'all11HealthyUnchanged':True,
              'hostsUnchanged':True,'exactSourceAndContainerRemoved':True,'deletedSourceBytesReRead':False,'sourceDevice':baseline['original']['acl-file.bin']['identity'][0],
              'hddDevice':hdd_device,'lateReplayWasSignedRpc':True,'humanBffOrUi':False}
    (evidence/'independent-final.json').write_text(json.dumps(observed));print(json.dumps(observed))


if __name__=='__main__':main()
