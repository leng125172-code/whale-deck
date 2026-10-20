#!/usr/bin/env python3
"""Private HDD directory capture -> independently verified new directory.

The fixed Agent loader exposes only plan/apply/status/discard. No source
removal, overwrite, purge or automatic retry. The application workflow must
authorize a current actor, bind the old
capture as a resource (not impersonate its owner), and add persistent plans,
hierarchical locks, jobs and audit. This is the absent-target restore leg, not
the complete directory recycle/overwrite-restore workflow.
"""
import contextlib
import itertools
import os
import re


def restoration_type(unpacking_type):
    """Compose only a reviewed, trusted unpack/publication implementation."""
    class FileTreeRestoring(unpacking_type):
        DOMAIN = b'whaledeck-tree-restoring-v1\n'
        FOLDER = 'tree-restoring'
        TEMPORARY_SUFFIX = '.tree-restore'
        SOURCE_ACTIONS = frozenset(('files-list', 'files-download', 'files-history', 'files-tree-restore'))
        TARGET_ACTIONS = frozenset(('files-mutate', 'files-upload', 'files-archive', 'files-tree-restore'))

        def __init__(self, files, captures, manifest_type, codec_type, *, cancelled=None):
            super().__init__(files, manifest_type, codec_type, cancelled=cancelled)
            self.captures = captures

        def validate(self, command):
            fields = {'source', 'target', 'format', 'captureId', 'captureOwner', 'captureKind', 'manifestSha256'}
            if not isinstance(command, dict) or set(command) != fields or command['format'] != 'tar.gz' or command['captureKind'] not in ('Trash', 'Overwrite'):
                self.reject('FILES_TREE_RESTORE_INVALID')
            self.f.identity(command['captureId'])
            for field in ('captureOwner', 'manifestSha256'):
                if type(command[field]) is not str or not re.fullmatch('[A-F0-9]{64}', command[field]):
                    self.reject('FILES_TREE_RESTORE_INVALID')
            super().validate({key: command[key] for key in ('source', 'target', 'format')})

        @staticmethod
        def capture_request(command):
            return {'operationId': command['captureId'], 'owner': command['captureOwner'],
                'source': command['source'], 'manifestSha256': command['manifestSha256'],
                'captureKind': command['captureKind']}

        def policies(self, command):
            roots = self.f.roots()
            source, target = roots.get(command['source']['rootId']), roots.get(command['target']['rootId'])
            if source is None or target is None or not self.SOURCE_ACTIONS <= set(source['allowedActions']) or not self.TARGET_ACTIONS <= set(target['allowedActions']):
                self.reject('FILES_ROOT_FORBIDDEN')
            # The source is the attested private HDD capture, not the old live
            # pathname. Restoring to the original, now absent name is legal.
            return source, target

        def target(self, command):
            return self.f.directory(command['target'], ('mutate', 'upload', 'archive', 'tree-restore'))

        @contextlib.contextmanager
        def archive(self, command, codec):
            self.policies(command)
            request = self.capture_request(command)
            policy = self.captures.request(request)
            with self.captures.storage(request, existing=True) as (configuration, folder):
                seed = self.captures.seed(request, policy, configuration, folder)
                try:
                    record = self.captures.read(folder, request['operationId'], seed)
                except self.f.Rejected as error:
                    if str(error) != 'FILES_TREE_BACKUP_IDEMPOTENCY_CONFLICT': raise
                    # Existing captures predate this independent grant. Accept
                    # exactly the new restore/recycle grants, not a changed
                    # path/owner or any other policy relaxation. Never rewrite
                    # old metadata or accept arbitrary removed action sets.
                    grants = ('files-tree-restore', 'files-tree-trash', 'files-tree-overwrite-restore', 'files-tree-purge')
                    for added in itertools.chain.from_iterable(itertools.combinations(grants, size) for size in range(1, len(grants) + 1)):
                        previous = {**policy, 'allowedActions': [action for action in policy['allowedActions'] if action not in added]}
                        legacy = self.captures.seed(request, previous, configuration, folder)
                        try:
                            record = self.captures.read(folder, request['operationId'], legacy)
                            break
                        except self.f.Rejected as mismatch:
                            if str(mismatch) != 'FILES_TREE_BACKUP_IDEMPOTENCY_CONFLICT': raise
                    else: raise error
                if record is None: self.reject('FILES_TREE_BACKUP_NOT_FOUND')
                if record['state'] != 'Verified': self.reject('FILES_TREE_BACKUP_INCOMPLETE')
                self.captures.verify(folder, record, codec)
                name = request['operationId'] + '.tar.gz'
                descriptor = self.f.open_at(folder, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
                try:
                    before = codec.regular(descriptor, private=True)
                    if [before.st_dev, before.st_ino] != record['archiveIdentity']:
                        self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                    self.captures.b.attest(configuration, folder)
                    yield descriptor
                    # Keep the same capture lock through extraction; a later
                    # purge may not unlink or swap the input underneath it.
                    if self.f.version(os.fstat(descriptor)) != self.f.version(before) or self.f.version(os.stat(name, dir_fd=folder, follow_symlinks=False)) != self.f.version(before):
                        self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                    self.captures.verify(folder, record, codec)
                    self.captures.b.attest(configuration, folder)
                    if self.captures.seed(request, self.captures.request(request), configuration, folder) != seed:
                        self.reject('FILES_ROOT_CHANGED')
                    self.policies(command)
                finally: os.close(descriptor)

        def restore(self, request):
            return self.unpack(request)

        def public(self, record):
            result = super().public(record)
            return {**result, 'kind': 'DirectoryRestore', 'captureId': record['plan']['command']['captureId']}

    return FileTreeRestoring
