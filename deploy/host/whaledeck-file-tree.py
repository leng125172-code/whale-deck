#!/usr/bin/env python3
"""Internal descriptor-bound tree inventory for forthcoming directory workflows.

Not a CLI, installed helper, RPC or permission grant. A manifest is an optimistic
per-entry inventory, not an atomic filesystem snapshot. Callers must seal its
digest, stage and verify copies, acquire hierarchical locks and handle partial
source retention before exposing directory writes. Never rename an unverified
ancestor wholesale to bypass protected descendants.
"""
import hashlib
import hmac
import json
import math
import os
import re
import stat
import time


class FileTreeManifest:
    DOMAIN = b'whaledeck-file-tree-manifest-v1\n'

    def __init__(self, files, *, max_entries=10000, max_depth=32,
                 max_bytes=16 * 1024**3, max_manifest_bytes=16 * 1024**2,
                 timeout_seconds=300, cancelled=None, clock=time.monotonic):
        self.f = files
        for value, minimum, maximum in ((max_entries, 1, 10000), (max_depth, 0, 32),
                (max_bytes, 0, 16 * 1024**3), (max_manifest_bytes, 128, 16 * 1024**2)):
            if type(value) is not int or not minimum <= value <= maximum:
                self.reject('FILES_TREE_LIMITS_INVALID')
        if type(timeout_seconds) not in (int, float) or not math.isfinite(timeout_seconds) or not 0.001 <= timeout_seconds <= 600:
            self.reject('FILES_TREE_LIMITS_INVALID')
        if not callable(clock) or cancelled is not None and not callable(cancelled):
            self.reject('FILES_TREE_LIMITS_INVALID')
        self.max_entries, self.max_depth, self.max_bytes = max_entries, max_depth, max_bytes
        self.max_manifest_bytes, self.timeout_seconds = max_manifest_bytes, timeout_seconds
        self.cancelled, self.clock = cancelled, clock

    def reject(self, code): raise self.f.Rejected(code)

    @staticmethod
    def encode(value):
        return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')

    def path(self, value):
        try:
            result = self.f.relative(value)
            if any(127 <= ord(character) <= 159 for character in result): self.reject('FILES_PATH_INVALID')
            return result
        except UnicodeError: self.reject('FILES_PATH_INVALID')

    def scan(self, command):
        if not isinstance(command, dict) or set(command) != {'rootId', 'path', 'expectedVersion'} or any(type(value) is not str for value in command.values()):
            self.reject('FILES_TREE_COMMAND_INVALID')
        if not re.fullmatch(r'[A-F0-9]{64}', command['expectedVersion']): self.reject('FILES_VERSION_CONFLICT')
        self.path(command['path'])
        registered = self.f.roots().get(command['rootId'])
        if registered is None or not {'files-list', 'files-download'} <= set(registered['allowedActions']):
            self.reject('FILES_ROOT_FORBIDDEN')
        with self.f.directory(command, 'download') as (_, parent, leaf):
            folder = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                result = self._scan_descriptor(command, folder, registered)
                if self.f.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != command['expectedVersion']:
                    self.reject('FILES_TREE_CHANGED')
            finally: os.close(folder)
        return result

    def _scan_descriptor(self, command, folder, registered):
        """Trusted engine only: inventory a pinned descriptor, never an RPC FD.

        Used to rehash private staged trees without registering their private
        paths or weakening the public roots/protected-name policy.
        """
        policy_hash = hashlib.sha256(self.encode(registered)).hexdigest().upper()
        deadline = self.clock() + self.timeout_seconds
        entries = []; encoded_entries = []; encoded_size = 0; admitted = 1; total = 0; files = 0; directories = 0

        def check():
            if self.cancelled is not None and self.cancelled(): self.reject('FILES_TREE_CANCELED')
            if self.clock() >= deadline: self.reject('FILES_TREE_DEADLINE')

        def add(path, info, kind, checksum=''):
            nonlocal encoded_size, files, directories
            check()
            if len(entries) >= self.max_entries: self.reject('FILES_TREE_ENTRY_LIMIT')
            value = {'path': path, 'kind': kind, 'sourceIdentity': self.f.publication_identity(info),
                     'sourceVersion': self.f.version(info), 'sizeBytes': info.st_size if kind == 'File' else 0, 'sha256': checksum}
            encoded = self.encode(value)
            encoded_size += 8 + len(encoded)
            if encoded_size > self.max_manifest_bytes: self.reject('FILES_TREE_MANIFEST_LIMIT')
            entries.append(value); encoded_entries.append(encoded)
            if kind == 'File': files += 1
            else: directories += 1

        def ordinary(info):
            if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1: self.reject('FILES_REGULAR_SINGLE_LINK_REQUIRED')
            if info.st_size < 0 or info.st_size > self.max_bytes: self.reject('FILES_TREE_SIZE_LIMIT')
            return info

        def content(parent, name, relative, expected):
            nonlocal total
            descriptor = self.f.open_at(parent, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                before = ordinary(os.fstat(descriptor))
                if self.f.version(before) != self.f.version(expected): self.reject('FILES_TREE_CHANGED')
                if total + before.st_size > self.max_bytes: self.reject('FILES_TREE_SIZE_LIMIT')
                total += before.st_size
                checksum = hashlib.sha256(); count = 0
                while count < before.st_size:
                    check()
                    value = os.read(descriptor, min(self.f.CHUNK, before.st_size - count))
                    if not value: self.reject('FILES_READ_CHANGED')
                    checksum.update(value); count += len(value)
                check()
                if os.read(descriptor, 1) or self.f.version(ordinary(os.fstat(descriptor))) != self.f.version(before):
                    self.reject('FILES_READ_CHANGED')
                if self.f.version(os.stat(name, dir_fd=parent, follow_symlinks=False)) != self.f.version(before):
                    self.reject('FILES_TREE_CHANGED')
                add(relative, before, 'File', checksum.hexdigest().upper())
            finally: os.close(descriptor)

        def walk(folder, relative, expected, depth):
            nonlocal admitted
            check(); before = os.fstat(folder)
            if not stat.S_ISDIR(before.st_mode) or self.f.version(before) != self.f.version(expected):
                self.reject('FILES_TREE_CHANGED')
            add(relative, before, 'Directory')
            names = []
            with os.scandir(folder) as children:
                for child in children:
                    check(); admitted += 1
                    if admitted > self.max_entries: self.reject('FILES_TREE_ENTRY_LIMIT')
                    if depth >= self.max_depth: self.reject('FILES_TREE_DEPTH_LIMIT')
                    inside = child.name if not relative else relative + '/' + child.name
                    full = self.path(command['path'] + '/' + inside)
                    if self.f.protected(full): self.reject('FILES_TREE_PROTECTED')
                    names.append(child.name)
            if self.f.version(os.fstat(folder)) != self.f.version(before): self.reject('FILES_TREE_CHANGED')
            # Canonical directory-preorder, sorting each sibling set by UTF-8.
            # Admission is bounded BEFORE sorting; no unbounded listdir/sort.
            for name in sorted(names, key=lambda value: value.encode('utf-8')):
                check(); path = name if not relative else relative + '/' + name
                info = os.stat(name, dir_fd=folder, follow_symlinks=False)
                if stat.S_ISDIR(info.st_mode):
                    child = self.f.open_at(folder, name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                    try: walk(child, path, info, depth + 1)
                    finally: os.close(child)
                    if self.f.version(os.stat(name, dir_fd=folder, follow_symlinks=False)) != self.f.version(info):
                        self.reject('FILES_TREE_CHANGED')
                elif stat.S_ISREG(info.st_mode): content(folder, name, path, info)
                else: self.reject('FILES_TREE_SPECIAL_OBJECT')
            if self.f.version(os.fstat(folder)) != self.f.version(before): self.reject('FILES_TREE_CHANGED')

        check(); before = os.fstat(folder)
        if self.f.version(before) != command['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
        walk(folder, '', before, 0)
        check()
        current = self.f.roots().get(command['rootId'])
        if current is None or hashlib.sha256(self.encode(current)).hexdigest().upper() != policy_hash:
            self.reject('FILES_ROOT_CHANGED')
        result = {'schemaVersion': 1, 'rootId': command['rootId'], 'path': command['path'],
                  'rootVersion': command['expectedVersion'], 'policyHash': policy_hash,
                  'entryCount': len(entries), 'fileCount': files, 'directoryCount': directories, 'totalBytes': total}
        header = self.encode(result)
        if encoded_size + len(header) + 8 > self.max_manifest_bytes: self.reject('FILES_TREE_MANIFEST_LIMIT')
        digest = hashlib.sha256(self.DOMAIN + len(header).to_bytes(8, 'big') + header)
        for value in encoded_entries: digest.update(len(value).to_bytes(8, 'big') + value)
        return {**result, 'entries': entries, 'manifestSha256': digest.hexdigest().upper()}

    def verify(self, command, fingerprint):
        if type(fingerprint) is not str or not re.fullmatch(r'[A-F0-9]{64}', fingerprint):
            self.reject('FILES_TREE_MANIFEST_INVALID')
        result = self.scan(command)
        if not hmac.compare_digest(result['manifestSha256'], fingerprint): self.reject('FILES_TREE_CHANGED')
        return result
