"""Execute the pinned OCI unpacker in a separate, write-confined OCI bundle."""

import hashlib
import os
from pathlib import Path
import platform
import stat
import subprocess
import sys
import uuid

UMOCI_PATH = '/usr/local/libexec/whaledeck-umoci'
RUNC_PATH = '/usr/bin/runc'
UMOCI_SHA256 = {
    'x86_64': 'b51c267ec394499e42c6fde47f240b7b7dba57ea49df0b5acd304378b82a3b71',
    'aarch64': '5cfd17f2e7a4bcf9ed67ea1b955ca893d200349b9ce6a3d3707dba415f458a1f',
}
TOOL_LIMIT = 16 * 1024**2
UNPACK_TIMEOUT = 180
EXTRACTION = '.oci-extraction'
STATE = '.runtime-state'
DESTINATION = 'result'
OUTPUT = 'output'
CAPABILITIES = ['CAP_CHOWN', 'CAP_DAC_OVERRIDE', 'CAP_FOWNER', 'CAP_FSETID']
METADATA_SYSCALLS = [
    'chown', 'fchown', 'fchownat', 'lchown', 'fsetxattr', 'lsetxattr', 'setxattr',
    'fremovexattr', 'lremovexattr', 'removexattr',
]


class UnpackError(Exception):
    """The isolated unpacker failed before publishing an output."""


def pinned_digest():
    digest = UMOCI_SHA256.get(platform.machine())
    if digest is None:
        raise UnpackError('WORKSPACE_UNPACK_PLATFORM_UNSUPPORTED')
    return digest


def checked_tool(path, expected_digest, *, installed=False):
    fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as source:
        info = os.fstat(source.fileno())
        if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= TOOL_LIMIT
                or (installed and (info.st_uid != 0 or stat.S_IMODE(info.st_mode) != 0o755))):
            raise UnpackError('WORKSPACE_UNPACK_TOOL_UNSAFE')
        data = source.read(TOOL_LIMIT + 1)
    if len(data) != info.st_size or hashlib.sha256(data).hexdigest() != expected_digest:
        raise UnpackError('WORKSPACE_UNPACK_TOOL_DIGEST')
    return data


def copy_tool(destination, expected_digest):
    data = checked_tool(UMOCI_PATH, expected_digest, installed=True)
    with destination.open('xb') as target:
        target.write(data)
        os.fchmod(target.fileno(), 0o500)


def install_artifact(data):
    if os.geteuid() != 0:
        raise UnpackError('WORKSPACE_UNPACK_INSTALL_ROOT_REQUIRED')
    target = Path(UMOCI_PATH)
    parent = os.open(target.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        info = os.fstat(parent)
        if info.st_uid != 0 or stat.S_IMODE(info.st_mode) & 0o022:
            raise UnpackError('WORKSPACE_UNPACK_INSTALL_PATH_UNSAFE')
        staging = '.whaledeck-umoci-' + uuid.uuid4().hex
        fd = os.open(staging, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_CLOEXEC, 0o600, dir_fd=parent)
        try:
            with os.fdopen(fd, 'wb') as stream:
                stream.write(data)
                stream.flush()
                os.fchmod(stream.fileno(), 0o755)
                os.fsync(stream.fileno())
            os.replace(staging, target.name, src_dir_fd=parent, dst_dir_fd=parent)
            os.fsync(parent)
        finally:
            try:
                os.unlink(staging, dir_fd=parent)
            except FileNotFoundError:
                pass
    finally:
        os.close(parent)


def configuration(isolation, image, output):
    # Only this invocation restores tar ownership and xattrs. Keep the normal
    # workload seccomp profile separate and do not grant either device access
    # or sockets here.
    seccomp = isolation.seccomp_profile()
    seccomp['syscalls'][0]['names'] = [
        name for name in seccomp['syscalls'][0]['names']
        if name not in ('connect', 'bind', 'listen', 'accept', 'accept4')
    ] + METADATA_SYSCALLS
    seccomp['syscalls'] = [rule for rule in seccomp['syscalls']
                           if rule['names'][0] not in ('socket', 'mknod', 'mknodat', 'ioctl')]
    return {
        'ociVersion': '1.0.2',
        'hostname': 'whaledeck-unpack',
        'root': {'path': 'jail', 'readonly': True},
        'process': {
            'terminal': False, 'user': {'uid': 0, 'gid': 0, 'additionalGids': []},
            'args': ['/umoci', 'unpack', '--image', '/image:runner', '/output/' + DESTINATION],
            'cwd': '/', 'noNewPrivileges': True, 'env': ['PATH=/', 'HOME=/', 'LANG=C', 'GOMAXPROCS=2'],
            'capabilities': {key: CAPABILITIES for key in ('bounding', 'effective', 'permitted')},
            'rlimits': [
                {'type': 'RLIMIT_CORE', 'soft': 0, 'hard': 0},
                {'type': 'RLIMIT_NOFILE', 'soft': 1024, 'hard': 1024},
                {'type': 'RLIMIT_FSIZE', 'soft': 8 * 1024**3, 'hard': 8 * 1024**3},
            ],
        },
        'mounts': [
            {'destination': '/proc', 'type': 'proc', 'source': 'proc', 'options': ['ro', 'nosuid', 'nodev', 'noexec']},
            {'destination': '/dev', 'type': 'tmpfs', 'source': 'tmpfs', 'options': ['nosuid', 'noexec', 'mode=755', 'size=1m']},
            {'destination': '/image', 'type': 'bind', 'source': str(image),
             'options': ['bind', 'ro', 'nosuid', 'nodev', 'noexec', 'rprivate']},
            {'destination': '/output', 'type': 'bind', 'source': str(output),
             'options': ['bind', 'rw', 'nosuid', 'nodev', 'noexec', 'rprivate']},
        ],
        'linux': {
            'namespaces': [{'type': kind} for kind in ('mount', 'pid', 'network', 'ipc', 'uts', 'cgroup')],
            'maskedPaths': ['/proc/kcore', '/proc/keys', '/proc/sysrq-trigger', '/proc/timer_list'],
            'resources': {
                'devices': [{'allow': False, 'access': 'rwm'},
                            {'allow': True, 'type': 'c', 'major': 1, 'minor': 3, 'access': 'rw'}],
                'pids': {'limit': 32}, 'cpu': {'quota': 200000, 'period': 100000},
                'memory': {'limit': 768 * 1024**2, 'swap': 768 * 1024**2},
            },
            'seccomp': seccomp,
        },
    }


def runtime(base, extraction):
    env = {'PATH': '/usr/bin:/bin', 'LANG': 'C'}
    streams = {'stdin': subprocess.DEVNULL, 'stdout': subprocess.DEVNULL, 'stderr': subprocess.DEVNULL}
    state = extraction / STATE
    state.mkdir(mode=0o700)
    container = 'whaledeck-unpack-' + uuid.uuid4().hex
    prefix = [RUNC_PATH, '--root', str(state)]
    try:
        subprocess.run(prefix + ['run', '--bundle', str(extraction), container],
                       cwd=base, env=env, check=True, timeout=UNPACK_TIMEOUT, **streams)
    except (OSError, subprocess.SubprocessError) as exc:
        try:
            subprocess.run(prefix + ['delete', '--force', container], cwd=base, env=env,
                           check=True, timeout=15, **streams)
        except (OSError, subprocess.SubprocessError):
            # Leave all private state available for recovery. The caller
            # must never publish or reuse this bundle on a failed exit.
            raise UnpackError('WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED') from exc
        raise UnpackError('WORKSPACE_UNPACK_FAILED') from exc
    if any(state.iterdir()):
        raise UnpackError('WORKSPACE_UNPACK_CLEANUP_UNCONFIRMED')


def unpack(files, rootfs, isolation, bundle, input_name, output_name):
    """Use the fixed tools with input and output slots in this private bundle."""
    if (input_name, output_name) != (rootfs.INPUT, rootfs.UNPACKED):
        raise UnpackError('WORKSPACE_UNPACK_SLOT_INVALID')
    identity = rootfs.private_directory(bundle)
    base = Path(os.readlink(f'/proc/self/fd/{bundle}'))
    if not base.is_absolute() or not os.path.samestat(identity, base.lstat()):
        raise UnpackError('WORKSPACE_UNPACK_BUNDLE_CHANGED')
    input_fd = files.open_at(bundle, input_name, rootfs.DIR)
    try:
        rootfs.private_directory(input_fd)
    finally:
        os.close(input_fd)

    extraction = base / EXTRACTION
    extraction.mkdir(mode=0o700)
    jail = extraction / 'jail'
    jail.mkdir(mode=0o700)
    for name in ('proc', 'dev', 'image', 'output'):
        (jail / name).mkdir(mode=0o755)
    output = extraction / OUTPUT
    output.mkdir(mode=0o700)
    copy_tool(jail / 'umoci', pinned_digest())
    extraction_fd = files.open_at(bundle, EXTRACTION, rootfs.DIR)
    try:
        rootfs.write_metadata(extraction_fd, 'config.json', configuration(isolation, base / input_name, output))
        runtime(base, extraction)
        output_fd = files.open_at(extraction_fd, OUTPUT, rootfs.DIR)
        try:
            rootfs.private_directory(output_fd)
            result = files.open_at(output_fd, DESTINATION, rootfs.DIR)
            try:
                rootfs.private_directory(result, exact=False)
                files.exchange(output_fd, DESTINATION, bundle, output_name, 1)
            finally:
                os.close(result)
        finally:
            os.close(output_fd)
    finally:
        os.close(extraction_fd)


if __name__ == '__main__':
    if len(sys.argv) != 3 or sys.argv[1] not in ('verify-artifact', 'install-artifact'):
        sys.exit('Usage: whaledeck-development-unpack.py {verify-artifact|install-artifact} PATH')
    try:
        data = checked_tool(sys.argv[2], pinned_digest())
        if sys.argv[1] == 'install-artifact':
            install_artifact(data)
    except (UnpackError, OSError) as error:
        sys.exit(str(error) if isinstance(error, UnpackError) else 'WORKSPACE_UNPACK_ARTIFACT_IO')
