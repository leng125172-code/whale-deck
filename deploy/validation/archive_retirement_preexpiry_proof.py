"""Pure pre-expiry refusal proof; no host executor imports, I/O or mutations.

This proves a bounded before/after observation, not natural expiry, authorized
retirement, installation or immutable survival of a writable live copy.
"""
import hashlib
import json
import re
import stat
from acl_history_browser_proof import typed_equal


MAX_TIME_MS=253402300799999


def canonical(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()


def digest(domain,value):return hashlib.sha256(domain+canonical(value)).hexdigest().upper()


def integer(value,minimum,maximum):assert type(value) is int and minimum<=value<=maximum


def private_file(value):
    assert type(value) is dict and set(value)=={'identity','sha256'}
    identity=value['identity'];assert type(identity) is list and len(identity)==9 and all(type(n) is int for n in identity)
    assert stat.S_ISREG(identity[2]) and stat.S_IMODE(identity[2])==0o600 and identity[3:6]==[0,0,1]
    assert type(value['sha256']) is str and re.fullmatch('[a-f0-9]{64}',value['sha256'])
    return identity


def raw_record_proof(value,record):
    identity=private_file(value);raw=json.dumps(record,separators=(',',':')).encode()
    assert identity[6]==len(raw) and value['sha256']==hashlib.sha256(raw).hexdigest()


def preexpiry_refusal_proof(value,request,archive_root):
    assert type(value) is dict and set(value)=={'request','clockBeforeUnixMs','clockAfterUnixMs','elapsedMonotonicNs','error','journalBefore','journalAfter','markerBefore','markerAfter','protectedBefore','protectedAfter','retirementRootsBefore','retirementRootsAfter'}
    typed_equal(value['request'],request)
    assert type(request) is dict and set(request)=={'operationId','owner','plan'}
    assert type(request['operationId']) is str and re.fullmatch('[a-f0-9]{32}',request['operationId'])
    assert type(request['owner']) is str and re.fullmatch('[A-F0-9]{64}',request['owner'])
    p=request['plan'];command=p['command'];archive=command['archive'];ap=archive['plan'];purge=ap['command']['purge'];ref=purge['plan']['command']['source'];source_id=ref['operationId']
    assert len({request['operationId'],archive['operationId'],purge['operationId'],source_id})==4
    integer(p['schemaVersion'],1,1);assert command['mode']=='ArchiveData';integer(command['retainDays'],1,3650)
    assert p['fingerprint']==digest(b'whaledeck-container-recycle-archive-retention-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})
    integer(p['historyCreatedAtUnixMs'],0,MAX_TIME_MS);integer(p['notBeforeUnixMs'],0,MAX_TIME_MS)
    assert p['notBeforeUnixMs']==p['historyCreatedAtUnixMs']+command['retainDays']*86400000
    integer(value['clockBeforeUnixMs'],p['historyCreatedAtUnixMs'],p['notBeforeUnixMs']-1)
    integer(value['clockAfterUnixMs'],value['clockBeforeUnixMs'],p['notBeforeUnixMs']-1)
    integer(value['elapsedMonotonicNs'],0,120000000000)
    assert p['retentionElapsed'] is p['retirementEligible'] is False and p['bindingCurrent'] is True
    assert value['error']=='CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_ELIGIBLE'
    for field in ('journal','marker','protected','retirementRoots'):typed_equal(value[field+'Before'],value[field+'After'])
    journal=dict(schemaVersion=1,**request,state='Reserved');typed_equal(value['journalBefore'],journal)
    marker=dict(schemaVersion=1,archive=archive,retirementOperationId=request['operationId'],retirementOwner=request['owner'],retirementFingerprint=p['fingerprint'],state='Reserved');typed_equal(value['markerBefore'],marker)
    roots=value['retirementRootsBefore'];op=request['operationId']
    assert set(roots)=={op+'.json',op+'.lock','claims/'+source_id+'.json'}
    # Compare actual read JSON order to its physical bytes; the expected
    # semantic record above may have a different insertion order.
    raw_record_proof(roots[op+'.json'],value['journalBefore']);raw_record_proof(roots['claims/'+source_id+'.json'],value['markerBefore'])
    assert private_file(roots[op+'.lock'])[6]==0 and roots[op+'.lock']['sha256']==hashlib.sha256(b'').hexdigest()
    assert archive_root['state']=='Completed';typed_equal({k:archive_root[k] for k in ('operationId','owner','plan')},archive)
    assert p['archiveRecordDigest']==digest(b'archive-retention-copy-v1\n',archive_root)
    typed_equal(p['publishedIdentity'],archive_root['publishedIdentity'])
    for key in ('sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','storeIdentity','archiveIdentity','archiveBytes','archiveSha256'):typed_equal(p[key],ap[key])
    protected=value['protectedBefore'];assert set(protected)=={'archive','lifecycles','namespace'}
    raw_record_proof(protected['archive'][archive['operationId']+'.json'],archive_root)
    data=protected['lifecycles']['hdd'][source_id+'.data']
    typed_equal(data['identity'],p['archiveIdentity']);assert data['sha256']==p['archiveSha256'] and data['sizeBytes']==p['archiveBytes'] and data['attributes']==[]
    raw=protected['lifecycles']['restore']['claims/'+source_id+'.json']
    claim=dict(schemaVersion=1,source=ref,restoreOperationId=archive['operationId'],restoreOwner=archive['owner'],restoreFingerprint=ap['fingerprint'])
    raw_record_proof(raw,claim);assert p['claimDigest']==digest(b'archive-retention-claim-v1\n',claim)
    live=protected['namespace'][ref['plan']['command']['path'].rsplit('/',1)[-1]]
    assert live['kind']=='file';typed_equal(live['identity'],archive_root['publishedIdentity'])
    assert live['sha256']==p['archiveSha256'] and live['sizeBytes']==p['archiveBytes'];typed_equal(live['attributes'],ref['plan']['attributes'])
    return dict(preexpiryRefusal='passed',dataDeletionProved=False,naturalExpiryProved=False)
