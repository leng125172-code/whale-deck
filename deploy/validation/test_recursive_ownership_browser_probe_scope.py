"""Offline safety/serialization boundaries for the real installed browser gate.

Never signs RPC, connects dependencies, creates identities or writes a real tree.
"""
import ast
from pathlib import Path
import tempfile
import unittest

from validate_container_recursive_ownership_browser import managed_plan, version, snapshot, wait_case
from verify_container_recursive_ownership_browser import verify
from recover_container_recursive_ownership_browser import recover

ROOT = Path(__file__).resolve().parent


class RecursiveOwnershipBrowserProbeScopeTests(unittest.TestCase):
    def test_managed_plan_preserves_large_kernel_identity_and_explicit_owners(self):
        identity = {'Device': 18446744073709551615, 'Inode': 18446744073709551614, 'Mode': 16872, 'OwnerUid': 2100, 'OwnerGid': 3100, 'LinkCount': 2, 'SizeBytes': 4096, 'ModifiedAtUnixNs': 1700000000123456789}
        location = {'ContainerId': 'a'*64, 'InstanceId': '777:2026-11-02T01:02:03.123456789Z', 'Path': 'work/中文'}
        source = {'SchemaVersion': 1, 'Command': {'Location': location, 'Uid': 0, 'Gid': 3300, 'ExpectedVersion': 'A'*64},
            'RootIdentity': identity, 'RootChangedAtUnixNs': 1700000000987654321, 'ParentIdentity': {'Device': 18446744073709551615, 'Inode': 33},
            'TargetProof': 'B'*64, 'ParentVersion': 'C'*64, 'OwnerUid': 1000, 'OwnerGid': 1001, 'Fingerprint': 'D'*64,
            'ManifestDigest': 'E'*64, 'EntryCount': 4, 'FileCount': 2, 'DirectoryCount': 2, 'ChangeCount': 4}
        result = managed_plan(source)
        self.assertEqual([18446744073709551615, 18446744073709551614], result['rootIdentity'][:2])
        self.assertEqual(1700000000123456789, result['rootIdentity'][7]); self.assertEqual(1700000000987654321, result['rootIdentity'][8])
        self.assertEqual([0,3300], [result['command']['uid'],result['command']['gid']])
        self.assertEqual('work/中文', result['command']['path']); self.assertEqual([1000,1001], [result['uid'],result['gid']])
        self.assertEqual([2100,3100], result['rootIdentity'][3:5])

    def test_explicit_root_is_distinct_from_missing_or_bool_owner(self):
        source={'Command':{'Uid':False,'Gid':0},'RootIdentity':{},'ParentIdentity':{}}
        for command in ({'Uid':False,'Gid':0},{'Uid':0,'Gid':True},{'Uid':4294967295,'Gid':0},{'Gid':0}):
            with self.subTest(command=command),self.assertRaises((AssertionError,KeyError)):
                managed_plan({**source,'Command':{'Location':{},**command}})

    def test_version_binds_instance_path_and_all_nine_metadata_fields(self):
        baseline = {'containerId': 'a'*64, 'instanceId': '777:2026-11-02T01:02:03Z'}
        identity = [1,2,33184,1000,1001,1,2097159,1700000000123456789,1700000000987654321]
        original = version(baseline,'work/tree',identity)
        self.assertEqual(64,len(original)); self.assertNotEqual(original,version({**baseline,'instanceId':'888:2026-11-02T01:02:03Z'},'work/tree',identity))
        self.assertNotEqual(original,version(baseline,'work/other',identity))
        for index in range(9):
            changed = list(identity); changed[index] += 1
            self.assertNotEqual(original,version(baseline,'work/tree',changed))

    def test_invalid_reference_rejects_before_database(self):
        for identifier in ('', 'a'*32, 'AA0A8865-343E-49BD-B01A-72680131BF39', "x';DELETE FROM operation_jobs;--"):
            with self.subTest(identifier=identifier), self.assertRaises((AssertionError,ValueError)):
                snapshot(identifier)

    def test_private_signal_names_cannot_escape_owned_case(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in ('../escape','other','complete/result','complete-result.json'):
                with self.subTest(name=name), self.assertRaises(AssertionError): wait_case(None,Path(directory),name)
            self.assertEqual([],list(Path(directory).iterdir()))

    def test_independent_input_cannot_select_arbitrary_artifacts(self):
        for name in ('../escape','container-recursive-permission-browser-'+'a'*32,'container-recursive-ownership-browser-'+'A'*32):
            with self.subTest(name=name), self.assertRaises(AssertionError): verify(name)

    def test_recovery_cannot_select_arbitrary_fixture_or_other_workflow(self):
        for name in ('../escape','container-recursive-permission-browser-'+'a'*32,'container-recursive-ownership-browser-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):recover(name)

    def test_native_queries_are_selects_not_fixture_state_edits(self):
        for file in ('validate_container_recursive_ownership_browser.py','verify_container_recursive_ownership_browser.py'):
            tree = ast.parse((ROOT/file).read_text())
            queries = [node.args[0] for node in ast.walk(tree) if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query']
            self.assertTrue(queries)
            for sql in queries:
                self.assertIsInstance(sql,ast.Constant); self.assertTrue(sql.value.lstrip().upper().startswith('SELECT '))
                self.assertNotRegex(sql.value,r'(?i)\b(?:UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')

    def test_browser_never_injects_sessions_or_replaces_product_responses(self):
        source = (ROOT/'validate_container_recursive_ownership_browser.mjs').read_text()
        for token in ('page.route(', 'context.route(', 'addCookies(', 'storageState:', 'sessionStorage.setItem', 'localStorage.setItem'):
            self.assertNotIn(token,source)
        self.assertIn('await waitSignal(evidence, item.name + \'-verified.json\')',source)


if __name__ == '__main__': unittest.main()
