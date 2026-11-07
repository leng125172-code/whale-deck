#!/usr/bin/env python3
"""Independent post-cleanup reread; never copies or invokes native recovery."""
import hashlib,json,os,re,stat,subprocess,sys
from pathlib import Path
from validate_container_recycle_archive_agent import baseline
from validate_container_recycle_archive_browser import old_rows,idle,hdd_observation
from recursive_acl_restore_agent_io import snapshot
from acl_history_browser_proof import typed_equal
from container_recycle_agent_io import file_snapshot
import validate_container_file_publishing as owned


def retirement_reservations_proof(e,copied,after_cleanup=False):
    read=lambda n:json.loads((e/n).read_bytes());value=read('retirement-reservations.json');requests=value['requests'];assert len(requests)==2
    a,b=requests;q=copied['request'];source=q['plan']['command']['purge']['plan']['command']['source'];source_id=source['operationId']
    assert all(set(r)=={'operationId','owner','plan'} and re.fullmatch('[a-f0-9]{32}',r['operationId']) and re.fullmatch('[A-F0-9]{64}',r['owner']) for r in requests)
    assert len({source_id,q['plan']['command']['purge']['operationId'],q['operationId'],copied['canceledRequest']['operationId'],a['operationId'],b['operationId']})==6
    typed_equal(a['plan'],b['plan']);typed_equal(a['plan'],read('retention-assessment-live.json')['plan'])
    assert a['plan']['retentionElapsed'] is a['plan']['retirementEligible'] is False and a['plan']['bindingCurrent'] is True
    typed_equal(value['before'],value['after']);assert value['sourceBusyRefusals']==2 and value['originalArchiveClaimUnchanged'] and value['schedulingBeforeExpiryOnly'] and value['retirementApplied'] is False
    expected=[(a,'reserve','Reserved',True,True,a,'Reserved'),(a,'discard','Aborted',False,False,a,'Released'),(b,'reserve','Reserved',True,True,b,'Reserved'),(a,'reserve','Aborted',True,False,b,'Reserved'),(a,'inspect','Aborted',True,False,b,'Reserved'),(a,'discard','Aborted',True,False,b,'Reserved'),(b,'reserve','Reserved',True,True,b,'Reserved'),(b,'discard','Aborted',False,False,b,'Released')]
    assert len(value['steps'])==len(expected)
    for i,(step,(request,action,state,fenced,owned,marker_request,marker_state)) in enumerate(zip(value['steps'],expected,strict=True)):
        typed_equal({k:step[k] for k in ('operationId','action')},dict(operationId=request['operationId'],action=action))
        receipt=dict(operationId=request['operationId'],sourceOperationId=source_id,archiveOperationId=q['operationId'],fingerprint=request['plan']['fingerprint'],state=state,sourceFenced=fenced,ownedFence=owned,retirementApplied=False,notBeforeUnixMs=request['plan']['notBeforeUnixMs']);typed_equal(step['receipt'],receipt)
        journal=dict(schemaVersion=1,**request,state=state);typed_equal(step['journal'],journal)
        marker=dict(schemaVersion=1,archive=q,retirementOperationId=marker_request['operationId'],retirementOwner=marker_request['owner'],retirementFingerprint=marker_request['plan']['fingerprint'],state=marker_state);typed_equal(step['marker'],marker)
        expected_names={a['operationId']+'.json',a['operationId']+'.lock','claims/'+source_id+'.json'}
        if i>=1:expected_names.add(b['operationId']+'.lock')
        if i>=2:expected_names.add(b['operationId']+'.json')
        assert set(step['roots'])==expected_names
        for name,observed in step['roots'].items():
            info=observed['identity'];assert stat.S_ISREG(info[2]) and stat.S_IMODE(info[2])==0o600 and info[3:6]==[0,0,1]
            if name.endswith('.lock'):assert info[6]==0 and observed['sha256']==hashlib.sha256(b'').hexdigest()
        for name,data in ((request['operationId']+'.json',step['journal']),('claims/'+source_id+'.json',step['marker'])):
            raw=json.dumps(data,separators=(',',':')).encode();assert step['roots'][name]['sha256']==hashlib.sha256(raw).hexdigest() and step['roots'][name]['identity'][6]==len(raw)
    typed_equal(value['roots'],value['steps'][-1]['roots']);typed_equal(value['roots'],snapshot(e/'retirement-state'))
    for request in requests:typed_equal(read('retirement-state/'+request['operationId']+'.json'),dict(schemaVersion=1,**request,state='Aborted'))
    typed_equal(read('retirement-state/claims/'+source_id+'.json'),value['steps'][-1]['marker'])
    if after_cleanup:
        final=read('retirement-after-cleanup.json');typed_equal(final['roots'],value['roots']);typed_equal(final['originalClaim'],copied['claim']);typed_equal(final['receipt'],value['steps'][-1]['receipt']);typed_equal(final['hdd'],copied['protectedAfter']['hdd']);assert final['retirementApplied'] is False


def live_main(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;assert e.resolve(strict=True)==e and e.stat().st_uid==0 and e.stat().st_mode&0o777==0o700
    read=lambda n:json.loads((e/n).read_bytes());source=read('source.json');copied=read('archive-copy.json');fixture=read('fixture-initial.json');q=copied['request'];ref=q['plan']['command']['purge']['plan']['command']['source'];op=ref['operationId']
    retirement_reservations_proof(e,copied)
    for folder,prefix,key in (('source','deploy/host/','sourceSha256'),('validation','deploy/validation/','validationSourceSha256')):
        for n,digest in source[key].items():
            assert re.fullmatch(r'[A-Za-z0-9_.-]+\.(py|mjs)',n)
            frozen=(e/folder/n).read_bytes();assert hashlib.sha256(frozen).hexdigest()==digest and frozen==(repo/prefix/n).read_bytes()
            assert frozen==subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),'show',source['validationRevision']+':'+prefix+n])
    assert fixture['containerId']==ref['plan']['command']['containerId'] and re.fullmatch('[a-f0-9]{64}',fixture['containerId'])
    c=json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0];folder=Path(fixture['folder']);owned.verify_container(c,fixture['containerId'],fixture['containerName'],folder)
    assert str(c['State']['Pid'])+':'+c['State']['StartedAt']==ref['plan']['command']['instanceId'] and folder.resolve(strict=True)==folder and [folder.stat().st_dev,folder.stat().st_ino]==q['plan']['parentIdentity']
    actual=file_snapshot(folder/'purge-kernel.bin');typed_equal(actual,copied['actual']);typed_equal(actual['identity'],copied['root']['publishedIdentity']);assert actual['identity'][:2]!=copied['original']['identity'][:2] and actual['identity'][:2]!=q['plan']['archiveIdentity'][:2]
    stage=folder/('.whaledeck-container-archive-'+q['operationId']);info=stage.lstat();assert not stage.is_symlink() and [info.st_dev,info.st_ino]==copied['root']['privateIdentity'] and (info.st_uid,info.st_gid,info.st_mode&0o777)==(0,0,0o700) and not list(stage.iterdir())
    typed_equal(read('archive-state/'+q['operationId']+'.json'),copied['root']);typed_equal(snapshot(e/'source-state'),copied['protectedAfter']['source']);typed_equal(snapshot(e/'purge-state'),copied['protectedAfter']['purge']);typed_equal(snapshot(e/'restore-state'),copied['claims']);typed_equal(read('restore-state/claims/'+op+'.json'),copied['claim'])
    hdd=hdd_observation(op);typed_equal(hdd['data']['identity'],q['plan']['archiveIdentity']);assert hdd['history']['captureState']=='Purged' and hdd['data']['sha256']==actual['sha256']
    before=read('before.json');current=baseline(source['revision']);typed_equal({**before,'recycleHdd':current['recycleHdd']},current);assert all(current['recycleHdd'].get(n)==v for n,v in before['recycleHdd'].items()) and set(current['recycleHdd'])-set(before['recycleHdd'])=={op+'.json',op+'.data'}
    typed_equal(old_rows('0'*64),read('business-before.json'));idle();typed_equal(file_snapshot(folder/'purge-kernel.bin'),actual)
    owned.verify_container(json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0],fixture['containerId'],fixture['containerName'],folder)
    with (e/'live-target-reread.json').open('x') as out:json.dump(dict(actualLiveNewInodeReread=True,containerInstanceCurrent=True,actual=actual,hdd=hdd,stageIdentity=[info.st_dev,info.st_ino],rootCompleted=True,oldBusinessAndProtectionPreserved=True),out)
    print(json.dumps(dict(archiveCopiesIndependent='passed',liveTargetNewInodeReread=True,archiveCopyPerformed=True)),flush=True)


def main(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name;assert e.resolve(strict=True)==e and e.stat().st_uid==0 and e.stat().st_mode&0o777==0o700
    read=lambda n:json.loads((e/n).read_bytes());source=read('source.json');raw=read('purged.json');proof=read('archive-preflight.json');absent=read('archive-after-private-cleanup.json');verified=read('verified.json')
    for folder,prefix,key in (('source','deploy/host/','sourceSha256'),('validation','deploy/validation/','validationSourceSha256')):
        for n,digest in source[key].items():
            assert re.fullmatch(r'[A-Za-z0-9_.-]+\.(py|mjs)',n)
            frozen=(e/folder/n).read_bytes();assert hashlib.sha256(frozen).hexdigest()==digest and frozen==(repo/prefix/n).read_bytes()
            committed=subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),'show',source['validationRevision']+':'+prefix+n]);assert frozen==committed
    assert raw['onlyOwnedRetainedFilePurged'] and raw['receipt']['state']=='Completed' and raw['receipt']['purgeApplied'] is True and raw['journal']['unlinkConfirmed'] is True
    assert raw['source']['state']=='Purged' and raw['source']['actualIdentity'] is None and raw['marker']['state']=='Purged'
    for item in (proof,absent):typed_equal(item['before'],item['after'])
    for key,folder in (('source','source-state'),('purge','purge-state')):typed_equal(absent['after'][key],snapshot(e/folder))
    ref=raw['request']['plan']['command']['source'];op=ref['operationId'];current=hdd_observation(op)
    typed_equal(current['history'],raw['metadataAfter']);typed_equal(current['data'],raw['archiveBefore']);typed_equal(raw['archiveBefore'],raw['archiveAfter'])
    typed_equal(raw['metadataAfter'],{**raw['metadataBefore'],'captureState':'Purged'})
    typed_equal(read('business-before.json'),read('business-after.json'));typed_equal(read('business-after.json'),old_rows('0'*64))
    before,after=read('before.json'),read('after.json');typed_equal({**before,'recycleHdd':after['recycleHdd']},after)
    assert all(after['recycleHdd'].get(n)==v for n,v in before['recycleHdd'].items()) and set(after['recycleHdd'])-set(before['recycleHdd'])=={op+'.json',op+'.data'}
    typed_equal(baseline(source['revision']),after);idle()
    assert verified['actualContainer'] and verified['actualConfiguredHdd'] and verified['archivePreflight'] and verified['ownedFixtureRemoved'] and not any(verified[k] for k in ('installed','signedRpc','businessUi','automaticRetention','sourceRestoredForQaCleanup'))
    copied=read('archive-copy.json');cleanup=read('copy-cleanup.json');q=copied['request'];r=copied['root'];actual=copied['actual'];old=copied['original']
    retirement_reservations_proof(e,copied,True)
    live=read('live-target-reread.json');assert live['actualLiveNewInodeReread'] and live['containerInstanceCurrent'] and live['rootCompleted'];typed_equal(live['actual'],actual)
    assert copied['actualNewInode'] and copied['archiveCopyPerformed'] and copied['replayDidNotChangeTarget'] and copied['oldLifecyclesPreserved'] and verified['archiveCopyPerformed'] and verified['archiveCopies']
    absent_lock=read('retention-absent.json')['competitorOperationId']+'.lock'
    assert re.fullmatch('[a-f0-9]{32}\\.lock',absent_lock) and set(cleanup['journals'])-set(copied['journals'])=={absent_lock}
    typed_equal(copied['protectedBefore'],copied['protectedAfter']);typed_equal(copied['journals'],{n:v for n,v in cleanup['journals'].items() if n!=absent_lock});typed_equal(cleanup['journals'],snapshot(e/'archive-state'));typed_equal(copied['claims'],cleanup['claims']);typed_equal(cleanup['claims'],snapshot(e/'restore-state'))
    assert r['state']=='Completed' and actual['identity'][:2]!=old['identity'][:2] and actual['identity'][:2]!=current['data']['identity'][:2]
    typed_equal(actual['identity'][2:8],old['identity'][2:8]);typed_equal(r['publishedIdentity'],actual['identity'])
    for key in ('sha256','attributes','sizeBytes'):typed_equal(actual[key],old[key])
    typed_equal(q['plan'],absent['plan']);typed_equal({k:r[k] for k in ('operationId','owner','plan')},q)
    current_root=read('archive-state/'+q['operationId']+'.json');typed_equal(current_root,r)
    receipt=copied['receipt'];assert receipt['state']=='Completed' and receipt['archiveCopied'] is True and receipt['bindingCurrent'] is True
    assert cleanup['exactNewCopyRemoved'] and cleanup['exactEmptyStageRemoved'] and cleanup['oldOriginalInodeNotReused']
    assert cleanup['after']['state']=='Completed' and cleanup['after']['archiveCopied'] is True and cleanup['after']['bindingCurrent'] is False
    claim=copied['claim'];typed_equal(claim,dict(schemaVersion=1,source=ref,restoreOperationId=q['operationId'],restoreOwner=q['owner'],restoreFingerprint=q['plan']['fingerprint']))
    typed_equal(read('restore-state/claims/'+op+'.json'),claim)
    assert set(cleanup['claims'])=={'claims/'+op+'.json'} and absent['after']['restore']=={}
    canceled=copied['canceledRequest'];assert len({op,raw['request']['operationId'],q['operationId'],canceled['operationId']})==4
    cr=read('archive-state/'+canceled['operationId']+'.json');assert cr['state']=='Aborted';typed_equal({k:cr[k] for k in ('operationId','owner','plan')},canceled)
    rejected=[]
    for label in ('live','absent'):
        retained=read('retention-'+label+'.json');operation=retained['competitorOperationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
        assert retained['errors']==['CONTAINER_RECYCLE_ARCHIVE_SOURCE_BUSY']*2 and retained['emptyLockOnly'] and retained['sourceClaimRetained'] and retained['automaticCleanup'] is False
        typed_equal(retained['claim'],claim);assert retained['rootSha256']==hashlib.sha256((e/('archive-state/'+q['operationId']+'.json')).read_bytes()).hexdigest()
        assert not (e/('archive-state/'+operation+'.json')).exists()
        lock=e/('archive-state/'+operation+'.lock');info=lock.stat();assert (info.st_uid,info.st_gid,info.st_mode&0o777,info.st_nlink,info.st_size)==(0,0,0o600,1,0)
        rejected.append(operation)
    assert len(set(rejected+[op,raw['request']['operationId'],q['operationId'],canceled['operationId']]))==6
    assert set(cleanup['journals'])=={value['operationId']+suffix for value in (q,canceled) for suffix in ('.json','.lock')}|{operation+'.lock' for operation in rejected}
    def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()
    def digest(domain,value):return hashlib.sha256(domain+canonical(value)).hexdigest().upper()
    plans=[]
    for label,binding in (('live',True),('absent',False)):
        assessment=read('retention-assessment-'+label+'.json');p=assessment['plan'];typed_equal(assessment['before'],assessment['after'])
        assert assessment['installed'] is False and assessment['automaticCleanup'] is False
        typed_equal(p['command'],dict(archive=q,retainDays=30,mode='ArchiveData'))
        assert p['historyCreatedAtUnixMs']==raw['metadataAfter']['createdAtUnixMs'] and p['notBeforeUnixMs']==p['historyCreatedAtUnixMs']+30*86400000
        assert p['historyCreatedAtUnixMs']<=assessment['clockBeforeUnixMs']<=assessment['clockAfterUnixMs']<p['notBeforeUnixMs']
        assert p['retentionElapsed'] is False and p['retirementEligible'] is False and p['bindingCurrent'] is binding
        typed_equal(p['archiveIdentity'],current['data']['identity']);typed_equal(p['publishedIdentity'],r['publishedIdentity'])
        assert p['archiveSha256']==q['plan']['archiveSha256'] and p['archiveBytes']==current['data']['sizeBytes']
        assert p['archiveRecordDigest']==digest(b'archive-retention-copy-v1\n',r) and p['claimDigest']==digest(b'archive-retention-claim-v1\n',claim)
        for key,domain,folder,identifier in (('sourceRecordDigest',b'archive-source-v1\n','source-state',op),('purgeRecordDigest',b'archive-purge-v1\n','purge-state',raw['request']['operationId']),('purgeMarkerDigest',b'archive-marker-v1\n','source-state',op+'.purge')):
            assert p[key]==digest(domain,read(folder+'/'+identifier+'.json'))==q['plan'][key]
        for key in ('historyMetadataDigest','storageHash','storeIdentity'):typed_equal(p[key],q['plan'][key])
        assert p['fingerprint']==digest(b'whaledeck-container-recycle-archive-retention-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})
        plans.append(p)
    typed_equal({k:v for k,v in plans[0].items() if k not in ('bindingCurrent','fingerprint')},{k:v for k,v in plans[1].items() if k not in ('bindingCurrent','fingerprint')})
    fixture=read('fixture-initial.json');assert re.fullmatch('[a-f0-9]{64}',fixture['containerId']) and not Path(fixture['folder']).exists()
    assert subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True,timeout=10).returncode!=0
    assert proof['originalAbsent'] and proof['archiveCopyPerformed'] is False and absent['oldPlanRejected'] and absent['plan']['privateIdentity'] is None
    assert proof['plan']['archiveIdentity']==current['data']['identity'] and proof['plan']['deletedIdentity']==raw['retainedBefore']['identity']
    for plan in (proof['plan'],absent['plan']):
        expected=hashlib.sha256(b'whaledeck-container-recycle-archive-plan-v1\n'+json.dumps({k:v for k,v in plan.items() if k!='fingerprint'},sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper();assert plan['fingerprint']==expected
    print(json.dumps(dict(archiveCopiesIndependent='passed',hddRereadAfterCleanup=True,archiveCopyPerformed=True)),flush=True)


if __name__=='__main__':
    try:
        assert len(sys.argv) in (2,3) and (len(sys.argv)==2 or sys.argv[2]=='--live')
        live_main(sys.argv[1]) if len(sys.argv)==3 else main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(archiveCopiesIndependent='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
