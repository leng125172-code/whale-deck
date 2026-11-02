"""Actual temporary inode transitions for QA's post-fchown observation boundary."""
import copy
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import bound_container_ownership_cut as cut


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class OwnershipResultProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='wd-owner-result-test-');self.addCleanup(self.temp.cleanup)
        self.source=Path(self.temp.name);self.file=self.source/'original.bin';self.file.write_bytes(b'preserved-owner-result');self.file.chmod(0o640)
        self.old=cut.fingerprint(self.file.stat())
        self.record={'state':'Applying','ownerConfirmed':None,'actualIdentity':None,'version':'','historyMayExist':True,'historyVerified':True,'historyDigest':'B'*64,
            'plan':{'kind':'File','noChange':False,'parentIdentity':list(cut.identity(self.source)),'sourceIdentity':self.old,
                'attributesDigest':hashlib.sha256(b'whaledeck-container-ownership-attributes-v1\n[]').hexdigest().upper(),
                'command':{'path':'work/original.bin','uid':2200,'gid':3200}}}
    def change(self):os.chown(self.file,2200,3200)

    def test_only_actual_changed_inode_with_unconfirmed_receipt_is_counted(self):
        self.assertIsNone(cut.changed_proof(self.record,self.source));self.change()
        proof=cut.changed_proof(self.record,self.source)
        self.assertEqual(proof['changedIdentity'][3:5],[2200,3200]);self.assertEqual(proof['originalIdentity'],self.old)
        self.assertEqual(self.file.read_bytes(),b'preserved-owner-result')

    def test_other_receipt_phases_cannot_count_as_pre_receipt_post_owner_cut(self):
        self.change()
        for state in ('Preparing','Prepared','Completed','Aborted','Unknown'):
            with self.subTest(state=state):self.assertIsNone(cut.changed_proof({**self.record,'state':state},self.source))

    def test_foreign_owner_mode_or_size_never_authorizes_a_signal(self):
        for field in ('owner','mode','size'):
            with self.subTest(field=field):
                self.file.write_bytes(b'preserved-owner-result');self.file.chmod(0o640)
                os.utime(self.file,ns=(self.old[7],self.old[7]))
                self.change()
                self.assertIsNotNone(cut.changed_proof(self.record,self.source))
                if field=='owner':os.chown(self.file,2201,3200)
                elif field=='mode':self.file.chmod(0o600)
                else:self.file.write_bytes(b'wrong-size')
                with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)

    def test_replaced_inode_and_hardlink_are_refused(self):
        self.change();self.file.rename(self.source/'retained-original');self.file.write_bytes(b'preserved-owner-result');self.file.chmod(0o640);self.change()
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)
        self.record['plan']['sourceIdentity'][:2]=cut.fingerprint(self.file.stat())[:2]
        os.link(self.file,self.source/'other')
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)

    def test_symlink_and_arbitrary_relative_path_are_refused(self):
        self.change();other=self.source/'other';self.file.rename(other);self.file.symlink_to(other)
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)
        for path in ('/etc/shadow','work/../other','work/nested/file'):
            row=copy.deepcopy(self.record);row['plan']['command']['path']=path
            with self.subTest(path=path), self.assertRaises(AssertionError):cut.changed_proof(row,self.source)

    def test_attribute_drift_is_not_a_post_owner_cut(self):
        self.change();os.setxattr(self.file,'user.whaledeck-probe',b'changed')
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)

    def test_unverified_history_or_already_confirmed_owner_is_refused(self):
        self.change()
        for key,value in [('historyVerified',False),('ownerConfirmed',True),('version','A'*64)]:
            with self.subTest(key=key), self.assertRaises(AssertionError):cut.changed_proof({**self.record,key:value},self.source)

    def test_non_owned_scope_cannot_reach_pidfd(self):
        row={'container_id':'a'*64,'instance_id':'100:started','execution_job_id':'11111111-2222-3333-4444-555555555555'}
        with patch.object(cut.os,'pidfd_open',side_effect=AssertionError('must not open pidfd')) as opened:
            with self.assertRaises(AssertionError):cut.freeze_after_owner(row,self.source,100)
            opened.assert_not_called()


if __name__=='__main__':unittest.main()
