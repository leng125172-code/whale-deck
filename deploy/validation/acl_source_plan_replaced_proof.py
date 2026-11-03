"""A queued restore retains its original source when a valid new capture is published."""
from acl_source_register_replaced_proof import SEEDS as PREVIOUS,attempt_identifier
from validate_container_acl_browser import version
from acl_source_apply_missing_proof import source_closure
from acl_source_plan_missing_proof import CODE,require_queued,require_failed,summary,verify_audits,worker_unchanged_except_start
from acl_source_confirm_replaced_proof import verify_changed_snapshot

KIND='source-plan-replaced'
SEEDS=(*PREVIOUS,'acl_source_plan_replaced_proof.py','test_acl_source_plan_replaced_proof.py',
       'validate_container_acl_source_plan_replaced.py','validate_container_acl_source_plan_replaced.mjs',
       'verify_container_acl_source_plan_replaced.py')


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def require_replacement_observed(source,proof,observed,identifier):
    assert observed['id']==identifier and observed['sourceStatus']==409 and observed['sourceCode']=='AGENT_VERSION_CONFLICT'
    assert observed['replacementReadStatus']==200 and observed['byKeyStatus']==200
    assert observed['queued']==observed['byKey']['intent'] and observed['queued']['state']=='Queued'
    assert observed['queued']['executionJobId'] is None and observed['queued']['confirmation'] is None
    assert observed['byKey']['source']==source
    verify_changed_snapshot(source,proof,observed['replacementSnapshot'])


def require_registration_attempt(baseline,capture_row,attempt,identifier):
    assert attempt_identifier(baseline,capture_row,attempt)==identifier
    body=attempt['body'];source=baseline['restoreSource']
    assert set(body)=={'instanceId','path','captureId','expectedHistoryDigest','expectedVersion'}
    assert body['instanceId']==baseline['instanceId'] and body['path']=='work/acl-file.bin'
    assert body['captureId']==source['entry']['captureId'] and body['expectedHistoryDigest']==source['entry']['historyDigest']
    assert body['expectedVersion']==version(baseline,'work/acl-file.bin',baseline['original']['acl-file.bin']['identity'])


def require_queued_attempt(data,attempt):
    from acl_restore_browser_proof import unique_json
    assert unique_json(data['row']['command_json'])['ExpectedVersion']==attempt['body']['expectedVersion']
