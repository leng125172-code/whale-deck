"""Temporary native bytes/receipts; completion certificates and clock injected.

No retirement executor or real expiry proof. Certificate fixtures model the
future authorized executor's durable output; only owned tempfile data is removed.
"""
import copy
import json
import os
import unittest
from unittest.mock import patch
import test_container_file_staging as staging
import test_container_recycle_archive_retirement_fences as fences
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-retired-history.py','retired_history_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetiredHistoryTests(unittest.TestCase):
    def setUp(self):
        self.f=fences.ArchiveRetirementFenceTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.engine=candidate.ContainerRecycleRetiredHistory(self.f.engine);self.ref=self.f.engine.ref(self.f.q)
        self.digest=self.f.q['plan']['command']['archive']['plan']['command']['historyDigest']
        self.data=self.f.native.base.target/(self.ref['operationId']+'.data');self.meta=self.data.with_suffix('.json')

    def protected(self):return dict(original=self.f.protected(),retirement=snapshot(self.f.state))
    def observe(self):return self.engine.observe(self.ref,self.digest)
    def change(self,path,**values):
        v=json.loads(path.read_bytes());v.update(values);path.write_text(json.dumps(v))
    def completion(self,remove=True):
        # Test-only fixture, never a production mutation path. No wall clock
        # or configured HDD file changes; all files are in this test's tempdir.
        self.f.reserve();p=copy.deepcopy(self.f.q['plan']);p.update(retentionElapsed=True,retirementEligible=True);p['fingerprint']=self.f.preflight.digest(p)
        proof=dict(executionPlan=p,unlinkConfirmed=True,unlinkedIdentity=list(p['archiveIdentity']),completedAtUnixMs=p['notBeforeUnixMs']+1);proof['proofDigest']=self.f.engine.completion_digest(proof)
        record=dict(schemaVersion=2,**copy.deepcopy(self.f.q),state='Completed',completion=proof)
        with self.f.engine.locked(self.f.q) as (fd,_),self.f.engine.records(self.f.engine.archive(self.f.q)):
            if remove:self.data.unlink()
            self.f.engine.save(fd,record);self.f.engine.write_marker(self.f.q,'Retired')
        return record

    def test_legacy_available_is_full_byte_verification_without_metadata_mutation(self):
        before=self.protected()
        with patch.object(self.f.engine,'reconcile',side_effect=AssertionError('no repair')),patch.object(self.f.engine.f,'save_receipt',side_effect=AssertionError('read only')):
            r=self.observe()
        self.assertEqual((r['disposition'],r['reason'],r['dataAvailable'],r['dataPresent']),('Available','VerifiedData',True,True));self.assertEqual(before,self.protected())

    def test_pending_and_released_reservation_do_not_make_present_data_retired(self):
        self.f.reserve();before=self.protected();r=self.observe();self.assertEqual(r['disposition'],'Available');self.assertEqual(r['retirementOperationId'],self.f.q['operationId']);self.assertEqual(before,self.protected())
        self.f.engine.discard(self.f.q,self.f.session);before=self.protected();self.assertEqual(self.observe()['disposition'],'Available');self.assertEqual(before,self.protected())

    def test_missing_or_corrupt_data_without_certificate_is_never_retired(self):
        raw=self.data.read_bytes();self.data.unlink();before=self.protected();r=self.observe();self.assertEqual((r['disposition'],r['reason'],r['dataPresent']),('Unavailable','MissingData',False));self.assertEqual(before,self.protected())
        self.data.write_bytes(b'corrupt');self.data.chmod(0o600);before=self.protected();r=self.observe();self.assertEqual((r['disposition'],r['reason']),('Unavailable','InvalidData'));self.assertEqual(before,self.protected());self.assertNotEqual(raw,b'corrupt')

    def test_completed_certificate_exact_marker_and_absence_join_as_retired(self):
        record=self.completion();before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']),patch.object(self.f.engine.f,'save_receipt',side_effect=AssertionError('no receipt write')):
            r=self.observe()
        self.assertEqual((r['disposition'],r['reason'],r['dataAvailable'],r['dataPresent']),('Retired','CommittedRetirement',False,False));self.assertEqual(before,self.protected())
        # Existing source/restore paths still require actual verified data.
        with self.assertRaises(FileNotFoundError):self.f.native.source.history.verify(self.ref,self.digest)
        source_record=json.loads((self.f.native.source.state_path/(self.ref['operationId']+'.json')).read_bytes())
        receipt=self.f.native.source.public(source_record,None,True)
        self.assertFalse(receipt['historyAvailable']);self.assertEqual(receipt['state'],'Purged')

    def test_completion_in_future_cannot_report_retired(self):
        self.completion();before=self.protected();r=self.observe();self.assertEqual((r['disposition'],r['reason']),('Unavailable','CompletionInFuture'));self.assertEqual(before,self.protected())

    def test_completed_but_data_present_or_reappeared_is_conflict(self):
        record=self.completion(remove=False);before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']):r=self.observe()
        self.assertEqual((r['disposition'],r['reason']),('Unavailable','RetirementDataConflict'));self.assertTrue(r['dataPresent']);self.assertEqual(before,self.protected())

    def test_partial_file_even_with_absent_data_prevents_retired(self):
        record=self.completion();self.data.with_suffix('.partial').write_bytes(b'foreign partial');before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']):r=self.observe()
        self.assertEqual((r['disposition'],r['reason']),('Unavailable','RetirementDataConflict'));self.assertEqual(before,self.protected())

    def test_missing_or_released_terminal_marker_does_not_infer_retired(self):
        record=self.completion();self.f.marker().unlink();before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']):r=self.observe()
        self.assertEqual((r['disposition'],r['reason']),('Unavailable','RetirementProofIncomplete'));self.assertEqual(before,self.protected())
        self.f.engine.write_marker(self.f.q,'Released');before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']):self.assertEqual(self.observe()['disposition'],'Unavailable')
        self.assertEqual(before,self.protected())

    def test_retired_marker_without_terminal_record_is_not_a_certificate(self):
        self.f.reserve();self.f.engine.write_marker(self.f.q,'Retired');self.data.unlink();before=self.protected();r=self.observe();self.assertEqual((r['disposition'],r['reason']),('Unavailable','RetirementProofIncomplete'));self.assertEqual(before,self.protected())
        r=self.f.engine.inspect(self.f.q,None);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['retirementApplied']);self.assertEqual(self.f.record()['schemaVersion'],2)

    def test_orphan_retired_marker_reconstructs_only_versioned_unknown(self):
        self.completion();self.f.journal().unlink();before=self.protected();self.assertEqual(self.observe()['disposition'],'Unavailable');self.assertEqual(before,self.protected())
        r=self.f.engine.inspect(self.f.q,None);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['retirementApplied']);self.assertIsNone(self.f.record()['completion'])

    def test_terminal_proof_has_no_replay_or_cancel_and_missing_index_stays_fenced(self):
        self.completion();before=self.protected()
        for action in (self.f.engine.reserve,self.f.engine.inspect,self.f.engine.discard):
            r=action(self.f.q,None);self.assertEqual(r['state'],'Completed');self.assertTrue(r['retirementApplied'])
        self.assertEqual(before,self.protected());self.f.marker().unlink();r=self.f.engine.inspect(self.f.q,None);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['retirementApplied']);self.assertIsNotNone(self.f.record()['completion'])
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.f.engine.reserve(self.f.new(),self.f.session)

    def test_completion_rejects_young_false_unlink_wrong_facts_or_changed_proof(self):
        record=self.completion(remove=False)
        other_identity=list(record['completion']['unlinkedIdentity']);other_identity[1]+=1
        for values in (dict(unlinkConfirmed=False),dict(completedAtUnixMs=self.f.f.due-1),dict(proofDigest='F'*64),dict(unlinkConfirmed=1),dict(unlinkedIdentity=other_identity)):
            bad=copy.deepcopy(record);bad['completion'].update(values)
            if 'proofDigest' not in values:bad['completion']['proofDigest']=self.f.engine.completion_digest(bad['completion'])
            with self.subTest(values=values),self.assertRaises(Exception):self.f.engine.validate_record(bad,bad['operationId'])
        bad=copy.deepcopy(record);bad['completion']['executionPlan']=self.f.q['plan'];bad['completion']['proofDigest']=self.f.engine.completion_digest(bad['completion'])
        with self.assertRaisesRegex(Exception,'COMPLETION_INVALID'):self.f.engine.validate_record(bad,bad['operationId'])
        bad=copy.deepcopy(record);p=bad['completion']['executionPlan'];p['sourceRecordDigest']='F'*64;p['fingerprint']=self.f.preflight.digest(p);bad['completion']['proofDigest']=self.f.engine.completion_digest(bad['completion'])
        with self.assertRaisesRegex(Exception,'PLAN_INVALID'):self.f.engine.validate_record(bad,bad['operationId'])

    def test_terminal_record_must_join_original_claim_and_completed_copy(self):
        record=self.completion();self.f.native.f.claim().unlink();before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']),self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.observe()
        self.assertEqual(before,self.protected())

    def test_changed_original_history_or_purge_record_cannot_join_certificate(self):
        record=self.completion();purge=self.f.native.q['plan']['command']['purge'];path=self.f.native.engine.preflight.purges.state_path/(purge['operationId']+'.json');self.change(path,state='Unknown');before=self.protected()
        with self.f.f.clock(record['completion']['completedAtUnixMs']),self.assertRaisesRegex(Exception,'SOURCE_CHANGED'):self.observe()
        self.assertEqual(before,self.protected())

    def test_original_receipt_schema_boolean_and_wrong_digest_are_rejected(self):
        before=self.protected()
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.engine.observe(self.ref,'F'*64)
        self.assertEqual(before,self.protected());self.change(self.meta,schemaVersion=True);before=self.protected()
        with self.assertRaisesRegex(Exception,'HISTORY_CHANGED'):self.observe()
        self.assertEqual(before,self.protected())

    def test_uncertain_or_lost_reserved_marker_is_unavailable_without_repair(self):
        self.f.reserve();self.f.marker().unlink();before=self.protected();r=self.observe();self.assertEqual((r['disposition'],r['reason']),('Unavailable','RetirementUncertain'));self.assertFalse(self.f.marker().exists());self.assertEqual(before,self.protected())

    def test_proof_change_during_read_is_rejected_instead_of_a_stale_status(self):
        self.f.reserve();verify=self.engine.history.storage.verify_data
        def change_after(*args):
            verify(*args);self.f.engine.write_marker(self.f.q,'Released')
        with patch.object(self.engine.history.storage,'verify_data',side_effect=change_after),self.assertRaisesRegex(Exception,'OBSERVATION_CHANGED'):self.observe()

    def test_wrong_store_configuration_and_unsafe_registry_are_rejected(self):
        self.f.state.chmod(0o755)
        with self.assertRaises(Exception):self.observe()
        self.f.state.chmod(0o700);self.completion();config=self.engine.history.storage.configuration
        def other():return {**config(),'reserveBytes':config()['reserveBytes']+1}
        before=self.protected()
        with patch.object(self.engine.history.storage,'configuration',side_effect=other),self.assertRaises(Exception):self.observe()
        self.assertEqual(before,self.protected())
