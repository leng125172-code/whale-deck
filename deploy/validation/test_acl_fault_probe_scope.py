"""Denial boundaries of the QA-only exact ACL helper observer (no real services)."""
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import uuid

import bound_container_acl_cut as cut
from validate_container_file_staging import load


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class AclFaultScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary=tempfile.TemporaryDirectory(prefix='wd-acl-cut-test-');self.addCleanup(self.temporary.cleanup)
        self.root=Path(self.temporary.name);self.source=self.root/'source';self.source.mkdir();self.file=self.source/'acl-file.bin';self.file.write_bytes(b'unchanged full content');self.file.chmod(0o640)
        os.setxattr(self.file,'user.qa',b'unchanged attribute');self.module=load(Path(__file__).resolve().parents[1]/'host/whaledeck-container-acl.py','acl_cut_unit_module')
        self.module_patch=patch.object(cut,'acl_module',return_value=self.module);self.module_patch.start();self.addCleanup(self.module_patch.stop)
        self.operation=uuid.uuid4().hex;old=cut.fingerprint(self.file.stat());base=self.module.base_acl(0o640);entries=[base[0],{'tag':'user','id':2201,'permissions':6},{'tag':'group_obj','id':None,'permissions':0},{'tag':'mask','id':None,'permissions':4},base[2]]
        digest=hashlib.sha256(b'whaledeck-container-acl-attributes-v1\n'+self.module.encode([['user.qa',base64.b64encode(b'unchanged attribute').decode()]])).hexdigest().upper()
        self.managed={'SchemaVersion':1,'Command':{'Location':{'ContainerId':'a'*64,'InstanceId':'100:test','Path':'work/acl-file.bin'},'AclType':'access','Entries':[{'Tag':e['tag'],'Id':e['id'],'Permissions':e['permissions']} for e in entries],'ExpectedVersion':'E'*64},'SourceIdentity':dict(zip(('Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs'),old[:8])),
            'ParentIdentity':{'Device':self.source.stat().st_dev,'Inode':self.source.stat().st_ino},'ParentVersion':'P'*64,'SourceChangedAtUnixNs':old[8],'OwnerUid':1000,'OwnerGid':1000,'Kind':'File','TargetProof':'T'*64,'AttributesDigest':digest,'NoChange':False,'AccessAcl':[{'Tag':e['tag'],'Id':e['id'],'Permissions':e['permissions']} for e in base],'DefaultAcl':[],'TargetMode':0o640,'Fingerprint':''}
        from validate_container_acl_browser import managed_plan
        self.plan=managed_plan(self.managed);self.plan['fingerprint']=self.managed['Fingerprint']=hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+self.module.encode({k:v for k,v in self.plan.items() if k!='fingerprint'})).hexdigest().upper()
        self.record={'schemaVersion':1,'operationId':self.operation,'owner':'B'*64,'plan':self.plan,'state':'Prepared','aclConfirmed':None,'actualIdentity':None,'version':'','historyMayExist':True,'historyVerified':True,'historyDigest':'D'*64}
        self.row={'execution_job_id':str(uuid.UUID(self.operation)),'owner_hash':'B'*64,'container_id':'a'*64,'instance_id':'100:test','relative_path':'work/acl-file.bin','plan_json':json.dumps(self.managed)}
        self.path=self.root/(self.operation+'.json');self.path.write_text(json.dumps(self.record));self.path.chmod(0o600)

    def test_full_sql_root_plan_and_actor_binding(self):
        with patch.object(cut,'ROOT',self.root):
            self.assertEqual(cut.record_for(self.row),self.record)
            for key,value in [('owner_hash','C'*64),('container_id','b'*64),('instance_id','200:foreign'),('relative_path','work/foreign')]:
                with self.subTest(key=key),self.assertRaises(AssertionError):cut.record_for({**self.row,key:value})

    def test_acl_entry_or_mask_tamper_is_not_authorization(self):
        self.plan['command']['entries'][1]['id']=0;self.path.write_text(json.dumps(self.record))
        with patch.object(cut,'ROOT',self.root),self.assertRaises(AssertionError):cut.record_for(self.row)

    def test_sql_full_original_acl_and_ctime_must_agree(self):
        for key in ('AccessAcl','SourceChangedAtUnixNs','AttributesDigest','TargetMode','NoChange'):
            changed=copy.deepcopy(self.managed)
            if key=='AccessAcl':changed[key][1]['Permissions']=0
            elif key=='AttributesDigest':changed[key]='F'*64
            elif key=='NoChange':changed[key]=True
            else:changed[key]+=1
            with self.subTest(key=key),patch.object(cut,'ROOT',self.root),self.assertRaises(AssertionError):cut.record_for({**self.row,'plan_json':json.dumps(changed)})

    def test_only_full_original_inode_and_acls_count(self):
        self.assertEqual(cut.original_proof(self.record,self.source)['originalIdentity'],self.plan['sourceIdentity'])
        os.chmod(self.file,0o600)
        with self.assertRaisesRegex(AssertionError,'SOURCE_CHANGED'):cut.original_proof(self.record,self.source)

    def test_original_acl_and_attributes_are_independently_read(self):
        for key,value in [('accessAcl',self.module.base_acl(0o600)),('attributesDigest','F'*64)]:
            record=copy.deepcopy(self.record);record['plan'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.original_proof(record,self.source)

    def test_applying_or_terminal_is_not_a_pre_syscall_cut(self):
        for state in ('Applying','Completed','Aborted','Unknown'):
            with self.subTest(state=state):self.assertIsNone(cut.original_proof({**self.record,'state':state},self.source))

    def test_foreign_path_symlink_and_parent_are_rejected(self):
        for path in ('/etc/shadow','work/../acl-file.bin','work/nested/file','work/foreign'):
            changed=copy.deepcopy(self.record);changed['plan']['command']['path']=path
            with self.subTest(path=path),self.assertRaises(AssertionError):cut.original_proof(changed,self.source)
        old=self.source/'old';self.file.rename(old);self.file.symlink_to(old)
        with self.assertRaises(AssertionError):cut.original_proof(self.record,self.source)
        self.file.unlink();old.rename(self.file);changed=copy.deepcopy(self.record);changed['plan']['parentIdentity']=[1,2]
        with self.assertRaises(AssertionError):cut.original_proof(changed,self.source)

    def test_foreign_process_with_both_fds_is_not_acl_helper(self):
        lock=self.root/'lock';lock.touch(mode=0o600)
        with lock.open('rb') as stream:
            fd=os.open(self.source,os.O_RDONLY|os.O_DIRECTORY)
            try:
                child=subprocess.Popen(['/bin/sleep','5'],pass_fds=(fd,stream.fileno()))
                try:self.assertFalse(cut.bound_fds(child.pid,cut.identity(self.source),cut.identity(lock)))
                finally:child.terminate();child.wait(timeout=2)
            finally:os.close(fd)

    def test_non_owned_source_refused_before_pidfd(self):
        with patch.object(cut.os,'pidfd_open',side_effect=AssertionError('must not open')) as opened:
            with self.assertRaises(AssertionError):cut.freeze_before_acl(self.row,self.source,100)
            opened.assert_not_called()

    def test_nonempty_wrong_mode_hardlinked_and_symlink_lock_refused(self):
        lock=self.root/'lock';lock.touch(mode=0o600);self.assertEqual(cut.operation_lock(lock),cut.identity(lock))
        lock.write_bytes(b'x')
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        lock.write_bytes(b'');lock.chmod(0o644)
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        lock.chmod(0o600);os.link(lock,self.root/'linked')
        with self.assertRaises(AssertionError):cut.operation_lock(lock)
        link=self.root/'symlink';link.symlink_to(lock)
        with self.assertRaises(AssertionError):cut.operation_lock(link)

    def write_target_acl(self, entries=None):
        os.setxattr(self.file,self.module.XATTRS['access'],self.module.encode_acl(entries or self.plan['command']['entries']))
        self.record['state']='Applying'

    def test_post_acl_cut_requires_real_changed_acl_and_original_inode(self):
        self.record['state']='Applying';self.assertIsNone(cut.changed_proof(self.record,self.source))
        self.write_target_acl();proof=cut.changed_proof(self.record,self.source)
        self.assertEqual(proof['changedIdentity'],cut.fingerprint(self.file.stat()));self.assertEqual(proof['changedIdentity'][:8],self.plan['sourceIdentity'][:8])
        self.assertGreater(proof['changedIdentity'][8],self.plan['sourceIdentity'][8]);self.assertEqual(proof['accessAcl'],self.plan['command']['entries'])
        self.assertEqual(self.file.read_bytes(),b'unchanged full content')

    def test_wrong_named_permissions_with_same_mode_are_not_a_post_acl_cut(self):
        entries=copy.deepcopy(self.plan['command']['entries']);entries[1]['permissions']=7;self.write_target_acl(entries)
        self.assertEqual(self.file.stat().st_mode,self.plan['sourceIdentity'][2])
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)

    def test_post_acl_cut_rejects_changed_owner_or_ordinary_attribute(self):
        self.write_target_acl();os.setxattr(self.file,'user.qa',b'foreign value')
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)
        os.setxattr(self.file,'user.qa',b'unchanged attribute');os.chown(self.file,2201,3201)
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)

    def test_post_acl_cut_does_not_accept_unverified_history_or_noop(self):
        self.write_target_acl()
        for key,value in [('historyVerified',False),('historyMayExist',False),('aclConfirmed',True),('version','already-completed')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.changed_proof({**self.record,key:value},self.source)
        changed=copy.deepcopy(self.record);changed['plan']['noChange']=True
        with self.assertRaises(AssertionError):cut.changed_proof(changed,self.source)
        for phase in ('Preparing','Prepared','Completed','Aborted','Unknown'):
            with self.subTest(phase=phase):self.assertIsNone(cut.changed_proof({**self.record,'state':phase},self.source))

    def test_post_acl_cut_rejects_hardlink_and_foreign_parent(self):
        self.write_target_acl();changed=copy.deepcopy(self.record);changed['plan']['parentIdentity']=[1,2]
        with self.assertRaises(AssertionError):cut.changed_proof(changed,self.source)
        os.link(self.file,self.source/'extra')
        with self.assertRaises(AssertionError):cut.changed_proof(self.record,self.source)


class AclFailureCleanupTests(unittest.TestCase):
    def setUp(self):
        import validate_container_acl_faults as driver
        self.driver=driver;self.temporary=tempfile.TemporaryDirectory();self.addCleanup(self.temporary.cleanup);self.directory=Path(self.temporary.name)

    def cleanup(self, identifier='a', browser_started=True):
        return self.driver.cleanup_original_after_failure(identifier,self.directory,self.directory,None,None,b'bytes',{'original':'facts'},browser_started=browser_started)

    def test_lost_registration_signal_never_authorizes_deletion(self):
        with patch.object(self.driver,'remove_proved') as remove,patch.object(self.driver,'inventory') as read:
            self.assertFalse(self.cleanup(None));remove.assert_not_called();read.assert_not_called()

    def test_active_unknown_barrier_or_lease_preserves_original_source(self):
        base={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':None}
        for delta in ({'state':'Unknown'},{'state':'Applying'},{'publish_barrier':True},{'lease_token':'live'},{'lease_expires_at_utc':'future'}):
            with self.subTest(delta=delta),patch.object(self.driver,'wait_settled',return_value={'row':{**base,**delta}}),patch.object(self.driver,'remove_proved') as remove:
                self.assertFalse(self.cleanup());remove.assert_not_called()

    def test_nonterminal_root_and_changed_source_preserved_after_sql_terminal(self):
        row={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':'execution'}
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Preparing'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertFalse(self.cleanup());remove.assert_not_called()
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Aborted'}),patch.object(self.driver,'inventory',return_value={'changed':'facts'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertFalse(self.cleanup());remove.assert_not_called()

    def test_terminal_proved_original_can_be_removed(self):
        row={'state':'Canceled','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'execution_job_id':'execution'}
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'record_for',return_value={'state':'Aborted'}),patch.object(self.driver,'inventory',return_value={'original':'facts'}),patch.object(self.driver,'remove_proved') as remove:
            self.assertTrue(self.cleanup());remove.assert_called_once()

    def test_missed_post_acl_cut_never_cleans_unknown_or_active_result(self):
        with patch.object(self.driver,'remove_proved') as remove,patch.object(self.driver,'wait_settled') as wait:
            self.assertFalse(self.driver.cleanup_post_acl_failure(None,{},self.directory,self.directory,None,None,None,b'bytes'));wait.assert_not_called();remove.assert_not_called()
        for state,barrier in [('Unknown',True),('Succeeded',True)]:
            with self.subTest(state=state),patch.object(self.driver,'wait_settled',return_value={'row':{'state':state,'publish_barrier':barrier}}),patch.object(self.driver,'remove_proved') as remove:
                self.assertFalse(self.driver.cleanup_post_acl_failure('id',{},self.directory,self.directory,None,None,None,b'bytes'));remove.assert_not_called()

    def test_missed_post_acl_cut_preserves_mismatched_source(self):
        row={'state':'Succeeded','publish_barrier':False,'lease_token':None,'lease_expires_at_utc':None,'plan_json':'{}'}
        with patch.object(self.driver,'wait_settled',return_value={'row':row}),patch.object(self.driver,'inventory',return_value={}),patch.object(self.driver,'managed_plan',return_value={'targetMode':0o640}),patch.object(self.driver,'unchanged_except_target',side_effect=AssertionError('foreign source')),patch.object(self.driver,'remove_proved') as remove:
            with self.assertRaises(AssertionError):self.driver.cleanup_post_acl_failure('id',{'original':{}},self.directory,self.directory,None,None,None,b'bytes')
            remove.assert_not_called()


class AclRevocationEvidenceTests(unittest.TestCase):
    def setUp(self):
        import verify_container_acl_faults as verifier
        self.verifier=verifier
        self.subject='11111111-2222-3333-4444-555555555555'
        self.baseline={'permissionExpiresAtUtc':'2026-11-03T06:00:00+00:00'}
        self.ui={'authorityRevoked':True,'normalDualLogout':False,'renderedRevocationPage':'forbidden','hintsCleared':True,'originalIntentStatus':403,'currentIdentityStatus':200,'subject':self.subject,'cancellations':0}
        self.row={'actor_subject':self.subject,'cancel_requested_at_utc':None};self.result={'kind':'group-removal','authorityRevoked':True}
        before={'pk':41,'uuid':self.subject,'username':'whaledeck-upload-qa-123456abcdef','is_active':True,'groups':['admin-fixture']}
        self.proof={'kind':'group-removal','onlyOwnedIdentityChanged':True,'subject':self.subject,'before':before,'after':{**before,'groups':[]},'confirmedAtUtc':'2026-11-03T04:00:00+00:00'}

    def check(self):self.verifier.verify_authority(self.baseline,self.ui,self.row,self.result,self.proof)

    def test_original_unexpired_account_and_exact_native_change_are_required(self):
        self.check()
        for key,value in [('pk',42),('uuid','foreign'),('username','admin'),('is_active',False)]:
            proof=copy.deepcopy(self.proof);proof['after'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):self.verifier.verify_authority(self.baseline,self.ui,self.row,self.result,proof)
        self.baseline['permissionExpiresAtUtc']='2026-11-03T04:30:00+00:00'
        with self.assertRaises(AssertionError):self.check()

    def test_logout_cancel_uncleared_hints_or_successful_old_access_is_not_revocation(self):
        for key,value in [('normalDualLogout',True),('hintsCleared',False),('originalIntentStatus',200),('cancellations',1),('currentIdentityStatus',401)]:
            ui={**self.ui,key:value}
            with self.subTest(key=key),self.assertRaises(AssertionError):self.verifier.verify_authority(self.baseline,ui,self.row,self.result,self.proof)

    def test_disable_requires_original_groups_preserved_and_login_denial(self):
        self.result['kind']=self.proof['kind']='account-disable'
        self.ui.update(renderedRevocationPage='login',currentIdentityStatus=401)
        self.proof['after']={**self.proof['before'],'is_active':False};self.check()
        self.proof['after']['groups']=[]
        with self.assertRaises(AssertionError):self.check()

    def test_post_acl_revocation_keeps_same_authority_denial_requirements(self):
        self.result['kind']='post-acl-group-removal';self.check()
        self.result['kind']='post-acl-account-disable';self.proof['kind']='account-disable';self.proof['after']={**self.proof['before'],'is_active':False}
        self.ui.update(renderedRevocationPage='login',currentIdentityStatus=403);self.check()
        self.ui['originalIntentStatus']=200
        with self.assertRaises(AssertionError):self.check()

    def test_historical_success_read_requires_distinct_current_admin_and_original_ids(self):
        row={'id':'intent','execution_job_id':'execution','actor_subject':'old'};record={'version':'version','historyDigest':'digest'}
        intent={'id':'intent','executionJobId':'execution','state':'Succeeded','publishedVersion':'version','aclConfirmed':True,'confirmation':None,'historyVerified':True,'historyDigest':'digest','receipt':{'id':'execution','state':'Completed','bindingCurrent':True}}
        proof={'subject':'new','currentlyAdministrator':True,'readStatus':200,'readOnlyOperation':True,'normalHttpDualLogout':True,'intent':intent}
        self.verifier.verify_current_admin(proof,row,record,'old')
        for key,value in [('subject','old'),('currentlyAdministrator',False),('readOnlyOperation',False),('normalHttpDualLogout',False),('readStatus',403)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):self.verifier.verify_current_admin({**proof,key:value},row,record,'old')
        changed=copy.deepcopy(proof);changed['intent']['executionJobId']='foreign'
        with self.assertRaises(AssertionError):self.verifier.verify_current_admin(changed,row,record,'old')

    def test_legacy_sources_remain_valid_only_for_legacy_kinds(self):
        sources={n:'synthetic-sha' for n in self.verifier.SOURCES};old={k:v for k,v in sources.items() if k!='owned_authentik_revocation.py'}
        self.verifier.validate_source_profile({'sha256':old},['cancel','logout'])
        with self.assertRaises(AssertionError):self.verifier.validate_source_profile({'sha256':old},['group-removal'])
        self.verifier.validate_source_profile({'schemaVersion':2,'sha256':sources},['group-removal','account-disable'])
        for schema in (True,6,'2'):
            with self.subTest(schema=schema),self.assertRaises(AssertionError):self.verifier.validate_source_profile({'schemaVersion':schema,'sha256':sources},['group-removal'])
        with self.assertRaises(AssertionError):self.verifier.validate_source_profile({'schemaVersion':2,'sha256':old},['account-disable'])
        with self.assertRaises(AssertionError):self.verifier.validate_source_profile({'schemaVersion':2,'sha256':sources},['worker-crash'])
        self.verifier.validate_source_profile({'schemaVersion':3,'sha256':sources},['worker-crash','agent-restart'])
        with self.assertRaises(AssertionError):self.verifier.validate_source_profile({'schemaVersion':3,'sha256':sources},['post-acl-cancel'])
        self.verifier.validate_source_profile({'schemaVersion':4,'sha256':sources},['post-acl-cancel','post-acl-worker-crash'])
        with self.assertRaises(AssertionError):self.verifier.validate_source_profile({'schemaVersion':4,'sha256':sources},['post-acl-group-removal'])
        self.verifier.validate_source_profile({'schemaVersion':5,'sha256':sources},['post-acl-group-removal','post-acl-account-disable'])


class AclRestartEvidenceTests(unittest.TestCase):
    def setUp(self):
        import validate_container_acl_faults as driver
        import verify_container_acl_faults as verifier
        self.driver,self.verifier=driver,verifier
        self.row={'id':'11111111-2222-3333-4444-555555555555','execution_job_id':'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'}
        self.held=type('Held',(),{'cgroup':'whaledeck-container-acl-'+'a'*32+'.service'})()
        self.counts={key:0 for key in driver.RESTART_KEYS}

    def test_each_other_intent_blocks_restart_before_transient_or_signals(self):
        for key in self.counts:
            with self.subTest(key=key),patch.object(self.driver,'preflight',return_value={}),patch.object(self.driver,'query',return_value={**self.counts,key:1}),patch.object(self.driver,'TRANSIENT') as transient:
                with self.assertRaises(AssertionError):self.driver.restart_allowed(self.held,self.row)
                transient.glob.assert_not_called()

    def test_incomplete_or_truthy_false_idle_counts_cannot_authorize_restart(self):
        for counts in ({},{**self.counts,'acls':False}):
            with self.subTest(counts=counts),patch.object(self.driver,'preflight',return_value={}),patch.object(self.driver,'query',return_value=counts),self.assertRaises(AssertionError):self.driver.restart_allowed(self.held,self.row)

    def test_unrelated_transient_or_inactive_live_pid_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);(root/'whaledeck-other.service').touch()
            for active,pid in [('active','123'),('inactive','123')]:
                props='ActiveState='+active+'\nMainPID='+pid+'\nControlPID=0\nControlGroup=\n'
                with self.subTest(active=active),patch.object(self.driver,'preflight',return_value={}),patch.object(self.driver,'query',return_value=self.counts),patch.object(self.driver,'TRANSIENT',root),patch.object(self.driver,'command',return_value=props),self.assertRaises(AssertionError):self.driver.restart_allowed(self.held,self.row)

    def test_changed_operation_or_lease_cannot_be_a_natural_wait(self):
        before={key:'original' for key in ('id','execution_job_id','container_id','instance_id','actor_subject','owner_hash','root_started_at_utc','plan_json','command_json')}
        before.update(state='Applying',publish_barrier=True,completed_at_utc=None,cancel_requested_at_utc=None,lease_token='lease',lease_expires_at_utc='future')
        self.driver.lease_held(before,before)
        for key,value in [('id','other'),('lease_token','new'),('lease_expires_at_utc','changed'),('publish_barrier',False),('state','Failed'),('cancel_requested_at_utc','now')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):self.driver.lease_held(before,{**before,key:value})

    def test_shortened_expired_unbounded_or_naive_lease_refused(self):
        import datetime as dt
        now=dt.datetime(2026,11,3,tzinfo=dt.timezone.utc)
        for seconds in (-1,20,96):
            with self.subTest(seconds=seconds),self.assertRaises(AssertionError):self.driver.observation_expiry({'lease_token':'lease','lease_expires_at_utc':(now+dt.timedelta(seconds=seconds)).isoformat()},now)
        with self.assertRaises(AssertionError):self.driver.observation_expiry({'lease_token':'lease','lease_expires_at_utc':'2026-11-03T00:01:00'},now)
        self.driver.observation_expiry({'lease_token':'lease','lease_expires_at_utc':(now+dt.timedelta(seconds=60)).isoformat()},now)

    def test_maintenance_probe_requires_bounded_healthy_ready_no_store(self):
        good=json.dumps({'state':'healthy','phase':'Ready'}).encode();self.driver.maintenance_response(200,good,'no-store')
        for status,raw,cache in [(503,good,'no-store'),(200,good,''),(200,b' '*2049,'no-store'),(200,b'{"state":"offline","phase":"Ready"}','no-store'),(200,b'{"state":"healthy","phase":"Ready","secret":"fixture"}','no-store')]:
            with self.subTest(status=status,cache=cache),self.assertRaises(AssertionError):self.driver.maintenance_response(status,raw,cache)

    def test_only_agent_pid_changes_and_both_hosts_keep_frozen_source(self):
        revision='a'*40;before={'whaledeck-agent.service':{'pid':100,'sourceRevision':revision},'whaledeck-maintenance.service':{'pid':200,'sourceRevision':revision}}
        after={**before,'whaledeck-agent.service':{'pid':101,'sourceRevision':revision}};self.driver.require_host_restart(before,after,revision)
        for changed in [before,{**after,'whaledeck-maintenance.service':{'pid':201,'sourceRevision':revision}},{**after,'whaledeck-agent.service':{'pid':101,'sourceRevision':'b'*40}}]:
            with self.subTest(changed=changed),self.assertRaises(AssertionError):self.driver.require_host_restart(before,changed,revision)

    def test_actual_worker_id_image_and_start_time_must_bind_to_baseline(self):
        baseline={'coreBefore':{'/whaledeck-worker':['cid','image','running','2026-11-03T00:00:00Z']}}
        service={'workerId':'cid','beforeStartedAt':'2026-11-03T00:01:00Z','afterStartedAt':'2026-11-03T00:02:00Z'}
        frame={'containerId':'cid','imageId':'image','startedAt':service['afterStartedAt'],'running':True,'observedAtUtc':'2026-11-03T00:03:00Z'}
        self.verifier.verify_worker_snapshot(service,baseline,frame)
        for key,value in [('containerId','other'),('imageId','other-image'),('startedAt',service['beforeStartedAt']),('running',False),('observedAtUtc','2026-11-03T00:01:30Z')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):self.verifier.verify_worker_snapshot(service,baseline,{**frame,key:value})

    def test_original_full_lease_observation_cannot_finish_early_or_skip_gap(self):
        import datetime as dt
        began=dt.datetime(2026,11,3,tzinfo=dt.timezone.utc);at=lambda seconds:(began+dt.timedelta(seconds=seconds)).isoformat()
        raw=b'{"state":"Preparing","historyVerified":true}';digest=hashlib.sha256(raw).hexdigest()
        row={'state':'Applying','publish_barrier':True,'completed_at_utc':None,'cancel_requested_at_utc':None,'lease_token':'lease','lease_expires_at_utc':at(60)}
        proof={'originalLeaseToken':'lease','originalLeaseExpiresAtUtc':at(60),'waitBeganAtUtc':at(0),'waitEndedAtUtc':at(60),'monotonicWaitSeconds':60,'clocksOrSqlModified':False,'rootSha256':digest,'rootStateAfterHelperExit':'Preparing',
            'samples':[{'atUtc':at(i),'state':'Applying','barrier':True,'leaseToken':'lease','leaseExpiresAtUtc':at(60),'rootSha256':digest,'sourceFullBytesAclsAndAttributesUnchanged':True} for i in range(0,60,2)]}
        final={'completed_at_utc':at(61)};self.verifier.verify_lease_proof(row,final,proof,raw)
        for delta in ({'waitEndedAtUtc':at(59)},{'originalLeaseToken':'other'},{'clocksOrSqlModified':True},{'samples':proof['samples'][:3]}):
            with self.subTest(delta=delta),self.assertRaises(AssertionError):self.verifier.verify_lease_proof(row,final,{**proof,**delta},raw)
        with self.assertRaises(AssertionError):self.verifier.verify_lease_proof(row,{'completed_at_utc':at(59)},proof,raw)


if __name__=='__main__':unittest.main()
