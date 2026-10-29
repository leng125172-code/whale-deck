"""Kernel/file boundary checks for the QA-only partial-copy observer."""
import json
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch
import uuid

import bound_container_copy_cut as cut


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class CopyInflightScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-copy-cut-test-'); self.root = Path(self.temporary.name)
        self.operation = uuid.uuid4().hex; self.owner = 'A' * 64
        self.row = {'execution_job_id': str(uuid.UUID(self.operation)), 'owner_hash': self.owner, 'container_id': 'a' * 64,
            'instance_id': '100:2026-10-29T01:00:00Z', 'source_path': 'work/source', 'target_path': 'work/target',
            'plan_json': json.dumps({'Kind':'File','Fingerprint':'F'*64}), 'command_json':json.dumps({'ExpectedVersion':'E'*64})}
        self.source = self.root / 'source';self.source.mkdir()
        self.plan = {'kind':'File','fingerprint':'F'*64,'targetParent':list(cut.identity(self.source)),
            'command':{'containerId':self.row['container_id'],'instanceId':self.row['instance_id'],'sourcePath':'work/source','targetPath':'work/target','expectedVersion':'E'*64},
            'innerPlan':{'sizeBytes':1024}}
        self.record = {'operationId':self.operation,'owner':self.owner,'plan':self.plan,'state':'Running'}
        self.write(self.root/(self.operation+'.json'),self.record)
        self.operations = self.root/'engines/operations';self.operations.mkdir(parents=True)
        self.private = self.source/'.whaledeck-container-copies-v1';self.private.mkdir(mode=0o700)
        self.pending = self.private/(self.operation+'.mutation');self.pending.write_bytes(b'x'*128);self.pending.chmod(0o600)
        self.inner = {'operationId':self.operation,'owner':self.owner,'plan':self.plan['innerPlan'],'state':'Preparing','newIdentity':list(cut.identity(self.pending))}
        self.write(self.operations/(self.operation+'.json'),self.inner)

    def tearDown(self): self.temporary.cleanup()

    def write(self,path,value):path.write_text(json.dumps(value));path.chmod(0o600)

    def test_actual_registered_partial_inode_is_required(self):
        with patch.object(cut,'ROOT',self.root):
            proof=cut.partial_proof(self.record,self.source)
        self.assertEqual((proof['partialBytes'],proof['totalBytes']),(128,1024));self.assertEqual(self.pending.read_bytes(),b'x'*128)

    def test_full_or_empty_file_is_not_a_partial_cut(self):
        with patch.object(cut,'ROOT',self.root):
            for size in (0,1024,2048):
                self.pending.write_bytes(b'x'*size);self.assertIsNone(cut.partial_proof(self.record,self.source))

    def test_published_target_or_completed_receipt_cannot_count_as_interruption(self):
        with patch.object(cut,'ROOT',self.root):
            (self.source/'target').write_bytes(b'foreign');self.assertIsNone(cut.partial_proof(self.record,self.source));(self.source/'target').unlink()
            self.inner['state']='Completed';self.write(self.operations/(self.operation+'.json'),self.inner)
            self.assertIsNone(cut.partial_proof(self.record,self.source))

    def test_wrong_owner_and_foreign_private_object_are_refused(self):
        with patch.object(cut,'ROOT',self.root):
            self.inner['owner']='B'*64;self.write(self.operations/(self.operation+'.json'),self.inner)
            with self.assertRaises(AssertionError):cut.partial_proof(self.record,self.source)
            self.inner['owner']=self.owner;self.write(self.operations/(self.operation+'.json'),self.inner)
            (self.private/'unregistered').write_bytes(b'keep');self.assertIsNone(cut.partial_proof(self.record,self.source))
            self.assertEqual((self.private/'unregistered').read_bytes(),b'keep')

    def test_replaced_inode_or_symlink_is_not_receipt_owned(self):
        old=self.private/'old';self.pending.rename(old);self.pending.write_bytes(b'new');self.pending.chmod(0o600);old.unlink()
        with patch.object(cut,'ROOT',self.root):
            with self.assertRaises(AssertionError):cut.partial_proof(self.record,self.source)
            self.pending.unlink();outside=self.root/'outside';outside.write_bytes(b'keep');self.pending.symlink_to(outside)
            with self.assertRaises(AssertionError):cut.partial_proof(self.record,self.source)
            self.assertEqual(outside.read_bytes(),b'keep')

    def test_root_record_must_match_original_execution_owner_and_command(self):
        with patch.object(cut,'ROOT',self.root):
            self.assertEqual(cut.record_for(self.row),self.record)
            for field,value in [('owner_hash','B'*64),('target_path','work/other'),('container_id','b'*64)]:
                changed={**self.row,field:value}
                with self.assertRaises(AssertionError):cut.record_for(changed)

    def test_real_child_holding_both_fds_still_needs_exact_installed_apply_argv(self):
        with open(self.root/(self.operation+'.lock'),'xb') as lock:
            root_fd=os.open(self.source,os.O_RDONLY|os.O_DIRECTORY)
            try:
                child=subprocess.Popen(['/bin/sleep','10'],pass_fds=(root_fd,lock.fileno()))
                try:self.assertFalse(cut.bound_fds(child.pid,cut.identity(self.source),cut.identity(Path(lock.name))))
                finally:child.terminate();child.wait(timeout=5)
            finally:os.close(root_fd)

    def test_scope_refuses_non_owned_source_before_pid_or_signal_access(self):
        with self.assertRaises(AssertionError):cut.freeze_partial_copy(self.row,self.source,100)

    def test_receipt_replaced_after_open_cannot_authorize_signal(self):
        target=self.operations/(self.operation+'.json')
        real_read=os.read
        def replace_after_read(fd,size):
            value=real_read(fd,size)
            replacement=self.operations/'new';self.write(replacement,self.inner)
            os.replace(replacement,target)
            return value
        with patch.object(cut.os,'read',side_effect=replace_after_read):
            self.assertIsNone(cut.receipt_json(target))

    def test_linked_unsafe_receipt_is_refused_rather_than_retried(self):
        target=self.operations/(self.operation+'.json');target.chmod(0o644)
        with self.assertRaisesRegex(AssertionError,'UNSAFE_RECEIPT'):cut.receipt_json(target)

    def test_exit_clock_is_observed_during_a_longer_service_action(self):
        child=subprocess.Popen(['/bin/sleep','10'])
        held=cut.HeldCopy(child.pid,os.pidfd_open(child.pid),'test',{},{} )
        try:
            began=time.monotonic()
            with ThreadPoolExecutor(max_workers=1) as pool:
                observed=pool.submit(held.wait_exit_seconds,began,2)
                child.terminate();child.wait(timeout=2)
                elapsed=observed.result(timeout=2)
                time.sleep(0.05)  # Service can still be draining after pidfd exit.
            self.assertIsNotNone(elapsed)
            self.assertLess(elapsed,time.monotonic()-began)
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)
            held.release()

    def test_live_process_timeout_and_late_observation_are_not_exit_proof(self):
        child=subprocess.Popen(['/bin/sleep','10'])
        held=cut.HeldCopy(child.pid,os.pidfd_open(child.pid),'test',{},{} )
        try:
            self.assertIsNone(held.wait_exit_seconds(time.monotonic(),0.02))
            self.assertIsNone(child.poll())
            began=time.monotonic()-1
            child.terminate();child.wait(timeout=2)
            self.assertIsNone(held.wait_exit_seconds(began,0.02))
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=2)
            held.release()


if __name__ == '__main__':unittest.main()
