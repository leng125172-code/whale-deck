"""Offline serializer and safety boundaries; never an installed-RPC acceptance."""
import ast
import copy
import hashlib
from pathlib import Path
import unittest

from verify_container_recursive_acl_agent import proto_plan,verify_fixture_recipe,expected_targets
from validate_container_recursive_acl_agent import fixture_bytes,CASES


class RecursiveAclAgentProbeScope(unittest.TestCase):
    def value(self):
        entries=[dict(tag='user_obj',permissions=6),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',permissions=0),dict(tag='mask',permissions=4),dict(tag='other',permissions=0)]
        return {'schemaVersion':1,'command':{'location':{'containerId':'a'*64,'instanceId':'777:2026-11-04T01:02:03Z','path':'work/recursive-tree'},
            'aclType':'access','fileEntries':{'entries':entries},'directoryEntries':{'entries':copy.deepcopy(entries)},'expectedVersion':'A'*64},
            'targetProof':'B'*64,'parentIdentity':{'device':'18446744073709551615','inode':'18446744073709551614'},'parentVersion':'C'*64,
            'ownerUid':1000,'ownerGid':1000,'fingerprint':'D'*64,'rootIdentity':{'device':'18446744073709551615','inode':'18446744073709551613','mode':16872,
            'ownerUid':2100,'ownerGid':3100,'linkCount':'2','sizeBytes':'4096','modifiedAtUnixNs':'1700000000123456789'},
            'rootChangedAtUnixNs':'1700000000987654321','manifestDigest':'E'*64,'entryCount':4,'fileCount':2,'directoryCount':2,'changeCount':4}

    def rejected(self,value):
        with self.assertRaises((AssertionError,KeyError,TypeError)):proto_plan(value)

    def test_zero_named_id_and_zero_permission_remain_present(self):
        result=proto_plan(self.value())
        self.assertEqual(result['command']['fileEntries'][1],dict(tag='user',id=0,permissions=0))
        self.assertIsNone(result['command']['fileEntries'][0]['id'])
        self.assertEqual(result['parentIdentity'],[18446744073709551615,18446744073709551614])
        self.assertEqual(result['rootIdentity'][-2:],[1700000000123456789,1700000000987654321])

    def test_default_null_and_present_empty_directory_are_distinct(self):
        value=self.value();value['command'].update(aclType='default',fileEntries=None,directoryEntries={});value['changeCount']=2
        self.assertIsNone(proto_plan(value)['command']['fileEntries']);self.assertEqual(proto_plan(value)['command']['directoryEntries'],[])
        del value['command']['fileEntries'];self.assertIsNone(proto_plan(value)['command']['fileEntries'])
        value['command']['directoryEntries']={'entries':[]};self.assertEqual(proto_plan(value)['command']['directoryEntries'],[])

    def test_default_cannot_have_even_an_empty_file_wrapper(self):
        for bad in ({},{'entries':[]},{'entries':None}):
            value=self.value();value['command'].update(aclType='default',fileEntries=bad);value['changeCount']=2
            with self.subTest(bad=bad):self.rejected(value)

    def test_missing_access_wrappers_are_never_invented(self):
        for key in ('fileEntries','directoryEntries'):
            for missing in (True,False):
                value=self.value()
                if missing:del value['command'][key]
                else:value['command'][key]=None
                with self.subTest(key=key,missing=missing):self.rejected(value)

    def test_missing_default_directory_is_not_a_clear_instruction(self):
        value=self.value();value['command'].update(aclType='default',fileEntries=None);value['changeCount']=2
        del value['command']['directoryEntries'];self.rejected(value)

    def test_missing_named_id_or_permission_is_refused(self):
        for key in ('id','permissions'):
            value=self.value();del value['command']['fileEntries']['entries'][1][key];self.rejected(value)

    def test_non_integer_or_sentinel_named_id_is_refused(self):
        for bad in (True,False,'0',0.0,None,-1,4294967295):
            value=self.value();value['command']['fileEntries']['entries'][1]['id']=bad
            with self.subTest(bad=bad):self.rejected(value)

    def test_non_integer_and_out_of_range_permissions_are_refused(self):
        for bad in (True,'0',0.0,None,-1,8):
            value=self.value();value['command']['directoryEntries']['entries'][0]['permissions']=bad
            with self.subTest(bad=bad):self.rejected(value)

    def test_invalid_structure_and_unknown_fields_are_refused(self):
        for mutation in ('missing-mask','duplicate','reorder','missing-base','base-id','unknown','wrapper-extra','too-many'):
            value=self.value();wrapper=value['command']['fileEntries'];items=wrapper['entries']
            if mutation=='missing-mask':del items[3]
            elif mutation=='duplicate':items.append(copy.deepcopy(items[-1]))
            elif mutation=='reorder':items.reverse()
            elif mutation=='missing-base':del items[0]
            elif mutation=='base-id':items[0]['id']=0
            elif mutation=='unknown':items[0]['extra']=0
            elif mutation=='wrapper-extra':wrapper['extra']=[]
            else:wrapper['entries']=items*13
            with self.subTest(mutation=mutation):self.rejected(value)

    def test_kernel_integer_boolean_float_and_overflow_are_refused(self):
        for bad in (True,1.5,'1.0','01','1e3',-1,'18446744073709551616'):
            value=self.value();value['parentIdentity']['device']=bad
            with self.subTest(bad=bad):self.rejected(value)

    def test_signed_timestamps_remain_exact_but_overflow_is_refused(self):
        value=self.value();value['rootChangedAtUnixNs']='-9223372036854775808';self.assertEqual(proto_plan(value)['rootIdentity'][-1],-9223372036854775808)
        for bad in (True,1.5,'9223372036854775808','-9223372036854775809'):
            value['rootChangedAtUnixNs']=bad
            with self.subTest(bad=bad):self.rejected(value)

    def test_counts_do_not_accept_boolean_or_inconsistent_totals(self):
        for key,bad in (('schemaVersion',True),('ownerUid',True),('entryCount',True),('fileCount',3),('changeCount',5)):
            value=self.value();value[key]=bad
            with self.subTest(key=key):self.rejected(value)
        value=self.value();value['command'].update(aclType='default',fileEntries=None);self.rejected(value)

    def test_tree_identity_and_version_binding_are_checked(self):
        for target,key,bad in (('rootIdentity','mode',33188),('rootIdentity','ownerUid',4294967295),('rootIdentity','linkCount','0'),('command','expectedVersion','a'*64)):
            value=self.value();value[target][key]=bad
            with self.subTest(target=target,key=key):self.rejected(value)
        value=self.value();value['command']['location']['path']='work/other';self.rejected(value)

    def test_parent_and_independent_full_byte_recipes_agree(self):
        identifier='a'*64;raw=fixture_bytes(identifier);baseline=dict(containerId=identifier,bytesPerFile=len(raw),sha256=hashlib.sha256(raw).hexdigest().upper())
        self.assertEqual(verify_fixture_recipe(baseline),raw)
        self.assertEqual(len(raw),2*1024**2+7)
        for prefix in ('recursive-acl-candidate:','installed-acl:','recursive-owner:'):
            wrong=hashlib.shake_256((prefix+identifier).encode()).digest(len(raw));baseline['sha256']=hashlib.sha256(wrong).hexdigest().upper()
            with self.subTest(prefix=prefix),self.assertRaises(AssertionError):verify_fixture_recipe(baseline)

    def test_default_precedes_access_and_cancel_never_applies(self):
        self.assertEqual(CASES,(('default','default','named',2),('access','access','named',4),('noop','access','named',0),('mask','access','mask',2),
            ('root','access','root',4),('default-noop','default','named',0),('clear-default','default','clear',2),('cancel','access','mask',4)))
        self.assertEqual(sum(case[3] for case in CASES[:-1]),14)

    def test_independent_targets_keep_root_acl_distinct_from_inode_owner(self):
        file,directory=expected_targets(4)
        self.assertEqual(file[1],dict(tag='user',id=0,permissions=0));self.assertEqual(directory[1],file[1])
        self.assertEqual(expected_targets(6),(None,[]))
        self.assertIsNone(expected_targets(0)[0]);self.assertEqual(expected_targets(0),expected_targets(5))
        self.assertEqual(expected_targets(1),expected_targets(2));self.assertNotEqual(expected_targets(1),expected_targets(3))
        self.assertEqual(expected_targets(3),expected_targets(7))

    def test_gate_uses_signed_uds_and_never_instantiates_a_mutation_engine(self):
        directory=Path(__file__).parent
        for name in ('validate_container_recursive_acl_agent.py','verify_container_recursive_acl_agent.py'):
            source=(directory/name).read_text();ast.parse(source)
            self.assertNotIn('recursive_acl_type(',source);self.assertNotIn('write_acl(',source);self.assertNotIn("['docker','exec'",source)
        probe=(directory/'TerminalProbe/ContainerRecursiveAclProbe.cs').read_text()
        self.assertIn('UnixDomainSocketEndPoint("/run/whaledeck/agent.sock")',probe)
        self.assertIn('CryptographicOperations.ZeroMemory(key)',probe)
        self.assertNotIn('Console.WriteLine(key',probe)


if __name__=='__main__':unittest.main()
