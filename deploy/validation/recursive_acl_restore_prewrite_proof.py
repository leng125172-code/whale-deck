"""Read-only early-abort proof: current preimage aggregate survives, zero writes."""
import json
import os
import re
import uuid
from pathlib import Path
from collections import Counter
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_rpc_proof import source_proof,manifest_proof,child_id,digest,fields,integer
from recursive_acl_restore_record_proof import ROOT,CHILD,COUNTS,receipt_proof
from recursive_acl_restore_business_proof import managed_plan,managed_receipt,metadata,json_projection
from recursive_acl_restore_agent_io import read_at,namespace,archive_raw


def read_early(io,request,folder,*,create=False):
    op=request['operationId'];child_id(op,0)
    if create:folder.mkdir(mode=0o700,parents=True,exist_ok=True)
    def save(name,raw):archive_raw(folder,name,raw,create)
    raw,root,_=read_at(None,str(io.restoreState/(op+'.json')));save('root.json',raw)
    with namespace(io,True) as fd:
        assert fd is not None
        raw,meta,_=read_at(fd,op+'.json');save('hdd.json',raw)
        children=[];started=root['startedThrough'];assert type(started) is int and started in (-1,0)
        directory=io.restoreState/(op+'.entries')
        actual={f.name for f in directory.glob('*.json')} if directory.exists() else set()
        assert actual==({child_id(op,0)+'.json'} if started==0 else set())
        if started==0:
            sub=child_id(op,0);raw,cr,_=read_at(None,str(directory/(sub+'.json')));save(sub+'.root.json',raw);hdd=None
            if cr['historyVerified']:
                child=io.files.open_at(fd,op+'/entries',os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
                try:
                    raw,hdd,_=read_at(child,sub+'.json');save(sub+'.hdd.json',raw)
                finally:os.close(child)
            children.append(dict(root=cr,hdd=hdd))
        # No other child HDD JSON can be silently ignored.
        base=Path(os.readlink('/proc/self/fd/'+str(fd)))/op/'entries'
        assert ({f.name for f in base.glob('*.json')} if base.exists() else set())==({child_id(op,0)+'.json'} if children and children[0]['hdd'] is not None else set())
    return dict(root=root,metadata=meta,children=children)


def early_abort_proof(request,receipt,bundle,source,frame,before,after,attrs):
    root=bundle['root'];plan=request['plan'];op=request['operationId'];n=plan['entryCount']
    typed_equal(before,after);source_proof(plan,source,frame,before)
    fields(root,ROOT);integer(root['schemaVersion'],1,1)
    assert all(root[k]==request[k] for k in ('operationId','owner','plan'))
    assert root['state']=='Aborted' and root['historyMayExist'] is root['historyVerified'] is True
    assert type(root['startedThrough']) is type(root['finishedThrough']) is int and root['finishedThrough']==-1 and root['startedThrough'] in (-1,0)
    for key in COUNTS:integer(root[key],0,n)
    started=root['startedThrough']==0
    assert [root[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')]==[0,0,int(started),n-int(started),0]
    entries=manifest_proof(plan,bundle['metadata'],source,before,attrs,operation_id=op,owner=request['owner'])
    assert bundle['metadata']['historyDigest']==root['historyDigest']
    assert len(bundle['children'])==int(started);verified=0
    if started:
        pair=bundle['children'][0];cr=pair['root'];hdd=pair['hdd'];sub=child_id(op,0);fields(cr,CHILD)
        integer(cr['schemaVersion'],1,1)
        assert cr['operationId']==sub and cr['owner']==request['owner'] and cr['plan']==entries[0]['plan']
        assert cr['state']=='Aborted' and cr['actualIdentity'] is None and cr['aclConfirmed'] is False and cr['version']==''
        assert type(cr['historyMayExist']) is type(cr['historyVerified']) is bool
        if cr['historyVerified']:
            fields(hdd,('schemaVersion','resourceType','storageId','operationId','owner','plan','createdAtUnixMs','parentOperationId','manifestDigest','historyDigest'))
            integer(hdd['schemaVersion'],1,1)
            assert hdd['resourceType']=='ContainerRecursiveAclRestoreEntryMetadata' and hdd['storageId']=='database-platform.backup.hdd'
            assert hdd['operationId']==sub and hdd['parentOperationId']==op and hdd['manifestDigest']==plan['manifestDigest'] and hdd['owner']==request['owner'] and hdd['plan']==cr['plan']
            integer(hdd['createdAtUnixMs'],0,9223372036854775807)
            assert cr['historyMayExist'] is True and hdd['historyDigest']==cr['historyDigest']==digest(b'whaledeck-container-recursive-acl-restore-entry-history-v1\n',{k:v for k,v in hdd.items() if k!='historyDigest'});verified=1
        else:assert hdd is None and cr['historyDigest']==''
    outcomes=[dict(id=child_id(op,i),state='Aborted' if i==0 and started else 'NotStarted',version='',changed=False) for i in range(n)]
    assert root['outcomeDigest']==(digest(b'whaledeck-recursive-acl-restore-outcomes-v1\n',outcomes) if started else '')
    assert type(receipt['replayed']) is bool
    receipt_proof(receipt,request,root,restore=True,binding=False,replayed=receipt['replayed'])
    return dict(aggregateHdd=1,childHdd=verified,counts=[root[k] for k in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount')])


def sql_proof(data,ready_signal,initial,b,source_meta):
    row=data['row'];old=initial['row'];result=ready_signal['terminal'];subject=ready_signal['subject']
    assert row['actor_subject']==subject==old['actor_subject'] and row['container_id']==b['containerId'] and row['instance_id']==b['instanceId'] and row['relative_path']=='work/recursive-tree'
    for key in ('id','idempotency_key','login_binding','owner_hash','command_json','request_hash','plan_json','plan_hash','ready_expires_at_utc'):typed_equal(row[key],old[key])
    assert row['state']==result['state']==data['executionJob']['state']=='Canceled' and row['result_code']==result['resultCode']=='CONTAINER_RECURSIVE_ACL_RESTORE_CANCELED'
    assert row['cancel_requested_at_utc'] and row['confirmed_at_utc'] and row['root_started_at_utc'] and row['completed_at_utc'] and row['publish_barrier'] is False and row['lease_token'] is row['lease_expires_at_utc'] is None
    assert all(type(data[k]) is int and data[k]>=0 for k in ('pending','accepted','confirmed','events'))
    assert data['pending']==0 and data['accepted']==data['confirmed']==1 and data['events']>=4 and data['planJob']['state']=='Succeeded'
    assert data['planJob']['job_type']=='containers.files.recursive-acl-restore-plan' and data['executionJob']['job_type']=='containers.files.recursive-acl-restore'
    assert row['id']==result['id']==result['planningJobId']==data['planJob']['id'] and row['execution_job_id']==result['executionJobId']==data['executionJob']['id']
    for job in ('planJob','executionJob'):typed_equal(json_projection(data[job]['request_json']),metadata(row));assert data[job]['actor_subject']==subject
    assert data['planJob']['idempotency_key']==row['idempotency_key'] and data['executionJob']['idempotency_key']==row['execution_job_id']
    assert result['confirmation'] is None and result['version']==row['version']
    typed_equal(result['command'],ready_signal['readySignal']['ready']['command'])
    for key in ('request_hash','plan_hash','execution_hash','receipt_hash'):assert re.fullmatch('[A-F0-9]{64}',row[key])
    plan=managed_plan(row['plan_json']);typed_equal(plan['restoreSource'],source_meta);typed_equal(result['restoreSource'],source_meta)
    for key in ('entryCount','fileCount','directoryCount','changeCount','manifestDigest'):typed_equal(result[key],plan[key])
    assert [result[k] for k in ('rootUid','rootGid','rootMode','executorUid','executorGid')]==[*plan['rootIdentity'][3:5],plan['rootIdentity'][2],plan['uid'],plan['gid']]
    receipt=managed_receipt(row['receipt_json']);typed_equal(receipt,managed_receipt(result['receipt'],api=True))
    assert receipt['operationId']==uuid.UUID(row['execution_job_id']).hex and receipt['state']==row['root_state']=='Aborted'
    for key in ('completedCount','changedCount','abortedCount','notStartedCount','unknownCount','outcomeDigest','historyMayExist','historyVerified','historyDigest','historyAvailable','bindingCurrent'):
        column=re.sub(r'(?<!^)(?=[A-Z])','_',key).lower();typed_equal(receipt[key],row[column])
    wanted=Counter({('plan-accepted','Accepted',row['id']):1,('plan-result','Ready',row['id']):1,('confirmed','Accepted',row['execution_job_id']):1,('cancel-requested','Accepted',row['execution_job_id']):1,('result','Canceled',row['execution_job_id']):1});seen=Counter();ids=set()
    for audit in data['audits']:
        assert audit['id'] not in ids;ids.add(audit['id']);assert audit['actor_subject']==subject and audit['target_type']=='container' and audit['target_id']==row['container_id']
        prefix='containers.files.recursive-acl-restore.';assert audit['action'].startswith(prefix) and 0<len(audit['trace_id'])<=128
        seen[(audit['action'].removeprefix(prefix),audit['result'],audit['job_id'])]+=1;typed_equal(json_projection(audit['detail_json']),metadata(row))
    assert seen==wanted
    return dict(operationId=uuid.UUID(row['execution_job_id']).hex,owner=row['owner_hash'],plan=plan),receipt
