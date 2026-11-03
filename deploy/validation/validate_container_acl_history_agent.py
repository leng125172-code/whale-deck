#!/usr/bin/env python3
"""Installed signed history reads over NEW owned ACL captures; no restore or UI.

Reuses the physical ACL fixture and its independent archival checks, then reads
every preimage via the new UDS service before the original exact cleanup.
"""
import hashlib
import json
import os
import stat
import subprocess

import validate_container_acl_agent as acl

NAMES = ('whaledeck-container-acl-history.py', 'whaledeck-container-acl-history-transport.py')
ENTRY_FIELDS = {'captureId', 'historyDigest', 'createdAtUnixMs', 'sourceInstanceId',
                'changedAclType', 'kind', 'originalMode', 'accessEntryCount', 'defaultEntryCount'}


class Probe(acl.Probe):
    def __init__(self, binary, evidence, header):
        self.error = (evidence / 'history-rpc.stderr').open('wb')
        self.transcript = evidence / 'history-rpc.jsonl'
        self.buffer = b''
        self.process = subprocess.Popen([str(binary), 'container-acl-history'], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=self.error)
        try:
            self.send(header)
            ready = self.receive()
            assert ready['kind'] == 'ready' and ready['revision'] == header['revision'] and ready['protocolMinor'] >= 52
        except BaseException:
            self.close(False)
            raise


def entry_proof(entry, row):
    assert type(entry) is dict and set(entry) == ENTRY_FIELDS
    plan, result = row['request']['plan'], row['result']
    assert entry['captureId'] == row['request']['operationId'] and entry['historyDigest'] == result['historyDigest']
    assert entry['sourceInstanceId'] == plan['command']['instanceId']
    assert entry['changedAclType'] == plan['command']['aclType'] and entry['kind'] == plan['kind']
    assert type(entry['originalMode']) is int and entry['originalMode'] == plan['sourceIdentity'][2] & 0o777
    assert type(entry['accessEntryCount']) is int and entry['accessEntryCount'] == len(plan['accessAcl'])
    assert type(entry['defaultEntryCount']) is int and entry['defaultEntryCount'] == len(plan['defaultAcl'])
    assert 0 <= acl.number(entry['createdAtUnixMs']) <= 253402300799999


def snapshot_proof(snapshot, row, location):
    assert type(snapshot) is dict and set(snapshot) == {'location', 'entry', 'accessAcl', 'defaultAcl'}
    assert snapshot['location'] == location
    entry_proof(snapshot['entry'], row)
    assert acl.managed_entries(snapshot['accessAcl']) == row['request']['plan']['accessAcl']
    assert acl.managed_entries(snapshot['defaultAcl']) == row['request']['plan']['defaultAcl']


def history_bytes(storage, rows):
    result = {}
    with storage.target(create_namespace=False, read_only=True) as (configuration, base):
        directory = storage.f.open_at(base, 'container-acl-v1', os.O_RDONLY | os.O_DIRECTORY)
        try:
            storage.attest(configuration, directory, read_only=True)
            for row in rows:
                if row['result']['state'] != 'Completed': continue
                operation = row['request']['operationId']
                fd = storage.f.open_at(directory, operation + '.json', os.O_RDONLY)
                try:
                    before = os.fstat(fd)
                    assert stat.S_ISREG(before.st_mode) and before.st_uid == 0 and before.st_nlink == 1 and stat.S_IMODE(before.st_mode) == 0o600 and before.st_size <= 16384
                    storage.attest(configuration, fd, read_only=True)
                    data = b''
                    while len(data) <= 16384:
                        chunk = os.read(fd, min(4096, 16385 - len(data)))
                        if not chunk: break
                        data += chunk
                    after = os.fstat(fd)
                    identity = lambda info: (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
                                              info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns)
                    assert identity(before) == identity(after) and len(data) == before.st_size
                    result[operation] = dict(identity=identity(before), sha256=hashlib.sha256(data).hexdigest())
                finally: os.close(fd)
        finally: os.close(directory)
    return result


def exercise(binary, evidence, header, rows, observe, archive, storage):
    # Observe all bytes/inodes/ACLs and all immutable Root/HDD preimages again
    # after EVERY successful RPC response (including its internal negatives).
    baseline = observe()
    original_hdd = history_bytes(storage, rows)
    (evidence / 'history-hdd-before.json').write_text(json.dumps(original_hdd))
    def unchanged():
        assert observe() == baseline
        assert history_bytes(storage, rows) == original_hdd
        for row in rows:
            record, sha = archive(row['request']['operationId'])
            assert sha == row['rootSha256'] and record['plan'] == row['request']['plan']
    unchanged()
    probe = Probe(binary, evidence, header)
    pages, snapshots = [], []
    try:
        for path in ('work/original.bin', 'work/acl-file.bin', 'work/acl-directory'):
            expected = {r['request']['operationId']: r for r in rows if r['result']['state'] == 'Completed'
                        and r['request']['plan']['command']['path'] == path}
            after, version, seen = '', '', []
            for index in range(100):
                boundaries = path == 'work/acl-file.bin' and index == 0
                response = probe.call(dict(action='list', path=path, after=after, take=1,
                                           expectedVersion=version, boundaries=boundaries))
                assert response['kind'] == 'page' and response['boundaryRefusals'] == (19 if boundaries else 0)
                page = json.loads(response['page'])
                assert set(page) == {'location', 'version', 'entries', 'nextAfter', 'scannedCount', 'invalidCount', 'complete'}
                assert page['location'] == dict(containerId=header['containerId'], instanceId=header['instanceId'], path=path)
                assert type(page['complete']) is bool and type(page['scannedCount']) is int and type(page['invalidCount']) is int
                assert 0 <= page['invalidCount'] <= page['scannedCount'] <= 200 and len(page['entries']) <= 1
                assert not version or version == page['version']
                for entry in page['entries']:
                    assert entry['captureId'] in expected and entry['captureId'] not in seen
                    entry_proof(entry, expected[entry['captureId']]); seen.append(entry['captureId'])
                pages.append(response); unchanged()
                (evidence / 'history-pages.json').write_text(json.dumps(pages))
                if page['complete']:
                    assert not page['nextAfter']
                    break
                assert page['nextAfter'] > after
                after, version = page['nextAfter'], page['version']
            else:
                raise AssertionError('HISTORY_PAGINATION_INCOMPLETE')
            assert set(seen) == set(expected)
        for row in rows:
            if row['result']['state'] != 'Completed':
                continue
            command = row['request']['plan']['command']
            response = probe.call(dict(action='read', path=command['path'], captureId=row['request']['operationId'],
                                       historyDigest=row['result']['historyDigest'], boundaries=not snapshots))
            assert response['kind'] == 'snapshot' and response['boundaryRefusals'] == (10 if not snapshots else 0)
            snapshot_proof(json.loads(response['snapshot']), row,
                           dict(containerId=command['containerId'], instanceId=command['instanceId'], path=command['path']))
            snapshots.append(response); unchanged()
            (evidence / 'history-snapshots.json').write_text(json.dumps(snapshots))
        closing = probe; probe = None; closing.close(True); unchanged()
    finally:
        if probe is not None:
            probe.close(False)
    result = dict(installedAclHistoryRpc='passed', pages=len(pages), snapshots=len(snapshots),
                  listBoundaryRefusals=19, readBoundaryRefusals=10, sourceAndRootAndHddUnchanged=True,
                  emptyPathProven=True, exactPreimages=True, humanBffOrUi=False, restorationExecuted=False)
    (evidence / 'history-hdd-after.json').write_text(json.dumps(history_bytes(storage, rows)))
    (evidence / 'history-verified.json').write_text(json.dumps(result))
    return result


if __name__ == '__main__':
    acl.main(history=True)
