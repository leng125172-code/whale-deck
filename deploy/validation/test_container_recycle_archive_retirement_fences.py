"""Temporary real journals/markers/fsync and owned child SIGKILL cuts.

Namespace/storage attestation doubles; no deletion, installed RPC or policy UI.
"""
import ast
import copy
import fcntl
import json
import hashlib
import os
import select
import signal
import stat
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch
import test_container_file_staging as staging
import test_container_file_recycling as recycling
import test_container_recycle_archive_retention_plan as plans
from test_container_recycle_purge_plan import snapshot

candidate=staging.load('whaledeck-container-recycle-archive-retirement-fences.py','archive_retirement_fences_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ArchiveRetirementFenceTests(unittest.TestCase):
    def setUp(self):
        self.f=plans.ArchiveRetentionPlanTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        self.preflight=self.f.engine;self.session=self.f.session;self.native=self.f.f
        self.state=self.native.base.disk.disk/'retirement-state';self.state.mkdir(mode=0o700);(self.state/'claims').mkdir(mode=0o700)
        self.engine=candidate.retirement_fences_type(recycling.directories.ContainerDirectories)(self.preflight,self.state)
        self.q=dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=self.engine.plan(self.f.command,self.session))

    def protected(self):
        p=self.native.private();return dict(files=self.f.state(),stage=self.native.source.metadata(p.stat()) if p.exists() else None)
    def journal(self,q=None):return self.state/((q or self.q)['operationId']+'.json')
    def marker(self):return self.state/'claims'/(self.engine.ref(self.q)['operationId']+'.json')
    def record(self):return json.loads(self.journal().read_bytes())
    def new(self):return {**self.q,'operationId':uuid.uuid4().hex}
    def reserve(self):return self.engine.reserve(self.q,self.session)
    def change(self,path,**values):
        v=json.loads(path.read_bytes());v.update(values);path.write_text(json.dumps(v))
    def kill_at(self,phase,cancel=False):
        child=os.fork()
        if child==0:
            try:
                self.engine.checkpoint=lambda at:os.kill(os.getpid(),signal.SIGKILL) if at==phase else None
                (self.engine.discard if cancel else self.engine.reserve)(self.q,self.session)
            except BaseException:os._exit(2)
            os._exit(3)
        fd=os.pidfd_open(child)
        try:
            ready=select.select([fd],[],[],20)[0]
            if not ready:signal.pidfd_send_signal(fd,signal.SIGKILL)
            _,status=os.waitpid(child,0)
        finally:os.close(fd)
        self.assertTrue(ready);self.assertTrue(os.WIFSIGNALED(status));self.assertEqual(os.WTERMSIG(status),signal.SIGKILL)

    def test_young_reservation_is_scheduling_only_and_keeps_archive_claim_and_hdd(self):
        self.assertFalse(self.q['plan']['retentionElapsed']);before=self.protected();r=self.reserve()
        self.assertEqual(r['state'],'Reserved');self.assertTrue(r['sourceFenced'] and r['ownedFence']);self.assertFalse(r['retirementApplied']);self.assertEqual(r['notBeforeUnixMs'],self.f.due);self.assertEqual(before,self.protected());self.assertEqual(json.loads(self.marker().read_bytes()),self.engine.marker(self.q,'Reserved'))
        for action in (self.engine.apply,self.engine.recover,self.engine.private):
            with self.assertRaisesRegex(Exception,'RESERVATION_ONLY'):action(self.q,self.session)
        self.assertEqual(before,self.protected())

    def test_reserved_replay_is_observation_only_and_competitors_are_blocked(self):
        self.reserve();before=self.protected();owned=snapshot(self.state)
        with patch.object(self.preflight,'plan_locked',side_effect=AssertionError('no replay validation or execution')):
            for action in (self.engine.reserve,self.engine.inspect):self.assertEqual(action(self.q,None)['state'],'Reserved')
        self.assertEqual(owned,snapshot(self.state));other=self.new()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.assertFalse(self.journal(other).exists())
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.plan(self.f.command,self.session)
        self.assertEqual(before,self.protected())

    def test_cancel_commits_abort_then_released_tombstone_before_new_owner(self):
        self.reserve();before=self.protected();r=self.engine.discard(self.q,self.session)
        self.assertEqual(r['state'],'Aborted');self.assertFalse(r['sourceFenced'] or r['ownedFence']);self.assertEqual(json.loads(self.marker().read_bytes())['state'],'Released');self.assertEqual(before,self.protected())
        other=self.new();self.assertEqual(self.engine.reserve(other,self.session)['state'],'Reserved');marker=self.marker().read_bytes()
        for action in (self.engine.reserve,self.engine.inspect,self.engine.discard):self.assertEqual(action(self.q,None)['state'],'Aborted');self.assertEqual(self.marker().read_bytes(),marker)
        self.assertEqual(before,self.protected())

    def test_cancel_before_reserve_is_durable_and_never_late_activates(self):
        before=self.protected();self.assertEqual(self.engine.discard(self.q,None)['state'],'Aborted')
        with patch.object(self.preflight,'plan_locked',side_effect=AssertionError('no late reservation')):self.assertEqual(self.reserve()['state'],'Aborted')
        self.assertFalse(self.marker().exists());self.assertEqual(before,self.protected())

    def test_cancel_after_deadline_or_copy_loss_only_releases_own_metadata(self):
        self.reserve();self.native.base.leaf.rename(self.native.base.leaf.with_suffix('.held'));before=self.protected()
        with self.f.clock(self.f.due+1):r=self.engine.discard(self.q,self.session)
        self.assertEqual(r['state'],'Aborted');self.assertEqual(json.loads(self.marker().read_bytes())['state'],'Released');self.assertEqual(before,self.protected())

    def test_explicit_cancel_requires_valid_session_and_preserves_reserved_on_failure(self):
        self.reserve();before=snapshot(self.state)
        with self.assertRaisesRegex(Exception,'SESSION_REQUIRED'):self.engine.discard(self.q,None)
        self.assertEqual(before,snapshot(self.state))
        with patch.object(self.session,'revalidate',side_effect=RuntimeError('lost authority')),self.assertRaisesRegex(RuntimeError,'lost authority'):self.engine.discard(self.q,self.session)
        self.assertEqual(before,snapshot(self.state))

    def test_actual_sigkill_cuts_never_replay_or_touch_payload(self):
        for phase,expected,marked in (('Preparing','Aborted',None),('SourceFenced','Unknown','Reserved'),('Reserved','Reserved','Reserved'),('AbortedSaved','Aborted','Released'),('Released','Aborted','Released')):
            with self.subTest(phase=phase):
                f=ArchiveRetirementFenceTests();f.setUp()
                try:
                    if phase in ('AbortedSaved','Released'):f.reserve()
                    before=f.protected();f.kill_at(phase,cancel=phase in ('AbortedSaved','Released'));cold=type(f.engine)(f.preflight,f.state)
                    with patch.object(f.preflight,'plan_locked',side_effect=AssertionError('never resume')):
                        r=cold.inspect(f.q,None);self.assertEqual(r['state'],expected);self.assertFalse(r['retirementApplied'])
                    self.assertEqual(json.loads(f.marker().read_bytes())['state'] if f.marker().exists() else None,marked);self.assertEqual(before,f.protected())
                finally:f.doCleanups()

    def test_lost_marker_is_blocked_by_active_journal_before_observation(self):
        self.reserve();self.marker().unlink();before=self.protected();other=self.new()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.assertFalse(self.marker().exists());self.assertFalse(self.journal(other).exists());r=self.engine.inspect(self.q,None);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['sourceFenced']);self.assertFalse(r['ownedFence'])
        for action in (self.engine.reserve,self.engine.discard):self.assertEqual(action(self.q,None)['state'],'Unknown');self.assertFalse(self.marker().exists())
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.assertEqual(before,self.protected())

    def test_orphan_marker_blocks_new_id_and_reconstructs_only_unknown(self):
        self.reserve();self.journal().unlink();marker=self.marker().read_bytes();before=self.protected();other=self.new()
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(other,self.session)
        self.assertEqual(self.engine.inspect(self.q,None)['state'],'Unknown');self.assertEqual(marker,self.marker().read_bytes());self.assertEqual(before,self.protected())

    def test_released_orphan_reconstructs_abort_and_never_reserves_again(self):
        self.reserve();self.engine.discard(self.q,self.session);self.journal().unlink();marker=self.marker().read_bytes();before=self.protected()
        with patch.object(self.preflight,'plan_locked',side_effect=AssertionError('no resurrection')):self.assertEqual(self.reserve()['state'],'Aborted')
        self.assertEqual(marker,self.marker().read_bytes());self.assertEqual(before,self.protected())

    def test_conflicting_marker_is_never_overwritten_or_released(self):
        self.reserve();foreign=self.engine.marker(self.new(),'Reserved');self.marker().write_text(json.dumps(foreign));raw=self.marker().read_bytes();before=self.protected()
        for action in (self.engine.inspect,self.engine.reserve,self.engine.discard):
            r=action(self.q,None);self.assertEqual(r['state'],'Unknown');self.assertFalse(r['ownedFence']);self.assertEqual(self.marker().read_bytes(),raw)
        self.assertEqual(before,self.protected())

    def test_early_released_marker_cannot_erase_reserved_journal_protection(self):
        self.reserve();self.change(self.marker(),state='Released');raw=self.marker().read_bytes();self.assertEqual(self.engine.inspect(self.q,None)['state'],'Unknown')
        with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):self.engine.reserve(self.new(),self.session)
        self.assertEqual(self.marker().read_bytes(),raw)

    def test_wrong_owner_and_nonnew_id_cannot_change_metadata(self):
        self.reserve();before=snapshot(self.state)
        for action in (self.engine.reserve,self.engine.inspect,self.engine.discard):
            with self.assertRaisesRegex(Exception,'NOT_FOUND'):action({**self.q,'owner':'E'*64},self.session)
        self.assertEqual(before,snapshot(self.state))
        for op in (self.native.q['operationId'],self.native.q['plan']['command']['purge']['operationId'],self.engine.ref(self.q)['operationId']):
            with self.assertRaisesRegex(Exception,'NEW_OPERATION_REQUIRED'):self.engine.reserve({**self.q,'operationId':op},self.session)

    def test_forged_eligibility_and_missing_copy_do_not_reserve(self):
        bad=copy.deepcopy(self.q);bad['plan'].update(retentionElapsed=True,retirementEligible=True);bad['plan']['fingerprint']=self.preflight.digest(bad['plan']);before=self.protected()
        with self.assertRaisesRegex(Exception,'PLAN_CHANGED'):self.engine.reserve(bad,self.session)
        self.assertFalse(self.journal().exists());self.assertFalse(self.marker().exists());self.assertEqual(before,self.protected())
        self.native.base.leaf.rename(self.native.base.leaf.with_suffix('.held'));q={**self.q,'plan':self.engine.plan(self.f.command,self.session)};before=self.protected()
        with self.assertRaisesRegex(Exception,'COPY_UNAVAILABLE'):self.engine.reserve(q,self.session)
        self.assertFalse(self.marker().exists());self.assertEqual(before,self.protected())

    def test_missing_original_archive_claim_is_not_repaired(self):
        self.native.f.claim().unlink();before=self.protected()
        with self.assertRaisesRegex(Exception,'FENCE_CHANGED'):self.reserve()
        self.assertFalse(self.marker().exists());self.assertFalse(self.journal().exists());self.assertEqual(before,self.protected())

    def test_unsafe_marker_or_malformed_active_journal_never_gets_ignored(self):
        self.reserve();self.marker().chmod(0o644)
        with self.assertRaises(Exception):self.engine.reserve(self.new(),self.session)
        self.marker().chmod(0o600);self.engine.discard(self.q,self.session);self.change(self.journal(),state='Unrecognised');before=self.protected()
        with self.assertRaisesRegex(Exception,'RECEIPT_INVALID'):self.engine.reserve(self.new(),self.session)
        self.assertEqual(before,self.protected())

    def test_independent_state_and_claims_paths_are_required(self):
        for path in (self.native.engine.state_path,self.native.source.state_path,self.native.engine.restore.state_path,self.native.engine.preflight.purges.state_path):
            with self.assertRaises(ValueError):type(self.engine)(self.preflight,path)
        self.engine.claims_path.rmdir();self.engine.claims_path.symlink_to(self.native.engine.restore.claims_path,target_is_directory=True);before=self.protected()
        with self.assertRaisesRegex(Exception,'STATE_UNSAFE'):self.reserve()
        self.assertEqual(before,self.protected())

    def test_existing_source_lock_busy_never_creates_reservation_or_marker(self):
        ref=self.engine.ref(self.q);path=self.native.source.state_path/(ref['operationId']+'.lock');fd=os.open(path,os.O_RDONLY)
        try:
            fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB);before=self.protected()
            with self.assertRaisesRegex(Exception,'BUSY'):self.reserve()
            self.assertFalse(self.marker().exists());self.assertFalse(self.journal().exists());self.assertEqual(before,self.protected())
        finally:os.close(fd)

    def test_marker_write_failure_never_changes_archive_or_hdd(self):
        save=self.engine.f.save_receipt
        def fail_marker(fd,op,value):
            if set(value)==self.engine.MARKER:raise OSError('owned marker write failure')
            return save(fd,op,value)
        before=self.protected()
        with patch.object(self.engine.f,'save_receipt',side_effect=fail_marker),self.assertRaisesRegex(OSError,'owned marker write'):self.reserve()
        self.assertEqual(self.record()['state'],'Preparing');self.assertFalse(self.marker().exists());self.assertEqual(self.engine.inspect(self.q,None)['state'],'Aborted');self.assertEqual(before,self.protected())

    def test_marker_directory_fsync_failure_keeps_unknown_protection(self):
        fsync=os.fsync;before=self.protected()
        def fail_directory(fd):
            if os.readlink('/proc/self/fd/'+str(fd))==str(self.engine.claims_path):raise OSError('owned marker directory fsync failure')
            return fsync(fd)
        with patch.object(os,'fsync',side_effect=fail_directory),self.assertRaisesRegex(OSError,'owned marker directory fsync'):self.reserve()
        self.assertEqual(self.record()['state'],'Preparing');self.assertTrue(self.marker().exists());r=self.engine.inspect(self.q,None);self.assertEqual(r['state'],'Unknown');self.assertTrue(r['sourceFenced'] and r['ownedFence']);self.assertEqual(before,self.protected())

    def test_independent_real_record_join_checks_steps_and_rejects_marker_tampering(self):
        from acl_history_browser_proof import typed_equal
        from recursive_acl_restore_agent_io import snapshot as root_snapshot
        e=self.state.parent;steps=[];a=self.q;b=self.new();before=self.protected()
        def capture(q,action,session):
            r=getattr(self.engine,action)(q,session);steps.append(dict(operationId=q['operationId'],action=action,receipt=r,journal=json.loads(self.journal(q).read_bytes()),marker=json.loads(self.marker().read_bytes()),roots=root_snapshot(self.state)))
        capture(a,'reserve',self.session)
        for action in (lambda:self.engine.reserve(b,self.session),lambda:self.engine.plan(self.f.command,self.session)):
            with self.assertRaisesRegex(Exception,'SOURCE_BUSY'):action()
        capture(a,'discard',self.session);capture(b,'reserve',self.session)
        for action in ('reserve','inspect','discard'):capture(a,action,None)
        capture(b,'reserve',None);capture(b,'discard',self.session)
        value=dict(requests=[a,b],steps=steps,before=before,after=self.protected(),roots=root_snapshot(self.state),sourceBusyRefusals=2,originalArchiveClaimUnchanged=True,schedulingBeforeExpiryOnly=True,retirementApplied=False)
        payload=e/'retirement-reservations.json';payload.write_text(json.dumps(value));(e/'retention-assessment-live.json').write_text(json.dumps(dict(plan=a['plan'])))
        source=Path(__file__).with_name('verify_container_recycle_archive_copies.py');node=next(n for n in ast.parse(source.read_bytes()).body if isinstance(n,ast.FunctionDef) and n.name=='retirement_reservations_proof')
        scope=dict(json=json,re=__import__('re'),hashlib=hashlib,stat=stat,snapshot=root_snapshot,typed_equal=typed_equal);exec(compile(ast.Module(body=[node],type_ignores=[]),str(source),'exec'),scope)
        copied=dict(request=self.native.q,canceledRequest=dict(operationId=uuid.uuid4().hex));scope['retirement_reservations_proof'](e,copied)
        value['steps'][3]['marker']['retirementOperationId']=a['operationId'];payload.write_text(json.dumps(value))
        with self.assertRaises(AssertionError):scope['retirement_reservations_proof'](e,copied)
