"""No services: register rejection is absent work, never an invented Failed Job."""
import copy
import unittest
from unittest.mock import patch
from acl_source_register_missing_proof import COUNTS,require_absent,intent_id,no_registration,require_rejected


class RegisterMissingProofTests(unittest.TestCase):
    def setUp(self):
        self.subject='own-subject';self.login='A'*64;self.key='11111111-2222-3333-4444-555555555555'
        self.zero={k:0 for k in COUNTS}

    def test_all_five_counts_must_be_exact_integer_zero(self):
        require_absent(self.zero)
        for key in COUNTS:
            for value in (1,-1,False,None,'0'):
                with self.subTest(key=key,value=value),self.assertRaises(AssertionError):require_absent({**self.zero,key:value})
        for changed in ({},{**self.zero,'unknown':0}):
            with self.assertRaises(AssertionError):require_absent(changed)

    def test_guid_depends_on_subject_login_and_key_and_never_reuses_another_scope(self):
        identifier=intent_id(self.subject,self.login,self.key)
        for subject,login,key in [('other',self.login,self.key),(self.subject,'B'*64,self.key),(self.subject,self.login,'22222222-2222-3333-4444-555555555555')]:
            self.assertNotEqual(identifier,intent_id(subject,login,key))

    def test_invalid_actor_binding_key_is_rejected_before_database(self):
        with patch('acl_source_register_missing_proof.query') as query:
            for subject,login,key in [('',self.login,self.key),('bad\nsubject',self.login,self.key),(self.subject,'a'*64,self.key),
                (self.subject,'wrong',self.key),(self.subject,self.login,'0'*32),(self.subject,self.login,'00000000-0000-0000-0000-000000000000')]:
                with self.subTest(subject=subject,login=login[:4],key=key),self.assertRaises((AssertionError,ValueError)):
                    no_registration(subject,login,key)
            query.assert_not_called()

    def test_query_uses_original_scopes_and_derived_ids_without_any_writes(self):
        with patch('acl_source_register_missing_proof.query',return_value=self.zero) as query:
            result=no_registration(self.subject,self.login,self.key)
        self.assertEqual(result['expectedIntentId'],intent_id(self.subject,self.login,self.key));self.assertEqual(result['counts'],self.zero)
        self.assertNotEqual(result['expectedIntentId'],result['expectedExecutionId'])
        sql=query.call_args.args[0];self.assertTrue(sql.startswith('SELECT '))
        for field in ('container_acl_intents','operation_jobs','outbox_messages','operation_job_events','audit_events'):self.assertIn(field,sql)
        self.assertEqual(query.call_args.kwargs['key'],self.key);self.assertEqual(query.call_args.kwargs['subject'],self.subject)

    def test_existing_job_or_orphan_event_prevents_absence_claim(self):
        for key in COUNTS:
            with self.subTest(key=key),patch('acl_source_register_missing_proof.query',return_value={**self.zero,key:1}),self.assertRaises(AssertionError):
                no_registration(self.subject,self.login,self.key)

    def fixture(self):
        baseline=dict(subject=self.subject,instanceId='owned-instance',restoreSource=dict(entry=dict(captureId='c'*32,historyDigest='D'*64)),original={'acl-file.bin':{'identity':[1]*9}})
        value=dict(subject=self.subject,key=self.key,registrationStatus=409,code='OPERATION_CONFLICT',sourceStatus=404,byKeyStatus=404,
            registrations=1,confirmations=0,cancellations=0,contentWrites=0,jobWrites=0,
            body=dict(instanceId='owned-instance',path='work/acl-file.bin',captureId='c'*32,expectedHistoryDigest='D'*64,expectedVersion='actual-version'))
        return baseline,value

    def test_valid_rejection_requires_exact_selected_source_and_current_target_version(self):
        b,v=self.fixture()
        with patch('acl_source_register_missing_proof.version',return_value='actual-version'):require_rejected(b,v)

    def test_wrong_failure_authorization_or_accepted_plan_is_not_this_case(self):
        b,v=self.fixture()
        with patch('acl_source_register_missing_proof.version',return_value='actual-version'):
            for key,value in [('registrationStatus',403),('registrationStatus',202),('sourceStatus',503),('byKeyStatus',200),('code','VALIDATION_FAILED'),('confirmations',1),('registrations',2)]:
                with self.subTest(key=key,value=value),self.assertRaises(AssertionError):require_rejected(b,{**v,key:value})

    def test_wrong_source_path_body_or_version_is_not_source_loss_rejection(self):
        b,v=self.fixture()
        with patch('acl_source_register_missing_proof.version',return_value='actual-version'):
            for key,value in [('path','work/other'),('captureId','f'*32),('instanceId','other'),('expectedHistoryDigest','A'*64),('expectedVersion','stale'),('extra','value')]:
                changed=copy.deepcopy(v);changed['body'][key]=value
                with self.subTest(key=key),self.assertRaises(AssertionError):require_rejected(b,changed)


if __name__=='__main__':unittest.main()
