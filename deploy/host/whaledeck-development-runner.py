#!/usr/bin/env python3
"""Root-only workspace launcher. Its caller must authorize the full run request."""

import contextlib
import ctypes
import errno
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import uuid

IMAGE_ROOT = Path('/var/lib/whaledeck-development-images')
RUNC = '/usr/bin/runc'
RUN_UID = 10001
RUNS = '.runner-runs'
REQUEST = 'request.json'
RESULT = 'result.json'
REPORT = 'test-report.xml'
REPORT_LIMIT = 16 * 1024 * 1024
REPORT_SLOT = '/tmp/whaledeck-results'
REPORT_COMMANDS = {
    'node': (['/usr/local/bin/node', '--test', '--test-reporter=junit',
              '--test-reporter-destination=' + REPORT_SLOT + '/tests.xml'], 'JUnit', 'tests.xml'),
    'dotnet': (['/usr/share/dotnet/dotnet', 'test', '--no-restore', '--results-directory', REPORT_SLOT,
                '--logger', 'trx;LogFileName=tests.trx'], 'Trx', 'tests.trx'),
}
KEY = re.compile(r'[a-z0-9]+(?:-[a-z0-9]+)*\Z')
VERSION = re.compile(r'[0-9A-Za-z][0-9A-Za-z.+-]{0,47}\Z')
RUN_ID = re.compile(r'[a-f0-9]{32}\Z')
SERVICE_CGROUP = re.compile(r'0::(/system\.slice/whaledeck-development-run-start-[a-f0-9]{32}\.service)/launcher\n?\Z')
REQUEST_LIMIT = 32768
READ = os.O_RDONLY | os.O_NONBLOCK
DIRECTORY = os.O_RDONLY | os.O_DIRECTORY
CAP_SYS_RESOURCE = 24
PR_CAPBSET_READ = 23
PR_CAPBSET_DROP = 24
CAPABILITY_VERSION_3 = 0x20080522


class CapabilityHeader(ctypes.Structure):
    _fields_ = [('version', ctypes.c_uint32), ('pid', ctypes.c_int)]


class CapabilityData(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint32) for name in ('effective', 'permitted', 'inheritable')]


class RunError(Exception):
    """A rejected launch or unconfirmed execution state."""


def refuse(reason):
    raise RunError('WORKSPACE_RUN_' + reason)


def enforce_staging_quota():
    """Remove the quota override from the launcher and any exec descendants."""
    libc = ctypes.CDLL(None, use_errno=True)
    arguments = (CAP_SYS_RESOURCE, 0, 0, 0)
    bounded = libc.prctl(PR_CAPBSET_READ, *arguments)
    if bounded < 0 or (bounded and libc.prctl(PR_CAPBSET_DROP, *arguments) != 0):
        refuse('QUOTA_CAPABILITY_UNAVAILABLE')
    header, capabilities = CapabilityHeader(CAPABILITY_VERSION_3, 0), (CapabilityData * 2)()
    if libc.capget(ctypes.byref(header), capabilities) != 0:
        refuse('QUOTA_CAPABILITY_UNAVAILABLE')
    word = capabilities[CAP_SYS_RESOURCE // 32]
    bit = 1 << (CAP_SYS_RESOURCE % 32)
    word.effective &= ~bit
    word.permitted &= ~bit
    word.inheritable &= ~bit
    if libc.capset(ctypes.byref(header), capabilities) != 0 or libc.prctl(PR_CAPBSET_READ, *arguments) != 0:
        refuse('QUOTA_CAPABILITY_UNAVAILABLE')


def request_binding(driver, isolation, rootfs, request):
    if set(request) != {'runId', 'storage', 'environment', 'runtime', 'argv', 'cpuMillis', 'memoryMiB', 'deadlineSeconds'}:
        refuse('REQUEST_INVALID')
    run_id = request['runId']
    if (not isinstance(run_id, str) or not RUN_ID.fullmatch(run_id) or run_id == '0' * 32
            or type(request['deadlineSeconds']) is not int or not 1 <= request['deadlineSeconds'] <= 3600):
        refuse('REQUEST_INVALID')
    driver.validate_request(request['storage'])
    isolation.validate_process(request['argv'], RUN_UID, request['cpuMillis'], request['memoryMiB'])
    raw = request['environment']
    if not isinstance(raw, str) or not raw.isascii() or not 0 < len(raw) <= 4096:
        refuse('ENVIRONMENT_INVALID')
    if hashlib.sha256(raw.encode('ascii')).hexdigest() != request['storage']['environmentSha256']:
        refuse('ENVIRONMENT_CHANGED')
    environment = rootfs.decode(raw)
    if set(environment) != {'version', 'runtimes'} or type(environment['version']) is not int or environment['version'] != 1:
        refuse('ENVIRONMENT_INVALID')
    runtimes = environment['runtimes']
    if not isinstance(runtimes, list) or not 1 <= len(runtimes) <= 4:
        refuse('ENVIRONMENT_INVALID')
    pins = {}
    for runtime in runtimes:
        if (not isinstance(runtime, dict) or set(runtime) != {'key', 'version', 'image'}
                or not isinstance(runtime['key'], str) or not 0 < len(runtime['key']) <= 48
                or not KEY.fullmatch(runtime['key']) or runtime['key'] in pins
                or not isinstance(runtime['version'], str) or not VERSION.fullmatch(runtime['version'])):
            refuse('ENVIRONMENT_INVALID')
        rootfs.reference_digest(runtime['image'])
        pins[runtime['key']] = runtime['image']
    selected = request['runtime']
    if not isinstance(selected, str) or selected not in pins:
        refuse('RUNTIME_NOT_REGISTERED')
    fingerprint = hashlib.sha256(rootfs.canonical(request)).hexdigest()
    return pins[selected], {'runId': run_id, 'requestSha256': fingerprint}


def workspace_directory(driver, rootfs, payload, name, proof):
    try:
        os.mkdir(name, mode=0o700, dir_fd=payload)
    except FileExistsError:
        pass
    fd = driver.files.open_at(payload, name, DIRECTORY)
    try:
        rootfs.private_directory(fd)
        driver.quota.verify(fd, proof['projectId'], proof['diskMiB'])
    except BaseException:
        os.close(fd)
        raise
    return fd


def verify_work(driver, payload, proof):
    # Source provisioning owns creation and ownership. Launching never
    # chowns an existing workspace or promotes an incomplete import.
    with driver.opened(payload, 'work', DIRECTORY) as work:
        info = os.fstat(work)
        if info.st_uid != RUN_UID or info.st_gid != RUN_UID or stat.S_IMODE(info.st_mode) != 0o700:
            refuse('WORK_IDENTITY_CHANGED')
        driver.quota.verify(work, proof['projectId'], proof['diskMiB'])


def report_spec(request):
    spec = REPORT_COMMANDS.get(request['runtime'])
    return spec[1:] if spec is not None and request['argv'] == spec[0] else None


def report_unavailable(spec):
    return {'format': spec[0], 'state': 'Unavailable'} if spec else None


def seal_report(driver, output, directory, spec):
    """Copy one bounded regular file after the workload has stopped."""
    format_name, name = spec
    result = {'format': format_name}
    staging = '.report-' + uuid.uuid4().hex
    try:
        entry = os.stat(name, dir_fd=output, follow_symlinks=False)
        if not stat.S_ISREG(entry.st_mode):
            return dict(result, state='Rejected')
        with driver.opened(output, name, READ) as source:
            identity = os.fstat(source)
            if (identity != entry or identity.st_uid != RUN_UID or identity.st_gid != RUN_UID
                    or identity.st_nlink != 1 or identity.st_size == 0):
                return dict(result, state='Rejected')
            if identity.st_size > REPORT_LIMIT:
                return dict(result, state='TooLarge')
            digest = hashlib.sha256()
            count = 0
            flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC
            with os.fdopen(os.open(staging, flags, 0o600, dir_fd=directory), 'wb') as target:
                while block := os.read(source, min(65536, identity.st_size + 1 - count)):
                    count += len(block)
                    if count > identity.st_size:
                        return dict(result, state='Rejected')
                    digest.update(block)
                    target.write(block)
                after = os.fstat(source)
                if (count != identity.st_size or after.st_ctime_ns != identity.st_ctime_ns
                        or after.st_mtime_ns != identity.st_mtime_ns or after.st_size != identity.st_size):
                    return dict(result, state='Rejected')
                target.flush()
                os.fsync(target.fileno())
        driver.files.exchange(directory, staging, directory, REPORT, 1)
        os.fsync(directory)
        return dict(result, state='Present', size=count, sha256=digest.hexdigest())
    except FileNotFoundError:
        return dict(result, state='Missing')
    except OSError as error:
        if error.errno in (errno.ELOOP, errno.EXDEV, errno.ENXIO, errno.ENODEV, errno.EISDIR):
            return dict(result, state='Rejected')
        return dict(result, state='Unavailable')
    finally:
        with contextlib.suppress(FileNotFoundError):
            os.unlink(staging, dir_fd=directory)


def valid_report(value, spec, status):
    if not isinstance(value, dict) or value.get('format') != spec[0]:
        return False
    state = value.get('state')
    if state == 'Present':
        return (status in ('Succeeded', 'Failed') and set(value) == {'format', 'state', 'size', 'sha256'}
                and type(value['size']) is int and 0 < value['size'] <= REPORT_LIMIT
                and isinstance(value['sha256'], str) and re.fullmatch(r'[a-f0-9]{64}', value['sha256']) is not None)
    return (set(value) == {'format', 'state'} and state in ('Missing', 'Rejected', 'TooLarge', 'Unavailable')
            and (status in ('Succeeded', 'Failed') or state == 'Unavailable'))


def execute(bundle, workspace_id, run_id, deadline):
    state = bundle / '.runner-state'
    state.mkdir(mode=0o700)
    name = 'whaledeck-dev-' + workspace_id.replace('-', '') + '-' + run_id
    prefix = [RUNC, '--root', str(state)]
    options = {'stdin': subprocess.DEVNULL, 'stdout': subprocess.DEVNULL, 'stderr': subprocess.DEVNULL,
               'env': {'PATH': '/usr/bin:/bin', 'LANG': 'C'}}
    try:
        job = subprocess.run(prefix + ['run', '--bundle', str(bundle), name], timeout=deadline, **options)
    except subprocess.TimeoutExpired:
        try:
            subprocess.run(prefix + ['delete', '--force', name], timeout=15, check=True, **options)
        except (OSError, subprocess.SubprocessError):
            refuse('CLEANUP_UNCONFIRMED')
        status, code = 'TimedOut', None
    else:
        status, code = ('Succeeded' if job.returncode == 0 else 'Failed'), job.returncode
    if (code is not None and code < 0) or any(state.iterdir()):
        refuse('CLEANUP_UNCONFIRMED')
    return status, code


@contextlib.contextmanager
def lock_run(directory, *, wait=False):
    try:
        fcntl.flock(directory, fcntl.LOCK_EX | (0 if wait else fcntl.LOCK_NB))
    except BlockingIOError:
        refuse('BUSY')
    try:
        yield
    finally:
        fcntl.flock(directory, fcntl.LOCK_UN)


def require_request(driver, directory, binding):
    try:
        recorded = driver.read_json(directory, REQUEST, 512)
    except FileNotFoundError:
        refuse('INTERRUPTED')
    if recorded != binding:
        refuse('REQUEST_CONFLICT')


def terminal_result(driver, directory, binding, spec):
    try:
        result = driver.read_json(directory, RESULT, 1024)
    except FileNotFoundError:
        return None
    state, code = result.get('status'), result.get('runtimeExitCode')
    expected = set(binding) | {'status', 'runtimeExitCode'} | ({'testReport'} if spec else set())
    if (set(result) != expected
            or any(result.get(key) != value for key, value in binding.items())
            or state not in ('Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'Interrupted')
            or (state in ('TimedOut', 'Cancelled', 'Interrupted') and code is not None)
            or (state in ('Succeeded', 'Failed') and (
                type(code) is not int or not 0 <= code <= 255 or (code == 0) != (state == 'Succeeded')))
            or (spec and not valid_report(result.get('testReport'), spec, state))):
        refuse('RECEIPT_INVALID')
    return result


def read_result(driver, directory, binding, spec):
    with lock_run(directory):
        require_request(driver, directory, binding)
        result = terminal_result(driver, directory, binding, spec)
        if result is None:
            refuse('INTERRUPTED')
        return result


def persist_result(driver, rootfs, directory, binding, status, code=None, *, report=None):
    result = dict(binding, status=status, runtimeExitCode=code)
    if report is not None:
        result['testReport'] = report
    staging = '.result-' + uuid.uuid4().hex
    try:
        rootfs.write_metadata(directory, staging, result)
        driver.files.exchange(directory, staging, directory, RESULT, 1)
        os.fsync(directory)
    finally:
        with contextlib.suppress(FileNotFoundError):
            os.unlink(staging, dir_fd=directory)
    return result


@contextlib.contextmanager
def open_run(driver, rootfs, payload, binding):
    with contextlib.ExitStack() as stack:
        try:
            runs = stack.enter_context(driver.opened(payload, RUNS, DIRECTORY))
            rootfs.private_directory(runs)
            directory = stack.enter_context(driver.opened(runs, binding['runId'], DIRECTORY))
            rootfs.private_directory(directory)
        except FileNotFoundError:
            refuse('NOT_FOUND')
        yield directory


def discard_bundle(driver, rootfs, directory):
    try:
        with driver.opened(directory, 'bundle', DIRECTORY) as bundle:
            info = rootfs.private_directory(bundle)
            path = os.readlink(f'/proc/self/fd/{bundle}')
            if not os.path.isabs(path) or not os.path.samestat(info, os.stat(path, follow_symlinks=False)):
                refuse('CLEANUP_UNCONFIRMED')
    except FileNotFoundError:
        # Confirm a missing slot, rather than a directory disappearing during
        # verification. Other errors such as symlinks/mounts fail closed.
        try:
            os.stat('bundle', dir_fd=directory, follow_symlinks=False)
        except FileNotFoundError:
            return
        refuse('CLEANUP_UNCONFIRMED')
    with open('/proc/self/mountinfo', encoding='utf-8') as stream:
        mounts = stream.read(1024 * 1024 + 1)
    if len(mounts) > 1024 * 1024:
        refuse('CLEANUP_UNCONFIRMED')
    for line in mounts.splitlines():
        mount = re.sub(r'\\([0-7]{3})', lambda value: chr(int(value[1], 8)), line.split()[4])
        if mount == path or mount.startswith(path + '/'):
            refuse('CLEANUP_UNCONFIRMED')
    shutil.rmtree('bundle', dir_fd=directory)
    os.fsync(directory)


def observe(driver, isolation, rootfs, request):
    _, binding = request_binding(driver, isolation, rootfs, request)
    with driver.attested_payload(request['storage']) as (payload, _):
        try:
            fcntl.flock(payload, fcntl.LOCK_SH | fcntl.LOCK_NB)
        except BlockingIOError:
            refuse('BUSY')
        with open_run(driver, rootfs, payload, binding) as directory:
            return read_result(driver, directory, binding, report_spec(request))


def reconcile(driver, isolation, rootfs, control, request, *, stop):
    _, binding = request_binding(driver, isolation, rootfs, request)
    with driver.attested_payload(request['storage']) as (payload, _):
        with open_run(driver, rootfs, payload, binding) as directory, lock_run(directory):
            require_request(driver, directory, binding)
            spec = report_spec(request)
            result = terminal_result(driver, directory, binding, spec)
            if result is not None:
                return result
            status = control.settle(driver, directory, stop=stop, refuse=refuse)
            discard_bundle(driver, rootfs, directory)
            if spec:
                for name in os.listdir(directory):
                    if name == REPORT or re.fullmatch(r'\.report-[a-f0-9]{32}', name):
                        os.unlink(name, dir_fd=directory)
                os.fsync(directory)
            return persist_result(driver, rootfs, directory, binding, status, report=report_unavailable(spec))


def start(driver, isolation, rootfs, unpacker, request, *, cgroup_parent=None, control=None):
    image, binding = request_binding(driver, isolation, rootfs, request)
    spec = report_spec(request)
    run_id = binding['runId']
    enforce_staging_quota()
    namespace = isolation.RunnerMountNamespace()
    files = driver.files
    with driver.attested_payload(request['storage']) as (payload, proof), contextlib.ExitStack() as stack:
        try:
            fcntl.flock(payload, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            refuse('BUSY')
        runs = workspace_directory(driver, rootfs, payload, RUNS, proof)
        stack.callback(os.close, runs)
        try:
            os.mkdir(run_id, mode=0o700, dir_fd=runs)
        except FileExistsError:
            with driver.opened(runs, run_id, DIRECTORY) as directory:
                rootfs.private_directory(directory)
                return read_result(driver, directory, binding, spec)
        os.fsync(runs)
        directory = stack.enter_context(driver.opened(runs, run_id, DIRECTORY))
        rootfs.private_directory(directory)
        with lock_run(directory):
            if cgroup_parent:
                control.record(driver, rootfs, directory, cgroup_parent)
            rootfs.write_metadata(directory, REQUEST, binding)
        verify_work(driver, payload, proof)
        digest = rootfs.reference_digest(image)[7:]
        cache = stack.enter_context(driver.absolute(IMAGE_ROOT))
        rootfs.private_directory(cache)
        source = stack.enter_context(driver.opened(cache, digest, DIRECTORY))
        bundle = workspace_directory(driver, rootfs, directory, 'bundle', proof)
        stack.callback(os.close, bundle)
        output = None
        try:
            rootfs.materialize(files, source, bundle, image, lambda fd, staged, output:
                unpacker.unpack(files, rootfs, isolation, fd, staged, output, cgroup_parent=cgroup_parent))
        except (OSError, files.Rejected, rootfs.RootfsError, unpacker.UnpackError) as error:
            if str(error) != 'WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED':
                shutil.rmtree('bundle', dir_fd=directory)
                os.fsync(directory)
            raise
        with namespace.project(payload, bundle, proof['payloadInode']) as projected:
            verify_work(driver, projected, proof)
            config = isolation.oci_config(files, bundle, projected, request['argv'], RUN_UID, request['cpuMillis'],
                                          request['memoryMiB'], rootfs_policy=rootfs, runtime_image=image)
            if spec:
                output = workspace_directory(driver, rootfs, bundle, 'test-output', proof)
                stack.callback(os.close, output)
                os.fchown(output, RUN_UID, RUN_UID)
                config['mounts'].append({
                    'destination': REPORT_SLOT, 'type': 'bind',
                    'source': os.path.join(os.readlink(f'/proc/self/fd/{bundle}'), 'test-output'),
                    'options': ['bind', 'rw', 'nosuid', 'nodev', 'noexec', 'rprivate'],
                })
            if cgroup_parent:
                config['linux']['cgroupsPath'] = cgroup_parent + '/workload'
            rootfs.write_metadata(bundle, 'config.json', config)
            status, code = execute(Path(os.readlink(f'/proc/self/fd/{bundle}')), proof['workspaceId'],
                                   run_id, request['deadlineSeconds'])
        with lock_run(directory, wait=True):
            existing = terminal_result(driver, directory, binding, spec)
            if existing is not None:
                return existing
            report = (seal_report(driver, output, directory, spec) if output is not None and code is not None
                      else report_unavailable(spec))
            discard_bundle(driver, rootfs, directory)
            return persist_result(driver, rootfs, directory, binding, status, code, report=report)


def load(filename):
    path = Path(__file__).with_name(filename)
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
            or info.st_mode & 0o7022 or not 0 < info.st_size <= 256 * 1024):
        raise PermissionError()
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def delegated_parent():
    with open('/proc/self/cgroup', encoding='ascii') as stream:
        match = SERVICE_CGROUP.fullmatch(stream.read(4097))
    if not match:
        refuse('DELEGATION_UNAVAILABLE')
    parent = match[1]
    available = (Path('/sys/fs/cgroup' + parent) / 'cgroup.controllers').read_text(encoding='ascii').split()
    if not {'cpu', 'memory', 'pids'} <= set(available):
        refuse('DELEGATION_UNAVAILABLE')
    return parent


def main():
    if os.geteuid() != 0 or sys.argv[1:] not in (
            [], ['observe'], ['service-start'], ['service-observe'], ['service-cancel'], ['service-reclaim']):
        return 2
    try:
        files = load('whaledeck-files.py')
        storage = load('whaledeck-development-storage.py')
        quota = load('whaledeck-development-project-quota.py')
        isolation = load('whaledeck-development-runner-isolation.py')
        rootfs = load('whaledeck-development-rootfs.py')
        unpacker = load('whaledeck-development-unpack.py')
        control = load('whaledeck-development-runner-control.py')
    except (ImportError, OSError):
        print('WORKSPACE_RUN_HELPER_UNAVAILABLE', file=sys.stderr)
        return 2
    try:
        raw = sys.stdin.buffer.read(REQUEST_LIMIT + 1)
        if not 0 < len(raw) <= REQUEST_LIMIT:
            refuse('REQUEST_INVALID')
        mode = sys.argv[1] if len(sys.argv) == 2 else ''
        driver = storage.WorkspaceStorage(files, quota=quota, sandboxed=mode.startswith('service-'))
        request = rootfs.decode(raw)
        if mode in ('observe', 'service-observe'):
            result = observe(driver, isolation, rootfs, request)
        elif mode in ('service-cancel', 'service-reclaim'):
            result = reconcile(driver, isolation, rootfs, control, request, stop=mode == 'service-cancel')
        else:
            parent = delegated_parent() if mode == 'service-start' else None
            result = start(driver, isolation, rootfs, unpacker, request, cgroup_parent=parent, control=control)
        print(json.dumps(dict(result, inputSha256=hashlib.sha256(raw).hexdigest()), separators=(',', ':')))
        return 0
    except (RunError, files.Rejected, rootfs.RootfsError, unpacker.UnpackError) as error:
        code = str(error)
        if not re.fullmatch(r'WORKSPACE_[A-Z_]{1,64}', code):
            code = 'WORKSPACE_RUN_REFUSED'
    except OSError as error:
        code = 'WORKSPACE_RUN_CAPACITY' if error.errno in (errno.ENOSPC, errno.EDQUOT) else 'WORKSPACE_RUN_UNAVAILABLE'
    except (ValueError, RuntimeError, quota.QuotaError):
        code = 'WORKSPACE_RUN_REFUSED'
    print(code, file=sys.stderr)
    return 2


if __name__ == '__main__':
    sys.exit(main())
