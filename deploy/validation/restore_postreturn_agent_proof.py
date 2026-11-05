"""Fixed post-return Agent restart and maintenance sampling proof."""
import copy,json
from container_recycle_restore_restart_proof import restart,same_operation,lease_held,TABLES
from validate_container_recycle_restore_postreturn_cancel import preserved
from validate_container_acl_faults import require_host_restart
from acl_history_browser_proof import typed_equal
KINDS=('agent-restart',)


def preserved_restart(before, after, source_op, restore_op, service, kind):
    assert kind=='agent-restart' and service['kind'] == kind and service['otherActiveWork']['transientScopeVerified']
    assert all(type(v) is int and v == 0 for k in ('checks', 'counts') for v in service['otherActiveWork'][k].values())
    assert set(service['otherActiveWork']['counts']) == set(TABLES)
    expected = copy.deepcopy(before)
    typed_equal(service['hostsBefore'], before['hosts']); typed_equal(service['hostsAfter'], after['hosts'])
    if kind == 'worker-crash':
        a, b = before['core']['/whaledeck-worker'], after['core']['/whaledeck-worker']
        assert a[:3] == b[:3] and a[2] == 'running' and a[3] == service['beforeStartedAt'] and b[3] == service['afterStartedAt'] and a[3] != b[3]
        assert service['sameWorkerContainerAndImage'] and service['workerId'] == a[0] and service['imageId'] == a[1] and service['signal'] == 'SIGKILL'
        expected['core']['/whaledeck-worker'] = b
    else:
        revision = before['hosts']['whaledeck-agent.service']['sourceRevision']; require_host_restart(before['hosts'], after['hosts'], revision)
        expected['hosts'] = after['hosts']
    preserved(expected, after, source_op, restore_op)


def maintenance_proof(service,samples):
    began=service['restartBeganAtUtc'];ended=service['restartEndedAtUtc'];times=[v['atUtc'] for v in samples]
    assert len(samples)==service['maintenanceSamples']>=3 and times==sorted(times) and times[0]<=began<ended<=times[-1] and any(began<t<ended for t in times)
    assert all(v['httpStatus']==200 and v['state']=='healthy' and v['phase']=='Ready' and v['noStore'] for v in samples)


def verify_restart(e,fixture,final,before,after):
    read=lambda n:json.loads((e/n).read_text());service=read('service-restart.json');row=read('restart-start-sql.json')['row'];same_operation(row,final);lease_held(row,read('restart-start-sql.json'))
    source=read('source-proof.json');preserved_restart(before,after,source['request']['operationId'],final['execution_job_id'].replace('-',''),service,fixture['kind'])
    exited=read('helper-exit.json');assert exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeServiceFault'] and 0<exited['elapsedSeconds']<20
    maintenance_proof(service,read('maintenance-observations.json'))
