import copy
from datetime import datetime, timedelta, timezone
import hashlib
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import container_recycle_restart_proof as proof
import validate_container_recycle_restart as gate


class RecycleRestartTests(unittest.TestCase):
    def row(self):
        row = {k: k for k in proof.IMMUTABLE}
        row.update(state='Applying', publish_barrier=True, completed_at_utc=None, cancel_requested_at_utc=None, lease_token='original-token', lease_expires_at_utc='2026-11-05T10:01:00+00:00')
        return row

    def data(self):
        return dict(row=self.row(), executionJob=dict(state='Running', cancel_requested_at_utc=None))

    def test_lease_fence_keeps_original_binding_token_and_running_job(self):
        original = self.row(); data = self.data(); proof.lease_held(original, data)
        for field, bad in [('execution_job_id', 'another'), ('login_binding', 'new-login'), ('lease_token', 'reclaimed'), ('lease_expires_at_utc', 'later'), ('publish_barrier', False), ('state', 'Failed')]:
            altered = copy.deepcopy(data); altered['row'][field] = bad
            with self.subTest(field=field), self.assertRaises(AssertionError): proof.lease_held(original, altered)
        data['executionJob']['state'] = 'Failed'
        with self.assertRaises(AssertionError): proof.lease_held(original, data)

    def test_transient_guard_accepts_empty_receipts_but_rejects_other_processes(self):
        helper = 'whaledeck-container-recycling-' + 'a'*32 + '.service'; owner = 'whaledeck-qa-recycle-restart-' + 'b'*12 + '.service'
        entries = {'old-exited.service': dict(MainPID='0', ControlPID='0', processes=[])}
        proof.require_transients(entries, helper, owner)
        for p in [dict(MainPID='8', ControlPID='0', processes=[]), dict(MainPID='0', ControlPID='0', processes=['8']), dict(MainPID='0', ControlPID='8', processes=[])]:
            with self.assertRaises(AssertionError): proof.require_transients({'other.service': p}, helper, owner)
        with self.assertRaises(AssertionError): proof.require_transients({}, 'unrelated.service', owner)

    def test_other_file_work_blocks_fault_before_any_service_command(self):
        for table in proof.TABLES:
            counts = dict.fromkeys(proof.TABLES, 0); counts[table] = 1
            with self.subTest(table=table), patch.object(proof, 'preflight', return_value={}), patch.object(proof, 'query', return_value=counts), patch.object(proof, 'command') as command, self.assertRaisesRegex(AssertionError, 'OTHER_FILE_WORK'):
                proof.restart_allowed(SimpleNamespace(), dict(id='intent', execution_job_id='execution'))
            command.assert_not_called()

    def baseline(self, kind):
        before = dict(core={'/whaledeck-worker': ['worker', 'image', 'running', 'old'], '/unrelated': ['other', 'image', 'running', 'same']}, health={'/whaledeck-worker':'healthy'}, hosts={'whaledeck-agent.service':dict(pid=10, sourceRevision='a'*40), 'whaledeck-maintenance.service':dict(pid=11, sourceRevision='a'*40)}, protected={'original':'bytes'}, recycleRoot={}, recycleHdd={})
        after = copy.deepcopy(before)
        if kind == 'worker-crash': after['core']['/whaledeck-worker'][3] = 'new'
        else: after['hosts']['whaledeck-agent.service']['pid'] = 12
        service = dict(kind=kind, otherActiveWork=dict(transientScopeVerified=True, checks=dict(otherJobs=0), counts=dict.fromkeys(proof.TABLES, 0)), hostsBefore=before['hosts'], hostsAfter=copy.deepcopy(after['hosts']), sameWorkerContainerAndImage=True, workerId='worker', imageId='image', signal='SIGKILL', beforeStartedAt='old', afterStartedAt='new')
        return before, after, service

    def test_baseline_allows_only_original_worker_start_time_change(self):
        before, after, service = self.baseline('worker-crash'); proof.preserved_restart(before, after, [], service, 'worker-crash')
        for key, value in [('core', {'/unrelated': ['changed','image','running','same']}), ('protected', {'original':'changed'})]:
            bad = copy.deepcopy(after); bad[key].update(value)
            with self.subTest(key=key), self.assertRaises(AssertionError): proof.preserved_restart(before, bad, [], service, 'worker-crash')
        after['core']['/whaledeck-worker'][0] = 'recreated'
        with self.assertRaises(AssertionError): proof.preserved_restart(before, after, [], service, 'worker-crash')

    def test_agent_restart_cannot_hide_maintenance_or_container_changes(self):
        before, after, service = self.baseline('agent-restart'); proof.preserved_restart(before, after, [], service, 'agent-restart')
        for target in ('maintenance', 'container'):
            bad = copy.deepcopy(after); svc = copy.deepcopy(service)
            if target == 'maintenance': bad['hosts']['whaledeck-maintenance.service']['pid'] = 99; svc['hostsAfter'] = bad['hosts']
            else: bad['core']['/unrelated'][3] = 'restarted'
            with self.subTest(target=target), self.assertRaises(AssertionError): proof.preserved_restart(before, bad, [], svc, 'agent-restart')

    def lease(self):
        start = datetime(2026,11,5,10,tzinfo=timezone.utc); expiry = start + timedelta(seconds=60); raw=b'original-root'
        row = self.row(); row['lease_expires_at_utc'] = expiry.isoformat()
        final = {**row, 'completed_at_utc': (expiry+timedelta(seconds=1)).isoformat()}
        samples=[dict(atUtc=(start+timedelta(seconds=n)).isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],sourceUnchanged=True,rootSha256=hashlib.sha256(raw).hexdigest()) for n in range(0,60,2)]
        value=dict(waitBeganAtUtc=start.isoformat(),waitEndedAtUtc=expiry.isoformat(),monotonicSeconds=60,originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],clocksOrSqlModified=False,samples=samples)
        return row, final, value, raw

    def test_natural_lease_requires_real_duration_and_sampled_original_fence(self):
        row, final, value, raw = self.lease(); proof.verify_lease(row, final, value, raw)
        for field, bad in [('monotonicSeconds', 1), ('clocksOrSqlModified', True), ('originalLeaseToken','new'), ('samples',value['samples'][::5])]:
            altered=copy.deepcopy(value);altered[field]=bad
            with self.subTest(field=field), self.assertRaises(AssertionError): proof.verify_lease(row, final, altered, raw)
        with self.assertRaises(AssertionError): proof.verify_lease(row, {**final,'completed_at_utc':value['waitBeganAtUtc']}, value, raw)

    def test_canceled_outcome_is_not_a_restart_failure_proof(self):
        f=dict(subject='s',containerId='c',instanceId='i'); row=dict(actor_subject='s',container_id='c',instance_id='i',relative_path='work/recycle-rpc.bin',state='Canceled')
        with patch.object(gate,'sql',return_value=dict(row=row,executionJob=dict(state='Canceled'))),self.assertRaises(AssertionError):gate.prove(dict(id='id',state='Canceled'),f,Path('/unused'))

    def test_restart_evidence_requires_unchanged_owner_authority_and_normal_logout(self):
        f=dict(kind='worker-crash',subject='s');r=dict(normalUiLogout=True,postStatus=302,providerNavigationObserved=True,bffStatus=401,identityStatus=403,finalLoginPage=True)
        b=dict(kind=f['kind'],subject='s',actualUi=True,registrations=1,confirmations=1,cancellations=0,originalOwnerStillAdministrator=True,logout=r)
        gate.browser_proof(b,f)
        for key,bad in [('cancellations',1),('originalOwnerStillAdministrator',False),('subject','different'),('actualUi',False)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:bad},f)
        with self.assertRaises(AssertionError):gate.browser_proof({**b,'logout':{**r,'identityStatus':200}},f)

    def test_unsupported_fault_and_missed_cut_cannot_mutate_services(self):
        for kind in ('logout','reboot','maintenance-restart'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()
        code=Path(gate.__file__).read_text();self.assertLess(code.index("assert held is not None,'EXACT_CUT_MISSED_NO_REPLAY'"),code.index('restart(kind,held,worker,revision,e)'))
        self.assertIn('worker.start()',code);self.assertIn("f['retain_unsettled']=True",code);self.assertLess(code.index("e/'independent-live.log'"),code.index('leaf.unlink()'))


if __name__=='__main__':unittest.main()
