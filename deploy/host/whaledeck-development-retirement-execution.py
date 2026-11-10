"""Internal source retirement engine, deliberately without CLI/install/RPC.

A caller must independently authorize the current user and exact execution.
Only a new, dedicated QA allocation may exercise this candidate on a host.
The permanent fence and retained journal are never undone on failure. A partial
execution has no completion receipt and must keep its application budget.
"""
import contextlib
import hashlib
import os
import stat

DIR = os.O_RDONLY | os.O_DIRECTORY
READ = os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW
MAX_RECORD = 32 * 1024**2


class DevelopmentRetirementExecution:
    def __init__(self, archives, payload_files_type):
        self.a, self.d = archives, archives.d
        self.payload_files_type = payload_files_type

    def reject(self, code):
        self.d.reject('RETIREMENT_' + code)

    def digest(self, value):
        return hashlib.sha256(self.d.canonical(value)).hexdigest()

    def exists(self, parent, name):
        try:
            return os.stat(name, dir_fd=parent, follow_symlinks=False)
        except FileNotFoundError:
            return None

    def durable_read(self, parent, name, maximum, *, expected=None):
        value = self.d.read_json(parent, name, maximum, expected=expected, sync=True)
        # A prior rename may be visible after its directory fsync failed.
        # Reading that name is not a durable checkpoint until this retry also
        # syncs its directory, before another irreversible step may proceed.
        os.fsync(parent)
        return value

    def write(self, parent, name, value):
        """Immutable, prefix-recoverable publication, bounded before any write."""
        raw = self.d.canonical(value)
        if len(raw) > MAX_RECORD:
            self.reject('JOURNAL_LIMIT')
        try:
            self.durable_read(parent, name, MAX_RECORD, expected=value)
            return
        except FileNotFoundError:
            pass
        pending = '.' + name + '.pending'
        try:
            fd = os.open(pending, os.O_RDWR | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
        except FileExistsError:
            fd = self.d.files.open_at(parent, pending, os.O_RDWR | os.O_NOFOLLOW)
        with os.fdopen(fd, 'r+b') as stream:
            info = os.fstat(stream.fileno())
            if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                    or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size > len(raw)):
                self.reject('JOURNAL_CONFLICT')
            old = stream.read(len(raw) + 1)
            if not raw.startswith(old):
                self.reject('JOURNAL_CONFLICT')
            stream.write(raw[len(old):]); stream.flush(); os.fsync(stream.fileno())
        self.d.files.exchange(parent, pending, parent, name, 1)
        os.fsync(parent)

    def validate(self, value):
        if not isinstance(value, dict) or set(value) != {'schemaVersion', 'retirementId', 'prerequisite', 'retirementPlanSha256'}:
            self.reject('REQUEST_INVALID')
        if type(value['schemaVersion']) is not int or value['schemaVersion'] != 1:
            self.reject('REQUEST_INVALID')
        self.a.operation(value['retirementId'])
        # Reuse the strict existing six-field read-only prerequisite policy.
        # The module is supplied by the trusted caller, not loaded from input.
        if not isinstance(value['retirementPlanSha256'], str) or len(value['retirementPlanSha256']) != 64 or any(c not in '0123456789abcdef' for c in value['retirementPlanSha256']):
            self.reject('REQUEST_INVALID')

    def plan(self, prerequisite, parent, config):
        d, a = self.d, self.a
        storage = prerequisite['storage']; workspace = storage['workspaceId']
        with d.opened(parent, workspace, DIR) as allocation, d.opened(allocation, 'data', DIR) as payload:
            d.directory(allocation, private=True); d.directory(payload, private=True)
            original = d.receipt(parent, allocation, storage, config)
            d.read_json(allocation, 'allocation.json', 4096, expected=original)
            if set(os.listdir(allocation)) != {'allocation.json', 'data'}:
                self.reject('ALLOCATION_CHANGED')
            proof = d.quota_claim(parent, payload, original, config, prepare=False)
            d.quota.verify(payload, proof['projectId'], proof['diskMiB'])
            lifecycle = d.read_json(parent, '.lifecycle-' + workspace + '.json', 4096)
            if (lifecycle.get('state') != 'Stopped' or lifecycle.get('epoch') != prerequisite['expectedEpoch']
                    or any(lifecycle.get(k) != v for k, v in storage.items())
                    or lifecycle.get('storageProofSha256') != self.digest(proof)):
                self.reject('NOT_STOPPED')
            command = dict(rootId='development-private-payload', path='data', expectedVersion=d.files.version(os.fstat(payload)))
            # The trusted caller supplies the same private adapter as capture.
            adapter = self.payload_adapter(allocation, command, proof)
            manifest = a.manifest_type(adapter, max_bytes=16 * 1024**3).scan(command)
            a.require_metadata(payload, manifest)
            with a.target.locked(exclusive=False) as (target, hdd):
                if os.fstat(payload).st_dev == os.fstat(target).st_dev:
                    self.reject('SEPARATE_STORAGE_REQUIRED')
                verified = self.verify_archive(prerequisite)
                facts = a.facts(storage, prerequisite['expectedEpoch'], manifest, proof, hdd)
                if any(verified.get(k) != v for k, v in facts.items()):
                    self.reject('SOURCE_CHANGED')
                plan = dict(facts, purpose='whaledeck-development-source-retirement-plan-v1',
                    archiveId=prerequisite['archiveId'], archivePlanSha256=prerequisite['archivePlanSha256'],
                    captureReceiptSha256=prerequisite['captureReceiptSha256'],
                    archiveSha256=verified['archiveSha256'], archiveIndexSha256=verified['archiveIndexSha256'],
                    archiveBytes=verified['archiveBytes'], archiveInode=verified['archiveInode'],
                    retainsSource=True, reservesBudget=True, deletionAuthorized=False)
                plan['retirementPlanSha256'] = self.digest(plan)
                a.manifest_type(adapter, max_bytes=16 * 1024**3).verify(command, manifest['manifestSha256'])
                a.target.recheck(target, hdd)
            d.recheck(parent, storage['rootId'], config)
            return dict(plan=plan, proof=proof, lifecycle=lifecycle, manifest=manifest,
                allocationIdentity=d.files.publication_identity(os.fstat(allocation)),
                markerIdentity=d.files.publication_identity(os.stat('allocation.json', dir_fd=allocation, follow_symlinks=False)),
                claimIdentity=d.files.publication_identity(os.stat('.quota-' + str(proof['projectId']) + '.json', dir_fd=parent, follow_symlinks=False)))

    def payload_adapter(self, allocation, command, proof):
        return self.payload_files_type(self.d.files, allocation, command,
            dict(allowedActions=['files-list', 'files-download'], storageProofSha256=self.digest(proof)))

    def verify_archive(self, p):
        return self.a.verify(dict(storage=p['storage'], expectedEpoch=p['expectedEpoch'],
            operationId=p['archiveId'], planSha256=p['archivePlanSha256']), p['captureReceiptSha256'])

    def intent(self, journal, index, row):
        self.write(journal, 'remove-' + str(index) + '.json', dict(index=index, entry=row))

    def has_intent(self, journal, index, row):
        try:
            self.durable_read(journal, 'remove-' + str(index) + '.json', MAX_RECORD, expected=dict(index=index, entry=row))
            return True
        except FileNotFoundError:
            return False

    def directory_matches(self, fd, row):
        identity = self.d.files.publication_identity(os.fstat(fd))
        if identity[:5] != row['sourceIdentity'][:5] or not stat.S_ISDIR(identity[2]) or os.listxattr(fd):
            self.reject('SOURCE_CHANGED')

    @contextlib.contextmanager
    def parent(self, payload, path, rows, indexes, journal):
        with contextlib.ExitStack() as stack:
            current, relative = payload, ''
            for name in path.split('/')[:-1]:
                relative = relative + '/' + name if relative else name
                try:
                    current = stack.enter_context(self.d.opened(current, name, DIR))
                except FileNotFoundError:
                    if not self.has_intent(journal, indexes[relative], rows[relative]):
                        self.reject('SOURCE_MISSING_WITHOUT_INTENT')
                    yield None
                    return
                self.directory_matches(current, rows[relative])
            yield current

    def remove_tree(self, allocation, journal, manifest):
        entries = manifest['entries']; rows = {r['path']: r for r in entries}; indexes = {r['path']: i for i, r in enumerate(entries)}
        if not self.exists(allocation, 'data'):
            if not all(self.has_intent(journal, i, r) for i, r in enumerate(entries)):
                self.reject('SOURCE_MISSING_WITHOUT_INTENT')
            return
        with self.d.opened(allocation, 'data', DIR) as payload:
            self.directory_matches(payload, rows[''])
            for index in reversed(range(1, len(entries))):
                row = entries[index]; present_intent = self.has_intent(journal, index, row)
                with self.parent(payload, row['path'], rows, indexes, journal) as parent:
                    leaf = row['path'].rsplit('/', 1)[-1]
                    info = self.exists(parent, leaf) if parent is not None else None
                    if info is None:
                        if not present_intent:
                            self.reject('SOURCE_MISSING_WITHOUT_INTENT')
                        continue
                    flags = DIR if row['kind'] == 'Directory' else READ
                    with self.d.opened(parent, leaf, flags) as fd:
                        if row['kind'] == 'Directory':
                            self.directory_matches(fd, row)
                            if os.listdir(fd): self.reject('SOURCE_CHANGED')
                        else:
                            if self.d.files.version(os.fstat(fd)) != row['sourceVersion']:
                                self.reject('SOURCE_CHANGED')
                            with os.fdopen(os.dup(fd), 'rb') as stream:
                                if hashlib.file_digest(stream, 'sha256').hexdigest().upper() != row['sha256']:
                                    self.reject('SOURCE_CHANGED')
                            if self.d.files.version(os.fstat(fd)) != row['sourceVersion']:
                                self.reject('SOURCE_CHANGED')
                        self.intent(journal, index, row)
                        if not os.path.samestat(os.fstat(fd), os.stat(leaf, dir_fd=parent, follow_symlinks=False)):
                            self.reject('SOURCE_CHANGED')
                        (os.rmdir if row['kind'] == 'Directory' else os.unlink)(leaf, dir_fd=parent)
                        os.fsync(parent)
            self.directory_matches(payload, rows[''])
            if os.listdir(payload): self.reject('SOURCE_CHANGED')
            self.intent(journal, 0, rows[''])
        os.rmdir('data', dir_fd=allocation); os.fsync(allocation)

    def run(self, value, prerequisite_policy):
        self.validate(value); p = value['prerequisite']; prerequisite_policy.validate(p)
        root, workspace = self.d.validate_request(p['storage'])
        if value['retirementId'] in (workspace, p['archiveId']): self.reject('REQUEST_INVALID')
        binding = self.digest(value)
        with self.d.locked_root(root, exclusive=True) as (parent, config):
            name = '.retirement-' + workspace
            if not self.exists(parent, name):
                prepared = self.plan(p, parent, config)
                if prepared['plan']['retirementPlanSha256'] != value['retirementPlanSha256']:
                    self.reject('PLAN_CHANGED')
                prepared.update(bindingSha256=binding, request=value)
                if len(self.d.canonical(prepared)) > MAX_RECORD: self.reject('JOURNAL_LIMIT')
                os.mkdir(name, 0o700, dir_fd=parent); os.fsync(parent)
            with self.d.opened(parent, name, DIR) as journal:
                self.d.directory(journal, private=True)
                try:
                    prepared = self.durable_read(journal, 'prepared.json', MAX_RECORD)
                except FileNotFoundError:
                    # No irreversible work precedes this exact durable record.
                    prepared = self.plan(p, parent, config)
                    if prepared['plan']['retirementPlanSha256'] != value['retirementPlanSha256']:
                        self.reject('PLAN_CHANGED')
                    prepared.update(bindingSha256=binding, request=value)
                    self.write(journal, 'prepared.json', prepared)
                if prepared['bindingSha256'] != binding or prepared['request'] != value:
                    self.reject('REQUEST_CHANGED')
                try:
                    return self.durable_read(journal, 'receipt.json', 8192, expected=self.receipt(value, prepared))
                except FileNotFoundError:
                    pass
                with self.a.target.locked(exclusive=False) as (target, hdd):
                    verified = self.verify_archive(p)
                    for k in ('archiveSha256','archiveIndexSha256','archiveInode','archiveBytes','hddProofSha256','archiveFilesystemUuid'):
                        if verified[k] != prepared['plan'][k]: self.reject('ARCHIVE_CHANGED')
                    original = self.exists(parent, workspace); detached = self.exists(journal, 'source')
                    if original is not None:
                        if detached is not None: self.reject('SOURCE_CHANGED')
                        current = self.plan(p, parent, config)
                        if current != {k:v for k,v in prepared.items() if k not in ('bindingSha256','request')}:
                            self.reject('SOURCE_CHANGED')
                        self.d.files.exchange(parent, workspace, journal, 'source', 1)
                        os.fsync(journal); os.fsync(parent)
                    if self.exists(journal, 'source'):
                        with self.d.opened(journal, 'source', DIR) as allocation:
                            if self.d.files.publication_identity(os.fstat(allocation))[:5] != prepared['allocationIdentity'][:5]:
                                self.reject('ALLOCATION_CHANGED')
                            self.remove_tree(allocation, journal, prepared['manifest'])
                            marker = self.exists(allocation, 'allocation.json')
                            if marker:
                                if self.d.files.publication_identity(marker) != prepared['markerIdentity']: self.reject('ALLOCATION_CHANGED')
                                self.intent(journal, 'allocation', dict(proof=prepared['proof']))
                                os.unlink('allocation.json', dir_fd=allocation); os.fsync(allocation)
                            elif not self.has_intent(journal, 'allocation', dict(proof=prepared['proof'])):
                                self.reject('ALLOCATION_CHANGED')
                            if os.listdir(allocation): self.reject('ALLOCATION_CHANGED')
                        os.rmdir('source', dir_fd=journal); os.fsync(journal)
                    elif not self.has_intent(journal, 'allocation', dict(proof=prepared['proof'])):
                        self.reject('SOURCE_MISSING_WITHOUT_INTENT')
                    # No source FD survives here: open-unlinked inodes must keep
                    # quota nonzero and prevent a completion receipt.
                    proof = prepared['proof']; claim = '.quota-' + str(proof['projectId']) + '.json'
                    released = dict(requestSha256=binding, projectId=proof['projectId'])
                    try:
                        self.durable_read(journal, 'quota-released.json', 4096, expected=released)
                    except FileNotFoundError:
                        self.d.read_json(parent, claim, 4096, expected=proof)
                        if self.d.files.publication_identity(os.stat(claim, dir_fd=parent, follow_symlinks=False)) != prepared['claimIdentity']:
                            self.reject('CLAIM_CHANGED')
                        self.d.quota.release_empty(parent, proof['projectId'], proof['diskMiB'])
                        self.write(journal, 'quota-released.json', released)
                    if self.exists(parent, claim):
                        current = self.d.read_json(parent, claim, 4096)
                        if current == proof:
                            if self.d.files.publication_identity(os.stat(claim, dir_fd=parent, follow_symlinks=False)) != prepared['claimIdentity']:
                                self.reject('CLAIM_CHANGED')
                            os.unlink(claim, dir_fd=parent); os.fsync(parent)
                        # A different allocation may reuse an already released
                        # ID after a crash. Never touch its claim or quota.
                    self.d.recheck(parent, root, config); self.a.target.recheck(target, hdd)
                    result = self.receipt(value, prepared)
                    self.write(journal, 'receipt.json', result)
                    return result

    def receipt(self, value, prepared):
        plan = prepared['plan']
        return dict(schemaVersion=1, purpose='whaledeck-development-source-retirement-receipt-v1',
            retirementId=value['retirementId'], workspaceId=value['prerequisite']['storage']['workspaceId'],
            requestSha256=self.digest(value), retirementPlanSha256=value['retirementPlanSha256'],
            storageProofSha256=plan['storageProofSha256'], sourceManifestSha256=plan['sourceManifestSha256'],
            captureReceiptSha256=plan['captureReceiptSha256'], projectId=prepared['proof']['projectId'],
            sourceRemoved=True, quotaReleased=True, claimReleased=True, retainsHdd=True,
            releasesApplicationBudget=False)
