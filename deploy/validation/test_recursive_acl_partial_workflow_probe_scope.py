"""Partial protection and truthful explicit acknowledgement boundaries only."""
import ast,copy,json
import datetime as dt
import hashlib
from unittest.mock import patch
from recursive_acl_partial_restart_proof import partial_checkpoint,inventory_digest,verify_partial_lease
from recursive_acl_restart_proof import require_only_transients,owner_unit
from recursive_acl_partial_unknown_proof import require_unknown_identity,remove_unknown_target
from recursive_acl_instance_proof import require_instance_transition,prepare_instance,restart_instance,instance_frame
import validate_container_file_publishing as owned
from recursive_acl_cross_directory_proof import selected_waiting,require_registration,require_deferrals,require_completed,require_blocked,clean_waiting,STABLE,WAITING_PATH
from pathlib import Path
import unittest
from recursive_acl_partial_proof import require_phase,require_ack,require_case,entry_checkpoint
from validate_container_recursive_acl_partial import selected_case,create_tree
from verify_container_recursive_acl_partial import target

ROOT=Path(__file__).resolve().parent


def phase():
    stable=('id','actor_subject','login_binding','container_id','instance_id','relative_path','owner_hash','command_json','request_hash','plan_json','plan_hash','execution_hash','execution_job_id','confirmed_at_utc','execution_expires_at_utc','root_started_at_utc','outcome_digest','history_digest','cancel_requested_at_utc')
    row={k:k for k in stable};receipt=dict(Id='original-execution',State='Partial',ChangedCount=1,Replayed=True,BindingCurrent=False,HistoryAvailable=True)
    row.update(state='Partial',root_state='Partial',publish_barrier=True,completed_at_utc=None,lease_token=None,lease_expires_at_utc=None,history_verified=True,history_may_exist=True,
        result_code='CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT',completed_count=1,changed_count=1,aborted_count=0,not_started_count=3,unknown_count=0,
        acknowledged_by=None,acknowledgement_json=None,receipt_json=json.dumps(receipt),receipt_hash='A'*64,version=5)
    return dict(row=row,planJob=dict(state='Succeeded'),executionJob=dict(state='Running',completed_at_utc=None),pending=1,accepted=1,confirmed=1)


def acknowledgement(before):
    after=copy.deepcopy(before);r=after['row'];receipt=json.loads(r['receipt_json']);receipt['Replayed']=False
    r.update(state='Acknowledged',publish_barrier=False,completed_at_utc='now',result_code='CONTAINER_RECURSIVE_ACL_PARTIAL_ACKNOWLEDGED',acknowledged_by='different-admin',acknowledgement_json=json.dumps(receipt),receipt_json=json.dumps(receipt),receipt_hash='B'*64,version=6)
    after['executionJob']=dict(state='Failed',completed_at_utc='now',phase='Acknowledged');after['pending']=0;return after


class RecursiveAclPartialWorkflowScope(unittest.TestCase):
    def test_owned_tree_build_does_not_overwrite_logout_scenario(self):
        import tempfile,stat
        from unittest.mock import patch
        scenario=selected_case(['logout'])
        with tempfile.TemporaryDirectory() as directory,patch('validate_container_recursive_acl_partial.os.chown') as chown:
            root=Path(directory)/'recursive-tree';create_tree(root,b'fixed test bytes')
            self.assertEqual('logout',scenario);self.assertEqual(4,chown.call_count)
            self.assertEqual(b'fixed test bytes',(root/'sub/中文.bin').read_bytes());self.assertEqual(0o600,stat.S_IMODE((root/'sub/中文.bin').stat().st_mode))
            self.assertEqual(0o640,stat.S_IMODE((root/'one.bin').stat().st_mode));self.assertEqual(0o750,stat.S_IMODE(root.stat().st_mode))
            text=(ROOT/'validate_container_recursive_acl_partial.py').read_text();main=next(n for n in ast.parse(text).body if isinstance(n,ast.FunctionDef) and n.name=='main')
            for node in ast.walk(main):
                if isinstance(node,ast.For):self.assertNotIn('kind',{n.id for n in ast.walk(node.target) if isinstance(n,ast.Name)})

    def test_scenario_selection_cannot_accidentally_repeat_cancel_or_revoke(self):
        self.assertEqual('cancel',selected_case([]));self.assertEqual('logout',selected_case(['logout']))
        self.assertEqual('instance-changed',selected_case(['instance-changed']))
        self.assertEqual('target-removed',selected_case(['target-removed']))
        self.assertEqual('worker-crash',selected_case(['worker-crash']));self.assertEqual('agent-restart',selected_case(['agent-restart']))
        self.assertEqual('group-removal',selected_case(['group-removal']));self.assertEqual('account-disable',selected_case(['account-disable']))
        for values in (['cancel'],['logout','cancel'],['group-removal','account-disable'],['--all']):
            with self.subTest(values=values),self.assertRaises(AssertionError):selected_case(values)

    def test_restart_owner_must_be_exact_unit_with_current_pid_and_invocation(self):
        import os
        helper='whaledeck-container-recursive-acl-'+'a'*32+'.service'
        for label in ('worker','agent'):
            unit='wd-qa-recursive-acl-partial-restarts-123abcd-'+label+'.service';group='/system.slice/'+unit;inv='b'*32
            require_only_transients([unit,helper],helper,unit)
            props=f'InvocationID={inv}\nMainPID={os.getpid()}\nControlGroup={group}'
            with patch('recursive_acl_restart_proof.Path.read_text',return_value='0::'+group),patch.dict(os.environ,{'INVOCATION_ID':inv}),patch('recursive_acl_restart_proof.command',return_value=props):self.assertEqual(unit,owner_unit())
            with self.assertRaises(AssertionError):require_only_transients([unit,helper,'wd-qa-foreign.service'],helper,unit)
        for unit in ('wd-qa-recursive-acl-partial-restarts-123abcd.service','wd-qa-recursive-acl-partial-restarts-123abcd-all.service'):
            with self.assertRaises(AssertionError):require_only_transients([],helper,unit)

    def test_partial_checkpoint_cannot_treat_zero_unknown_or_full_tree_as_partial(self):
        cut=dict(operationId='a'*32,owner='owner',plan=dict(command=dict(aclType='access'),entryCount=4,changeCount=4,fileCount=2,directoryCount=2),historyDigest='A'*64,
            state='Applying',historyVerified=True,historyMayExist=True,finishedThrough=0,startedThrough=1)
        partial_checkpoint(cut,cut)
        partial={**cut,'state':'Partial','completedCount':1,'changedCount':1,'unknownCount':0,'abortedCount':1,'notStartedCount':2};partial_checkpoint(partial,cut)
        for key,value in [('changedCount',0),('changedCount',True),('unknownCount',1),('completedCount',4),('notStartedCount',0),('historyVerified',False),('operationId','b'*32),('historyDigest','B'*64),('state','Completed')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):partial_checkpoint({**partial,key:value},cut)
        for key,value in [('finishedThrough',-1),('finishedThrough',1),('startedThrough',2)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):partial_checkpoint({**cut,key:value},cut)

    def test_partial_natural_lease_rejects_early_recovery_new_binding_or_lost_physical_facts(self):
        keys=('id','execution_job_id','root_started_at_utc','actor_subject','owner_hash','container_id','instance_id','relative_path','command_json','request_hash','plan_json','plan_hash','execution_hash','confirmed_at_utc','execution_expires_at_utc','login_binding')
        row={k:k for k in keys};row.update(execution_job_id='a'*32,plan_json='{}',state='Applying',publish_barrier=True,completed_at_utc=None,cancel_requested_at_utc=None,lease_token='original',lease_expires_at_utc='2026-11-04T12:01:00+00:00')
        cut=dict(operationId='a'*32,owner=row['owner_hash'],plan=dict(command=dict(aclType='access'),entryCount=4,changeCount=4,fileCount=2,directoryCount=2),historyDigest='A'*64,
            state='Applying',historyVerified=True,historyMayExist=True,finishedThrough=0,startedThrough=1)
        physical={'first':{'changed':True},'otherThree':{'changed':False}};raw=json.dumps(cut).encode();sha=hashlib.sha256(raw).hexdigest();physical_sha=inventory_digest(physical);began=dt.datetime.fromisoformat('2026-11-04T12:00:00+00:00')
        proof=dict(originalLeaseToken='original',originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],waitBeganAtUtc=began.isoformat(),waitEndedAtUtc='2026-11-04T12:01:00+00:00',monotonicWaitSeconds=60,rootSha256=sha,partialInventorySha256=physical_sha,rootStateAfterHelperExit='Applying',clocksOrSqlModified=False,
            samples=[dict(atUtc=(began+dt.timedelta(seconds=i)).isoformat(),state='Applying',barrier=True,leaseToken='original',leaseExpiresAtUtc=row['lease_expires_at_utc'],rootSha256=sha,partialInventorySha256=physical_sha) for i in range(0,60,2)])
        final=dict(plan_json='{}',completed_at_utc=None,state='Partial');observed=dict(atUtc='2026-11-04T12:01:01+00:00')
        with patch('recursive_acl_partial_restart_proof.managed_plan',return_value=cut['plan']):
            verify_partial_lease(row,final,proof,raw,cut,physical,observed)
            for key,value in [('originalLeaseToken','new'),('clocksOrSqlModified',True),('waitEndedAtUtc','2026-11-04T12:00:59+00:00'),('partialInventorySha256','0'*64),('monotonicWaitSeconds',5),('samples',[])]:
                with self.subTest(key=key),self.assertRaises(AssertionError):verify_partial_lease(row,final,{**proof,key:value},raw,cut,physical,observed)
            for sample_change in ({'barrier':False},{'leaseToken':'new'},{'partialInventorySha256':'0'*64},{'atUtc':'2026-11-04T12:01:01+00:00'}):
                bad=copy.deepcopy(proof);bad['samples'][3].update(sample_change)
                with self.subTest(change=sample_change),self.assertRaises(AssertionError):verify_partial_lease(row,final,bad,raw,cut,physical,observed)
            with self.assertRaises(AssertionError):verify_partial_lease(row,final,proof,raw,cut,physical,{'atUtc':'2026-11-04T12:00:59+00:00'})
            with self.assertRaises(AssertionError):verify_partial_lease(row,final,proof,raw,cut,{'changed':'lost'},observed)

    def test_unknown_aggregate_does_not_relabel_a_known_prior_change_as_partial_success(self):
        before=phase();r=before['row'];receipt=json.loads(r['receipt_json']);receipt.update(State='Unknown',ChangedCount=0)
        r.update(state='Unknown',root_state='Unknown',result_code='CONTAINER_RECURSIVE_ACL_RESULT_UNKNOWN',completed_count=0,changed_count=0,aborted_count=0,not_started_count=0,unknown_count=4,receipt_json=json.dumps(receipt),cancel_requested_at_utc=None)
        require_phase(before,unknown=True)
        with self.assertRaises(AssertionError):require_phase(before)
        after=acknowledgement(before);after['row']['result_code']='CONTAINER_RECURSIVE_ACL_UNKNOWN_ACKNOWLEDGED';require_ack(before,after,'different-admin',unknown=True)
        for key,value in [('completed_count',1),('unknown_count',0),('unknown_count',True),('changed_count',1),('result_code','CONTAINER_RECURSIVE_ACL_COMPLETED')]:
            bad=copy.deepcopy(before);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_phase(bad,unknown=True)
        for key,value in [('execution_job_id','replacement'),('root_started_at_utc','new-start'),('history_digest','rewritten'),('unknown_count',3),('outcome_digest','fabricated')]:
            bad=copy.deepcopy(after);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_ack(before,bad,'different-admin',unknown=True)

    def test_disappearance_refuses_foreign_fixture_or_changed_inventory_before_remove(self):
        from unittest.mock import Mock
        folder=Path('/data/WhaleDeck/apps/wdv-container-publish-'+'a'*32)
        b=dict(kind='target-removed',containerId='b'*64,source=str(folder),containerName=folder.name)
        fixture=dict(identifier='b'*64,folder=folder,remove_owned=Mock())
        require_unknown_identity(b,fixture)
        for changes in ({'identifier':'c'*64},{'folder':Path('/etc')}):
            with self.subTest(changes=changes),self.assertRaises(AssertionError):require_unknown_identity(b,{**fixture,**changes})
        held=Mock(descriptor=7)
        with self.assertRaises(AssertionError):remove_unknown_target(b,fixture,held,lambda:{'modified':True},{'modified':False},Path('/tmp'))
        fixture['remove_owned'].assert_not_called()
        with patch('recursive_acl_partial_unknown_proof.select.select',return_value=([7],[],[])),self.assertRaises(AssertionError):remove_unknown_target(b,fixture,held,lambda:{},{},Path('/tmp'))
        fixture['remove_owned'].assert_not_called()

    def test_unknown_browser_waits_for_exact_removal_then_normal_logout_before_settlement(self):
        text=(ROOT/'validate_container_recursive_acl_partial.mjs').read_text()
        self.assertIn("unknown?'结果未知，保护已保留':'部分修改，保护已保留'",text)
        self.assertLess(text.index("waitSignal(directory,'logout.json'"),text.index("phase='inflight-normal-visible-logout'"))
        self.assertIn("unknown?'logout-done.json':'action-done.json'",text)
        self.assertIn("assert.equal(final.state,expectedState)",text)
        helper=(ROOT/'recursive_acl_partial_unknown_proof.py').read_text()
        for forbidden in ('os.kill(', 'SIGKILL', 'rmtree(', 'docker rm', 'query('):self.assertNotIn(forbidden,helper)

    def test_ack_and_logout_evidence_are_separate_and_no_failure_replays_ack(self):
        text=(ROOT/'validate_container_recursive_acl_partial.mjs').read_text();helper=(ROOT/'installed_ui_logout.mjs').read_text()
        self.assertIn("'recovery-before-logout.json'",text);self.assertIn('normalDualLogoutProved:false',text)
        self.assertLess(text.index("'recovery-before-logout.json'"),text.index("phase='normal-visible-logout'",text.index("'recovery-before-logout.json'")))
        for mark in ('controls-ready','menu-opened','bff-accepted','provider-observed','sessions-cleared','login-route'):
            self.assertIn("mark('"+mark+"'",helper)
        self.assertIn("phase:'failed',atStep:phase,failureType:error?.name,bffStatus,identityStatus",helper)
        for secret in ('error.message','error.stack','response.headers()', 'cookies()', 'storageState('):self.assertNotIn(secret,helper)
        self.assertNotIn('context.request.post',helper[helper.index('}catch(error)'):])

    def test_started_marker_without_child_record_is_only_unknown_not_an_aborted_entry(self):
        record=dict(startedThrough=1,finishedThrough=0)
        self.assertTrue(entry_checkpoint(record,0,True,True));self.assertFalse(entry_checkpoint(record,1,False,True));self.assertFalse(entry_checkpoint(record,2,False,True))
        for index,present,unknown in ((0,False,True),(1,False,False),(2,True,True)):
            with self.subTest(index=index,present=present,unknown=unknown),self.assertRaises(AssertionError):entry_checkpoint(record,index,present,unknown)
        with self.assertRaises(AssertionError):entry_checkpoint(record,1,0,True)

    def test_resume_only_targets_recorded_failure_and_never_replays_original_execution(self):
        from recover_recursive_acl_partial_unknown import target,CASE
        self.assertEqual(CASE,target(CASE).name)
        for name in ('../escape','container-recursive-acl-partial-'+'a'*32):
            with self.assertRaises(AssertionError):target(name)
        text=(ROOT/'recover_recursive_acl_partial_unknown.py').read_text()
        for forbidden in ('remove_owned(', 'docker rm', "'confirm.json'", 'rmtree(', 'os.kill(', 'SIGKILL'):self.assertNotIn(forbidden,text)
        self.assertIn('originalGatePassed=False,resumedRecoveryPassed=True',text)
        self.assertIn("browser(repo,sys.argv[1],'recovery'",text)

    def instance_fixture(self):
        folder=Path('/data/WhaleDeck/apps/wdv-container-publish-'+'a'*32);cid='b'*64
        state=dict(Running=True,Pid=123,StartedAt='2026-11-04T12:00:00.100Z')
        frame=dict(Id=cid,Name='/'+folder.name,Image=owned.common.IMAGE,State=state,
            Config=dict(User='1000:1000',Labels={'io.whaledeck.validation':folder.name}),
            Mounts=[dict(Type='bind',Source=str(folder),Destination='/work',RW=True)],
            HostConfig=dict(NetworkMode='none',ReadonlyRootfs=True,Binds=None,Mounts=[dict(Type='bind',Source=str(folder),Target='/work',ReadOnly=False)],CapDrop=['ALL'],SecurityOpt=['no-new-privileges'],Privileged=False,PidMode='',IpcMode='private',PidsLimit=16,Memory=32*1024**2,NanoCpus=250000000,RestartPolicy=dict(Name='no',MaximumRetryCount=0)))
        b=dict(kind='instance-changed',containerId=cid,containerName=folder.name,containerPid=123,source=str(folder),instanceId='123:'+state['StartedAt'])
        after=copy.deepcopy(frame);after['State'].update(Pid=456,StartedAt='2026-11-04T12:00:02.100Z');physical={'firstKnownChanged':True}
        from recursive_acl_restart_proof import RESTART_KEYS
        proof=dict(before=frame,after=after,oldInstanceId=b['instanceId'],newInstanceId='456:'+after['State']['StartedAt'],commandAcknowledgement=cid,restartAttemptedOnce=True,helperAliveAfterRestart=True,
            sourceInventory=physical,newInstanceInventory=physical,coreBefore={'core':'unchanged'},coreAfter={'core':'unchanged'},otherActiveWork={k:0 for k in RESTART_KEYS})
        return b,proof,physical

    def test_instance_transition_requires_real_new_instance_and_exact_original_resource(self):
        b,p,physical=self.instance_fixture();require_instance_transition(b,p,physical)
        for change in (lambda v:v['after']['State'].update(Pid=123),lambda v:v['after']['State'].update(StartedAt=v['before']['State']['StartedAt']),lambda v:v['after'].update(Id='c'*64),lambda v:v['after'].update(Image='foreign-image'),lambda v:v['after']['HostConfig'].update(Privileged=True),lambda v:v['after']['HostConfig'].update(NetworkMode='host'),lambda v:v['after']['HostConfig']['Mounts'][0].update(Source='/etc'),lambda v:v['after']['Config'].update(User='0:0'),lambda v:v.update(newInstanceInventory={'firstKnownChanged':False}),lambda v:v.update(coreAfter={'core':'restarted'}),lambda v:v.update(commandAcknowledgement='foreign'),lambda v:v.update(restartAttemptedOnce=False),lambda v:v['otherActiveWork'].update(acls=True)):
            bad=copy.deepcopy(p);change(bad)
            with self.subTest(proof=bad),self.assertRaises(AssertionError):require_instance_transition(b,bad,physical)

    def test_instance_guard_refuses_foreign_fixture_or_busy_work_before_any_command(self):
        from unittest.mock import Mock
        b,p,physical=self.instance_fixture();fixture=dict(identifier=b['containerId'],folder=Path(b['source']));held=Mock(descriptor=7)
        with patch('recursive_acl_instance_proof.command') as command,patch('recursive_acl_instance_proof.select.select',return_value=([],[],[])),patch('recursive_acl_instance_proof.restart_allowed',side_effect=AssertionError('OTHER_FILE_WORK_RESTART_REFUSED')):
            with self.assertRaises(AssertionError):prepare_instance(b,fixture,held,{},lambda:physical,physical)
            command.assert_not_called()
        with patch('recursive_acl_instance_proof.command') as command,self.assertRaises(AssertionError):prepare_instance(b,{**fixture,'identifier':'c'*64},held,{},lambda:physical,physical)
        command.assert_not_called()
        with patch('recursive_acl_instance_proof.command') as command,self.assertRaises(AssertionError):restart_instance(b,{**fixture,'instanceRestartAttempted':True},held,p,lambda:physical,physical,lambda pid:physical,Path('/tmp'))
        command.assert_not_called()

    def test_instance_unit_exception_requires_own_cgroup_pid_and_invocation(self):
        import os
        unit='wd-qa-recursive-acl-partial-instance-123abcd.service';group='/system.slice/'+unit;inv='c'*32;helper='whaledeck-container-recursive-acl-'+'a'*32+'.service'
        require_only_transients([unit,helper],helper,unit)
        props=f'InvocationID={inv}\nMainPID={os.getpid()}\nControlGroup={group}'
        with patch('recursive_acl_restart_proof.Path.read_text',return_value='0::'+group),patch.dict(os.environ,{'INVOCATION_ID':inv}),patch('recursive_acl_restart_proof.command',return_value=props):self.assertEqual(unit,owner_unit())
        with patch('recursive_acl_restart_proof.Path.read_text',return_value='0::'+group),patch.dict(os.environ,{'INVOCATION_ID':'d'*32}),patch('recursive_acl_restart_proof.command',return_value=props),self.assertRaises(AssertionError):owner_unit()
        for owner in ('wd-qa-recursive-acl-partial-instance-all.service','wd-qa-recursive-acl-partial-instance-123abcd-extra.service'):
            with self.assertRaises(AssertionError):require_only_transients([],helper,owner)
        with self.assertRaises(AssertionError):require_only_transients([unit,helper,'wd-qa-foreign.service'],helper,unit)

    def test_instance_frame_excludes_environment_and_independent_evidence_requires_after_ack(self):
        b,p,physical=self.instance_fixture();frame=copy.deepcopy(p['before']);frame['Config'].update(Env=['PRIVATE_VALUE'],Labels={**frame['Config']['Labels'],'foreign':'PRIVATE_VALUE'})
        safe=instance_frame(frame);self.assertNotIn('PRIVATE_VALUE',json.dumps(safe))
        text=(ROOT/'recursive_acl_instance_proof.py').read_text()
        self.assertIn('at>=ended',text);self.assertIn("actual==proof['after']",text)
        for forbidden in ('os.kill(', 'SIGKILL', 'rmtree(', "'systemctl'", 'query('):self.assertNotIn(forbidden,text)

    def cross_fixture(self):
        began=dt.datetime.fromisoformat('2026-11-04T12:00:00+00:00');identifier='22222222-2222-4222-8222-222222222222';key='11111111-1111-4111-8111-111111111111'
        b=dict(containerId='a'*64,instanceId='123:instance',path=WAITING_PATH)
        command=dict(containerId=b['containerId'],instanceId=b['instanceId'],path=WAITING_PATH,mode='0750')
        rp=dict(command=command,uid=1000,gid=1000,parentVersion='A'*64,targetProof='B'*64)
        rp['fingerprint']=hashlib.sha256(b'whaledeck-container-directory-plan-v1\n'+json.dumps(rp,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest().upper()
        plan=dict(Fingerprint=rp['fingerprint'],TargetProof=rp['targetProof'],ParentVersion=rp['parentVersion'])
        row={k:k for k in STABLE};row.update(id=identifier,idempotency_key=key,actor_subject='different-admin',owner_hash='D'*64,container_id=b['containerId'],instance_id=b['instanceId'],relative_path=WAITING_PATH,plan_hash='C'*64,plan_json=json.dumps(plan),state='Pending',root_started_at_utc=None,completed_at_utc=None,lease_token=None,lease_expires_at_utc=None,publish_barrier=False,version=2,next_attempt_at_utc=(began+dt.timedelta(seconds=6)).isoformat())
        initial=dict(row=row,job=dict(id=identifier,state='Queued'),accepted=1,terminalAudits=0,pending=1,events=1)
        reg=dict(id=identifier,subject='different-admin',intent=dict(id=identifier,jobId=identifier,command=dict(location=dict(containerId=b['containerId'],instanceId=b['instanceId'],path=WAITING_PATH),mode='0750')),plans=1,registrations=1,cancellations=0,uploads=0,originAndIdempotencyObserved=True,registration=dict(planTokenSha256='C'*64,confirmed=True,idempotencyKey=key))
        raw=b'unchanged Unknown root';sha=hashlib.sha256(raw).hexdigest();samples=[]
        for i in range(4):
            data=copy.deepcopy(initial);data['row'].update(version=2+2*i,next_attempt_at_utc=(began+dt.timedelta(seconds=6*i+6)).isoformat())
            samples.append(dict(observedAtUtc=(began+dt.timedelta(seconds=6*i)).isoformat(),sql=data,rootAbsent=True,targetAbsent=True,recursiveUnknownProtected=True,originalPhysicalUnchanged=True,unknownRootSha256=sha))
        final=copy.deepcopy(initial);final['row'].update(state='Succeeded',root_started_at_utc=(began+dt.timedelta(seconds=26)).isoformat(),completed_at_utc=(began+dt.timedelta(seconds=27)).isoformat(),result_code='CONTAINER_DIRECTORY_CREATED',published_version='E'*64,version=10)
        final.update(job=dict(id=identifier,state='Succeeded'),terminalAudits=1,pending=0,events=3)
        receipt=dict(state='Completed',operationId=identifier.replace('-',''),owner='D'*64,version='E'*64,plan=rp)
        return b,initial,reg,samples,final,receipt,began+dt.timedelta(seconds=25),raw

    def test_cross_directory_is_explicit_and_rejects_unrelated_fault_flags(self):
        for args in ([],['logout'],['target-removed']):self.assertFalse(selected_waiting(args))
        self.assertTrue(selected_waiting(['target-removed','cross-directory']))
        for args in (['cancel','cross-directory'],['instance-changed','cross-directory'],['target-removed','--all'],['target-removed','cross-directory','extra']):
            with self.subTest(args=args),self.assertRaises(AssertionError):selected_waiting(args)

    def test_waiting_registration_binds_token_digest_and_does_not_store_raw_plan_token(self):
        b,initial,reg,*_=self.cross_fixture();require_registration(reg,initial,b)
        for change in ({'planTokenSha256':'raw signed plan'},{'confirmed':False},{'idempotencyKey':'33333333-3333-4333-8333-333333333333'},{'planHash':'raw signed plan'}):
            bad=copy.deepcopy(reg);bad['registration'].update(change)
            with self.subTest(change=change),self.assertRaises(AssertionError):require_registration(bad,initial,b)
        bad=copy.deepcopy(initial);bad['job']['id']='another-job'
        with self.assertRaises(AssertionError):require_registration(reg,bad,b)
        text=(ROOT/'recursive_acl_cross_directory.mjs').read_text();self.assertIn("createHash('sha256').update(post.planHash",text);self.assertNotIn('registration:response.request().postDataJSON()',text)

    def test_four_natural_deferrals_cannot_repeat_one_row_or_release_barrier_early(self):
        b,initial,reg,samples,_,_,released,raw=self.cross_fixture();require_deferrals(samples,b,reg,initial,released,raw)
        for change in (lambda s:s[1]['sql']['row'].update(version=2),lambda s:s[1]['sql']['row'].update(next_attempt_at_utc=s[0]['sql']['row']['next_attempt_at_utc']),lambda s:s[1]['sql']['row'].update(lease_token='live'),lambda s:s[1]['sql']['row'].update(root_started_at_utc='2026-11-04T12:00:06+00:00'),lambda s:s[1].update(rootAbsent=False),lambda s:s[1].update(recursiveUnknownProtected=False),lambda s:s[1].update(unknownRootSha256='0'*64),lambda s:s[1]['sql']['row'].update(actor_subject='other-admin'),lambda s:s[1]['sql']['row'].update(plan_json='new-plan')):
            bad=copy.deepcopy(samples);change(bad)
            with self.subTest(samples=bad),self.assertRaises(AssertionError):require_deferrals(bad,b,reg,initial,released,raw)
        with self.assertRaises(AssertionError):require_deferrals(samples,b,reg,initial,dt.datetime.fromisoformat(samples[-1]['observedAtUtc']),raw)

    def test_waiting_completion_requires_original_job_root_and_time_after_ack(self):
        b,initial,reg,_,final,receipt,released,_=self.cross_fixture();require_completed(final,initial,b,reg,released,receipt)
        for key,value in [('id','new-id'),('actor_subject','other'),('published_version','bad'),('root_started_at_utc','2026-11-04T12:00:20+00:00'),('publish_barrier',True),('request_hash','new-hash')]:
            bad=copy.deepcopy(final);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_completed(bad,initial,b,reg,released,receipt)
        for key,value in [('state','Aborted'),('operationId','0'*32),('owner','F'*64),('version','F'*64)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_completed(final,initial,b,reg,released,{**receipt,key:value})

    def test_cross_directory_cleanup_refuses_foreign_path_before_reading_or_removing(self):
        with patch('recursive_acl_cross_directory_proof.trusted') as trusted,self.assertRaises(AssertionError):clean_waiting((Path('/etc/foreign'),(),()))
        trusted.assert_not_called()

    def test_cross_directory_browser_uses_visible_controls_without_mutating_test_interfaces(self):
        text=(ROOT/'recursive_acl_cross_directory.mjs').read_text()
        for value in ('新建目录','生成目录计划','确认创建目录','waiting-pending.png','waiting-succeeded.png','originalTaskVisibleAfterRelease:true'):self.assertIn(value,text)
        for value in ('page.route(', 'context.route(', 'request.post(', 'addCookies(', 'sessionStorage.setItem','localStorage.setItem'):self.assertNotIn(value,text)
        proof=(ROOT/'recursive_acl_cross_directory_proof.py').read_text()
        for value in ('rmtree(', 'os.kill(', 'SIGKILL', 'UPDATE ', 'DELETE ', 'time.time='):self.assertNotIn(value,proof)

    def test_transient_claim_lease_is_not_counted_as_a_natural_deferral(self):
        b,initial,reg,*_=self.cross_fixture();claimed=copy.deepcopy(initial);claimed['row'].update(lease_token='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',lease_expires_at_utc='2026-11-04T12:01:30+00:00')
        self.assertFalse(require_blocked(claimed,b,reg['subject'],dt.datetime.fromisoformat('2026-11-04T12:00:00+00:00')))
        claimed['row']['lease_expires_at_utc']=None
        with self.assertRaises(AssertionError):require_blocked(claimed,b,reg['subject'],dt.datetime.fromisoformat('2026-11-04T12:00:00+00:00'))

    def test_cross_failure_resolution_only_accepts_exact_recorded_case(self):
        from recover_recursive_acl_cross_directory import target,CASE
        self.assertEqual(CASE,target(CASE).name)
        for name in ('../escape','container-recursive-acl-partial-'+'a'*32):
            with self.assertRaises(AssertionError):target(name)
        text=(ROOT/'recover_recursive_acl_cross_directory.py').read_text()
        self.assertIn('originalGatePassed=False',text);self.assertIn('crossFenceAcceptance=False',text)
        for value in ('remove_owned(', 'rmtree(', 'os.kill(', 'UPDATE ', 'DELETE '):self.assertNotIn(value,text)

    def test_partial_revocation_preserves_the_real_result_and_denies_old_context(self):
        subject='11111111-1111-4111-8111-111111111111';p=phase();p['row'].update(actor_subject=subject,cancel_requested_at_utc=None);p['executionJob']['cancel_requested_at_utc']=None
        before=dict(pk=1,uuid=subject,username='whaledeck-upload-qa-'+'a'*12,is_active=True,groups=['admin'])
        for kind,changes in [('group-removal',dict(groups=[])),('account-disable',dict(is_active=False))]:
            authority=dict(kind=kind,subject=subject,before=before,after={**before,**changes},onlyOwnedIdentityChanged=True,permissionExpiresAtUtc='2026-11-04T14:00:00+00:00',confirmedAtUtc='2026-11-04T12:00:00+00:00')
            ui=dict(kind=kind,subject=subject,normalDualLogout=False,logout=None,registrations=1,confirmations=1,cancellations=0,acknowledgements=0,uploads=0,result=None,hintsCleared=True,authorityRevoked=True,anonymousGet401=False,originalIntentStatus=403,
                privatePanelsAbsent=True,currentIdentityStatus=200 if kind=='group-removal' else 403,currentlyAdministrator=False if kind=='group-removal' else None,currentSubject=subject if kind=='group-removal' else None,renderedRevocationPage='forbidden' if kind=='group-removal' else 'login')
            require_case(kind,p,ui,authority)
            for key,value in [('normalDualLogout',True),('cancellations',1),('result',{'state':'Canceled'}),('originalIntentStatus',200),('hintsCleared',False),('authorityRevoked',False),('anonymousGet401',True)]:
                with self.subTest(kind=kind,key=key),self.assertRaises(AssertionError):require_case(kind,p,{**ui,key:value},authority)
            bad=copy.deepcopy(authority);bad['after']['pk']=2
            with self.assertRaises(AssertionError):require_case(kind,p,ui,bad)
            with self.assertRaises(AssertionError):require_case(kind,p,ui,{**authority,'permissionExpiresAtUtc':'2026-11-04T12:59:59+00:00'})

    def test_normal_partial_logout_is_not_cancel_revoke_expiry_or_fabricated_private_get(self):
        subject='11111111-1111-4111-8111-111111111111';p=phase();p['row'].update(actor_subject=subject,cancel_requested_at_utc=None);p['executionJob']['cancel_requested_at_utc']=None
        identity=dict(pk=1,uuid=subject,username='whaledeck-upload-qa-'+'a'*12,is_active=True,groups=['admin'])
        authority=dict(kind='logout',subject=subject,before=identity,after=copy.deepcopy(identity),noIdentityMutation=True,permissionExpiresAtUtc='2026-11-04T14:00:00+00:00',confirmedAtUtc='2026-11-04T12:00:00+00:00')
        ui=dict(subject=subject,normalDualLogout=True,registrations=1,confirmations=1,cancellations=0,acknowledgements=0,uploads=0,result=None,hintsCleared=True)
        require_case('logout',p,ui,authority)
        for key,value in [('normalDualLogout',False),('cancellations',1),('result',{'state':'Partial'}),('hintsCleared',False),('subject','other')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_case('logout',p,{**ui,key:value},authority)
        for changed in ({'groups':[]},{'is_active':False},{'pk':2}):
            bad=copy.deepcopy(authority);bad['after'].update(changed)
            with self.subTest(changed=changed),self.assertRaises(AssertionError):require_case('logout',p,ui,bad)
        with self.assertRaises(AssertionError):require_case('logout',p,ui,{**authority,'permissionExpiresAtUtc':'2026-11-04T12:59:59+00:00'})
        p['row']['cancel_requested_at_utc']='cancel'
        with self.assertRaises(AssertionError):require_case('logout',p,ui,authority)

    def test_partial_cannot_finish_job_release_barrier_or_assert_zero_changes(self):
        original=phase();require_phase(original)
        for key,value in [('publish_barrier',False),('completed_at_utc','early'),('root_state','Aborted'),('changed_count',0),('changed_count',True),('unknown_count',1),('history_verified',False),('lease_token','live'),('acknowledged_by','somebody')]:
            bad=copy.deepcopy(original);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_phase(bad)
        bad=copy.deepcopy(original);bad['executionJob']['state']='Canceled'
        with self.assertRaises(AssertionError):require_phase(bad)

    def test_ack_releases_only_after_different_admin_preserves_real_counts(self):
        before=phase();after=acknowledgement(before);require_ack(before,after,'different-admin')
        for key,value in [('actor_subject','rewritten'),('execution_job_id','new-operation'),('root_started_at_utc','new-start'),('plan_json','new-plan'),('changed_count',0),('publish_barrier',True),('acknowledged_by','original-owner')]:
            bad=copy.deepcopy(after);bad['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_ack(before,bad,'different-admin')
        bad=copy.deepcopy(after);receipt=json.loads(bad['row']['receipt_json']);receipt['ChangedCount']=0;bad['row']['receipt_json']=json.dumps(receipt);bad['row']['acknowledgement_json']=json.dumps(receipt)
        with self.assertRaises(AssertionError):require_ack(before,bad,'different-admin')
        with self.assertRaises(AssertionError):require_ack(before,after,before['row']['actor_subject'])

    def test_artefacts_and_queries_cannot_select_foreign_paths_or_mutate_sql(self):
        for name in ('../escape','container-recursive-acl-faults-'+'a'*32,'container-recursive-acl-partial-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)
        for name in ('validate_container_recursive_acl_partial.py','verify_container_recursive_acl_partial.py','recursive_acl_partial_proof.py'):
            text=(ROOT/name).read_text();self.assertNotIn('rmtree(',text);self.assertNotIn('os.kill(',text)
            for node in ast.walk(ast.parse(text)):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);self.assertTrue(node.args[0].value.startswith('SELECT '))

    def test_browser_only_real_controls_with_stdin_and_no_implicit_ack_on_failure(self):
        text=(ROOT/'validate_container_recursive_acl_partial.mjs').read_text()
        for forbidden in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem','localStorage.setItem'):
            self.assertNotIn(forbidden,text)
        for expected in ('loginVisible','logoutFromUserMenu',"'/dev/stdin'","'暂不处置'","'确认处置并释放保护'",'assert.notEqual(profile.subject,f.originalSubject)'):
            self.assertIn(expected,text)
        catch=text[text.index('}catch(error)'):];self.assertNotIn('context.request.post',catch)


if __name__=='__main__':unittest.main()
