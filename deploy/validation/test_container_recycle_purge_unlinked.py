"""Temporary native unlink and owned child FDs; SQL/UI values remain fixtures."""
import copy,json,os,signal,subprocess,tempfile,unittest,uuid
from pathlib import Path
from contextlib import contextmanager
from unittest.mock import patch
import bound_container_recycle_purge_unlinked_cut as cut
import test_container_recycle_purge_cut_scope as prior
from test_container_recycle_purge_browser_proof import row,summary
from recycle_purge_unlinked_proof import row_proof,require_ack
from container_recycle_purge_restart_proof import IMMUTABLE
from validate_container_recycle_purge_unlinked_unknown import browser_proof


class StopAtUnlink(Exception):pass


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated root Linux files/pidfds required')
class UnlinkedCutTests(unittest.TestCase):
    def setUp(self):
        self.f=prior.PurgeCutTests();self.f.setUp();self.addCleanup(self.f.doCleanups)

    def at_unlink(self,inspect):
        def checkpoint(name):
            if name!='Unlinked':return
            record=json.loads((self.f.state/(self.f.q['operationId']+'.json')).read_text());number=cut.unlinked_fd(os.getpid(),self.f.q['plan']['retainedIdentity']);self.assertIsNotNone(number)
            snap=cut.snapshot_unlinked(os.getpid(),number,self.f.q['plan']['retainedIdentity'])
            with patch.object(cut,'SOURCE_STATE',self.f.f.base.state):inspect(record,snap)
            raise StopAtUnlink()
        self.f.engine.checkpoint=checkpoint
        with self.assertRaises(StopAtUnlink):self.f.engine.apply(self.f.q,self.f.f.base.session)

    def test_actual_native_unlink_is_proved_by_link_zero_fd_not_missing_names_alone(self):
        def inspect(r,s):
            v=cut.after_unlink_proof(r,self.f.folder,s);self.assertTrue(v['namespaceUnlinked']);self.assertFalse(v['durableUnlinkConfirmation']);self.assertEqual(s['identity'][5],0);self.assertEqual(v['rootPhase'],'Removing')
        self.at_unlink(inspect)

    def test_wrong_bytes_link_count_or_later_phase_cannot_pass_the_cut(self):
        def inspect(r,s):
            for key,bad in [('sha256','0'*64),('sizeBytes',s['sizeBytes']+1),('attributes',[['user.wrong','Yg==']])]:
                with self.subTest(key=key),self.assertRaises(AssertionError):cut.after_unlink_proof(r,self.f.folder,{**s,key:bad})
            wrong=copy.deepcopy(s);wrong['identity'][5]=1
            with self.assertRaises(AssertionError):cut.after_unlink_proof(r,self.f.folder,wrong)
            for phase in ('Reserved','Applying','Quarantined','Removed','Completed','Unknown'):
                self.assertIsNone(cut.after_unlink_proof({**r,'state':phase},self.f.folder,s))
        self.at_unlink(inspect)

    def test_foreign_marker_or_source_state_is_rejected(self):
        def inspect(r,s):
            path=self.f.f.base.state/(self.f.f.ref['operationId']+'.purge.json');old=json.loads(path.read_text())
            for field,bad in [('schemaVersion',True),('operationId','a'*32),('owner','A'*64),('fingerprint','A'*64),('state','Reserved')]:
                path.write_text(json.dumps({**old,field:bad}));path.chmod(0o600)
                with self.subTest(field=field),self.assertRaises(AssertionError):cut.after_unlink_proof(r,self.f.folder,s)
            path.write_text(json.dumps(old));path.chmod(0o600)
            path=self.f.f.base.state/(self.f.f.ref['operationId']+'.json');root=json.loads(path.read_text());path.write_text(json.dumps({**root,'state':'Trashed'}));path.chmod(0o600)
            with self.assertRaises(AssertionError):cut.after_unlink_proof(r,self.f.folder,s)
        self.at_unlink(inspect)

    def test_foreign_scope_is_refused_before_storage_or_signals(self):
        with patch.object(cut,'current_archive') as storage,patch.object(cut.os,'pidfd_open') as opened,self.assertRaises(AssertionError):cut.freeze_after_unlink(dict(container_id='a'*64,instance_id='777:fixture'),self.f.folder,777)
        storage.assert_not_called();opened.assert_not_called()

    def handoff(self,fail):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp);folder=base/('wdv-container-publish-'+uuid.uuid4().hex);folder.mkdir();state=base/'state';state.mkdir();sources=base/'sources';sources.mkdir();groups=base/'groups';groups.mkdir()
            original=base/'original';original.write_bytes(b'owned unlink descriptor');fd=os.open(original,os.O_RDONLY);expected=cut.source_io.frame(os.fstat(fd));original.unlink()
            child=subprocess.Popen(['/bin/sleep','20'],pass_fds=[fd]);ident=str(uuid.uuid4());op=cut.execution(ident);source='a'*32;group='whaledeck-container-recycle-purges-'+uuid.uuid4().hex+'.service';(groups/group).mkdir()
            for p in (state/(uuid.UUID(op).hex+'.lock'),sources/(source+'.lock')):p.touch(mode=0o600)
            plan=dict(parentIdentity=list(cut.identity(folder)),retainedIdentity=expected,command=dict(source=dict(operationId=source)));row=dict(id=ident,execution_job_id=op,container_id='a'*64,instance_id=str(child.pid)+':fixture',plan_json={});checked=[];real_path=Path;held=None
            def path(value):
                if str(value)=='/data/WhaleDeck/apps':return base
                if str(value)=='/data/WhaleDeck/apps/'+folder.name:return folder
                return real_path(value)
            @contextmanager
            def archive(_):
                yield (999,888),dict(currentHddFdAttested=True)
                checked.append(True)
                if fail:raise RuntimeError('final attestation failed')
            try:
                with patch.multiple(cut,STATE=state,SOURCE_STATE=sources,CGROUP=groups),patch.object(cut,'Path',side_effect=path),patch.object(cut,'managed_plan',return_value=plan),patch.object(cut,'current_archive',side_effect=archive),patch.object(cut,'unit_identity',return_value=(child.pid,'b'*32)),patch.object(cut,'bound_process',return_value=True),patch.object(cut,'record_for',return_value={'state':'Removing'}),patch.object(cut,'after_unlink_proof',return_value={}):
                    if fail:
                        with self.assertRaisesRegex(RuntimeError,'attestation'):cut.freeze_after_unlink(row,'/data/WhaleDeck/apps/'+folder.name,child.pid)
                        child.wait(timeout=2);self.assertEqual(child.returncode,-signal.SIGKILL)
                    else:
                        held=cut.freeze_after_unlink(row,'/data/WhaleDeck/apps/'+folder.name,child.pid);self.assertIsNotNone(held);self.assertEqual(checked,[True]);self.assertTrue(held.proof['fiveKernelFdProofs']);held.release();self.assertIsNone(child.poll())
            finally:
                if child.poll() is None:child.kill();child.wait(timeout=2)
                if held and held.descriptor is not None:held.release()
                os.close(fd)

    def test_successful_fd_handoff_waits_for_final_current_archive_attestation(self):self.handoff(False)
    def test_failed_attestation_terminates_bound_child_without_resuming_apply(self):self.handoff(True)


def data(ack=False):
    r=row('Acknowledged' if ack else 'Unknown')
    for key in IMMUTABLE:r.setdefault(key,key)
    r.update(version=2 if ack else 1,actor_subject='subject',container_id='cid',instance_id='instance',relative_path='work/recycle-rpc.bin',cancel_requested_at_utc='cancel',confirmed_at_utc='confirmed',completed_at_utc='completed' if ack else None,lease_token=None,lease_expires_at_utc=None,acknowledged_by='other' if ack else None,acknowledgement_json={'receipt':'fixture'} if ack else None)
    v=dict(row=r,pending=0 if ack else 2,accepted=1,confirmed=1,canceled=0,planJob=dict(state='Succeeded',result_json=summary(r,False)),executionJob=dict(state='Failed' if ack else 'Running',phase='Acknowledged' if ack else 'AwaitingContainerRecyclePurgeReceipt',completed_at_utc='now' if ack else None,cancel_requested_at_utc='cancel',result_json=summary(r)))
    return v,dict(subject='subject',containerId='cid',instanceId='instance')


class UnlinkedOutcomeTests(unittest.TestCase):
    def test_unknown_and_ack_keep_nullable_fact_and_distinct_job_lifecycle(self):
        for ack in (False,True):v,f=data(ack);row_proof(v,f,ack)

    def test_forged_applied_or_canceled_unknown_cannot_pass(self):
        for field,bad in [('purge_applied',False),('purge_applied',True),('state','Canceled'),('source_fenced',False),('publish_barrier',False),('cancel_requested_at_utc',None)]:
            v,f=data();v['row'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):row_proof(v,f,False)
        v,f=data();v['pending']=1
        with self.assertRaises(AssertionError):row_proof(v,f,False)

    def test_ack_requires_different_admin_and_unchanged_original_receipt_and_bytes(self):
        before,f=data();after,_=data(True);old=dict(sql=before,receipt={'state':'Unknown'},records={'original':'bytes'},physical={'absent':True},hdd={'sha':'same'});new={**old,'sql':after};require_ack(old,new,'other')
        for field,bad in [('records',{'changed':True}),('receipt',{'state':'Completed'}),('hdd',{'sha':'changed'})]:
            with self.subTest(field=field),self.assertRaises(AssertionError):require_ack(old,{**new,field:bad},'other')
        with self.assertRaises(AssertionError):require_ack(old,new,'subject')

    def test_browser_proof_rejects_extra_ack_write_and_same_actor(self):
        receipt=dict(normalUiLogout=True,postStatus=303,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True);f=dict(subject='subject',recoverySubject='other')
        b=dict(actualUi=True,subject='subject',recoverySubject='other',registrations=1,confirmations=1,cancellations=1,acknowledgements=1,managementWrites=1,originalSessionReadStatus=401,differentAdminOldIdStatus=404,hintsCleared=True,cancelledDecisionPreserved=True,logoutReceipts=[receipt,receipt],jobProofs=[{},{}]);browser_proof(b,f)
        for field,bad in [('acknowledgements',2),('managementWrites',2),('cancelledDecisionPreserved',False),('recoverySubject','subject')]:
            with self.subTest(field=field),self.assertRaises(AssertionError):browser_proof({**b,field:bad},f)


if __name__=='__main__':unittest.main()
