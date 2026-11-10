#!/usr/bin/env python3
"""Plan and fence an idle workspace. Never remove code, allocation or quota."""
import hashlib
import json
import os
from pathlib import Path
import re
import sys

GIT_CGROUP = Path('/sys/fs/cgroup/system.slice/whaledeck-development-git-acquire.service')


def digest(driver, value):
    return hashlib.sha256(driver.canonical(value)).hexdigest()


class Lifecycle:
    def __init__(self, driver):
        self.driver = driver

    def refuse(self, code):
        self.driver.reject('LIFECYCLE_' + code)

    def request(self, value, apply):
        fields = {'storage', 'action', 'expectedEpoch', 'operationId', 'planSha256', 'storageProofSha256'}
        if (not isinstance(value, dict) or set(value) != fields or value['action'] not in ('stop', 'resume')
                or type(value['expectedEpoch']) is not int or not 1 <= value['expectedEpoch'] < 0x7ffffffffffffffd):
            self.refuse('REQUEST_INVALID')
        root, workspace = self.driver.validate_request(value['storage'])
        operation = value['operationId']
        valid_operation = isinstance(operation, str) and (
            bool(re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}', operation))
            and operation != '00000000-0000-0000-0000-000000000000' if apply else operation == '')
        if not valid_operation:
            self.refuse('REQUEST_INVALID')
        for key in ('planSha256', 'storageProofSha256'):
            if not isinstance(value[key], str) or (not re.fullmatch('[a-f0-9]{64}', value[key]) if apply else value[key] != ''):
                self.refuse('REQUEST_INVALID')
        return root, workspace

    def run(self, value, *, apply=False):
        driver = self.driver
        root, workspace = self.request(value, apply)
        # Exclusive root lock waits out every attested Runner/source operation.
        # A busy root is a retry, never evidence that execution has stopped.
        with driver.locked_root(root, exclusive=apply) as (parent, config):
            if apply:
                self.require_git_idle()
            original = driver.read_existing(parent, workspace, value['storage'], config)
            if original is None:
                self.refuse('NOT_ALLOCATED')
            with driver.quota_payload(parent, workspace, original, config) as (_, proof):
                current = driver.lifecycle_state(parent, workspace, value['storage'], proof=proof)
                epoch, state = (current['epoch'], current['state']) if current else (1, 'Active')
                expected_state, target = ('Active', 'Stopped') if value['action'] == 'stop' else ('Stopped', 'Active')
                proof_hash = digest(driver, proof)
                plan_hash = digest(driver, dict(schemaVersion=1, storageProof=proof,
                    action=value['action'], expectedEpoch=value['expectedEpoch'], state=expected_state))
                if apply and current is not None and epoch == value['expectedEpoch'] + 1:
                    if (state != target or current['operationId'] != value['operationId']
                            or current['planSha256'] != value['planSha256']
                            or value['planSha256'] != plan_hash or value['storageProofSha256'] != proof_hash):
                        self.refuse('PLAN_CHANGED')
                    os.fsync(parent)
                else:
                    if epoch != value['expectedEpoch'] or state != expected_state:
                        self.refuse('PLAN_CHANGED')
                    if apply:
                        if value['planSha256'] != plan_hash or value['storageProofSha256'] != proof_hash:
                            self.refuse('PLAN_CHANGED')
                        marker = dict(value['storage'], state=target, epoch=epoch + 1,
                            operationId=value['operationId'], planSha256=plan_hash, storageProofSha256=proof_hash)
                        self.publish(parent, workspace, marker)
                        epoch, state = epoch + 1, target
                driver.recheck(parent, root, config)
                if driver.read_existing(parent, workspace, value['storage'], config) != original:
                    self.refuse('BINDING_CHANGED')
                return dict(schemaVersion=1, requestSha256=digest(driver, value), workspaceId=workspace,
                    action=value['action'], epoch=epoch, state=state,
                    planSha256=plan_hash, storageProofSha256=proof_hash)

    def require_git_idle(self):
        # Git acquisition cannot access the workspace mount, but its legacy
        # cancellation reply is not a process-drain receipt. Check its actual
        # fixed cgroup as well as the application's conservative teardown window.
        try:
            directory = os.open(GIT_CGROUP, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        except FileNotFoundError:
            return
        try:
            fd = os.open('cgroup.events', os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=directory)
            with os.fdopen(fd, 'rb') as stream:
                raw = stream.read(1025)
            populated = [line for line in raw.splitlines() if line.startswith(b'populated ')]
            if len(raw) > 1024 or populated != [b'populated 0']:
                self.driver.reject('BUSY')
        finally:
            os.close(directory)

    def publish(self, parent, workspace, value):
        driver = self.driver
        name = '.lifecycle-' + workspace + '.json'
        temporary = '.lifecycle-staging-' + value['operationId'] + '.json'
        raw = driver.canonical(value)
        try:
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600, dir_fd=parent)
        except FileExistsError:
            if driver.read_json(parent, temporary, 4096) != value:
                self.refuse('STAGING_CONFLICT')
        else:
            with os.fdopen(fd, 'wb') as stream:
                stream.write(raw)
                stream.flush()
                os.fsync(stream.fileno())
        os.replace(temporary, name, src_dir_fd=parent, dst_dir_fd=parent)
        os.fsync(parent)


def main():
    try:
        if os.geteuid() != 0 or sys.argv[1:] not in (['plan'], ['apply']):
            raise PermissionError()
        # Reuse the installed loader's root ownership, no-symlink and size checks.
        import importlib.util
        spec = importlib.util.spec_from_file_location('storage_transport', Path(__file__).with_name('whaledeck-development-storage-transport.py'))
        loader = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(loader)
        files, storage, quota, guard = (loader.load(name) for name in ('whaledeck-files.py',
            'whaledeck-development-storage.py', 'whaledeck-development-project-quota.py', 'whaledeck-development-quota-guard.py'))
        guard.install(quota, read_only=True)
        raw = sys.stdin.buffer.read(8193)
        if len(raw) > 8192:
            raise ValueError()
        value = json.loads(raw, object_pairs_hook=files.unique_object)
        result = Lifecycle(storage.WorkspaceStorage(files, sandboxed=True, quota=quota)).run(value, apply=sys.argv[1] == 'apply')
        print(json.dumps(result, separators=(',', ':')))
        return 0
    except Exception as error:
        code = str(error)
        if not re.fullmatch('WORKSPACE_STORAGE_[A-Z_]{1,48}', code):
            code = 'WORKSPACE_STORAGE_LIFECYCLE_UNAVAILABLE'
        print(code, file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
