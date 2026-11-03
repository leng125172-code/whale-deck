"""No services: reject wrong source-loss stage, receipt, job facts or bindings."""
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import uuid
from acl_source_apply_missing_proof import CODE,FROZEN,require_pending,require_failed,summary,verify_audits,worker_unchanged_except_start,source_closure
from acl_restore_browser_proof import expected_metadata


class SourceApplyProofTests(unittest.TestCase):
    def setUp(self):
        self.patch=patch('acl_source_apply_missing_proof.managed_plan');self.plan=self.patch.start();self.addCleanup(self.patch.stop)
        self.plan.return_value=dict(command=dict(aclType='access'),kind='File',targetMode=416,noChange=False,fingerprint='fingerprint',targetProof='target')
        row={key:'original-'+key for key in FROZEN};row.update(id=str(uuid.uuid4()),state='Ready',version=5,plan_json='{}')
        self.ready=dict(row=row,planJob={'state':'Succeeded'})
        execution=str(uuid.uuid4())
        row={**row,'state':'Pending','result_code':'CONTAINER_ACL_ACCEPTED','execution_job_id':execution,'confirmed_at_utc':'confirmed',
            'execution_expires_at_utc':'original-expiry','execution_hash':'original-execution-mac','root_started_at_utc':None,'completed_at_utc':None,
            'lease_token':None,'lease_expires_at_utc':None,'publish_barrier':False,'acl_confirmed':None,'receipt_json':None,
            'history_may_exist':False,'history_verified':False,'history_digest':None,'published_version':None,'version':6}
        self.pending=dict(row=row,planJob=self.ready['planJob'],executionJob=dict(id=execution,state='Queued'),accepted=1,confirmed=1,pending=1)
        receipt=dict(Id=execution,State='Aborted',AclConfirmed=False,BindingCurrent=False,HistoryMayExist=False,HistoryVerified=False,HistoryDigest='',Version='',
            ContainerId=row['container_id'],InstanceId=row['instance_id'],Fingerprint='fingerprint',TargetProof='target',AclType='access',Kind='File',TargetMode=416,NoChange=False)
        self.record=dict(operationId=uuid.UUID(execution).hex,owner=row['owner_hash'],plan=self.plan.return_value,state='Aborted',aclConfirmed=False,
            actualIdentity=None,version='',historyMayExist=False,historyVerified=False,historyDigest='')
        row={**row,'state':'Failed','result_code':CODE,'completed_at_utc':'finished','cancel_requested_at_utc':None,'acl_confirmed':False,
            'receipt_json':json.dumps(receipt),'receipt_hash':'A'*64}
        self.final={**self.pending,'row':row,'pending':0,'executionJob':dict(id=execution,state='Failed',jobType='containers.files.acl',resultJson=json.dumps(summary(row)))}

    def check(self):require_failed(self.final,self.pending,self.record)

    def test_pending_and_failed_original_abort_facts(self):require_pending(self.pending,self.ready);self.check()

    def test_source_plan_identity_or_expiry_changes_refused(self):
        for key in (*FROZEN,'execution_job_id','execution_hash','confirmed_at_utc','execution_expires_at_utc'):
            changed=copy.deepcopy(self.final);changed['row'][key]='changed'
            with self.subTest(key=key),self.assertRaises((AssertionError,ValueError)):require_failed(changed,self.pending,self.record)

    def test_root_started_unknown_or_cancel_not_this_case(self):
        for key,value in [('root_started_at_utc','started'),('state','Unknown'),('result_code','CONTAINER_ACL_INTERRUPTED'),('acl_confirmed',None),
                          ('cancel_requested_at_utc','canceled'),('history_verified',True),('history_may_exist',True),('published_version','version')]:
            changed=copy.deepcopy(self.final);changed['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.pending,self.record)

    def test_root_without_exact_original_abort_or_history_absence_refused(self):
        for key,value in [('operationId',uuid.uuid4().hex),('owner','foreign'),('state','Completed'),('aclConfirmed',True),('actualIdentity',[1]*9),
                          ('version','published'),('historyMayExist',True),('historyVerified',True),('historyDigest','D'*64),('plan',{})]:
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(self.final,self.pending,{**self.record,key:value})

    def test_receipt_binding_or_tristate_tamper_refused(self):
        for key,value in [('Id',str(uuid.uuid4())),('AclConfirmed',None),('BindingCurrent',True),('ContainerId','foreign'),('Fingerprint','wrong'),
                          ('HistoryMayExist',True),('Kind','Directory'),('TargetMode',0),('NoChange',True)]:
            changed=copy.deepcopy(self.final);r=json.loads(changed['row']['receipt_json']);r[key]=value;changed['row']['receipt_json']=json.dumps(r)
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.pending,self.record)

    def test_job_summary_cannot_claim_unknown_success_or_unproved(self):
        for key,value in [('aclConfirmed',None),('state','Succeeded'),('receiptState','Unknown'),('historyVerified',True),('bindingCurrent',True)]:
            changed=copy.deepcopy(self.final);s=json.loads(changed['executionJob']['resultJson']);s[key]=value;changed['executionJob']['resultJson']=json.dumps(s)
            with self.subTest(key=key),self.assertRaises(AssertionError):require_failed(changed,self.pending,self.record)

    def test_pending_cannot_already_have_started_or_finished(self):
        for key,value in [('root_started_at_utc','started'),('state','Applying'),('publish_barrier',True),('execution_expires_at_utc',None)]:
            changed=copy.deepcopy(self.pending);changed['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):require_pending(changed,self.ready)

    def test_exact_audit_prefix_actor_source_hash_and_execution_are_required(self):
        row=self.final['row'];rows=[dict(action='containers.files.acl.restore.'+action,result=result,job_id=job,actor_subject=row['actor_subject'],
            target_id=row['container_id'],target_type='container',trace_id='trace',detail_json=expected_metadata(row)) for action,result,job in (
                ('plan-accepted','Accepted',row['id']),('plan-result','Ready',row['id']),('confirmed','Accepted',row['execution_job_id']),('result','Failed',row['execution_job_id']))]
        verify_audits(rows,row)
        for key,value in [('actor_subject','other'),('result','Succeeded'),('job_id',row['id']),('action','containers.files.acl.restore.cancel-requested')]:
            changed=copy.deepcopy(rows);changed[-1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):verify_audits(changed,row)
        changed=copy.deepcopy(rows);changed[-1]['detail_json']['restoreSourceHash']='foreign'
        with self.assertRaises(AssertionError):verify_audits(changed,row)

    def test_only_same_worker_start_time_may_change(self):
        before={'/whaledeck-worker':['worker-id','image','running','old'],'/database-platform-postgres':['db-id','db-image','running','old']}
        after=copy.deepcopy(before);after['/whaledeck-worker'][3]='new';worker_unchanged_except_start(before,after)
        for name,index in [('/whaledeck-worker',0),('/whaledeck-worker',1),('/database-platform-postgres',3)]:
            changed=copy.deepcopy(after);changed[name][index]='changed'
            with self.subTest(name=name,index=index),self.assertRaises(AssertionError):worker_unchanged_except_start(before,changed)


class SourceClosureTests(unittest.TestCase):
    def test_transitive_cyclic_python_and_static_dynamic_mjs_imports_from_git(self):
        with tempfile.TemporaryDirectory(prefix='wd-source-closure-') as tmp:
            repo=Path(tmp);folder=repo/'deploy/validation';folder.mkdir(parents=True)
            files={'entry.py':'from second import helper\nimport os\n','second.py':'import third\n','third.py':'import entry\n',
                   'start.mjs':"import { value } from './middle.mjs'\n",'middle.mjs':"await import('./last.mjs')\n",'last.mjs':'export const value=1\n',
                   'unrelated.py':'raise RuntimeError("never import")\n'}
            for name,raw in files.items():(folder/name).write_text(raw)
            def git(*args):return subprocess.check_output(['git','-C',str(repo),*args],stderr=subprocess.DEVNULL,text=True).strip()
            git('init');git('add','deploy');git('-c','user.name=QA','-c','user.email=qa@example.invalid','commit','-m','isolated source closure')
            revision=git('rev-parse','HEAD');(folder/'third.py').write_text('modified working file\n')
            with patch('acl_source_apply_missing_proof.SEEDS',('entry.py','start.mjs')):
                manifest=source_closure(repo,revision)
                self.assertEqual(set(manifest),set(files)-{'unrelated.py'})
                self.assertEqual(manifest['third.py'],hashlib.sha256(files['third.py'].encode()).hexdigest())
                self.assertNotEqual(manifest['third.py'],hashlib.sha256((folder/'third.py').read_bytes()).hexdigest())
            with patch('acl_source_apply_missing_proof.SEEDS',('missing.py',)):
                with self.assertRaises(AssertionError):source_closure(repo,revision)

    def test_invalid_revision_rejected_before_git(self):
        with patch('acl_source_apply_missing_proof.subprocess.check_output') as run:
            for revision in ('HEAD','--help','a'*39,'A'*40):
                with self.subTest(revision=revision),self.assertRaises(AssertionError):source_closure(Path('/unused'),revision)
            run.assert_not_called()


if __name__=='__main__':unittest.main()
