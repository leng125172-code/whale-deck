"""Partial protection and truthful explicit acknowledgement boundaries only."""
import ast,copy,json
import datetime as dt
import hashlib
from unittest.mock import patch
from recursive_acl_partial_restart_proof import partial_checkpoint,inventory_digest,verify_partial_lease
from recursive_acl_restart_proof import require_only_transients,owner_unit
from recursive_acl_partial_unknown_proof import require_unknown_identity,remove_unknown_target
from pathlib import Path
import unittest
from recursive_acl_partial_proof import require_phase,require_ack,require_case
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
