#!/usr/bin/env python3
"""Internal overwrite restoration: verified HDD history + atomic directory swap.

The fixed helper and signed Agent RPC expose only plan/apply/status/discard.
A later application workflow must add current-user authorization,
confirmation, hierarchy locks, jobs, history registration and audit before
HTTP/UI exposure. Never deletes the displaced directory or a
capture; never resumes/retries a swap after interruption. A retained original
and an independently verified HDD capture precede a successful outcome.
"""
import contextlib
import copy
import hashlib
import os
import re
import stat


def overwrite_restoration_type(restoring_type):
    class FileTreeOverwriteRestoring(restoring_type):
        DOMAIN = b'whaledeck-tree-overwrite-restoring-v1\n'
        FOLDER = 'tree-overwrite-restoring'
        TEMPORARY_SUFFIX = '.tree-overwrite-restore'
        PLAN_FIELDS = restoring_type.PLAN_FIELDS | {'targetManifestSha256', 'targetTreeSha256',
            'targetEntryCount', 'targetTotalBytes', 'targetIdentity', 'targetContentSha256'}
        RECORD_FIELDS = restoring_type.RECORD_FIELDS | {'backupStarted', 'backupVerified', 'backupArchiveBytes',
            'backupArchiveSha256', 'swapStarted', 'originalsRetained'}
        TARGET_ACTIONS = restoring_type.TARGET_ACTIONS | {'files-list', 'files-download', 'files-history', 'files-tree-overwrite-restore'}

        def validate(self, command):
            if not isinstance(command, dict) or not isinstance(command.get('target'), dict) or set(command['target']) != {'rootId', 'path', 'expectedVersion'}:
                self.reject('FILES_TREE_OVERWRITE_INVALID')
            expected = command['target']['expectedVersion']
            if type(expected) is not str or not re.fullmatch('[A-F0-9]{64}', expected):
                self.reject('FILES_VERSION_CONFLICT')
            base = {**command, 'target': {key: value for key, value in command['target'].items() if key != 'expectedVersion'}}
            super().validate(base)

        def target(self, command):
            return self.f.directory(command['target'], ('list', 'download', 'mutate', 'upload', 'archive', 'history', 'tree-restore', 'tree-overwrite-restore'))

        def target_inventory(self, command, descriptor, check):
            policy = self.policies(command)[1]
            target = {**command['target'], 'expectedVersion': self.f.version(os.fstat(descriptor))}
            return self.manifest_type(self.f, timeout_seconds=600, cancelled=check)._scan_descriptor(target, descriptor, policy)

        def tree_digest(self, inventory):
            # rename changes ctime, not these identities, metadata or full bytes.
            return self.digest([{key: row[key] for key in ('path', 'kind', 'sourceIdentity', 'sizeBytes', 'sha256')}
                for row in inventory['entries']])

        def target_proof(self, command, parent, leaf, check):
            target = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                before = os.fstat(target)
                if self.f.version(before) != command['target']['expectedVersion']: self.reject('FILES_VERSION_CONFLICT')
                inventory = self.target_inventory(command, target, check)
                if self.f.version(os.stat(leaf, dir_fd=parent, follow_symlinks=False)) != self.f.version(before):
                    self.reject('FILES_TREE_CHANGED')
                return {'targetManifestSha256': inventory['manifestSha256'], 'targetTreeSha256': self.tree_digest(inventory),
                    'targetContentSha256': hashlib.sha256(b'whaledeck-tree-archive-content-v1\n' + self.encode([self.codec_type.from_manifest(row) for row in inventory['entries']])).hexdigest().upper(),
                    'targetEntryCount': inventory['entryCount'], 'targetTotalBytes': inventory['totalBytes'],
                    'targetIdentity': self.identity(target)}
            finally: os.close(target)

        def validate_plan(self, plan):
            # Preserve read-only inspection of root-owned pre-1.35 receipts.
            # The request boundary below never permits their replay or fencing
            # via a new Apply/Discard request lacking the new signed fact.
            legacy = isinstance(plan, dict) and 'targetContentSha256' not in plan
            if legacy:
                reader = copy.copy(self); reader.PLAN_FIELDS = self.PLAN_FIELDS - {'targetContentSha256'}
                super(FileTreeOverwriteRestoring, reader).validate_plan(plan)
            else: super().validate_plan(plan)
            for key in ('targetManifestSha256', 'targetTreeSha256'):
                if type(plan[key]) is not str or not re.fullmatch('[A-F0-9]{64}', plan[key]): self.reject('FILES_PLAN_INVALID')
            if not legacy and (type(plan['targetContentSha256']) is not str or not re.fullmatch('[A-F0-9]{64}', plan['targetContentSha256'])): self.reject('FILES_PLAN_INVALID')
            identity = plan['targetIdentity']
            if not isinstance(identity, list) or len(identity) != 2 or any(type(value) is not int or not 0 <= value < 2**64 for value in identity) or not identity[1]:
                self.reject('FILES_PLAN_INVALID')
            if type(plan['targetEntryCount']) is not int or not 1 <= plan['targetEntryCount'] <= 10000 or type(plan['targetTotalBytes']) is not int or not 0 <= plan['targetTotalBytes'] <= 16 * 1024**3:
                self.reject('FILES_PLAN_INVALID')

        def request(self, request):
            super().request(request)
            if 'targetContentSha256' not in request['plan']: self.reject('FILES_PLAN_INVALID')

        def read(self, folder, operation):
            record = super().read(folder, operation)
            if record is None: return None
            for key in ('backupStarted', 'backupVerified', 'swapStarted', 'originalsRetained'):
                if key in record and type(record[key]) is not bool: self.reject('FILES_RECEIPT_UNSAFE')
            if record.get('backupVerified'):
                if not record.get('backupStarted') or type(record.get('backupArchiveBytes')) is not int or not 1 <= record['backupArchiveBytes'] <= 17 * 1024**3 or type(record.get('backupArchiveSha256')) is not str or not re.fullmatch('[A-F0-9]{64}', record['backupArchiveSha256']):
                    self.reject('FILES_RECEIPT_UNSAFE')
            elif any(key in record for key in ('backupArchiveBytes', 'backupArchiveSha256')):
                self.reject('FILES_RECEIPT_UNSAFE')
            if record.get('swapStarted') and not record.get('backupVerified') or record.get('originalsRetained') and not record.get('swapStarted'):
                self.reject('FILES_RECEIPT_UNSAFE')
            if record['state'] == 'Completed' and not all(record.get(key) for key in ('backupVerified', 'swapStarted', 'originalsRetained')):
                self.reject('FILES_RECEIPT_UNSAFE')
            return record

        @staticmethod
        def backup_request(record):
            return {'operationId': record['operationId'], 'owner': record['owner'], 'source': record['plan']['command']['target'],
                'manifestSha256': record['plan']['targetManifestSha256'], 'captureKind': 'Overwrite'}

        @contextlib.contextmanager
        def verified_backup(self, record, check):
            request = self.backup_request(record)
            with self.captures.storage(request, existing=True) as (configuration, folder):
                saved = self.captures.read(folder, request['operationId'], self.captures.seed(request, self.captures.request(request), configuration, folder))
                if saved is None or saved['state'] != 'Verified' or any(saved.get(key) != record.get('backup' + key[0].upper() + key[1:]) for key in ('archiveBytes', 'archiveSha256')):
                    self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                if 'targetContentSha256' in record['plan'] and saved['contentSha256'] != record['plan']['targetContentSha256']: self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
                codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
                self.captures.verify(folder, saved, codec); self.captures.b.attest(configuration, folder); check()
                yield
                self.captures.verify(folder, saved, codec); self.captures.b.attest(configuration, folder); check()

        def retained_tree(self, plan, parent, private, temporary, check):
            visible = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                info = os.fstat(visible)
                if self.identity(visible) != self.identity(private) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                    self.reject('FILES_STAGING_UNSAFE')
            finally: os.close(visible)
            old = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            try:
                if self.identity(old) != plan['targetIdentity'] or self.tree_digest(self.target_inventory(plan['command'], old, check)) != plan['targetTreeSha256']:
                    self.reject('FILES_TREE_CHANGED')
            finally: os.close(old)

        def publish_target(self, folder, record, private, temporary, parent, leaf, check):
            plan = record['plan']; check(); self.bound(plan)
            if self.target_proof(plan['command'], parent, leaf, check) != {key: plan[key] for key in self.PLAN_FIELDS - restoring_type.PLAN_FIELDS}:
                self.reject('FILES_TREE_CHANGED')
            record['backupStarted'] = True; self.save(folder, record); self.checkpoint('BackupStarting')
            writer = copy.copy(self.captures); writer.cancelled = check
            backup = writer.ensure(self.backup_request(record))
            if backup['state'] != 'Verified': self.reject('FILES_TREE_BACKUP_INCOMPLETE')
            if backup['contentSha256'] != plan['targetContentSha256']: self.reject('FILES_TREE_BACKUP_CONTENT_INVALID')
            record.update(backupVerified=True, backupArchiveBytes=backup['archiveBytes'], backupArchiveSha256=backup['archiveSha256'])
            self.save(folder, record); self.checkpoint('BackupVerified')
            with self.verified_backup(record, check):
                if self.target_proof(plan['command'], parent, leaf, check) != {key: plan[key] for key in self.PLAN_FIELDS - restoring_type.PLAN_FIELDS}:
                    self.reject('FILES_TREE_CHANGED')
                # Re-attest all participants after potentially slow HDD capture.
                codec = self.codec_type(self.f, self.manifest_type, cancelled=check)
                inventory = self.inspect_source(plan, codec, check); policy = self.bound(plan)
                staged = self.f.open_at(private, temporary, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    if self.identity(staged) != record['newIdentity'] or self.snapshot(staged, plan, policy, check, inventory) != record['treeSha256']:
                        self.reject('FILES_ARCHIVE_CONTENT_INVALID')
                finally: os.close(staged)
                with self.target(plan['command']) as (_, current, _):
                    if self.identity(current) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                visible = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                try:
                    info = os.fstat(visible)
                    if self.identity(visible) != self.identity(private) or info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                        self.reject('FILES_STAGING_UNSAFE')
                finally: os.close(visible)
                check(); self.bound(plan)
                record['swapStarted'] = True; self.save(folder, record); self.checkpoint('SwapStarting')
                check()
                self.f.exchange(private, temporary, parent, leaf, 2)
                os.fsync(parent); os.fsync(private); self.checkpoint('Swapped')
                self.retained_tree(plan, parent, private, temporary, check)
                record['originalsRetained'] = True; self.save(folder, record)

        def recover(self, folder, record):
            if record['state'] in self.TERMINAL: return
            record['state'] = 'Unknown'
            plan = record['plan']; check = self.guard(cancellation=False)
            try:
                policy = self.bound(plan)
                with self.target(plan['command']) as (_, parent, leaf):
                    if self.identity(parent) != plan['targetParent']: self.reject('FILES_PARENT_CHANGED')
                    target = self.f.open_at(parent, leaf, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                    try:
                        if record.get('swapStarted') and self.identity(target) == record.get('newIdentity') and self.snapshot(target, plan, policy, check) == record.get('treeSha256'):
                            private = self.f.open_at(parent, self.f.STAGING, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
                            try:
                                with self.verified_backup(record, check):
                                    self.retained_tree(plan, parent, private, record['operationId'] + self.TEMPORARY_SUFFIX, check)
                                self.bound(plan)
                                record.update(state='Completed', originalsRetained=True, version=self.f.version(os.fstat(target)))
                            finally: os.close(private)
                        elif not record.get('swapStarted') and self.identity(target) == plan['targetIdentity'] and self.f.version(os.fstat(target)) == plan['command']['target']['expectedVersion'] and self.tree_digest(self.target_inventory(plan['command'], target, check)) == plan['targetTreeSha256']:
                            record['state'] = 'Aborted'
                    finally: os.close(target)
            except (OSError, self.f.Rejected): pass
            self.save(folder, record)

        def public(self, record):
            result = super().public(record)
            return {**result, 'kind': 'DirectoryOverwriteRestore', 'overwrittenCaptureId': record['operationId'] if record.get('backupVerified') else '',
                'backupMayBeRetained': bool(record.get('backupStarted')), 'backupVerified': bool(record.get('backupVerified')),
                'originalsRetained': bool(record.get('originalsRetained')), 'targetMayHaveChanged': bool(record.get('swapStarted')),
                'backupArchiveBytes': record.get('backupArchiveBytes', 0), 'backupArchiveSha256': record.get('backupArchiveSha256', '')}

    return FileTreeOverwriteRestoring
