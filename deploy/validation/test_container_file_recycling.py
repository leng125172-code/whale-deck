"""Real temporary Linux file/HDD-format I/O; explicit namespace/liveness/attestation doubles.

Not an installed kernel, real Docker/RPC, physical HDD, business or UI acceptance.
"""
import copy
import hashlib
import json
import os
import signal
import select
import stat
import struct
from types import SimpleNamespace
import unittest
import uuid
from unittest.mock import Mock, patch
import test_container_file_staging as staging
import test_file_backups as backups

candidate=staging.load('whaledeck-container-recycling.py','container_recycle_candidate')
permissions=staging.load('whaledeck-container-permissions.py','recycle_history_base')
directories=staging.load('whaledeck-container-directories.py','recycle_directory_base')
publisher=staging.load('whaledeck-container-publish.py','recycle_writability')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class LinuxContainerRecycleTests(unittest.TestCase):
    def setUp(self):
        self.disk=backups.FileBackupTests();self.disk.setUp();self.addCleanup(self.disk.tearDown)
        self.f,self.r,self.root=self.disk.engine.f,staging.read_engine,self.disk.root
        self.state=self.disk.disk/'recycle-state';self.state.mkdir(mode=0o700)
        fd=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY);self.addCleanup(os.close,fd)
        self.session=SimpleNamespace(root=fd,request=dict(containerId=staging.ID,instanceId=str(os.getpid())+':2026-10-30T01:02:03.123456789Z'),
            mounts=b'M'*32,identity=('12345',os.getpid(),888),current=Mock(),revalidate=Mock(),container={'HostConfig':{'ReadonlyRootfs':False},'Mounts':[]})
        u=staging.uploads.ContainerUploads(self.r,self.f,self.state);p=publisher.ContainerPublisher(u,SimpleNamespace(u=u))
        self.history=candidate.history_type(permissions.ContainerPermissionHistory)(self.disk.engine)
        self.engine=candidate.recycling_type(directories.ContainerDirectories)(u,p,self.state,self.history)
        self.leaf=self.root/'可回收.bin';self.content=hashlib.shake_256(b'container-recycle-real-bytes').digest(1024**2+7)
        self.leaf.write_bytes(self.content);self.leaf.chmod(0o640);os.chown(self.leaf,2100,3100)
        os.setxattr(self.leaf,'user.recycle',b'original metadata');os.utime(self.leaf,ns=(1700000000123456789,1700000000123456789))
        self.target=self.disk.target/self.history.NAMESPACE

    def request(self):
        c={**self.session.request,'path':self.leaf.name,'expectedVersion':self.r.version(self.session.request,self.leaf.name,self.leaf.stat())}
        return dict(operationId=uuid.uuid4().hex,owner=staging.OWNER,plan=self.engine.plan(c,self.session))

    def record(self,q):return json.loads((self.state/(q['operationId']+'.json')).read_text())
    def saved(self,q):return json.loads((self.target/(q['operationId']+'.json')).read_text())
    def retained(self,q):return self.root/self.engine.PRIVATE/(q['operationId']+'.trash')

    def kill_at(self,q,phase,restore=False):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                (self.engine.restore if restore else self.engine.apply)(q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0);self.assertTrue(ready,'test child exceeded deadline')
        finally:os.close(fd)
        self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_plan_is_read_only_and_binds_full_container_file_parent_bytes_and_attributes(self):
        before=self.r.fingerprint(self.leaf.stat());parent=self.r.fingerprint(self.root.stat());q=self.request()
        self.assertEqual(q['plan']['sourceIdentity'],list(before));self.assertEqual(q['plan']['sha256'],hashlib.sha256(self.content).hexdigest().upper())
        self.assertEqual(q['plan']['attributes'],[['user.recycle','b3JpZ2luYWwgbWV0YWRhdGE=']])
        self.assertEqual(self.r.fingerprint(self.leaf.stat()),before);self.assertEqual(self.r.fingerprint(self.root.stat()),parent)
        self.assertEqual(list(self.state.iterdir()),[]);self.assertFalse(self.target.exists())

    def test_trash_retains_exact_inode_and_full_backup_then_restores_only_empty_original(self):
        q=self.request();before=self.leaf.stat();r=self.engine.apply(q,self.session)
        self.assertEqual((r['state'],r['sourceRetained'],r['bindingCurrent']),('Trashed',True,True))
        self.assertFalse(self.leaf.exists());self.assertEqual(self.retained(q).stat().st_ino,before.st_ino)
        self.assertEqual(self.retained(q).read_bytes(),self.content);self.assertEqual((self.target/(q['operationId']+'.data')).read_bytes(),self.content)
        m=self.saved(q);self.assertEqual((m['resourceType'],m['captureKind'],m['captureState']),('ContainerFileTrash','Trash','Trashed'))
        self.assertNotIn('rootId',m);self.assertEqual(m['plan'],q['plan']);self.assertEqual(stat.S_IMODE(self.retained(q).parent.stat().st_mode),0o700)
        with patch.object(self.f,'exchange',side_effect=AssertionError('must not trash twice')):
            self.assertTrue(self.engine.apply(q,self.session)['replayed'])
        restored=self.engine.restore(q,self.session);after=self.leaf.stat()
        self.assertEqual((restored['state'],restored['sourceRestored'],restored['bindingCurrent']),('Restored',True,True))
        self.assertEqual(self.leaf.read_bytes(),self.content);self.assertEqual((after.st_ino,after.st_mode,after.st_uid,after.st_gid,after.st_mtime_ns),(before.st_ino,before.st_mode,2100,3100,before.st_mtime_ns))
        self.assertEqual(os.getxattr(self.leaf,'user.recycle'),b'original metadata');self.assertEqual(self.saved(q)['captureState'],'Restored')
        with patch.object(self.f,'exchange',side_effect=AssertionError('must not move on replay')):
            self.assertTrue(self.engine.restore(q,self.session)['replayed']);self.assertEqual(self.engine.apply(q,self.session)['state'],'Restored')

    def test_restore_does_not_overwrite_late_destination_or_touch_retained_source(self):
        q=self.request();self.engine.apply(q,self.session);self.leaf.write_bytes(b'new foreign file')
        before=self.r.fingerprint(self.leaf.stat())
        with self.assertRaisesRegex(self.r.Rejected,'TARGET_EXISTS'):self.engine.restore(q,self.session)
        self.assertEqual(self.leaf.read_bytes(),b'new foreign file');self.assertEqual(self.r.fingerprint(self.leaf.stat()),before)
        self.assertEqual(self.retained(q).read_bytes(),self.content);self.assertEqual(self.record(q)['state'],'Trashed')

    def test_real_process_kill_before_and_after_retention_is_observe_only(self):
        for phase,wanted in [('Preparing','Aborted'),('HistoryVerified','Aborted'),('Retaining','Aborted'),('Retained','Trashed')]:
            # Every phase gets a fresh source and operation, never resurrects old data.
            if not self.leaf.exists():self.fail('earlier phase unexpectedly removed source')
            q=self.request();self.kill_at(q,phase)
            with patch.object(self.f,'exchange',side_effect=AssertionError('recovery must not move')):
                r=self.engine.inspect(q,self.session);self.assertEqual(r['state'],wanted)
            self.assertEqual(self.leaf.exists(),wanted!='Trashed')
            if wanted=='Trashed':self.assertEqual(self.retained(q).read_bytes(),self.content)

    def test_real_process_restore_kill_before_publish_keeps_trash_without_replay(self):
        q=self.request();self.engine.apply(q,self.session);self.kill_at(q,'Restoring',restore=True)
        with patch.object(self.f,'exchange',side_effect=AssertionError('inspect must not restore')):
            self.assertEqual(self.engine.inspect(q,self.session)['state'],'Trashed')
        self.assertFalse(self.leaf.exists());self.assertEqual(self.retained(q).read_bytes(),self.content)

    def test_real_process_restore_kill_after_publish_records_existing_restored_inode(self):
        q=self.request();self.engine.apply(q,self.session);self.kill_at(q,'RestoredNamespace',restore=True)
        with patch.object(self.f,'exchange',side_effect=AssertionError('inspect must not restore twice')):
            self.assertEqual(self.engine.inspect(q,self.session)['state'],'Restored')
        self.assertEqual(self.leaf.read_bytes(),self.content);self.assertFalse(self.retained(q).exists())

    def test_wrong_owner_instance_plan_and_private_path_are_not_authority(self):
        q=self.request();self.engine.apply(q,self.session)
        for key,bad in [('owner','C'*64),('operationId','../escape')]:
            other=copy.deepcopy(q);other[key]=bad
            with self.subTest(key=key),self.assertRaises((self.r.Rejected,self.f.Rejected)):self.engine.apply(other,self.session)
        with patch.object(self.session,'request',dict(containerId=staging.ID,instanceId='778:2026-10-21T01:02:03.123456789Z')):
            with self.assertRaises(self.r.Rejected):self.engine.restore(q,self.session)
        c={**self.session.request,'path':self.engine.PRIVATE+'/file','expectedVersion':'A'*64}
        with self.assertRaisesRegex(self.r.Rejected,'PROTECTED'):self.engine.plan(c,self.session)

    def test_symlink_hardlink_directory_setid_and_extended_acl_fail_before_capture(self):
        q=self.request();os.chmod(self.leaf,0o4640)
        with self.assertRaises(self.r.Rejected):self.engine.apply(q,self.session)
        os.chmod(self.leaf,0o640);link=self.root/'link';os.link(self.leaf,link)
        with self.assertRaises(self.r.Rejected):self.request()
        link.unlink();self.leaf.unlink();self.leaf.symlink_to(self.root/'absent')
        with self.assertRaises((OSError,self.r.Rejected,self.f.Rejected)):self.request()
        self.assertFalse(self.target.exists())

    def test_backup_capacity_or_corruption_never_allows_source_removal(self):
        q=self.request()
        with patch.object(self.disk.engine,'check_capacity',side_effect=self.f.Rejected('capacity')):
            with self.assertRaises(self.f.Rejected):self.engine.apply(q,self.session)
        self.assertEqual(self.leaf.read_bytes(),self.content);self.assertEqual(self.record(q)['state'],'Aborted')

    def test_corrupt_backup_or_retained_payload_blocks_restore_and_does_not_erase(self):
        q=self.request();self.engine.apply(q,self.session);data=self.target/(q['operationId']+'.data');data.write_bytes(b'corrupt')
        with self.assertRaises(self.f.Rejected):self.engine.restore(q,self.session)
        self.assertEqual(self.retained(q).read_bytes(),self.content);self.assertFalse(self.leaf.exists())
        data.write_bytes(self.content);self.retained(q).write_bytes(b'changed retained')
        with self.assertRaises(self.r.Rejected):self.engine.restore(q,self.session)
        self.assertFalse(self.leaf.exists())

    def test_lost_root_receipt_with_hdd_artifacts_is_unknown_and_never_recaptured(self):
        q=self.request();self.engine.apply(q,self.session);(self.state/(q['operationId']+'.json')).unlink()
        with patch.object(self.history,'capture',side_effect=AssertionError('no recapture')):
            r=self.engine.apply(q,self.session);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['historyMayExist'])
            self.assertIsNone(r['sourceRetained']);self.assertIsNone(r['sourceRestored'])
        self.assertEqual(self.retained(q).read_bytes(),self.content);self.assertFalse(self.leaf.exists())

    def test_source_change_after_plan_is_not_captured_or_renamed(self):
        q=self.request();self.leaf.write_bytes(b'foreign')
        with self.assertRaises(self.r.Rejected):self.engine.apply(q,self.session)
        self.assertEqual(self.leaf.read_bytes(),b'foreign');self.assertFalse(self.target.exists())

    def test_real_posix_acl_and_directory_are_rejected_without_silent_metadata_loss(self):
        raw=struct.pack('<I',2)+b''.join(struct.pack('<HHI',tag,perm,identity) for tag,perm,identity in
            [(1,6,0xffffffff),(2,4,2200),(4,0,0xffffffff),(16,4,0xffffffff),(32,0,0xffffffff)])
        os.setxattr(self.leaf,'system.posix_acl_access',raw)
        with self.assertRaisesRegex(self.r.Rejected,'EXTENDED_METADATA_REQUIRED'):self.request()
        self.assertEqual(self.leaf.read_bytes(),self.content);self.assertFalse(self.target.exists())
        self.leaf.unlink();self.leaf.mkdir()
        with self.assertRaisesRegex(self.r.Rejected,'SOURCE_UNSUPPORTED'):self.request()

    def test_replaced_parent_or_container_namespace_cannot_redirect_retention(self):
        directory=self.root/'parent';directory.mkdir();self.leaf=self.leaf.rename(directory/self.leaf.name)
        c={**self.session.request,'path':'parent/'+self.leaf.name,'expectedVersion':self.r.version(self.session.request,'parent/'+self.leaf.name,self.leaf.stat())}
        q=dict(operationId=uuid.uuid4().hex,owner=staging.OWNER,plan=self.engine.plan(c,self.session))
        directory.rename(self.root/'old-parent');directory.mkdir();foreign=directory/self.leaf.name;foreign.write_bytes(b'foreign')
        with self.assertRaises(self.r.Rejected):self.engine.apply(q,self.session)
        self.assertEqual(foreign.read_bytes(),b'foreign');self.assertEqual((self.root/'old-parent'/self.leaf.name).read_bytes(),self.content)
        self.assertFalse(self.target.exists())

    def test_partial_hdd_copy_is_never_restarted_or_used_to_remove_source(self):
        q=self.request()
        def fail(*args):raise self.f.Rejected('injected full-copy verification failure')
        with patch.object(self.history.storage,'verify_data',side_effect=fail):
            with self.assertRaises(self.f.Rejected):self.engine.apply(q,self.session)
        self.assertEqual(self.leaf.read_bytes(),self.content)
        with patch.object(self.history,'capture',side_effect=AssertionError('no recapture')):
            self.assertEqual(self.engine.apply(q,self.session)['state'],'Aborted')
        self.assertFalse((self.root/self.engine.PRIVATE).exists())

    def test_late_restore_destination_is_not_replaced_and_uncertainty_is_not_false(self):
        q=self.request();self.engine.apply(q,self.session)
        self.engine.checkpoint=lambda phase:self.leaf.write_bytes(b'late foreign source') if phase=='Restoring' else None
        with self.assertRaises(FileExistsError):self.engine.restore(q,self.session)
        with patch.object(self.f,'exchange',side_effect=AssertionError('no automatic retry')):
            r=self.engine.inspect(q,self.session);self.assertEqual(r['state'],'Unknown')
            self.assertIsNone(r['sourceRetained']);self.assertIsNone(r['sourceRestored'])
        self.assertEqual(self.leaf.read_bytes(),b'late foreign source');self.assertEqual(self.retained(q).read_bytes(),self.content)


if __name__=='__main__':unittest.main()
