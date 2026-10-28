#!/usr/bin/env python3
"""Internal FD-only adapter for the existing durable file/tree copy engines.

No CLI, RPC, host registry, mount, Docker Exec or user-selected ownership. The
caller must supply a trusted pinned ContainerRoot and a dedicated root-owned
state directory. An outer container-domain sealed plan, permanent business
intent and publication barrier are still required before exposing this adapter.
Host-engine metadata versions remain internal; they are NOT container ETags.
"""
import contextlib
import fcntl
import os
from pathlib import Path
import re
import stat


class ContainerCopyScope:
    COMMAND = {'containerId', 'instanceId', 'sourcePath', 'targetPath', 'expectedVersion'}
    SOURCE = 'container-copy-source'
    TARGET = 'container-copy-target'
    PRIVATE = '.whaledeck-container-copies-v1'

    def __init__(self, uploads, publisher, session, command, state_path, *, target_parent=None):
        if publisher.u is not uploads: raise ValueError('CONTAINER_COPY_ENGINE_MISMATCH')
        self.u, self.p, self.r, self.f = uploads, publisher, uploads.r, uploads.f
        self.Rejected, self.CHUNK, self.MAX_BYTES = self.f.Rejected, self.f.CHUNK, self.f.MAX_BYTES
        if type(command) is not dict or set(command) != self.COMMAND: self.reject('REQUEST_INVALID')
        self.r.target_binding(command['containerId'], command['instanceId'])
        for key in ('sourcePath', 'targetPath'):
            self.relative(command[key])
            if self.protected(command[key]): self.reject('PROTECTED')
        if type(command['expectedVersion']) is not str or not re.fullmatch('[A-F0-9]{64}', command['expectedVersion']):
            self.reject('VERSION_INVALID')
        source, target = command['sourcePath'], command['targetPath']
        if source == target or source.startswith(target + '/') or target.startswith(source + '/'):
            self.reject('OVERLAP')
        self.command, self.session = dict(command), session
        if target_parent is not None and (type(target_parent) not in (list, tuple) or len(target_parent) != 2 or
                any(type(n) is not int or not 0 <= n <= 18446744073709551615 for n in target_parent) or target_parent[1] == 0):
            self.reject('PARENT_INVALID')
        # A resumed caller supplies the original sealed targetParent. During a
        # read-only plan, capture it on the first target open and retain it for
        # every subsequent open, including the host engine's recovery observer.
        self.target_parent = None if target_parent is None else tuple(target_parent)
        self.u.session_scope(command, session); session.revalidate()
        self.proof = self.u.target_proof(session)
        self.owner = tuple(self.p.owner(session))
        self.STATE = Path(state_path)
        with self.state_directory(): pass
        self.guard()
        # Explicit primitives only. Never delegate roots/directory/staging or
        # namespace_lock to the host module's global registry/state namespace.
        for name in ('identity', 'save_receipt', 'unique_object'):
            setattr(self, name, getattr(self.f, name))

    def reject(self, suffix): raise self.f.Rejected('CONTAINER_COPY_' + suffix)

    def guard(self):
        self.u.session_scope(self.command, self.session); self.session.revalidate()
        if self.u.target_proof(self.session) != self.proof: self.reject('TARGET_CHANGED')
        if tuple(self.p.owner(self.session)) != self.owner: self.reject('OWNER_CHANGED')

    def relative(self, value, empty=False): return self.r.canonical_path(value, empty=empty)
    def protected(self, path): return self.r.protected(path, self.f)

    def metadata(self, info):
        if info.st_mode & 0o7000: self.reject('SPECIAL_MODE_UNSUPPORTED')
        return info

    def version(self, info): return self.f.version(self.metadata(info))
    def publication_identity(self, info): return self.f.publication_identity(self.metadata(info))
    def regular(self, descriptor): return self.metadata(self.f.regular(descriptor))
    def open_at(self, *args, **kwargs): return self.f.open_at(*args, **kwargs)

    def roots(self):
        self.guard()
        binding = {'containerId': self.command['containerId'], 'instanceId': self.command['instanceId'],
                   'targetProof': self.proof, 'ownerUid': self.owner[0], 'ownerGid': self.owner[1]}
        # A logical common root for hierarchy checks ONLY; it is never opened.
        common = {'path': '/container', **binding}
        return {self.SOURCE: {**common, 'allowedActions': ['files-list', 'files-download', 'files-tree-copy']},
                self.TARGET: {**common, 'allowedActions': ['files-mutate', 'files-upload', 'files-tree-copy']}}

    @contextlib.contextmanager
    def directory(self, request, action, allow_empty=False):
        if type(request) is not dict or not {'rootId', 'path'} <= set(request) or set(request) - {'rootId', 'path', 'expectedVersion'}:
            self.reject('LOCATION_INVALID')
        source = request['rootId'] == self.SOURCE
        if request['rootId'] not in (self.SOURCE, self.TARGET): self.reject('LOCATION_INVALID')
        path = self.command['sourcePath' if source else 'targetPath']
        if request['path'] != path or allow_empty or action not in ('mutate', 'download', 'stat'):
            self.reject('LOCATION_INVALID')
        if not source and action == 'download': self.reject('LOCATION_INVALID')
        self.guard(); root = os.dup(self.session.root); parent = None
        try:
            parent_path, _, leaf = path.rpartition('/')
            # Docker mount proof was frozen by ContainerRoot. Subsequent tree
            # traversal uses open_at's no-cross-mount default under this parent.
            parent = self.f.open_at(root, parent_path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
            if not source:
                identity = self.inode(parent)
                if self.target_parent is None: self.target_parent = identity
                elif identity != self.target_parent: self.reject('PARENT_CHANGED')
                self.p.writable({'request': {**self.command, 'path': path}}, self.session, parent)
            yield root, parent, leaf
            self.guard()
        finally:
            if parent is not None: os.close(parent)
            os.close(root)

    def source_version(self):
        """Translate a verified public container ETag to an internal version."""
        with self.directory({'rootId': self.SOURCE, 'path': self.command['sourcePath']}, 'stat') as (_, parent, leaf):
            fd = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                info = self.metadata(os.fstat(fd))
                if not stat.S_ISDIR(info.st_mode): self.regular(fd)
                if self.r.version(self.command, self.command['sourcePath'], info) != self.command['expectedVersion']:
                    self.reject('VERSION_CONFLICT')
                return self.version(info)
            finally: os.close(fd)

    def file_command(self):
        return {'kind': 'copy', 'sourceRootId': self.SOURCE, 'sourcePath': self.command['sourcePath'],
                'targetRootId': self.TARGET, 'targetPath': self.command['targetPath'],
                'expectedVersion': self.source_version(), 'mode': ''}

    def tree_command(self):
        return {'source': {'rootId': self.SOURCE, 'path': self.command['sourcePath'], 'expectedVersion': self.source_version()},
                'target': {'rootId': self.TARGET, 'path': self.command['targetPath']}}

    @contextlib.contextmanager
    def state_directory(self):
        if not self.STATE.is_absolute() or self.STATE != self.STATE.resolve(strict=True): self.reject('STATE_UNSAFE')
        descriptor = self.f.trusted_directory(self.STATE)
        try:
            if stat.S_IMODE(os.fstat(descriptor).st_mode) != 0o700: self.reject('STATE_UNSAFE')
            yield descriptor
        finally: os.close(descriptor)

    def trusted_directory(self, path):
        if Path(path) != self.STATE: self.reject('STATE_UNSAFE')
        # Return an independently owned FD; the caller closes it.
        with self.state_directory() as descriptor: return os.dup(descriptor)

    @contextlib.contextmanager
    def namespace_lock(self):
        # Metadata-only lock: no liveness check, so permanent receipts remain
        # readable after container loss. Outer observer handles target absence.
        with self.state_directory() as directory:
            lock = os.open('namespace.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC,
                           0o600, dir_fd=directory)
            try:
                info = os.fstat(lock)
                if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_size != 0 or stat.S_IMODE(info.st_mode) != 0o600:
                    self.reject('STATE_UNSAFE')
                try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError: self.reject('BUSY')
                yield
            finally: os.close(lock)

    def staging(self, parent):
        self.guard()
        try: os.mkdir(self.PRIVATE, 0o700, dir_fd=parent); os.fsync(parent)
        except FileExistsError: pass
        descriptor = self.f.open_at(parent, self.PRIVATE, os.O_RDONLY | os.O_DIRECTORY)
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('PRIVATE_UNSAFE')
            return descriptor
        except BaseException: os.close(descriptor); raise

    def exchange(self, source, name, target, leaf, flags):
        # Only new-destination publication. There is no source removal,
        # overwrite/exchange or chmod route in this adapter.
        if flags != 1 or type(name) is not str or not re.fullmatch('[a-f0-9]{32}\\.(mutation|tree-copy)', name):
            self.reject('PUBLICATION_INVALID')
        with self.directory({'rootId': self.TARGET, 'path': self.command['targetPath']}, 'mutate') as (_, parent, expected):
            if leaf != expected or self.inode(parent) != self.inode(target): self.reject('PARENT_CHANGED')
            private = self.f.open_at(parent, self.PRIVATE, os.O_RDONLY | os.O_DIRECTORY)
            try:
                value = os.fstat(private)
                if value.st_uid != 0 or stat.S_IMODE(value.st_mode) != 0o700 or self.inode(private) != self.inode(source):
                    self.reject('PRIVATE_UNSAFE')
                self.guard()
                self.f.exchange(source, name, target, leaf, 1)
            finally: os.close(private)

    @staticmethod
    def inode(descriptor):
        value = os.fstat(descriptor); return value.st_dev, value.st_ino
