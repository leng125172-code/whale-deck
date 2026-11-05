"""Explicit synthetic Job projections: no real identity, installed SQL or UI claims."""
import copy,json,unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import Mock,patch
from recursive_acl_restore_success_jobs_proof import LABELS,CHANGES,ready_proof,terminal_proof,authority_proof,images
from recursive_acl_restore_job_results_proof import projection,display,COUNTS
from test_recursive_acl_restore_agent_probe_scope import synthetic,wire
from test_recursive_acl_restore_business_proof import managed
from validate_container_file_publishing import common

def fixture(name='restore-access'):
    index=LABELS.index(name);plan=synthetic();plan['changeCount']=CHANGES[index];plan['restoreSource']['changeCount']=4 if name in ('restore-access','restore-noop','restore-cancel') else 2;plan['restoreSource']['changedAclType']='default' if name in ('restore-default','restore-clear') else 'access'
    row=dict(id='12345678-1234-1234-1234-123456789abc',execution_job_id='87654321-1234-1234-1234-123456789abc',state='Succeeded',result_code='CONTAINER_RECURSIVE_ACL_RESTORE_COMPLETED',root_state='Completed',cancel_requested_at_utc=None,
        completed_count=4,changed_count=CHANGES[index],aborted_count=0,not_started_count=0,unknown_count=0,binding_current=True,history_may_exist=True,history_verified=True,history_available=True,publish_barrier=False,plan_json=json.dumps(managed(wire(plan))))
    old=copy.deepcopy(row);old.update(state='Ready',result_code='CONTAINER_RECURSIVE_ACL_RESTORE_READY',execution_job_id=None,root_state=None,binding_current=None,history_may_exist=False,history_verified=False,history_available=None)
    for k in COUNTS:
        import re
        old[re.sub(r'(?<!^)(?=[A-Z])','_',k).lower()]=None
    ready=projection(old,plan,execution=False);actual=projection(row,plan,execution=True)
    def sql(s):return dict(id=s['jobId'],job_type='containers.files.recursive-acl-restore'+('-plan' if s['stage']=='planning' else ''),state='Succeeded',result_json=s)
    def observed(s):
        job=dict(id=s['jobId'],jobType=sql(s)['job_type'],state='Succeeded',resultJson=json.dumps(s));prefix=f"job-{name}-{s['stage']}"
        labels=dict(identity=['原意图','阶段','恢复ACL类型'],plan=['计划对象','文件','目录（含根）','计划修改'],actual=['已完成核对','实际修改','已中止','未开始','待证明'],source=['来源对象','来源文件','来源目录','来源原修改'],metadata=['原回执','当时对象绑定有效','可能已有历史','历史已验证','当时历史可用','当时发布围栏保留','结果码'])
        return dict(job=job,listJob=copy.deepcopy(job),request=dict(method='GET',pathname='/api/v1/jobs/'+s['jobId'],status=200),listRequest=dict(method='GET',pathname='/api/v1/jobs',status=200,query=dict(take='100')),display=display(s),labels=labels,viewport=dict(width=320,height=600),theme='dark',stateText='预检已完成，尚未恢复ACL' if s['state']=='Ready' else '递归ACL恢复已完成',historical='历史事实；来源原修改数与本次实际修改数分别记录',screenshots=[prefix+'-counts.png',prefix+'-metadata.png'])
    initial=dict(row=old,planJob=sql(ready),executionJob=None);data=dict(row=row,planJob=copy.deepcopy(sql(ready)),executionJob=sql(actual))
    rs=dict(name=name,id=row['id'],source=plan['restoreSource'],planning=observed(ready));packet=dict(planning=copy.deepcopy(rs['planning']),execution=observed(actual),historicalPlanning=copy.deepcopy(rs['planning']['job']))
    if name=='restore-cancel':data['row'].update(state='Canceled',execution_job_id=None);data['executionJob']=None;packet['execution']=None
    signal=dict(name=name,result=dict(id=row['id']),jobResults=packet)
    return signal,rs,initial,data

class SuccessJobEvidenceTests(unittest.TestCase):
    def test_five_rounds_keep_ready_history_and_distinguish_noop_source(self):
        for name in LABELS:
            values=fixture(name);value=terminal_proof(*values)
            with self.subTest(name=name):self.assertEqual(value['executionCreated'],name!='restore-cancel')
        value=terminal_proof(*fixture('restore-noop'));self.assertEqual(value['sourceChangedCount'],4);self.assertEqual(value['plannedChanges'],0);self.assertEqual(value['actualCounts'],[4,0,0,0,0])

    def test_zero_is_not_a_ready_execution_fact(self):
        s,r,i,d=fixture();r['planning']['display']['actual']=['0']*5
        with self.assertRaises(AssertionError):ready_proof(r,i,'restore-access')

    def test_noop_source_cannot_replace_actual_or_planned_changes(self):
        for key in ('changedCount','changeCount'):
            s,r,i,d=fixture('restore-noop');v=json.loads(s['jobResults']['execution']['job']['resultJson']);v[key]=4;s['jobResults']['execution']['job']['resultJson']=json.dumps(v)
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(s,r,i,d)

    def test_planning_summary_and_sql_row_cannot_be_rewritten_after_confirmation(self):
        for part in ('result_json','state'):
            s,r,i,d=fixture();d['planJob'][part]={} if part=='result_json' else 'Canceled'
            with self.subTest(part=part),self.assertRaises(AssertionError):terminal_proof(s,r,i,d)

    def test_cancel_after_ready_must_have_no_execution_and_still_ready_job(self):
        s,r,i,d=fixture('restore-cancel');self.assertTrue(terminal_proof(s,r,i,d)['planningSnapshotPreserved'])
        for change in ('job','id','row'):
            s,r,i,d=fixture('restore-cancel')
            if change=='job':s['jobResults']['execution']={}
            elif change=='id':d['row']['execution_job_id']='87654321-1234-1234-1234-123456789abc'
            else:d['planJob']['state']='Canceled'
            with self.subTest(change=change),self.assertRaises(AssertionError):terminal_proof(s,r,i,d)

    def test_completed_receipt_binding_history_and_every_outcome_must_agree(self):
        for key,value in (('root_state','Aborted'),('binding_current',False),('history_available',False),('history_verified',False),('publish_barrier',True),('completed_count',True),('unknown_count',1),('changed_count',3)):
            s,r,i,d=fixture();d['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):terminal_proof(s,r,i,d)

    def test_source_and_ready_id_cannot_be_substituted(self):
        for kind in ('source','id'):
            s,r,i,d=fixture()
            if kind=='source':r['source']['captureId']='f'*32
            else:r['id']='other'
            with self.subTest(kind=kind),self.assertRaises(AssertionError):ready_proof(r,i,'restore-access')

    def test_exact_eighteen_job_images_are_distinct_from_existing_restore_shots(self):
        result=images();self.assertEqual(len(result),18);self.assertFalse(any(n.endswith(('-ready.png','-result.png','-targets.png')) for n in result));self.assertFalse(any('cancel-execution' in n for n in result))

    def test_counter_labels_cannot_be_swapped_while_numbers_match(self):
        s,r,i,d=fixture();r['planning']['labels']['actual'][0]='实际修改'
        with self.assertRaises(AssertionError):ready_proof(r,i,'restore-access')

    def test_every_original_job_route_requires_admin_and_normal_exit_denial(self):
        completed=[dict(planningJobId='p'+str(i),executionJobId=None if i==4 else 'e'+str(i)) for i in range(5)];ids=[j for r in completed for j in (r['planningJobId'],r['executionJobId']) if j]
        value=dict(ids=ids,adminAnonymous=[401]*10);ordinary=dict(jobsDenied=[403]*10,jobsAnonymous=[401]*10,jobsUiRequests=0);authority_proof(value,ordinary,completed)
        for key,bad in (('jobsDenied',[200]*10),('jobsAnonymous',[403]*10),('jobsUiRequests',True)):
            changed={**ordinary,key:bad}
            with self.subTest(key=key),self.assertRaises(AssertionError):authority_proof(value,changed,completed)
        with self.assertRaises(AssertionError):authority_proof({**value,'adminAnonymous':[200]*10},ordinary,completed)
        with self.assertRaises(AssertionError):authority_proof({**value,'ids':ids[:-1]},ordinary,completed)

    def test_failed_mode_keeps_only_owned_target_and_drains_before_identity_cleanup(self):
        from recursive_acl_restore_success_jobs_failure import defer_failed_fixture
        fixture=dict(identifier='a'*64,folder=common.APPS/('wdv-container-publish-'+'b'*32));b=dict(containerId='a'*64,instanceId='1:owned')
        rows=[dict(id='12345678-1234-1234-1234-123456789abc',execution_job_id=None,state='Expired',completed=True,leased=False,kind='restore')]
        with TemporaryDirectory() as folder,patch('validate_metric_continuity.query',return_value=rows),patch('recursive_acl_restore_prewrite.stop_browser') as stop:
            defer_failed_fixture(Mock(),Path(folder),b,fixture);self.assertTrue(fixture['retain_unsettled']);stop.assert_called_once();proof=json.loads((Path(folder)/'job-summary-failure-drain.json').read_text());self.assertTrue(proof[-1]['quiet'])
        with self.assertRaises(AssertionError):defer_failed_fixture(Mock(),Path('/unused'),{**b,'containerId':'other'},fixture)

if __name__=='__main__':unittest.main()
