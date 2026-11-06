#!/usr/bin/env python3
"""Independent post-cleanup reread; never copies or invokes native recovery."""
import hashlib,json,os,re,subprocess,sys
from pathlib import Path
from validate_container_recycle_purge_agent import baseline
from validate_container_recycle_purge_browser import old_rows,idle,hdd_observation
from recursive_acl_restore_agent_io import snapshot
from acl_history_browser_proof import typed_equal


def main(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-fences-[a-f0-9]{32}',name);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
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
    for key,folder in (('source','source-state'),('restore','restore-state'),('purge','purge-state')):typed_equal(absent['after'][key],snapshot(e/folder))
    ref=raw['request']['plan']['command']['source'];op=ref['operationId'];current=hdd_observation(op)
    typed_equal(current['history'],raw['metadataAfter']);typed_equal(current['data'],raw['archiveBefore']);typed_equal(raw['archiveBefore'],raw['archiveAfter'])
    typed_equal(raw['metadataAfter'],{**raw['metadataBefore'],'captureState':'Purged'})
    typed_equal(read('business-before.json'),read('business-after.json'));typed_equal(read('business-after.json'),old_rows('0'*64))
    before,after=read('before.json'),read('after.json');typed_equal({**before,'recycleHdd':after['recycleHdd']},after)
    assert all(after['recycleHdd'].get(n)==v for n,v in before['recycleHdd'].items()) and set(after['recycleHdd'])-set(before['recycleHdd'])=={op+'.json',op+'.data'}
    typed_equal(baseline(source['revision']),after);idle()
    assert verified['actualContainer'] and verified['actualConfiguredHdd'] and verified['archivePreflight'] and verified['ownedFixtureRemoved'] and not any(verified[k] for k in ('installed','signedRpc','businessUi','archiveCopyPerformed','automaticRetention','sourceRestoredForQaCleanup'))
    reservations=read('archive-reservations.json');typed_equal(reservations['protectedBefore'],reservations['protectedAfter']);typed_equal(reservations['sharedBefore'],reservations['sharedAfter']);typed_equal(reservations['journals'],snapshot(e/'archive-state'))
    assert reservations['coldEngineReread'] and reservations['foreignAndLegacyRejected'] and reservations['lateFirstDidNotReleaseSecond'] and reservations['archiveCopyPerformed'] is False and verified['archiveReservations']
    one,two=reservations['firstRequest'],reservations['secondRequest'];assert len({one['operationId'],two['operationId'],op,raw['request']['operationId']})==4
    for request,reserved,aborted in ((one,reservations['firstReserved'],reservations['firstAborted']),(two,reservations['secondReserved'],reservations['secondAborted'])):
        record=read('archive-state/'+request['operationId']+'.json');typed_equal({k:record[k] for k in ('operationId','owner','plan')},request);assert record['state']=='Aborted'
        assert reserved['state']=='Reserved' and reserved['ownedFence'] and reserved['sourceFenced'] and reserved['archiveCopied'] is False
        assert aborted['state']=='Aborted' and not aborted['ownedFence'] and not aborted['sourceFenced'] and aborted['archiveCopied'] is False
        assert reserved['operationId']==aborted['operationId']==request['operationId'] and reserved['sourceOperationId']==op and reserved['purgeOperationId']==raw['request']['operationId']
        typed_equal(request['plan'],proof['plan'])
    assert not list((e/'restore-state/claims').iterdir())
    expected={q['operationId']+suffix for q in (one,two) for suffix in ('.json','.lock')};assert set(reservations['journals'])==expected
    first=reservations['firstClaim'];typed_equal(first,dict(schemaVersion=1,source=ref,restoreOperationId=one['operationId'],restoreOwner=one['owner'],restoreFingerprint=one['plan']['fingerprint']))
    fixture=read('fixture-initial.json');assert not Path(fixture['folder']).exists() and re.fullmatch('[a-f0-9]{64}',fixture['containerId'])
    assert subprocess.run(['docker','inspect',fixture['containerId']],capture_output=True,timeout=10).returncode!=0
    assert proof['originalAbsent'] and proof['archiveCopyPerformed'] is False and absent['oldPlanRejected'] and absent['plan']['privateIdentity'] is None
    assert proof['plan']['archiveIdentity']==current['data']['identity'] and proof['plan']['deletedIdentity']==raw['retainedBefore']['identity']
    for plan in (proof['plan'],absent['plan']):
        expected=hashlib.sha256(b'whaledeck-container-recycle-archive-plan-v1\n'+json.dumps({k:v for k,v in plan.items() if k!='fingerprint'},sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper();assert plan['fingerprint']==expected
    print(json.dumps(dict(archiveFencesIndependent='passed',hddRereadAfterCleanup=True,archiveCopyPerformed=False)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:print(json.dumps(dict(archiveFencesIndependent='failed',type=type(error).__name__)),flush=True);raise SystemExit(1) from None
