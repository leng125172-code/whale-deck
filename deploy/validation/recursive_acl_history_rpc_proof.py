"""Independent strict projection of recursive preimages; no mutation/RPC provider.

Plan counts describe a full capture, never the outcome of its execution.
Protobuf JSON int64 is canonical decimal text; uint32/boolean presence is exact.
"""
import hashlib
import json
import re

from verify_container_recursive_acl_agent import number

SUMMARY_FIELDS={'captureId','historyDigest','createdAtUnixMs','sourceInstanceId','changedAclType','manifestDigest','entryCount','fileCount','directoryCount','changeCount'}
LOCATION_FIELDS={'containerId','instanceId','path'}


def integer(value,low=0,high=4294967295):
    assert type(value) is int and low<=value<=high
    return value


def verify_metadata(value,row):
    assert type(value) is dict and set(value)=={'schemaVersion','resourceType','storageId','operationId','owner','plan','entries','createdAtUnixMs','historyDigest'}
    assert type(value['schemaVersion']) is int and value['schemaVersion']==1 and value['resourceType']=='ContainerRecursiveAclMetadata' and value['storageId']=='database-platform.backup.hdd'
    assert all(value[key]==row['request'][key] for key in ('operationId','owner','plan'))
    integer(value['createdAtUnixMs'],0,9223372036854775807)
    def digest(domain,item):return hashlib.sha256(domain+json.dumps(item,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper()
    assert digest(b'whaledeck-container-recursive-acl-history-v1\n',{k:v for k,v in value.items() if k!='historyDigest'})==value['historyDigest']==row['result']['historyDigest']
    entries=value['entries'];assert type(entries) is list and len(entries)==value['plan']['entryCount']
    assert digest(b'whaledeck-container-recursive-acl-manifest-v1\n',entries)==value['plan']['manifestDigest']
    paths=[entry['relativePath'] for entry in entries]
    assert len(paths)==len(set(paths)) and set(paths)==set(row['before'])
    assert paths==sorted(paths,key=lambda rel:(-(rel.count('/')+1 if rel else 0),rel.encode('utf-8')))
    for entry in entries:
        plan=entry['plan'];old=row['before'][entry['relativePath']]
        assert plan['sourceIdentity']==old['identity'] and plan['accessAcl']==old['access'] and plan['defaultAcl']==old['default']
    return value


def summary_proof(summary,row,metadata):
    assert type(summary) is dict and set(summary)==SUMMARY_FIELDS
    request=row['request'];plan=request['plan'];command=plan['command']
    assert summary['captureId']==request['operationId'] and re.fullmatch('[a-f0-9]{32}',summary['captureId'])
    assert summary['historyDigest']==row['result']['historyDigest']==metadata['historyDigest'] and re.fullmatch('[A-F0-9]{64}',summary['historyDigest'])
    assert summary['sourceInstanceId']==command['instanceId'] and summary['changedAclType']==command['aclType']
    assert summary['manifestDigest']==plan['manifestDigest']==metadata['plan']['manifestDigest'] and re.fullmatch('[A-F0-9]{64}',summary['manifestDigest'])
    assert number(summary['createdAtUnixMs'],0,9223372036854775807)==metadata['createdAtUnixMs']
    for key in ('entryCount','fileCount','directoryCount','changeCount'):
        assert integer(summary[key],0,10000)==plan[key]==metadata['plan'][key]
    assert 1<=summary['directoryCount']<=summary['entryCount']==summary['fileCount']+summary['directoryCount'] and summary['changeCount']<=summary['entryCount']
    if summary['changedAclType']=='default':assert summary['changeCount']<=summary['directoryCount']


def acl_proof(values):
    assert type(values) is list and len(values)<=64
    output=[];order={'user_obj':1,'user':2,'group_obj':4,'group':8,'mask':16,'other':32};keys=[]
    for value in values:
        assert type(value) is dict
        tag=value.get('tag');assert tag in order
        named=tag in ('user','group');assert set(value)==({'tag','id','permissions'} if named else {'tag','permissions'})
        qualifier=integer(value['id'],0,4294967294) if named else None
        permissions=integer(value['permissions'],0,7)
        keys.append((order[tag],qualifier if named else -1));output.append(dict(tag=tag,id=qualifier,permissions=permissions))
    assert keys==sorted(set(keys))
    if output:
        assert all(sum(v['tag']==t for v in output)==1 for t in ('user_obj','group_obj','other'))
        assert not any(v['tag'] in ('user','group') for v in output) or any(v['tag']=='mask' for v in output)
    return output


def snapshot_proof(snapshot,row,location,metadata,offset,take):
    assert type(snapshot) is dict and type(snapshot.get('complete')) is bool
    complete=snapshot['complete'];fields={'location','summary','offset','entries','complete'}| (set() if complete else {'nextOffset'})
    assert set(snapshot)==fields and snapshot['location']==location and set(location)==LOCATION_FIELDS
    assert integer(snapshot['offset'],0,9999)==offset and type(take) is int and 1<=take<=50
    summary_proof(snapshot['summary'],row,metadata)
    items=snapshot['entries'];assert type(items) is list and 1<=len(items)<=take
    end=offset+len(items);total=snapshot['summary']['entryCount'];assert end<=total and complete is (end==total)
    if not complete:assert integer(snapshot['nextOffset'],1,9999)==end
    expected=metadata['entries'];assert len(expected)==total
    for index,value in enumerate(items,offset):
        assert type(value) is dict and set(value)=={'relativePath','kind','originalMode','accessAcl','defaultAcl'}
        original=expected[index];relative=original['relativePath'];old=row['before'][relative];plan=original['plan']
        assert value['relativePath']==relative and value['kind']==plan['kind'] and integer(value['originalMode'],0,511)==old['identity'][2]&0o777
        assert plan['sourceIdentity']==old['identity'] and plan['accessAcl']==old['access'] and plan['defaultAcl']==old['default']
        access=acl_proof(value['accessAcl']);default=acl_proof(value['defaultAcl']);assert access==old['access'] and default==old['default']
        assert len(access)>=3 and (value['kind']!='File' or not default)
    return end


def page_proof(page,location,rows,metadata,after,take,version):
    assert type(page) is dict and set(page)=={'location','version','entries','nextAfter','scannedCount','invalidCount','complete'}
    assert page['location']==location and set(location)==LOCATION_FIELDS and re.fullmatch('[A-F0-9]{64}',page['version'])
    assert not version or version==page['version']
    scanned=integer(page['scannedCount'],0,200);invalid=integer(page['invalidCount'],0,scanned)
    assert type(page['entries']) is list and len(page['entries'])<=take and len(page['entries'])+invalid<=scanned
    assert type(page['complete']) is bool and type(page['nextAfter']) is str and page['complete'] is (not page['nextAfter'])
    if not page['complete']:assert re.fullmatch('[a-f0-9]{32}',page['nextAfter']) and page['nextAfter']>after
    previous=after;captures=[]
    for value in page['entries']:
        capture=value['captureId'];assert capture in rows and capture>previous and (not page['nextAfter'] or capture<=page['nextAfter'])
        summary_proof(value,rows[capture],metadata[capture]);captures.append(capture);previous=capture
    return captures


def verify_transcript(pages,snapshots,rows,metadata,location,header,transcript):
    expected={row['request']['operationId']:row for row in rows if row['result']['state']=='Completed'}
    assert len(expected)==7 and set(metadata)==set(expected)
    assert type(pages) is list and pages and type(snapshots) is list
    after='';version='';seen=[]
    for index,response in enumerate(pages):
        assert set(response)=={'kind','page','boundaryRefusals'} and response['kind']=='page'
        assert integer(response['boundaryRefusals'])==(21 if index==0 else 0)
        value=json.loads(response['page']);assert value['complete'] is (index==len(pages)-1)
        seen+=page_proof(value,location,expected,metadata,after,1,version);after=value['nextAfter'];version=value['version']
    assert seen==sorted(expected) and len(seen)==len(set(seen))
    position=0
    for capture in sorted(expected):
        offset=0
        while True:
            response=snapshots[position];assert set(response)=={'kind','snapshot','boundaryRefusals'} and response['kind']=='snapshot'
            assert integer(response['boundaryRefusals'])==(19 if position==0 else 0)
            value=json.loads(response['snapshot']);offset=snapshot_proof(value,expected[capture],location,metadata[capture],offset,1);position+=1
            if value['complete']:break
        assert offset==expected[capture]['request']['plan']['entryCount']==4
    assert position==len(snapshots)==28
    assert type(transcript) is list and len(transcript)==len(pages)+len(snapshots)+2
    first=transcript[0];assert set(first)=={'kind','revision','probeRevision','protocolMinor'}
    assert first['kind']=='ready' and first['revision']==header['revision'] and first['probeRevision']==header['probeRevision'] and integer(first['protocolMinor'],54)>=54
    assert transcript[1:-1]==pages+snapshots and transcript[-1]=={'kind':'done'}
    return dict(captures=7,entryPages=28,authorityRefusals=40,listingPages=len(pages),fullPreimagesIncludingZeroChange=True)
