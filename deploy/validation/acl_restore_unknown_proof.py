"""Restore-specific Unknown source, provenance and explicit-ack audit facts."""
import re
from acl_restore_fault_proof import RESTORE_SOURCES, require_restore_profile
from acl_restore_browser_proof import expected_metadata, unique_json
from acl_history_browser_proof import typed_equal

def restore_unknown_sources():
    from validate_container_acl_unknown import SOURCES
    return tuple(dict.fromkeys((*SOURCES,*RESTORE_SOURCES,'acl_restore_unknown_proof.py',
        'validate_container_acl_restore_unknown.py','verify_container_acl_restore_unknown.py','test_acl_restore_unknown_proof.py','test_acl_unknown_proof.py')))

def require_restore_unknown(baseline):
    require_restore_profile(baseline)
    version=baseline['aclRestoreFaultVersion'];assert version in (7,8)
    if 'recoverySourceUiVersion' in baseline:assert type(baseline['recoverySourceUiVersion']) is int and baseline['recoverySourceUiVersion']==1
    assert baseline['kind']==('post-acl-instance-change' if version==8 else 'post-acl-target-removed')
    assert baseline['postAcl'] is True and baseline.get('instanceChanged',False) is (version==8)

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


RECOVERY_SOURCE_FIELDS=('captureId','changedAclType','kind','createdAtUnixMs','originalMode','accessEntryCount','defaultEntryCount')

def recovery_source_summary(baseline):
    require_restore_unknown(baseline)
    entry=baseline['restoreSource']['entry']
    assert type(entry) is dict
    value={key:entry[key] for key in RECOVERY_SOURCE_FIELDS}
    assert type(value['captureId']) is str and re.fullmatch('[a-f0-9]{32}',value['captureId']) and value['captureId']!='0'*32
    assert value['changedAclType'] in ('access','default') and value['kind'] in ('File','Directory')
    for key in ('createdAtUnixMs','originalMode','accessEntryCount','defaultEntryCount'):assert type(value[key]) is int
    assert 0<=value['createdAtUnixMs']<=253402300799999 and 0<=value['originalMode']<=511
    assert 3<=value['accessEntryCount']<=64 and (value['defaultEntryCount']==0 or 3<=value['defaultEntryCount']<=64)
    return value

def require_recovery_source_ui(baseline,recovery,proof):
    require_restore_unknown(baseline)
    assert type(baseline.get('recoverySourceUiVersion')) is int and baseline['recoverySourceUiVersion']==1
    assert type(recovery.get('recoverySourceUiVersion')) is int and recovery['recoverySourceUiVersion']==1
    assert type(proof) is dict and set(proof)=={'schemaVersion','id','subject','reads','events','cardText','timeText','confirmationText','cardInViewport','confirmationInViewport','horizontalOverflow'}
    assert type(proof['schemaVersion']) is int and proof['schemaVersion']==1
    assert proof['id']==baseline['id']==recovery['id'] and proof['subject']==recovery['subject']!=baseline['originalSubject']
    expected=recovery_source_summary(baseline)
    typed_equal(proof['events'],['source-request','source-response','source-request','source-response','ack-request','ack-response','source-request','source-response'])
    assert type(proof['reads']) is list and len(proof['reads'])==3
    for read in proof['reads']:
        assert type(read) is dict and set(read)=={'status','noStore','source'}
        assert type(read['status']) is int and read['status']==200 and read['noStore'] is True
        typed_equal(read['source'],expected)
    assert proof['cardInViewport'] is True and proof['confirmationInViewport'] is True and proof['horizontalOverflow'] is False
    text=proof['cardText'];assert type(text) is str and len(text)<2048
    for fragment in ('历史ACL恢复来源',expected['captureId'],expected['changedAclType'], '文件' if expected['kind']=='File' else '目录',
        '原模式 '+format(expected['originalMode'],'04o'),'access '+str(expected['accessEntryCount'])+' 项','default '+str(expected['defaultEntryCount'])+' 项',
        '这是登记时保存的历史来源摘要，不证明当前文件状态或恢复成功。'):
        assert fragment in text
    assert type(proof['timeText']) is str and 0<len(proof['timeText'])<64 and proof['timeText'] in text
    assert type(proof['confirmationText']) is str and baseline['id'] in proof['confirmationText'] and expected['captureId'] in proof['confirmationText'] and expected['changedAclType'] in proof['confirmationText']


def require_source_denials(proof):
    typed_equal(proof,{'schemaVersion':1,'anonymousStatus':401,'ordinaryStatus':403,'ordinaryAdministrator':False,'anonymousIsolatedSession':True,'normalHttpDualLogout':True})
