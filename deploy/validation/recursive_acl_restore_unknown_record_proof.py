"""Known first restoration plus target rename: distinct conservative Unknown facts.

Read-only proof only. Native/SQL/identity acquisition and owned mutation belong
to their respective gates, not this validator. Only two explicit access-tree
size/name pairs are covered; other paths, child records and Unknown shapes reject.
"""
import stat
from pathlib import PurePosixPath
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_partial_record_proof import completed_first_facts
from recursive_acl_restore_record_proof import ROOT,COUNTS,receipt_proof
from recursive_acl_restore_rpc_proof import fields,integer,manifest_proof
from recursive_acl_restore_rpc_proof import child_id
from recursive_acl_restore_agent_io import read_at
from validate_container_upload_recovery_agent import trusted

RENAME_SCENARIOS={(4,2,2):('one.bin','renamed.bin'),(26,25,1):('item-01.bin','renamed-item-01.bin')}

def prefix_archives_unchanged(cut_folder,unknown_folder,operation):
    trusted(cut_folder,True);trusted(unknown_folder,True);sub=child_id(operation,0)
    for name in ('hdd.json',sub+'.root.json',sub+'.hdd.json'):
        before,_,_=read_at(None,str(cut_folder/name));after,_,_=read_at(None,str(unknown_folder/name))
        assert before==after,'KNOWN_RESTORE_ORIGINAL_CHANGED'
    return 3

def renamed_leaf(cut,after,old,new):
    for name in (old,new):
        assert type(name) is str and name not in ('','.','..') and '/' not in name and '\\' not in name and '\x00' not in name and len(name.encode())<=255 and PurePosixPath(name).name==name
    assert old!=new and old in cut and new not in cut and old not in after and after.keys()==(cut.keys()-{old})|{new}
    previous=cut[old];actual=after[new];assert previous.keys()==actual.keys()
    a=previous['identity'];b=actual['identity'];assert stat.S_ISREG(a[2]) and type(b) is list and len(b)==9 and all(type(v) is int for v in b)
    assert b[:8]==a[:8] and b[8]>=a[8]
    for key in previous.keys()-{'identity'}:typed_equal(actual[key],previous[key])
    root_before=cut[''];root_after=after[''];assert root_before.keys()==root_after.keys()
    r=root_after['identity'];assert type(r) is list and len(r)==9 and all(type(v) is int for v in r) and r[:6]==root_before['identity'][:6] and stat.S_ISDIR(r[2]) and r[6]>=0 and r[7]>=root_before['identity'][7] and r[8]>=root_before['identity'][8]
    for key in root_before.keys()-{'identity'}:typed_equal(root_after[key],root_before[key])
    for path in cut.keys()-{old,''}:typed_equal(after[path],cut[path])
    return dict(oldPath=old,newPath=new,originalInodeRetained=True,onlyRootDirectoryMetadataMayChange=True)

def unknown_renamed_first_proof(request,receipt,cut_bundle,bundle,source,frame,before,cut,after,attrs,old,new):
    p=request['plan'];shape=tuple(p[k] for k in ('entryCount','fileCount','directoryCount'))
    assert all(type(v) is int for v in shape) and p['restoreSource']['changedAclType']=='access' and p['changeCount']==p['entryCount']
    assert RENAME_SCENARIOS.get(shape)==(old,new),'UNKNOWN_RENAME_SCENARIO_MISMATCH'
    first=completed_first_facts(request,cut_bundle,source,frame,before,cut,attrs)
    assert cut_bundle['root']['state']=='Applying' and first['firstRelativePath'] not in ('',old)
    renamed=renamed_leaf(cut,after,old,new)
    root=bundle['root'];fields(root,ROOT);integer(root['schemaVersion'],1,1)
    for key in ('operationId','owner','plan'):typed_equal(root[key],request[key])
    n=request['plan']['entryCount'];integer(n,2,10000)
    assert root['state']=='Unknown' and type(root['startedThrough']) is type(root['finishedThrough']) is int and root['finishedThrough']==0 and root['startedThrough'] in (0,1)
    for key in COUNTS:integer(root[key],0,n)
    assert [root[key] for key in COUNTS]==[0,0,0,0,n] and root['outcomeDigest']==''
    assert root['historyMayExist'] is root['historyVerified'] is True and root['historyDigest']==cut_bundle['root']['historyDigest']
    manifest_proof(request['plan'],bundle['metadata'],source,before,attrs,operation_id=request['operationId'],owner=request['owner'])
    typed_equal(bundle['metadata'],cut_bundle['metadata']);typed_equal(bundle['children'],cut_bundle['children'])
    assert type(receipt['replayed']) is bool
    receipt_proof(receipt,request,root,restore=True,binding=False,replayed=receipt['replayed'])
    return dict(**renamed,knownCompletedChildren=1,knownActualChanges=1,aggregateCounts=[root[key] for key in COUNTS],aggregateUnknownIsNotZeroActualChanges=True,firstRelativePath=first['firstRelativePath'])
