"""Offline strict cut/selector checks. No live service, identity or file access."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

from bound_container_recursive_acl_cut import child_id, first_entry_boundary, freeze_before_first_acl
from validate_container_recursive_acl_faults import selected_kinds, terminal_state, verify_revoked_browser, require_authority_change
from recursive_acl_restart_proof import require_only_transients, owner_unit, restart_allowed, lease_held, verify_lease, RESTART_KEYS
from verify_container_recursive_acl_faults import target

ROOT=Path(__file__).resolve().parent


def record():
    return dict(state='Applying',startedThrough=0,finishedThrough=-1,historyVerified=True,historyMayExist=True,historyDigest='A'*64,
        plan=dict(command=dict(aclType='access'),entryCount=4,changeCount=4,fileCount=2,directoryCount=2))


class RecursiveAclFaultProbeScope(unittest.TestCase):
    def test_cut_requires_first_entry_before_any_completion_and_verified_hdd(self):
        self.assertTrue(first_entry_boundary(record()))
        r=record();r['startedThrough']=-1;self.assertTrue(first_entry_boundary(r))
        for key,value in [('state','Preparing'),('state','Completed'),('startedThrough',1),('finishedThrough',0),('historyVerified',False),('historyMayExist',False),('historyDigest','a'*64),('historyDigest','')]:
            r=record();r[key]=value
            with self.subTest(key=key,value=value):self.assertFalse(first_entry_boundary(r))
        for key in ('startedThrough','finishedThrough'):
            r=record();r[key]=False
            with self.subTest(key=key),self.assertRaises(AssertionError):first_entry_boundary(r)
        for key,value in [('entryCount',5),('changeCount',3),('fileCount',3),('directoryCount',1)]:
            r=record();r['plan'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):first_entry_boundary(r)
        r=record();r['plan']['command']['aclType']='default'
        with self.assertRaises(AssertionError):first_entry_boundary(r)

    def test_child_domain_and_range_cannot_match_chmod_or_ownership(self):
        import hashlib
        for i in range(4):
            value=child_id('a'*32,i);self.assertEqual(32,len(value))
            for name in ('permission','ownership'):
                self.assertNotEqual(value,hashlib.sha256(('whaledeck-recursive-'+name+'-entry-id-v1:'+'a'*32+':'+str(i)).encode()).hexdigest()[:32])
        for operation,index in [('A'*32,0),('../x',0),('a'*32,False),('a'*32,-1),('a'*32,4)]:
            with self.subTest(operation=operation,index=index),self.assertRaises(AssertionError):child_id(operation,index)

    def test_selector_and_archive_reject_arbitrary_inputs(self):
        self.assertEqual(('cancel','logout'),selected_kinds([]));self.assertEqual(('cancel',),selected_kinds(['cancel']))
        self.assertEqual(('group-removal','account-disable'),selected_kinds(['revocation']))
        self.assertEqual(('group-removal',),selected_kinds(['group-removal']))
        self.assertEqual(('account-disable',),selected_kinds(['account-disable']))
        self.assertEqual(('worker-crash','agent-restart'),selected_kinds(['restart']))
        self.assertEqual('Failed',terminal_state('worker-crash'));self.assertEqual('Failed',terminal_state('agent-restart'))
        for args in (['restart','--all'],['cancel','logout'],['logout','--all'],['post-acl-cancel']):
            with self.subTest(args=args),self.assertRaises(AssertionError):selected_kinds(args)
        for name in ('../escape','container-recursive-acl-browser-'+'a'*32,'container-recursive-acl-faults-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)

    def test_transient_restart_exception_is_only_exact_helper_and_owner(self):
        helper='whaledeck-container-recursive-acl-'+'a'*32+'.service';owner='wd-qa-recursive-acl-restarts-'+'b'*7+'.service'
        require_only_transients([helper,owner],helper,owner)
        for other in ('whaledeck-dev-publish-abcdef0.service','wd-qa-another.service','whaledeck-container-acl-'+'c'*32+'.service'):
            with self.subTest(other=other),self.assertRaises(AssertionError):require_only_transients([helper,owner,other],helper,owner)
        with self.assertRaises(AssertionError):require_only_transients([],helper,'arbitrary.service')

    def test_owner_exception_requires_current_pid_cgroup_and_invocation(self):
        import os
        owner='wd-qa-recursive-acl-restarts-'+'b'*7+'.service';group='/system.slice/'+owner;inv='a'*32
        prefix='recursive_acl_restart_proof.'
        props=f'InvocationID={inv}\nMainPID={os.getpid()}\nControlGroup={group}'
        with patch(prefix+'Path.read_text',return_value='0::'+group+'\n'),patch.dict(os.environ,{'INVOCATION_ID':inv}),patch(prefix+'command',return_value=props):self.assertEqual(owner,owner_unit())
        for bad in (props.replace(inv,'c'*32),props.replace(f'MainPID={os.getpid()}','MainPID=1'),props.replace('ControlGroup='+group,'ControlGroup=/other')):
            with patch(prefix+'Path.read_text',return_value='0::'+group),patch.dict(os.environ,{'INVOCATION_ID':inv}),patch(prefix+'command',return_value=bad),self.assertRaises(AssertionError):owner_unit()

    def test_all_ten_file_workflows_block_restart_before_service_or_pid_access(self):
        from unittest.mock import Mock
        prefix='recursive_acl_restart_proof.';values={k:0 for k in RESTART_KEYS};held=Mock(cgroup='whaledeck-container-recursive-acl-'+'a'*32+'.service')
        for key in RESTART_KEYS:
            for bad in (1,True):
                with self.subTest(key=key,bad=bad),patch(prefix+'preflight',return_value={}),patch(prefix+'query',return_value={**values,key:bad}),patch(prefix+'owner_unit') as owner,self.assertRaises(AssertionError):restart_allowed(held,dict(id='original',execution_job_id='execution'))
                owner.assert_not_called()
        sql=(ROOT/'recursive_acl_restart_proof.py').read_text()
        for table in ('container_file_upload_operations','container_directory_operations','container_copy_intents','container_move_intents','container_permission_intents','container_ownership_intents','container_recursive_permission_intents','container_recursive_ownership_intents','container_acl_intents','container_recursive_acl_intents'):self.assertIn(table,sql)

    def test_natural_lease_cannot_release_barrier_change_binding_or_rewrite_clock(self):
        import datetime as dt,hashlib,json
        keys=('id','execution_job_id','root_started_at_utc','actor_subject','owner_hash','container_id','instance_id','relative_path','command_json','request_hash','plan_json','plan_hash','execution_hash','confirmed_at_utc','execution_expires_at_utc','login_binding')
        row={k:k for k in keys};row.update(state='Applying',publish_barrier=True,completed_at_utc=None,cancel_requested_at_utc=None,lease_token='original-lease',lease_expires_at_utc='2026-11-04T12:01:00+00:00')
        lease_held(row,copy.deepcopy(row))
        for key,value in [('login_binding','another-login'),('plan_json','new-plan'),('execution_job_id','new-job'),('root_started_at_utc','new-start'),('lease_token','new-lease'),('publish_barrier',False),('completed_at_utc','early'),('cancel_requested_at_utc','cancel')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):lease_held(row,{**row,key:value})
        raw=json.dumps(dict(state='Applying',historyVerified=True,changedCount=0)).encode();sha=hashlib.sha256(raw).hexdigest();began=dt.datetime.fromisoformat('2026-11-04T12:00:00+00:00')
        proof=dict(originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],waitBeganAtUtc=began.isoformat(),waitEndedAtUtc='2026-11-04T12:01:00+00:00',monotonicWaitSeconds=60,rootSha256=sha,rootStateAfterHelperExit='Applying',clocksOrSqlModified=False,
            samples=[dict(atUtc=(began+dt.timedelta(seconds=i)).isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],rootSha256=sha,sourceFullBytesAclsAndAttributesUnchanged=True) for i in range(0,60,2)])
        final=dict(completed_at_utc='2026-11-04T12:01:01+00:00');verify_lease(row,final,proof,raw)
        for key,value in [('clocksOrSqlModified',True),('originalLeaseToken','different'),('rootSha256','0'*64),('waitEndedAtUtc','2026-11-04T12:00:59+00:00'),('monotonicWaitSeconds',10),('samples',[])]:
            bad={**proof,key:value}
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_lease(row,final,bad,raw)
        bad=copy.deepcopy(proof);bad['samples'][3]['sourceFullBytesAclsAndAttributesUnchanged']=False
        with self.assertRaises(AssertionError):verify_lease(row,final,bad,raw)
        with self.assertRaises(AssertionError):verify_lease(row,dict(completed_at_utc='2026-11-04T12:00:59+00:00'),proof,raw)

    def test_revoked_browser_cannot_be_normal_logout_expiry_or_other_identity(self):
        subject='11111111-1111-4111-8111-111111111111'
        for kind in ('group-removal','account-disable'):
            ui=dict(kind=kind,subject=subject,authorityRevoked=True,normalDualLogout=False,logout=None,anonymousGet401=False,
                originalIntentStatus=403,hintsCleared=True,privatePanelsAbsent=True,cancellations=0,uploads=0,
                currentIdentityStatus=200 if kind=='group-removal' else 403,currentlyAdministrator=False if kind=='group-removal' else None,
                currentSubject=subject if kind=='group-removal' else None,renderedRevocationPage='forbidden' if kind=='group-removal' else 'login')
            verify_revoked_browser(ui,kind,subject)
            for field,value in [('normalDualLogout',True),('logout',{}),('anonymousGet401',True),('authorityRevoked',False),('originalIntentStatus',200),('hintsCleared',False),('privatePanelsAbsent',False),('cancellations',1),('uploads',1),('currentIdentityStatus',200 if kind=='account-disable' else 403),('currentlyAdministrator',True),('currentSubject','22222222-2222-4222-8222-222222222222'),('renderedRevocationPage','overview')]:
                bad={**ui,field:value}
                with self.subTest(kind=kind,field=field),self.assertRaises(AssertionError):verify_revoked_browser(bad,kind,subject)

    def test_positive_authority_outlives_the_exact_owned_patch(self):
        subject='11111111-1111-4111-8111-111111111111'
        before=dict(pk=1,uuid=subject,username='whaledeck-upload-qa-'+'a'*12,is_active=True,groups=['admin'])
        ready=dict(subject=subject,permissionExpiresAtUtc='2026-11-04T12:00:00+00:00')
        for kind,changes in [('group-removal',dict(groups=[])),('account-disable',dict(is_active=False))]:
            proof=dict(kind=kind,subject=subject,before=before,after={**before,**changes},onlyOwnedIdentityChanged=True,confirmedAtUtc='2026-11-04T10:00:00+00:00')
            require_authority_change(proof,ready,kind)
            for change in ({'pk':2},{'username':'real-admin'},{'uuid':'22222222-2222-4222-8222-222222222222'}):
                bad=copy.deepcopy(proof);bad['after'].update(change)
                with self.subTest(kind=kind,change=change),self.assertRaises(AssertionError):require_authority_change(bad,ready,kind)
            with self.assertRaises(AssertionError):require_authority_change(proof,{**ready,'permissionExpiresAtUtc':'2026-11-04T10:59:59+00:00'},kind)

    def test_arbitrary_host_source_refused_before_any_proc_or_signal(self):
        row=dict(container_id='a'*64,instance_id='123:instance')
        with patch('bound_container_recursive_acl_cut.os.geteuid',return_value=0),patch('bound_container_recursive_acl_cut.os.pidfd_open') as pidfd:
            for source in ('/etc','/data/WhaleDeck/apps/not-owned','/data/WhaleDeck/apps/wdv-container-publish-'+'A'*32):
                with self.subTest(source=source),self.assertRaises(AssertionError):freeze_before_first_acl(row,source,123,{},lambda:{})
            pidfd.assert_not_called()

    def test_browser_only_uses_product_controls_and_stdin_credentials(self):
        s=(ROOT/'validate_container_recursive_acl_faults.mjs').read_text();p=(ROOT/'validate_container_recursive_acl_faults.py').read_text()
        for forbidden in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem','localStorage.setItem'):
            self.assertNotIn(forbidden,s)
        for required in ('loginVisible','logoutFromUserMenu',"'继续等待'","'确认请求取消'","'/dev/stdin'","'normal-visible-logout'"):
            self.assertIn(required,s)
        self.assertNotIn("'VALIDATION_PASSWORD':",p);self.assertIn("environment.pop('VALIDATION_PASSWORD',None)",p)
        self.assertIn("if proved:remove_proved",p)

    def test_no_production_sql_mutation_or_generic_process_kill(self):
        for filename in ('validate_container_recursive_acl_faults.py','verify_container_recursive_acl_faults.py','bound_container_recursive_acl_cut.py'):
            s=(ROOT/filename).read_text();tree=ast.parse(s)
            self.assertNotIn('SIGKILL',s);self.assertNotIn('os.kill(',s);self.assertNotIn('rmtree(',s)
            for node in ast.walk(tree):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);self.assertTrue(node.args[0].value.lstrip().startswith('SELECT '))


if __name__=='__main__':unittest.main()
