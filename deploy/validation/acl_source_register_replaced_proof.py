"""No restore registration when an originally selected capture was validly replaced."""
from acl_source_apply_replaced_proof import SEEDS as PREVIOUS
from acl_source_apply_missing_proof import source_closure
from acl_source_register_missing_proof import intent_id,no_registration
from acl_source_confirm_replaced_proof import verify_changed_snapshot
from validate_container_acl_browser import version

KIND='source-register-replaced'
SEEDS=(*PREVIOUS,'acl_source_register_replaced_proof.py','test_acl_source_register_replaced_proof.py',
       'validate_container_acl_source_register_replaced.py','validate_container_acl_source_register_replaced.mjs',
       'verify_container_acl_source_register_replaced.py')


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def require_rejected(baseline,value,proof):
    assert value['subject']==baseline['subject'] and value['registrationStatus']==409 and value['code']=='OPERATION_CONFLICT'
    assert value['sourceStatus']==409 and value['sourceCode']=='AGENT_VERSION_CONFLICT' and value['replacementReadStatus']==200 and value['byKeyStatus']==404
    assert (value['registrations'],value['confirmations'],value['cancellations'],value['contentWrites'],value['jobWrites'])==(1,0,0,0,0)
    body=value['body'];source=baseline['restoreSource']
    assert set(body)=={'instanceId','path','captureId','expectedHistoryDigest','expectedVersion'}
    assert body['instanceId']==baseline['instanceId'] and body['path']=='work/acl-file.bin'
    assert body['captureId']==source['entry']['captureId'] and body['expectedHistoryDigest']==source['entry']['historyDigest']
    assert body['expectedVersion']==version(baseline,'work/acl-file.bin',baseline['original']['acl-file.bin']['identity'])
    verify_changed_snapshot(source,proof,value['replacementSnapshot'])
