"""Actual isolated files and crash recovery; NO product services/HDD/UI evidence."""
import copy
import contextlib
import hashlib
import json
import os
from pathlib import Path
import select
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid

import owned_acl_source_quarantine as source


def encode(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class OwnedSourceQuarantineTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='wd-source-quarantine-test-')
        self.addCleanup(self.tmp.cleanup)
        self.folder = Path(self.tmp.name); self.namespace = self.folder/'namespace'; self.journal = self.folder/'journal'
        self.namespace.mkdir(mode=0o700); self.journal.mkdir(mode=0o700)
        self.fd = os.open(self.namespace, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, self.fd)
        self.jfd = os.open(self.journal, os.O_RDONLY | os.O_DIRECTORY)
        self.addCleanup(os.close, self.jfd)
        self.oldname = uuid.uuid4().hex + '.json'
        (self.namespace/self.oldname).write_bytes(b'{"old":"untouched"}')
        (self.namespace/self.oldname).chmod(0o600)
        self.old = source.catalogue(self.fd)
        operation = str(uuid.uuid4()); intent = str(uuid.uuid4())
        self.capture = uuid.UUID(operation).hex; self.name = self.capture + '.json'
        self.path = self.namespace/self.name
        self.baseline = dict(subject='own-subject',containerId='a'*64,instanceId='100:owned')
        self.managed = dict(SchemaVersion=1,Command=dict(Location=dict(ContainerId='a'*64,InstanceId='100:owned',Path='work/acl-file.bin'),
            AclType='access',Entries=[],ExpectedVersion='version'),TargetProof='proof',ParentIdentity=dict(Device=1,Inode=2),
            ParentVersion='parent',OwnerUid=1000,OwnerGid=1000,Fingerprint='fingerprint',Kind='File',SourceIdentity=dict(
                Device=1,Inode=3,Mode=416,OwnerUid=2100,OwnerGid=3100,LinkCount=1,SizeBytes=2097159,ModifiedAtUnixNs=1),
            SourceChangedAtUnixNs=2,AttributesDigest='attrs',NoChange=False,AccessAcl=[],DefaultAcl=[],TargetMode=416)
        from validate_container_acl_browser import managed_plan
        plan = managed_plan(self.managed)
        self.metadata = dict(schemaVersion=1,resourceType='ContainerAclMetadata',storageId='database-platform.backup.hdd',
                             operationId=self.capture,owner='own-owner',plan=plan,createdAtUnixMs=1700000000000)
        self.metadata['historyDigest'] = hashlib.sha256(b'whaledeck-container-acl-history-v1\n' + encode(self.metadata)).hexdigest().upper()
        self.raw = encode(self.metadata); self.path.write_bytes(self.raw); self.path.chmod(0o600)
        self.root = dict(operationId=self.capture,owner='own-owner',plan=plan,state='Completed',aclConfirmed=True,
                         historyVerified=True,historyMayExist=True,historyDigest=self.metadata['historyDigest'])
        self.root_raw = encode(self.root)
        self.result = dict(id=intent,executionJobId=operation,name='access',state='Succeeded')
        self.proof = dict(result=self.result,rootSha256=hashlib.sha256(self.root_raw).hexdigest(),
                          hddSha256=hashlib.sha256(self.raw).hexdigest(),hddRecords=1,before={'access':'old'},after={'access':'new'})
        self.sql = dict(row=dict(id=intent,execution_job_id=operation,restore_source_json=None,actor_subject='own-subject',
            container_id='a'*64,instance_id='100:owned',relative_path='work/acl-file.bin',state='Succeeded',completed_at_utc='completed',
            root_started_at_utc='started',publish_barrier=False,lease_token=None,lease_expires_at_utc=None,acl_confirmed=True,
            history_verified=True,history_may_exist=True,owner_hash='own-owner',history_digest=self.metadata['historyDigest'],
            plan_json=json.dumps(self.managed)),pending=0,accepted=1,confirmed=1,planJob={'state':'Succeeded'},executionJob={'state':'Succeeded'})
        self.attest = unittest.mock.Mock()
        self.tool = source.OwnedSourceQuarantine(self.fd, self.jfd, self.attest)

    def arm(self):
        return self.tool.arm(self.baseline,self.proof,self.sql,self.root_raw,self.old)

    def test_exact_inode_bytes_attributes_restored_old_nine_fields_preserved(self):
        before = source.catalogue(self.fd); self.arm()
        with self.tool.hidden() as value:
            self.assertFalse(self.path.exists()); self.assertTrue(value['canonicalSourceAbsent'])
            self.assertEqual(source.catalogue(self.fd),self.old)
            held = self.namespace/('.wd-qa-acl-source-'+self.capture)/self.name
            self.assertEqual(held.read_bytes(),self.raw)
        after = source.catalogue(self.fd)
        self.assertEqual(after[self.oldname],before[self.oldname])
        self.assertEqual(after[self.name]['frame'][:8],before[self.name]['frame'][:8])
        self.assertGreaterEqual(after[self.name]['frame'][8],before[self.name]['frame'][8])
        self.assertEqual(after[self.name]['sha256'],before[self.name]['sha256'])
        self.assertEqual(set(os.listdir(self.namespace)),{self.name,self.oldname})
        self.assertTrue(self.tool.restore()['alreadyPresent'])
        self.assertGreaterEqual(self.attest.call_count,5)

    def test_python_failure_after_hold_restores_original(self):
        self.arm()
        with self.assertRaisesRegex(RuntimeError,'test failure'):
            with self.tool.hidden(): raise RuntimeError('test failure')
        self.assertEqual(self.path.read_bytes(),self.raw)

    def test_sigkill_after_hide_new_process_recovers_from_durable_journal(self):
        self.arm()
        program = '''import os,signal,sys
from owned_acl_source_quarantine import OwnedSourceQuarantine
fd=os.open(sys.argv[1],os.O_RDONLY|os.O_DIRECTORY); jfd=os.open(sys.argv[2],os.O_RDONLY|os.O_DIRECTORY)
tool=OwnedSourceQuarantine(fd,jfd,lambda fd:None)
tool.hold(); print('hidden',flush=True)
signal.pause()
'''
        child = subprocess.Popen([sys.executable,'-c',program,str(self.namespace),str(self.journal)],
            stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,cwd=Path(__file__).parent)
        try:
            self.assertTrue(select.select([child.stdout],[],[],8)[0]); self.assertEqual(child.stdout.readline(),'hidden\n')
            self.assertFalse(self.path.exists()); child.kill(); self.assertEqual(child.wait(timeout=5),-signal.SIGKILL)
        finally:
            if child.poll() is None: child.kill(); child.wait(timeout=5)
            child.stdout.close(); child.stderr.close()
        recover = '''import os,sys,json
from owned_acl_source_quarantine import OwnedSourceQuarantine
fd=os.open(sys.argv[1],os.O_RDONLY|os.O_DIRECTORY); jfd=os.open(sys.argv[2],os.O_RDONLY|os.O_DIRECTORY)
try: print(json.dumps(OwnedSourceQuarantine(fd,jfd,lambda fd:None).restore()))
finally: os.close(fd);os.close(jfd)
'''
        result = subprocess.run([sys.executable,'-c',recover,str(self.namespace),str(self.journal)],
            capture_output=True,text=True,timeout=10,cwd=Path(__file__).parent,check=True)
        self.assertTrue(json.loads(result.stdout)['originalRestored']); self.assertEqual(self.path.read_bytes(),self.raw)
        self.assertEqual(source.catalogue(self.fd)[self.oldname],self.old[self.oldname])

    def test_occupied_destination_preserves_both_copies_no_overwrite(self):
        self.arm(); self.tool.hold(); self.path.write_bytes(b'occupied'); self.path.chmod(0o600)
        with self.assertRaises(AssertionError): self.tool.restore()
        self.assertEqual(self.path.read_bytes(),b'occupied')
        self.assertEqual((self.namespace/('.wd-qa-acl-source-'+self.capture)/self.name).read_bytes(),self.raw)

    def test_existing_old_capture_cannot_be_armed(self):
        before = source.catalogue(self.fd)
        with self.assertRaisesRegex(AssertionError,'OLD_CAPTURE'): self.tool.arm(self.baseline,self.proof,self.sql,self.root_raw,before)
        self.assertEqual(source.catalogue(self.fd),before); self.assertFalse((self.journal/source.JOURNAL).exists())

    def test_foreign_actor_target_instance_owner_or_nonterminal_sql_refused(self):
        for key,value in [('actor_subject','other'),('container_id','b'*64),('instance_id','other'),('relative_path','work/other'),
                          ('state','Unknown'),('owner_hash','foreign'),('lease_token','active'),('publish_barrier',True),('acl_confirmed',None),
                          ('restore_source_json','{}')]:
            sql=copy.deepcopy(self.sql);sql['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError): self.tool.arm(self.baseline,self.proof,sql,self.root_raw,self.old)
        self.assertEqual(self.path.read_bytes(),self.raw);self.assertFalse((self.journal/source.JOURNAL).exists())

    def test_proof_hash_or_managed_sql_plan_cannot_be_substituted(self):
        for item in ('rootSha256','hddSha256'):
            with self.subTest(item=item),self.assertRaises(AssertionError): self.tool.arm(self.baseline,{**self.proof,item:'0'*64},self.sql,self.root_raw,self.old)
        sql=copy.deepcopy(self.sql); plan=copy.deepcopy(self.managed);plan['TargetMode']=0;sql['row']['plan_json']=json.dumps(plan)
        with self.assertRaises(AssertionError): self.tool.arm(self.baseline,self.proof,sql,self.root_raw,self.old)

    def test_wrong_hdd_digest_refused_even_with_matching_raw_hash(self):
        metadata=copy.deepcopy(self.metadata);metadata['createdAtUnixMs']+=1;raw=encode(metadata)
        self.path.write_bytes(raw)
        with self.assertRaises(AssertionError): self.tool.arm(self.baseline,{**self.proof,'hddSha256':hashlib.sha256(raw).hexdigest()},self.sql,self.root_raw,self.old)

    def test_unrelated_capture_byte_or_metadata_change_refuses_hold(self):
        self.arm(); oldpath=self.namespace/self.oldname; oldpath.write_bytes(b'changed')
        with self.assertRaisesRegex(AssertionError,'UNRELATED_CAPTURE'): self.tool.hold()
        self.assertEqual(self.path.read_bytes(),self.raw);self.assertEqual(oldpath.read_bytes(),b'changed')

    def test_unexpected_extra_capture_refuses_arm(self):
        extra=self.namespace/(uuid.uuid4().hex+'.json');extra.write_bytes(b'new');extra.chmod(0o600)
        with self.assertRaisesRegex(AssertionError,'UNRELATED_CAPTURE'):self.arm()

    def test_modified_or_replaced_quarantined_original_is_retained_not_restored(self):
        self.arm();self.tool.hold();held=self.namespace/('.wd-qa-acl-source-'+self.capture)/self.name
        held.write_bytes(b'changed')
        with self.assertRaisesRegex(AssertionError,'ORIGINAL_CHANGED'):self.tool.restore()
        self.assertFalse(self.path.exists());self.assertEqual(held.read_bytes(),b'changed')

    def test_symlink_hardlink_fifo_and_nonprivate_capture_refused(self):
        self.path.chmod(0o644)
        with self.assertRaises(AssertionError): self.arm()
        self.path.chmod(0o600); os.link(self.path,self.folder/'linked')
        with self.assertRaises(AssertionError): self.arm()
        (self.folder/'linked').unlink();self.path.unlink();self.path.symlink_to(self.namespace/self.oldname)
        with self.assertRaises(OSError): self.arm()
        self.path.unlink();os.mkfifo(self.path,0o600)
        with self.assertRaises(AssertionError): self.arm()

    def test_preexisting_quarantine_not_reused_or_cleaned_if_foreign(self):
        self.arm();folder=self.namespace/('.wd-qa-acl-source-'+self.capture);folder.mkdir(mode=0o700)
        (folder/'foreign').write_bytes(b'keep')
        with self.assertRaises(FileExistsError):self.tool.hold()
        with self.assertRaises(FileNotFoundError):self.tool.restore()
        self.assertEqual(self.path.read_bytes(),self.raw);self.assertEqual((folder/'foreign').read_bytes(),b'keep')

    def test_preexisting_empty_directory_is_preserved_without_own_directory_proof(self):
        self.arm();folder=self.namespace/('.wd-qa-acl-source-'+self.capture);folder.mkdir(mode=0o700)
        original=folder.stat().st_ino
        with self.assertRaises(FileNotFoundError) as failure:
            with self.tool.hidden():self.fail('preexisting directory must refuse hold')
        # The cleanup attempt also refuses an unproved directory. FileNotFoundError
        # derives from OSError; the original canonical capture remains untouched.
        self.assertIsInstance(failure.exception.__context__,FileExistsError)
        self.assertEqual(folder.stat().st_ino,original);self.assertEqual(self.path.read_bytes(),self.raw)

    def test_replaced_directory_with_same_name_does_not_authorize_restore(self):
        self.arm();self.tool.hold();folder=self.namespace/('.wd-qa-acl-source-'+self.capture)
        folder.rename(self.namespace/'held-original');folder.mkdir(mode=0o700)
        with self.assertRaises(AssertionError):self.tool.restore()
        self.assertFalse(self.path.exists());self.assertTrue((self.namespace/'held-original'/self.name).exists())

    def test_no_replace_syscall_cannot_overwrite_existing_destination(self):
        with self.assertRaises(FileExistsError): source.move_without_replace(self.fd,self.name,self.fd,self.oldname)
        self.assertEqual(self.path.read_bytes(),self.raw);self.assertEqual(source.catalogue(self.fd)[self.oldname],self.old[self.oldname])

    def test_attestation_failure_before_mutation_preserves_capture(self):
        self.arm();self.attest.side_effect=RuntimeError('attestation refused')
        with self.assertRaisesRegex(RuntimeError,'attestation refused'):self.tool.hold()
        self.assertEqual(self.path.read_bytes(),self.raw)

    def test_duplicate_or_traversal_journal_and_changed_permissions_refused(self):
        self.arm();path=self.journal/source.JOURNAL;original=path.read_bytes()
        for raw in (b'{"schemaVersion":1,"schemaVersion":1}',encode({**json.loads(original),'name':'../escape'}),
                    encode({**json.loads(original),'quarantine':'../escape'})):
            path.write_bytes(raw)
            with self.assertRaises(AssertionError):self.tool.restore()
        path.write_bytes(original);path.chmod(0o644)
        with self.assertRaises(AssertionError):self.tool.restore()
        self.assertEqual(self.path.read_bytes(),self.raw)

    def test_journal_must_be_private_and_distinct_from_namespace(self):
        with self.assertRaisesRegex(AssertionError,'JOURNAL_IS_NAMESPACE'):source.OwnedSourceQuarantine(self.fd,self.fd,self.attest)
        self.journal.chmod(0o755)
        with self.assertRaises(AssertionError):source.OwnedSourceQuarantine(self.fd,self.jfd,self.attest)

    def test_restore_after_arm_without_hide_does_not_move_anything(self):
        before=source.catalogue(self.fd);self.arm();self.assertTrue(self.tool.restore()['alreadyPresent'])
        self.assertEqual(source.catalogue(self.fd),before)

    def test_hidden_failure_after_rename_still_restores_from_journal(self):
        self.arm();original=self.tool.old_unchanged
        def fail_once(value,*,present):
            if not present:
                self.tool.old_unchanged=original
                raise RuntimeError('post-move observation failed')
            return original(value,present=present)
        self.tool.old_unchanged=fail_once
        with self.assertRaisesRegex(RuntimeError,'post-move observation failed'):
            with self.tool.hidden():self.fail('hold should fail')
        self.assertEqual(self.path.read_bytes(),self.raw)


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class NamespaceBindingTests(unittest.TestCase):
    def test_installed_factory_uses_fixed_namespace_and_writer_attestation(self):
        with tempfile.TemporaryDirectory(prefix='wd-source-namespace-test-') as tmp:
            base=Path(tmp);(base/'container-acl-v1').mkdir(mode=0o700)
            parent=os.open(base,os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage=unittest.mock.Mock()
                @contextlib.contextmanager
                def target(**kwargs):
                    self.assertEqual(kwargs,{'create_namespace':False});yield {'proof':'isolated-fixture'},parent
                storage.target=target;storage.f.open_at.side_effect=lambda fd,name,flags:os.open(name,flags,dir_fd=fd)
                with source.installed_namespace(storage) as (fd,attest):
                    self.assertEqual(os.fstat(fd).st_ino,(base/'container-acl-v1').stat().st_ino)
                    attest(fd);self.assertTrue(all(c.kwargs=={'read_only':False} for c in storage.attest.call_args_list))
                    (base/'container-acl-v1').rename(base/'original')
                    (base/'container-acl-v1').mkdir(mode=0o700)
                    with self.assertRaisesRegex(AssertionError,'CANONICAL_NAMESPACE_CHANGED'):attest(fd)
            finally:os.close(parent)


if __name__ == '__main__': unittest.main()
