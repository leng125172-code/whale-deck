"""Bounded 26-capture chain and real inventory page/DOM proofs; no mutations."""
import re

from acl_history_browser_proof import typed_equal
from recursive_acl_history_browser_proof import location,request_proof,prove_pages
from recursive_acl_history_paging_proof import verify_capture

LABELS=('first','last','back','next','selected')

def summary(row,metadata):
    p=row['request']['plan']
    return dict(captureId=row['request']['operationId'],historyDigest=metadata['historyDigest'],createdAtUnixMs=metadata['createdAtUnixMs'],
        sourceInstanceId=p['command']['instanceId'],changedAclType='access',**{k:p[k] for k in ('manifestDigest','entryCount','fileCount','directoryCount','changeCount')})

def verify_chain(evidence,b,rows,storage):
    assert type(rows) is list and len(rows)==26
    previous=b['original'];metadata={}
    for index,row in enumerate(rows):
        assert row['index']==index and type(row['index']) is int
        profile='inventory-mask' if index%2 else 'inventory-named';assert row['profile']==profile
        typed_equal(row['before'],previous)
        capture=row['request']['operationId'];assert re.fullmatch('[a-f0-9]{32}',capture) and capture not in metadata
        assert row['request']['plan']['changeCount']==(4 if index==0 else 2)
        metadata[capture]=verify_capture(evidence/'captures'/capture,{**b,'original':previous},row,storage,profile=profile)
        previous=row['after']
    return metadata

def expected_snapshot(b,row,metadata):
    entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,
                  accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in metadata['entries']]
    assert len(entries)==4
    return dict(location=location(b),summary=summary(row,metadata),offset=0,entries=entries,complete=True,nextOffset=None)

def prove_signal(signal,b,rows,metadata,label):
    assert label in LABELS and signal['label']==label and signal['subject']
    expected={row['request']['operationId']:summary(row,metadata[row['request']['operationId']]) for row in rows}
    captures=sorted(expected);assert len(captures)==26
    pages=signal['pages'];assert (len(pages)==1 if label=='first' else 2<=len(pages)<=100)
    first=pages[0];request_proof(first['request'],b)
    page=first['page'];assert set(page)=={'location','version','entries','nextAfter','scannedCount','invalidCount','complete'}
    typed_equal(page['location'],location(b));assert re.fullmatch('[A-F0-9]{64}',page['version'])
    assert type(page['entries']) is list and 0<=len(page['entries'])<=25
    first_count=len(page['entries'])
    assert type(page['complete']) is bool and page['complete'] is False and re.fullmatch('[a-f0-9]{32}',page['nextAfter'])
    assert (not first_count or page['nextAfter']>=captures[first_count-1]) and page['nextAfter']<captures[first_count]
    assert type(page['scannedCount']) is int and type(page['invalidCount']) is int and 0<=page['invalidCount']<=page['scannedCount']<=200 and first_count+page['invalidCount']<=page['scannedCount'] and page['scannedCount']>0
    typed_equal(page['entries'],[expected[c] for c in captures[:first_count]])
    if label!='first':
        prove_pages(pages,b,expected)
    typed_equal(signal['visibleCaptures'],captures[:first_count] if label=='first' else captures[:25] if label=='back' else captures[-1:])
    assert signal['screenshot']=='inventory-'+label+'.png'
    if label=='selected':
        row=next(r for r in rows if r['request']['operationId']==captures[-1])
        typed_equal(signal['snapshot'],expected_snapshot(b,row,metadata[captures[-1]]))
        request_proof(signal['request'],b,captures[-1],expected[captures[-1]])
    else:assert 'snapshot' not in signal and 'request' not in signal
    return expected
