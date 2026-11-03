"""Reject missing/corrupt/foreign replacements; no product or disk access."""
import copy
import hashlib
import unittest
from acl_source_confirm_replaced_proof import replacement_snapshot,verify_changed_snapshot
from validate_container_acl_source_confirm_replaced import fixture_bytes
from verify_container_acl_source_confirm_replaced import verify_fixture_recipe


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

    def test_independent_verifier_recipe_matches_parent_payload_and_rejects_other_scenarios(self):
        identifier='a'*64;content=fixture_bytes(identifier)
        baseline=dict(containerId=identifier,bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper())
        self.assertEqual(verify_fixture_recipe(baseline),content)
        old=hashlib.shake_256(('source-missing:'+identifier).encode()).digest(len(content))
        with self.assertRaises(AssertionError):verify_fixture_recipe({**baseline,'sha256':hashlib.sha256(old).hexdigest().upper()})
        for change in ({'containerId':'b'*64},{'bytesPerFile':True},{'bytesPerFile':len(content)-1}):
            with self.subTest(change=change),self.assertRaises(AssertionError):verify_fixture_recipe({**baseline,**change})


if __name__=='__main__':unittest.main()
