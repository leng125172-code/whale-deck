#!/usr/bin/env python3
"""Finish physical cleanup of one sealed, already-ACKed failed QA case; never replay.

Not UI acceptance: the original gate remains failed, including its logout.
"""
import hashlib,json,os,re,stat,subprocess,sys,uuid
from pathlib import Path
from acl_history_browser_proof import typed_equal
from verify_container_recursive_acl import private_json
from acl_source_apply_missing_proof import source_closure
from archive_published_restart_unknown_proof import prove
from container_recycle_archive_restart_proof import same_operation
from cleanup_owned_archive_published_target import cleanup_owned_target
from validate_container_recycle_archive_published_restart import baseline,idle,old_rows
from validate_container_recycle_archive_browser import archive_sql
from validate_container_recycle_purge_browser import hdd_observation
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common,verify_container
import container_recycle_agent_io as source_io

CASE='container-recycle-archive-published-restart-141f74cda980432898a1399ce56b6759'
SEAL='8394c0b238302fb906c3ecc2fca35ac4235eaf0db461f8c5072440dec9af9c35'
ATTEMPT='archive-published-failure-disposal-96c137da31dc420ea32dd85da15a1f80'
ATTEMPT_SEAL='4fd1ff933f05c3748bcb4cdf30bca3de07e451d99144f1e2a6f028e28277135e'
SOURCES=('recover_archive_published_restart_failure.py','verify_archive_published_restart_failure.py','test_container_recycle_archive_published_failure_scope.py')


def sealed(repo,name,seal):
    e=repo/'deploy/artifacts'/name;manifest=e/'SHA256SUMS';assert hashlib.sha256(manifest.read_bytes()).hexdigest()==seal
    names=[]
    for line in manifest.read_text().splitlines():
        digest,relative=line.split('  ',1);p=Path(relative);assert not p.is_absolute() and '..' not in p.parts and p.as_posix()==relative
        path=e/p;s=path.lstat();assert stat.S_ISREG(s.st_mode) and s.st_uid==s.st_gid==0 and s.st_nlink==1 and stat.S_IMODE(s.st_mode)==0o600 and not path.is_symlink()
        assert hashlib.sha256(path.read_bytes()).hexdigest()==digest;names.append(relative)
    assert set(names)=={str(p.relative_to(e)) for p in e.rglob('*') if p.is_file() and p!=manifest}
    return e


def failed_case(repo,name):
    assert name==CASE,'ONLY_SEALED_FAILED_CASE_MAY_BE_DISPOSED'
    e=sealed(repo,name,SEAL);assert (e/'failure.json').is_file() and not (e/'verified.json').exists();return e


def ack_attempt(repo):
    e=sealed(repo,ATTEMPT,ATTEMPT_SEAL);assert private_json(e/'failure-boundary.json')['ackHadAlreadySucceeded'] and not (e/'disposed.json').exists();return e


def disposal_ack(before,after,actor,stopped):
    a,b=before['sql']['row'],after['sql']['row'];same_operation(a,b)
    assert a['state']=='Unknown' and b['state']=='Acknowledged' and actor==b['acknowledged_by']!=a['actor_subject'] and b['version']>a['version']
    for k in ('root_state','archive_copied','source_fenced','owned_fence','cancel_requested_at_utc'):typed_equal(a[k],b[k])
    # A fresh Discard may report no current binding after this inert QA sleep
    # exited. Only this exact false->null observation change is allowed here.
    assert stopped is True and a['binding_current'] is False and b['binding_current'] is None
    assert before['receipt']['bindingCurrent'] is False and after['receipt']['bindingCurrent'] is None
    for k in ('request','records','physical','hdd'):typed_equal(before[k],after[k])
    typed_equal({k:v for k,v in before['receipt'].items() if k!='bindingCurrent'},{k:v for k,v in after['receipt'].items() if k!='bindingCurrent'})
    assert before['sql']['pending']==1 and after['sql']['pending']==0 and before['audits']==dict(cancel=0,ack=0,ackActors=[]) and after['audits']==dict(cancel=0,ack=1,ackActors=[actor])


def inputs(repo):
    old=failed_case(repo,CASE);f=private_json(old/'fixture-initial.json');event=private_json(old/'archive-inflight-completed.json');f['subject']=event['subject']
    assert f['kind']=='worker-crash';return old,f,event['result'],private_json(old/'source-proof.json'),private_json(old/'success-proof.json'),private_json(old/'cut-record.json')


def current(repo,acknowledged):
    old,f,dto,source,purge,cut=inputs(repo);identifier=dto['id'];data=archive_sql(identifier)
    # SQL-derived public shape is for disposal proof only, never called UI output.
    receipt=json.loads(data['row']['receipt_json']);public={k[0].lower()+k[1:]:v for k,v in receipt.items()}
    dto={**dto,'state':data['row']['state'],'receipt':public}
    result=prove(dto,f,source,purge,cut,old,acknowledged=acknowledged)
    typed_equal(source_io.file_snapshot(Path(f['folder'])/'original.bin'),f['originalFixtureFile'])
    return result


def independent(repo,e,live):
    with (e/('independent-live.log' if live else 'independent-final.log')).open('x') as out:
        subprocess.run([sys.executable,str(repo/'deploy/validation/verify_archive_published_restart_failure.py'),e.name]+(['--live'] if live else []),check=True,stdout=out,stderr=subprocess.STDOUT,timeout=120)


def fixture_state(c,f,folder):
    verify_container(c,f['containerId'],f['containerName'],folder);s=c['State'];assert s['StartedAt']==f['instanceId'].split(':',1)[1]
    if s['Running']:assert str(s['Pid'])+':'+s['StartedAt']==f['instanceId'] and not s['Paused'] and not s['Restarting']
    else:assert s['Status']=='exited' and s['Pid']==0 and s['ExitCode']==0 and not s['OOMKilled'] and not s['Paused'] and not s['Restarting']


def main(revision):
    assert os.geteuid()==0 and re.fullmatch('[a-f0-9]{40}',revision);os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';repo=Path(__file__).resolve().parents[2]
    git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+str(repo),*args],text=True)
    assert not git('status','--porcelain').strip();sha=git('rev-parse','HEAD').strip();old,f,dto,source,purge,cut=inputs(repo);assert revision==f['revision']
    before=private_json(ack_attempt(repo)/'unknown-proof.json');ack=current(repo,True);preflight();idle()
    folder=Path(f['folder']);c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];fixture_state(c,f,folder)
    assert c['State']['Running'] is False;disposal_ack(before,ack,ack['sql']['row']['acknowledged_by'],True)
    e=repo/'deploy/artifacts'/('archive-published-failure-disposal-'+uuid.uuid4().hex);e.mkdir(mode=0o700)
    def save(n,v):
        with (e/n).open('x') as out:json.dump(v,out)
    closure=source_closure(repo,sha,seeds=SOURCES);frozen=e/'source';frozen.mkdir(mode=0o700)
    for n,h in closure.items():
        raw=(repo/'deploy/validation'/n).read_bytes();assert hashlib.sha256(raw).hexdigest()==h;(frozen/n).write_bytes(raw)
    save('source.json',dict(revision=revision,validationRevision=sha,validationSourceSha256=closure,original=CASE,originalSeal=SEAL,ackAttempt=ATTEMPT,ackAttemptSeal=ATTEMPT_SEAL,originalGatePassed=False));save('before.json',baseline(revision));save('unknown-proof.json',before);save('prior-business.json',old_rows(f['containerId']))
    print(json.dumps(dict(failedCaseDisposal='started',evidence=e.name)),flush=True)
    save('ack-proof.json',ack);save('existing-ack.json',dict(subject=ack['sql']['row']['acknowledged_by'],newAcknowledgementRequests=0,actualUi=False,originalGatePassed=False,preAckApiDtoNotCaptured=True))
    save('container-before.json',dict(id=f['containerId'],status=c['State']['Status'],running=False,exitCode=0,startedAt=c['State']['StartedAt']))
    independent(repo,e,True)
    # Stop only the exactly attested inert QA container. No product writes follow.
    c=json.loads(common.command(['docker','inspect',f['containerId']]))[0];fixture_state(c,f,folder)
    if c['State']['Running']:common.command(['docker','stop','--time','5',f['containerId']])
    stopped=json.loads(common.command(['docker','inspect',f['containerId']]))[0];verify_container(stopped,f['containerId'],f['containerName'],folder)
    assert not stopped['State']['Running'] and stopped['State']['Pid']==0 and stopped['State']['StartedAt']==c['State']['StartedAt']
    typed_equal(current(repo,True),ack);save('owned-target-cleanup.json',cleanup_owned_target(folder,ack['request'],ack['records']['root'],cut,ack['physical'],private_json(old/'cut-proof.json')['parentVersionAfterPublication'],ack['sql']['row']))
    private=folder/source_io.PRIVATE;i=private.lstat();assert not private.is_symlink() and [i.st_dev,i.st_ino]==purge['request']['plan']['privateIdentity'] and not list(private.iterdir());private.rmdir()
    typed_equal(source_io.file_snapshot(folder/'original.bin'),f['originalFixtureFile']);assert {p.name for p in folder.iterdir()}=={'original.bin'}
    common.command(['docker','rm',f['containerId']]);(folder/'original.bin').unlink();folder.rmdir()
    idle();typed_equal(old_rows(f['containerId']),private_json(e/'prior-business.json'));save('after.json',baseline(revision));typed_equal(private_json(e/'after.json'),private_json(e/'before.json'))
    save('hdd-after-cleanup.json',hdd_observation(source['request']['operationId']));typed_equal(private_json(e/'hdd-after-cleanup.json'),ack['hdd']);failed_case(repo,CASE);ack_attempt(repo)
    save('disposed.json',dict(originalGatePassed=False,actualUi=False,existingAdministratorAckPreserved=True,newAcknowledgementRequests=0,noApplyReplayed=True,rootClaimHddRetained=True,onlyOwnedFixtureRemoved=True))
    independent(repo,e,False);print(json.dumps(dict(failedCaseDisposal='passed',evidence=e.name,originalGatePassed=False)),flush=True)


if __name__=='__main__':
    try:assert len(sys.argv)==2;main(sys.argv[1])
    except BaseException as error:
        import traceback
        print(json.dumps(dict(failedCaseDisposal='failed',errorType=type(error).__name__,frames=[dict(file=Path(t.filename).name,line=t.lineno) for t in traceback.extract_tb(error.__traceback__)])),flush=True);raise SystemExit(1) from None
