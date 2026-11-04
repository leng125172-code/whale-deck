"""Pure installed source/restore Root-HDD and terminal outcome proof; never writes."""
import re
import hashlib
import stat

from recursive_acl_restore_rpc_proof import digest, fields, integer, manifest_proof, source_proof, file_version, child_id, acl_mode
from recursive_acl_history_rpc_proof import verify_metadata

ROOT = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'startedThrough', 'finishedThrough', 'historyMayExist', 'historyVerified', 'historyDigest', 'completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount', 'outcomeDigest'}
CHILD = {'schemaVersion', 'operationId', 'owner', 'plan', 'state', 'version', 'actualIdentity', 'aclConfirmed', 'historyMayExist', 'historyVerified', 'historyDigest'}
RECEIPT = {'operationId', 'containerId', 'instanceId', 'fingerprint', 'targetProof', 'state', 'bindingCurrent', 'replayed', 'completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount', 'historyMayExist', 'historyVerified', 'historyDigest', 'historyAvailable', 'outcomeDigest', 'aclType'}
COUNTS = ('completedCount', 'changedCount', 'abortedCount', 'notStartedCount', 'unknownCount')


def receipt_proof(receipt, request, root, *, restore, binding, replayed):
    fields(receipt, RECEIPT); plan = request['plan']; command = plan['command']
    assert receipt['operationId'] == request['operationId'] and receipt['containerId'] == command['containerId'] and receipt['instanceId'] == command['instanceId']
    assert receipt['fingerprint'] == plan['fingerprint'] and receipt['targetProof'] == plan['targetProof']
    assert receipt['aclType'] == (plan['restoreSource']['changedAclType'] if restore else command['aclType'])
    for name in ('bindingCurrent', 'replayed', 'historyMayExist', 'historyVerified', 'historyAvailable'): assert type(receipt[name]) is bool
    assert receipt['bindingCurrent'] is binding and receipt['replayed'] is replayed and receipt['historyAvailable'] is root['historyVerified']
    for key in ('state', 'historyMayExist', 'historyVerified', 'historyDigest', 'outcomeDigest', *COUNTS): assert receipt[key] == root[key]
    for key in COUNTS: integer(receipt[key], 0, plan['entryCount'])


def records_proof(row, bundle, *, restore, source=None, frame=None, attribute_digest):
    request = row['request']; fields(request, ('operationId', 'owner', 'plan'))
    operation = request['operationId']; owner = request['owner']; plan = request['plan']; n = plan['entryCount']; root = bundle['root']
    assert re.fullmatch('[a-f0-9]{32}', operation) and re.fullmatch('[A-F0-9]{64}', owner)
    fields(root, ROOT); integer(root['schemaVersion'], 1, 1)
    assert all(root[k] == request[k] for k in ('operationId', 'owner', 'plan'))
    for key in COUNTS: integer(root[key], 0, n)
    for key in ('historyMayExist', 'historyVerified'): assert type(root[key]) is bool
    metadata = bundle['metadata']; children = bundle['children']; before = row['before']; after = row['after']
    if restore: source_proof(plan, source, frame, before)
    assert root['state'] in ('Completed', 'Aborted')
    if root['state'] == 'Aborted':
        assert restore and root['startedThrough'] == root['finishedThrough'] == -1 and root['notStartedCount'] == n
        assert all(root[k] == 0 for k in COUNTS if k != 'notStartedCount')
        assert root['historyMayExist'] is root['historyVerified'] is False and root['historyDigest'] == root['outcomeDigest'] == ''
        assert metadata is None and children == [] and before == after
        receipt_proof(row['result'], request, root, restore=restore, binding=False, replayed=False)
        return 0
    assert root['startedThrough'] == root['finishedThrough'] == n - 1 and type(root['startedThrough']) is type(root['finishedThrough']) is int
    assert root['completedCount'] == n and root['changedCount'] == plan['changeCount'] and all(root[k] == 0 for k in ('abortedCount', 'notStartedCount', 'unknownCount'))
    assert root['historyMayExist'] is root['historyVerified'] is True and metadata['historyDigest'] == root['historyDigest']
    if restore:
        entries = manifest_proof(plan, metadata, source, before, attribute_digest, operation_id=operation, owner=owner)
    else:
        verify_metadata(metadata, row); entries = metadata['entries']
        assert plan['fingerprint'] == digest(b'whaledeck-container-recursive-acl-plan-v1\n', {k:v for k,v in plan.items() if k != 'fingerprint'})
    assert len(children) == len(entries) == n
    outcomes = []; prefix = 'acl-restore' if restore else 'acl'
    for index, (entry, pair) in enumerate(zip(entries, children, strict=True)):
        rel = entry['relativePath']; child = entry['plan']; cr = pair['root']; hdd = pair['hdd']
        sub = child_id(operation, index) if restore else hashlib.sha256(b'whaledeck-recursive-acl-entry-id-v1:' + operation.encode() + b':' + str(index).encode()).hexdigest()[:32]
        fields(cr, CHILD); fields(hdd, ('schemaVersion', 'resourceType', 'storageId', 'operationId', 'owner', 'plan', 'createdAtUnixMs', 'parentOperationId', 'manifestDigest', 'historyDigest'))
        for item in (cr, hdd):
            integer(item['schemaVersion'], 1, 1)
            assert item['operationId'] == sub and item['owner'] == owner and item['plan'] == child
        assert cr['state'] == 'Completed' and cr['aclConfirmed'] is cr['historyMayExist'] is cr['historyVerified'] is True
        integer(hdd['createdAtUnixMs'], 0, 9223372036854775807)
        assert hdd['resourceType'] == ('ContainerRecursiveAclRestoreEntryMetadata' if restore else 'ContainerRecursiveAclEntryMetadata')
        assert hdd['storageId'] == 'database-platform.backup.hdd' and hdd['parentOperationId'] == operation and hdd['manifestDigest'] == plan['manifestDigest']
        assert hdd['historyDigest'] == cr['historyDigest'] == digest(('whaledeck-container-recursive-' + prefix + '-entry-history-v1\n').encode(), {k:v for k,v in hdd.items() if k != 'historyDigest'})
        old = before[rel]; actual = after[rel]; selected = child['command']['aclType']; target = child['command']['entries']; identity = actual['identity']
        if not restore:
            fields(child, ('schemaVersion', 'command', 'targetProof', 'parentIdentity', 'parentVersion', 'uid', 'gid', 'fingerprint', 'kind', 'sourceIdentity', 'attributesDigest', 'accessAcl', 'defaultAcl', 'targetMode', 'noChange'))
            integer(child['schemaVersion'], 1, 1)
            path = plan['command']['path'] + ('/' + rel if rel else '')
            assert child['kind'] == ('Directory' if stat.S_ISDIR(old['identity'][2]) else 'File')
            assert child['command'] == dict(containerId=plan['command']['containerId'], instanceId=plan['command']['instanceId'], path=path, expectedVersion=file_version(plan['command'], path, old['identity']), aclType=selected, entries=target)
            if rel:
                parent = rel.rpartition('/')[0]; assert parent in before and stat.S_ISDIR(before[parent]['identity'][2])
                parent_path = plan['command']['path'] + ('/' + parent if parent else '')
                assert child['parentIdentity'] == before[parent]['identity'][:2] and child['parentVersion'] == file_version(plan['command'], parent_path, before[parent]['identity'])
            else:
                assert child['sourceIdentity'] == plan['rootIdentity'] and all(child[k] == plan[k] for k in ('parentIdentity', 'parentVersion'))
            kind = plan['command']['aclType']; file = child['kind'] == 'File'; expected_type = 'access' if file and kind == 'default' else kind
            expected_target = old['access'] if file and kind == 'default' else plan['command']['fileEntries' if file else 'directoryEntries']
            assert selected == expected_type and target == expected_target and child['sourceIdentity'] == old['identity']
            assert child['accessAcl'] == old['access'] and child['defaultAcl'] == old['default'] and child['attributesDigest'] == attribute_digest
            assert all(child[k] == plan[k] for k in ('targetProof', 'uid', 'gid'))
            assert child['fingerprint'] == digest(b'whaledeck-container-acl-plan-v1\n', {k:v for k,v in child.items() if k != 'fingerprint'})
            assert type(child['noChange']) is bool and child['noChange'] is (target == old[selected])
            assert child['targetMode'] == (acl_mode(target) if selected == 'access' else old['identity'][2] & 0o777)

        assert cr['actualIdentity'] == identity and type(identity) is list and len(identity) == 9 and all(type(v) is int for v in identity)
        assert identity[:2] == old['identity'][:2] and identity[3:8] == old['identity'][3:8] and identity[2] & ~0o777 == old['identity'][2] & ~0o777
        wanted = list(old['identity']); wanted[2] = wanted[2] & ~0o777 | child['targetMode']; assert identity[:8] == wanted[:8]
        assert actual['access'] == (target if selected == 'access' else old['access']) and actual['default'] == (target if selected == 'default' else old['default'])
        if child['noChange']: assert actual == old
        assert cr['version'] == file_version(plan['command'], child['command']['path'], identity)
        outcomes.append(dict(id=sub, state='Completed', version=cr['version'], changed=not child['noChange']))
    assert digest(('whaledeck-recursive-' + prefix + '-outcomes-v1\n').encode(), outcomes) == root['outcomeDigest']
    receipt_proof(row['result'], request, root, restore=restore, binding=True, replayed=False)
    return len(children)
