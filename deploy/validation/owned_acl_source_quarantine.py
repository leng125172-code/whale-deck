"""Opt-in QA primitive: hide ONLY a newly verified own ACL capture, then restore it.

No CLI, installed helper, sudo grant, SQL edit, metadata replacement, unlink of
captures or product rollback. Callers must first verify the real capture through
the existing physical/SQL/Root/HDD proof. This module additionally binds that
proof to a before-capture catalogue. A private durable journal permits an exact
restore after the QA process exits. Neither an occupied destination nor an
unproved original is ever overwritten. Source ctime and directory timestamps
necessarily change; all OLD capture bytes and nine-field identities must match.
"""
import contextlib
import ctypes
import hashlib
import json
import os
import re
import stat
import uuid


CAPTURE = re.compile(r'[a-f0-9]{32}\.json')
LIMIT = 10000
MAXIMUM = 16384
JOURNAL = 'source-quarantine.json'
DIRECTORY_JOURNAL = 'source-quarantine-directory.json'
LIBC = ctypes.CDLL(None, use_errno=True)
LIBC.renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
LIBC.renameat2.restype = ctypes.c_int


def frame(info):
    return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
            info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns]


def private_directory(fd):
    info = os.fstat(fd)
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0
    assert stat.S_IMODE(info.st_mode) == 0o700, 'QA_SOURCE_DIRECTORY_UNSAFE'
    return frame(info)[:5]


def unique_json(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            assert key not in result, 'QA_SOURCE_DUPLICATE_JSON_KEY'
            result[key] = value
        return result
    def invalid_constant(_):
        raise AssertionError('QA_SOURCE_INVALID_JSON_NUMBER')
    return json.loads(raw, object_pairs_hook=unique, parse_constant=invalid_constant)


def read_file(fd, name, maximum=MAXIMUM):
    child = os.open(name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=fd)
    try:
        info = os.fstat(child)
        assert stat.S_ISREG(info.st_mode) and info.st_uid == info.st_gid == 0
        assert stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1
        assert 0 < info.st_size <= maximum, 'QA_SOURCE_METADATA_UNSAFE'
        raw = os.read(child, maximum + 1)
        assert len(raw) == info.st_size and frame(os.fstat(child)) == frame(info)
        assert frame(os.stat(name, dir_fd=fd, follow_symlinks=False)) == frame(info)
        return dict(frame=frame(info), sha256=hashlib.sha256(raw).hexdigest()), raw
    finally:
        os.close(child)


def catalogue(fd):
    """Bounded, read-only byte + nine-field baseline, taken BEFORE new capture."""
    private_directory(fd)
    before = frame(os.fstat(fd))
    result = {}
    names = os.listdir(fd)
    assert len(names) <= LIMIT + 64, 'QA_SOURCE_CATALOGUE_LIMIT'
    for name in sorted(names):
        if CAPTURE.fullmatch(name):
            assert len(result) < LIMIT
            result[name] = read_file(fd, name)[0]
    assert frame(os.fstat(fd)) == before, 'QA_SOURCE_CATALOGUE_CHANGED'
    return result


@contextlib.contextmanager
def installed_namespace(storage):
    """Only fixed installed storage; recheck mount AND canonical namespace inode."""
    with storage.target(create_namespace=False) as (configuration, base):
        fd = storage.f.open_at(base, 'container-acl-v1', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            original = private_directory(fd)
            def attest(descriptor):
                assert descriptor == fd and private_directory(descriptor) == original
                info = os.stat('container-acl-v1', dir_fd=base, follow_symlinks=False)
                assert frame(info)[:5] == original, 'QA_SOURCE_CANONICAL_NAMESPACE_CHANGED'
                storage.attest(configuration, descriptor, read_only=False)
            attest(fd)
            yield fd, attest
        finally:
            os.close(fd)


def move_without_replace(source, name, target, leaf):
    if LIBC.renameat2(source, os.fsencode(name), target, os.fsencode(leaf), 1) != 0:
        raise OSError(ctypes.get_errno(), 'QA_SOURCE_RENAME_REFUSED')


def bind_capture(baseline, proof, sql, root_raw, hdd_raw, old):
    """Require a NEW normal completed capture with exact target/owner/proof."""
    result = proof['result']; row = sql['row']
    from validate_container_acl_browser import managed_plan
    operation = uuid.UUID(result['executionJobId']).hex
    assert str(uuid.UUID(result['id'])) == result['id']
    assert operation + '.json' not in old, 'QA_SOURCE_OLD_CAPTURE_REFUSED'
    assert result['name'] == 'access' and result['state'] == row['state'] == 'Succeeded'
    assert proof['hddRecords'] == 1 and proof['before'] != proof['after']
    assert row['id'] == result['id'] and row['execution_job_id'] == result['executionJobId']
    assert uuid.UUID(row['id']).hex != operation and row['restore_source_json'] is None
    assert row['actor_subject'] == baseline['subject'] and row['container_id'] == baseline['containerId']
    assert row['instance_id'] == baseline['instanceId'] and row['relative_path'] == 'work/acl-file.bin'
    assert row['completed_at_utc'] and row['root_started_at_utc'] and not row['publish_barrier']
    assert row['lease_token'] is row['lease_expires_at_utc'] is None
    assert row['acl_confirmed'] is row['history_verified'] is row['history_may_exist'] is True
    assert sql['pending'] == 0 and sql['accepted'] == sql['confirmed'] == 1
    assert sql['planJob']['state'] == sql['executionJob']['state'] == 'Succeeded'
    assert hashlib.sha256(root_raw).hexdigest() == proof['rootSha256']
    assert hashlib.sha256(hdd_raw).hexdigest() == proof['hddSha256']
    root = unique_json(root_raw); hdd = unique_json(hdd_raw)
    assert root['operationId'] == hdd['operationId'] == operation
    assert root['owner'] == hdd['owner'] == row['owner_hash']
    assert root['state'] == 'Completed' and root['aclConfirmed'] is True
    assert root['historyVerified'] is root['historyMayExist'] is True
    assert hdd['resourceType'] == 'ContainerAclMetadata' and hdd['storageId'] == 'database-platform.backup.hdd'
    assert type(hdd['schemaVersion']) is int and hdd['schemaVersion'] == 1
    assert set(hdd) == {'schemaVersion','resourceType','storageId','operationId','owner','plan','createdAtUnixMs','historyDigest'}
    assert root['plan'] == hdd['plan'] == managed_plan(unique_json(row['plan_json']))
    command = hdd['plan']['command']
    assert (command['containerId'], command['instanceId'], command['path'], command['aclType']) == (
        baseline['containerId'], baseline['instanceId'], 'work/acl-file.bin', 'access')
    assert root['historyDigest'] == hdd['historyDigest'] == row['history_digest']
    payload = {key: value for key, value in hdd.items() if key != 'historyDigest'}
    encoded = json.dumps(payload, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False).encode()
    assert hashlib.sha256(b'whaledeck-container-acl-history-v1\n' + encoded).hexdigest().upper() == hdd['historyDigest']
    return operation


class OwnedSourceQuarantine:
    """FD-scoped primitive; use only while unrelated product work is idle.

    namespace_fd must come from the installed FileBackups target/attestation,
    never a caller path. journal_fd is a new private evidence directory. An
    attestation callback is called before EVERY mutation, including recovery.
    Keep both FDs open. No mutation happens in construction.
    """
    def __init__(self, namespace_fd, journal_fd, attest):
        assert os.geteuid() == 0, 'QA_SOURCE_ROOT_REQUIRED'
        self.fd = namespace_fd; self.journal = journal_fd; self.attest = attest
        self.identity = private_directory(namespace_fd)
        self.journal_identity = private_directory(journal_fd)
        assert frame(os.fstat(namespace_fd))[:2] != frame(os.fstat(journal_fd))[:2], 'QA_SOURCE_JOURNAL_IS_NAMESPACE'

    def check(self):
        assert private_directory(self.fd) == self.identity, 'QA_SOURCE_NAMESPACE_CHANGED'
        assert private_directory(self.journal) == self.journal_identity, 'QA_SOURCE_JOURNAL_CHANGED'
        self.attest(self.fd)

    def arm(self, baseline, proof, sql, root_raw, old):
        self.check()
        capture = uuid.UUID(proof['result']['executionJobId']).hex
        name = capture + '.json'
        info, raw = read_file(self.fd, name)
        assert bind_capture(baseline, proof, sql, root_raw, raw, old) == capture
        assert catalogue(self.fd) == {**old, name: info}, 'QA_SOURCE_UNRELATED_CAPTURE_CHANGED'
        value = dict(schemaVersion=1, captureId=capture, name=name,
                     quarantine='.wd-qa-acl-source-' + capture, namespaceIdentity=self.identity,
                     original=info, old=old, sourceIntent=sql['row']['id'],
                     rootSha256=hashlib.sha256(root_raw).hexdigest())
        raw_journal = json.dumps(value, sort_keys=True, separators=(',', ':')).encode()
        assert len(raw_journal) <= 4 * 1024**2
        target = os.open(JOURNAL, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=self.journal)
        try:
            with os.fdopen(target, 'wb', closefd=False) as output:
                output.write(raw_journal); output.flush(); os.fsync(target)
        finally:
            os.close(target)
        os.fsync(self.journal)
        return value

    def state(self):
        self.check()
        value = unique_json(read_file(self.journal, JOURNAL, 4 * 1024**2)[1])
        assert set(value) == {'schemaVersion','captureId','name','quarantine','namespaceIdentity',
                              'original','old','sourceIntent','rootSha256'}
        assert type(value['schemaVersion']) is int and value['schemaVersion'] == 1
        capture = value['captureId']; assert type(capture) is str and re.fullmatch('[a-f0-9]{32}', capture)
        assert value['name'] == capture + '.json' and value['quarantine'] == '.wd-qa-acl-source-' + capture
        assert value['namespaceIdentity'] == self.identity and value['name'] not in value['old']
        assert len(value['old']) < LIMIT
        for name, info in {**value['old'], value['name']: value['original']}.items():
            assert CAPTURE.fullmatch(name) and set(info) == {'frame','sha256'}
            assert len(info['frame']) == 9 and all(type(n) is int for n in info['frame'])
            assert re.fullmatch('[a-f0-9]{64}', info['sha256'])
        return value

    def old_unchanged(self, value, *, present):
        current = catalogue(self.fd)
        expected = {**value['old']}
        if present:
            own = current.get(value['name']); assert own is not None
            self.same_original(own, value['original']); expected[value['name']] = own
        assert current == expected, 'QA_SOURCE_UNRELATED_CAPTURE_CHANGED'

    @staticmethod
    def same_original(actual, expected):
        assert actual['sha256'] == expected['sha256'] and actual['frame'][:8] == expected['frame'][:8], 'QA_SOURCE_ORIGINAL_CHANGED'

    def hold(self):
        value = self.state(); self.old_unchanged(value, present=True)
        assert read_file(self.fd, value['name'])[0] == value['original'], 'QA_SOURCE_CAPTURE_CHANGED_SINCE_ARM'
        self.check()
        os.mkdir(value['quarantine'], 0o700, dir_fd=self.fd)
        child = os.open(value['quarantine'], os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=self.fd)
        try:
            identity = private_directory(child)
            directory_proof = dict(schemaVersion=1,captureId=value['captureId'],identity=identity,
                armedSha256=read_file(self.journal,JOURNAL,4*1024**2)[0]['sha256'])
            target = os.open(DIRECTORY_JOURNAL,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600,dir_fd=self.journal)
            try:
                with os.fdopen(target,'wb',closefd=False) as output:
                    output.write(json.dumps(directory_proof,sort_keys=True,separators=(',',':')).encode())
                    output.flush();os.fsync(target)
            finally:os.close(target)
            os.fsync(self.journal);os.fsync(self.fd)
            move_without_replace(self.fd, value['name'], child, value['name'])
            os.fsync(child); os.fsync(self.fd)
            self.same_original(read_file(child, value['name'])[0], value['original'])
            self.old_unchanged(value, present=False)
        finally:
            os.close(child)
        return dict(captureId=value['captureId'], canonicalSourceAbsent=True,
                    oldCapturesUnchanged=len(value['old']), originalPreserved=True)

    def restore(self):
        """Durable-journal recovery, also usable by a NEW process; never overwrite."""
        value = self.state()
        try:
            child = os.open(value['quarantine'], os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=self.fd)
        except FileNotFoundError:
            self.old_unchanged(value, present=True)
            return dict(captureId=value['captureId'], originalRestored=True, alreadyPresent=True)
        try:
            identity = private_directory(child)
            directory_proof = unique_json(read_file(self.journal,DIRECTORY_JOURNAL)[1])
            assert set(directory_proof) == {'schemaVersion','captureId','identity','armedSha256'}
            assert type(directory_proof['schemaVersion']) is int and directory_proof['schemaVersion'] == 1
            assert directory_proof['captureId'] == value['captureId'] and directory_proof['identity'] == identity
            assert directory_proof['armedSha256'] == read_file(self.journal,JOURNAL,4*1024**2)[0]['sha256']
            names = os.listdir(child)
            if not names:
                self.old_unchanged(value, present=True)
            else:
                assert names == [value['name']], 'QA_SOURCE_QUARANTINE_FOREIGN_ENTRY'
                self.same_original(read_file(child, value['name'])[0], value['original'])
                self.old_unchanged(value, present=False)
                self.check()
                move_without_replace(child, value['name'], self.fd, value['name'])
                os.fsync(self.fd); os.fsync(child)
                self.old_unchanged(value, present=True)
        finally:
            os.close(child)
        os.rmdir(value['quarantine'], dir_fd=self.fd); os.fsync(self.fd)
        return dict(captureId=value['captureId'], originalRestored=True, alreadyPresent=False)

    @contextlib.contextmanager
    def hidden(self):
        # Restore even if hold moved successfully and its later proof failed.
        try:
            yield self.hold()
        finally:
            self.restore()
