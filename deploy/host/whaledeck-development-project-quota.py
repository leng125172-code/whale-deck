"""FD-bound Linux project quota primitives for an exclusively reserved project ID."""

import ctypes
import errno
import fcntl
import os
import platform
import stat
import struct

# Linux UAPI for native x86-64/aarch64. The VFS quota commands use 1 KiB
# blocks, including on XFS. GETQSTATV reports whether limits are enforced.
QUOTACTL_FD = 443
Q_GETSTATE = 0x00580802
Q_GETQUOTA = 0x80000702
Q_SETQUOTA = 0x80000802
PROJECT_ENFORCING = 0x30
QIF_ALL = 0x3f
QIF_LIMITS = 0x05
FSGETXATTR = 0x801c581f
FSSETXATTR = 0x401c5820
PROJINHERIT = 0x200
REALTIME_FLAGS = 0x101
FILE_ATTR = struct.Struct('=5I8x')
QUOTA = struct.Struct('=8QI4x')
LIBC = ctypes.CDLL(None, use_errno=True)
LIBC.syscall.restype = ctypes.c_long


class QuotaError(Exception):
    """A safe refusal without host paths or device details."""


def quota_call(fd, command, project, buffer):
    if platform.machine() not in ('x86_64', 'aarch64') or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise QuotaError('QUOTA_UNSUPPORTED')
    storage = (ctypes.c_char * len(buffer)).from_buffer(buffer)
    if LIBC.syscall(ctypes.c_long(QUOTACTL_FD), ctypes.c_uint(fd), ctypes.c_uint(command),
                    ctypes.c_uint(project), ctypes.byref(storage)) != 0:
        number = ctypes.get_errno()
        raise OSError(number, os.strerror(number))


def require_enforcing(fd):
    state = bytearray(160)
    state[0] = 1  # FS_QSTATV_VERSION1
    try:
        quota_call(fd, Q_GETSTATE, 0, state)
    except OSError as error:
        raise QuotaError('QUOTA_UNAVAILABLE') from error
    if state[0] != 1 or struct.unpack_from('=H', state, 2)[0] & PROJECT_ENFORCING != PROJECT_ENFORCING:
        raise QuotaError('QUOTA_NOT_ENFORCING')


def read_quota(fd, project):
    data = bytearray(QUOTA.size)
    try:
        quota_call(fd, Q_GETQUOTA, project, data)
    except OSError as error:
        # XFS returns ENOENT for an unused ID. Disabled quotas are checked
        # independently with GETQSTATV before any absent ID can be used.
        if error.errno == errno.ENOENT:
            return (0,) * 8
        raise QuotaError('QUOTA_UNAVAILABLE') from error
    *values, valid = QUOTA.unpack(data)
    if valid & QIF_ALL != QIF_ALL:
        raise QuotaError('QUOTA_UNAVAILABLE')
    return tuple(values)


def read_attributes(fd):
    try:
        return FILE_ATTR.unpack(fcntl.ioctl(fd, FSGETXATTR, bytes(FILE_ATTR.size)))
    except OSError as error:
        raise QuotaError('QUOTA_ATTRIBUTES_UNAVAILABLE') from error


def require_project_id(project):
    if type(project) is not int or not 1 <= project <= 0x7fffffff:
        raise QuotaError('QUOTA_REQUEST_INVALID')


def limits(project, disk_mib):
    require_project_id(project)
    if type(disk_mib) is not int or not 256 <= disk_mib <= 1024 * 1024:
        raise QuotaError('QUOTA_REQUEST_INVALID')
    return disk_mib * 1024, min(disk_mib * 256, 1024 * 1024)


def matches_limits(state, expected):
    hard_blocks, soft_blocks, _, hard_inodes, soft_inodes, _, block_timer, inode_timer = state
    return (hard_blocks, hard_inodes) == expected and not any((soft_blocks, soft_inodes, block_timer, inode_timer))


def require_unused(fd, project):
    require_project_id(project)
    require_enforcing(fd)
    if any(read_quota(fd, project)):
        raise QuotaError('QUOTA_ID_IN_USE')


def verify(fd, project, disk_mib):
    expected = limits(project, disk_mib)
    require_enforcing(fd)
    flags, _, _, actual_project, _ = read_attributes(fd)
    state = read_quota(fd, project)
    if (actual_project != project or not flags & PROJINHERIT or flags & REALTIME_FLAGS
            or not matches_limits(state, expected) or state[2] > expected[0] * 1024 or state[5] > expected[1]):
        raise QuotaError('QUOTA_BINDING_CHANGED')


def attach(fd, project, disk_mib):
    """Attach an already claimed ID to a private, empty payload directory.

    The caller must own a durable, exclusive claim for both the ID and inode
    before entering. A matching limit on a still-empty unused ID permits
    recovery after a stop between setting the quota and tagging the inode.
    """
    expected = limits(project, disk_mib)
    info = os.fstat(fd)
    if info.st_uid != 0 or not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
        raise QuotaError('QUOTA_DIRECTORY_UNSAFE')
    require_enforcing(fd)
    flags, extent, _, actual_project, cow = read_attributes(fd)
    if actual_project == project:
        verify(fd, project, disk_mib)
        os.fsync(fd)
        return
    if actual_project != 0 or flags & REALTIME_FLAGS or os.listdir(fd):
        raise QuotaError('QUOTA_DIRECTORY_UNSAFE')
    state = read_quota(fd, project)
    if any(state) and (state[2] or state[5] or not matches_limits(state, expected)):
        raise QuotaError('QUOTA_ID_IN_USE')
    if flags & PROJINHERIT and not matches_limits(state, expected):
        raise QuotaError('QUOTA_DIRECTORY_UNSAFE')
    try:
        if not any(state):
            hard_blocks, hard_inodes = expected
            data = bytearray(QUOTA.pack(hard_blocks, 0, 0, hard_inodes, 0, 0, 0, 0, QIF_LIMITS))
            quota_call(fd, Q_SETQUOTA, project, data)
            if not matches_limits(read_quota(fd, project), expected):
                raise QuotaError('QUOTA_BINDING_CHANGED')
        fcntl.ioctl(fd, FSSETXATTR, FILE_ATTR.pack(flags | PROJINHERIT, extent, 0, project, cow))
        os.fsync(fd)
    except OSError as error:
        raise QuotaError('QUOTA_ATTACH_FAILED') from error
    verify(fd, project, disk_mib)
