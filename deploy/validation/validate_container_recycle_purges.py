#!/usr/bin/env python3
"""Frozen Originals purge on one owned container and actual configured HDD.

No installation, RPC, permanent business workflow or scheduling.
Only this newly owned retained file may be purged; HDD history stays.
"""
import hashlib,json,os,re,subprocess,sys,uuid
from pathlib import Path
from validate_container_file_staging import load
import validate_container_file_publishing as owned
from validate_container_recycle_restore_agent import baseline
from validate_container_recycle_restore_browser import idle,old_rows
from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot
from recursive_acl_restore_agent_io import snapshot

NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py','whaledeck-container-recycle-restores.py','whaledeck-container-recycle-purge-plan.py','whaledeck-container-recycle-purge-fences.py','whaledeck-container-recycle-purges.py')


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();idle();before=baseline(revision);rows=old_rows('0'*64)
    e=repo/'deploy/artifacts'/('container-recycle-purges-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('business-before.json',rows);bundle=e/'source';bundle.mkdir(mode=0o700);hashes={}
    for n in NAMES:
        raw=git('show',sha+':deploy/host/'+n);assert raw==(repo/'deploy/host'/n).read_bytes();(bundle/n).write_bytes(raw);(bundle/n).chmod(0o750);hashes[n]=hashlib.sha256(raw).hexdigest()
    save('source.json',dict(revision=revision,validationRevision=sha,sourceSha256=hashes));print(json.dumps(dict(purgeKernel='started',evidence=e.name)),flush=True)
    reader=load(bundle/NAMES[1],'purge_preflight_reader');files=reader.trusted_module(NAMES[0],'purge_preflight_files');docker=reader.trusted_module(NAMES[2],'purge_preflight_docker')
    state=e/'source-state';state.mkdir(mode=0o700);restore_state=e/'restore-state';restore_state.mkdir(mode=0o700);(restore_state/'claims').mkdir(mode=0o700)
    upload=load(bundle/NAMES[3],'purge_preflight_upload').ContainerUploads(reader,files,state)
    publisher=load(bundle/NAMES[4],'purge_preflight_publisher').ContainerPublisher(upload,type('UnusedCapture',(),{'u':upload})())
    storage=load(bundle/NAMES[5],'purge_preflight_storage').FileBackups(files)
    directory=load(bundle/NAMES[6],'purge_preflight_directory').ContainerDirectories;base=load(bundle/NAMES[7],'purge_preflight_history').ContainerPermissionHistory
    recycle=load(bundle/NAMES[8],'purge_preflight_recycle');history=recycle.history_type(base)(storage);source=recycle.recycling_type(directory)(upload,publisher,state,history)
    restore=load(bundle/NAMES[9],'purge_preflight_restore').restores_type(directory)(source,restore_state)
    planner=load(bundle/NAMES[10],'purge_preflight_candidate').ContainerRecyclePurgePreflight(restore)
    purge_state=e/'purge-state';purge_state.mkdir(mode=0o700)
    fence_type=load(bundle/NAMES[11],'purge_fences_candidate').purge_fences_type(directory)
    purger=load(bundle/NAMES[12],'purge_kernel_candidate').purges_type(fence_type)(planner,purge_state)
    with owned.fixture() as f:
        f['retain_unsettled']=True;leaf=f['folder']/'purge-kernel.bin';data=hashlib.shake_256(('purge-kernel:'+f['identifier']).encode()).digest(1024**2+7);leaf.write_bytes(data);os.chown(leaf,1000,1000);leaf.chmod(0o640);os.setxattr(leaf,'user.retention',b'preflight keeps both copies')
        initial=file_snapshot(leaf);c=json.loads(owned.common.command(['docker','inspect',f['identifier']]))[0]
        command=dict(containerId=f['identifier'],instanceId=str(c['State']['Pid'])+':'+c['State']['StartedAt'],path='work/'+leaf.name)
        with reader.ContainerRoot(command,files,docker) as session:
            ref=dict(operationId=uuid.uuid4().hex,owner=hashlib.sha256(f['identifier'].encode()).hexdigest().upper(),plan=source.plan({**command,'expectedVersion':reader.version(command,command['path'],leaf.stat())},session))
            captured=source.apply(ref,session);assert captured['state']=='Trashed' and captured['bindingCurrent'] and captured['historyVerified']
            retained=f['folder']/source.PRIVATE/(ref['operationId']+'.trash');retained_before=file_snapshot(retained);assert retained_before['sha256']==initial['sha256']
            native_before=snapshot(state);claims_before=snapshot(restore_state)
            with history.target(False) as (_,fd):hdd_folder=Path(os.readlink('/proc/self/fd/'+str(fd)));hdd_before={str(p.relative_to(hdd_folder)):dict(identity=source.metadata(p.stat()),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in hdd_folder.iterdir() if p.is_file()}
            request=dict(source=ref,historyDigest=captured['historyDigest'],scope='Originals');plan=planner.plan(request,session);typed_equal(planner.revalidate(plan,session),plan)
            typed_equal(file_snapshot(retained),retained_before);typed_equal(snapshot(state),native_before);typed_equal(snapshot(restore_state),claims_before)
            with history.target(False) as (_,fd):
                current={str(p.relative_to(hdd_folder)):dict(identity=source.metadata(p.stat()),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in hdd_folder.iterdir() if p.is_file()};typed_equal(current,hdd_before)
            reservation=dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=plan)
            archive_before=file_snapshot(hdd_folder/(ref['operationId']+'.data'))
            metadata_before=json.loads((hdd_folder/(ref['operationId']+'.json')).read_text())
            source_before=(state/(ref['operationId']+'.json')).read_bytes()
            reserved=purger.reserve(reservation,session);assert reserved['state']=='Reserved' and reserved['ownedFence'] and not reserved['purgeApplied']
            typed_equal(file_snapshot(retained),retained_before);typed_equal(file_snapshot(hdd_folder/(ref['operationId']+'.data')),archive_before)
            assert (state/(ref['operationId']+'.json')).read_bytes()==source_before
            pending=retained.parent/(reservation['operationId']+'.purging')
            assert not leaf.exists() and not pending.exists() and retained.name==ref['operationId']+'.trash'
            result=purger.apply(reservation,session);assert result['state']=='Completed' and result['purgeApplied'] and result['ownedFence']
            assert not leaf.exists() and not retained.exists() and not pending.exists()
            typed_equal(file_snapshot(hdd_folder/(ref['operationId']+'.data')),archive_before)
            metadata_after=json.loads((hdd_folder/(ref['operationId']+'.json')).read_text());typed_equal(metadata_after,{**metadata_before,'captureState':'Purged'})
            source_record=json.loads((state/(ref['operationId']+'.json')).read_text());assert source_record['state']=='Purged' and source_record['actualIdentity'] is None
            marker=json.loads((state/(ref['operationId']+'.purge.json')).read_text());assert marker['state']=='Purged'
            own=json.loads((purge_state/(reservation['operationId']+'.json')).read_text());assert own['state']=='Completed' and own['unlinkConfirmed']
            for observe in (lambda:purger.apply(reservation,session),lambda:purger.inspect(reservation),lambda:purger.discard(reservation,session)):
                typed_equal(observe(),result)
            try:source.restore(ref,session);raise AssertionError('PURGED_SOURCE_RESTORED')
            except reader.Rejected as error:assert str(error).endswith('RESTORE_UNPROVEN')
            save('purged.json',dict(request=reservation,reserved=reserved,receipt=result,source=source_record,marker=marker,journal=own,original=initial,retainedBefore=retained_before,archiveBefore=archive_before,archiveAfter=file_snapshot(hdd_folder/(ref['operationId']+'.data')),metadataBefore=metadata_before,metadataAfter=metadata_after,onlyOwnedRetainedFilePurged=True))
        op=ref['operationId'];private=f['folder']/source.PRIVATE
        assert not list(private.iterdir()) and [private.stat().st_dev,private.stat().st_ino]==source_record['privateIdentity'] and not private.is_symlink() and not leaf.exists()
        private.rmdir();f['retain_unsettled']=False
    after=baseline(revision);expected={**before,'recycleHdd':after['recycleHdd']};typed_equal(expected,after)
    assert all(after['recycleHdd'].get(k)==v for k,v in before['recycleHdd'].items()) and set(after['recycleHdd'])-set(before['recycleHdd'])=={op+'.json',op+'.data'}
    typed_equal(old_rows('0'*64),rows);idle();save('after.json',after)
    save('verified.json',dict(actualContainer=True,actualConfiguredHdd=True,onlyOwnedRetainedFilePurged=True,archivePreserved=True,sourceRestoredForQaCleanup=False,ownedFixtureRemoved=True,oldRootBusinessAndProtectedPreserved=True,hddHistoryRetained=True,installed=False,signedRpc=False,businessUi=False,purgeApplied=True,automaticRetention=False,differentSourceDevice=plan['archiveIdentity'][0]!=plan['retainedIdentity'][0]))
    print(json.dumps(dict(purgeKernel='passed',evidence=e.name,installed=False,purgeApplied=True)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(purgeKernel='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
