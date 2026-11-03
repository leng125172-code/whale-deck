"""Zero-registration proof for a valid selected source lost BEFORE first POST."""
import hashlib
import re
import uuid
from acl_source_plan_missing_proof import SEEDS as PREVIOUS
from acl_source_apply_missing_proof import source_closure
from validate_metric_continuity import query
from validate_container_acl_browser import version

KIND='source-register-missing'
SEEDS=(*PREVIOUS,'acl_source_register_missing_proof.py','validate_container_acl_source_register_missing.py',
       'validate_container_acl_source_register_missing.mjs','verify_container_acl_source_register_missing.py','test_acl_source_register_missing_proof.py')
COUNTS={'intentRows','jobRows','outboxRows','eventRows','auditRows'}


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def intent_id(subject,login,key):
    assert type(subject) is str and subject.strip() and len(subject.encode('utf-16-le'))//2<=256
    assert not any(ord(c)<32 or 127<=ord(c)<=159 for c in subject)
    assert type(login) is str and re.fullmatch('[A-F0-9]{64}',login)
    assert type(key) is str and str(uuid.UUID(key))==key and uuid.UUID(key).int!=0
    subject_hash=hashlib.sha256(('whaledeck-bff-subject-v2:'+subject).encode()).hexdigest().upper()
    raw=hashlib.sha256(('whaledeck-container-acl-request-id-v1:'+subject_hash+':'+login+':'+uuid.UUID(key).hex).encode()).digest()
    return str(uuid.UUID(bytes_le=raw[:16]))


def no_registration(subject,login,key):
    identifier=intent_id(subject,login,key)
    raw=hashlib.sha256(('whaledeck-container-acl-execution-id-v1:'+uuid.UUID(identifier).hex).encode()).digest()
    execution=str(uuid.UUID(bytes_le=raw[:16]))
    counts=query('''SELECT json_build_object(
      'intentRows',(SELECT count(*) FROM container_acl_intents WHERE id=:'id'::uuid OR actor_subject=:'subject' AND restore_source_json IS NOT NULL),
      'jobRows',(SELECT count(*) FROM operation_jobs WHERE id IN(:'id'::uuid,:'execution'::uuid) OR actor_subject=:'subject' AND idempotency_key IN(:'key',:'execution')),
      'outboxRows',(SELECT count(*) FROM outbox_messages WHERE payload_json->>'jobId' IN(:'id',:'execution')),
      'eventRows',(SELECT count(*) FROM operation_job_events WHERE job_id IN(:'id'::uuid,:'execution'::uuid)),
      'auditRows',(SELECT count(*) FROM audit_events WHERE actor_subject=:'subject' AND action LIKE 'containers.files.acl.restore.%'));''',
      id=identifier,execution=execution,subject=subject,key=key)
    require_absent(counts)
    return dict(expectedIntentId=identifier,expectedExecutionId=execution,counts=counts)


def require_absent(counts):
    assert type(counts) is dict and set(counts)==COUNTS
    assert all(type(value) is int and value==0 for value in counts.values()),'RESTORE_REGISTRATION_NOT_ABSENT'


def require_rejected(baseline,value):
    assert value['subject']==baseline['subject'] and value['registrationStatus']==409 and value['code']=='OPERATION_CONFLICT'
    assert value['sourceStatus']==value['byKeyStatus']==404
    assert (value['registrations'],value['confirmations'],value['cancellations'],value['contentWrites'],value['jobWrites'])==(1,0,0,0,0)
    body=value['body'];source=baseline['restoreSource']
    assert set(body)=={'instanceId','path','captureId','expectedHistoryDigest','expectedVersion'}
    assert body['instanceId']==baseline['instanceId'] and body['path']=='work/acl-file.bin'
    assert body['captureId']==source['entry']['captureId'] and body['expectedHistoryDigest']==source['entry']['historyDigest']
    assert body['expectedVersion']==version(baseline,'work/acl-file.bin',baseline['original']['acl-file.bin']['identity'])
