"""Offline rejection/cleanup boundaries; these never claim real BFF/Agent/UI evidence."""
import contextlib
import copy
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock,patch
import uuid

import validate_container_acl_stale as gate
from test_acl_browser_proof import Reader,Acl
import test_acl_browser_proof as browser_proof


class StalePreflightProofTests(unittest.TestCase):
    def fixture(self):
        identifier=str(uuid.uuid4());base={'containerId':'a'*64,'instanceId':'100:started','sourceVersion':'B'*64}
        row=dict(id=identifier,state='Failed',result_code=gate.CODE,actor_subject='original',container_id=base['containerId'],instance_id=base['instanceId'],relative_path='work/acl-file.bin',
            completed_at_utc='2026-11-03T00:00:00Z',publish_barrier=False,lease_token=None,lease_expires_at_utc=None,history_may_exist=False,history_verified=False)
        row.update({k:None for k in ('execution_job_id','root_started_at_utc','plan_json','plan_hash','receipt_json','receipt_hash','acl_confirmed','history_digest','published_version')})
        api=dict(id=identifier,planningJobId=identifier,state='Failed',resultCode=gate.CODE,
            command={'expectedVersion':base['sourceVersion'],'location':{'containerId':base['containerId'],'instanceId':base['instanceId'],'path':'work/acl-file.bin'},'aclType':'access',
                'entries':[{'tag':'user_obj','id':None,'permissions':6},{'tag':'group_obj','id':None,'permissions':4},{'tag':'other','id':None,'permissions':0}]})
        api.update({k:None for k in ('executionJobId','confirmation','receipt','publishedVersion','kind','targetMode','noChange')})
        job={'id':identifier,'state':'Failed','job_type':'containers.files.acl-plan','result_json':json.dumps(gate.expected_summary(identifier))}
        return {'row':row,'planJob':job,'executionJob':None,'pending':0,'confirmed':0,'accepted':1},api,base
    def test_no_receipt_does_not_become_false_confirmation(self):
        data,api,base=self.fixture();gate.require_failed(data,api,base,'original')
        self.assertIsNone(json.loads(data['planJob']['result_json'])['aclConfirmed'])
        self.assertIsNone(json.loads(data['planJob']['result_json'])['kind'])
    def test_execution_history_or_plan_in_a_rejected_preflight_is_refused(self):
        for key,value in [('execution_job_id',str(uuid.uuid4())),('root_started_at_utc','started'),('plan_json','{}'),('receipt_json','{}'),('acl_confirmed',False),('history_verified',True),('publish_barrier',True)]:
            data,api,base=self.fixture();data['row'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.require_failed(data,api,base,'original')
    def test_wrong_original_version_actor_or_public_summary_is_refused(self):
        for mode in ('version','actor','summary','api-execution'):
            data,api,base=self.fixture()
            if mode=='version':api['command']['expectedVersion']='C'*64
            elif mode=='actor':data['row']['actor_subject']='another'
            elif mode=='api-execution':api['executionJobId']=str(uuid.uuid4())
            else:data['planJob']['result_json']=json.dumps({**gate.expected_summary(data['row']['id']),'aclConfirmed':False})
            with self.subTest(mode=mode),self.assertRaises(AssertionError):gate.require_failed(data,api,base,'original')
    def test_touch_changes_only_pinned_file_times_and_never_another_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder,content=browser_proof.AclBrowserProofTests().fixture(Path(tmp));original=gate.inventory(folder,Reader,Acl,content)
            baseline=dict(containerId='a'*64,containerName=folder.name,instanceId='101:started',original=original)
            baseline['sourceVersion']=gate.version(baseline,'work/acl-file.bin',original['acl-file.bin']['identity'])
            fixture={'folder':folder,'identifier':baseline['containerId']}
            with patch.object(gate.owned,'verify_container'),patch.object(gate.owned.common,'command',return_value=json.dumps([{'State':{'Pid':101,'StartedAt':'started'}}])):
                after=gate.touch_exact(fixture,baseline,Reader,Acl,content)
            gate.require_touch(original,after);self.assertEqual(after['acl-directory/existing.bin'],original['acl-directory/existing.bin']);self.assertEqual((folder/'acl-file.bin').read_bytes(),content)
            for key in ('access','default'):
                bad=copy.deepcopy(after);bad['acl-file.bin'][key]=[{'forged':True}]
                with self.subTest(key=key),self.assertRaises(AssertionError):gate.require_touch(original,bad)
            changed=copy.deepcopy(after);changed['acl-directory']['identity'][8]+=1
            with self.assertRaises(AssertionError):gate.require_touch(original,changed)
    def test_wrong_container_refuses_before_the_native_utime(self):
        with patch.object(gate.owned.common,'command',return_value='[{}]'),patch.object(gate.owned,'verify_container',side_effect=AssertionError),patch.object(gate.os,'utime') as mutation:
            with self.assertRaises(AssertionError):gate.touch_exact({'identifier':'owned','folder':Path('/not-used')},{'containerId':'owned','containerName':'owned'},None,None,None)
            mutation.assert_not_called()
    def test_execution_id_matches_an_actual_prior_dotnet_execution(self):
        self.assertEqual(gate.execution_id('393f2ec3-97cb-bf2d-f559-aa373bb02ce2'),'3de4442e2324feaa88846871aa371d9b')
    def test_root_receipt_presence_refuses_without_touching_hdd(self):
        storage=Mock()
        with patch.object(gate.os.path,'lexists',return_value=True),self.assertRaises(AssertionError):gate.no_operation_history(str(uuid.uuid4()),storage)
        storage.target.assert_not_called()
    def test_hdd_presence_refuses_and_unrelated_metadata_is_allowed(self):
        identifier=str(uuid.uuid4())
        with tempfile.TemporaryDirectory() as tmp:
            fd=os.open(tmp,os.O_RDONLY|os.O_DIRECTORY)
            storage=Mock();storage.target=lambda **kw:contextlib.nullcontext(({},fd));storage.f.open_at.return_value=os.dup(fd)
            try:
                with patch.object(gate.os.path,'lexists',return_value=False):gate.no_operation_history(identifier,storage)
                (Path(tmp)/(gate.execution_id(identifier)+'.json')).write_text('preserve')
                storage.f.open_at.return_value=os.dup(fd)
                with patch.object(gate.os.path,'lexists',return_value=False),self.assertRaises(AssertionError):gate.no_operation_history(identifier,storage)
                self.assertEqual(len(list(Path(tmp).iterdir())),1)
            finally:os.close(fd)


if __name__=='__main__':unittest.main()
