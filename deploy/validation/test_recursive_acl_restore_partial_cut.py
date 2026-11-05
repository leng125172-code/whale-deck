"""Observer scope negatives use explicit process doubles; never real host signals."""
import copy,unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch
from contextlib import ExitStack
import signal
from bound_container_recursive_acl_restore_cut import completed_entry_boundary,first_entry_boundary,freeze_after_first_restore
from test_recursive_acl_restore_prewrite_proof import boundary

class RestorePartialCutTests(unittest.TestCase):
    def test_only_first_completed_before_second_started_is_a_boundary(self):
        r=boundary();r.update(startedThrough=0,finishedThrough=0);self.assertTrue(completed_entry_boundary(r));self.assertFalse(first_entry_boundary(r))
        for key,value in (('state','Partial'),('state','Completed'),('startedThrough',1),('finishedThrough',-1),('finishedThrough',True),('historyVerified',False),('historyDigest','a'*64)):
            value_r=copy.deepcopy(r);value_r[key]=value
            with self.subTest(key=key):
                if type(value) is bool and key=='finishedThrough':
                    with self.assertRaises(AssertionError):completed_entry_boundary(value_r)
                else:self.assertFalse(completed_entry_boundary(value_r))

    def test_prewrite_does_not_become_partial_and_26_scope_stays_fixed(self):
        r=boundary();self.assertTrue(first_entry_boundary(r));self.assertFalse(completed_entry_boundary(r))
        for key,value in (('entryCount',4),('changeCount',0),('fileCount',24),('directoryCount',True)):
            t=copy.deepcopy(r);t['plan'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):completed_entry_boundary(t)

    def test_existing_archive_or_unowned_target_never_authorizes_signalling(self):
        prefix='bound_container_recursive_acl_restore_cut.'
        with TemporaryDirectory() as folder,patch(prefix+'os.geteuid',return_value=0),patch('validate_container_upload_recovery_agent.trusted'),patch(prefix+'signal.pidfd_send_signal',side_effect=AssertionError('must not signal')) as signal:
            archive=Path(folder)/'new'
            for source in ('/etc','/data/WhaleDeck/apps/not-owned'):
                with self.subTest(source=source),self.assertRaises(AssertionError):freeze_after_first_restore(dict(container_id='a'*64),source,2,{},lambda:{},{},[], '',archive)
            archive.mkdir()
            with self.assertRaisesRegex(AssertionError,'ARCHIVE_ALREADY_EXISTS'):freeze_after_first_restore({},'/etc',2,{},lambda:{},{},[],'',archive)
            signal.assert_not_called()

    def test_bound_success_transfers_pidfd_and_failed_physical_proof_resumes_it(self):
        prefix='bound_container_recursive_acl_restore_cut.';folder=Path('/data/WhaleDeck/apps/wdv-container-publish-'+'a'*32);unit='whaledeck-container-recursive-acl-restore-'+'c'*32+'.service'
        row=dict(container_id='b'*64,instance_id='2:owned',execution_job_id='c'*32,plan_json='{}',owner_hash='D'*64)
        record=boundary();record.update(startedThrough=0,finishedThrough=0)
        for fail in (False,True):
            with self.subTest(fail=fail),TemporaryDirectory() as directory,ExitStack() as stack:
                replacements={'os.geteuid':dict(return_value=0),'Path.resolve':dict(return_value=folder),'Path.is_symlink':dict(return_value=False),'identity':dict(return_value=(1,2)),'managed_plan':dict(return_value=dict(parentIdentity=[1,2])),
                    'Path.exists':dict(autospec=True,side_effect=lambda p:p.suffix=='.lock'),'Path.glob':dict(return_value=[Path(unit)]),'Path.read_text':dict(autospec=True,side_effect=lambda p,*a,**k:'1234' if p.name=='cgroup.procs' else '1234 (python) T'),
                    'operation_lock':dict(return_value=(3,4)),'bound_fds':dict(return_value=True),'process_start':dict(return_value=99),'os.pidfd_open':dict(return_value=55),'pidfd_same':dict(return_value=True),'record_for':dict(return_value=record),
                    'invocation_proof':dict(return_value=dict(invocationId='e'*32,mainPid=1234,exactCgroupAndInvocation=True))}
                for name,kwargs in replacements.items():stack.enter_context(patch(prefix+name,**kwargs))
                stack.enter_context(patch('validate_container_upload_recovery_agent.trusted'));stack.enter_context(patch('recursive_acl_restore_agent_io.installed'))
                stack.enter_context(patch('recursive_acl_restore_partial_record_proof.read_applying_first',return_value=dict(root=record)))
                facts=stack.enter_context(patch('recursive_acl_restore_partial_record_proof.completed_first_facts',side_effect=AssertionError('physical mismatch') if fail else None,return_value=dict(completedChildHdd=1,otherEntriesUnchanged=25)))
                send=stack.enter_context(patch(prefix+'signal.pidfd_send_signal'));close=stack.enter_context(patch(prefix+'os.close'))
                args=(row,folder,2,{},lambda:{},{},[],'',Path(directory)/'new')
                if fail:
                    with self.assertRaisesRegex(AssertionError,'physical mismatch'):freeze_after_first_restore(*args)
                    self.assertEqual([c.args[1] for c in send.call_args_list],[signal.SIGSTOP,signal.SIGCONT]);close.assert_called_once_with(55)
                else:
                    held=freeze_after_first_restore(*args);self.assertTrue(held.proof['firstRestoreCompleted']);close.assert_not_called();self.assertEqual(send.call_count,1)
                    held.release();self.assertEqual([c.args[1] for c in send.call_args_list],[signal.SIGSTOP,signal.SIGCONT]);close.assert_called_once_with(55)
                facts.assert_called_once()

if __name__=='__main__':unittest.main()
