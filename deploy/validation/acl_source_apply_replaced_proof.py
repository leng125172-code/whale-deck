"""Original confirmed execution must abort against a valid replacement source."""
from acl_source_confirm_replaced_proof import SEEDS as PREVIOUS,verify_changed_snapshot
from acl_source_apply_missing_proof import source_closure,CODE,require_pending,require_failed,summary,verify_audits,worker_unchanged_except_start

KIND='source-apply-replaced'
SEEDS=(*PREVIOUS,'acl_source_apply_replaced_proof.py','test_acl_source_apply_replaced_proof.py',
       'validate_container_acl_source_apply_replaced.py','validate_container_acl_source_apply_replaced.mjs',
       'verify_container_acl_source_apply_replaced.py')


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def require_replacement_observed(source,proof,observed,identifier):
    assert observed['id']==identifier and observed['sourceStatus']==409 and observed['sourceCode']=='AGENT_VERSION_CONFLICT'
    assert observed['replacementReadStatus']==200 and observed['byKeyStatus']==200
    assert observed['pending']==observed['byKey']['intent'] and observed['byKey']['source']==source
    verify_changed_snapshot(source,proof,observed['replacementSnapshot'])
