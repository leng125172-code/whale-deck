"""Agent restart must preserve maintenance, containers and the successful source claim."""
import copy,unittest
from unittest.mock import patch
import restore_postreturn_agent_proof as proof
import validate_container_recycle_restore_postreturn_agent as gate
import test_restore_restart_proof as prior


class PostreturnAgentProofTests(unittest.TestCase):
    def test_only_fixed_agent_fault_is_exposed_before_runtime_access(self):
        for kind in ('worker-crash','maintenance-restart','reboot'):
            with self.subTest(kind=kind),patch.object(gate.os,'geteuid') as uid,self.assertRaises(AssertionError):gate.main('a'*40,kind)
            uid.assert_not_called()

    def test_restart_cannot_hide_worker_or_maintenance_change(self):
        before,after,service=prior.RestoreRestartTests().baseline('agent-restart');after['recycleRestoreRoot']['claims/'+('a'*32)+'.json']={'this':'claim'}
        proof.preserved_restart(before,after,'a'*32,'b'*32,service,'agent-restart')
        for kind in ('worker','maintenance'):
            bad=copy.deepcopy(after);svc=copy.deepcopy(service)
            if kind=='worker':bad['core']['/whaledeck-worker'][3]='restarted'
            else:bad['hosts']['whaledeck-maintenance.service']['pid']=999;svc['hostsAfter']=bad['hosts']
            with self.subTest(kind=kind),self.assertRaises(AssertionError):proof.preserved_restart(before,bad,'a'*32,'b'*32,svc,'agent-restart')

    def test_sampling_requires_before_during_after_and_exact_healthy_facts(self):
        service=dict(restartBeganAtUtc='2026-11-06T10:00:01+00:00',restartEndedAtUtc='2026-11-06T10:00:03+00:00',maintenanceSamples=3)
        samples=[dict(atUtc='2026-11-06T10:00:0'+str(n)+'+00:00',httpStatus=200,state='healthy',phase='Ready',noStore=True) for n in (0,2,4)]
        proof.maintenance_proof(service,samples)
        for key,value in [('httpStatus',503),('phase','Updating'),('noStore',False)]:
            bad=copy.deepcopy(samples);bad[1][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.maintenance_proof(service,bad)
        with self.assertRaises(AssertionError):proof.maintenance_proof(service,samples[::-1])

    def test_late_cancel_is_not_pure_agent_recovery(self):
        from pathlib import Path
        with patch.object(gate,'prove_result',return_value=dict(sql=dict(row=dict(state='Succeeded',cancel_requested_at_utc='explicit',result_code='CONTAINER_RECYCLE_RESTORE_COMPLETED')))),self.assertRaises(AssertionError):gate.prove({}, {}, {}, Path('/unused'))


if __name__=='__main__':unittest.main()
