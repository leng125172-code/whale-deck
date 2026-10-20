#!/usr/bin/env python3
"""Internal, explicitly scoped directory capture/original payload destruction.

No helper verb, RPC, Worker, HTTP or UI exposure yet. A caller must later add
signed administrator confirmation, durable jobs, shared capture/hierarchy locks
and human audit. Never accepts a caller-selected private path. Never deletes
capture metadata/manifests, root journals, jobs or audit. Recovery only observes:
after the first quarantine an interruption is Unknown, never an automatic retry.
"""
import contextlib
import hmac
import itertools
import os
import re
import stat


def purging_type(recycling_type):
    class FileTreePurging(recycling_type):
        DOMAIN = b'whaledeck-tree-purging-v1\n'
        FOLDER = 'tree-purging'
        SUFFIX = '.tree-purge'
        ACTIONS = frozenset(('files-list', 'files-download', 'files-mutate', 'files-history', 'files-archive', 'files-tree-purge'))
        PHASES = ('Preparing', 'OriginalsQuarantining', 'OriginalsRemoving', 'ArchiveQuarantining', 'ArchiveRemoving', 'Completing')
        COMMAND_FIELDS = {'source', 'captureId', 'captureOwner', 'captureKind', 'manifestSha256', 'scope'}
        PLAN_FIELDS = {'command', 'sourcePolicyHash', 'sourceParent', 'storageHash', 'storeIdentity', 'captureMetadataHash',
            'originReceiptHash', 'manifestFileSha256', 'archiveIdentity', 'archiveBytes', 'archiveSha256', 'contentSha256',
            'entryCount', 'totalBytes', 'retainedIdentity', 'stagingIdentity', 'retainedTreeSha256', 'fingerprint'}

        def __init__(self, files, captures, manifest_type, codec_type, io_type, *, origins, cancelled=None):
            super().__init__(files, captures, manifest_type, codec_type, io_type, cancelled=cancelled)
            if set(origins) != {'Trash', 'Overwrite'}: self.reject('FILES_TREE_PURGE_INVALID')
            self.origins = origins

        def command(self, command):
            if not isinstance(command, dict) or set(command) != self.COMMAND_FIELDS or command['scope'] not in ('Archive', 'Originals', 'All') or command['captureKind'] not in self.origins:
                self.reject('FILES_TREE_PURGE_INVALID')
            self.source(command['source']); self.f.identity(command['captureId'])
            for key in ('captureOwner', 'manifestSha256'):
                if type(command[key]) is not str or not re.fullmatch('[A-F0-9]{64}', command[key]): self.reject('FILES_TREE_PURGE_INVALID')

        @staticmethod
        def wants(command, component): return command['scope'] in (component, 'All')

        @staticmethod
        def exists(folder, name):
            try: os.stat(name, dir_fd=folder, follow_symlinks=False); return True
            except FileNotFoundError: return False

        def bound(self, plan):
            policy = self.policy(plan['command']['source'])
            if self.digest(policy) != plan['sourcePolicyHash']: self.reject('FILES_ROOT_CHANGED')
            return policy

        def directory(self, source):
            return self.f.directory(source, ('list', 'download', 'mutate', 'history', 'archive', 'tree-purge'))

        @staticmethod
        def capture_request(command):
            return {'operationId': command['captureId'], 'owner': command['captureOwner'], 'source': command['source'],
                'manifestSha256': command['manifestSha256'], 'captureKind': command['captureKind']}

        def capture(self, folder, configuration, command):
            request = self.capture_request(command); policy = self.captures.request(request)
            error = None
            # Only these exact additive grants may postdate an immutable
            # capture. A different root, owner, mount or any other policy change
            # remains an idempotency failure; no old receipt is rewritten.
            grants = ('files-tree-restore', 'files-tree-trash', 'files-tree-overwrite-restore', 'files-tree-purge')
            for removed in itertools.chain(((),), itertools.chain.from_iterable(itertools.combinations(grants, size) for size in range(1, len(grants) + 1))):
                previous = {**policy, 'allowedActions': [action for action in policy['allowedActions'] if action not in removed]}
                try: saved = self.captures.read(folder, command['captureId'], self.captures.seed(request, previous, configuration, folder))
                except self.f.Rejected as mismatch:
                    if str(mismatch) != 'FILES_TREE_BACKUP_IDEMPOTENCY_CONFLICT': raise
                    error = mismatch; continue
                if saved is None: self.reject('FILES_TREE_BACKUP_NOT_FOUND')
                return saved
            raise error

        def origin(self, command, saved):
            engine = self.origins[command['captureKind']]
            base = self.f.trusted_directory(self.f.STATE)
            try: folder = self.f.open_at(base, engine.FOLDER, os.O_RDONLY | os.O_DIRECTORY)
            finally: os.close(base)
            try:
                info = os.fstat(folder)
                if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
                record = engine.read(folder, command['captureId'])
                if record is None or record['owner'] != command['captureOwner']: self.reject('FILES_OPERATION_NOT_FOUND')
                if record['state'] != 'Completed': self.reject('FILES_TREE_ORIGIN_UNCONFIRMED')
                if command['captureKind'] == 'Trash':
                    if not all(record.get(key) for key in ('sourceRemoved', 'sourceRetained', 'captureVerified')): self.reject('FILES_TREE_ORIGIN_UNCONFIRMED')
                    source, manifest = record['plan']['source'], record['plan']['manifestSha256']
                    identity, suffix = record['retainedIdentity'], '.tree-trash'
                    archive_bytes, archive_hash = record['archiveBytes'], record['archiveSha256']
                else:
                    if not all(record.get(key) for key in ('backupVerified', 'swapStarted', 'originalsRetained')): self.reject('FILES_TREE_ORIGIN_UNCONFIRMED')
                    source, manifest = record['plan']['command']['target'], record['plan']['targetManifestSha256']
                    identity, suffix = record['plan']['targetIdentity'], '.tree-overwrite-restore'
                    archive_bytes, archive_hash = record['backupArchiveBytes'], record['backupArchiveSha256']
                if source != command['source'] or manifest != command['manifestSha256'] or (archive_bytes, archive_hash) != (saved['archiveBytes'], saved['archiveSha256']): self.reject('FILES_TREE_ORIGIN_UNCONFIRMED')
                return record, identity, suffix
            finally: os.close(folder)

        @contextlib.contextmanager
        def retained(self, command, expected_parent=None, expected_staging=None, name=None):
            with self.directory(command['source']) as (_, parent, _):
                if expected_parent is not None and self.identity(parent) != expected_parent: self.reject('FILES_PARENT_CHANGED')
                private = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    info = os.fstat(private)
                    if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700 or expected_staging is not None and self.identity(private) != expected_staging: self.reject('FILES_STAGING_UNSAFE')
                    if name is None: name = command['captureId'] + ('.tree-trash' if command['captureKind'] == 'Trash' else '.tree-overwrite-restore')
                    root = self.f.open_at(private, name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                    try: yield parent, private, root
                    finally: os.close(root)
                finally: os.close(private)

        def inventory(self, command, root, manifest, identity, check):
            if self.identity(root) != identity: self.reject('FILES_TREE_CHANGED')
            source = {**command['source'], 'expectedVersion': self.f.version(os.fstat(root))}
            actual = self.manifest_type(self.f, cancelled=check)._scan_descriptor(source, root, self.policy(command['source']))
            if (actual['entryCount'], actual['totalBytes']) != (manifest['entryCount'], manifest['totalBytes']): self.reject('FILES_TREE_CHANGED')
            for current, original in zip(actual['entries'], manifest['entries']):
                if (current['path'], current['kind'], current['sha256'], current['sizeBytes']) != (original['path'], original['kind'], original['sha256'], original['sizeBytes']): self.reject('FILES_TREE_CHANGED')
                if current['kind'] == 'File' or command['captureKind'] == 'Overwrite':
                    if current['sourceIdentity'] != original['sourceIdentity']: self.reject('FILES_TREE_CHANGED')
                elif current['sourceIdentity'][3:5] != [0, 0] or current['sourceIdentity'][2] & 0o7777 != 0o700: self.reject('FILES_RECEIPT_UNSAFE')
            return actual

        def tree_hash(self, inventory):
            return self.digest([{key: row[key] for key in ('path', 'kind', 'sourceIdentity', 'sizeBytes', 'sha256')} for row in inventory['entries']])

        def snapshot(self, command, folder, configuration, saved, check):
            if self.wants(command, 'Archive') and saved['state'] != 'Verified' or self.wants(command, 'Originals') and saved.get('originalsPurged', False):
                self.reject('FILES_TREE_PURGE_ALREADY_REMOVED')
            manifest = self.captures.manifest(folder, saved)
            origin, identity, _ = self.origin(command, saved)
            if saved['state'] == 'Verified':
                codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
                self.captures.verify(folder, saved, codec)
            elif self.exists(folder, command['captureId'] + '.tar.gz'):
                self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
            retained_identity = staging_identity = retained_hash = parent_identity = None
            if self.wants(command, 'Originals'):
                with self.directory(command['source']) as (_, parent, _): parent_identity = self.identity(parent)
                with self.retained(command, parent_identity) as (_, private, root):
                    inventory = self.inventory(command, root, manifest, identity, check)
                    retained_identity, staging_identity, retained_hash = self.identity(root), self.identity(private), self.tree_hash(inventory)
            self.captures.b.attest(configuration, folder); check()
            return {'sourceParent': parent_identity, 'storageHash': saved['storageHash'], 'storeIdentity': saved['storeIdentity'],
                'captureMetadataHash': self.digest(saved), 'originReceiptHash': self.digest(origin),
                **{key: saved[key] for key in ('manifestFileSha256', 'archiveIdentity', 'archiveBytes', 'archiveSha256', 'contentSha256', 'entryCount', 'totalBytes')},
                'retainedIdentity': retained_identity, 'stagingIdentity': staging_identity, 'retainedTreeSha256': retained_hash}

        def plan(self, command):
            self.command(command); policy = self.policy(command['source'])
            seed = {'command': command, 'sourcePolicyHash': self.digest(policy)}; check = self.guard(seed)
            with self.captures.storage(self.capture_request(command), existing=True) as (configuration, folder):
                saved = self.capture(folder, configuration, command)
                plan = {**seed, **self.snapshot(command, folder, configuration, saved, check)}
            self.bound(plan); return {**plan, 'fingerprint': self.digest(plan)}

        def validate(self, plan):
            if not isinstance(plan, dict) or set(plan) != self.PLAN_FIELDS: self.reject('FILES_PLAN_INVALID')
            self.command(plan['command'])
            for key in ('sourcePolicyHash', 'storageHash', 'captureMetadataHash', 'originReceiptHash', 'manifestFileSha256', 'archiveSha256', 'contentSha256', 'fingerprint'):
                if type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key]): self.reject('FILES_PLAN_INVALID')
            for key in ('sourceParent', 'storeIdentity', 'archiveIdentity', 'retainedIdentity', 'stagingIdentity'):
                value = plan[key]
                if value is None and key in ('sourceParent', 'retainedIdentity', 'stagingIdentity') and not self.wants(plan['command'], 'Originals'): continue
                if not isinstance(value, list) or len(value) != 2 or any(type(item) is not int or not 0 <= item < 2**64 for item in value) or not value[1]: self.reject('FILES_PLAN_INVALID')
            if self.wants(plan['command'], 'Originals'):
                if type(plan['retainedTreeSha256']) is not str or not re.fullmatch('[A-F0-9]{64}', plan['retainedTreeSha256']): self.reject('FILES_PLAN_INVALID')
            elif any(plan[key] is not None for key in ('sourceParent', 'retainedIdentity', 'stagingIdentity', 'retainedTreeSha256')): self.reject('FILES_PLAN_INVALID')
            for key, minimum, maximum in (('entryCount', 1, 10000), ('totalBytes', 0, 16 * 1024**3), ('archiveBytes', 1, 17 * 1024**3)):
                if type(plan[key]) is not int or not minimum <= plan[key] <= maximum: self.reject('FILES_PLAN_INVALID')
            if not hmac.compare_digest(plan['fingerprint'], self.digest({key: value for key, value in plan.items() if key != 'fingerprint'})): self.reject('FILES_PLAN_INVALID')

        def read(self, folder, operation):
            try: record = self.read_json(folder, operation + '.json', 32768)
            except FileNotFoundError: return None
            fields = {'operationId', 'owner', 'plan', 'state', 'deletionStarted', 'archiveRemoved', 'originalsRemoved', 'currentPath'}
            try:
                if not isinstance(record, dict) or set(record) - fields or record['operationId'] != operation or record['state'] not in self.TERMINAL + self.PHASES or type(record['owner']) is not str or not re.fullmatch('[A-F0-9]{64}', record['owner']): self.reject('FILES_RECEIPT_UNSAFE')
                self.validate(record['plan'])
                for key in ('deletionStarted', 'archiveRemoved', 'originalsRemoved'):
                    if key in record and type(record[key]) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
                if record.get('currentPath') is not None: self.f.relative(record['currentPath'], empty=True)
                if (record.get('archiveRemoved') or record.get('originalsRemoved')) and not record.get('deletionStarted'): self.reject('FILES_RECEIPT_UNSAFE')
                for component in ('Archive', 'Originals'):
                    if record.get(component.lower() + 'Removed') and not self.wants(record['plan']['command'], component): self.reject('FILES_RECEIPT_UNSAFE')
                if record['state'] == 'Completed' and (not record.get('deletionStarted') or any(self.wants(record['plan']['command'], component) and not record.get(component.lower() + 'Removed') for component in ('Archive', 'Originals'))): self.reject('FILES_RECEIPT_UNSAFE')
                if record['state'] == 'Aborted' and record.get('deletionStarted'): self.reject('FILES_RECEIPT_UNSAFE')
                if record['state'] not in ('Preparing', 'Aborted') and not record.get('deletionStarted'): self.reject('FILES_RECEIPT_UNSAFE')
            except (KeyError, ValueError, TypeError): self.reject('FILES_RECEIPT_UNSAFE')
            return record

        def request(self, request):
            if not isinstance(request, dict) or set(request) != {'operationId', 'owner', 'plan'}: self.reject('FILES_TREE_PURGE_INVALID')
            self.validate(request['plan']); self.f.identity(request['operationId'])
            if request['operationId'] == request['plan']['command']['captureId']: self.reject('FILES_TREE_PURGE_INVALID')

        def pinned(self, record, parent, private, root, name):
            plan = record['plan']
            self.bound(plan)
            with self.directory(plan['command']['source']) as (_, current_parent, _):
                if self.identity(current_parent) != plan['sourceParent'] or self.identity(parent) != plan['sourceParent']: self.reject('FILES_PARENT_CHANGED')
                current_private = self.f.open_at(current_parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY)
                try:
                    info = os.fstat(current_private)
                    if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700 or self.identity(current_private) != plan['stagingIdentity'] or self.identity(private) != plan['stagingIdentity']: self.reject('FILES_STAGING_UNSAFE')
                    current = os.stat(name, dir_fd=current_private, follow_symlinks=False)
                    if not stat.S_ISDIR(current.st_mode) or [current.st_dev, current.st_ino] != plan['retainedIdentity'] or self.identity(root) != plan['retainedIdentity']: self.reject('FILES_TREE_CHANGED')
                finally: os.close(current_private)

        def store_bound(self, plan, store):
            with self.captures.b.target() as (configuration, base):
                binding = {key: configuration[key] for key in ('storageId', 'mountPoint', 'filesystemUuid', 'targetRelative')}
                if self.captures.digest(binding) != plan['storageHash']: self.reject('FILES_BACKUP_DEVICE_MISMATCH')
                current = self.f.open_at(base, self.captures.FOLDER, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    info = os.fstat(current)
                    if self.identity(current) != plan['storeIdentity'] or self.identity(store) != plan['storeIdentity'] or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                        self.reject('FILES_TREE_BACKUP_STORE_UNSAFE')
                finally: os.close(current)

        def remove_originals(self, state, record, store, configuration, saved, check):
            plan = record['plan']; command = plan['command']; manifest = self.captures.manifest(store, saved)
            old_name = command['captureId'] + ('.tree-trash' if command['captureKind'] == 'Trash' else '.tree-overwrite-restore')
            name = record['operationId'] + self.SUFFIX
            with self.retained(command, plan['sourceParent'], plan['stagingIdentity']) as (parent, private, root):
                actual = self.inventory(command, root, manifest, plan['retainedIdentity'], check)
                if self.tree_hash(actual) != plan['retainedTreeSha256']: self.reject('FILES_TREE_CHANGED')
                self.pinned(record, parent, private, root, old_name); self.store_bound(plan, store); check()
                record.update(state='OriginalsQuarantining', deletionStarted=True); self.save(state, record); self.checkpoint('OriginalsQuarantining')
                self.pinned(record, parent, private, root, old_name); self.store_bound(plan, store); check()
                self.f.exchange(private, old_name, private, name, 1); os.fsync(private); self.checkpoint('OriginalsQuarantined')
                self.pinned(record, parent, private, root, name)
                quarantined = self.inventory(command, root, manifest, plan['retainedIdentity'], check)
                if self.tree_hash(quarantined) != plan['retainedTreeSha256']: self.reject('FILES_TREE_CHANGED')
                directories = {row['path']: row for row in actual['entries'] if row['kind'] == 'Directory'}
                for row in actual['entries']:
                    if row['kind'] != 'File': continue
                    check(); self.pinned(record, parent, private, root, name); path, _, leaf = row['path'].rpartition('/')
                    self.io.check_ancestors(self, root, path, directories)
                    descriptor = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY)
                    try:
                        record.update(state='OriginalsRemoving', currentPath=row['path']); self.save(state, record); self.checkpoint('BeforeOriginalUnlink')
                        check(); self.pinned(record, parent, private, root, name); self.io.check_ancestors(self, root, path, directories)
                        if self.io.file_hash(self, descriptor, leaf, row['sourceIdentity'], check) != row['sha256']: self.reject('FILES_TREE_CHANGED')
                        self.store_bound(plan, store); self.pinned(record, parent, private, root, name)
                        os.unlink(leaf, dir_fd=descriptor); os.fsync(descriptor); self.checkpoint('OriginalUnlinked')
                    finally: os.close(descriptor)
                for row in reversed(actual['entries']):
                    if row['kind'] != 'Directory': continue
                    check(); self.pinned(record, parent, private, root, name); path, _, leaf = row['path'].rpartition('/')
                    self.io.check_ancestors(self, root, path, directories)
                    descriptor = self.f.open_at(root, path or '.', os.O_RDONLY | os.O_DIRECTORY) if row['path'] else os.dup(private)
                    try:
                        if not row['path']: leaf = name
                        current = os.stat(leaf, dir_fd=descriptor, follow_symlinks=False)
                        if not stat.S_ISDIR(current.st_mode) or self.f.publication_identity(current)[:5] != row['sourceIdentity'][:5]: self.reject('FILES_TREE_CHANGED')
                        self.store_bound(plan, store)
                        os.rmdir(leaf, dir_fd=descriptor); os.fsync(descriptor); self.checkpoint('OriginalDirectoryRemoved')
                    finally: os.close(descriptor)
                self.store_bound(plan, store)
                saved.update(originalsPurged=True, originalsPurgedBy=record['operationId'])
                self.f.save_receipt(store, command['captureId'], saved)
                record.update(originalsRemoved=True); record.pop('currentPath', None); self.save(state, record); self.checkpoint('OriginalsRemoved')

        def remove_archive(self, state, record, store, configuration, saved, check):
            command, plan = record['plan']['command'], record['plan']; original = command['captureId'] + '.tar.gz'; name = record['operationId'] + '.purging.tar.gz'
            codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
            self.captures.verify(store, saved, codec); self.store_bound(plan, store); self.bound(plan); check()
            record.update(state='ArchiveQuarantining', deletionStarted=True); self.save(state, record); self.checkpoint('ArchiveQuarantining')
            self.captures.verify(store, saved, codec); self.store_bound(plan, store); self.bound(plan); check()
            self.f.exchange(store, original, store, name, 1); os.fsync(store); self.checkpoint('ArchiveQuarantined')
            descriptor = self.f.open_at(store, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
            try:
                info = codec.regular(descriptor, private=True)
                result = codec.inspect(descriptor)
                if self.identity(descriptor) != plan['archiveIdentity'] or any(result[key] != plan[key] for key in ('archiveBytes', 'archiveSha256', 'contentSha256', 'entryCount', 'totalBytes')): self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                record['state'] = 'ArchiveRemoving'; self.save(state, record); self.checkpoint('BeforeArchiveUnlink'); check()
                self.store_bound(plan, store)
                self.bound(plan)
                if self.f.version(os.fstat(descriptor)) != self.f.version(info) or self.f.version(os.stat(name, dir_fd=store, follow_symlinks=False)) != self.f.version(info): self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                os.unlink(name, dir_fd=store); os.fsync(store); self.checkpoint('ArchiveUnlinked')
            finally: os.close(descriptor)
            self.store_bound(plan, store)
            saved.update(state='Purged', archivePurgedBy=record['operationId'])
            self.f.save_receipt(store, command['captureId'], saved)
            record.update(archiveRemoved=True); self.save(state, record); self.checkpoint('ArchiveRemoved')

        def verify_removed(self, record, store, configuration, saved):
            plan = record['plan']; command = plan['command']; self.store_bound(plan, store); self.bound(plan)
            current = self.capture(store, configuration, command)
            if self.digest(current) != self.digest(saved): self.reject('FILES_TREE_CHANGED')
            if self.wants(command, 'Archive'):
                if current['state'] != 'Purged' or current.get('archivePurgedBy') != record['operationId']: self.reject('FILES_RECEIPT_UNSAFE')
                if any(self.exists(store, name) for name in (command['captureId'] + '.tar.gz', record['operationId'] + '.purging.tar.gz')): self.reject('FILES_TREE_CHANGED')
            if self.wants(command, 'Originals'):
                if not current.get('originalsPurged') or current.get('originalsPurgedBy') != record['operationId']: self.reject('FILES_RECEIPT_UNSAFE')
                with self.directory(command['source']) as (_, parent, _):
                    if self.identity(parent) != plan['sourceParent']: self.reject('FILES_PARENT_CHANGED')
                    private = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                    try:
                        info = os.fstat(private)
                        if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700 or self.identity(private) != plan['stagingIdentity']: self.reject('FILES_STAGING_UNSAFE')
                        original = command['captureId'] + ('.tree-trash' if command['captureKind'] == 'Trash' else '.tree-overwrite-restore')
                        if any(self.exists(private, name) for name in (original, record['operationId'] + self.SUFFIX)): self.reject('FILES_TREE_CHANGED')
                    finally: os.close(private)

        def purge(self, request):
            self.request(request)
            with self.state(request['operationId'], request['owner'], True) as (state, record):
                if record is not None:
                    if record['plan'] != request['plan']: self.reject('FILES_IDEMPOTENCY_CONFLICT')
                    self.recover(state, record); return self.public(record)
                plan = request['plan']; command = plan['command']; check = self.guard(plan)
                with self.captures.storage(self.capture_request(command), existing=True) as (configuration, store):
                    saved = self.capture(store, configuration, command)
                    expected = {key: value for key, value in plan.items() if key not in ('command', 'sourcePolicyHash', 'fingerprint')}
                    if self.snapshot(command, store, configuration, saved, check) != expected: self.reject('FILES_TREE_CHANGED')
                    record = {**request, 'state': 'Preparing'}; self.save(state, record); self.checkpoint('Preparing')
                    try:
                        # Originals first keeps the verified HDD archive intact
                        # throughout each original unlink, even in All mode.
                        if self.wants(command, 'Originals'): self.remove_originals(state, record, store, configuration, saved, check)
                        if self.wants(command, 'Archive'): self.remove_archive(state, record, store, configuration, saved, check)
                        record['state'] = 'Completing'; self.save(state, record); self.checkpoint('Completing')
                        check(); self.verify_removed(record, store, configuration, saved)
                        record['state'] = 'Completed'; self.save(state, record); self.checkpoint('Completed')
                    except BaseException:
                        with contextlib.suppress(OSError, self.f.Rejected): self.recover(state, record)
                        raise
                return self.public(record)

        def recover(self, folder, record):
            if record['state'] in self.TERMINAL: return
            # Never re-open a quarantine for deletion, regenerate a plan or
            # infer success from a path simply being absent.
            record['state'] = 'Unknown' if record.get('deletionStarted') else 'Aborted'
            self.save(folder, record)

        @staticmethod
        def public(record):
            plan = record['plan']; command = plan['command']
            return {'operationId': record['operationId'], 'kind': 'DirectoryPurge', 'state': record['state'],
                'fingerprint': plan['fingerprint'], 'captureId': command['captureId'], 'scope': command['scope'],
                'entryCount': plan['entryCount'], 'totalBytes': plan['totalBytes'], 'archiveBytes': plan['archiveBytes'],
                'deletionStarted': bool(record.get('deletionStarted')), 'archiveRemoved': bool(record.get('archiveRemoved')),
                'originalsRemoved': bool(record.get('originalsRemoved'))}

    return FileTreePurging
