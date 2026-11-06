"""Unknown facts remain nullable through a single explicit different-admin ACK."""
import copy,json,unittest
from unittest.mock import patch
import restore_unknown_proof as proof
from test_container_recycle_restore_superseded import case as completed_case


def case(ack=False):
    v,d,f,s=completed_case();r=d['row'];r.update(state='Acknowledged' if ack else 'Unknown',result_code='CONTAINER_RECYCLE_RESTORE_UNKNOWN_ACKNOWLEDGED' if ack else 'CONTAINER_RECYCLE_RESTORE_RESULT_UNKNOWN',completed_at_utc='2026-11-06T01:00:00Z' if ack else None,publish_barrier=not ack,root_state='Unknown',source_state=None,source_retained=None,source_restored=None,restored_by_this_operation=None,acknowledged_by='different-admin' if ack else None,acknowledgement_json='receipt' if ack else None)
    v['state']=r['state'];v['receipt'].update(state='Unknown',sourceState=None,sourceRetained=None,sourceRestored=None,restoredByThisOperation=None)
    r['receipt_json']={k[0].upper()+k[1:]:x for k,x in v['receipt'].items()}
    d['pending']=0 if ack else 1;job=d['executionJob'];job.update(state='Failed' if ack else 'Running',phase='Acknowledged' if ack else 'AwaitingContainerRecycleRestoreReceipt',error_code=r['result_code'],completed_at_utc=r['completed_at_utc'])
    job['result_json'].update(state=r['state'],code=r['result_code'],receiptState='Unknown',sourceState=None,sourceRetained=None,sourceRestored=None,restoredByThisOperation=None,publishBarrier=not ack)
    return v,d,f,s


class UnknownProofTests(unittest.TestCase):
    def test_unknown_and_acknowledged_preserve_the_same_null_attribution(self):
        for ack in (False,True):
            v,d,f,s=case(ack);q,r=proof.facts(v,d,f,s);self.assertEqual(r['state'],'Unknown');self.assertIsNone(r['restoredByThisOperation'])

    def test_false_cannot_replace_unknown_and_completed_cannot_borrow_unknown(self):
        for key,bad in [('root_state','Completed'),('source_state','Restored'),('source_retained',False),('source_restored',True),('restored_by_this_operation',False),('binding_current',True),('publish_barrier',False)]:
            v,d,f,s=case();d['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.facts(v,d,f,s)

    def test_ack_requires_different_actor_terminal_job_and_processed_delivery(self):
        for target,key,bad in [('row','acknowledged_by','actor'),('row','publish_barrier',True),('executionJob','phase','AwaitingContainerRecycleRestoreReceipt'),('executionJob','state','Running')]:
            v,d,f,s=case(True);d[target][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.facts(v,d,f,s)
        v,d,f,s=case(True);d['pending']=1
        with self.assertRaises(AssertionError):proof.facts(v,d,f,s)

    def test_only_replay_may_change_between_api_receipt_and_database(self):
        v,d,f,s=case();d['row']['receipt_json']['Replayed']=not v['receipt']['replayed'];proof.facts(v,d,f,s)
        d['row']['receipt_json']['SourceRestored']=False
        with self.assertRaises(AssertionError):proof.facts(v,d,f,s)

    def test_job_summary_cannot_drop_unknown_write_protection(self):
        v,d,f,s=case();d['executionJob']['result_json']['publishBarrier']=False
        with self.assertRaises(AssertionError):proof.facts(v,d,f,s)


if __name__=='__main__':unittest.main()
