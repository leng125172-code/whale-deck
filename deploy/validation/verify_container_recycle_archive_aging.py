#!/usr/bin/env python3
"""Read-only handoff observer for one owned naturally aging QA capture.

No executor import, recovery, reservation, scheduler, cleanup or deletion. Even
when the real clock reaches due, this command only reports capture age. A later
explicitly authorized gate must revalidate and independently sign actual unlink.
"""
from datetime import datetime,timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from validate_container_recycle_archive_copies import NAMES,VALIDATION_SEEDS
from archive_retirement_preexpiry_proof import integer,private_file,raw_record_proof,preexpiry_refusal_proof
from recursive_acl_restore_agent_io import snapshot,read_at
from validate_container_recycle_archive_agent import baseline
from validate_container_recycle_archive_browser import old_rows,idle,hdd_observation
from verify_container_recycle_archive_copies import retirement_namespace_reread,retirement_namespace_proof
import validate_container_file_publishing as owned


def source_manifest_proof(source,hosts,validation):
    assert hosts and validation
    typed_equal(source['sourceSha256'],hosts);typed_equal(source['validationSourceSha256'],validation)


def aging_live_records(e):
    return tuple(read_at(None,str(e/name))[1] for name in ('live-target-reread.json','retention-assessment-live.json'))


def aging_live_evidence_proof(live,assessment,value,copied,current,hdd):
    assert set(live)=={'actualLiveNewInodeReread','containerInstanceCurrent','actual','hdd','stageIdentity','namespace','liveObservedAtUnixMs','rootCompleted','oldBusinessAndProtectionPreserved'}
    assert all(live[k] is True for k in ('actualLiveNewInodeReread','containerInstanceCurrent','rootCompleted','oldBusinessAndProtectionPreserved'))
    assert set(assessment)=={'plan','clockBeforeUnixMs','clockAfterUnixMs','before','after','installed','automaticCleanup'}
    p=value['request']['plan'];typed_equal(assessment['plan'],p)
    integer(assessment['clockBeforeUnixMs'],p['historyCreatedAtUnixMs'],p['notBeforeUnixMs']-1)
    integer(assessment['clockAfterUnixMs'],assessment['clockBeforeUnixMs'],p['notBeforeUnixMs']-1)
    integer(live['liveObservedAtUnixMs'],assessment['clockAfterUnixMs'],value['preparedAtUnixMs'])
    assert assessment['installed'] is assessment['automaticCleanup'] is False
    typed_equal(assessment['before'],assessment['after']);typed_equal(assessment['before'],current['protected'])
    typed_equal(live['namespace'],current['protected']['namespace']);typed_equal(live['actual'],copied['actual']);typed_equal(live['hdd'],hdd)
    typed_equal(live['stageIdentity'],copied['root']['privateIdentity'])


def aging_handoff_proof(value,copied,reservations,early,current):
    assert type(value) is dict and set(value)=={'schemaVersion','request','journal','marker','retirementRootsBefore','retirementRootsAfter','protectedBefore','protectedAfter','preparedAtUnixMs','lifetimeSeconds'}
    integer(value['schemaVersion'],1,1);integer(value['lifetimeSeconds'],172800,172800)
    q=value['request'];assert set(q)=={'operationId','owner','plan'}
    assert type(q['operationId']) is str and re.fullmatch('[a-f0-9]{32}',q['operationId']) and q['owner']=='8'*64
    a,b=reservations['requests'];typed_equal(a['plan'],b['plan']);typed_equal(q['plan'],a['plan'])
    preexpiry_refusal_proof(early,a,copied['root'])
    typed_equal(early['retirementRootsBefore'],reservations['steps'][0]['roots'])
    typed_equal(early['protectedBefore'],reservations['before']);typed_equal(reservations['before'],reservations['after'])
    p=q['plan'];assert p['command']['retainDays']==1
    archive=p['command']['archive'];typed_equal(archive,copied['request']);purge=archive['plan']['command']['purge'];ref=purge['plan']['command']['source'];source_id=ref['operationId']
    assert len({q['operationId'],a['operationId'],b['operationId'],archive['operationId'],copied['canceledRequest']['operationId'],purge['operationId'],source_id})==7
    integer(value['preparedAtUnixMs'],early['clockAfterUnixMs'],p['notBeforeUnixMs']-1)
    before=value['retirementRootsBefore'];after=value['retirementRootsAfter'];typed_equal(before,reservations['roots']);typed_equal(after,current['roots'])
    marker_name='claims/'+source_id+'.json'
    assert set(before)=={op+suffix for op in (a['operationId'],b['operationId']) for suffix in ('.json','.lock')}|{marker_name}
    assert set(after)==set(before)|{q['operationId']+'.json',q['operationId']+'.lock'}
    for name in before:
        if name!=marker_name:typed_equal(after[name],before[name])
    for old in (a,b):typed_equal(current['oldJournals'][old['operationId']],dict(schemaVersion=1,**old,state='Aborted'));raw_record_proof(after[old['operationId']+'.json'],current['oldJournals'][old['operationId']])
    typed_equal(value['journal'],dict(schemaVersion=1,**q,state='Reserved'));typed_equal(value['journal'],current['journal'])
    typed_equal(value['marker'],dict(schemaVersion=1,archive=archive,retirementOperationId=q['operationId'],retirementOwner=q['owner'],retirementFingerprint=p['fingerprint'],state='Reserved'));typed_equal(value['marker'],current['marker'])
    raw_record_proof(after[q['operationId']+'.json'],current['journal']);raw_record_proof(after[marker_name],current['marker'])
    for name in after:
        info=private_file(after[name])
        if name.endswith('.lock'):assert info[6]==0 and after[name]['sha256']==hashlib.sha256(b'').hexdigest()
    typed_equal(value['protectedBefore'],value['protectedAfter']);typed_equal(value['protectedBefore'],early['protectedBefore']);typed_equal(value['protectedBefore'],current['protected'])
    protected=current['protected'];typed_equal(protected['archive'],copied['journals'])
    for name in ('source','purge','hdd'):typed_equal(protected['lifecycles'][name],copied['protectedAfter'][name])
    typed_equal(protected['lifecycles']['restore'],copied['claims'])
    return dict(request=q,notBeforeUnixMs=p['notBeforeUnixMs'])


def aging_container_proof(container,request,prepared,now):
    """Fixed finite inert process; never a daemon or restart/retirement job."""
    integer(now,prepared,253402300799999)
    command=request['plan']['command']['archive']['plan']['command']['purge']['plan']['command']['source']['plan']['command']
    assert container['Id']==command['containerId'] and re.fullmatch('[a-f0-9]{64}',container['Id'])
    config=container['Config'];host=container['HostConfig'];state=container['State']
    assert config['Entrypoint']==['/bin/sleep'] and config['Cmd']==['172800'] and container['Path']=='/bin/sleep' and container['Args']==['172800']
    assert config['User']=='1000:1000' and config['Tty'] is config['OpenStdin'] is False
    assert host['NetworkMode']=='none' and host['ReadonlyRootfs'] is True and host['Privileged'] is host['AutoRemove'] is False
    assert host['CapDrop']==['ALL'] and not host['CapAdd'] and host['SecurityOpt']==['no-new-privileges']
    assert host['RestartPolicy']==dict(Name='no',MaximumRetryCount=0)
    assert host['Memory']==33554432 and host['NanoCpus']==250000000 and host['PidsLimit']==16
    assert state['Running'] is True and state['Paused'] is state['Restarting'] is state['OOMKilled'] is False
    integer(state['Pid'],1,2**31-1);assert command['instanceId']==str(state['Pid'])+':'+state['StartedAt']
    started=datetime.fromisoformat(state['StartedAt'].replace('Z','+00:00'));assert started.utcoffset().total_seconds()==0
    started_ms=int((started-datetime(1970,1,1,tzinfo=timezone.utc)).total_seconds()*1000)
    p=request['plan'];assert started_ms<=p['historyCreatedAtUnixMs']<=prepared<=now<started_ms+172800000
    assert p['notBeforeUnixMs']+6*3600000<started_ms+172800000
    return dict(captureAgeElapsed=now>=p['notBeforeUnixMs'],retirementExecuted=False,automaticExecution=False)


def main(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name);os.environ['GIT_OPTIONAL_LOCKS']='0'
    repo=Path(__file__).resolve().parents[2];e=repo/'deploy/artifacts'/name
    info=e.lstat();assert e.resolve(strict=True)==e and stat.S_ISDIR(info.st_mode) and (info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode))==(0,0,0o700)
    read=lambda n:read_at(None,str(e/n))[1]
    source=read('source.json');value=read('aging-handoff.json');copied=read('archive-copy.json');reservations=read('retirement-reservations.json');early=read('retirement-preexpiry.json');fixture=read('fixture-initial.json')
    live,assessment=aging_live_records(e)
    # Freeze host and validator bytes against both Git and the current checkout.
    # Changed code or lost original evidence is a refusal, never a repair.
    assert re.fullmatch('[a-f0-9]{40}',source['validationRevision'])
    git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),*args])
    hosts={n:hashlib.sha256(git('show',source['validationRevision']+':deploy/host/'+n)).hexdigest() for n in NAMES}
    validation=source_closure(repo,source['validationRevision'],seeds=VALIDATION_SEEDS)
    source_manifest_proof(source,hosts,validation)
    for folder,prefix,key in (('source','deploy/host/','sourceSha256'),('validation','deploy/validation/','validationSourceSha256')):
        for n,expected in source[key].items():
            assert re.fullmatch(r'[A-Za-z0-9_.-]+\.(py|mjs)',n)
            raw=(e/folder/n).read_bytes();assert hashlib.sha256(raw).hexdigest()==expected and raw==(repo/prefix/n).read_bytes()
            assert raw==subprocess.check_output(['git','-c','safe.directory='+str(repo),'-C',str(repo),'show',source['validationRevision']+':'+prefix+n])
    q=value['request'];p=q['plan'];ref=p['command']['archive']['plan']['command']['purge']['plan']['command']['source'];source_id=ref['operationId']
    assert re.fullmatch('[a-f0-9]{32}',q['operationId']) and re.fullmatch('[a-f0-9]{32}',source_id)
    folder=Path(fixture['folder']);assert folder.parent==owned.common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}',folder.name)
    container=json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0]
    owned.verify_container(container,fixture['containerId'],fixture['containerName'],folder)
    typed_equal([folder.stat().st_dev,folder.stat().st_ino],p['command']['archive']['plan']['parentIdentity'])
    namespace=retirement_namespace_reread(folder);retirement_namespace_proof(namespace,fixture,copied)
    current_baseline=baseline(source['revision']);typed_equal(current_baseline,read('aging-baseline.json'));typed_equal(old_rows('0'*64),read('aging-business.json'));typed_equal(read('aging-business.json'),read('business-before.json'));idle()
    assert all(type(old) is dict and type(old['operationId']) is str and re.fullmatch('[a-f0-9]{32}',old['operationId']) for old in reservations['requests'])
    roots=snapshot(e/'retirement-state')
    current=dict(roots=roots,journal=read('retirement-state/'+q['operationId']+'.json'),marker=read('retirement-state/claims/'+source_id+'.json'),
        oldJournals={old['operationId']:read('retirement-state/'+old['operationId']+'.json') for old in reservations['requests']},
        protected=dict(archive=snapshot(e/'archive-state'),lifecycles=dict(source=snapshot(e/'source-state'),purge=snapshot(e/'purge-state'),restore=snapshot(e/'restore-state'),hdd=current_baseline['recycleHdd']),namespace=namespace))
    aging_handoff_proof(value,copied,reservations,early,current)
    hdd=hdd_observation(source_id);typed_equal(hdd['data'],read('purged.json')['archiveBefore']);typed_equal(hdd['history'],read('purged.json')['metadataAfter']);assert hdd['history']['createdAtUnixMs']==p['historyCreatedAtUnixMs']
    aging_live_evidence_proof(live,assessment,value,copied,current,hdd)
    status=aging_container_proof(container,q,value['preparedAtUnixMs'],time.time_ns()//1000000)
    # The observer never calls native inspect/recover: even those can reconcile.
    typed_equal(roots,snapshot(e/'retirement-state'));typed_equal(current['protected']['namespace'],retirement_namespace_reread(folder))
    owned.verify_container(json.loads(owned.common.command(['docker','inspect',fixture['containerId']]))[0],fixture['containerId'],fixture['containerName'],folder)
    print(json.dumps(dict(archiveAgingIndependent='passed',**status,notBeforeUnixMs=p['notBeforeUnixMs'])),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1];print(json.dumps(dict(archiveAgingIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)),flush=True);raise SystemExit(1) from None
