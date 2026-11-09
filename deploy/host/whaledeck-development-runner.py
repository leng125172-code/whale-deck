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

IMAGE_ROOT = Path('/var/lib/whaledeck-development-images')
RUNC = '/usr/bin/runc'
RUN_UID = 10001
RUNS = '.runner-runs'
REQUEST = 'request.json'
RESULT = 'result.json'
KEY = re.compile(r'[a-z0-9]+(?:-[a-z0-9]+)*\Z')
VERSION = re.compile(r'[0-9A-Za-z][0-9A-Za-z.+-]{0,47}\Z')
RUN_ID = re.compile(r'[a-f0-9]{32}\Z')
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


def read_result(driver, directory, binding):
    try:
        recorded = driver.read_json(directory, REQUEST, 512)
    except FileNotFoundError:
        refuse('INTERRUPTED')
    if recorded != binding:
        refuse('REQUEST_CONFLICT')
    try:
        result = driver.read_json(directory, RESULT, 512)
    except FileNotFoundError:
        refuse('INTERRUPTED')
    state, code = result.get('status'), result.get('runtimeExitCode')
    if (set(result) != set(binding) | {'status', 'runtimeExitCode'}
            or any(result.get(key) != value for key, value in binding.items())
            or state not in ('Succeeded', 'Failed', 'TimedOut')
            or (state == 'TimedOut' and code is not None)
            or (state != 'TimedOut' and (
                type(code) is not int or not 0 <= code <= 255 or (code == 0) != (state == 'Succeeded')))):
        refuse('RECEIPT_INVALID')
    return result


def observe(driver, isolation, rootfs, request):
    _, binding = request_binding(driver, isolation, rootfs, request)
    with driver.attested_payload(request['storage']) as (payload, _):
        try:
            fcntl.flock(payload, fcntl.LOCK_SH | fcntl.LOCK_NB)
        except BlockingIOError:
            refuse('BUSY')
        try:
            with driver.opened(payload, RUNS, DIRECTORY) as runs:
                rootfs.private_directory(runs)
                with driver.opened(runs, binding['runId'], DIRECTORY) as directory:
                    rootfs.private_directory(directory)
                    return read_result(driver, directory, binding)
        except FileNotFoundError:
            refuse('NOT_FOUND')


def start(driver, isolation, rootfs, unpacker, request):
    image, binding = request_binding(driver, isolation, rootfs, request)
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
                return read_result(driver, directory, binding)
        os.fsync(runs)
        directory = stack.enter_context(driver.opened(runs, run_id, DIRECTORY))
        rootfs.private_directory(directory)
        rootfs.write_metadata(directory, REQUEST, binding)
        verify_work(driver, payload, proof)
        digest = rootfs.reference_digest(image)[7:]
        cache = stack.enter_context(driver.absolute(IMAGE_ROOT))
        rootfs.private_directory(cache)
        source = stack.enter_context(driver.opened(cache, digest, DIRECTORY))
        bundle = workspace_directory(driver, rootfs, directory, 'bundle', proof)
        stack.callback(os.close, bundle)
        try:
            rootfs.materialize(files, source, bundle, image, lambda fd, staged, output:
                unpacker.unpack(files, rootfs, isolation, fd, staged, output))
        except (OSError, files.Rejected, rootfs.RootfsError, unpacker.UnpackError) as error:
            if str(error) != 'WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED':
                shutil.rmtree('bundle', dir_fd=directory)
                os.fsync(directory)
            raise
        with namespace.project(payload, bundle, proof['payloadInode']) as projected:
            verify_work(driver, projected, proof)
            config = isolation.oci_config(files, bundle, projected, request['argv'], RUN_UID, request['cpuMillis'],
                                          request['memoryMiB'], rootfs_policy=rootfs, runtime_image=image)
            rootfs.write_metadata(bundle, 'config.json', config)
            status, code = execute(Path(os.readlink(f'/proc/self/fd/{bundle}')), proof['workspaceId'],
                                   run_id, request['deadlineSeconds'])
        shutil.rmtree('bundle', dir_fd=directory)
        os.fsync(directory)
        result = dict(binding, status=status, runtimeExitCode=code)
        rootfs.write_metadata(directory, RESULT, result)
        return result


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


def main():
    if os.geteuid() != 0 or sys.argv[1:] not in ([], ['observe']):
        return 2
    try:
        files = load('whaledeck-files.py')
        storage = load('whaledeck-development-storage.py')
        quota = load('whaledeck-development-project-quota.py')
        isolation = load('whaledeck-development-runner-isolation.py')
        rootfs = load('whaledeck-development-rootfs.py')
        unpacker = load('whaledeck-development-unpack.py')
    except (ImportError, OSError):
        print('WORKSPACE_RUN_HELPER_UNAVAILABLE', file=sys.stderr)
        return 2
    try:
        raw = sys.stdin.buffer.read(REQUEST_LIMIT + 1)
        if not 0 < len(raw) <= REQUEST_LIMIT:
            refuse('REQUEST_INVALID')
        driver = storage.WorkspaceStorage(files, quota=quota)
        request = rootfs.decode(raw)
        result = (observe(driver, isolation, rootfs, request) if len(sys.argv) == 2 else
                  start(driver, isolation, rootfs, unpacker, request))
        print(json.dumps(result, separators=(',', ':')))
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
