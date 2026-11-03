"""Reject missing/corrupt/foreign replacements; no product or disk access."""
import copy
import hashlib
import json
import os
import unittest
from acl_source_confirm_replaced_proof import replacement_snapshot,verify_changed_snapshot
from acl_source_register_replaced_proof import require_rejected,attempt_identifier,intent_id
from unittest.mock import patch
from validate_container_acl_source_register_replaced import fixture_bytes,restore_registration_source
import test_owned_acl_source_replacement as replacement_fixture
from verify_container_acl_source_register_replaced import verify_fixture_recipe


class RegisterReplacedSourceProofTests(unittest.TestCase):
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

    def rejection(self):
        baseline=dict(subject='own',instanceId='instance',restoreSource=self.source,original={'acl-file.bin':{'identity':[1]*9}})
        value=dict(subject='own',registrationStatus=409,code='OPERATION_CONFLICT',sourceStatus=409,sourceCode='AGENT_VERSION_CONFLICT',replacementReadStatus=200,byKeyStatus=404,
            registrations=1,confirmations=0,cancellations=0,contentWrites=0,jobWrites=0,replacementSnapshot=replacement_snapshot(self.source,self.proof),
            body=dict(instanceId='instance',path='work/acl-file.bin',captureId=self.source['entry']['captureId'],expectedHistoryDigest=self.source['entry']['historyDigest'],expectedVersion='target-version'))
        return baseline,value

    def test_registration_rejection_requires_original_reference_and_both_actual_reads(self):
        baseline,value=self.rejection()
        with patch('acl_source_register_replaced_proof.version',return_value='target-version'):
            require_rejected(baseline,value,self.proof)
            for key,changed in [('subject','other'),('registrationStatus',202),('sourceStatus',404),('sourceCode','HISTORY_INVALID'),('replacementReadStatus',409),('byKeyStatus',200),('replacementSnapshot',self.source)]:
                with self.subTest(key=key),self.assertRaises(AssertionError):require_rejected(baseline,{**value,key:changed},self.proof)

    def test_automatic_reregistration_confirmation_or_generic_write_cannot_pass(self):
        baseline,value=self.rejection()
        with patch('acl_source_register_replaced_proof.version',return_value='target-version'):
            for key,changed in [('registrations',2),('confirmations',1),('cancellations',1),('contentWrites',1),('jobWrites',1)]:
                with self.subTest(key=key),self.assertRaises(AssertionError):require_rejected(baseline,{**value,key:changed},self.proof)

    def test_new_digest_stale_target_foreign_capture_or_extra_body_cannot_pass(self):
        baseline,value=self.rejection()
        with patch('acl_source_register_replaced_proof.version',return_value='target-version'):
            for key,changed in [('expectedHistoryDigest',self.proof['replacementDigest']),('expectedVersion','stale'),('captureId','c'*32),('path','work/foreign'),('instanceId','foreign'),('extra',0)]:
                altered=copy.deepcopy(value);altered['body'][key]=changed
                with self.subTest(key=key),self.assertRaises(AssertionError):require_rejected(baseline,altered,self.proof)

    def test_recipe_rejects_other_missing_or_replacement_stages(self):
        identifier='a'*64
        for domain in ('source-register-missing:','source-apply-replaced:','source-confirm-replaced:'):
            content=hashlib.shake_256((domain+identifier).encode()).digest(2*1024**2+7)
            with self.subTest(domain=domain),self.assertRaises(AssertionError):verify_fixture_recipe(dict(containerId=identifier,bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper()))

    def test_attempt_id_is_available_without_any_response_or_accepted_rejection(self):
        baseline=dict(subject='own');capture=dict(actor_subject='own',login_binding='A'*64)
        attempt=dict(subject='own',key='11111111-2222-3333-4444-555555555555')
        with patch('acl_source_register_missing_proof.query') as query:
            self.assertEqual(attempt_identifier(baseline,capture,attempt),intent_id('own','A'*64,attempt['key']))
            query.assert_not_called()

    def test_attempt_cannot_be_bound_to_a_foreign_actor_login_or_malformed_key(self):
        baseline=dict(subject='own');capture=dict(actor_subject='own',login_binding='A'*64)
        attempt=dict(subject='own',key='11111111-2222-3333-4444-555555555555')
        for altered in ({**attempt,'subject':'foreign'},{**attempt,'key':'invalid'}):
            with self.assertRaises((AssertionError,ValueError)):attempt_identifier(baseline,capture,altered)
        for altered in ({**capture,'actor_subject':'foreign'},{**capture,'login_binding':'invalid'}):
            with self.assertRaises((AssertionError,ValueError)):attempt_identifier(baseline,altered,attempt)

    @unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
    def test_unjournaled_stage_keeps_bytes_and_marks_recovery_without_deleting_original(self):
        replacement_fixture.ReplacementTests.setUp(self)
        stage=self.namespace/('.wd-qa-acl-replacement-'+self.capture);stage.mkdir(mode=0o700)
        candidate=stage/'candidate.json';candidate.write_bytes(b'not-proved-temporary-bytes');original=self.path.read_bytes()
        result=restore_registration_source(self.replacement,self.journal)
        self.assertTrue(result['originalRestored']);self.assertEqual(self.path.read_bytes(),original)
        self.assertEqual(candidate.read_bytes(),b'not-proved-temporary-bytes')
        marker=json.loads((self.journal/'quarantine-recovery-required.json').read_bytes())
        self.assertFalse(marker['gatePassed']);self.assertTrue(marker['unjournaledStagePreserved']);self.assertTrue(marker['originalsPreserved'])

    @unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
    def test_original_alone_does_not_report_a_nonexistent_unjournaled_stage(self):
        replacement_fixture.ReplacementTests.setUp(self)
        original=self.path.read_bytes();result=restore_registration_source(self.replacement,self.journal)
        self.assertTrue(result['originalRestored']);self.assertEqual(self.path.read_bytes(),original)
        self.assertFalse((self.journal/'quarantine-recovery-required.json').exists())


if __name__=='__main__':unittest.main()
