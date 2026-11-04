#!/usr/bin/env python3
"""Prove and clean the exact preserved source after a failed verifier, no replay."""
import hashlib,json,os,re,stat,subprocess,sys
from pathlib import Path
from validate_recursive_acl_restore_kernel import bundle,SOURCES
from verify_container_recursive_acl import private_json
from validate_container_recursive_acl import inventory,remove_proved
from validate_container_file_publishing import common
from validate_container_recursive_acl_faults import preservation_snapshot
from acl_source_apply_missing_proof import source_closure

def recover(name):
    assert re.fullmatch('recursive-acl-restore-kernel-[a-f0-9]{32}',name)
    repo=Path(__file__).resolve().parents[2];evidence=repo/'deploy/artifacts'/name;b=private_json(evidence/'baseline.json');assert (evidence/'failure.json').exists() and not any((evidence/n).exists() for n in ('verified.json','recovered-readonly.json'))
    assert not subprocess.check_output(['git','status','--porcelain']).strip(),'FREEZE_RESTORE_RECOVERY_FIRST'
    revision=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,revision,seeds=('recover_recursive_acl_restore_kernel.py',*SOURCES));assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in hashes.items())
    with (evidence/'preserved-recheck.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_kernel.py'),name,'--preserved'],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
    source=Path(b['source']);root=Path(b['root']);assert source.parent==common.APPS and source.name==b['containerName'] and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',source.name) and source.resolve(strict=True)==source and root==source/'recursive-tree' and {p.name for p in source.iterdir()}=={'original.bin','recursive-tree'}
    original=private_json(evidence/'sources.json')[0]['request']['plan'];parent=source.lstat();assert [parent.st_dev,parent.st_ino]==original['parentIdentity'] and parent.st_uid==0 and stat.S_IMODE(parent.st_mode)==0o755
    leaf=source/'original.bin';info=leaf.lstat();identity=common.fingerprint(leaf);expected=hashlib.shake_256(('owned-container-original:'+source.name).encode()).digest(common.SIZE);assert stat.S_ISREG(info.st_mode) and info.st_nlink==1 and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(1000,1000,0o640) and leaf.read_bytes()==expected
    with bundle(repo,b['revision'],evidence) as p:
        data=hashlib.shake_256(('restore-kernel:'+b['containerId']).encode()).digest(b['bytesPerFile']);physical=private_json(evidence/'physical-final.json');assert inventory(root,p.reader,data,p.acl)==physical;remove_proved(root,p.reader,data,physical,p.acl)
        assert common.fingerprint(leaf)==identity and leaf.read_bytes()==expected and {x.name for x in source.iterdir()}=={'original.bin'};leaf.unlink();assert not list(source.iterdir()) and (source.stat().st_dev,source.stat().st_ino)==(parent.st_dev,parent.st_ino);source.rmdir()
        protected=preservation_snapshot(p.storage);assert all(protected.get(n)==v for n,v in b['protectedFiles'].items());(evidence/'final-protected.json').write_text(json.dumps(protected))
    result=dict(originalWholeGatePassed=False,readonlyPhysicalAndHddRecheck=True,noRestoreOrSourceReplayed=True,exactSourceAndContainerAbsent=True,recoveryRevision=revision,recoverySourceSha256=hashes)
    with (evidence/'recovered-readonly.json').open('x') as stream:json.dump(result,stream)
    with (evidence/'recovered-final.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_restore_kernel.py'),name,'--recovered-final'],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
    return result

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';print(json.dumps(recover(sys.argv[1])))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(restoreKernelRecovery='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
