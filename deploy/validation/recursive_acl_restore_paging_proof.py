"""Bind real restore preview pages to the original 26-object capture; no I/O."""
from acl_history_browser_proof import typed_equal
from recursive_acl_history_paging_proof import summary
from recursive_acl_history_browser_proof import location,request_proof
from recursive_acl_restore_business_proof import managed_plan,database_proof

OFFSETS=(0,25,0,25)

def page_proof(signal,b,row,metadata,index,*,selection=False):
    assert type(index) is int and 0<=index<4 and type(selection) is bool
    offset=OFFSETS[index];end=min(offset+25,26);s=summary(row,metadata)
    entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in metadata['entries'][offset:end]]
    typed_equal(signal['snapshot'],dict(location=location(b),summary=s,offset=offset,entries=entries,complete=end==26,nextOffset=None if end==26 else 25))
    request_proof(signal['request'],b,s['captureId'],s,offset)
    assert signal['subject'] and signal['index']==index and type(signal['index']) is int
    assert signal['screenshot']==('selection.png' if selection else f'paging-{index}.png')
    return s

def ready_proof(signal,b,row,metadata,data):
    ready=signal['ready'];registered=signal['registered'];r=data['row'];p=managed_plan(r['plan_json']);s=summary(row,metadata)
    assert r['state']=='Ready' and ready['state']=='Ready' and registered['state']=='Queued'
    assert ready['id']==r['id']==registered['id']==ready['planningJobId'] and ready['version']==r['version'] and ready['executionJobId'] is r['execution_job_id'] is None
    assert signal['subject']==r['actor_subject'] and signal['key']==r['idempotency_key']
    typed_equal(ready['restoreSource'],s);typed_equal(p['restoreSource'],s)
    assert [ready[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[26,25,1,26]
    assert all(ready[k]==p[k] for k in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest'))
    typed_equal(signal['body'],dict(instanceId=b['instanceId'],path='work/recursive-tree',captureId=s['captureId'],expectedHistoryDigest=s['historyDigest'],expectedVersion=p['command']['expectedVersion']))
    typed_equal(signal['reference'],dict(subject=signal['subject'],containerId=b['containerId'],key=signal['key'],id=ready['id']))
    assert signal['duplicateRegistrationId']==ready['id']
    return p

def canceled_proof(signal,b,row,metadata,data):
    proof=database_proof(data,signal['result'],b,signal['subject']);assert proof['canceled']
    typed_equal(proof['plan']['restoreSource'],summary(row,metadata))
    assert [proof['plan'][k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[26,25,1,26]
    assert signal['result']['id']==signal['ready']['id'] and signal['dismissedWithoutWrite'] is True
    assert signal['registrations']==signal['cancellations']==1 and signal['confirmations']==signal['uploads']==0
    assert signal['result']['executionJobId'] is None
    return proof
