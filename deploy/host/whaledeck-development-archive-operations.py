"""Durable long archive tasks outside the workspace and HDD payload.

Internal engine only. Every Plan/Capture/Verify has one immutable task ID and
request. An RPC/Worker disconnect never restarts it. Observe and precise stop
do not acquire either archive storage lock. Failed/partial data is retained.
"""
import contextlib
import errno
import fcntl
import hashlib
import os
from pathlib import Path
import re
import stat
import time

BASE = Path('/var/lib/whaledeck-development-archive-operations')
MAX_TASKS = 30000
RESULT_LOCK_WAIT_SECONDS = 2
UUID = r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}'
ZERO = '00000000-0000-0000-0000-000000000000'
SHA = r'[a-f0-9]{64}'
DIR = os.O_RDONLY | os.O_DIRECTORY


class ArchiveOperations:
    def __init__(self, driver, engine, execution):
        self.d, self.engine, self.execution = driver, engine, execution

    def refuse(self, code):
        self.d.reject('ARCHIVE_TASK_' + code)

    def base(self):
        return BASE

    def persist(self, directory, name, value):
        self.engine.durable_json(directory, name, value)

    def io_failure(self, code):
        return 'WORKSPACE_ARCHIVE_' + code

    def perform(self, value):
        command = dict(storage=value['storage'], expectedEpoch=value['expectedEpoch'],
            operationId=value['archiveId'], planSha256=value['planSha256'])
        if value['phase'] == 'plan':
            return self.engine.plan(value['storage'], value['expectedEpoch'])
        if value['phase'] == 'capture':
            return self.engine.capture(command)
        return self.engine.verify(command, value['receiptSha256'])

    def validate(self, value):
        if (not isinstance(value, dict)
                or set(value) != {'schemaVersion', 'taskId', 'archiveId', 'phase', 'storage', 'expectedEpoch', 'planSha256', 'receiptSha256'}
                or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1
                or value['phase'] not in ('plan', 'capture', 'verify')
                or type(value['expectedEpoch']) is not int or not 2 <= value['expectedEpoch'] < 0x7ffffffffffffffe
                or value['expectedEpoch'] % 2 or len(self.d.canonical(value)) > 4096):
            self.refuse('REQUEST_INVALID')
        for key in ('taskId', 'archiveId'):
            if not isinstance(value[key], str) or not re.fullmatch(UUID, value[key]) or value[key] == ZERO:
                self.refuse('REQUEST_INVALID')
        for key, required in (('planSha256', value['phase'] != 'plan'), ('receiptSha256', value['phase'] == 'verify')):
            if not isinstance(value[key], str) or (not re.fullmatch(SHA, value[key]) if required else value[key] != ''):
                self.refuse('REQUEST_INVALID')
        self.d.validate_request(value['storage'])

    def journal_lock(self, directory, *, wait=False):
        deadline = time.monotonic() + RESULT_LOCK_WAIT_SECONDS if wait else None
        while True:
            try:
                fcntl.flock(directory, fcntl.LOCK_EX | fcntl.LOCK_NB)
                return
            except BlockingIOError:
                if deadline is None or time.monotonic() >= deadline:
                    self.refuse('BUSY')
                time.sleep(0.01)

    @contextlib.contextmanager
    def entry(self, value, *, create=False, pinned=None, wait=False):
        self.validate(value)
        d = self.d
        with d.absolute(self.base()) as root:
            d.directory(root, private=True)
            if pinned is not None and not os.path.samestat(os.fstat(root), os.fstat(pinned[0])):
                self.refuse('JOURNAL_CHANGED')
            if create:
                # Only the dispatched worker may wait here; user controls
                # retain nonblocking admission and never hold storage locks.
                self.journal_lock(root, wait=wait)
                try:
                    count, found = 0, False
                    with os.scandir(root) as rows:
                        for row in rows:
                            count += 1
                            found |= row.name == value['taskId']
                            if count > MAX_TASKS:
                                self.refuse('HISTORY_LIMIT')
                    if count >= MAX_TASKS and not found:
                        self.refuse('HISTORY_LIMIT')
                    try:
                        os.mkdir(value['taskId'], 0o700, dir_fd=root)
                        os.fsync(root)
                    except FileExistsError:
                        pass
                finally:
                    fcntl.flock(root, fcntl.LOCK_UN)
            with d.opened(root, value['taskId'], DIR) as directory:
                original = d.directory(directory, private=True)
                if pinned is not None and not os.path.samestat(original, os.fstat(pinned[1])):
                    self.refuse('JOURNAL_CHANGED')
                # Dispatched worker admission and its pinned result commit
                # wait briefly for observers. Observe/Cancel remain immediate.
                self.journal_lock(directory, wait=wait or pinned is not None)
                if create:
                    self.persist(directory, 'request.json', value)
                    self.cancelled(directory, initialize=True)
                else:
                    d.read_json(directory, 'request.json', 4096, expected=value)
                yield directory
                with d.absolute(self.base()) as current:
                    d.directory(current, private=True)
                    if not os.path.samestat(os.fstat(root), os.fstat(current)):
                        self.refuse('JOURNAL_CHANGED')
                    with d.opened(current, value['taskId'], DIR) as live:
                        if not os.path.samestat(original, os.fstat(live)):
                            self.refuse('JOURNAL_CHANGED')

    def cancelled(self, directory, *, initialize=False, mark=False):
        if initialize:
            try:
                fd = os.open('cancel', os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_CLOEXEC | os.O_NOFOLLOW, 0o600, dir_fd=directory)
            except FileExistsError:
                pass
            else:
                try:
                    if os.write(fd, b'0') != 1:
                        self.refuse('JOURNAL_UNSAFE')
                    os.fsync(fd)
                finally:
                    os.close(fd)
                os.fsync(directory)
        # Fixed private metadata needs pwrite; the general payload open helper
        # intentionally rejects O_RDWR and must retain that restriction.
        fd = os.open('cancel', (os.O_RDWR if mark else os.O_RDONLY) | os.O_NONBLOCK
            | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=directory)
        try:
            info = os.fstat(fd)
            if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
                    or stat.S_IMODE(info.st_mode) != 0o600 or info.st_size not in (0, 1)):
                self.refuse('JOURNAL_UNSAFE')
            if info.st_size == 0:
                # Only an interrupted first initialization can be repaired. No
                # execution or result may exist, even as a pending write.
                if not initialize:
                    self.refuse('JOURNAL_UNSAFE')
                for name in ('dispatch.json', '.dispatch.json.pending', 'execution.json', '.execution.json.pending',
                             'result.json', '.result.json.pending'):
                    try:
                        os.stat(name, dir_fd=directory, follow_symlinks=False)
                    except FileNotFoundError:
                        continue
                    self.refuse('JOURNAL_UNSAFE')
                # Reopen only this fixed validated private file for the repair.
                repair = os.open('cancel', os.O_WRONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=directory)
                try:
                    if not os.path.samestat(info, os.fstat(repair)) or os.pwrite(repair, b'0', 0) != 1:
                        self.refuse('JOURNAL_UNSAFE')
                    os.fsync(repair)
                finally:
                    os.close(repair)
            value = os.pread(fd, 2, 0)
            if value not in (b'0', b'1'):
                self.refuse('JOURNAL_UNSAFE')
            if mark:
                if value == b'0' and os.pwrite(fd, b'1', 0) != 1:
                    self.refuse('JOURNAL_UNSAFE')
                os.fsync(fd)
                return True
            return value == b'1'
        finally:
            os.close(fd)

    def proof(self, directory):
        try:
            proof = self.d.read_json(directory, 'execution.json', 1024)
        except FileNotFoundError:
            return None
        self.execution.validate(proof)
        return proof

    def receipt(self, value, status, *, code=None, result=None):
        return dict(schemaVersion=1, taskId=value['taskId'],
            requestSha256=hashlib.sha256(self.d.canonical(value)).hexdigest(), status=status, code=code, result=result)

    def completed(self, directory, value):
        try:
            result = self.d.read_json(directory, 'result.json', 8192)
        except FileNotFoundError:
            return None
        if (set(result) != {'schemaVersion', 'taskId', 'requestSha256', 'status', 'code', 'result'}
                or self.d.canonical(result) != self.d.canonical(self.receipt(value, result.get('status'),
                    code=result.get('code'), result=result.get('result')))
                or result['status'] not in ('Succeeded', 'Rejected')
                or (result['status'] == 'Succeeded' and (result['code'] is not None or not isinstance(result['result'], dict)))
                or (result['status'] == 'Rejected' and (result['result'] is not None or not isinstance(result['code'], str)
                    or not re.fullmatch('WORKSPACE_(?:STORAGE|ARCHIVE)_[A-Z_]{1,80}', result['code'])))):
            self.refuse('JOURNAL_UNSAFE')
        return result

    def state(self, directory, value, *, stop=False):
        result = self.completed(directory, value)
        proof = self.proof(directory)
        if result is not None:
            if proof is None:
                self.refuse('EXECUTION_INVALID')
            # Preserve an already committed success under a late cancellation.
            return result if self.execution.settled(proof) else self.receipt(value, 'Finishing')
        cancelled = self.cancelled(directory, mark=stop)
        if proof is None:
            return self.receipt(value, 'Stopped' if cancelled else 'Pending')
        if not self.execution.settled(proof, stop=stop):
            return self.receipt(value, 'Running')
        return self.receipt(value, 'Stopped' if cancelled else 'Interrupted')

    def observe(self, value):
        with self.entry(value) as directory:
            return self.state(directory, value)

    def admit_dispatch(self, value):
        # Commit before calling the manager. A lost launch receipt is unknown,
        # never permission to submit another execution under this task ID.
        with self.entry(value, create=True) as directory:
            if self.completed(directory, value) is not None or self.proof(directory) is not None or self.cancelled(directory):
                return False
            fence = dict(schemaVersion=1, requestSha256=self.receipt(value, 'Pending')['requestSha256'])
            try:
                self.d.read_json(directory, 'dispatch.json', 1024, expected=fence, sync=True)
                return False
            except FileNotFoundError:
                self.persist(directory, 'dispatch.json', fence)
                return True

    def require_dispatch(self, directory, value):
        self.d.read_json(directory, 'dispatch.json', 1024, expected=dict(schemaVersion=1,
            requestSha256=self.receipt(value, 'Pending')['requestSha256']))

    def cancel(self, value):
        # A durable pre-start tombstone rejects a signed request arriving late.
        # Stopped means this execution is gone, never "no HDD bytes exist".
        with self.entry(value, create=True) as directory:
            return self.state(directory, value, stop=True)

    def run(self, value):
        with contextlib.ExitStack() as pins:
            return self.run_pinned(value, pins)

    def run_pinned(self, value, pins):
        with self.entry(value, create=True, wait=True) as directory:
            previous = self.completed(directory, value)
            proof = self.proof(directory)
            if previous is not None or proof is not None or self.cancelled(directory):
                return self.state(directory, value)
            proof = self.execution.identity()
            self.execution.validate(proof)
            self.persist(directory, 'execution.json', proof)
            # New open descriptions pin the original inodes without retaining
            # the task flock while storage work runs. A copied replacement
            # journal cannot receive a result, even with identical JSON bytes.
            root_pin = pins.enter_context(self.d.absolute(self.base()))
            task_pin = pins.enter_context(self.d.opened(directory, '.', DIR))
        # Deliberately release the task lock before any storage/global lock.
        # A new attempt may only be admitted by a future explicit authorized
        # retry after exact stop proof. This task ID never re-executes itself.
        try:
            result = self.perform(value)
            completed = self.receipt(value, 'Succeeded', result=result)
        except self.d.files.Rejected as error:
            code = str(error)
            if not re.fullmatch('WORKSPACE_STORAGE_[A-Z_]{1,80}', code):
                code = self.io_failure('UNCONFIRMED')
            completed = self.receipt(value, 'Rejected', code=code)
        except OSError as error:
            code = {errno.ENOSPC: 'NO_SPACE', errno.EDQUOT: 'QUOTA_EXCEEDED', errno.EACCES: 'ACCESS_DENIED',
                errno.EPERM: 'ACCESS_DENIED'}.get(error.errno, 'IO_UNCONFIRMED')
            completed = self.receipt(value, 'Rejected', code=self.io_failure(code))
        with self.entry(value, pinned=(root_pin, task_pin)) as directory:
            if self.proof(directory) != proof:
                self.refuse('EXECUTION_CHANGED')
            self.persist(directory, 'result.json', completed)
            # The caller is itself still a populated unit. Independent Observe
            # waits for its disappearance before returning a terminal result.
            return self.receipt(value, 'Finishing')
