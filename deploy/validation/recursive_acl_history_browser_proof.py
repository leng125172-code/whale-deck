"""Strict HTTP/HDD and durable audit binding; no writes or dependency access."""
from collections import Counter
import json
import re
import uuid

from acl_history_browser_proof import digest,typed_equal

def endpoint(baseline):
    return '/api/v1/containers/'+baseline['containerId']+'/files/recursive-acls/history'

def location(baseline):
    return dict(containerId=baseline['containerId'],instanceId=baseline['instanceId'],path='work/recursive-tree')

def expected_summary(result,metadata):
    plan=metadata['plan'];capture=uuid.UUID(result['executionJobId']).hex
    assert metadata['operationId']==capture and metadata['historyDigest']==result['historyDigest']
    return dict(captureId=capture,historyDigest=metadata['historyDigest'],createdAtUnixMs=metadata['createdAtUnixMs'],
                sourceInstanceId=plan['command']['instanceId'],changedAclType=plan['command']['aclType'],
                manifestDigest=plan['manifestDigest'],**{k:plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')})

def request_proof(request,baseline,capture='',summary=None,offset=0,after='',version=''):
    assert type(request) is dict and set(request)=={'method','pathname','query','status','noStore'}
    assert request['method']=='GET' and type(request['status']) is int and request['status']==200 and request['noStore'] is True
    assert request['pathname']==endpoint(baseline)+('/'+capture if capture else '')
    query=dict(instanceId=baseline['instanceId'],path='work/recursive-tree')
    if capture:query.update(expectedHistoryDigest=summary['historyDigest'],offset=str(offset),take='25')
    else:query.update(after=after,take='25',expectedVersion=version)
    typed_equal(request['query'],query)

def prove_snapshot(signal,baseline,result,metadata):
    summary=expected_summary(result,metadata);capture=summary['captureId']
    assert signal['name']==result['name'] and signal['captureId']==capture and signal['subject']
    entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,
                  accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in metadata['entries']]
    # This owned fixture has four entries. Do not label its one-page read a UI pagination acceptance.
    assert len(entries)==4 and summary['entryCount']==4
    typed_equal(signal['snapshot'],dict(location=location(baseline),summary=summary,offset=0,entries=entries,complete=True,nextOffset=None))
    request_proof(signal['request'],baseline,capture,summary)
    assert signal['screenshot']=='history-'+result['name']+'.png'
    return summary

def prove_pages(pages,baseline,expected):
    assert type(pages) is list and pages
    after='';version='';seen=[]
    for index,item in enumerate(pages):
        assert type(item) is dict and set(item)=={'request','page'}
        request_proof(item['request'],baseline,after=after,version=version)
        page=item['page'];assert set(page)=={'location','version','entries','nextAfter','scannedCount','invalidCount','complete'}
        typed_equal(page['location'],location(baseline))
        assert type(page['version']) is str and re.fullmatch('[A-F0-9]{64}',page['version']) and (not version or page['version']==version)
        assert type(page['scannedCount']) is int and type(page['invalidCount']) is int and 0<=page['invalidCount']<=page['scannedCount']<=200
        assert type(page['entries']) is list and len(page['entries'])<=25 and len(page['entries'])+page['invalidCount']<=page['scannedCount']
        assert type(page['complete']) is bool and page['complete'] is (index==len(pages)-1)
        assert type(page['nextAfter']) is str and page['complete']==(page['nextAfter']=='')
        previous=after
        for entry in page['entries']:
            capture=entry['captureId'];assert capture>previous and capture in expected
            assert page['complete'] or capture<=page['nextAfter']
            typed_equal(entry,expected[capture]);seen.append(capture);previous=capture
        if not page['complete']:assert re.fullmatch('[a-f0-9]{32}',page['nextAfter']) and page['nextAfter']>after and page['nextAfter']>=previous
        version=page['version'];after=page['nextAfter']
    assert seen==sorted(expected) and len(seen)==len(set(seen))

def unique_json(raw):
    def unique(pairs):
        output={}
        for key,value in pairs:
            assert key not in output
            output[key]=value
        return output
    return json.loads(raw,object_pairs_hook=unique) if type(raw) is str else raw

def prove_audits(rows,subject,baseline,pages,signals):
    expected=[]
    common=dict(pathHash=digest('work/recursive-tree'),instanceHash=digest(baseline['instanceId']))
    for item in pages:
        page=item['page'];query=item['request']['query']
        expected.append(('containers.files.recursive-acl-history-list',dict(**common,
            afterHash=digest(query['after']) if query['after'] else None,take=25,inventoryVersion=page['version'],
            returnedCount=len(page['entries']),scannedCount=page['scannedCount'],invalidCount=page['invalidCount'],complete=page['complete'])))
    for signal in signals:
        snapshot=signal['snapshot'];summary=snapshot['summary']
        expected.append(('containers.files.recursive-acl-history-read',dict(**common,captureHash=digest(summary['captureId']),
            historyDigest=summary['historyDigest'],offset=snapshot['offset'],take=25,returnedCount=len(snapshot['entries']),complete=snapshot['complete'])))
    assert type(rows) is list and len(rows)==len(expected)
    ids=set();traces=set();observed=[]
    for row in rows:
        assert row['actor_subject']==subject and row['target_type']=='container' and row['target_id']==baseline['containerId']
        assert row['result']=='Validated' and row['job_id'] is None and row['id'] not in ids
        trace=row['trace_id'];assert type(trace) is str and 0<len(trace)<=64 and trace not in traces
        ids.add(row['id']);traces.add(trace);detail=unique_json(row['detail_json']);assert type(detail) is dict
        # Encoding preserves scalar types (false is not 0); reject duplicate JSON keys above.
        observed.append((row['action'],json.dumps(detail,sort_keys=True,separators=(',',':'))))
    assert Counter(observed)==Counter((action,json.dumps(detail,sort_keys=True,separators=(',',':'))) for action,detail in expected)
