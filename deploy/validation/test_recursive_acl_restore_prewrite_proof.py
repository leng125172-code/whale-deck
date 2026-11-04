"""Actual temporary early-abort ACL/HDD + negative scope tests, no installed RPC."""
import copy
from contextlib import contextmanager
import json
import os
from pathlib import Path
import unittest
from types import SimpleNamespace
from unittest.mock import patch
import test_recursive_acl_restore_record_probe_scope as native
from recursive_acl_restore_agent_io import read_bundle
from recursive_acl_restore_record_proof import records_proof
from recursive_acl_restore_prewrite_proof import early_abort_proof,read_early
from bound_container_recursive_acl_restore_cut import first_entry_boundary,freeze_before_first_restore,invocation_proof,bound_fds,pidfd_same


def boundary():
    return dict(state='Applying',startedThrough=0,finishedThrough=-1,historyVerified=True,historyMayExist=True,historyDigest='A'*64,plan=dict(restoreSource=dict(changedAclType='access'),entryCount=26,changeCount=26,fileCount=25,directoryCount=1))

class RestorePrewriteScopeTests(unittest.TestCase):
    def test_cut_only_accepts_26_access_objects_after_verified_current_hdd(self):
        self.assertTrue(first_entry_boundary(boundary()))
        for key,value in [('state','Preparing'),('state','Completed'),('startedThrough',1),('finishedThrough',0),('historyVerified',False),('historyMayExist',False),('historyDigest','a'*64)]:
            r=boundary();r[key]=value
            with self.subTest(key=key,value=value):self.assertFalse(first_entry_boundary(r))
        for key,value in [('entryCount',4),('changeCount',25),('fileCount',24),('directoryCount',True)]:
            r=boundary();r['plan'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):first_entry_boundary(r)
        r=boundary();r['plan']['restoreSource']['changedAclType']='default'
        with self.assertRaises(AssertionError):first_entry_boundary(r)
    def test_wrong_owned_scope_rejected_before_any_process_signal(self):
        with patch('bound_container_recursive_acl_restore_cut.os.geteuid',return_value=0),patch('bound_container_recursive_acl_restore_cut.os.pidfd_open',side_effect=AssertionError('must not reach pidfd')):
            for folder in ('/etc','/data/WhaleDeck/apps/not-owned','/data/WhaleDeck/apps/wdv-container-publish-'+'A'*32):
                with self.subTest(folder=folder),self.assertRaises(AssertionError):freeze_before_first_restore(dict(container_id='a'*64),folder,2,{},lambda:{})
    def test_invocation_requires_real_unit_main_pid_cgroup_and_private_env_match(self):
        unit='whaledeck-container-recursive-acl-restore-'+'b'*32+'.service';inv='a'*32;pid=1234
        prefix='bound_container_recursive_acl_restore_cut.'
        props=f'InvocationID={inv}\nMainPID={pid}\nControlGroup=/system.slice/{unit}\n'
        with patch(prefix+'Path.read_text',return_value='0::/system.slice/'+unit),patch(prefix+'Path.read_bytes',return_value=('INVOCATION_ID='+inv+'\0SECRET=never-export\0').encode()),patch(prefix+'subprocess.check_output',return_value=props):
            value=invocation_proof(pid,unit);self.assertEqual(inv,value['invocationId']);self.assertNotIn('SECRET',json.dumps(value))
        for bad in (props.replace(inv,'c'*32),props.replace(str(pid),'9999'),props.replace('ControlGroup=/system.slice/','ControlGroup=/other/').replace(unit,'different.service')):
            with patch(prefix+'Path.read_text',return_value='0::/system.slice/'+unit),patch(prefix+'Path.read_bytes',return_value=('INVOCATION_ID='+inv+'\0').encode()),patch(prefix+'subprocess.check_output',return_value=bad),self.assertRaises(AssertionError):invocation_proof(pid,unit)
        for unit_bad in ('whaledeck-agent.service','../escape','whaledeck-container-recursive-acl-'+'b'*32+'.service'):
            with self.subTest(unit=unit_bad),self.assertRaises(AssertionError):invocation_proof(pid,unit_bad)
    def test_worker_receipt_cannot_export_runtime_environment_or_secrets(self):
        from recursive_acl_restore_prewrite import worker_receipt
        value=dict(Id='a'*64,Name='/whaledeck-worker',Image='b'*64,Config=dict(Env=['PASSWORD=should-never-leave-memory']),State=dict(Running=True,StartedAt='time',Health=dict(Status='healthy',Log=['private-body'])))
        raw=json.dumps(worker_receipt(value));self.assertNotIn('PASSWORD',raw);self.assertNotIn('Config',raw);self.assertNotIn('private-body',raw)
    def test_actual_gate_paths_and_ui_confirmation_cancellation_are_bounded(self):
        from verify_recursive_acl_restore_prewrite import verify
        for name in ('../escape','recursive-acl-restore-paging-'+'a'*32,'recursive-acl-restore-prewrite-cancel-'+'A'*32):
            with self.subTest(name=name),self.assertRaises(AssertionError):verify(name)
        source=(Path(__file__).parent/'validate_recursive_acl_restore_prewrite.mjs').read_text()
        for marker in ('确认递归ACL恢复','确认请求取消','dismissedWithoutWrite:true','normalDualLogout:true','ordinary-normal-dual-logout'):self.assertIn(marker,source)
        for marker in ('.route(','.addCookies(','sessionStorage.setItem'):self.assertNotIn(marker,source)

    def test_fd_binding_requires_exact_restore_argv_root_uid_and_both_kernel_inodes(self):
        prefix='bound_container_recursive_acl_restore_cut.'
        args=b'/usr/bin/python3\0-I\0/usr/local/libexec/whaledeck-container-recursive-acl-restore-transport.py\0apply\0'
        for supplied,uid,ids,wanted in ((args,0,[(1,2),(3,4)],True),(args,1000,[(1,2),(3,4)],False),(args.replace(b'restore-transport',b'transport'),0,[(1,2),(3,4)],False),(args,0,[(1,2),(3,5)],False)):
            with patch(prefix+'Path.read_bytes',return_value=supplied),patch(prefix+'Path.stat',return_value=SimpleNamespace(st_uid=uid)),patch(prefix+'Path.iterdir',return_value=[Path('/mock1'),Path('/mock2')]),patch(prefix+'identity',side_effect=ids):self.assertEqual(wanted,bound_fds(1234,(1,2),(3,4)))

    def test_pidfd_must_still_name_original_live_pid_and_start_time(self):
        prefix='bound_container_recursive_acl_restore_cut.'
        for ready,pid_value,actual_start,wanted in (([],1234,99,True),([5],1234,99,False),([],-1,99,False),([],1234,100,False)):
            with patch(prefix+'select.select',return_value=(ready,[],[])),patch(prefix+'Path.read_text',return_value=f'Pid:\t{pid_value}\n'),patch(prefix+'process_start',return_value=actual_start):self.assertEqual(wanted,pidfd_same(5,1234,99))

    def test_real_linux_pidfd_pins_live_self_and_detects_owned_child_exit(self):
        import subprocess
        from bound_container_recursive_acl_restore_cut import process_start
        fd=os.pidfd_open(os.getpid())
        try:
            self.assertTrue(pidfd_same(fd,os.getpid(),process_start(os.getpid())))
            self.assertFalse(pidfd_same(fd,os.getpid(),process_start(os.getpid())+1))
        finally:os.close(fd)
        child=subprocess.Popen(['/bin/sleep','0.1'])
        fd=os.pidfd_open(child.pid)
        try:
            began=process_start(child.pid);self.assertTrue(pidfd_same(fd,child.pid,began));child.wait(timeout=2);self.assertFalse(pidfd_same(fd,child.pid,began))
        finally:os.close(fd);child.wait(timeout=2)

    def test_failed_gate_keeps_container_until_original_execution_is_quiescent(self):
        from recursive_acl_restore_prewrite import quiescent
        row=dict(execution_job_id='original',state='Applying',lease_token='lease')
        self.assertFalse(quiescent(row,dict(state='Completed'),True))
        row.update(state='Unknown',lease_token=None)
        self.assertFalse(quiescent(row,dict(state='Applying'),True));self.assertFalse(quiescent(row,dict(state='Unknown'),False));self.assertTrue(quiescent(row,dict(state='Unknown'),True))
        self.assertFalse(quiescent(dict(execution_job_id=None,completed_at_utc=None),None,False));self.assertTrue(quiescent(dict(execution_job_id=None,completed_at_utc='expired'),None,False))
    def test_retention_switch_cannot_target_arbitrary_container_or_source(self):
        from validate_container_file_publishing import retain_unsettled_fixture,common
        owned=dict(folder=common.APPS/('wdv-container-publish-'+'a'*32),identifier='b'*64,retain_unsettled=True)
        self.assertTrue(retain_unsettled_fixture(owned));self.assertFalse(retain_unsettled_fixture({}))
        for replacement in (dict(folder='/etc'),dict(identifier='arbitrary'),dict(retain_unsettled='true')):
            with self.subTest(replacement=replacement),self.assertRaises(AssertionError):retain_unsettled_fixture({**owned,**replacement})

    @unittest.skipUnless(os.geteuid()==0,'fixture chown uses temporary root-owned test directory')
    def test_failed_unsettled_fixture_retains_container_and_propagates_error(self):
        import tempfile,contextlib,io
        import validate_container_file_publishing as owned
        calls=[]
        def command(args):
            calls.append(args)
            if args[:3]==['docker','image','inspect']:return json.dumps([dict(Id=owned.common.IMAGE,Config=dict(Volumes=None))]).encode()
            if args[:2]==['docker','run']:return ('a'*64).encode()
            if args[:2]==['docker','inspect']:return b'[{}]'
            raise AssertionError('must not remove or otherwise mutate Docker')
        with tempfile.TemporaryDirectory() as folder,patch.object(owned.common,'APPS',Path(folder)),patch.object(owned.common,'verify_apps_directory'),patch.object(owned.common,'core_snapshot',return_value={}),patch.object(owned.common,'command',side_effect=command),patch.object(owned,'verify_container'),patch.object(owned.pwd,'getpwnam',return_value=SimpleNamespace(pw_uid=997)),contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(RuntimeError,'original failure'):
                with owned.fixture() as f:
                    kept=f['folder'];f['retain_unsettled']=True;raise RuntimeError('original failure')
            self.assertTrue(kept.exists());self.assertTrue((kept/'original.bin').exists());self.assertFalse(any(v[:2]==['docker','rm'] for v in calls))

    @unittest.skipUnless(os.geteuid()==0,'actual private record/lock FD requires root')
    def test_failure_drain_uses_bound_original_record_and_free_kernel_lock(self):
        import tempfile,fcntl
        from recursive_acl_restore_prewrite import drain_original
        from test_recursive_acl_restore_agent_probe_scope import synthetic,wire
        from test_recursive_acl_restore_business_proof import managed
        plan=synthetic();op='a'*32;owner='B'*64;identifier='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
        data=dict(row=dict(execution_job_id=identifier,state='Unknown',lease_token=None,owner_hash=owner,plan_json=json.dumps(managed(wire(plan)))))
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);evidence=root/'evidence';evidence.mkdir(mode=0o700);record=root/(op+'.json');lock=root/(op+'.lock');lock.write_bytes(b'');lock.chmod(0o600)
            record.write_text(json.dumps(dict(operationId=op,owner=owner,plan=plan,state='Unknown')));record.chmod(0o600)
            with patch('recursive_acl_restore_agent_io.RESTORE_STATE',root),patch('recursive_acl_restore_prewrite.sql_snapshot',return_value=data):
                self.assertTrue(drain_original(identifier,evidence))
                fd=os.open(lock,os.O_RDONLY)
                try:
                    fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
                    with patch('recursive_acl_restore_prewrite.time.monotonic',side_effect=[0,400,400]):self.assertFalse(drain_original(identifier,evidence))
                finally:os.close(fd)
                record.write_text(json.dumps(dict(operationId='c'*32,owner=owner,plan=plan,state='Unknown')))
                with patch('recursive_acl_restore_prewrite.time.monotonic',side_effect=[0,400,400]):self.assertFalse(drain_original(identifier,evidence))

    def test_only_pidfd_stop_continue_and_no_validator_sigkill_or_arbitrary_transport(self):
        source=(Path(__file__).parent/'bound_container_recursive_acl_restore_cut.py').read_text()
        self.assertIn('signal.SIGSTOP',source);self.assertIn('signal.SIGCONT',source);self.assertNotIn('SIGKILL',source)
        self.assertIn('STOPPED_RESTORE_FD_BINDING_CHANGED',source);self.assertIn('invocation_proof(pid,group)',source)

@unittest.skipUnless(os.geteuid()==0,'actual temporary root-private ACL/HDD required')
class RestoreEarlyAbortRecordsTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreRecordProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
    def case(self,stage):
        k=self.f.k;request,result,old=k.seed();source=dict(request=request,result=result,before=old,after=k.b.snapshot())
        bundle,frame=read_bundle(self.f.io,request,False,self.f.archive/'source',create=True);source['metadata']=bundle['metadata'];records_proof(source,bundle,restore=False,attribute_digest=self.f.attrs)
        before=k.b.snapshot();restore=k.request(request,result)
        if stage=='before-start':
            k.engine.cancelled=lambda:(k.state/(restore['operationId']+'.json')).exists() and k.record(restore)['state']=='Applying'
        elif stage=='before-child':
            def checkpoint(phase,index):
                if phase=='BeforeEntry' and index==0:raise RuntimeError('owned temporary before child')
            k.engine.checkpoint=checkpoint
        else:
            original=k.engine.child_engine
            @contextmanager
            def child_engine(state,request,create):
                with original(state,request,create) as (child,fd):
                    if create:
                        def checkpoint(phase):
                            if phase==('Applying' if stage=='applying-child' else 'Prepared'):raise RuntimeError('owned temporary before ACL write')
                        child.checkpoint=checkpoint
                    yield child,fd
            k.engine.child_engine=child_engine
        with self.assertRaises(Exception):k.engine.apply(restore,k.b.base.session)
        receipt=k.engine.inspect(restore,k.b.base.session);after=k.b.snapshot();self.assertEqual(before,after)
        saved=read_early(self.f.io,restore,self.f.archive/'early',create=True)
        self.assertEqual(saved,read_early(self.f.io,restore,self.f.archive/'early'))
        proof=early_abort_proof(restore,receipt,saved,source,frame,before,after,self.f.attrs)
        return restore,receipt,saved,source,frame,before,after,proof
    def test_actual_aborted_after_aggregate_before_start_retains_verified_hdd(self):
        *_,proof=self.case('before-start');self.assertEqual(proof,dict(aggregateHdd=1,childHdd=0,counts=[0,0,0,4,0]))
    def test_actual_started_child_fence_no_child_history_and_no_acl_write(self):
        *_,proof=self.case('before-child');self.assertEqual(proof,dict(aggregateHdd=1,childHdd=0,counts=[0,0,1,3,0]))
    def test_actual_prepared_child_history_is_preserved_but_no_acl_written(self):
        *_,proof=self.case('prepared-child');self.assertEqual(proof,dict(aggregateHdd=1,childHdd=1,counts=[0,0,1,3,0]))
    def test_actual_applying_child_still_before_acl_write_is_aborted_with_history(self):
        *_,proof=self.case('applying-child');self.assertEqual(proof,dict(aggregateHdd=1,childHdd=1,counts=[0,0,1,3,0]))
    def test_changed_physical_acl_or_partial_unknown_cannot_be_signed_as_zero_writes(self):
        req,receipt,bundle,source,frame,before,after,_=self.case('before-child')
        for field,value in (('state','Partial'),('state','Unknown'),('changedCount',1),('unknownCount',1),('historyVerified',False)):
            changed=copy.deepcopy(bundle);changed['root'][field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):early_abort_proof(req,receipt,changed,source,frame,before,after,self.f.attrs)
        changed=copy.deepcopy(after);next(iter(changed.values()))['access'][0]['permissions']^=1
        with self.assertRaises(AssertionError):early_abort_proof(req,receipt,bundle,source,frame,before,changed,self.f.attrs)
    def test_wrong_domain_or_mutated_current_preimage_rejected_even_with_valid_json(self):
        req,receipt,bundle,source,frame,before,after,_=self.case('prepared-child')
        for target,key,value in (('metadata','resourceType','ContainerRecursiveAclMetadata'),('metadata','owner','F'*64),('root','historyDigest','F'*64)):
            changed=copy.deepcopy(bundle);changed[target][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):early_abort_proof(req,receipt,changed,source,frame,before,after,self.f.attrs)
        changed=copy.deepcopy(bundle);changed['children'][0]['hdd']['resourceType']='ContainerRecursiveAclEntryMetadata'
        with self.assertRaises(AssertionError):early_abort_proof(req,receipt,changed,source,frame,before,after,self.f.attrs)
    def test_lost_child_or_false_confirmation_or_wrong_outcome_is_rejected(self):
        req,receipt,bundle,source,frame,before,after,_=self.case('prepared-child')
        for change in (lambda b:b['children'].clear(),lambda b:b['children'][0]['root'].update(aclConfirmed=True),lambda b:b['root'].update(outcomeDigest='F'*64),lambda b:b['children'][0]['root'].update(actualIdentity=before[next(iter(before))]['identity'])):
            changed=copy.deepcopy(bundle);change(changed)
            with self.subTest(change=change),self.assertRaises(AssertionError):early_abort_proof(req,receipt,changed,source,frame,before,after,self.f.attrs)

if __name__=='__main__':unittest.main()
