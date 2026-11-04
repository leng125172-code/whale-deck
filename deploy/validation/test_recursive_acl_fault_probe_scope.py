"""Offline strict cut/selector checks. No live service, identity or file access."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

from bound_container_recursive_acl_cut import child_id, first_entry_boundary, freeze_before_first_acl
from validate_container_recursive_acl_faults import selected_kinds, verify_revoked_browser, require_authority_change
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
        for args in (['restart'],['cancel','logout'],['logout','--all'],['post-acl-cancel']):
            with self.subTest(args=args),self.assertRaises(AssertionError):selected_kinds(args)
        for name in ('../escape','container-recursive-acl-browser-'+'a'*32,'container-recursive-acl-faults-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)

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
