"""Safety/serialization proofs for the real recursive ACL browser gate.

No dependencies, identities, signed RPC or real file tree are accessed here.
"""
import ast
import copy
from pathlib import Path
import tempfile
import unittest
from validate_container_recursive_acl_browser import managed_entries,managed_plan,snapshot,archive,wait_case,CASES,CHANGES,expected_targets,selected_cases
from verify_container_recursive_acl_browser import verify
from recover_container_recursive_acl_browser import recover
from validate_recursive_acl_auth_only import target as auth_only_target

ROOT=Path(__file__).resolve().parent

def target():return [{'Tag':'user_obj','Id':None,'Permissions':6},{'Tag':'user','Id':0,'Permissions':0},{'Tag':'group_obj','Id':None,'Permissions':0},{'Tag':'mask','Id':None,'Permissions':4},{'Tag':'other','Id':None,'Permissions':0}]

def plan(kind='access'):
    return dict(SchemaVersion=1,Command=dict(Location=dict(ContainerId='a'*64,InstanceId='777:2026-11-04T01:02:03Z',Path='work/中文'),AclType=kind,FileEntries=target() if kind=='access' else None,DirectoryEntries=target(),ExpectedVersion='A'*64),
        RootIdentity=dict(Device=18446744073709551615,Inode=18446744073709551614,Mode=0o40750,OwnerUid=2100,OwnerGid=3100,LinkCount=2,SizeBytes=4096,ModifiedAtUnixNs=1700000000123456789),RootChangedAtUnixNs=1700000000987654321,
        ParentIdentity=dict(Device=18446744073709551615,Inode=33),TargetProof='B'*64,ParentVersion='C'*64,OwnerUid=1000,OwnerGid=1000,Fingerprint='D'*64,ManifestDigest='E'*64,EntryCount=4,FileCount=2,DirectoryCount=2,ChangeCount=2)

class RecursiveAclBrowserProbeScopeTests(unittest.TestCase):
    def test_cancel_only_is_bounded_and_cannot_repeat_prior_write_cases(self):
        self.assertEqual(('cancel',),selected_cases(['--cancel-only']))
        self.assertEqual(CASES[5:],selected_cases(['--remaining-defaults']))
        self.assertEqual(CASES,selected_cases([]))
        for arguments in (['--cancel-only','--confirm'],['--access'],['../escape'],['--cancel-only;echo']):
            with self.subTest(arguments=arguments),self.assertRaises(AssertionError):selected_cases(arguments)

    def test_plan_preserves_full_mode_64bit_identity_zero_entries_and_separate_targets(self):
        source=plan();out=managed_plan(source)
        self.assertEqual({'schemaVersion','command','targetProof','parentIdentity','parentVersion','uid','gid','fingerprint','rootIdentity','manifestDigest','entryCount','fileCount','directoryCount','changeCount'},set(out))
        self.assertEqual('E'*64,out['manifestDigest'])
        self.assertEqual([18446744073709551615,18446744073709551614],out['rootIdentity'][:2]);self.assertEqual(0o40750,out['rootIdentity'][2])
        self.assertEqual(1700000000123456789,out['rootIdentity'][7]);self.assertEqual(1700000000987654321,out['rootIdentity'][8])
        self.assertEqual({'tag':'user','id':0,'permissions':0},out['command']['fileEntries'][1]);self.assertIsNot(out['command']['fileEntries'],out['command']['directoryEntries'])
        source['Command']['FileEntries'][1]['Permissions']=7;self.assertEqual(0,out['command']['fileEntries'][1]['permissions'])

    def test_browser_root_target_preserves_its_explicit_mask_six_and_named_zero(self):
        files,dirs=expected_targets(4)
        self.assertEqual(6,next(e['permissions'] for e in files if e['tag']=='mask'))
        self.assertEqual(7,next(e['permissions'] for e in dirs if e['tag']=='mask'))
        self.assertEqual({'tag':'user','id':0,'permissions':0},files[1])

    def test_default_null_and_explicit_clear_cannot_default_access_targets(self):
        source=plan('default');source['Command']['DirectoryEntries']=[];out=managed_plan(source)
        self.assertIsNone(out['command']['fileEntries']);self.assertEqual([],out['command']['directoryEntries'])
        for value in ([],target(),False):
            source['Command']['FileEntries']=value
            with self.subTest(value=value),self.assertRaises(AssertionError):managed_plan(source)
        source=plan();source['Command']['FileEntries']=[]
        with self.assertRaises(AssertionError):managed_plan(source)

    def test_acl_presence_numeric_and_order_rejections(self):
        changes=[lambda x:x[1].pop('Id'),lambda x:x[1].update(Id=False),lambda x:x[1].update(Id=4294967295),lambda x:x[1].update(Permissions=True),lambda x:x[1].update(Permissions=8),lambda x:x[0].update(Id=0),lambda x:x.reverse(),lambda x:x.append(copy.deepcopy(x[1])),lambda x:x.pop(3)]
        for change in changes:
            values=target();change(values)
            with self.subTest(values=values),self.assertRaises((AssertionError,KeyError)):managed_entries(values)

    def test_invalid_mode_identity_and_counts_reject_before_any_dependency(self):
        for field,value in [('Mode',0o100640),('Mode',0o44750),('OwnerUid',True),('OwnerGid',4294967295),('Device',1.0),('Inode',0)]:
            source=plan();source['RootIdentity'][field]=value
            with self.subTest(field=field,value=value),self.assertRaises(AssertionError):managed_plan(source)
        for field,value in [('EntryCount',True),('FileCount',5),('ChangeCount',5),('SchemaVersion',False)]:
            source=plan();source[field]=value
            with self.subTest(field=field,value=value),self.assertRaises(AssertionError):managed_plan(source)
        source=plan('default');source['ChangeCount']=3
        with self.assertRaises(AssertionError):managed_plan(source)

    def test_reference_and_artifact_selection_rejects_before_dependency_access(self):
        for value in ('','a'*32,'AA0A8865-343E-49BD-B01A-72680131BF39',"x';DELETE FROM operation_jobs;--"):
            with self.subTest(value=value),self.assertRaises((AssertionError,ValueError)):snapshot(value)
        for name in ('../escape','container-recursive-ownership-browser-'+'a'*32,'container-recursive-acl-browser-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
            with self.subTest(recovery=name),self.assertRaises(AssertionError):recover(name)
            with self.subTest(auth_only=name),self.assertRaises(AssertionError):auth_only_target(name)
        with tempfile.TemporaryDirectory() as d:
            for name in ('../escape','other','default/result','default-result.json'):
                with self.subTest(name=name),self.assertRaises(AssertionError):wait_case(None,Path(d),name)

    def test_independent_archive_check_never_overwrites_missing_or_changed_proof(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d);archive(p,'default.root.json',b'original',False)
            archive(p,'default.root.json',b'original',True)
            with self.assertRaises(AssertionError):archive(p,'default.root.json',b'changed',True)
            with self.assertRaises(FileNotFoundError):archive(p,'other.root.json',b'original',True)
            self.assertEqual(b'original',(p/'default.root.json').read_bytes());self.assertEqual(['default.root.json'],[x.name for x in p.iterdir()])

    def test_sql_queries_cannot_edit_production_fixture_or_history(self):
        for name in ('validate_container_recursive_acl_browser.py','verify_container_recursive_acl_browser.py'):
            tree=ast.parse((ROOT/name).read_text())
            for node in ast.walk(tree):
                if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                    self.assertIsInstance(node.args[0],ast.Constant);sql=node.args[0].value
                    self.assertTrue(sql.lstrip().upper().startswith('SELECT '));self.assertNotRegex(sql,r'(?i)\b(?:UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')

    def test_browser_has_visible_controls_original_references_and_no_response_or_cookie_injection(self):
        s=(ROOT/'validate_container_recursive_acl_browser.mjs').read_text()
        for forbidden in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem','localStorage.setItem'):
            self.assertNotIn(forbidden,s)
        for required in ('loginVisible','logoutFromUserMenu','await waitSignal(evidence, item.name + \'-verified.json\')',):
            self.assertIn(required,s)
        self.assertIn('recursiveAclBrowser',s);self.assertNotIn('recursiveOwnershipBrowser',s)
        for point in ('before-reload','after-reload','after-files-open','identity-response','identity-request-failed'):
            self.assertIn(point,s)
        self.assertIn('Object.keys(v).sort()',s);self.assertNotIn('request.allHeaders()).',s)
        self.assertNotIn('url:activePage.url()',s)
        self.assertNotIn('storageState()',s)
        auth=(ROOT/'validate_recursive_acl_auth_only.mjs').read_text()
        for forbidden in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'localStorage.setItem','sessionStorage.setItem'):
            self.assertNotIn(forbidden,auth)
        self.assertIn("name:'首页'",auth)
        self.assertIn('fileMutations:0',auth)
        self.assertEqual(8,len(CASES));self.assertEqual((2,4,0,2,4,0,2,4),CHANGES)

if __name__=='__main__':unittest.main()
