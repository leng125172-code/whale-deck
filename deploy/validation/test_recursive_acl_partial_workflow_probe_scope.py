"""Partial protection and truthful explicit acknowledgement boundaries only."""
import ast,copy,json
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
        self.assertEqual('group-removal',selected_case(['group-removal']));self.assertEqual('account-disable',selected_case(['account-disable']))
        for values in (['cancel'],['logout','cancel'],['group-removal','account-disable'],['--all']):
            with self.subTest(values=values),self.assertRaises(AssertionError):selected_case(values)

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
