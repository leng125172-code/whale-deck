"""Reject missing/corrupt/foreign replacements; no product or disk access."""
import copy
import unittest
from acl_source_confirm_replaced_proof import replacement_snapshot,verify_changed_snapshot


class ReplacedSourceProofTests(unittest.TestCase):
    def setUp(self):
        self.source=dict(location=dict(containerId='a'*64,instanceId='100:own',path='work/acl-file.bin'),
            entry=dict(captureId='b'*32,historyDigest='D'*64,createdAtUnixMs=1700000000000,kind='File'),accessAcl=[{'permissions':6}],defaultAcl=[])
        self.proof=dict(captureId='b'*32,originalDigest='D'*64,replacementDigest='E'*64,originalPreserved=True,sameCaptureId=True,canonicalSourceExists=True)

    def test_valid_replacement_keeps_source_frozen_and_only_changes_time_digest(self):
        frozen=copy.deepcopy(self.source);actual=replacement_snapshot(self.source,self.proof)
        verify_changed_snapshot(self.source,self.proof,actual);self.assertEqual(self.source,frozen)
        self.assertEqual(actual['entry']['createdAtUnixMs'],1700000000001);self.assertEqual(actual['entry']['historyDigest'],'E'*64)

    def test_wrong_capture_original_digest_or_absence_not_accepted(self):
        for key,value in [('captureId','c'*32),('originalDigest','F'*64),('replacementDigest','D'*64),('replacementDigest','not-a-digest'),
                          ('originalPreserved',False),('sameCaptureId',False),('canonicalSourceExists',False)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):replacement_snapshot(self.source,{**self.proof,key:value})

    def test_stale_snapshot_or_changed_plan_arrays_target_kind_cannot_pass_as_valid_replacement(self):
        expected=replacement_snapshot(self.source,self.proof)
        for key,value in [('createdAtUnixMs',1700000000000),('historyDigest','D'*64),('kind','Directory')]:
            changed=copy.deepcopy(expected);changed['entry'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_changed_snapshot(self.source,self.proof,changed)
        for key,value in [('location',dict(containerId='foreign')),('accessAcl',[]),('defaultAcl',[{'permissions':7}])]:
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_changed_snapshot(self.source,self.proof,{**expected,key:value})


if __name__=='__main__':unittest.main()
