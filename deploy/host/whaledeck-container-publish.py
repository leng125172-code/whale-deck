#!/usr/bin/env python3
"""Private container publication engine; no CLI, RPC or authorization bypass.

Copies the proven host payload onto the target filesystem before renameat2.
Overwrite requires a verified HDD original and keeps the displaced inode.
Observation never repeats an uncertain rename or rolls back a changed target.
"""
import contextlib
import hashlib
import os
import stat
import time

PRIVATE = '.whaledeck-container-files-v1'


class ContainerPublisher:
    def __init__(self, uploads, backups):
        self.u, self.b, self.f, self.r = uploads, backups, uploads.f, uploads.r
        if backups.u is not uploads: self.u.reject('BACKUP_ENGINE_MISMATCH')

    def guard(self, request, session):
        self.u.scope_request(request)
        self.u.session_scope(request, session)
        session.revalidate()

    def alive(self, record, session, deadline):
        session.current()
        self.u.validate_target_proof(record, session)
        if time.monotonic() > deadline: self.u.reject('PUBLISH_TIMEOUT')

    @contextlib.contextmanager
    def parent(self, record, session):
        path = record['request']['path'].rpartition('/')[0]
        descriptor = self.f.open_at(session.root, path or '.', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True)
        try:
            if 'publication' in record and [os.fstat(descriptor).st_dev, os.fstat(descriptor).st_ino] != record['publication']['parentIdentity']:
                self.u.reject('PUBLISH_PARENT_CHANGED')
            yield descriptor
        finally: os.close(descriptor)

    def writable(self, record, session, parent):
        container = getattr(session, 'container', None)
        if type(container) is not dict or type(container.get('HostConfig')) is not dict:
            self.u.reject('SESSION_PROOF_INVALID')
        path = '/' + record['request']['path']
        matches = []
        for mount in container.get('Mounts') or []:
            destination = mount.get('Destination')
            if type(destination) is not str or not destination.startswith('/'):
                self.u.reject('SESSION_PROOF_INVALID')
            if path == destination: self.u.reject('PUBLISH_MOUNT_ROOT')
            if path.startswith(destination.rstrip('/') + '/'):
                matches.append(mount)
        allowed = max(matches, key=lambda row: len(row['Destination'])).get('RW') is True if matches else \
            container['HostConfig'].get('ReadonlyRootfs') is False
        if not allowed or os.fstatvfs(parent).f_flag & os.ST_RDONLY:
            self.u.reject('PUBLISH_READ_ONLY')

    def owner(self, session):
        pid = self.r.target_binding(session.request['containerId'], session.request['instanceId'])
        # Kernel-mapped filesystem IDs, including username-based Config.User;
        # never guess ownership from a user-controlled Docker username string.
        descriptor = os.open('/proc/' + str(pid) + '/status', os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try: raw = os.read(descriptor, 65537)
        finally: os.close(descriptor)
        if len(raw) > 65536: self.u.reject('PUBLISH_OWNER_INVALID')
        result = []
        for field in (b'Uid:', b'Gid:'):
            rows = [line.split() for line in raw.splitlines() if line.startswith(field)]
            if len(rows) != 1 or len(rows[0]) != 5 or not all(value.isdigit() for value in rows[0][1:]):
                self.u.reject('PUBLISH_OWNER_INVALID')
            value = int(rows[0][4])
            if not 0 <= value < 4294967295: self.u.reject('PUBLISH_OWNER_INVALID')
            result.append(value)
        session.current()
        return result

    def private(self, parent, record, create=False):
        if create:
            try: os.mkdir(PRIVATE, 0o700, dir_fd=parent); os.fsync(parent)
            except FileExistsError: pass
        descriptor = self.f.open_at(parent, PRIVATE, os.O_RDONLY | os.O_DIRECTORY)
        try:
            info = os.fstat(descriptor)
            if info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o700:
                self.u.reject('PUBLISH_PRIVATE_UNSAFE')
            expected = record['publication'].get('privateIdentity')
            if expected is not None and [info.st_dev, info.st_ino] != expected:
                self.u.reject('PUBLISH_PRIVATE_CHANGED')
            return descriptor
        except BaseException: os.close(descriptor); raise

    def probe(self, parent, name, record, session, deadline):
        try: descriptor = self.f.open_at(parent, name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        except FileNotFoundError: return None, None
        try:
            before = self.f.regular(descriptor)
            digest, count = hashlib.sha256(), 0
            while chunk := os.read(descriptor, self.r.CHUNK):
                self.alive(record, session, deadline)
                digest.update(chunk); count += len(chunk)
                if count > before.st_size: self.u.reject('PUBLISH_OBJECT_CHANGED')
            if count != before.st_size or self.r.fingerprint(before) != self.r.fingerprint(self.f.regular(descriptor)):
                self.u.reject('PUBLISH_OBJECT_CHANGED')
            return before, digest.hexdigest().upper()
        finally: os.close(descriptor)

    def recheck(self, parent, record, session, deadline):
        self.alive(record, session, deadline)
        with self.parent(record, session) as current:
            if self.r.fingerprint(os.fstat(current)) != self.r.fingerprint(os.fstat(parent)):
                self.u.reject('PUBLISH_PARENT_CHANGED')
            parent_path = record['request']['path'].rpartition('/')[0]
            if self.r.version(record['request'], parent_path, os.fstat(current)) != record['publication']['parentVersion']:
                self.u.reject('PARENT_VERSION_CONFLICT')

    def original(self, parent, record, session, deadline):
        request = record['request']; leaf = request['path'].rsplit('/', 1)[-1]
        info, digest = self.probe(parent, leaf, record, session, deadline)
        if (self.r.version(request, request['path'], info) if info else 'ABSENT') != request['expectedVersion']:
            self.u.reject('VERSION_CONFLICT')
        if info is not None and (self.f.publication_identity(info) != record['originalCapture']['originalIdentity'] or
                digest != record['originalCapture']['originalSha256']):
            self.u.reject('VERSION_CONFLICT')
        return info

    def copy(self, state, parent, private, record, session, deadline, original):
        request = record['request']; operation = record['operationId']; publication = record['publication']
        source_name = self.u.payload(state, record, complete=True)
        source = self.f.open_at(state, source_name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        output = None
        try:
            before = self.f.regular(source)
            if self.f.publication_identity(before) != record['payloadProof']: self.u.reject('PAYLOAD_CHANGED')
            usage = os.fstatvfs(parent)
            reserve = max(512 * 1024**2, usage.f_blocks * usage.f_frsize // 20)
            if usage.f_bavail * usage.f_frsize < request['sizeBytes'] + reserve: self.u.reject('CAPACITY_LOW')
            output = os.open(operation + '.upload', os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC,
                             0o600, dir_fd=private)
            info = os.fstat(output)
            publication['pendingIdentity'] = [info.st_dev, info.st_ino]
            self.f.save_receipt(state, operation, record)
            digest, count = hashlib.sha256(), 0
            while chunk := os.read(source, self.r.CHUNK):
                self.recheck(parent, record, session, deadline)
                count += len(chunk)
                if count > request['sizeBytes']: self.u.reject('PAYLOAD_CHANGED')
                usage = os.fstatvfs(parent)
                if usage.f_bavail * usage.f_frsize < len(chunk) + reserve: self.u.reject('CAPACITY_LOW')
                digest.update(chunk); view = memoryview(chunk)
                while view:
                    written = os.write(output, view)
                    if written <= 0: self.u.reject('WRITE_FAILED')
                    view = view[written:]
            if count != request['sizeBytes'] or digest.hexdigest().upper() != request['sha256'] or \
                    self.r.fingerprint(before) != self.r.fingerprint(self.f.regular(source)):
                self.u.reject('PAYLOAD_CHANGED')
            if 'restoreSource' in request:
                if self.u.restorations is None: self.u.reject('RESTORE_ENGINE_REQUIRED')
                # Full private content is durable before ownership can change.
                # A crash between chown/chmod/utime and the final proof must
                # remain explicitly discardable using this inode/content proof.
                os.fsync(output); os.fsync(private)
                publication['attributePreparationProof'] = self.f.publication_identity(os.fstat(output))
                self.f.save_receipt(state, operation, record)
                self.u.restorations.apply(output, record['restorationAttributes'], session)
            else:
                uid, gid = (original.st_uid, original.st_gid) if original else publication['owner']
                os.fchown(output, uid, gid)
                os.fchmod(output, stat.S_IMODE(original.st_mode) & 0o777 if original else 0o600)
            os.fsync(output); os.fsync(private)
            publication['pendingProof'] = self.f.publication_identity(os.fstat(output))
            self.f.save_receipt(state, operation, record)
        finally:
            if output is not None: os.close(output)
            os.close(source)

    def public(self, record, binding=False, replayed=False):
        return {**self.u.public(record, replayed), 'bindingCurrent': binding,
            'previousRetained': record.get('previousRetained', False), 'version': record.get('publishedVersion', ''),
            'backupCapture': record.get('backupCapture')}

    def reconcile(self, state, parent, private, record, session, deadline):
        publication = record['publication']; request = record['request']; operation = record['operationId']
        if record['state'] in ('Unknown', 'PublishDiscarded'): return self.public(record, replayed=True)
        if record['state'] == 'PublishDiscarding':
            pending, _ = self.probe(private, operation + '.upload', record, session, deadline) if private is not None else (None, None)
            names = (operation + '.partial', operation + '.data')
            remaining = any(self.exists(state, name) for name in names)
            if pending is None and not remaining:
                record['state'] = 'PublishDiscarded'; self.f.save_receipt(state, operation, record)
            return self.public(record)
        if record['state'] == 'Published':
            actual, digest = self.probe(parent, request['path'].rsplit('/', 1)[-1], record, session, deadline)
            binding = actual is not None and self.r.version(request, request['path'], actual) == record['publishedVersion'] and digest == request['sha256']
            return self.public(record, binding, True)
        if record['state'] == 'PublishApplying' and request['expectedVersion'] != 'ABSENT':
            self.b.verify_capture(record, session)
        try:
            pending, pending_hash = self.probe(private, operation + '.upload', record, session, deadline)
            actual, actual_hash = self.probe(parent, request['path'].rsplit('/', 1)[-1], record, session, deadline)
            if record['state'] == 'PublishApplying':
                new = publication.get('pendingProof')
                placed = actual is not None and self.f.publication_identity(actual) == new and actual_hash == request['sha256']
                retained = pending is None if request['expectedVersion'] == 'ABSENT' else pending is not None and \
                    self.f.publication_identity(pending) == record['originalCapture']['originalIdentity'] and pending_hash == record['originalCapture']['originalSha256']
                if placed and retained:
                    session.revalidate(); self.u.validate_target_proof(record, session)
                    os.fsync(parent); os.fsync(private)
                    record.update(state='Published', publishedVersion=self.r.version(request, request['path'], actual),
                        previousRetained=request['expectedVersion'] != 'ABSENT')
                else:
                    pending_new = pending is not None and self.f.publication_identity(pending) == new and pending_hash == request['sha256']
                    old_current = (self.r.version(request, request['path'], actual) if actual else 'ABSENT') == request['expectedVersion']
                    record['state'] = 'PublishInterrupted' if pending_new and old_current else 'Unknown'
            else:
                owned = pending is None and publication.get('pendingIdentity') is None or pending is not None and \
                    [pending.st_dev, pending.st_ino] == publication.get('pendingIdentity')
                record['state'] = 'PublishInterrupted' if owned else 'Unknown'
        except (OSError, self.f.Rejected):
            record['state'] = 'Unknown'
        self.f.save_receipt(state, operation, record)
        return self.public(record, record['state'] == 'Published')

    def publish(self, request, session):
        self.guard(request, session)
        # Capture has its own same-operation lock. Never hold it recursively;
        # re-read and prove everything again under both locks before commit.
        with self.u.locked(request['operationId']) as state:
            record = self.u.scoped(state, request)
            self.u.validate_target_proof(record, session)
            if record['state'] == 'Prevented': self.u.reject('CANCELED')
            fresh = record['state'] == 'Staged'
            if fresh:
                if 'restoreSource' in record['request']:
                    if self.u.restorations is None: self.u.reject('RESTORE_ENGINE_REQUIRED')
                    self.u.restorations.permitted(record['restorationAttributes'], session)
                with self.parent(record, session) as parent: self.writable(record, session, parent)
        if fresh and record['request']['expectedVersion'] != 'ABSENT': self.b.capture(request, session)
        deadline = time.monotonic() + 300
        with self.u.locked(request['operationId']) as state, self.b.gate(state):
            record = self.u.scoped(state, request)
            session.revalidate()
            self.u.validate_target_proof(record, session)
            with self.parent(record, session) as parent:
                if 'publication' in record:
                    return self.observe_locked(state, parent, record, session, deadline)
                if record['state'] != 'Staged': self.u.reject('STAGED_REQUIRED')
                self.writable(record, session, parent)
                self.u.target(record['request'], session)
                if record['request']['expectedVersion'] != 'ABSENT': self.b.verify_capture(record, session)
                original = self.original(parent, record, session, deadline)
                session.revalidate(); self.u.validate_target_proof(record, session)
                record['publication'] = {'parentIdentity': [os.fstat(parent).st_dev, os.fstat(parent).st_ino], 'owner': self.owner(session)}
                record['state'] = 'PublishPreparing'
                self.f.save_receipt(state, record['operationId'], record)
                private = self.private(parent, record, create=True)
                try:
                    value = os.fstat(private)
                    record['publication'].update(privateIdentity=[value.st_dev, value.st_ino],
                        parentVersion=self.r.version(record['request'], record['request']['path'].rpartition('/')[0], os.fstat(parent)))
                    self.f.save_receipt(state, record['operationId'], record)
                    self.recheck(parent, record, session, deadline)
                    self.original(parent, record, session, deadline)
                    self.copy(state, parent, private, record, session, deadline, original)
                    prepared, digest = self.probe(private, record['operationId'] + '.upload', record, session, deadline)
                    if prepared is None or self.f.publication_identity(prepared) != record['publication']['pendingProof'] or digest != record['request']['sha256']:
                        self.u.reject('PUBLISH_OBJECT_CHANGED')
                    session.revalidate(); self.recheck(parent, record, session, deadline)
                    self.original(parent, record, session, deadline)
                    if original is None and self.owner(session) != record['publication']['owner']: self.u.reject('PUBLISH_OWNER_CHANGED')
                    if original is not None: self.b.verify_capture(record, session)
                    session.revalidate(); self.recheck(parent, record, session, deadline)
                    self.original(parent, record, session, deadline)
                    if 'restoreSource' in record['request']:
                        if self.u.restorations is None: self.u.reject('RESTORE_ENGINE_REQUIRED')
                        self.u.restorations.permitted(record['restorationAttributes'], session)
                    record['state'] = 'PublishApplying'
                    self.f.save_receipt(state, record['operationId'], record)
                    self.f.exchange(private, record['operationId'] + '.upload', parent,
                        record['request']['path'].rsplit('/', 1)[-1], 1 if original is None else 2)
                    os.fsync(parent); os.fsync(private)
                    return self.reconcile(state, parent, private, record, session, deadline)
                finally: os.close(private)

    def observe_locked(self, state, parent, record, session, deadline):
        if record['state'] in ('Published', 'PublishDiscarded', 'Unknown'):
            return self.reconcile(state, parent, None, record, session, deadline)
        try: private = self.private(parent, record)
        except FileNotFoundError:
            if record['state'] == 'PublishDiscarding' and record['publication'].get('privateIdentity') is None:
                return self.reconcile(state, parent, None, record, session, deadline)
            record['state'] = 'PublishInterrupted' if record['state'] in ('PublishPreparing', 'PublishInterrupted') and \
                record['publication'].get('privateIdentity') is None and record['publication'].get('pendingIdentity') is None else 'Unknown'
            self.f.save_receipt(state, record['operationId'], record)
            return self.public(record)
        try: return self.reconcile(state, parent, private, record, session, deadline)
        finally: os.close(private)

    def inspect(self, request, session):
        self.guard(request, session)
        with self.u.locked(request['operationId']) as state, self.b.gate(state):
            record = self.u.scoped(state, request)
            self.u.validate_target_proof(record, session)
            if 'publication' in record:
                with self.parent(record, session) as parent:
                    return self.observe_locked(state, parent, record, session, time.monotonic() + 300)
        # Receiving/Verified recovery belongs to the staging engine. Re-read
        # under its lock; a concurrent transition fails closed, never guesses.
        return self.u.inspect(request, session)

    @staticmethod
    def exists(parent, name):
        try: os.stat(name, dir_fd=parent, follow_symlinks=False); return True
        except FileNotFoundError: return False

    def attribute_preparation(self, record, pending, digest):
        proof = record['publication'].get('attributePreparationProof')
        return record['schemaVersion'] == 4 and 'restoreSource' in record['request'] and \
            type(proof) is list and len(proof) == 8 and all(type(value) is int for value in proof) and \
            stat.S_ISREG(proof[2]) and proof[3] == 0 and stat.S_IMODE(proof[2]) == 0o600 and proof[5] == 1 and \
            [pending.st_dev, pending.st_ino] == proof[:2] and pending.st_nlink == 1 and \
            pending.st_size == proof[6] == record['request']['sizeBytes'] and digest == record['request']['sha256']

    def discard(self, request, session):
        self.guard(request, session)
        with self.u.locked(request['operationId']) as state:
            record = self.u.scoped(state, request)
            delegated = 'publication' not in record
        if delegated: return self.u.discard(request, session)
        deadline = time.monotonic() + 300
        with self.u.locked(request['operationId']) as state, self.b.gate(state):
            record = self.u.scoped(state, request)
            self.u.validate_target_proof(record, session)
            with self.parent(record, session) as parent:
                self.observe_locked(state, parent, record, session, deadline)
                if record['state'] == 'Published': self.u.reject('ALREADY_PUBLISHED')
                if record['state'] == 'Unknown': self.u.reject('PREVIOUS_OPERATION_INCOMPLETE')
                if record['state'] == 'PublishDiscarded': return self.public(record, replayed=True)
                private = None
                try:
                    try: private = self.private(parent, record)
                    except FileNotFoundError:
                        if record['publication'].get('privateIdentity') is not None: raise
                    operation = record['operationId']; pending = None
                    if private is not None:
                        pending, digest = self.probe(private, operation + '.upload', record, session, deadline)
                        if pending is not None:
                            publication = record['publication']
                            valid = [pending.st_dev, pending.st_ino] == publication.get('pendingIdentity')
                            if publication.get('pendingProof'):
                                valid = valid and self.f.publication_identity(pending) == publication['pendingProof'] and digest == record['request']['sha256']
                            elif publication.get('attributePreparationProof') is not None:
                                valid = valid and self.attribute_preparation(record, pending, digest)
                            else:
                                valid = valid and pending.st_uid == 0 and stat.S_IMODE(pending.st_mode) == 0o600 and pending.st_size <= record['request']['sizeBytes']
                            if not valid: self.u.reject('PUBLISH_OBJECT_CHANGED')
                    name = None
                    remaining = any(self.exists(state, operation + suffix) for suffix in ('.partial', '.data'))
                    if remaining: name = self.u.payload(state, record, complete=True)
                    elif record['state'] != 'PublishDiscarding': self.u.reject('PAYLOAD_CHANGED')
                    # Durable cancellation intent precedes both unlinks. A
                    # repeated explicit discard may finish missing owned data;
                    # observation itself never deletes anything.
                    record['state'] = 'PublishDiscarding'
                    self.f.save_receipt(state, operation, record)
                    session.revalidate(); self.u.validate_target_proof(record, session)
                    if pending is not None: os.unlink(operation + '.upload', dir_fd=private); os.fsync(private)
                    if name is not None: os.unlink(name, dir_fd=state); os.fsync(state)
                    record['state'] = 'PublishDiscarded'
                    self.f.save_receipt(state, operation, record)
                    return self.public(record)
                finally:
                    if private is not None: os.close(private)
