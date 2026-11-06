"""Constructed fault/lease/baseline proofs only; no real service changes."""
import copy,hashlib,unittest
from datetime import datetime,timedelta,timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_purge_restart_proof as proof
import validate_container_recycle_purge_restart as gate
from test_container_recycle_purge_inflight_logout import sample as logout_sample
from test_container_recycle_purge_browser_proof import summary


def sample():
    v,b,f=logout_sample();v['row'].update(state='Failed',result_code='CONTAINER_RECYCLE_PURGE_INTERRUPTED');v['executionJob'].update(state='Failed',result_json=summary(v['row']))
    f['kind']='worker-crash';b.update(kind=f['kind'],originalOwnerStillAdministrator=True,ownerLogoutPostsBeforeRestartOutcome=0,logout=b['logoutReceipts'][0])
    return v,b,f


class PurgeRestartTests(unittest.TestCase):
    def row(self):
        r={k:k for k in proof.IMMUTABLE};r.update(state='Applying',publish_barrier=True,completed_at_utc=None,cancel_requested_at_utc=None,lease_token='original-token',lease_expires_at_utc='2026-11-06T10:01:00+00:00')
        return r

    def data(self):return dict(row=self.row(),executionJob=dict(state='Running',cancel_requested_at_utc=None))

    def test_applying_sql_retains_original_reservation_apply_markers_and_lease(self):
        old=self.row();proof.lease_held(old,self.data())
        for field,bad in [('source_proof_hash','forged'),('reservation_json',None),('reservation_hash',None),('apply_started_at_utc',None),('lease_token','new'),('lease_expires_at_utc','later'),('state','Reserving'),('publish_barrier',False)]:
            data=self.data();data['row'][field]=bad
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.lease_held(old,data)

    def test_empty_transient_receipts_do_not_block_but_other_live_units_do(self):
        helper='whaledeck-container-recycle-purges-'+'a'*32+'.service';owner='whaledeck-qa-purge-restart-'+'b'*12+'.service'
        proof.require_transients({'old':dict(MainPID='0',ControlPID='0',processes=[])},helper,owner)
        for x in (dict(MainPID='4',ControlPID='0',processes=[]),dict(MainPID='0',ControlPID='4',processes=[]),dict(MainPID='0',ControlPID='0',processes=['4'])):
            with self.assertRaises(AssertionError):proof.require_transients({'other':x},helper,owner)
        with self.assertRaises(AssertionError):proof.require_transients({},'other.service',owner)

    def test_other_work_in_every_file_table_blocks_before_service_commands(self):
        self.assertIn('container_recycle_purge_intents',proof.TABLES)
        for table in proof.TABLES:
            counts=dict.fromkeys(proof.TABLES,0);counts[table]=1
            with self.subTest(table=table),patch.object(proof,'preflight',return_value={}),patch.object(proof,'query',return_value=counts),patch.object(proof,'command') as cmd,self.assertRaisesRegex(AssertionError,'OTHER_FILE_WORK'):
                proof.restart_allowed(SimpleNamespace(),dict(id='intent',execution_job_id='execution'))
            cmd.assert_not_called()

    def baseline(self,kind):
        before=dict(core={'/whaledeck-worker':['worker','image','running','old'],'/other':['other','image','running','same']},hosts={'whaledeck-agent.service':dict(pid=10,sourceRevision='a'*40),'whaledeck-maintenance.service':dict(pid=11,sourceRevision='a'*40)},health={'/whaledeck-worker':'healthy'},protected={'original':'bytes'},recycleRoot={},recycleHdd={},recycleRestoreRoot={},purgeRoot={})
        after=copy.deepcopy(before);after['recycleRoot']['a'*32+'.purge.json']={};after['purgeRoot']['c'*32+'.json']={}
        if kind=='worker-crash':after['core']['/whaledeck-worker'][3]='new'
        else:after['hosts']['whaledeck-agent.service']['pid']=12
        service=dict(kind=kind,otherActiveWork=dict(transientScopeVerified=True,checks=dict(otherJobs=0),counts=dict.fromkeys(proof.TABLES,0)),hostsBefore=before['hosts'],hostsAfter=copy.deepcopy(after['hosts']),sameWorkerContainerAndImage=True,workerId='worker',imageId='image',signal='SIGKILL',beforeStartedAt='old',afterStartedAt='new')
        return before,after,service

    def test_worker_fault_allows_only_same_container_image_with_new_start_time(self):
        a,b,s=self.baseline('worker-crash');proof.preserved_restart(a,b,'a'*32,'b'*32,'c'*32,s,'worker-crash')
        for field,bad in [('core',{'/other':['other','image','running','new']}),('protected',{'original':'changed'}),('purgeRoot',{'d'*32+'.json':{}})]:
            wrong=copy.deepcopy(b);wrong[field].update(bad)
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.preserved_restart(a,wrong,'a'*32,'b'*32,'c'*32,s,'worker-crash')
        b['core']['/whaledeck-worker'][0]='recreated'
        with self.assertRaises(AssertionError):proof.preserved_restart(a,b,'a'*32,'b'*32,'c'*32,s,'worker-crash')

    def test_agent_fault_cannot_hide_maintenance_restart_or_other_container_change(self):
        a,b,s=self.baseline('agent-restart');proof.preserved_restart(a,b,'a'*32,'b'*32,'c'*32,s,'agent-restart')
        for target in ('maintenance','container'):
            wrong=copy.deepcopy(b);service=copy.deepcopy(s)
            if target=='maintenance':wrong['hosts']['whaledeck-maintenance.service']['pid']=99;service['hostsAfter']=wrong['hosts']
            else:wrong['core']['/other'][3]='restarted'
            with self.subTest(target=target),self.assertRaises(AssertionError):proof.preserved_restart(a,wrong,'a'*32,'b'*32,'c'*32,service,'agent-restart')

    def lease(self):
        start=datetime(2026,11,6,10,tzinfo=timezone.utc);expiry=start+timedelta(seconds=60);raw=b'original-reserved-record';row=self.row();row['lease_expires_at_utc']=expiry.isoformat();final={**row,'completed_at_utc':(expiry+timedelta(seconds=1)).isoformat()}
        samples=[dict(atUtc=(start+timedelta(seconds=n)).isoformat(),state='Applying',barrier=True,leaseToken=row['lease_token'],leaseExpiresAtUtc=row['lease_expires_at_utc'],sourceUnchanged=True,rootSha256=hashlib.sha256(raw).hexdigest()) for n in range(0,60,2)]
        value=dict(waitBeganAtUtc=start.isoformat(),waitEndedAtUtc=expiry.isoformat(),monotonicSeconds=60,originalLeaseToken=row['lease_token'],originalLeaseExpiresAtUtc=row['lease_expires_at_utc'],clocksOrSqlModified=False,samples=samples)
        return row,final,value,raw

    def test_natural_lease_requires_real_duration_original_token_and_dense_samples(self):
        r,f,v,raw=self.lease();proof.verify_lease(r,f,v,raw)
        for key,bad in [('monotonicSeconds',1),('clocksOrSqlModified',True),('originalLeaseToken','new'),('samples',v['samples'][::5])]:
            wrong=copy.deepcopy(v);wrong[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.verify_lease(r,f,wrong,raw)
        with self.assertRaises(AssertionError):proof.verify_lease(r,{**f,'completed_at_utc':v['waitBeganAtUtc']},v,raw)

    def originals(self):
        r=self.row();r['execution_job_id']='a'*32;r['owner_hash']='owner';p=dict(fingerprint='fp',command=dict(source={'operationId':'b'*32}))
        root=dict(operationId='a'*32,owner='owner',plan=p,state='Reserved',pendingIdentity=None,unlinkConfirmed=False);marker=dict(operationId='a'*32,owner='owner',fingerprint='fp',source=p['command']['source'],state='Reserved')
        return r,root,marker,dict(plan=p)

    def test_prelease_record_can_be_reserved_or_aborted_but_never_delete_phase(self):
        r,root,m,cut=self.originals();proof.prelease_originals(r,root,m,cut)
        for key,bad in [('state','Applying'),('state','Unknown'),('unlinkConfirmed',True),('pendingIdentity',[1]*9),('operationId','c'*32)]:
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.prelease_originals(r,{**root,key:bad},m,cut)
        for key,bad in [('state','Deleting'),('fingerprint','changed'),('operationId','c'*32)]:
            with self.subTest(marker=key),self.assertRaises(AssertionError):proof.prelease_originals(r,root,{**m,key:bad},cut)

    def test_restart_terminal_is_failed_interrupted_not_user_canceled(self):
        v,b,f=sample();self.assertTrue(gate.terminal_proof(v,b,f)['originalExecutionInterrupted'])
        for state,code in [('Canceled','CONTAINER_RECYCLE_PURGE_CANCELED'),('Unknown','CONTAINER_RECYCLE_PURGE_RESULT_UNKNOWN')]:
            wrong=copy.deepcopy(v);wrong['row'].update(state=state,result_code=code);wrong['executionJob']['state']=state
            with self.subTest(state=state),self.assertRaises(AssertionError):gate.terminal_proof(wrong,b,f)

    def test_owner_remains_administrator_until_fault_outcome_without_logout_or_cancel(self):
        _,b,f=sample();gate.browser_proof(b,f)
        for key,bad in [('originalOwnerStillAdministrator',False),('ownerLogoutPostsBeforeRestartOutcome',1),('cancellations',1),('subject','other')]:
            with self.subTest(key=key),self.assertRaises(AssertionError):gate.browser_proof({**b,key:bad},f)

    def test_missed_cut_or_unsupported_fault_cannot_authorize_service_mutation(self):
        for kind in ('logout','reboot','maintenance-restart'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()
        code=Path(gate.__file__).read_text();self.assertLess(code.index('EXACT_PURGE_RESTART_CUT_MISSED_NO_REPLAY'),code.index('restart(kind,held,worker,revision,e)'));self.assertIn('worker.start()',code)
        self.assertLess(code.index('independent(repo,e,True)'),code.index('leaf.unlink()'))


if __name__=='__main__':unittest.main()
