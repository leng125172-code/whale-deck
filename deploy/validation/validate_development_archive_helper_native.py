"""Actual fixed helper/sandbox proof on the parent probe's isolated images.

Copies rewrite fixed fixture paths and use stricter runtime/CPU limits. Production
configuration and binaries remain untouched. No storage/execution doubles.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import select
import shutil
import subprocess
import sys
import time
import uuid


def load(path):
    spec = importlib.util.spec_from_file_location(path.stem, path)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    return value


def save(path, value):
    path.write_text(json.dumps(value, sort_keys=True, indent=2) + chr(10)); path.chmod(0o600)


def isolate_text(text, replacements):
    paths = re.compile('(?:' + '|'.join(re.escape(p) for p in sorted(replacements, key=len, reverse=True))
                       + r')(?![A-Za-z0-9_-])')
    return paths.sub(lambda match: replacements[match[0]], text)


def rpc_publication():
    option = os.environ.get('WHALEDECK_ARCHIVE_RPC_PROBE')
    if option is None: return None
    if option != '1': raise ValueError('NATIVE_ARCHIVE_RPC_OPTION_INVALID')
    # This is a manually authorized root QA script, not a privileged entrypoint.
    # Execute only this repository's reviewed fixed project publication. The
    # mount namespace does not sandbox arbitrary root assemblies.
    return Path(__file__).resolve().parent / 'DevelopmentArchiveRpcProbe/bin/Release/net10.0/publish'


class RpcBridge:
    """QA-only private mount binding; calls the unchanged production C# facade."""
    def __init__(self, published, library, evidence, kind="archive"):
        assert kind in ("archive", "retirement")
        project = "DevelopmentArchiveRpcProbe" if kind == "archive" else "DevelopmentRetirementRpcProbe"
        prefix = "" if kind == "archive" else "retirement-"
        self.fixed_helper = Path('/usr/local/libexec/whaledeck-privileged')
        self.fixed_original = self.fixed_helper.lstat()
        self.fixed_sha256 = hashlib.sha256(self.fixed_helper.read_bytes()).hexdigest()
        self.home = Path('/run/wd-arpc-' + uuid.uuid4().hex)
        self.home.mkdir(mode=0o700); self.original = self.home.stat()
        self.process = None; self.errors = None
        try:
            assert published.resolve(strict=True) == published and published.is_dir()
            originals = {}
            for path in published.rglob('*'):
                assert not path.is_symlink()
                if path.is_file():
                    assert path.lstat().st_nlink == 1
                    originals[str(path.relative_to(published))] = hashlib.sha256(path.read_bytes()).hexdigest()
            runtime = self.home / 'runtime'; shutil.copytree(published, runtime, symlinks=True)
            runtime.chmod(0o700)
            for path in runtime.rglob('*'):
                assert not path.is_symlink()
                path.chmod(0o700 if path.is_dir() else 0o600)
            assert originals == {str(p.relative_to(runtime)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in runtime.rglob('*') if p.is_file()}
            assert originals == {str(p.relative_to(published)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in published.rglob('*') if p.is_file()}
            assert (runtime / (project + '.dll')).is_file()
            self.errors = (evidence / (prefix + 'rpc-bridge.stderr')).open('x')
            save(evidence / (prefix + 'rpc-binary-sha256.json'), {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                for p in runtime.iterdir() if p.is_file()})
            self.process = subprocess.Popen(['unshare', '--mount', '--propagation', 'private',
                sys.executable, '-B', '-I', __file__, '--rpc-namespace', str(library / 'whaledeck-privileged'),
                str(runtime / (project + '.dll')), str(self.home),
                str(Path('/proc/self/ns/mnt').readlink()), str(Path(shutil.which('dotnet')).resolve())],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.errors, text=True, bufsize=1)
            assert self.read() == dict(ready=True), 'NATIVE_ARCHIVE_RPC_NOT_READY'
        except BaseException:
            self.close(failed=True); raise

    def read(self):
        assert select.select([self.process.stdout], [], [], 30)[0], 'NATIVE_ARCHIVE_RPC_READ_TIMEOUT'
        line = self.process.stdout.readline(32769)
        assert 0 < len(line) <= 32768, 'NATIVE_ARCHIVE_RPC_OUTPUT_INVALID'
        return json.loads(line)

    def call(self, action, value):
        self.process.stdin.write(json.dumps(dict(action=action, request=value), separators=(',', ':')) + chr(10))
        self.process.stdin.flush()
        return self.read()

    def close(self, failed=False):
        code = None
        try:
            if self.process is not None:
                try: self.process.stdin.close()
                except OSError: pass
                finally:
                    try: code = self.process.wait(timeout=8)
                    except subprocess.TimeoutExpired:
                        self.process.kill(); code = self.process.wait(timeout=5)
                    self.process.stdout.close()
            if self.errors is not None: self.errors.close()
        finally:
            # Keep the private tree and possible ephemeral key if termination
            # is uncertain. Never remove paths still owned by a live bridge.
            assert self.process is None or self.process.poll() is not None, 'NATIVE_ARCHIVE_RPC_PROCESS_STILL_ALIVE'
            assert os.path.samestat(self.original, self.home.lstat()) and not self.home.is_mount()
            assert not any(p.is_mount() for p in self.home.rglob('*') if p.is_dir())
            shutil.rmtree(self.home)
            assert os.path.samestat(self.fixed_original, self.fixed_helper.lstat())
            assert self.fixed_sha256 == hashlib.sha256(self.fixed_helper.read_bytes()).hexdigest()
        if not failed: assert code == 0, 'NATIVE_ARCHIVE_RPC_EXIT_UNCONFIRMED'


def rpc_namespace():
    assert os.geteuid() == 0 and len(sys.argv) == 7 and sys.argv[1] == '--rpc-namespace'
    helper, binary, home, parent_namespace, dotnet = sys.argv[2:]
    assert str(Path('/proc/self/ns/mnt').readlink()) != parent_namespace
    for path in (Path(helper), Path(binary)):
        info = path.lstat()
        assert path.is_absolute() and path.is_file() and not path.is_symlink() and info.st_uid == 0 and not info.st_mode & 0o022
    # Bind only this entrypoint in this process's mount namespace. PID 1's
    # transient units use the already path-rewritten private helper modules.
    subprocess.run(['mount', '--bind', helper, '/usr/local/libexec/whaledeck-privileged'], check=True)
    os.chdir(home)
    os.execv(dotnet, [dotnet, binary, home])


def exercise(sources, evidence, runtime_home, config, base, layout, hdd, request):
    private = runtime_home / 'helper-native'; private.mkdir(mode=0o700)
    library = private / 'libexec'; library.mkdir(mode=0o700)
    journal = private / 'journal'; journal.mkdir(mode=0o700)
    replacements = {'/usr/local/libexec': str(library),
        '/var/lib/whaledeck-development-archive-operations': str(journal),
        '/var/lib/whaledeck-development-disks/layout.json': str(layout),
        '/etc/whaledeck/development-storage.json': str(config),
        '/var/lib/whaledeck-development': str(base), '/data/DevSpace': str(hdd)}
    filenames = ('whaledeck-privileged', 'whaledeck-files.py', 'whaledeck-development-storage.py',
        'whaledeck-development-project-quota.py', 'whaledeck-development-quota-guard.py',
        'whaledeck-development-lifecycle.py',
        'whaledeck-development-archive-guard.py', 'whaledeck-development-archives.py',
        'whaledeck-development-archive-operations.py', 'whaledeck-development-archive-control.py',
        'whaledeck-development-archive-transport.py', 'whaledeck-file-tree.py', 'whaledeck-tree-archive.py')
    records = {}
    for name in filenames:
        original = (sources / name).read_bytes(); text = original.decode()
        text = isolate_text(text, replacements)
        text = text.replace('RuntimeMaxSec=7200', 'RuntimeMaxSec=90')
        text = text.replace('MemoryMax=512M --property=CPUQuota=100%', 'MemoryMax=512M --property=CPUQuota=10%')
        destination = library / name; destination.write_text(text); destination.chmod(0o750)
        records[name] = dict(sourceSha256=hashlib.sha256(original).hexdigest(),
                            isolatedSha256=hashlib.sha256(destination.read_bytes()).hexdigest())
    save(evidence / 'helper-source-copies.json', records)
    shutil.copyfile(__file__, evidence / 'helper-probe.py'); (evidence / 'helper-probe.py').chmod(0o600)
    copied_sources = evidence / 'helper-libexec'; shutil.copytree(library, copied_sources)
    for p in copied_sources.iterdir(): p.chmod(0o600)
    control = load(library / 'whaledeck-development-archive-control.py')
    files = load(library / 'whaledeck-files.py'); storage = load(library / 'whaledeck-development-storage.py')
    execution = control.Execution(storage.WorkspaceStorage(files))
    owned, commands, values = {}, [], {}
    def call(action, value, attempt=0):
        values[value['taskId']] = value
        commands.append(dict(action=action, taskId=value['taskId']))
        wire = None
        if bridge is None:
            result = subprocess.run([str(library / 'whaledeck-privileged'), 'development-archive-' + action, uuid.uuid4().hex],
                input=json.dumps(value), capture_output=True, text=True, timeout=25)
        else:
            wire = bridge.call(action, value)
            result = subprocess.CompletedProcess([], 0 if wire['rpcStatus'] == 'OK' else 2, wire['stdout'], wire['stderr'])
        save(evidence / ('helper-call-' + str(len(commands)) + '.json'),
             dict(action=action, taskId=value['taskId'], request=value, exitCode=result.returncode,
                  stdout=result.stdout, stderr=result.stderr, signedRpc=wire))
        path = '/system.slice/whaledeck-development-archive-run-' + value['taskId'].replace('-', '') + '.service'
        if action == 'start' and path not in owned:
            try: info = os.stat(control.CGROUP / path[1:])
            except FileNotFoundError: pass
            else: owned[path] = dict(bootId=execution.boot(), path=path, device=info.st_dev, inode=info.st_ino)
        busy = (wire is None and result.stderr.strip() == 'WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY' or
                wire is not None and wire['rpcStatus'] == 'Unavailable' and result.stderr == 'WORKSPACE_ARCHIVE_HOST_BUSY')
        if (result.returncode == 2 and busy
                and action in ('observe', 'cancel') and attempt < 10):
            time.sleep(0.1)
            return call(action, value, attempt + 1)
        assert result.returncode == 0, 'NATIVE_ARCHIVE_HELPER_' + action.upper() + '_FAILED'
        if wire is not None: assert wire['signatureRefusalsPassed'] is True
        receipt = json.loads(result.stdout); assert receipt['taskId'] == value['taskId']
        return receipt
    def finish(value):
        started = time.monotonic(); receipt = call('start', value)
        assert time.monotonic() - started < 12
        deadline = time.monotonic() + 60
        while receipt['status'] in ('Pending', 'Running', 'Finishing'):
            assert time.monotonic() < deadline, 'NATIVE_ARCHIVE_HELPER_DID_NOT_SETTLE'
            time.sleep(0.1); receipt = call('observe', value)
        assert receipt['status'] == 'Succeeded', 'NATIVE_ARCHIVE_HELPER_RESULT_REJECTED'
        assert call('start', value) == receipt
        return receipt
    accepted, partial, bridge = None, None, None
    try:
        published = rpc_publication()
        if published is not None: bridge = RpcBridge(published, library, evidence)
        command = dict(schemaVersion=1, taskId=str(uuid.uuid4()), archiveId=str(uuid.uuid4()),
            phase='plan', storage=request, expectedEpoch=2, planSha256='', receiptSha256='')
        plan = finish(command)
        capture = dict(command, taskId=str(uuid.uuid4()), phase='capture', planSha256=plan['result']['planSha256'])
        copied = finish(capture)
        verify = dict(capture, taskId=str(uuid.uuid4()), phase='verify', receiptSha256=copied['result']['receiptSha256'])
        checked = finish(verify); assert copied['result'] == checked['result']
        cancelled = dict(command, taskId=str(uuid.uuid4()))
        assert call('cancel', cancelled)['status'] == 'Stopped'
        assert call('start', cancelled)['status'] == 'Stopped'
        assert not (journal / cancelled['taskId'] / 'execution.json').exists()
        assert call('cancel', capture) == copied
        # A separate isolated allocation creates a long real compression, so
        # Observe/Cancel must work while both storage locks are actually held.
        quota = load(library / 'whaledeck-development-project-quota.py')
        lifecycle = load(library / 'whaledeck-development-lifecycle.py')
        lifecycle.GIT_CGROUP = private / 'no-fixture-git'
        driver = storage.WorkspaceStorage(files, quota=quota)
        long_storage = dict(request, workspaceId=str(uuid.uuid4()))
        driver.allocate(long_storage)
        payload = base / long_storage['rootId'] / long_storage['workspaceId'] / 'data'
        chunk = os.urandom(1024**2)
        with (payload / 'incompressible-fixture.bin').open('wb') as stream:
            for _ in range(200): stream.write(chunk)
        manager = lifecycle.Lifecycle(driver)
        transition = dict(storage=long_storage, action='stop', expectedEpoch=1, operationId='', planSha256='', storageProofSha256='')
        stopped = manager.run(transition)
        manager.run(dict(transition, operationId=str(uuid.uuid4()), planSha256=stopped['planSha256'],
                         storageProofSha256=stopped['storageProofSha256']), apply=True)
        long_plan = dict(command, taskId=str(uuid.uuid4()), archiveId=str(uuid.uuid4()), storage=long_storage)
        planned = finish(long_plan)
        long_capture = dict(long_plan, taskId=str(uuid.uuid4()), phase='capture', planSha256=planned['result']['planSha256'])
        call('start', long_capture)
        partial = hdd / 'workspace-archives-v1' / long_capture['archiveId'] / 'archive.partial'
        deadline = time.monotonic() + 60
        while not partial.exists() or partial.stat().st_size == 0:
            assert time.monotonic() < deadline, 'NATIVE_ARCHIVE_HELPER_NO_PARTIAL'
            time.sleep(0.025)
        assert call('observe', long_capture)['status'] == 'Running'
        started = time.monotonic(); assert call('cancel', long_capture)['status'] == 'Stopped'
        assert time.monotonic() - started < 18
        original_partial = (partial.stat().st_ino, partial.stat().st_size, hashlib.sha256(partial.read_bytes()).hexdigest())
        assert call('start', long_capture)['status'] == 'Stopped'
        assert original_partial == (partial.stat().st_ino, partial.stat().st_size, hashlib.sha256(partial.read_bytes()).hexdigest())
        accepted = dict(passed=True, kind='real-helper-systemd-seccomp-landlock-physical-nvme-hdd',
            planTask=command['taskId'], captureTask=capture['taskId'], verifyTask=verify['taskId'],
            receiptSha256=copied['result']['receiptSha256'], durablePrestartCancel=True,
            duplicateStartDoesNotRedispatch=True, lateCancelPreservesCompleted=True,
            activeCaptureCancelledUnderRealStorageLocks=True, cancelledPartialInodeAndBytesPreserved=True,
            nativeWorkerCpuQuotaPercent=10, signedAgentRpc=bridge is not None, productWorkflow=False)
    finally:
        bridge_error = None
        if bridge is not None:
            try: bridge.close()
            except BaseException as error: bridge_error = error
        # Original execution identities only. Failure keeps the journal and
        # stops teardown before the parent's images can be removed if uncertain.
        archives = load(library / 'whaledeck-development-archives.py')
        operations = load(library / 'whaledeck-development-archive-operations.py')
        driver = storage.WorkspaceStorage(files)
        archive = archives.DevelopmentArchives(driver, None, None, None)
        tasks = operations.ArchiveOperations(driver, archive, execution)
        # Also fence a manager submission whose reply/proof never arrived.
        # This is private fixture-only recovery, never a production bypass.
        for value in values.values():
            deadline = time.monotonic() + 3
            while True:
                try:
                    tasks.cancel(value); break
                except files.Rejected as error:
                    if str(error) != 'WORKSPACE_STORAGE_ARCHIVE_TASK_BUSY' or time.monotonic() >= deadline:
                        raise
                    time.sleep(0.025)
        for path in journal.glob('*/execution.json'):
            identity = json.loads(path.read_bytes()); owned.setdefault(identity['path'], identity)
        save(evidence / 'helper-owned-executions.json', list(owned.values()))
        for identity in owned.values():
            assert execution.settled(identity, stop=True), 'NATIVE_ARCHIVE_HELPER_CLEANUP_UNCONFIRMED'
        if partial is not None and partial.exists():
            retained_partial = evidence / 'helper-cancelled-archive.partial'
            shutil.copyfile(partial, retained_partial); retained_partial.chmod(0o600)
            save(evidence / 'helper-cancelled-partial.json', dict(size=partial.stat().st_size, inode=partial.stat().st_ino,
                sha256=hashlib.sha256(partial.read_bytes()).hexdigest()))
        save(evidence / 'helper-commands.json', commands)
        retained = evidence / 'helper-journal'; shutil.copytree(journal, retained)
        for p in retained.rglob('*'): p.chmod(0o700 if p.is_dir() else 0o600)
        if accepted is not None:
            accepted['executionResourcesSettled'] = True
            if bridge_error is None: save(evidence / 'helper-accepted.json', accepted)
        if bridge_error is not None: raise bridge_error
    return accepted


if __name__ == '__main__':
    rpc_namespace()
