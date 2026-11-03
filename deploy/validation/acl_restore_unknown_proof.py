"""Restore-specific Unknown source, provenance and explicit-ack audit facts."""
from acl_restore_fault_proof import RESTORE_SOURCES, require_restore_profile
from acl_restore_browser_proof import expected_metadata, unique_json
from acl_history_browser_proof import typed_equal

def restore_unknown_sources():
    from validate_container_acl_unknown import SOURCES
    return tuple(dict.fromkeys((*SOURCES,*RESTORE_SOURCES,'acl_restore_unknown_proof.py',
        'validate_container_acl_restore_unknown.py','verify_container_acl_restore_unknown.py','test_acl_restore_unknown_proof.py','test_acl_unknown_proof.py')))

def require_restore_unknown(baseline):
    require_restore_profile(baseline)
    assert baseline['aclRestoreFaultVersion']==7 and baseline['kind']=='post-acl-target-removed'
    assert baseline['postAcl'] is True and baseline.get('instanceChanged',False) is False

def verify_restore_unknown_audits(rows,row,baseline):
    require_restore_unknown(baseline)
    assert row['state'] in ('Unknown','Acknowledged')
    expected={'plan-accepted':('Accepted',row['id'],row['actor_subject']),
        'plan-result':('Ready',row['id'],row['actor_subject']),
        'confirmed':('Accepted',row['execution_job_id'],row['actor_subject']),
        'result':('Unknown',row['execution_job_id'],row['actor_subject'])}
    if row['state']=='Acknowledged':
        assert row['acknowledged_by'] and row['acknowledged_by']!=row['actor_subject']
        expected['unknown-acknowledged']=('Unknown',row['execution_job_id'],row['acknowledged_by'])
    assert type(rows) is list and len(rows)==len(expected)
    seen=set()
    for audit in rows:
        assert audit['target_type']=='container' and audit['target_id']==row['container_id']
        assert audit['action'].startswith('containers.files.acl.restore.')
        action=audit['action'].removeprefix('containers.files.acl.restore.')
        assert action in expected and action not in seen;seen.add(action)
        assert (audit['result'],audit['job_id'],audit['actor_subject'])==expected[action]
        assert type(audit['trace_id']) is str and 0<len(audit['trace_id'])<=64
        typed_equal(unique_json(audit['detail_json']),expected_metadata(row))
    assert seen==expected.keys()
