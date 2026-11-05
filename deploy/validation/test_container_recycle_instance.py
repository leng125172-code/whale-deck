import copy
import inspect
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import container_recycle_instance_proof as proof
import validate_container_recycle_instance as gate


class RecycleInstanceTests(unittest.TestCase):
    def fixture(self):
        cid='a'*64;name='wdv-container-publish-'+'b'*32;folder='/data/WhaleDeck/apps/'+name
        mount=dict(Type='bind',Source=folder,Target='/work',ReadOnly=False)
        host=dict(NetworkMode='none',ReadonlyRootfs=True,Binds=None,Mounts=[mount],CapDrop=['ALL'],SecurityOpt=['no-new-privileges'],Privileged=False,PidMode='',IpcMode='private',PidsLimit=16,Memory=32*1024**2,NanoCpus=250000000,RestartPolicy=dict(Name='no',MaximumRetryCount=0))
        frame=dict(Id=cid,Name='/'+name,Image=proof.owned.common.IMAGE,Mounts=[dict(Type='bind',Source=folder,Destination='/work',RW=True)],Config=dict(User='1000:1000',Labels={'io.whaledeck.validation':name}),HostConfig=host,State=dict(Running=True,Pid=200,StartedAt='2026-11-05T10:00:00Z'))
        original=dict(identity=[1,2,33184,1000,1000,1,7,10,11],sha256='bytes',attributes=[],sizeBytes=7)
        f=dict(containerId=cid,containerName=name,folder=folder,containerPid=200,instanceId='200:'+frame['State']['StartedAt'],original=original)
        after=copy.deepcopy(frame);after['State'].update(Pid=201,StartedAt='2026-11-05T10:00:01Z')
        t=dict(before=frame,after=after,oldInstanceId=f['instanceId'],newInstanceId='201:'+after['State']['StartedAt'],restartAttemptedOnce=True,commandAcknowledgement=cid,helperAliveBeforeRestart=True,helperAliveAfterRestart=False,sourceBefore=original,sourceAfter=original,newInstanceSource=original,coreBefore={'core':'same'},coreAfter={'core':'same'},checks=dict(otherJobs=0),counts=dict.fromkeys(proof.TABLES,0))
        return f,t

    def test_exact_owned_instance_transition_accepts_early_helper_exit(self):
        f,t=self.fixture();proof.require_transition(f,t)
        t['helperAliveAfterRestart']=True;proof.require_transition(f,t)

    def test_recreated_or_privileged_resource_cannot_be_an_instance_change(self):
        f,t=self.fixture()
        for mutate in [lambda v:v['after'].update(Id='c'*64),lambda v:v['after'].update(Image='different'),lambda v:v['after']['Config'].update(User='0'),lambda v:v['after']['HostConfig'].update(NetworkMode='host'),lambda v:v['after']['HostConfig'].update(Privileged=True),lambda v:v['after']['Mounts'][0].update(Source='/etc')]:
            bad=copy.deepcopy(t);mutate(bad)
            with self.assertRaises(AssertionError):proof.require_transition(f,bad)

    def test_pid_started_at_ack_and_single_attempt_are_all_required(self):
        f,t=self.fixture()
        for field,bad in [('newInstanceId',f['instanceId']),('commandAcknowledgement','wrong'),('restartAttemptedOnce',False),('helperAliveBeforeRestart',False)]:
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.require_transition(f,{**t,field:bad})
        for field,bad in [('Pid',200),('StartedAt',t['before']['State']['StartedAt'])]:
            altered=copy.deepcopy(t);altered['after']['State'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.require_transition(f,altered)

    def test_original_bytes_inode_new_namespace_and_other_work_must_match(self):
        f,t=self.fixture()
        for key in ('sourceBefore','sourceAfter','newInstanceSource'):
            bad=copy.deepcopy(t);bad[key]={**bad[key],'sha256':'changed'}
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_transition(f,bad)
        for key,value in [('coreAfter',{'core':'restarted'}),('counts',{**t['counts'],'container_copy_intents':1})]:
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.require_transition(f,{**t,key:value})

    def test_new_namespace_reader_checks_current_exact_frame_before_opening_proc(self):
        f,t=self.fixture();wrong=copy.deepcopy(t['after']);wrong['State']['Pid']=999
        with patch.object(proof,'command',return_value=json.dumps([wrong])),patch.object(proof.os,'pidfd_open') as open_pid,self.assertRaises(AssertionError):proof.read_new_source(f,t['after'])
        open_pid.assert_not_called()

    def test_foreign_fixture_is_refused_before_any_docker_command(self):
        with patch.object(proof,'command') as command,self.assertRaises(AssertionError):proof.prepare_instance(dict(identifier='a'*64,folder=Path('/tmp/foreign')),dict(containerId='a'*64,folder='/tmp/foreign'),SimpleNamespace(),{})
        command.assert_not_called()

    def test_frame_omits_environment_and_ack_requires_observation_after_completion(self):
        f,t=self.fixture();raw=copy.deepcopy(t['after']);raw['Config']['Env']=['SYNTHETIC_SECRET=value'];raw['unrelated']='ignored'
        frame=proof.instance_frame(raw);self.assertNotIn('Env',frame['Config']);self.assertNotIn('unrelated',frame)
        with tempfile.TemporaryDirectory() as folder:
            e=Path(folder);(e/'instance-transition.json').write_text(json.dumps(t));(e/'instance-restart-request.json').write_text(json.dumps(dict(containerId=f['containerId'],before=t['before'],restartAttemptedOnce=True)))
            (e/'instance-after-ack.json').write_text(json.dumps(dict(frame=t['after'],source=f['original'],observedAtUtc='2026-11-05T10:00:03Z')))
            proof.verify_instance(f,e,dict(completed_at_utc='2026-11-05T10:00:02Z'))
            with self.assertRaises(AssertionError):proof.verify_instance(f,e,dict(completed_at_utc='2026-11-05T10:00:04Z'))

    def test_gate_starts_original_pidfd_observation_before_restart_and_never_replays(self):
        code=Path(gate.__file__).read_text();self.assertLess(code.index("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'"),code.index('prepared=prepare_instance('));self.assertLess(code.index('pool.submit(held.wait_exit_seconds,began,20)'),code.index('transition=restart_instance('))
        self.assertIn("f['retain_unsettled']=True",code);self.assertLess(code.index("e/'independent-live.log'"),code.index('leaf.unlink()'))
        fault=inspect.getsource(proof.restart_instance);self.assertLess(fault.index("f['instanceRestartAttempted']=True"),fault.index("command('docker','restart'"));self.assertNotIn('os.rename',fault)


if __name__=='__main__':unittest.main()
