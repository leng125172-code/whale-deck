"""Explicit constructed SQL/API/DOM records: no live DB, identity or disk claims."""
import copy,json,re,unittest,uuid
from recursive_acl_restore_partial_business_proof import require_partial,require_acknowledged,PREFIX
from recursive_acl_restore_business_proof import metadata
from recursive_acl_restore_business_proof import managed_plan
from recursive_acl_restore_job_results_proof import projection,display,COUNTS
from test_recursive_acl_restore_agent_probe_scope import synthetic,wire
from test_recursive_acl_restore_business_proof import managed,api_receipt
from test_recursive_acl_restore_success_jobs_proof import fixture as job_fixture
from validate_container_recursive_acl_restore_browser import expected_execution

def fixture():
    plan=synthetic();plan.update(entryCount=26,fileCount=25,directoryCount=1,changeCount=26);plan['restoreSource'].update(entryCount=26,fileCount=25,directoryCount=1,changeCount=26)
    intent=str(uuid.uuid4());eid=expected_execution(intent);command=plan['command'];time='2026-11-05T00:00:00Z'
    row=dict(id=intent,execution_job_id=eid,idempotency_key=str(uuid.uuid4()),actor_subject='original',login_binding='A'*64,owner_hash='B'*64,container_id=command['containerId'],instance_id=command['instanceId'],relative_path=command['path'],request_hash='C'*64,plan_hash='D'*64,execution_hash='E'*64,receipt_hash='F'*64,
        command_json='fixed-command',plan_json=json.dumps(managed(wire(plan))),ready_expires_at_utc=time,confirmed_at_utc=time,execution_expires_at_utc=time,root_started_at_utc=time,cancel_requested_at_utc=time,completed_at_utc=None,lease_token=None,lease_expires_at_utc=None,
        version=10,state='Partial',result_code=PREFIX+'PARTIAL_REQUIRES_ACKNOWLEDGEMENT',root_state='Partial',publish_barrier=True,history_may_exist=True,history_verified=True,history_available=True,binding_current=False,history_digest='1'*64,outcome_digest='2'*64,acknowledged_by=None,acknowledgement_json=None)
    row.update(zip(('completed_count','changed_count','aborted_count','not_started_count','unknown_count'),(1,1,0,25,0)))
    receipt=dict(operationId=uuid.UUID(eid).hex,containerId=row['container_id'],instanceId=row['instance_id'],fingerprint=plan['fingerprint'],targetProof=plan['targetProof'],state='Partial',bindingCurrent=False,replayed=False,completedCount=1,changedCount=1,abortedCount=0,notStartedCount=25,unknownCount=0,historyMayExist=True,historyVerified=True,historyDigest=row['history_digest'],historyAvailable=True,outcomeDigest=row['outcome_digest'],aclType='access')
    row['receipt_json']=json.dumps(managed(api_receipt(receipt)))
    old=copy.deepcopy(row);old.update(state='Ready',result_code=PREFIX+'READY',root_state=None,execution_job_id=None,history_may_exist=False,history_verified=False,history_available=None,binding_current=None,publish_barrier=False,version=1)
    for key in COUNTS:old[re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()]=None
    def sqljob(jid,kind,state,phase,result):return dict(id=jid,job_type=kind,actor_subject='original',state=state,phase=phase,idempotency_key=row['idempotency_key'] if jid==intent else jid,request_json=metadata(row),completed_at_utc=time if state=='Succeeded' else None,cancel_requested_at_utc=None if jid==intent else time,result_json=result)
    p=sqljob(intent,'containers.files.recursive-acl-restore-plan','Succeeded','ContainerRecursiveAclRestorePlanReady',projection(old,plan,execution=False))
    e=sqljob(eid,'containers.files.recursive-acl-restore','Running','AwaitingContainerRecursiveAclRestoreResolution',projection(row,plan,execution=True))
    def observation(j,prefix):
        s=j['result_json'];o=copy.deepcopy(job_fixture()[1]['planning']);dto=dict(id=j['id'],jobType=j['job_type'],state=j['state'],phase=j['phase'],resultJson=json.dumps(s))
        o.update(job=dto,listJob=copy.deepcopy(dto),display=display(s),stateText={'Ready':'预检已完成，尚未恢复ACL','Partial':'部分修改，保护已保留','Acknowledged':'已处置历史结果，不代表整树修改成功'}[s['state']],request=dict(method='GET',pathname='/api/v1/jobs/'+j['id'],status=200),screenshots=[prefix+'-counts.png',prefix+'-metadata.png'],acknowledgementWarning='管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。' if s['state']=='Acknowledged' else None)
        return o
    api_command=dict(location={k:command[k] for k in ('containerId','instanceId','path')},**{k:command[k] for k in ('captureId','expectedHistoryDigest','expectedVersion')})
    result=dict(id=intent,planningJobId=intent,executionJobId=eid,state='Partial',resultCode=row['result_code'],version=10,confirmation=None,command=api_command,restoreSource=plan['restoreSource'],receipt=api_receipt(receipt),**{k:plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest')})
    result.update(zip(('rootUid','rootGid','rootMode','executorUid','executorGid'),[*plan['rootIdentity'][3:5],plan['rootIdentity'][2],plan['uid'],plan['gid']]))
    initial=dict(row=old,planJob=copy.deepcopy(p),executionJob=None);ready=dict(subject='original',ready=dict(id=intent,command=api_command),jobResults=dict(planning=observation(p,'job-planning')))
    data=dict(row=row,planJob=p,executionJob=e,pending=2,accepted=1,confirmed=1,events=5,audits=[])
    for action,status,jid in (('plan-accepted','Accepted',intent),('plan-result','Ready',intent),('confirmed','Accepted',eid),('cancel-requested','Accepted',eid),('result','Partial',eid)):
        data['audits'].append(dict(id=str(uuid.uuid4()),actor_subject='original',target_type='container',target_id=row['container_id'],trace_id='trace',action='containers.files.recursive-acl-restore.'+action,result=status,job_id=jid,detail_json=metadata(row)))
    after=copy.deepcopy(data);after['row'].update(state='Acknowledged',result_code=PREFIX+'PARTIAL_ACKNOWLEDGED',publish_barrier=False,completed_at_utc=time,version=11,acknowledged_by='resolver')
    after['row']['acknowledgement_json']=after['row']['receipt_json'];after['executionJob'].update(state='Failed',phase='Acknowledged',completed_at_utc=time,result_json=projection(after['row'],plan,execution=True));after.update(pending=0,events=6)
    after['audits'].append(dict(id=str(uuid.uuid4()),actor_subject='resolver',target_type='container',target_id=row['container_id'],trace_id='ack-trace',action='containers.files.recursive-acl-restore.partial-acknowledged',result='Partial',job_id=eid,detail_json=metadata(row)))
    ack_api={**result,'state':'Acknowledged','resultCode':after['row']['result_code'],'version':11}
    return dict(data=data,initial=initial,ready=ready,baseline=dict(containerId=row['container_id'],instanceId=row['instance_id']),source=plan['restoreSource'],api=result,job=observation(e,'job-fault-partial-execution'),after=after,ack_api=ack_api,ack_job=observation(after['executionJob'],'job-fault-acknowledged-execution'))

def partial(f):return require_partial(f['data'],f['ready'],f['initial'],f['baseline'],f['source'],f['api'],f['job'])
def ack(f,actor='resolver'):return require_acknowledged(f['data'],f['after'],actor,f['ack_api'],f['ack_job'])

class PartialBusinessProofTests(unittest.TestCase):
    def test_partial_then_different_admin_ack_retains_original_job_and_receipt(self):
        f=fixture();self.assertEqual(partial(f)[1]['changedCount'],1);self.assertEqual(ack(f)[1]['changedCount'],1)
    def test_partial_does_not_finish_or_clear_outbox(self):
        for key,bad in (('completed_at_utc','now'),('publish_barrier',False),('lease_token','lease'),('state','Canceled')):
            f=fixture();f['data']['row'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):partial(f)
        f=fixture();f['data']['pending']=0
        with self.assertRaises(AssertionError):partial(f)
    def test_wrong_workflow_code_or_phase_is_not_accepted(self):
        f=fixture();f['data']['row']['result_code']='CONTAINER_RECURSIVE_ACL_PARTIAL_REQUIRES_ACKNOWLEDGEMENT'
        with self.assertRaises(AssertionError):partial(f)
        f=fixture();f['data']['executionJob']['phase']='Completed'
        with self.assertRaises(AssertionError):partial(f)
    def test_audits_must_prove_cancel_and_partial_once(self):
        for change in ('missing','duplicate','actor','result'):
            f=fixture()
            if change=='missing':f['data']['audits'].pop()
            elif change=='duplicate':f['data']['audits'].append(copy.deepcopy(f['data']['audits'][-1]))
            elif change=='actor':f['data']['audits'][-1]['actor_subject']='other'
            else:f['data']['audits'][-1]['result']='Canceled'
            with self.subTest(change=change),self.assertRaises(AssertionError):partial(f)
    def test_original_ready_job_and_request_binding_cannot_change(self):
        for key in ('plan_json','login_binding','owner_hash'):
            f=fixture()
            if key=='plan_json':
                value=json.loads(f['initial']['row'][key]);value['Fingerprint']='9'*64;f['initial']['row'][key]=json.dumps(value)
            else:f['initial']['row'][key]='other'
            with self.subTest(key=key),self.assertRaises(AssertionError):partial(f)
        f=fixture();f['initial']['planJob']['result_json']={}
        with self.assertRaises(AssertionError):partial(f)
    def test_ack_requires_different_actor_and_empty_outbox(self):
        f=fixture()
        with self.assertRaises(AssertionError):ack(f,'original')
        f['after']['pending']=1
        with self.assertRaises(AssertionError):ack(f)
    def test_ack_cannot_rewrite_counts_digest_or_original_execution(self):
        for key,bad in (('changed_count',0),('history_digest','3'*64),('execution_job_id',str(uuid.uuid4())),('plan_json','other')):
            f=fixture();f['after']['row'][key]=bad
            with self.subTest(key=key),self.assertRaises((AssertionError,ValueError)):ack(f)
    def test_ack_api_and_job_must_match_after_sql_version_and_actor(self):
        for target,key,bad in (('ack_api','version',10),('ack_api','state','Succeeded'),('ack_job','stateText','恢复成功')):
            f=fixture();f[target][key]=bad
            with self.subTest(target=target,key=key),self.assertRaises(AssertionError):ack(f)
    def test_ack_current_hdd_availability_can_change_without_erasing_history(self):
        f=fixture();r=f['after']['row'];receipt=json.loads(r['receipt_json']);receipt.update(HistoryAvailable=False,Replayed=True)
        r.update(receipt_json=json.dumps(receipt),acknowledgement_json=json.dumps(receipt),history_available=False,receipt_hash='5'*64)
        f['ack_api']['receipt'].update(historyAvailable=False,replayed=True)
        s=projection(r,managed_plan(r['plan_json']),execution=True);f['after']['executionJob']['result_json']=s
        for job in (f['ack_job']['job'],f['ack_job']['listJob']):job['resultJson']=json.dumps(s)
        f['ack_job']['display']=display(s)
        result=ack(f)[1];self.assertFalse(result['historyAvailable']);self.assertTrue(result['historyVerified'])

if __name__=='__main__':unittest.main()
