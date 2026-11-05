"""Synthetic fault evidence, explicitly not an installed fault/UI acceptance."""
import copy,json,unittest
from test_recursive_acl_restore_job_results_proof import fixture
from recursive_acl_restore_job_results_proof import job_proof,display,COUNTS,UNKNOWN_WARNING
from recursive_acl_restore_ack_job_proof import acknowledgement_facts

WARNING='管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。'
def summary(state='Partial',receipt=True):
    s=fixture(True)[2];s.update(state=state,receiptState=state if receipt else None,publishBarrier=True,historyMayExist=True,historyVerified=receipt,bindingCurrent=False if receipt else None,historyAvailable=receipt if receipt else None)
    values=[1,1,0,25,0] if state=='Partial' else [0,0,0,0,26] if receipt else [None]*5
    s.update(zip(COUNTS,values));s['code']='CONTAINER_RECURSIVE_ACL_RESTORE_'+('PARTIAL_REQUIRES_ACKNOWLEDGEMENT' if state=='Partial' else 'RESULT_UNKNOWN');return s

def acknowledged(s):
    return {**s,'state':'Acknowledged','code':'CONTAINER_RECURSIVE_ACL_RESTORE_'+('PARTIAL_ACKNOWLEDGED' if s['state']=='Partial' else 'UNKNOWN_ACKNOWLEDGED'),'publishBarrier':False}

def observe(s):
    o,sql,_=fixture(True);phase={'Partial':'AwaitingContainerRecursiveAclRestoreResolution','Unknown':'AwaitingContainerRecursiveAclRestoreReceipt','Acknowledged':'Acknowledged'}[s['state']];state='Failed' if s['state']=='Acknowledged' else 'Running'
    for j in (o['job'],o['listJob']):j.update(state=state,phase=phase,resultJson=json.dumps(s))
    sql.update(state=state,phase=phase,result_json=s);o.update(display=display(s),stateText={'Partial':'部分修改，保护已保留','Unknown':'结果未知，保护已保留','Acknowledged':'已处置历史结果，不代表整树修改成功'}[s['state']],acknowledgementWarning=WARNING if s['state']=='Acknowledged' else None,unknownWarning=UNKNOWN_WARNING if s['state']=='Unknown' or s['receiptState']=='Unknown' else None)
    if s['state']=='Unknown' or s['receiptState']=='Unknown':o['historical']=o['historical'].replace('本次实际修改数','本次回执计数')
    return o,sql,s

class AckFaultJobTests(unittest.TestCase):
    def test_fault_jobs_stay_running_and_ack_job_is_failed(self):
        for s in (summary(),summary('Unknown'),summary('Unknown',False),acknowledged(summary())):job_proof(*observe(s))

    def test_wrong_phase_or_success_state_cannot_hide_a_fault(self):
        for layer in ('job','listJob','sql'):
            for key,bad in (('state','Succeeded'),('phase','Completed')):
                o,sql,s=observe(summary());(sql if layer=='sql' else o[layer])[key]=bad
                with self.subTest(layer=layer,key=key),self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_missing_receipt_must_not_display_zero(self):
        o,sql,s=observe(summary('Unknown',False));self.assertEqual(o['display']['actual'],['未证明']*5);o['display']['actual']=['0']*5
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_unknown_aggregate_zero_requires_explicit_nonzero_uncertainty_warning(self):
        o,sql,s=observe(summary('Unknown'));o['unknownWarning']=None
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_ack_warning_is_required_even_with_completed_receipt(self):
        s=acknowledged(summary());o,sql,s=observe(s);o['acknowledgementWarning']='恢复成功'
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_ack_retains_counts_but_current_availability_can_change(self):
        b=summary();a=acknowledged(b);a.update(bindingCurrent=False,historyAvailable=False)
        result=acknowledgement_facts(b,a);self.assertFalse(result['wholeTreeSuccessClaimed']);self.assertEqual(result['actualCounts'],[1,1,0,25,0])

    def test_prior_outcomes_scope_and_historical_verification_cannot_be_erased(self):
        for key,bad in (('changedCount',0),('receiptState','Completed'),('historyVerified',False),('historyMayExist',False),('jobId','other'),('sourceChangedCount',0),('publishBarrier',True),('code','CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED')):
            b=summary();a=acknowledged(b);a[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):acknowledgement_facts(b,a)

    def test_original_unknown_without_receipt_can_obtain_each_real_receipt_kind(self):
        for state,counts in (('Completed',[26,26,0,0,0]),('Partial',[1,1,0,25,0]),('Aborted',[0,0,1,25,0]),('Unknown',[0,0,0,0,26])):
            b=summary('Unknown',False);a=acknowledged(b);a.update(receiptState=state,historyVerified=True);a.update(zip(COUNTS,counts))
            with self.subTest(state=state):self.assertEqual(acknowledgement_facts(b,a)['receiptAfter'],state)

    def test_late_partial_receipt_does_not_change_original_unknown_ack_code(self):
        b=summary('Unknown',False);a=acknowledged(b);a.update(receiptState='Partial',historyVerified=True);a.update(zip(COUNTS,[1,1,0,25,0]));a['code']='CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_ACKNOWLEDGED'
        with self.assertRaises(AssertionError):acknowledgement_facts(b,a)

if __name__=='__main__':unittest.main()
