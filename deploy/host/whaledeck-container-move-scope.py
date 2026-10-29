#!/usr/bin/env python3
"""Internal FD-only scope for durable container file/directory moves.

No transport or host registry. A future container-domain sealed move intent and
publication barrier must own this adapter. Retained source objects remain in a
separate private namespace; they are not an HDD backup or public container ETag.
"""
import contextlib
import os
from pathlib import Path
import re
import stat


def container_move_scope_type(copy_scope_type):
    class ContainerMoveScope(copy_scope_type):
        SOURCE = 'container-move-source'
        TARGET = 'container-move-target'
        PRIVATE = STAGING = '.whaledeck-container-moves-v1'

        def __init__(self, *args, source_parent=None, operation_id=None, **kwargs):
            self.source_parent = None
            if source_parent is not None:
                if type(source_parent) not in (list, tuple) or len(source_parent) != 2 or \
                        any(type(n) is not int or not 0 <= n <= 18446744073709551615 for n in source_parent) or source_parent[1] == 0:
                    raise ValueError('CONTAINER_MOVE_PARENT_INVALID')
                self.source_parent = tuple(source_parent)
            if operation_id is not None and (type(operation_id) is not str or not re.fullmatch('[a-f0-9]{32}', operation_id)):
                raise ValueError('CONTAINER_MOVE_OPERATION_INVALID')
            self.operation_id = operation_id
            super().__init__(*args, **kwargs)
            self.identity = self.bound_identity
            if self.session is None and self.source_parent is None: self.reject('BINDING_INVALID')

        def reject(self, suffix): raise self.f.Rejected('CONTAINER_MOVE_' + suffix)

        def bound_identity(self, value):
            self.f.identity(value)
            if self.operation_id is None or value != self.operation_id: self.reject('OPERATION_MISMATCH')

        def roots(self):
            values = super().roots()
            values[self.SOURCE]['allowedActions'] = ['files-list', 'files-download', 'files-mutate', 'files-tree-move']
            values[self.TARGET]['allowedActions'] = ['files-mutate', 'files-upload', 'files-tree-move']
            return values

        @contextlib.contextmanager
        def directory(self, request, action, allow_empty=False):
            with super().directory(request, action, allow_empty) as (root, parent, leaf):
                if request['rootId'] == self.SOURCE:
                    identity = self.inode(parent)
                    if self.source_parent is None: self.source_parent = identity
                    elif identity != self.source_parent: self.reject('SOURCE_PARENT_CHANGED')
                    # Moves mutate both ends, including a source on a separate
                    # mount. Copy's read-only-source allowance must not apply.
                    self.p.writable({'request': {**self.command, 'path': self.command['sourcePath']}}, self.session, parent)
                yield root, parent, leaf

        def file_command(self): return {**super().file_command(), 'kind': 'move'}

        def current(self, parent, leaf): return self.f.current(parent, leaf)
        def checksum(self, parent, leaf): return self.f.checksum(parent, leaf)

        def staging(self, parent):
            if self.operation_id is None: self.reject('OPERATION_REQUIRED')
            if self.inode(parent) not in (self.source_parent, self.target_parent): self.reject('PARENT_CHANGED')
            return super().staging(parent)

        def private(self, parent):
            descriptor = self.f.open_at(parent, self.PRIVATE, os.O_RDONLY | os.O_DIRECTORY)
            try:
                info = os.fstat(descriptor)
                if info.st_uid != 0 or info.st_gid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                    self.reject('PRIVATE_UNSAFE')
                return descriptor
            except BaseException: os.close(descriptor); raise

        def relative_fd(self, root, child):
            """A proc link is a candidate only; reopen beneath pinned root.

            Never authorize from a pathname prefix or a descriptor string.
            openat2 must resolve the candidate below the already pinned FD and
            its kernel inode must still equal the supplied descriptor.
            """
            origin = Path(os.readlink('/proc/self/fd/' + str(root)))
            path = Path(os.readlink('/proc/self/fd/' + str(child)))
            try: relative = path.relative_to(origin).as_posix()
            except ValueError: self.reject('RETENTION_LOCATION_INVALID')
            check = self.f.open_at(root, relative, os.O_RDONLY | os.O_DIRECTORY)
            try:
                if self.inode(check) != self.inode(child): self.reject('RETENTION_LOCATION_INVALID')
            finally: os.close(check)
            return relative

        def manifest_exchange(self, source, name, target, leaf):
            # Host tree engine's immutable manifest is in this dedicated state
            # namespace, never in a container path or the host global registry.
            if not re.fullmatch(r'\.[a-f0-9]{32}', name) or leaf != self.operation_id + '.manifest.json':
                return False
            with self.state_directory() as state:
                folder = self.f.open_at(state, 'tree-moves', os.O_RDONLY | os.O_DIRECTORY)
                try:
                    info = os.fstat(folder)
                    if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700 or \
                            self.inode(source) != self.inode(folder) or self.inode(target) != self.inode(folder):
                        self.reject('STATE_UNSAFE')
                    item = os.stat(name, dir_fd=folder, follow_symlinks=False)
                    if not stat.S_ISREG(item.st_mode) or item.st_uid != 0 or item.st_nlink != 1 or stat.S_IMODE(item.st_mode) != 0o600:
                        self.reject('STATE_UNSAFE')
                    self.f.exchange(source, name, target, leaf, 1)
                finally: os.close(folder)
            return True

        def exchange(self, source, name, target, leaf, flags):
            if self.operation_id is None: self.reject('OPERATION_REQUIRED')
            if flags != 1 or any(type(value) is not str or not value or '/' in value or '\0' in value or value in ('.', '..') for value in (name, leaf)):
                self.reject('PUBLICATION_INVALID')
            self.guard()
            if self.manifest_exchange(source, name, target, leaf): return
            if name in (self.operation_id + '.mutation', self.operation_id + '.tree-copy'):
                return super().exchange(source, name, target, leaf, flags)
            with self.directory({'rootId': self.SOURCE, 'path': self.command['sourcePath']}, 'mutate') as (_, parent, expected):
                private = self.private(parent)
                try:
                    if name == expected and leaf == self.operation_id + '.mutation':
                        if self.inode(source) != self.inode(parent) or self.inode(target) != self.inode(private):
                            self.reject('SOURCE_PARENT_CHANGED')
                        item = os.stat(name, dir_fd=source, follow_symlinks=False)
                        self.metadata(item)
                        if not stat.S_ISREG(item.st_mode) or item.st_nlink != 1: self.reject('SOURCE_CHANGED')
                        self.guard(); self.f.exchange(source, name, target, leaf, 1)
                        return
                    if name != leaf: self.reject('RETENTION_LOCATION_INVALID')
                    root = self.f.open_at(parent, expected, os.O_RDONLY | os.O_DIRECTORY)
                    retained = None
                    try:
                        retained = self.f.open_at(private, self.operation_id + '.tree-move', os.O_RDONLY | os.O_DIRECTORY)
                        info = os.fstat(retained)
                        if info.st_uid != 0 or info.st_gid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('PRIVATE_UNSAFE')
                        relative = self.relative_fd(root, source)
                        matching = self.f.open_at(retained, relative, os.O_RDONLY | os.O_DIRECTORY)
                        try:
                            if self.inode(matching) != self.inode(target): self.reject('RETENTION_LOCATION_INVALID')
                            item = os.stat(name, dir_fd=source, follow_symlinks=False); self.metadata(item)
                            if not stat.S_ISREG(item.st_mode) or item.st_nlink != 1: self.reject('SOURCE_CHANGED')
                            self.guard(); self.f.exchange(source, name, target, leaf, 1)
                        finally: os.close(matching)
                    finally:
                        if retained is not None: os.close(retained)
                        os.close(root)
                finally: os.close(private)

    return ContainerMoveScope
