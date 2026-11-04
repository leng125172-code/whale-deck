"""Independent, read-only restore wire/manifest proof. No RPC or mutation engine.

Source and restore captures have separate domains. A historical source change
count is never substituted for this restore's count; old inode IDs never become
the current target's identities. File/Root/HDD acquisition belongs to the gate.
"""
import hashlib
import json
import re
import stat

from recursive_acl_history_rpc_proof import integer, verify_metadata as source_metadata
from verify_container_recursive_acl_agent import file_version, acl_mode, entries as wire_acl

SUMMARY = {'captureId', 'historyDigest', 'createdAtUnixMs', 'sourceInstanceId', 'changedAclType', 'manifestDigest', 'entryCount', 'fileCount', 'directoryCount', 'changeCount'}
FRAME = ('device', 'inode', 'mode', 'ownerUid', 'ownerGid', 'linkCount', 'sizeBytes', 'modifiedAtUnixNs', 'changedAtUnixNs')
COUNTS = ('entryCount', 'fileCount', 'directoryCount', 'changeCount')
PLAN = {'schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'ownerUid', 'ownerGid', 'fingerprint', 'rootIdentity', 'rootChangedAtUnixNs', 'manifestDigest', *COUNTS, 'restoreSource', 'restoreSourceFrame', 'restoreTargetsDigest'}


def digest(domain, value):
    return hashlib.sha256(domain + json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest().upper()


def fields(value, expected):
    assert type(value) is dict and set(value) == set(expected)


def number(value, low=0, high=18446744073709551615):
    assert type(value) is int or type(value) is str and re.fullmatch('-?(0|[1-9][0-9]*)', value)
    result = int(value)
    assert low <= result <= high and (type(value) is int or str(result) == value)
    return result


def hash_value(value):
    assert type(value) is str and re.fullmatch('[A-F0-9]{64}', value)
    return value


def instance(value):
    assert type(value) is str and re.fullmatch(r'[1-9][0-9]*:[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z', value)
    return value


def counts(value, kind):
    n, f, d, c = [integer(value[k], 0, 10000) for k in COUNTS]
    assert 1 <= d <= n == f + d and c <= n
    assert kind in ('access', 'default') and (kind != 'default' or c <= d)


def proto_plan(value):
    fields(value, PLAN)
    command = value['command']; fields(command, ('location', 'captureId', 'expectedHistoryDigest', 'expectedVersion'))
    location = command['location']; fields(location, ('containerId', 'instanceId', 'path'))
    assert re.fullmatch('[a-f0-9]{64}', location['containerId']) and location['path'] == 'work/recursive-tree'
    instance(location['instanceId']); assert re.fullmatch('[a-f0-9]{32}', command['captureId'])
    for key in ('expectedHistoryDigest', 'expectedVersion'): hash_value(command[key])
    summary = dict(value['restoreSource']); fields(summary, SUMMARY)
    assert summary['captureId'] == command['captureId'] and summary['historyDigest'] == command['expectedHistoryDigest']
    hash_value(summary['manifestDigest']); instance(summary['sourceInstanceId'])
    summary['createdAtUnixMs'] = number(summary['createdAtUnixMs'], 0, 9223372036854775807)
    counts(summary, summary['changedAclType']); counts(value, summary['changedAclType'])
    assert all(summary[k] == value[k] for k in COUNTS[:3])
    source = value['restoreSourceFrame']; fields(source, FRAME)
    for key in ('mode', 'ownerUid', 'ownerGid'): integer(source[key], 0, 4294967294)
    frame = [number(source[k]) for k in FRAME[:7]] + [number(source[k], -9223372036854775808, 9223372036854775807) for k in FRAME[7:]]
    assert frame[1] > 0 and frame[2:6] == [stat.S_IFREG | 0o600, 0, 0, 1] and 0 < frame[6] <= 16 * 1024**2
    root = value['rootIdentity']; fields(root, FRAME[:8])
    for key in ('mode', 'ownerUid', 'ownerGid'): integer(root[key], 0, 4294967294)
    identity = [number(root[k]) for k in FRAME[:7]] + [number(root['modifiedAtUnixNs'], -9223372036854775808, 9223372036854775807), number(value['rootChangedAtUnixNs'], -9223372036854775808, 9223372036854775807)]
    assert identity[1] > 0 and stat.S_ISDIR(identity[2]) and identity[2] <= 65535 and not identity[2] & 0o7000
    assert identity[3] < 4294967295 and identity[4] < 4294967295 and identity[5] > 0 and identity[6] <= 9223372036854775807
    parent = value['parentIdentity']; fields(parent, ('device', 'inode')); parent = [number(parent[k]) for k in ('device', 'inode')]; assert parent[1] > 0
    integer(value['schemaVersion'], 1, 1)
    for key in ('ownerUid', 'ownerGid'): integer(value[key], 0, 4294967294)
    for key in ('targetProof', 'parentVersion', 'fingerprint', 'manifestDigest', 'restoreTargetsDigest'): hash_value(value[key])
    return dict(schemaVersion=1, command={**location, **{k:command[k] for k in ('captureId', 'expectedHistoryDigest', 'expectedVersion')}},
        targetProof=value['targetProof'], parentIdentity=parent, parentVersion=value['parentVersion'], uid=value['ownerUid'], gid=value['ownerGid'], fingerprint=value['fingerprint'],
        rootIdentity=identity, manifestDigest=value['manifestDigest'], **{k:value[k] for k in COUNTS}, restoreSource=summary, restoreSourceFrame=frame, restoreTargetsDigest=value['restoreTargetsDigest'])


def source_proof(plan, source, frame, before):
    assert type(frame) is list and len(frame) == 9 and all(type(v) is int for v in frame)
    row = source['request']; metadata = source_metadata(source['metadata'], source)
    expected = dict(captureId=row['operationId'], historyDigest=metadata['historyDigest'], createdAtUnixMs=metadata['createdAtUnixMs'],
        sourceInstanceId=row['plan']['command']['instanceId'], changedAclType=row['plan']['command']['aclType'], manifestDigest=row['plan']['manifestDigest'], **{k:row['plan'][k] for k in COUNTS})
    assert plan['restoreSource'] == expected and plan['restoreSourceFrame'] == frame
    assert plan['command']['containerId'] == row['plan']['command']['containerId'] and plan['command']['path'] == row['plan']['command']['path']
    assert plan['command']['captureId'] == row['operationId'] and plan['command']['expectedHistoryDigest'] == metadata['historyDigest']
    assert plan['rootIdentity'] == before['']['identity'] and plan['command']['expectedVersion'] == file_version(plan['command'], plan['command']['path'], before['']['identity'])
    assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-acl-restore-plan-v1\n', {k:v for k,v in plan.items() if k != 'fingerprint'})
    return metadata


def native_acl(value):
    assert type(value) is list
    for row in value: fields(row, ('tag', 'id', 'permissions'))
    actual = wire_acl({'entries':[{k:v for k,v in row.items() if v is not None} for row in value]}, empty=True)
    assert actual == value


def manifest_proof(plan, metadata, source, before, attribute_digest, *, operation_id, owner):
    fields(metadata, ('schemaVersion', 'resourceType', 'storageId', 'operationId', 'owner', 'plan', 'entries', 'createdAtUnixMs', 'historyDigest'))
    integer(metadata['schemaVersion'], 1, 1); integer(metadata['createdAtUnixMs'], 0, 9223372036854775807)
    assert metadata['resourceType'] == 'ContainerRecursiveAclRestoreMetadata' and metadata['storageId'] == 'database-platform.backup.hdd' and metadata['plan'] == plan
    assert re.fullmatch('[a-f0-9]{32}', operation_id) and re.fullmatch('[A-F0-9]{64}', owner)
    assert metadata['operationId'] == operation_id and metadata['owner'] == owner
    assert metadata['historyDigest'] == digest(b'whaledeck-container-recursive-acl-restore-history-v1\n', {k:v for k,v in metadata.items() if k != 'historyDigest'})
    entries = metadata['entries']; assert type(entries) is list and len(entries) == plan['entryCount']
    assert digest(b'whaledeck-container-recursive-acl-restore-manifest-v1\n', entries) == plan['manifestDigest']
    historical = {e['relativePath']:e['plan'] for e in source['metadata']['entries']}; paths = [e['relativePath'] for e in entries]
    assert paths == sorted(before, key=lambda rel:(-(rel.count('/') + 1 if rel else 0), rel.encode())) and set(paths) == set(historical)
    projection = []; files = 0; changes = 0; inodes = set()
    hash_value(attribute_digest)
    for entry in entries:
        fields(entry, ('relativePath', 'plan')); rel = entry['relativePath']; child = entry['plan']; old = before[rel]
        fields(child, ('schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'uid', 'gid', 'fingerprint', 'kind', 'sourceIdentity', 'attributesDigest', 'accessAcl', 'defaultAcl', 'targetMode', 'noChange'))
        integer(child['schemaVersion'], 1, 1)
        assert all(child[k] == plan[k] for k in ('targetProof', 'uid', 'gid'))
        identity = child['sourceIdentity']; assert type(identity) is list and len(identity) == 9 and all(type(v) is int for v in identity)
        inode = tuple(identity[:2]); assert inode not in inodes and inode[0] == plan['rootIdentity'][0]; inodes.add(inode)
        if rel:
            parent = rel.rpartition('/')[0]; assert parent in before and stat.S_ISDIR(before[parent]['identity'][2])
            parent_path = plan['command']['path'] + ('/' + parent if parent else '')
            assert child['parentIdentity'] == before[parent]['identity'][:2] and child['parentVersion'] == file_version(plan['command'], parent_path, before[parent]['identity'])
        else:
            assert child['kind'] == 'Directory' and child['sourceIdentity'] == plan['rootIdentity']
            assert all(child[k] == plan[k] for k in ('parentIdentity', 'parentVersion'))
        assert child['kind'] == historical[rel]['kind'] and child['sourceIdentity'] == old['identity'] and child['accessAcl'] == old['access'] and child['defaultAcl'] == old['default']
        assert child['kind'] in ('File', 'Directory') and child['attributesDigest'] == attribute_digest
        kind = plan['restoreSource']['changedAclType']; file = child['kind'] == 'File'; files += file
        selected = 'access' if file and kind == 'default' else kind
        target = old['access'] if file and kind == 'default' else historical[rel]['accessAcl' if selected == 'access' else 'defaultAcl']; native_acl(target)
        path = plan['command']['path'] + ('/' + rel if rel else '')
        assert child['command'] == dict(containerId=plan['command']['containerId'], instanceId=plan['command']['instanceId'], path=path, expectedVersion=file_version(plan['command'], path, old['identity']), aclType=selected, entries=target)
        assert child['fingerprint'] == digest(b'whaledeck-container-acl-plan-v1\n', {k:v for k,v in child.items() if k != 'fingerprint'})
        assert type(child['noChange']) is bool and child['noChange'] is (target == old[selected]); changes += not child['noChange']
        assert child['targetMode'] == (acl_mode(target) if selected == 'access' else old['identity'][2] & 0o777)
        projection.append(dict(relativePath=rel, kind=child['kind'], aclType=selected, entries=target))
    assert plan['fileCount'] == files and plan['directoryCount'] == len(entries) - files and plan['changeCount'] == changes
    assert digest(b'whaledeck-container-recursive-acl-restore-targets-v1\n', projection) == plan['restoreTargetsDigest']
    return entries


def child_id(operation, index):
    assert re.fullmatch('[a-f0-9]{32}', operation) and type(index) is int and 0 <= index < 10000
    return hashlib.sha256(b'whaledeck-recursive-acl-restore-entry-id-v1:' + operation.encode() + b':' + str(index).encode()).hexdigest()[:32]


def ready_proof(value, header):
    fields(value, ('kind', 'revision', 'probeRevision', 'protocolMinor'))
    assert value['kind'] == 'ready' and value['revision'] == header['revision'] and value['probeRevision'] == header['probeRevision']
    integer(value['protocolMinor'], 55, 4294967295)
