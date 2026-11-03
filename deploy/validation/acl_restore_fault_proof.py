"""Selected historical source and current-preimage proof for restore fault cases."""
import json
from pathlib import Path
import uuid

from validate_container_acl_restore_browser import SOURCES as RESTORE_BROWSER_SOURCES, selected_source, audit_rows
from validate_container_acl_history_browser import immutable
from acl_restore_browser_proof import source_json, unique_json, expected_metadata
from acl_history_browser_proof import typed_equal

RESTORE_SOURCES=tuple(dict.fromkeys((*RESTORE_BROWSER_SOURCES,'acl_restore_fault_proof.py',
    'acl_restore_fault_ui.mjs','validate_container_acl_restore_faults.py',
    'verify_container_acl_restore_faults.py','test_acl_restore_fault_proof.py')))


RESTORE_FAULT_KINDS={1:frozenset(('cancel','logout')),2:frozenset(('group-removal','account-disable')),3:frozenset(('worker-crash','agent-restart')),4:frozenset(('post-acl-cancel','post-acl-worker-crash'))}

def restore_fault_version(kinds):
    assert kinds and len(kinds)==len(set(kinds)), 'RESTORE_FAULT_KINDS_INVALID'
    matches=[version for version,allowed in RESTORE_FAULT_KINDS.items() if set(kinds)<=allowed]
    assert len(matches)==1, 'RESTORE_FAULT_PROFILE_MISMATCH'
    return matches[0]

def require_restore_profile(baseline):
    value=baseline['aclRestoreFaultVersion']
    assert type(value) is int and value==restore_fault_version((baseline['kind'],))

def accept_source_capture(browser,directory,baseline,folder,reader,module,storage,content):
    from validate_container_acl_faults import wait_signal,write_signal
    from validate_container_acl_browser import inventory,verify_case
    require_restore_profile(baseline)
    (directory/'source-capture-baseline.json').write_text(json.dumps(baseline))
    captured=wait_signal(directory,'source-captured.json',browser,120)
    assert captured['result']['name']=='access'
    after=inventory(folder,reader,module,content)
    evidence=directory/'capture';evidence.mkdir(mode=0o700)
    proof=verify_case(captured['result'],baseline,captured['subject'],evidence,reader,module,storage,baseline['original'],after)
    (directory/'source-capture-proof.json').write_text(json.dumps(proof))
    metadata=immutable(evidence,baseline,[proof],storage)
    source=selected_source(baseline,proof,metadata)
    assert source['entry']['changedAclType']=='access' and source['accessAcl']!=after['acl-file.bin']['access']
    baseline['restoreSource']=source
    (directory/'baseline.json').write_text(json.dumps({**baseline,'original':after}))
    write_signal(directory,'source-verified.json',dict(source=source,physicalAndOriginalHistoryVerified=True))
    return after


def prove_binding(row,baseline,source,proof):
    require_restore_profile(baseline)
    item=proof['result'];loc=source['location'];entry=source['entry']
    assert loc==dict(containerId=row['container_id'],instanceId=row['instance_id'],path=row['relative_path'])
    assert loc['path']=='work/acl-file.bin' and entry['changedAclType']=='access'
    assert entry['captureId']==uuid.UUID(item['executionJobId']).hex
    assert row['id']!=item['id'] and row['execution_job_id']!=item['executionJobId']
    assert type(row['restore_source_json']) is str
    typed_equal(unique_json(row['restore_source_json']),source_json(source))
    plan=json.loads(row['plan_json']);command=plan['Command']
    assert command['AclType']=='access' and plan['NoChange'] is False
    typed_equal(command['Entries'],[dict(Tag=e['tag'],Id=e['id'],Permissions=e['permissions']) for e in source['accessAcl']])
    assert proof['after']==baseline['original'] and proof['before']!=proof['after']
    assert source['accessAcl']==proof['before']['acl-file.bin']['access']
    assert source['accessAcl']!=baseline['original']['acl-file.bin']['access']
    assert row['history_digest']!=entry['historyDigest']
    return source['accessAcl']


def verify_restore_binding(row,baseline,storage,module):
    from validate_container_acl_browser import verify_case
    directory=Path(baseline['evidence']);proof=json.loads((directory/'source-capture-proof.json').read_bytes())
    original=json.loads((directory/'source-capture-baseline.json').read_bytes())
    for key in ('containerId','instanceId','source','validationSourceRevision','kind','sha256','bytesPerFile'):
        assert original[key]==baseline[key]
    assert original['original']==proof['before']
    observed=directory/'capture-independent-observed';observed.mkdir(mode=0o700,exist_ok=True)
    actual=verify_case(proof['result'],original,row['actor_subject'],observed,
        None,module,storage,proof['before'],proof['after'])
    typed_equal(actual,proof)
    metadata=immutable(directory/'capture',original,[proof],storage)
    source=selected_source(original,proof,metadata)
    typed_equal(source,baseline['restoreSource'])
    signal=json.loads((directory/'source-captured.json').read_bytes());assert signal['subject']==row['actor_subject'] and signal['result']==proof['result']
    typed_equal(json.loads((directory/'source-verified.json').read_bytes()),dict(source=source,physicalAndOriginalHistoryVerified=True))
    desired=prove_binding(row,baseline,source,proof)
    verify_restore_audits(audit_rows(row['id']),row,baseline)
    return desired


def verify_restore_audits(rows,row,baseline):
    require_restore_profile(baseline)
    expected={'plan-accepted':('Accepted',row['id']),'plan-result':('Ready',row['id']),
        'confirmed':('Accepted',row['execution_job_id']),'result':('Succeeded' if baseline['aclRestoreFaultVersion']==4 else 'Failed' if baseline['aclRestoreFaultVersion']==3 else 'Canceled',row['execution_job_id'])}
    if baseline['kind'].removeprefix('post-acl-')=='cancel':expected['cancel-requested']=('Accepted',row['execution_job_id'])
    assert type(rows) is list and len(rows)==len(expected)
    seen=set()
    for audit in rows:
        assert audit['actor_subject']==row['actor_subject'] and audit['target_type']=='container' and audit['target_id']==row['container_id']
        assert audit['action'].startswith('containers.files.acl.restore.')
        assert type(audit['trace_id']) is str and 0<len(audit['trace_id'])<=64
        action=audit['action'].removeprefix('containers.files.acl.restore.')
        assert action in expected and action not in seen;seen.add(action)
        assert (audit['result'],audit['job_id'])==expected[action]
        typed_equal(unique_json(audit['detail_json']),expected_metadata(row))
    assert seen==expected.keys()
