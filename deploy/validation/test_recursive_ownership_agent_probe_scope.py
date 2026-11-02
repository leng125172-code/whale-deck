"""Offline boundaries for the installed probe, never live credentials or resources."""
import ast
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

from verify_container_recursive_ownership_agent import proto_plan
from validate_container_recursive_ownership_agent import no_active_work


class RecursiveOwnershipAgentProbeScope(unittest.TestCase):
    def value(self):
        return {'schemaVersion':1,'command':{'location':{'containerId':'a'*64,'instanceId':'777:2026-11-02T01:02:03Z','path':'work/recursive-tree'},
            'uid':0,'gid':0,'expectedVersion':'A'*64},'targetProof':'B'*64,'parentIdentity':{'device':'18446744073709551615','inode':'18446744073709551614'},
            'parentVersion':'C'*64,'ownerUid':1000,'ownerGid':1000,'fingerprint':'D'*64,
            'rootIdentity':{'device':'18446744073709551615','inode':'18446744073709551613','mode':16872,'ownerUid':2100,'ownerGid':3100,'linkCount':'2','sizeBytes':'4096','modifiedAtUnixNs':'1700000000123456789'},
            'rootChangedAtUnixNs':'1700000000987654321','manifestDigest':'E'*64,'entryCount':4,'fileCount':2,'directoryCount':2,'changeCount':4}

    def test_explicit_root_and_large_kernel_identity_are_exact(self):
        result=proto_plan(self.value())
        self.assertEqual((result['command']['uid'],result['command']['gid']),(0,0))
        self.assertEqual((result['uid'],result['gid']),(1000,1000))
        self.assertEqual(result['parentIdentity'],[18446744073709551615,18446744073709551614])
        self.assertEqual(result['rootIdentity'][-2:],[1700000000123456789,1700000000987654321])

    def test_missing_owner_is_never_invented_as_zero(self):
        for key in ('uid','gid'):
            value=self.value();del value['command'][key]
            with self.subTest(key=key),self.assertRaises(KeyError):proto_plan(value)

    def test_boolean_string_negative_and_sentinel_owners_are_refused(self):
        for key in ('uid','gid'):
            for bad in (True,False,'0',-1,4294967295):
                value=self.value();value['command'][key]=bad
                with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):proto_plan(value)

    def test_restarts_require_all_existing_file_intents_and_jobs_quiet(self):
        prefix='validate_container_recursive_ownership_agent.'
        with patch(prefix+'preflight',return_value={'otherJobs':0}) as jobs,patch(prefix+'inactive_except',return_value={'recursive':0}) as files:
            self.assertEqual(no_active_work(),{'otherJobs':0,'recursive':0});jobs.assert_called_once_with();files.assert_called_once_with()
        with patch(prefix+'preflight',side_effect=AssertionError('active job')),patch(prefix+'inactive_except') as files:
            with self.assertRaises(AssertionError):no_active_work()
            files.assert_not_called()

    def test_gate_never_instantiates_a_local_mutation_engine_in_place_of_rpc(self):
        directory=Path(__file__).parent
        for name in ('validate_container_recursive_ownership_agent.py','verify_container_recursive_ownership_agent.py'):
            source=(directory/name).read_text();ast.parse(source)
            self.assertNotIn('recursive_ownership_type(',source);self.assertNotIn('os.chmod(',source)
            self.assertNotIn("['docker','exec'",source)
        probe=(directory/'TerminalProbe/ContainerRecursiveOwnershipProbe.cs').read_text()
        self.assertIn('UnixDomainSocketEndPoint("/run/whaledeck/agent.sock")',probe)
        self.assertNotIn('Console.WriteLine(key',probe)


if __name__=='__main__':unittest.main()
