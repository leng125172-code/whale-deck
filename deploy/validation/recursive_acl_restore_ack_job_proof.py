"""Read-only before/after summary fact preservation, after SQL/native binding.

This supplements, never replaces, full SQL and native Root/HDD proofs. There is
no request, mutation, acknowledgement action, or missing-fact backfill here.
"""
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_job_results_proof import safe_summary,COUNTS

def acknowledgement_facts(before,after):
    before=safe_summary(before);after=safe_summary(after)
    assert before['stage']==after['stage']=='execution' and before['state'] in ('Partial','Unknown') and after['state']=='Acknowledged'
    fixed=('schemaVersion','intentId','jobId','stage','aclType','entryCount','fileCount','directoryCount','changeCount','sourceEntryCount','sourceFileCount','sourceDirectoryCount','sourceChangedCount')
    for key in fixed:typed_equal(after[key],before[key])
    assert after['publishBarrier'] is False
    assert after['code']=='CONTAINER_RECURSIVE_ACL_RESTORE_'+('PARTIAL_ACKNOWLEDGED' if before['state']=='Partial' else 'UNKNOWN_ACKNOWLEDGED')
    assert after['receiptState'] in ('Completed','Aborted','Partial','Unknown')
    assert all(type(after[k]) is int and 0<=after[k]<=10000 for k in COUNTS)
    assert after['completedCount']+after['abortedCount']+after['notStartedCount']+after['unknownCount']==after['entryCount']
    assert after['changedCount']<=after['completedCount'] and after['changedCount']<=after['changeCount']
    if before['receiptState'] is not None:
        for key in (*COUNTS,'receiptState','historyVerified'):typed_equal(after[key],before[key])
    else:
        assert before['state']=='Unknown' and all(before[k] is None for k in COUNTS)
    # Current binding and availability can change. Historical verification cannot vanish.
    for key in ('historyMayExist','historyVerified'):
        assert type(after[key]) is bool
        if before[key] is True:assert after[key] is True
    return dict(originalState=before['state'],receiptBefore=before['receiptState'],receiptAfter=after['receiptState'],actualCounts=[after[k] for k in COUNTS],originalUncertaintyPreserved=True,wholeTreeSuccessClaimed=False)
