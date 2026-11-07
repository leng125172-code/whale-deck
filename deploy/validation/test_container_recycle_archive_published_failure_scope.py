"""Fixed-case authority rejects arbitrary paths before any read or mutation."""
import ast,unittest
from pathlib import Path
from unittest.mock import patch
import recover_archive_published_restart_failure as recovery


class FailedCaseScopeTests(unittest.TestCase):
    def test_naturally_expired_owned_sleep_is_not_restarted_or_rebound(self):
        f=dict(containerId='id',containerName='name',instanceId='123:original-start');state=dict(Running=False,Status='exited',Pid=0,ExitCode=0,OOMKilled=False,Paused=False,Restarting=False,StartedAt='original-start')
        with patch.object(recovery,'verify_container'),patch.object(recovery.common,'command') as cmd:
            recovery.fixture_state(dict(State=state),f,Path('/owned'))
            for key,bad in (('StartedAt','other-start'),('ExitCode',1),('OOMKilled',True),('Running',True)):
                with self.subTest(key=key),self.assertRaises(AssertionError):recovery.fixture_state(dict(State={**state,key:bad}),f,Path('/owned'))
            cmd.assert_not_called()

    def test_only_fixed_sealed_failure_is_an_authority(self):
        for name in ('../foreign','container-recycle-archive-published-restart-'+'a'*32):
            with self.subTest(name=name),patch.object(Path,'read_bytes') as read,self.assertRaises(AssertionError):recovery.failed_case(Path('/tmp'),name)
            read.assert_not_called()

    def test_disposal_never_confuses_api_with_failed_ui_gate_or_replays_apply(self):
        root=Path(__file__).parent;s=(root/'recover_archive_published_restart_failure.py').read_text();self.assertIn('originalGatePassed=False',s);self.assertIn('actualUi=False',s)
        for bad in ('freeze_published_namespace(',"'confirm'","'apply'",'SIGKILL','SIGSTOP'):self.assertNotIn(bad,s)
        self.assertLess(s.index('independent(repo,e,True)'),s.index('cleanup_owned_target(folder'))
        tree=ast.parse((root/'verify_archive_published_restart_failure.py').read_text());fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='verify');calls={ast.unparse(n.func) for n in ast.walk(fn) if isinstance(n,ast.Call)}
        for bad in ('cleanup_owned_target','os.unlink','common.command','subprocess.Popen'):self.assertNotIn(bad,calls)


if __name__=='__main__':unittest.main()
