"""Denial boundaries of the QA-only exact ACL helper observer (no real services)."""
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import uuid

import bound_container_acl_cut as cut
from validate_container_file_staging import load


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class AclFaultScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix='wd-acl-cut-test-');self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);self.source=self.root/'source';self.source.mkdir();self.file=self.source/'acl-file.bin';self.file.write_bytes(b'unchanged full content');self.file.chmod(0o640)
        os.setxattr(self.file,'user.qa',b'unchanged attribute');self.module=load(Path(__file__).resolve().parents[1]/'host/whaledeck-container-acl.py','acl_cut_unit_module')
        self.module_patch=patch.object(cut,'acl_module',return_value=self.module);self.module_patch.start();self.addCleanup(self.module_patch.stop)
        self.operation=uuid.uuid4().hex;old=cut.fingerprint(self.file.stat());base=self.module.base_acl(0o640);entries=[base[0],{'tag':'user','id':2201,'permissions':6},{'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':4},base[2]]
        digest=hashlib.sha256(b'whaledeck-container-acl-attributes-v1\n'+self.module.encode([['user.qa',base64.b64encode(b'unchanged attribute').decode()]])).hexdigest().upper()
        self.managed={'SchemaVersion':1,'Command':{'Location':{'ContainerId':'a'*64,'InstanceId':'100:test','Path':'work/acl-file.bin'},'AclType':'access','Entries':[{'Tag':e['tag'],'Id':e['id'],'Permissions':e['permissions']} for e in entries],'ExpectedVersion':'E'*64},'SourceIdentity':dict(zip(('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs'),old[:8])),
            'ParentIdentity':{'Device':self.source.stat().st_dev,'Inode':self.source.stat().st_ino},'ParentVersion':'P'*64,'SourceChangedAtUnixNs':old[8],'OwnerUid':1000,'OwnerGid':1000,'Kind':'File','TargetProof':'T'*64,'AttributesDigest':digest,'NoChange':False,'AccessAcl':[{'Tag':e['tag'],'Id':e['id'],'Permissions':e['permissions']} for e in base],'DefaultAcl':[],'TargetMode':0o640,'Fingerprint':''}
        from validate_container_acl_browser import managed_plan
        self.plan=managed_plan(self.managed);self.plan['fingerprint']=self.managed['Fingerprint']=hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+self.module.encode({k:v for k,v in self.plan.items() if k!='fingerprint'})).hexdigest().upper()
        self.record={'schemaVersion':1,'operationId':self.operation,'owner':'B'*64,'plan':self.plan,'state':'Prepared','aclConfirmed':None,'actualIdentity':None,'version':'','historyMayExist':True,'historyVerified':True,'historyDigest':'D'*64}
        self.row={'execution_job_id':str(uuid.UUID(self.operation)),'owner_hash':'B'*64,'container_id':'a'*64,'instance_id':'100:test','relative_path':'work/acl-file.bin','plan_json':json.dumps(self.managed)}
        self.path=self.root/(self.operation+'.json');self.path.write_text(json.dumps(self.record));self.path.chmod(0o600)

    def test_full_sql_root_plan_and_actor_binding(self):
        with patch.object(cut,'ROOT',self.root):
            self.assertEqual(cut.record_for(self.row),self.record)
            for key,value in [('owner_hash','C'*64),('container_id','b'*64),('instance_id','200:foreign'),('relative_path','work/foreign')]:
                with self.subTest(key=key),self.assertRaises(AssertionError):cut.record_for({**self.row,key:value})

    def test_acl_entry_or_mask_tamper_is_not_authorization(self):
        self.plan['command']['entries'][1]['id']=0;self.path.write_text(json.dumps(self.record))
        with patch.object(cut,'ROOT',self.root),self.assertRaises(AssertionError):cut.record_for(self.row)

    def test_sql_full_original_acl_and_ctime_must_agree(self):
        for key in ('AccessAcl','SourceChangedAtUnixNs','AttributesDigest','TargetMode','NoChange'):
            changed=copy.deepcopy(self.managed)
            if key=='AccessAcl':changed[key][1]['Permissions']=0
            elif key=='AttributesDigest':changed[key]='F'*64
            elif key=='NoChange':changed[key]=True
            else:changed[key]+=1
            with self.subTest(key=key),patch.object(cut,'ROOT',self.root),self.assertRaises(AssertionError):cut.record_for({**self.row,'plan_json':json.dumps(changed)})

    def test_only_full_original_inode_and_acls_count(self):
        self.assertEqual(cut.original_proof(self.record,self.source)['originalIdentity'],self.plan['sourceIdentity'])
        os.chmod(self.file,0o600)
        with self.assertRaisesRegex(AssertionError,'SOURCE_CHANGED'):cut.original_proof(self.record,self.source)

    def test_original_acl_and_attributes_are_independently_read(self):
        for key,value in [('accessAcl',self.module.base_acl(0o600)),('attributesDigest','F'*64)]:
            record=copy.deepcopy(self.record);record['plan'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.original_proof(record,self.source)

    def test_applying_or_terminal_is_not_a_pre_syscall_cut(self):
        for state in ('Applying','Completed','Aborted','Unknown'):
            with self.subTest(state=state):self.assertIsNone(cut.original_proof({**self.record,'state':state},self.source))

    def test_foreign_path_symlink_and_parent_are_rejected(self):
        for path in ('/etc/shadow','work/../acl-file.bin','work/nested/file','work/foreign'):
            changed=copy.deepcopy(self.record);changed['plan']['command']['path']=path
            with self.subTest(path=path),self.assertRaises(AssertionError):cut.original_proof(changed,self.source)
        old=self.source/'old';self.file.rename(old);self.file.symlink_to(old)
        with self.assertRaises(AssertionError):cut.original_proof(self.record,self.source)
        self.file.unlink();old.rename(self.file);changed=copy.deepcopy(self.record);changed['plan']['parentIdentity']=[1,2]
        with self.assertRaises(AssertionError):cut.original_proof(changed,self.source)

    def test_foreign_process_with_both_fds_is_not_acl_helper(self):
        lock=self.root/'lock';lock.touch(mode=0o600)
        with lock.open('rb') as stream:
            fd=os.open(self.source,os.O_RDONLY|os.O_DIRECTORY)
            try:
                child=subprocess.Popen(['/bin/sleep','5'],pass_fds=(fd,stream.fileno()))
                try:self.assertFalse(cut.bound_fds(child.pid,cut.identity(self.source),cut.identity(lock)))
                finally:child.terminate();child.wait(timeout=2)
            finally:os.close(fd)

    def test_non_owned_source_refused_before_pidfd(self):
        with patch.object(cut.os,'pidfd_open',side_effect=AssertionError('must not open')) as opened:
            with self.assertRaises(AssertionError):cut.freeze_before_acl(self.row,self.source,100)
            opened.assert_not_called()

    def test_nonempty_wrong_mode_hardlinked_and_symlink_lock_refused(self):
        lock=self.root/'lock';lock.touch(mode=0o600);self.assertEqual(cut.operation_lock(lock),cut.identity(lock))
        lock.write_bytes(b'x')
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        lock.write_bytes(b'');lock.chmod(0o644)
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        lock.chmod(0o600);os.link(lock,self.root/'linked')
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        link=self.root/'symlink';link.symlink_to(lock)
        with self.assertRaises(AssertionError):cut.operation_lock(link)


class AclFailureCleanupTests(unittest.TestCase):
    def setUp(self):
        import validate_container_acl_faults as driver
        self.driver=driver;self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup);self.directory=Path(self.temporary.name)

    def cleanup(self, identifier='a', browser_started=True):
        return self.driver.cleanup_original_after_failure(identifier,self.directory,self.directory,None,None,b'bytes',{'original':'facts'},browser_started=browser_started)

    def test_lost_registration_signal_never_authorizes_deletion(self):
        with patch.object(self.driver,'remove_proved') as remove,patch.object(self.driver,'inventory') as read:
            self.assertFalse(self.cleanup(None));remove.assert_not_called();read.assert_not_called()

    def test_active_unknown_barrier_or_lease_preserves_original_source(self):
        base={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':None}
        for delta in ({'state':'Unknown'},{'state':'Applying'},{'publish_barrier':True},{'lease_token':'live'},{'lease_expires_at_utc':'future'}):
            with self.subTest(delta=delta),patch.object(self.driver,'wait_settled',return_value={'row':{**base,**delta}}),patch.object(self.driver,'remove_proved') as remove:
                self.assertFalse(self.cleanup());remove.assert_not_called()

    def test_nonterminal_root_and_changed_source_preserved_after_sql_terminal(self):
        row={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':'execution'}
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Preparing'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertFalse(self.cleanup());remove.assert_not_called()
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Aborted'}),patch.object(self.driver,'inventory',return_value={'changed':'facts'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertFalse(self.cleanup());remove.assert_not_called()

    def test_terminal_proved_original_can_be_removed(self):
        row={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':'execution'}
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Aborted'}),patch.object(self.driver,'inventory',return_value={'original':'facts'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertTrue(self.cleanup());remove.assert_called_once()


if __name__=='__main__':unittest.main()
