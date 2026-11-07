#!/usr/bin/env python3
"""Reserved archive copy to a new inode on an owned container.

No installation, RPC, permanent business workflow or scheduling.
Only this newly owned retained file may be purged; copy publishes only the selected new inode; HDD history stays.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from validate_container_file_staging import load
import validate_container_file_publishing as owned
from validate_container_recycle_archive_agent import baseline
from validate_container_recycle_archive_browser import idle,old_rows
from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot
from recursive_acl_restore_agent_io import snapshot,frame
from validate_container_recycle_archive_plan import payload_snapshot

NAMES=('whaledeck-files.py','whaledeck-container-files.py','whaledeck-container-terminal.py','whaledeck-container-upload.py','whaledeck-container-publish.py','whaledeck-file-backups.py','whaledeck-container-directories.py','whaledeck-container-permissions.py','whaledeck-container-recycling.py','whaledeck-container-recycle-restores.py','whaledeck-container-recycle-purge-plan.py','whaledeck-container-recycle-purge-fences.py','whaledeck-container-recycle-purges.py','whaledeck-container-recycle-archive-plan.py','whaledeck-container-recycle-archive-fences.py','whaledeck-container-recycle-archive-copies.py')
NAMES+=('whaledeck-container-recycle-archive-retention.py',)
NAMES+=('whaledeck-container-recycle-archive-retirement-fences.py',)
NAMES+=('whaledeck-container-recycle-retired-history.py',)


def retention_namespace_snapshot(folder):
    # Payload files belong to the container user and contain arbitrary binary
    # bytes. The Root JSON-journal reader is deliberately not used here.
    assert folder.is_absolute() and folder.resolve(strict=True)==folder
    paths=sorted(folder.iterdir());assert len(paths)<=4096;values={}
    for path in paths:
        assert not path.is_symlink()
        if path.is_dir():
            info=path.stat();assert (info.st_uid,info.st_gid,info.st_mode&0o777)==(0,0,0o700) and not list(path.iterdir())
            values[path.name]=dict(kind='directory',identity=frame(info))
        else:values[path.name]=dict(kind='file',**file_snapshot(path))
    return values


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    os.environ.update(GIT_CONFIG_COUNT='1',GIT_CONFIG_KEY_0='safe.directory',GIT_CONFIG_VALUE_0=str(repo))
    git=lambda *a:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*a])
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').decode().strip();idle();before=baseline(revision);rows=old_rows('0'*64)
    e=repo/'deploy/artifacts'/('container-recycle-archive-copies-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    save('before.json',before);save('business-before.json',rows);bundle=e/'source';bundle.mkdir(mode=0o700);hashes={}
    for n in NAMES:
        raw=git('show',sha+':deploy/host/'+n);assert raw==(repo/'deploy/host'/n).read_bytes();(bundle/n).write_bytes(raw);(bundle/n).chmod(0o600);hashes[n]=hashlib.sha256(raw).hexdigest()
    from acl_source_apply_missing_proof import source_closure
    closure=source_closure(repo,sha,seeds=('validate_container_recycle_archive_copies.py','verify_container_recycle_archive_copies.py','test_container_recycle_archive_copies.py','test_container_recycle_archive_retention.py','test_container_recycle_archive_retention_plan.py','test_container_recycle_archive_retirement_fences.py','test_container_recycle_retired_history.py'))
    validation=e/'validation';validation.mkdir(mode=0o700)
    for name,digest in closure.items():
        raw=git('show',sha+':deploy/validation/'+name);assert hashlib.sha256(raw).hexdigest()==digest and raw==(repo/'deploy/validation'/name).read_bytes();(validation/name).write_bytes(raw)
    save('source.json',dict(revision=revision,validationRevision=sha,sourceSha256=hashes,validationSourceSha256=closure));print(json.dumps(dict(archiveCopies='started',evidence=e.name)),flush=True)
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
        save('fixture-initial.json',dict(containerId=f['identifier'],containerName=c['Name'].lstrip('/'),folder=str(f['folder']),parentIdentity=[f['folder'].stat().st_dev,f['folder'].stat().st_ino],original=file_snapshot(f['folder']/'original.bin'),selected=initial))
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
            archive=load(bundle/NAMES[13],'archive_preflight_candidate').ContainerRecycleArchivePreflight(purger)
            archive_command=dict(purge=reservation,historyDigest=captured['historyDigest'],mode='ArchiveCopy')
            def unchanged():return dict(source=snapshot(state),restore=snapshot(restore_state),purge=snapshot(purge_state),hdd=payload_snapshot(hdd_folder))
            preflight_before=unchanged();archive_plan=archive.plan(archive_command,session);typed_equal(archive.revalidate(archive_plan,session),archive_plan);typed_equal(unchanged(),preflight_before)
            assert archive_plan['deletedIdentity']==retained_before['identity'] and archive_plan['archiveSha256']==initial['sha256'] and archive_plan['archiveIdentity']==archive_before['identity']
            assert not leaf.exists() and not retained.exists() and not pending.exists()
            save('archive-preflight.json',dict(command=archive_command,plan=archive_plan,before=preflight_before,after=unchanged(),originalAbsent=True,archiveCopyPerformed=False))
            private=retained.parent;assert not list(private.iterdir()) and [private.stat().st_dev,private.stat().st_ino]==source_record['privateIdentity'];private.rmdir()
            no_private_before=unchanged();absent_plan=archive.plan(archive_command,session);assert absent_plan['privateIdentity'] is None
            try:archive.revalidate(archive_plan,session);raise AssertionError('OLD_NAMESPACE_PLAN_ACCEPTED')
            except reader.Rejected as error:assert str(error).endswith('PLAN_CHANGED')
            typed_equal(archive.revalidate(absent_plan,session),absent_plan);typed_equal(unchanged(),no_private_before)
            save('archive-after-private-cleanup.json',dict(plan=absent_plan,before=no_private_before,after=unchanged(),oldPlanRejected=True))
            archive_state=e/'archive-state';archive_state.mkdir(mode=0o700)
            archive_fence=load(bundle/NAMES[14],'archive_fences_base').archive_fences_type(directory)
            engine=load(bundle/NAMES[15],'archive_copies_candidate').archive_copies_type(archive_fence)(archive,archive_state)
            canceled_request=dict(operationId=uuid.uuid4().hex,owner='E'*64,plan=absent_plan)
            assert engine.reserve(canceled_request,session)['state']=='Reserved';canceled=engine.discard(canceled_request,session);assert canceled['state']=='Aborted' and canceled['archiveCopied'] is False
            copy_request=dict(operationId=uuid.uuid4().hex,owner='F'*64,plan=absent_plan);save('copy-request.json',copy_request)
            stable=dict(source=snapshot(state),purge=snapshot(purge_state),hdd=payload_snapshot(hdd_folder))
            reserved_copy=engine.reserve(copy_request,session);assert reserved_copy['state']=='Reserved' and reserved_copy['ownedFence'] and reserved_copy['archiveCopied'] is False
            copied=engine.apply(copy_request,session);assert copied['state']=='Completed' and copied['archiveCopied'] is True and copied['bindingCurrent'] is True
            actual=file_snapshot(leaf);assert actual['identity'][:2]!=initial['identity'][:2] and actual['identity'][:2]!=archive_before['identity'][:2]
            typed_equal(actual['identity'][2:8],initial['identity'][2:8])
            for key in ('sha256','attributes','sizeBytes'):typed_equal(actual[key],initial[key])
            own_copy=json.loads((archive_state/(copy_request['operationId']+'.json')).read_text());assert own_copy['state']=='Completed';typed_equal(own_copy['publishedIdentity'],actual['identity'])
            stage=f['folder']/engine.stage_name(copy_request);stage_info=stage.lstat();assert [stage_info.st_dev,stage_info.st_ino]==own_copy['privateIdentity'] and not stage.is_symlink() and not list(stage.iterdir())
            for action in (engine.apply,engine.inspect,engine.discard):
                replay=action(copy_request,session);assert replay['state']=='Completed' and replay['archiveCopied'] is True and replay['bindingCurrent'] is True;typed_equal(file_snapshot(leaf),actual)
            typed_equal(stable,dict(source=snapshot(state),purge=snapshot(purge_state),hdd=payload_snapshot(hdd_folder)))
            claim=source.read_restore_claim(ref);typed_equal(claim,engine.claim(copy_request))
            def retention_check(label):
                # A new rejected ID may acquire its own empty .lock. It must
                # not create a journal or replace/release the completed claim.
                prior=dict(claims=snapshot(restore_state),root=(archive_state/(copy_request['operationId']+'.json')).read_bytes(),protected=unchanged())
                competitor={**copy_request,'operationId':uuid.uuid4().hex};errors=[]
                for action in (lambda:engine.reserve(competitor,session),lambda:engine.plan(archive_command,session)):
                    try:action();raise AssertionError('COMPLETED_SOURCE_REUSED')
                    except reader.Rejected as error:
                        assert str(error).endswith('SOURCE_BUSY');errors.append(str(error))
                assert not (archive_state/(competitor['operationId']+'.json')).exists()
                lock=archive_state/(competitor['operationId']+'.lock');info=lock.stat();assert (info.st_uid,info.st_gid,info.st_mode&0o777,info.st_nlink,info.st_size)==(0,0,0o600,1,0)
                typed_equal(snapshot(restore_state),prior['claims']);typed_equal(unchanged(),prior['protected']);assert (archive_state/(copy_request['operationId']+'.json')).read_bytes()==prior['root']
                save('retention-'+label+'.json',dict(competitorOperationId=competitor['operationId'],errors=errors,claim=claim,rootSha256=hashlib.sha256(prior['root']).hexdigest(),emptyLockOnly=True,sourceClaimRetained=True,automaticCleanup=False))
            retention_check('live')
            retirement=load(bundle/NAMES[16],'archive_retention_candidate').ContainerRecycleArchiveRetentionPreflight(engine)
            def assess_retention(label,binding):
                def protected():return dict(archive=snapshot(archive_state),lifecycles=unchanged(),namespace=retention_namespace_snapshot(f['folder']))
                stable=protected();clock_before=time.time_ns()//1000000
                p=retirement.plan(dict(archive=copy_request,retainDays=30,mode='ArchiveData'),session);typed_equal(retirement.revalidate(p,session),p)
                clock_after=time.time_ns()//1000000
                assert p['historyCreatedAtUnixMs']==metadata_after['createdAtUnixMs'] and p['notBeforeUnixMs']==p['historyCreatedAtUnixMs']+30*86400000 and clock_before<=clock_after<p['notBeforeUnixMs']
                assert p['bindingCurrent'] is binding and p['retentionElapsed'] is False and p['retirementEligible'] is False
                typed_equal(p['archiveIdentity'],archive_before['identity']);typed_equal(p['publishedIdentity'],own_copy['publishedIdentity']);typed_equal(stable,protected())
                save('retention-assessment-'+label+'.json',dict(plan=p,clockBeforeUnixMs=clock_before,clockAfterUnixMs=clock_after,before=stable,after=protected(),installed=False,automaticCleanup=False))
            assess_retention('live',True)
            retirement_state=e/'retirement-state';retirement_state.mkdir(mode=0o700);(retirement_state/'claims').mkdir(mode=0o700)
            retirement_fence=load(bundle/NAMES[17],'archive_retirement_fences_candidate').retirement_fences_type(directory)(retirement,retirement_state)
            retire_plan=retirement_fence.plan(dict(archive=copy_request,retainDays=30,mode='ArchiveData'),session)
            assert retire_plan['retentionElapsed'] is False and retire_plan['retirementEligible'] is False and retire_plan['bindingCurrent'] is True
            retire_a=dict(operationId=uuid.uuid4().hex,owner='9'*64,plan=retire_plan);retire_b={**retire_a,'operationId':uuid.uuid4().hex}
            def unchanged_originals():return dict(archive=snapshot(archive_state),lifecycles=unchanged(),namespace=retention_namespace_snapshot(f['folder']))
            retirement_before=unchanged_originals();retire_steps=[]
            history_reader=load(bundle/NAMES[18],'retired_history_candidate').ContainerRecycleRetiredHistory(retirement_fence)
            def history_disposition(label,operation):
                before=unchanged_originals();roots=snapshot(retirement_state);observed=history_reader.observe(ref,captured['historyDigest'])
                typed_equal(observed,dict(operationId=ref['operationId'],historyDigest=captured['historyDigest'],disposition='Available',reason='VerifiedData',dataAvailable=True,dataPresent=True,retirementOperationId=operation))
                typed_equal(before,unchanged_originals());typed_equal(roots,snapshot(retirement_state))
                save('history-disposition-'+label+'.json',dict(observation=observed,before=before,after=unchanged_originals(),retirementRoots=roots,retirementRootsAfter=snapshot(retirement_state),readOnly=True))
            history_disposition('unreserved',None)
            def retirement_step(q,action,expected,context=session):
                receipt=getattr(retirement_fence,action)(q,context);assert receipt['state']==expected and receipt['retirementApplied'] is False
                typed_equal(unchanged_originals(),retirement_before)
                retire_steps.append(dict(operationId=q['operationId'],action=action,receipt=receipt,journal=json.loads((retirement_state/(q['operationId']+'.json')).read_bytes()),marker=json.loads((retirement_state/'claims'/(ref['operationId']+'.json')).read_bytes()),roots=snapshot(retirement_state)));return receipt
            retirement_step(retire_a,'reserve','Reserved')
            history_disposition('reserved',retire_a['operationId'])
            for action in (lambda:retirement_fence.reserve(retire_b,session),lambda:retirement_fence.plan(retire_plan['command'],session)):
                try:action();raise AssertionError('RETIREMENT_SOURCE_REUSED')
                except reader.Rejected as error:assert str(error).endswith('SOURCE_BUSY')
            assert not (retirement_state/(retire_b['operationId']+'.json')).exists()
            retirement_step(retire_a,'discard','Aborted');retirement_step(retire_b,'reserve','Reserved')
            marker_before=(retirement_state/'claims'/(ref['operationId']+'.json')).read_bytes()
            for action in ('reserve','inspect','discard'):
                retirement_step(retire_a,action,'Aborted',None);assert (retirement_state/'claims'/(ref['operationId']+'.json')).read_bytes()==marker_before
            retirement_step(retire_b,'reserve','Reserved',None);retirement_step(retire_b,'discard','Aborted')
            history_disposition('released',retire_b['operationId'])
            save('retirement-reservations.json',dict(requests=[retire_a,retire_b],steps=retire_steps,before=retirement_before,after=unchanged_originals(),roots=snapshot(retirement_state),sourceBusyRefusals=2,originalArchiveClaimUnchanged=True,schedulingBeforeExpiryOnly=True,retirementApplied=False))
            save('archive-copy.json',dict(request=copy_request,reserved=reserved_copy,receipt=copied,canceledRequest=canceled_request,canceledReceipt=canceled,root=own_copy,actual=actual,original=initial,claim=claim,protectedBefore=stable,protectedAfter=dict(source=snapshot(state),purge=snapshot(purge_state),hdd=payload_snapshot(hdd_folder)),journals=snapshot(archive_state),claims=snapshot(restore_state),actualNewInode=True,archiveCopyPerformed=True,replayDidNotChangeTarget=True,oldLifecyclesPreserved=True))
            with (e/'independent-live.log').open('x') as log:
                subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_archive_copies.py'),e.name,'--live'],check=True,stdout=log,stderr=subprocess.STDOUT,timeout=120)
            live=json.loads((e/'live-target-reread.json').read_text());assert live['actualLiveNewInodeReread'] and live['containerInstanceCurrent'];typed_equal(live['actual'],actual)
            # Exact QA cleanup removes this proved new copy, never a recovery
            # rollback or the old purged inode. The Completed journal/claim stay.
            typed_equal(file_snapshot(leaf),actual);leaf.unlink();assert not list(stage.iterdir()) and [stage.stat().st_dev,stage.stat().st_ino]==own_copy['privateIdentity'];stage.rmdir()
            after_cleanup=engine.inspect(copy_request,session);assert after_cleanup['state']=='Completed' and after_cleanup['archiveCopied'] is True and after_cleanup['bindingCurrent'] is False
            retention_check('absent')
            assess_retention('absent',False)
            history_disposition('after-cleanup',retire_b['operationId'])
            retired_observation=retirement_fence.inspect(retire_b,None);assert retired_observation['state']=='Aborted' and not retired_observation['sourceFenced'] and retired_observation['retirementApplied'] is False
            save('retirement-after-cleanup.json',dict(receipt=retired_observation,roots=snapshot(retirement_state),originalClaim=source.read_restore_claim(ref),hdd=payload_snapshot(hdd_folder),retirementApplied=False))
            save('copy-cleanup.json',dict(exactNewCopyRemoved=True,exactEmptyStageRemoved=True,oldOriginalInodeNotReused=True,after=after_cleanup,journals=snapshot(archive_state),claims=snapshot(restore_state)))
            save('purged.json',dict(request=reservation,reserved=reserved,receipt=result,source=source_record,marker=marker,journal=own,original=initial,retainedBefore=retained_before,archiveBefore=archive_before,archiveAfter=file_snapshot(hdd_folder/(ref['operationId']+'.data')),metadataBefore=metadata_before,metadataAfter=metadata_after,onlyOwnedRetainedFilePurged=True))
        op=ref['operationId'];private=f['folder']/source.PRIVATE
        assert not private.exists() and not leaf.exists();f['retain_unsettled']=False
    after=baseline(revision);expected={**before,'recycleHdd':after['recycleHdd']};typed_equal(expected,after)
    assert all(after['recycleHdd'].get(k)==v for k,v in before['recycleHdd'].items()) and set(after['recycleHdd'])-set(before['recycleHdd'])=={op+'.json',op+'.data'}
    typed_equal(old_rows('0'*64),rows);idle();save('after.json',after);save('business-after.json',old_rows('0'*64))
    save('verified.json',dict(actualContainer=True,actualConfiguredHdd=True,onlyOwnedRetainedFilePurged=True,archivePreserved=True,sourceRestoredForQaCleanup=False,ownedFixtureRemoved=True,oldRootBusinessAndProtectedPreserved=True,hddHistoryRetained=True,installed=False,signedRpc=False,businessUi=False,purgeApplied=True,archivePreflight=True,archiveReservations=True,archiveCopies=True,archiveCopyPerformed=True,automaticRetention=False,differentSourceDevice=plan['archiveIdentity'][0]!=plan['retainedIdentity'][0]))
    print(json.dumps(dict(archiveCopies='passed',evidence=e.name,installed=False,purgeApplied=True)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(archiveCopies='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)));raise SystemExit(1) from None
