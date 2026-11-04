#!/usr/bin/env python3
"""Read-only failed capture-chain proof followed by exact owned-source cleanup.

Never recreate a deleted container, change its history binding, replay capture,
or upgrade the failed browser gate to Passed.
"""
import hashlib,json,os,re,stat,subprocess,sys
from pathlib import Path
from validate_container_file_staging import load
from validate_container_file_publishing import common
from verify_container_recursive_acl import private_json
from validate_recursive_acl_history_inventory import SOURCES,content,unchanged
from recursive_acl_inventory_proof import verify_chain
from validate_container_recursive_acl import inventory,remove_proved
from validate_metric_continuity import query
from acl_source_apply_missing_proof import source_closure

def recover(name):
    assert re.fullmatch('recursive-acl-history-inventory-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name
    from validate_container_upload_recovery_agent import trusted
    trusted(evidence,True);b=private_json(evidence/'baseline.json');rows=private_json(evidence/'captures.json');failure=private_json(evidence/'failure.json')
    assert failure['phase']=='actual-ui-first' and failure['verifiedCaptures']==26 and not any((evidence/n).exists() for n in ('verified.json','browser.json','recovered-failed-source.json'))
    assert b['validationSourceSha256']==source_closure(repo,b['validationSourceRevision'],seeds=SOURCES)
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_RECOVERY_FIRST'
    revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,revision,seeds=('recover_recursive_acl_history_inventory.py',*SOURCES));assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in hashes.items())
    for name,sha in b['sourceHashes'].items():assert hashlib.sha256((Path('/usr/local/libexec')/name).read_bytes()).hexdigest()==sha
    missing=subprocess.run(['docker','inspect',b['containerId']],capture_output=True,timeout=15);assert missing.returncode!=0 and b'no such' in missing.stderr.lower()
    source=Path(b['source']);root=Path(b['root']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name)
    assert source.resolve(strict=True)==source and root==source/'recursive-tree' and {p.name for p in source.iterdir()}=={'original.bin','recursive-tree'}
    parent=source.lstat();assert parent.st_uid==0 and stat.S_IMODE(parent.st_mode)==0o755 and [parent.st_dev,parent.st_ino]==rows[0]['request']['plan']['parentIdentity']
    r=load('/usr/local/libexec/whaledeck-container-files.py','inventory_recover_reader');f=r.trusted_module('whaledeck-files.py','inventory_recover_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','inventory_recover_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','inventory_recover_hdd').FileBackups(f)
    data=content(b['containerId']);assert len(data)==b['bytesPerFile'] and hashlib.sha256(data).hexdigest().upper()==b['sha256'];physical=inventory(root,r,data,acl);assert physical==rows[-1]['after'];metadata=verify_chain(evidence,b,rows,storage);protected=unchanged(evidence,b,storage)
    audits=query("SELECT COALESCE(json_agg(to_jsonb(a)),'[]') FROM audit_events a WHERE target_id=:'id' AND action IN('containers.files.recursive-acl-history-list','containers.files.recursive-acl-history-read');",id=b['containerId']);assert len(audits)==1 and audits[0]['action']=='containers.files.recursive-acl-history-list'
    detail=audits[0]['detail_json'];detail=json.loads(detail) if type(detail) is str else detail;assert detail['take']==25 and detail['returnedCount']==22 and detail['complete'] is False and detail['scannedCount']==79
    (evidence/'failed-first-page-audit.json').write_text(json.dumps(audits));(evidence/'recovery-physical.json').write_text(json.dumps(physical))
    leaf=source/'original.bin';info=leaf.lstat();identity=common.fingerprint(leaf);expected=hashlib.shake_256(('owned-container-original:'+source.name).encode()).digest(common.SIZE)
    assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(1000,1000,0o640) and leaf.read_bytes()==expected
    remove_proved(root,r,data,physical,acl);assert common.fingerprint(leaf)==identity and leaf.read_bytes()==expected and {p.name for p in source.iterdir()}=={'original.bin'}
    leaf.unlink();assert not list(source.iterdir()) and (source.stat().st_dev,source.stat().st_ino)==(parent.st_dev,parent.st_ino);source.rmdir();assert unchanged(evidence,b,storage)==protected
    result=dict(originalWholeGatePassed=False,originalUiAccepted=False,readonlyCaptureProofs=26,aggregateHdd=26,childHdd=104,originalHistoryRetained=True,noCaptureReplayed=True,exactSourceAndContainerAbsent=True,validationSourceRevision=revision,validationSourceSha256=hashes,protectedFiles=len(protected),firstPageReturnedCount=22,firstPageScannedCount=79,reason='soft deadline permits a progressing non-full page')
    with (evidence/'recovered-failed-source.json').open('x') as stream:json.dump(result,stream)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';print(json.dumps(recover(sys.argv[1])))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(inventorySourceRecovery='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
