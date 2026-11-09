#!/usr/bin/env python3
"""Read-only fresh execution-plan check for one independently verified aging capture.

This does not execute, reserve, reconcile or authorize retirement. Keep the
capture's frozen sources unchanged; the later destructive acceptance is separate.
"""
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import snapshot,read_at
from validate_container_file_staging import load
from validate_container_recycle_archive_copies import NAMES
from verify_container_recycle_archive_aging import configure_observer_git


def readiness_proof(value):
    assert set(value)=={'request','currentPlan','clockBeforeUnixMs','clockAfterUnixMs','status','retirementExecuted','rootsBefore','rootsAfter'}
    q=value['request'];p=value['currentPlan'];old=q['plan']
    for plan in (old,p):
        raw=json.dumps({k:v for k,v in plan.items() if k!='fingerprint'},sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()
        assert plan['fingerprint']==hashlib.sha256(b'whaledeck-container-recycle-archive-retention-plan-v1\n'+raw).hexdigest().upper()
    stable=lambda x:{k:v for k,v in x.items() if k not in ('fingerprint','retentionElapsed','retirementEligible')}
    typed_equal(stable(p),stable(old));assert p['bindingCurrent'] is True
    for k in ('clockBeforeUnixMs','clockAfterUnixMs'):assert type(value[k]) is int
    assert old['historyCreatedAtUnixMs']<=value['clockBeforeUnixMs']<=value['clockAfterUnixMs']<=253402300799999
    assert type(p['retentionElapsed']) is type(p['retirementEligible']) is bool
    due=p['notBeforeUnixMs'];before=value['clockBeforeUnixMs'];after=value['clockAfterUnixMs']
    # A check straddling the boundary must be repeated, never certified early.
    assert (before>=due and p['retentionElapsed'] is p['retirementEligible'] is True) or (after<due and p['retentionElapsed'] is p['retirementEligible'] is False)
    assert value['status']==('ReadyForExplicitAcceptance' if p['retirementEligible'] else 'NotDue')
    assert value['retirementExecuted'] is False;typed_equal(value['rootsBefore'],value['rootsAfter'])


def inspect_readiness(engine,q,session):
    preflight=engine.preflight;archive=engine.archive(q);source_id=engine.ref(q)['operationId']
    # Only existing locks: even a missing operation lock cannot be recreated.
    with engine.copies.preflight.existing_record(engine,q) as record,engine.records(archive) as records:
        assert record['schemaVersion']==1 and record['state']=='Reserved'
        marker=engine.read_marker(source_id);assert engine.matches(marker,q) and marker['state']=='Reserved'
        before=snapshot(engine.state_path);clock_before=time.time_ns()//1000000
        current=preflight.plan_locked(q['plan']['command'],session,*records)
        clock_after=time.time_ns()//1000000
        assert engine.read_marker(source_id)==marker
        result=dict(request=q,currentPlan=current,clockBeforeUnixMs=clock_before,clockAfterUnixMs=clock_after,status='ReadyForExplicitAcceptance' if current['retirementEligible'] else 'NotDue',retirementExecuted=False,rootsBefore=before,rootsAfter=snapshot(engine.state_path))
        readiness_proof(result);return result


def frozen_engine(e):
    bundle=e/'source'
    reader=load(bundle/NAMES[1],'readiness_reader');files=reader.trusted_module(NAMES[0],'readiness_files');docker=reader.trusted_module(NAMES[2],'readiness_docker')
    upload=load(bundle/NAMES[3],'readiness_upload').ContainerUploads(reader,files,e/'source-state')
    publisher=load(bundle/NAMES[4],'readiness_publisher').ContainerPublisher(upload,type('UnusedCapture',(),{'u':upload})())
    storage=load(bundle/NAMES[5],'readiness_storage').FileBackups(files)
    directory=load(bundle/NAMES[6],'readiness_directory').ContainerDirectories;base=load(bundle/NAMES[7],'readiness_base').ContainerPermissionHistory
    recycle=load(bundle/NAMES[8],'readiness_recycle');history=recycle.history_type(base)(storage);source=recycle.recycling_type(directory)(upload,publisher,e/'source-state',history)
    restore=load(bundle/NAMES[9],'readiness_restore').restores_type(directory)(source,e/'restore-state')
    planner=load(bundle/NAMES[10],'readiness_purge_plan').ContainerRecyclePurgePreflight(restore)
    fence=load(bundle/NAMES[11],'readiness_purge_fences').purge_fences_type(directory)
    purge=load(bundle/NAMES[12],'readiness_purge').purges_type(fence)(planner,e/'purge-state')
    plan=load(bundle/NAMES[13],'readiness_archive_plan').ContainerRecycleArchivePreflight(purge)
    fence=load(bundle/NAMES[14],'readiness_archive_fences').archive_fences_type(directory)
    copies=load(bundle/NAMES[15],'readiness_copies').archive_copies_type(fence)(plan,e/'archive-state')
    retention=load(bundle/NAMES[16],'readiness_retention').ContainerRecycleArchiveRetentionPreflight(copies)
    fence=load(bundle/NAMES[17],'readiness_retirement_fences').retirement_fences_type(directory)
    executor=load(bundle/NAMES[19],'readiness_retirement').archive_retirements_type(fence)(retention,e/'retirement-state')
    return executor,reader,files,docker


def main(name):
    assert os.geteuid()==0 and re.fullmatch('container-recycle-archive-copies-[a-f0-9]{32}',name);os.umask(0o077)
    repo=Path(__file__).resolve().parents[2];configure_observer_git(repo);e=repo/'deploy/artifacts'/name
    gate_revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
    assert re.fullmatch('[a-f0-9]{40}',gate_revision)
    gate_bytes=Path(__file__).read_bytes()
    assert gate_bytes==subprocess.check_output(['git','-C',str(repo),'show',gate_revision+':deploy/validation/'+Path(__file__).name])
    # Independent observer verifies full manifests, original live proof, current
    # Root/HDD/business/instance and bounded lifetime BEFORE loading any code.
    observed=subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_archive_aging.py'),name],check=True,capture_output=True,text=True,timeout=180)
    assert json.loads(observed.stdout)['archiveAgingIndependent']=='passed'
    q=read_at(None,str(e/'aging-handoff.json'))[1]['request'];engine,reader,files,docker=frozen_engine(e)
    command=engine.ref(q)['plan']['command']
    with reader.ContainerRoot(command,files,docker) as session:value=inspect_readiness(engine,q,session)
    # Check the independent whole-capture observation again after our scoped
    # native read. No output or receipt is written inside the retained capture.
    final=subprocess.run([sys.executable,str(repo/'deploy/validation/verify_container_recycle_archive_aging.py'),name],check=True,capture_output=True,text=True,timeout=180)
    assert json.loads(final.stdout)['archiveAgingIndependent']=='passed'
    out=repo/'deploy/artifacts'/('archive-retirement-readiness-'+uuid.uuid4().hex);out.mkdir(mode=0o700)
    (out/'readiness.json').write_text(json.dumps(value,indent=2)+'\n');(out/'observer-before.log').write_text(observed.stdout);(out/'observer-after.log').write_text(final.stdout)
    (out/'gate-source.py').write_bytes(gate_bytes)
    (out/'gate-source.json').write_text(json.dumps(dict(revision=gate_revision,sha256=hashlib.sha256(gate_bytes).hexdigest(),capture=name,captureRevision=read_at(None,str(e/'source.json'))[1]['validationRevision']))+'\n')
    print(json.dumps(dict(readiness=value['status'],notBeforeUnixMs=value['currentPlan']['notBeforeUnixMs'],retirementExecuted=False,evidence=out.name)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        print(json.dumps(dict(readiness='failed',type=type(error).__name__,retirementExecuted=False)),flush=True);raise SystemExit(1) from None
