#!/usr/bin/env python3
"""Verified, bounded ACL metadata reads; no CLI, writes or restoration grant.

Uses the existing attested container-acl-v1 store. An authenticated adapter must
provide the pinned current ContainerRoot. Historical owner/plan stay private.
"""
import contextlib
import hashlib
import json
import os
import re
import stat
import time

MAX_RECORDS = 10000
SCAN_LIMIT = 200
PAGE_LIMIT = 50
RESPONSE_BYTES = 128 * 1024
SECONDS = 5
HASH = re.compile('[A-F0-9]{64}')
CAPTURE = re.compile('[a-f0-9]{32}')


def encode(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False, allow_nan=False).encode()


class ContainerAclHistoryReader:
    def __init__(self, acl):
        self.a, self.h, self.f, self.r = acl, acl.history, acl.f, acl.r
        if self.h.NAMESPACE != 'container-acl-v1' or self.h.DOMAIN != b'whaledeck-container-acl-history-v1\n':
            raise ValueError('CONTAINER_ACL_HISTORY_ENGINE_MISMATCH')

    def reject(self, suffix):
        raise self.r.Rejected('CONTAINER_ACL_HISTORY_' + suffix)

    def scope(self, request, session, fields):
        if type(request) is not dict or set(request) != fields: self.reject('REQUEST_INVALID')
        self.r.target_binding(request['containerId'], request['instanceId'])
        self.r.canonical_path(request['path'], empty=False)
        if self.r.protected(request['path'], self.f): self.reject('PROTECTED')
        if session is None: self.reject('SESSION_REQUIRED')
        return self.a.guard(request, session)

    def current(self, request, session, proof, deadline):
        if time.monotonic() > deadline: self.reject('TIMEOUT')
        session.current()
        self.a.guard(request, session, proof)

    @staticmethod
    def frame(info):
        # Reading a file can change atime; it is never an authority token.
        return [info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_gid,
            info.st_nlink, info.st_size, info.st_mtime_ns, info.st_ctime_ns]

    @contextlib.contextmanager
    def namespace(self):
        # Only absence of this child under an attested base means no history.
        # Missing mount/base/configuration propagates; never create on reads.
        with self.h.storage.target(create_namespace=False, read_only=True) as (configuration, base):
            try: fd = self.f.open_at(base, self.h.NAMESPACE, os.O_RDONLY | os.O_DIRECTORY)
            except FileNotFoundError:
                yield configuration, base, None
                return
            try:
                info = os.fstat(fd)
                if (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)) != (0, 0, 0o700): self.reject('UNSAFE')
                self.h.storage.attest(configuration, fd, read_only=True)
                yield configuration, base, fd
            finally: os.close(fd)

    def inventory(self, descriptor, deadline):
        directory = self.frame(os.fstat(descriptor))
        rows = []
        with os.scandir(descriptor) as entries:
            for index, entry in enumerate(entries):
                if index >= MAX_RECORDS + 64: self.reject('LIMIT')
                if time.monotonic() > deadline: self.reject('TIMEOUT')
                if re.fullmatch('[a-f0-9]{32}\\.json', entry.name):
                    if len(rows) >= MAX_RECORDS: self.reject('LIMIT')
                    rows.append([entry.name, self.frame(entry.stat(follow_symlinks=False))])
        if self.frame(os.fstat(descriptor)) != directory: self.reject('VERSION_CONFLICT')
        return [directory, sorted(rows)]

    def metadata(self, descriptor, capture, expected_frame=None):
        self.f.identity(capture)
        name = capture + '.json'
        before = self.frame(os.stat(name, dir_fd=descriptor, follow_symlinks=False))
        if expected_frame is not None and before != expected_frame: self.reject('VERSION_CONFLICT')
        value = self.h.storage.read_json(descriptor, name)
        fields = {'schemaVersion', 'resourceType', 'storageId', 'operationId', 'owner', 'plan', 'createdAtUnixMs', 'historyDigest'}
        if set(value) != fields or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1 or \
                value['resourceType'] != 'ContainerAclMetadata' or value['storageId'] != 'database-platform.backup.hdd' or \
                value['operationId'] != capture or type(value['createdAtUnixMs']) is not int or not 0 <= value['createdAtUnixMs'] <= 9223372036854775807 or \
                type(value['historyDigest']) is not str or not HASH.fullmatch(value['historyDigest']): self.reject('INVALID')
        # Full original request/plan validation includes IDs, path protection,
        # explicit ACL structure/mask, inode version and original fingerprint.
        self.a.request({key: value[key] for key in ('operationId', 'owner', 'plan')})
        digest = hashlib.sha256(self.h.DOMAIN + encode({key: item for key, item in value.items() if key != 'historyDigest'})).hexdigest().upper()
        if digest != value['historyDigest']: self.reject('INVALID')
        if self.frame(os.stat(name, dir_fd=descriptor, follow_symlinks=False)) != before: self.reject('VERSION_CONFLICT')
        return value

    @staticmethod
    def matches(value, request):
        old = value['plan']['command']
        return old['containerId'] == request['containerId'] and old['path'] == request['path']

    @staticmethod
    def summary(value):
        plan = value['plan']
        return dict(captureId=value['operationId'], historyDigest=value['historyDigest'],
            createdAtUnixMs=value['createdAtUnixMs'], sourceInstanceId=plan['command']['instanceId'],
            changedAclType=plan['command']['aclType'], kind=plan['kind'], originalMode=plan['sourceIdentity'][2] & 0o777,
            accessEntryCount=len(plan['accessAcl']), defaultEntryCount=len(plan['defaultAcl']))

    def read(self, request, session):
        proof = self.scope(request, session, {'containerId', 'instanceId', 'path', 'captureId', 'expectedHistoryDigest'})
        if type(request['captureId']) is not str or not CAPTURE.fullmatch(request['captureId']) or \
                type(request['expectedHistoryDigest']) is not str or not HASH.fullmatch(request['expectedHistoryDigest']): self.reject('REQUEST_INVALID')
        deadline = time.monotonic() + SECONDS
        self.current(request, session, proof, deadline)
        with self.namespace() as (configuration, base, descriptor):
            if descriptor is None: self.reject('NOT_FOUND')
            try: value = self.metadata(descriptor, request['captureId'])
            except FileNotFoundError: self.reject('NOT_FOUND')
            if not self.matches(value, request): self.reject('NOT_FOUND')
            if value['historyDigest'] != request['expectedHistoryDigest']: self.reject('VERSION_CONFLICT')
            self.h.storage.attest(configuration, descriptor, read_only=True)
            self.current(request, session, proof, deadline)
            return {**self.summary(value), 'containerId': request['containerId'], 'instanceId': request['instanceId'],
                'path': request['path'], 'accessAcl': json.loads(encode(value['plan']['accessAcl'])),
                'defaultAcl': json.loads(encode(value['plan']['defaultAcl']))}

    def listing(self, request, session):
        proof = self.scope(request, session, {'containerId', 'instanceId', 'path', 'after', 'take', 'expectedVersion'})
        if type(request['after']) is not str or request['after'] and not CAPTURE.fullmatch(request['after']) or \
                type(request['take']) is not int or not 1 <= request['take'] <= PAGE_LIMIT or \
                type(request['expectedVersion']) is not str or request['expectedVersion'] and not HASH.fullmatch(request['expectedVersion']) or \
                request['after'] and not request['expectedVersion']: self.reject('REQUEST_INVALID')
        deadline = time.monotonic() + SECONDS
        self.current(request, session, proof, deadline)
        with self.namespace() as (configuration, base, descriptor):
            inventory = None if descriptor is None else self.inventory(descriptor, deadline)
            base_frame = self.frame(os.fstat(base))
            version = hashlib.sha256(b'whaledeck-container-acl-history-inventory-v1\n' + encode(dict(
                containerId=request['containerId'], instanceId=request['instanceId'], path=request['path'],
                targetProof=proof, inventory=inventory, absentBase=base_frame if descriptor is None else None))).hexdigest().upper()
            if request['expectedVersion'] and request['expectedVersion'] != version: self.reject('VERSION_CONFLICT')
            entries, scanned, invalid, size = [], 0, 0, 0
            cursor, complete = request['after'], True
            for name, frame in [] if inventory is None else inventory[1]:
                capture = name[:-5]
                if capture <= request['after']: continue
                if scanned >= SCAN_LIMIT or len(entries) >= request['take']:
                    complete = False
                    break
                self.current(request, session, proof, deadline)
                try: value = self.metadata(descriptor, capture, frame)
                except (self.r.Rejected, self.f.Rejected) as error:
                    if str(error) == 'CONTAINER_ACL_HISTORY_VERSION_CONFLICT': raise
                    invalid += 1; scanned += 1; cursor = capture
                    continue
                except (OSError, ValueError, TypeError, KeyError):
                    invalid += 1; scanned += 1; cursor = capture
                    continue
                if self.matches(value, request):
                    entry = self.summary(value)
                    length = len(encode(entry)) + 1
                    if length > RESPONSE_BYTES: self.reject('RESPONSE_LIMIT')
                    if size + length > RESPONSE_BYTES:
                        complete = False
                        break
                    entries.append(entry); size += length
                scanned += 1; cursor = capture
            self.current(request, session, proof, deadline)
            self.h.storage.attest(configuration, descriptor if descriptor is not None else base, read_only=True)
            if descriptor is None:
                try: os.stat(self.h.NAMESPACE, dir_fd=base, follow_symlinks=False)
                except FileNotFoundError: pass
                else: self.reject('VERSION_CONFLICT')
                if self.frame(os.fstat(base)) != base_frame: self.reject('VERSION_CONFLICT')
            elif self.inventory(descriptor, deadline) != inventory: self.reject('VERSION_CONFLICT')
            return dict(containerId=request['containerId'], instanceId=request['instanceId'], path=request['path'],
                version=version, entries=entries, scannedCount=scanned, invalidCount=invalid,
                complete=complete, nextAfter=None if complete else cursor)
