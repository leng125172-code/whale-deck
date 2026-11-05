"""Read-only proof of one completed restoration and an untouched remaining tree.

This deliberately accepts only startedThrough=finishedThrough=0, no unknown or
second started child. Other Partial shapes require their own proof. No writes,
resume, discard, SQL mutation, helper signalling or authority changes.
"""
import os
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at,namespace,archive_raw
from recursive_acl_restore_rpc_proof import child_id,fields,integer,digest,source_proof,manifest_proof
from recursive_acl_restore_record_proof import ROOT,CHILD,COUNTS,receipt_proof
from verify_container_recursive_acl import file_version
from validate_container_upload_recovery_agent import trusted

def _read_first(io,request,folder,phase,*,create=False):
    assert phase in ('Applying','Partial')
    op=request['operationId'];sub=child_id(op,0)
    if create:folder.mkdir(mode=0o700,parents=True,exist_ok=True)
    trusted(io.restoreState,True);trusted(folder,True)
    def save(name,raw):archive_raw(folder,name,raw,create)
    raw,root,_=read_at(None,str(io.restoreState/(op+'.json')));save('root.json',raw)
    assert root['state']==phase and type(root['startedThrough']) is type(root['finishedThrough']) is int and root['startedThrough']==root['finishedThrough']==0
    directory=io.restoreState/(op+'.entries')
    trusted(directory,True)
    assert {p.name for p in directory.iterdir() if p.suffix=='.json'}=={sub+'.json'}
    raw,cr,_=read_at(None,str(directory/(sub+'.json')));save(sub+'.root.json',raw)
    with namespace(io,True) as fd:
        assert fd is not None
        raw,meta,_=read_at(fd,op+'.json');save('hdd.json',raw)
        batch=io.files.open_at(fd,op+'/entries',os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            actual=Path(os.readlink('/proc/self/fd/'+str(batch)))
            trusted(actual,True)
            assert {p.name for p in actual.iterdir() if p.suffix=='.json'}=={sub+'.json'}
            raw,hdd,_=read_at(batch,sub+'.json');save(sub+'.hdd.json',raw)
        finally:os.close(batch)
    return dict(root=root,metadata=meta,children=[dict(root=cr,hdd=hdd)])

def read_partial_first(io,request,folder,*,create=False):
    return _read_first(io,request,folder,'Partial',create=create)

def read_applying_first(io,request,folder,*,create=False):
    return _read_first(io,request,folder,'Applying',create=create)

def completed_first_facts(request,bundle,source,frame,before,after,attrs):
    plan=request['plan'];op=request['operationId'];sub=child_id(op,0);root=bundle['root'];n=plan['entryCount']
    fields(root,ROOT);integer(root['schemaVersion'],1,1);integer(n,2,10000)
    for k in ('operationId','owner','plan'):typed_equal(root[k],request[k])
    assert root['state'] in ('Applying','Partial') and root['historyMayExist'] is root['historyVerified'] is True
    for k in COUNTS:integer(root[k],0,n)
    assert type(root['startedThrough']) is type(root['finishedThrough']) is int and root['startedThrough']==root['finishedThrough']==0
    source_proof(plan,source,frame,before)
    entries=manifest_proof(plan,bundle['metadata'],source,before,attrs,operation_id=op,owner=request['owner'])
    assert bundle['metadata']['historyDigest']==root['historyDigest'] and len(bundle['children'])==1
    entry=entries[0];rel=entry['relativePath'];cp=entry['plan'];old=before[rel];actual=after[rel];cr=bundle['children'][0]['root'];hdd=bundle['children'][0]['hdd']
    assert old.keys()==actual.keys()
    fields(cr,CHILD);integer(cr['schemaVersion'],1,1)
    assert cr['operationId']==sub and cr['owner']==request['owner'];typed_equal(cr['plan'],cp)
    assert cr['state']=='Completed' and cr['aclConfirmed'] is cr['historyMayExist'] is cr['historyVerified'] is True and cp['noChange'] is False
    fields(hdd,('schemaVersion','resourceType','storageId','operationId','owner','plan','createdAtUnixMs','parentOperationId','manifestDigest','historyDigest'))
    integer(hdd['schemaVersion'],1,1);integer(hdd['createdAtUnixMs'],0,9223372036854775807)
    assert hdd['resourceType']=='ContainerRecursiveAclRestoreEntryMetadata' and hdd['storageId']=='database-platform.backup.hdd'
    assert hdd['operationId']==sub and hdd['parentOperationId']==op and hdd['owner']==request['owner'] and hdd['manifestDigest']==plan['manifestDigest'];typed_equal(hdd['plan'],cp)
    assert hdd['historyDigest']==cr['historyDigest']==digest(b'whaledeck-container-recursive-acl-restore-entry-history-v1\n',{k:v for k,v in hdd.items() if k!='historyDigest'})
    identity=actual['identity'];assert type(identity) is list and len(identity)==9 and all(type(v) is int for v in identity)
    wanted=list(old['identity']);wanted[2]=wanted[2]&~0o777|cp['targetMode']
    assert identity[:8]==wanted[:8] and identity[8]>old['identity'][8];typed_equal(cr['actualIdentity'],identity)
    selected=cp['command']['aclType'];target=cp['command']['entries']
    assert actual[selected]==target and actual[selected]!=old[selected]
    for k in old.keys()-{'identity',selected}:typed_equal(actual[k],old[k])
    assert before.keys()==after.keys()
    for p in before.keys()-{rel}:typed_equal(after[p],before[p])
    assert cr['version']==file_version(plan['command'],cp['command']['path'],identity)
    return dict(aggregateHdd=1,completedChildHdd=1,firstRelativePath=rel,otherEntriesUnchanged=n-1)

def partial_first_proof(request,receipt,bundle,source,frame,before,after,attrs):
    proof=completed_first_facts(request,bundle,source,frame,before,after,attrs)
    root=bundle['root'];op=request['operationId'];n=request['plan']['entryCount'];cr=bundle['children'][0]['root']
    assert root['state']=='Partial'
    for k in COUNTS:integer(root[k],0,n)
    assert [root[k] for k in COUNTS]==[1,1,0,n-1,0]
    outcomes=[dict(id=child_id(op,i),state='Completed' if i==0 else 'NotStarted',version=cr['version'] if i==0 else '',changed=i==0) for i in range(n)]
    assert root['outcomeDigest']==digest(b'whaledeck-recursive-acl-restore-outcomes-v1\n',outcomes)
    assert type(receipt['replayed']) is bool
    receipt_proof(receipt,request,root,restore=True,binding=False,replayed=receipt['replayed'])
    return dict(**proof,counts=[root[k] for k in COUNTS])
