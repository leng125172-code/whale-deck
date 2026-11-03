"""Strict independent ACL history JSON and durable audit proofs; no mutations."""
import hashlib
import json
import uuid

def digest(value): return hashlib.sha256(value.encode()).hexdigest().upper()

def typed_equal(value, expected):
    assert type(value) is type(expected)
    if type(expected) is dict:
        assert value.keys() == expected.keys()
        for key in expected: typed_equal(value[key], expected[key])
    elif type(expected) is list:
        assert len(value) == len(expected)
        for actual, wanted in zip(value, expected, strict=True): typed_equal(actual, wanted)
    else: assert value == expected

def expected_entry(result, metadata):
    plan = metadata['plan']
    capture = uuid.UUID(result['executionJobId']).hex
    assert metadata['operationId'] == capture and metadata['historyDigest'] == result['historyDigest']
    return dict(captureId=capture, historyDigest=metadata['historyDigest'], createdAtUnixMs=metadata['createdAtUnixMs'],
                sourceInstanceId=plan['command']['instanceId'], changedAclType=plan['command']['aclType'], kind=plan['kind'],
                originalMode=plan['sourceIdentity'][2] & 0o777, accessEntryCount=len(plan['accessAcl']), defaultEntryCount=len(plan['defaultAcl']))

def prove_snapshot(signal, baseline, result, metadata):
    location = dict(containerId=baseline['containerId'], instanceId=baseline['instanceId'], path=result['command']['location']['path'])
    expected = expected_entry(result, metadata)
    assert signal['captureId'] == expected['captureId'] and signal['subject']
    typed_equal(signal['snapshot'], dict(location=location, entry=expected, accessAcl=metadata['plan']['accessAcl'], defaultAcl=metadata['plan']['defaultAcl']))
    request = signal['request']
    assert set(request) == {'method', 'pathname', 'query', 'status', 'noStore'} and request['method'] == 'GET' and request['status'] == 200 and request['noStore'] is True
    assert request['pathname'] == '/api/v1/containers/'+baseline['containerId']+'/files/acls/history/'+expected['captureId']
    assert request['query'] == dict(instanceId=location['instanceId'], path=location['path'], expectedHistoryDigest=expected['historyDigest'])
    for key, value in expected.items(): assert type(signal['snapshot']['entry'][key]) is type(value)
    return expected

def prove_audits(rows, subject, baseline, pages, snapshots):
    assert type(rows) is list and len(rows) == len(pages)+len(snapshots)
    expected = []
    for page in pages:
        expected.append(('containers.files.acl-history-list', page['location']['path'], page['version']))
    for snapshot in snapshots:
        expected.append(('containers.files.acl-history-read', snapshot['location']['path'], snapshot['entry']['historyDigest']))
    observed = []; ids = set(); traces = set()
    for row in rows:
        assert row['actor_subject'] == subject and row['target_type'] == 'container' and row['target_id'] == baseline['containerId']
        assert row['result'] == 'Validated' and row['job_id'] is None and row['id'] not in ids and row['trace_id'] not in traces
        assert type(row['trace_id']) is str and 0 < len(row['trace_id']) <= 64
        ids.add(row['id']); traces.add(row['trace_id'])
        detail = row['detail_json']
        if type(detail) is str:
            def unique(pairs):
                value={}
                for key,item in pairs:
                    assert key not in value
                    value[key]=item
                return value
            detail=json.loads(detail,object_pairs_hook=unique)
        assert type(detail) is dict
        assert set(detail) == {'pathHash', 'instanceHash', 'version', 'sizeBytes', 'sha256'}
        assert detail['instanceHash'] == digest(baseline['instanceId']) and detail['sizeBytes'] is detail['sha256'] is None
        observed.append((row['action'], detail['pathHash'], detail['version']))
    assert sorted(observed) == sorted((action, digest(path), version) for action, path, version in expected)

def prove_pages(pages, baseline, expected):
    by_path = {}
    for request in pages:
        assert request['method'] == 'GET' and request['status'] == 200 and request['noStore'] is True
        page = request['page']; path = page['location']['path']
        assert page['location'] == dict(containerId=baseline['containerId'], instanceId=baseline['instanceId'], path=path)
        assert set(page) == {'location','version','entries','nextAfter','scannedCount','invalidCount','complete'}
        assert type(page['complete']) is bool and page['complete'] == (page['nextAfter'] == '')
        assert type(page['scannedCount']) is int and type(page['invalidCount']) is int and 0 <= page['invalidCount'] <= page['scannedCount'] <= 200
        assert len(page['entries']) <= 25 and len(page['entries'])+page['invalidCount'] <= page['scannedCount']
        group = by_path.setdefault(path, []); after = group[-1]['nextAfter'] if group else ''
        assert not group or not group[-1]['complete'] and page['version'] == group[0]['version']
        assert request['pathname'] == '/api/v1/containers/'+baseline['containerId']+'/files/acls/history'
        assert request['query'] == dict(instanceId=baseline['instanceId'],path=path,after=after,take='25',expectedVersion=group[0]['version'] if group else '')
        previous = after
        for entry in page['entries']:
            assert entry['captureId'] > previous and expected[entry['captureId']][0] == path and entry == expected[entry['captureId']][1]
            typed_equal(entry, expected[entry['captureId']][1])
            previous = entry['captureId']
        if not page['complete']: assert page['nextAfter'] >= previous and page['nextAfter'] > after
        group.append(page)
    for path, group in by_path.items():
        assert group[-1]['complete']
        captures = [e['captureId'] for p in group for e in p['entries']]
        assert len(captures) == len(set(captures)) and set(captures) == {c for c,(p,_) in expected.items() if p == path}
    assert set(by_path) == {p for p,_ in expected.values()}
