"""Synthetic negative evidence checks; these never count as installed UI proof."""
import copy
import json
import unittest
from recursive_acl_restore_job_results_proof import projection,display,job_proof,safe_summary,api_job
from verify_recursive_acl_restore_prewrite import verify

def fixture(execution=False):
    row=dict(id='12345678-1234-1234-1234-123456789abc',execution_job_id='87654321-1234-1234-1234-123456789abc',state='Canceled' if execution else 'Ready',result_code='CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED' if execution else 'CONTAINER_RECURSIVE_ACL_RESTORE_READY',root_state='Aborted' if execution else None,
        completed_count=0 if execution else None,changed_count=0 if execution else None,aborted_count=1 if execution else None,not_started_count=25 if execution else None,unknown_count=0 if execution else None,
        binding_current=False if execution else None,history_may_exist=execution,history_verified=execution,history_available=True if execution else None,publish_barrier=False)
    plan=dict(entryCount=26,fileCount=25,directoryCount=1,changeCount=26,restoreSource=dict(entryCount=26,fileCount=25,directoryCount=1,changeCount=26,changedAclType='access'))
    s=projection(row,plan,execution=execution);state='Canceled' if execution else 'Succeeded';kind='containers.files.recursive-acl-restore'+('' if execution else '-plan')
    job=dict(id=s['jobId'],jobType=kind,state=state,resultJson=json.dumps(s));sql=dict(id=s['jobId'],job_type=kind,state=state,result_json=s)
    observed=dict(job=job,listJob=copy.deepcopy(job),request=dict(method='GET',pathname='/api/v1/jobs/'+s['jobId'],status=200),listRequest=dict(method='GET',pathname='/api/v1/jobs',status=200,query=dict(take='100')),display=display(s),viewport=dict(width=320,height=600),theme='dark',stateText='已取消' if execution else '预检已完成，尚未恢复ACL',historical='历史事实：来源原修改数与本次实际修改数分别记录',screenshots=[f"job-{s['stage']}-counts.png",f"job-{s['stage']}-metadata.png"])
    return observed,sql,s

class JobResultsEvidenceTests(unittest.TestCase):
    def test_ready_keeps_unproved_counts_and_execution_keeps_distinct_source_count(self):
        for mode in (False,True):job_proof(*fixture(mode))
        self.assertEqual(['未证明']*5,fixture()[0]['display']['actual'])
        self.assertEqual(['0','0','1','25','0'],fixture(True)[0]['display']['actual'])
        self.assertEqual('26',fixture(True)[0]['display']['source'][-1])

    def test_schema_rejects_secret_path_extra_missing_and_duplicate_fields(self):
        s=fixture()[2]
        for raw in (json.dumps({**s,'path':'/private'}),json.dumps({k:v for k,v in s.items() if k!='historyAvailable'}),json.dumps(s)[:-1]+',"state":"Ready"}'):
            with self.subTest(raw=raw[-40:]),self.assertRaises(AssertionError):safe_summary(raw)

    def test_result_size_is_bounded(self):
        s=fixture()[2];s['code']='A'*4096
        with self.assertRaises(AssertionError):safe_summary(json.dumps(s))

    def test_sql_and_http_must_both_match_the_original_facts(self):
        for key,value in (('changedCount',26),('completedCount',True),('historyAvailable',None),('sourceChangedCount',0),('receiptState','Completed'),('state','Succeeded')):
            for layer in ('http','sql'):
                o,sql,s=fixture(True);bad={**s,key:value}
                if layer=='sql':sql['result_json']=bad
                else:o['job']['resultJson']=json.dumps(bad)
                with self.subTest(key=key,layer=layer),self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_ready_null_must_not_be_presented_as_zero(self):
        o,sql,s=fixture();o['display']['actual']=['0']*5
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_source_changes_cannot_be_copied_into_actual_changes(self):
        o,sql,s=fixture(True);o['display']['actual'][1]='26'
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_list_detail_ids_stages_and_types_must_agree(self):
        for layer in ('job','listJob'):
            for key,value in (('id','other'),('state','Succeeded'),('jobType','containers.files.recursive-acl')):
                o,sql,s=fixture(True);o[layer][key]=value
                with self.subTest(layer=layer,key=key),self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_planning_history_cannot_be_replaced_by_later_terminal_summary(self):
        o,sql,s=fixture();execution=fixture(True)[2];o['job']['resultJson']=json.dumps(execution)
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_historical_warning_and_mobile_receipts_are_required(self):
        for key,value in (('historical','success'),('stateText','Succeeded'),('screenshots',[]),('viewport',dict(width=1440,height=600)),('theme','light')):
            o,sql,s=fixture();o[key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_shell_poll_must_not_replace_the_jobs_page_list_response(self):
        o,sql,s=fixture();o['listRequest']['query']['take']='50'
        with self.assertRaises(AssertionError):job_proof(o,sql,s)

    def test_other_mode_evidence_cannot_be_relabelled(self):
        for name,mode,logout in (('recursive-acl-restore-inflight-logout-'+'a'*32,True,True),('recursive-acl-restore-job-results-ui-'+'a'*32,False,True),('recursive-acl-restore-job-results-ui-'+'a'*32,True,False)):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name,logout=logout,job_results=mode)

    def test_failed_ready_cleanup_cannot_target_any_other_run(self):
        from reconcile_recursive_acl_restore_job_ready_failure import target,NAME
        self.assertEqual(NAME,target(NAME).name)
        for name in ('../escape','recursive-acl-restore-job-results-ui-'+'a'*32,'recursive-acl-restore-inflight-logout-'+'a'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):target(name)

if __name__=='__main__':unittest.main()
