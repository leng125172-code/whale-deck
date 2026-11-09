"""Temporary native refusal/proof tests; no production HDD or clock override."""
import ast
import copy
import json
import os
import stat
from pathlib import Path
import time
import unittest
from unittest.mock import patch
from archive_retirement_preexpiry_proof import preexpiry_refusal_proof
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import snapshot,read_at,frame
from container_recycle_agent_io import file_snapshot
from validate_container_recycle_archive_plan import payload_snapshot
import test_container_recycle_archive_retirements as native


source=Path(__file__).with_name('validate_container_recycle_archive_copies.py')
nodes=[n for n in ast.parse(source.read_bytes()).body if isinstance(n,ast.FunctionDef) and n.name in ('retirement_preexpiry_check','retention_namespace_snapshot')]
assert len(nodes)==2
scope=dict(read_at=read_at,snapshot=snapshot,frame=frame,file_snapshot=file_snapshot,time=time,typed_equal=typed_equal,preexpiry_refusal_proof=preexpiry_refusal_proof)
exec(compile(ast.Module(body=nodes,type_ignores=[]),str(source),'exec'),scope)
capture=scope['retirement_preexpiry_check'];namespace_snapshot=scope['retention_namespace_snapshot']


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchivePreexpiryTests(unittest.TestCase):
    def setUp(self):
        self.f=native.ArchiveRetirementTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.f.reserve();self.q=self.f.q;self.engine=self.f.engine;self.session=self.f.session;self.copied=self.f.native.record()

    def protected(self):
        n=self.f.native
        return dict(archive=snapshot(n.engine.state_path),lifecycles=dict(source=snapshot(n.source.state_path),restore=snapshot(n.engine.restore.state_path),purge=snapshot(n.engine.preflight.purges.state_path),hdd=payload_snapshot(n.base.target)),namespace=namespace_snapshot(n.base.root))

    def capture(self):return capture(self.engine,self.q,self.session,self.protected)
    def prove(self,value):return preexpiry_refusal_proof(value,self.q,self.copied)

    def test_actual_young_executor_refusal_is_readonly_and_independently_joined(self):
        before=self.protected();roots=snapshot(self.engine.state_path)
        with patch.object(os,'unlink',side_effect=AssertionError('no payload or journal writes')),patch.object(os,'write',side_effect=AssertionError('no write')):
            value=self.capture();result=self.prove(value)
        self.assertEqual(result,dict(preexpiryRefusal='passed',dataDeletionProved=False,naturalExpiryProved=False));self.assertEqual(before,self.protected());self.assertEqual(roots,snapshot(self.engine.state_path));self.assertTrue(self.f.data.exists())

    def test_clock_or_elapsed_boolean_lossy_and_out_of_range_values_are_rejected(self):
        value=self.capture();due=self.q['plan']['notBeforeUnixMs']
        for key,bad in (('clockBeforeUnixMs',True),('clockAfterUnixMs',float(value['clockAfterUnixMs'])),('elapsedMonotonicNs',False),('clockBeforeUnixMs',due),('clockAfterUnixMs',due),('clockAfterUnixMs',value['clockBeforeUnixMs']-1),('elapsedMonotonicNs',120000000001)):
            changed=copy.deepcopy(value);changed[key]=bad
            with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):self.prove(changed)

    def test_wrong_error_or_extra_claim_cannot_be_certified(self):
        value=self.capture()
        for changes in (dict(error='CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SESSION_REQUIRED'),dict(error='success'),dict(naturalExpiry=True)):
            with self.subTest(changes=changes),self.assertRaises(AssertionError):self.prove({**value,**changes})

    def test_request_and_owner_are_bound_to_independently_reserved_request(self):
        value=self.capture()
        for key,bad in (('owner','F'*64),('operationId','f'*32)):
            changed=copy.deepcopy(value);changed['request'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(changed)

    def test_refusal_does_not_accept_terminal_or_modified_journal_or_marker(self):
        value=self.capture()
        for key in ('journalBefore','journalAfter','markerBefore','markerAfter'):
            changed=copy.deepcopy(value);changed[key]['state']='Unknown'
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(changed)

    def test_equal_but_forged_root_hash_mode_or_lock_shape_is_rejected(self):
        value=self.capture();name=self.q['operationId']+'.json';lock=self.q['operationId']+'.lock'
        for which,field,bad in ((name,'sha256','f'*64),(name,'mode',0o100644),(lock,'size',1)):
            changed=copy.deepcopy(value)
            for suffix in ('Before','After'):
                item=changed['retirementRoots'+suffix][which]
                if field=='sha256':item['sha256']=bad
                else:item['identity'][2 if field=='mode' else 6]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):self.prove(changed)

    def test_hdd_data_loss_or_any_protected_snapshot_change_is_rejected(self):
        value=self.capture();source_id=self.f.ref['operationId']
        changed=copy.deepcopy(value);del changed['protectedAfter']['lifecycles']['hdd'][source_id+'.data']
        with self.assertRaises(AssertionError):self.prove(changed)
        for suffix in ('Before','After'):
            del value['protected'+suffix]['lifecycles']['hdd'][source_id+'.data']
        with self.assertRaises(KeyError):self.prove(value)

    def test_equal_but_foreign_hdd_identity_or_live_copy_is_rejected(self):
        value=self.capture();source_id=self.f.ref['operationId'];leaf=self.f.native.base.leaf.name
        for live in (False,True):
            changed=copy.deepcopy(value)
            for suffix in ('Before','After'):
                view=changed['protected'+suffix]
                item=view['namespace'][leaf] if live else view['lifecycles']['hdd'][source_id+'.data']
                item['identity'][1]+=1
            with self.subTest(live=live),self.assertRaises(AssertionError):self.prove(changed)

    def test_completed_copy_and_original_claim_are_joined_not_boolean_flags(self):
        value=self.capture();changed=copy.deepcopy(self.copied);changed['state']='Unknown'
        with self.assertRaises(AssertionError):preexpiry_refusal_proof(value,self.q,changed)
        changed=copy.deepcopy(value);name='claims/'+self.f.ref['operationId']+'.json'
        for suffix in ('Before','After'):changed['protected'+suffix]['lifecycles']['restore'][name]['sha256']='f'*64
        with self.assertRaises(AssertionError):self.prove(changed)

    def test_collector_refuses_success_or_wrong_rejection_instead_of_emitting_proof(self):
        with patch.object(self.engine,'apply',return_value=dict(state='Completed')),self.assertRaisesRegex(AssertionError,'PREEXPIRY_EXECUTION_NOT_REJECTED'):self.capture()
        with patch.object(self.engine,'apply',side_effect=self.engine.r.Rejected('WRONG_REASON')),self.assertRaises(AssertionError):self.capture()
        self.assertTrue(self.f.data.exists())

    def test_collector_detects_writes_even_when_executor_returns_expected_error(self):
        def write_then_reject(*args):
            path=self.f.data;path.write_bytes(b'changed unexpectedly')
            raise self.engine.r.Rejected('CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_ELIGIBLE')
        with patch.object(self.engine,'apply',side_effect=write_then_reject),self.assertRaises(AssertionError):self.capture()
        self.assertEqual(self.f.data.read_bytes(),b'changed unexpectedly')

    def proof_files(self):
        value=self.capture();e=self.f.engine.state_path.parent
        reserved=dict(requests=[self.q],steps=[dict(roots=copy.deepcopy(value['retirementRootsBefore']))],before=copy.deepcopy(value['protectedBefore']))
        def save():
            (e/'retirement-preexpiry.json').write_text(json.dumps(value));(e/'retirement-reservations.json').write_text(json.dumps(reserved))
        save();original=copy.deepcopy(value['protectedBefore'])
        current=copy.deepcopy({e/'archive-state':original['archive'],**{e/folder:original['lifecycles'][name] for name,folder in (('source','source-state'),('purge','purge-state'),('restore','restore-state'))}})
        verifier=Path(__file__).with_name('verify_container_recycle_archive_copies.py')
        nodes=[n for n in ast.parse(verifier.read_bytes()).body if isinstance(n,ast.FunctionDef) and n.name in ('early_retirement_refusal_proof','retirement_namespace_reread','retirement_namespace_proof')]
        scope=dict(json=json,stat=stat,frame=frame,file_snapshot=file_snapshot,preexpiry_refusal_proof=preexpiry_refusal_proof,typed_equal=typed_equal,snapshot=lambda path:copy.deepcopy(current[path]))
        exec(compile(ast.Module(body=nodes,type_ignores=[]),str(verifier),'exec'),scope)
        def prove(after=False):return scope['early_retirement_refusal_proof'](e,dict(root=self.copied,journals=original['archive']),original['namespace'],original['lifecycles']['hdd'],after_cleanup=after)
        return value,reserved,save,prove,scope,e,current

    def test_live_and_final_file_proof_must_match_reserved_step_and_before_baseline(self):
        value,reserved,save,prove,*_=self.proof_files();prove()
        reserved['steps'][0]['roots']={};save()
        with self.assertRaises(AssertionError):prove()

    def test_coherently_deleted_source_and_purge_trees_cannot_hide_from_current_read(self):
        value,reserved,save,prove,*_=self.proof_files();prove()
        for suffix in ('Before','After'):
            for name in ('source','purge'):value['protected'+suffix]['lifecycles'][name]={}
        reserved['before']=copy.deepcopy(value['protectedBefore']);reserved['after']=copy.deepcopy(value['protectedAfter']);save()
        with self.assertRaises(AssertionError):prove()

    def test_coherently_deleted_namespace_entries_cannot_hide_from_live_reread(self):
        value,reserved,save,prove,*_=self.proof_files();prove();leaf=self.f.native.base.leaf.name
        for suffix in ('Before','After'):value['protected'+suffix]['namespace']={leaf:value['protected'+suffix]['namespace'][leaf]}
        reserved['before']=copy.deepcopy(value['protectedBefore']);reserved['after']=copy.deepcopy(value['protectedAfter']);save()
        with self.assertRaises(AssertionError):prove()

    def test_final_proof_excludes_only_exact_later_competitor_lock(self):
        value,reserved,save,prove,scope,e,current=self.proof_files();later='a'*32+'.lock'
        (e/'retention-absent.json').write_text(json.dumps(dict(competitorOperationId='a'*32)))
        current[e/'archive-state'][later]=copy.deepcopy(value['retirementRootsBefore'][self.q['operationId']+'.lock'])
        prove(True)
        current[e/'source-state']['foreign.json']={}
        with self.assertRaises(AssertionError):prove(True)

    def test_independent_namespace_reread_is_complete_and_rejects_symlinks(self):
        *_,scope,e,current=self.proof_files();folder=self.f.native.base.root
        value=scope['retirement_namespace_reread'](folder);self.assertEqual(value,self.protected()['namespace'])
        (folder/'foreign-link').symlink_to('/does-not-exist')
        with self.assertRaises(AssertionError):scope['retirement_namespace_reread'](folder)

    def test_final_namespace_inventory_requires_original_copy_and_stage(self):
        *_,scope,e,current=self.proof_files();original=e/'original-fixture.bin';original.write_bytes(b'owned original')
        fixture=dict(original=file_snapshot(original));q=self.engine.archive(self.q);stage='.whaledeck-container-archive-'+q['operationId'];leaf=self.f.native.base.leaf.name
        copied=dict(request=q,root=self.copied,actual=file_snapshot(self.f.native.base.leaf))
        namespace={'original.bin':dict(kind='file',**fixture['original']),leaf:dict(kind='file',**copied['actual']),stage:dict(kind='directory',identity=frame(self.f.native.private().stat()))}
        scope['retirement_namespace_proof'](namespace,fixture,copied)
        for removed in ('original.bin',stage):
            bad=copy.deepcopy(namespace);del bad[removed]
            with self.subTest(removed=removed),self.assertRaises(AssertionError):scope['retirement_namespace_proof'](bad,fixture,copied)
