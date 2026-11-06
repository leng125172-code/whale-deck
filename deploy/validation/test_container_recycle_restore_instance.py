"""Returned-instance transitions remain owned, single and physically unchanged."""
import copy,json,tempfile,unittest
from types import SimpleNamespace
from pathlib import Path
from unittest.mock import patch
import restore_instance_proof as proof
import test_container_recycle_instance as prior


def case():
    f,t=prior.RecycleInstanceTests().fixture();f['returned']=copy.deepcopy(f['original']);f['returned']['identity'][8]=21
    for key in ('sourceBefore','sourceAfter','newInstanceSource'):t[key]=copy.deepcopy(f['returned'])
    t['checks']=dict(otherJobs=0,terminals=0,hostLocks=0,uploadBarriers=0,directoryBarriers=0)
    t['checksBeforeRestart']=t['checks'].copy();t['counts']=dict.fromkeys(proof.TABLES,0);t['countsBeforeRestart']=t['counts'].copy()
    return f,t


class RestoreInstanceTests(unittest.TestCase):
    def test_returned_ctime_is_preserved_across_a_new_instance(self):
        f,t=case();proof.require_transition(f,t)
        t['helperAliveAfterRestart']=True;proof.require_transition(f,t)
        bad=copy.deepcopy(t);bad['sourceAfter']['identity'][8]+=1
        with self.assertRaises(AssertionError):proof.require_transition(f,bad)

    def test_same_cid_image_mount_limits_and_new_pid_timestamp_are_required(self):
        f,t=case()
        for change in [lambda v:v['after'].update(Id='c'*64),lambda v:v['after'].update(Image='foreign'),lambda v:v['after']['HostConfig'].update(NetworkMode='host'),lambda v:v['after']['HostConfig'].update(Privileged=True),lambda v:v['after']['Mounts'][0].update(Source='/etc'),lambda v:v['after']['State'].update(Pid=f['containerPid']),lambda v:v['after']['State'].update(StartedAt=t['before']['State']['StartedAt'])]:
            bad=copy.deepcopy(t);change(bad)
            with self.subTest(bad=bad['after']['State']),self.assertRaises(AssertionError):proof.require_transition(f,bad)

    def test_single_restart_ack_and_core_identity_are_mandatory(self):
        f,t=case()
        for key,value in [('restartAttemptedOnce',False),('commandAcknowledgement','foreign'),('oldInstanceId','foreign'),('helperAliveBeforeRestart',False),('coreAfter',{'restarted':'core'})]:
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_transition(f,{**t,key:value})

    def test_other_restore_work_and_noninteger_counts_refuse_transition(self):
        f,t=case();self.assertEqual(len(proof.TABLES),13);self.assertIn('container_recycle_restore_intents',proof.TABLES)
        for name in ('counts','countsBeforeRestart'):
            for bad in (1,False):
                v=copy.deepcopy(t);v[name]['container_recycle_restore_intents']=bad
                with self.subTest(name=name,bad=bad),self.assertRaises(AssertionError):proof.require_transition(f,v)
        v=copy.deepcopy(t);v['counts'].pop('container_recycle_restore_intents')
        with self.assertRaises(AssertionError):proof.require_transition(f,v)

    def test_new_namespace_reader_rejects_changed_frame_before_pidfd(self):
        f,t=case();bad=copy.deepcopy(t['after']);bad['State']['Pid']=999
        with patch.object(proof,'command',return_value=json.dumps([bad])),patch.object(proof.os,'pidfd_open') as opened,self.assertRaises(AssertionError):proof.read_new_source(f,t['after'])
        opened.assert_not_called()

    def test_scope_refusal_precedes_any_docker_read_or_restart(self):
        with patch.object(proof.os,'geteuid',return_value=0),patch.object(proof,'command') as command,self.assertRaises(AssertionError):proof.prepare(dict(identifier='a'*64,folder=Path('/etc')),dict(containerId='b'*64,folder='/etc'),None,{}, {})
        command.assert_not_called()

    def test_preflight_counts_all_restore_intents_except_exact_own_row(self):
        f,t=case();row=dict(id='10000000-0000-0000-0000-000000000001',execution_job_id='20000000-0000-0000-0000-000000000001')
        with patch.object(proof,'sql',return_value={}),patch.object(proof,'lease_held') as lease,patch.object(proof,'preflight',return_value=t['checks']),patch.object(proof,'query',return_value=t['counts']) as query:
            checks,counts=proof.other_work(row);self.assertEqual(counts,t['counts']);lease.assert_called_once_with(row,{})
            text=query.call_args.args[0];self.assertIn("container_recycle_restore_intents WHERE (completed_at_utc IS NULL OR publish_barrier OR lease_token IS NOT NULL OR lease_expires_at_utc IS NOT NULL) AND id<>:'id'::uuid",text);self.assertEqual(text.count("id<>:'id'::uuid"),1)

    def test_already_exited_original_is_not_signaled_and_resume_decision_cannot_repeat(self):
        f,t=case();owned=dict(instanceRestartAttempted=True);held=SimpleNamespace(pid=321,descriptor=44,cgroup='original',proof=dict(pid=321,cgroup='original',invocationId='a'*32))
        with tempfile.TemporaryDirectory() as d,patch.object(proof.select,'select',return_value=([44],[],[])),patch.object(proof.signal,'pidfd_send_signal') as signal:
            result=proof.resume_after_restart(owned,f,held,{},t,Path(d));self.assertFalse(result['resumeExactOriginal']);self.assertTrue(result['alreadyExited']);self.assertEqual(held.descriptor,44);signal.assert_not_called()
            with self.assertRaises(AssertionError):proof.resume_after_restart(owned,f,held,{},t,Path(d))

    def test_instance_exit_is_observed_before_normal_logout_and_independent_cleanup(self):
        root=Path(__file__).parent;s=(root/'validate_container_recycle_restore_instance.py').read_text();browser=(root/'validate_container_recycle_restore_instance.mjs').read_text()
        self.assertLess(s.index('pool.submit(held.wait_exit_seconds'),s.index('transition=restart('))
        self.assertLess(s.index('transition=restart('),s.index('resume=resume_after_restart('))
        self.assertLess(s.index('independent(repo,e,True)'),s.index('leaf.unlink()'))
        self.assertLess(browser.index("waitSignal(evidence, 'instance-exited.json'"),browser.index('const firstLogout = await logoutFromUserMenu'))
        for forbidden in ('page.route(', 'addCookies(', 'storageState:', "name: '确认请求取消'"):self.assertNotIn(forbidden,browser)


if __name__=='__main__':unittest.main()
