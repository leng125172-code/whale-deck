"""Reject missing/corrupt/foreign replacements; no product or disk access."""
import copy
import hashlib
import unittest
from acl_source_confirm_replaced_proof import replacement_snapshot,verify_changed_snapshot
from acl_source_plan_replaced_proof import require_replacement_observed,require_registration_attempt
from acl_source_register_replaced_proof import intent_id
from unittest.mock import patch
from validate_container_acl_source_plan_replaced import fixture_bytes
from verify_container_acl_source_plan_replaced import verify_fixture_recipe


class PlanReplacedSourceProofTests(unittest.TestCase):
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

    def test_queued_replacement_evidence_requires_both_actual_reads_and_original_reference(self):
        pending=dict(id='original-intent',state='Queued',executionJobId=None,confirmation=None)
        actual=dict(id='original-intent',sourceStatus=409,sourceCode='AGENT_VERSION_CONFLICT',replacementReadStatus=200,
            replacementSnapshot=replacement_snapshot(self.source,self.proof),byKeyStatus=200,queued=pending,byKey=dict(intent=pending,source=self.source))
        require_replacement_observed(self.source,self.proof,actual,'original-intent')
        for key,value in [('id','foreign'),('sourceStatus',404),('sourceCode','HISTORY_INVALID'),('replacementReadStatus',409),('byKeyStatus',404),('replacementSnapshot',self.source)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_replacement_observed(self.source,self.proof,{**actual,key:value},'original-intent')
        for key,value in [('intent',{**pending,'state':'Ready'}),('source',replacement_snapshot(self.source,self.proof))]:
            changed=copy.deepcopy(actual);changed['byKey'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_replacement_observed(self.source,self.proof,changed,'original-intent')

    def test_replacement_cannot_hide_a_ready_confirmed_or_executing_intent(self):
        for key,value in [('state','Ready'),('state','Pending'),('executionJobId','unexpected-execution'),('confirmation',{'accepted':True})]:
            queued=dict(id='original-intent',state='Queued',executionJobId=None,confirmation=None);queued[key]=value
            observed=dict(id='original-intent',sourceStatus=409,sourceCode='AGENT_VERSION_CONFLICT',replacementReadStatus=200,
                replacementSnapshot=replacement_snapshot(self.source,self.proof),byKeyStatus=200,queued=queued,byKey=dict(intent=queued,source=self.source))
            with self.subTest(key=key,value=value),self.assertRaises(AssertionError):require_replacement_observed(self.source,self.proof,observed,'original-intent')

    def test_recipe_rejects_other_missing_or_replacement_stages(self):
        identifier='a'*64
        for domain in ('source-plan-missing:','source-register-replaced:','source-apply-replaced:','source-confirm-replaced:'):
            content=hashlib.shake_256((domain+identifier).encode()).digest(2*1024**2+7)
            with self.subTest(domain=domain),self.assertRaises(AssertionError):verify_fixture_recipe(dict(containerId=identifier,bytesPerFile=len(content),sha256=hashlib.sha256(content).hexdigest().upper()))

    def test_original_request_binding_and_current_target_version_cannot_change(self):
        baseline=dict(subject='own',instanceId='instance',restoreSource=self.source,original={'acl-file.bin':{'identity':[1]*9}})
        capture=dict(actor_subject='own',login_binding='A'*64)
        attempt=dict(subject='own',key='11111111-2222-3333-4444-555555555555',body=dict(instanceId='instance',path='work/acl-file.bin',
            captureId=self.source['entry']['captureId'],expectedHistoryDigest=self.source['entry']['historyDigest'],expectedVersion='target-version'))
        identifier=intent_id('own','A'*64,attempt['key'])
        with patch('acl_source_plan_replaced_proof.version',return_value='target-version'):
            require_registration_attempt(baseline,capture,attempt,identifier)
            for key,value in [('expectedHistoryDigest',self.proof['replacementDigest']),('expectedVersion','stale'),('instanceId','foreign'),('path','work/foreign'),('captureId','c'*32),('extra',0)]:
                changed=copy.deepcopy(attempt);changed['body'][key]=value
                with self.subTest(key=key),self.assertRaises(AssertionError):require_registration_attempt(baseline,capture,changed,identifier)
            with self.assertRaises(AssertionError):require_registration_attempt(baseline,capture,attempt,'foreign-id')


if __name__=='__main__':unittest.main()
