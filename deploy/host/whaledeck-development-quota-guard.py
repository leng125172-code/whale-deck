"""Narrow the fixed allocator's privileged syscall arguments before stdin."""

import ctypes
import errno
import os
import platform
import sys


class Filter(ctypes.Structure):
    _fields_ = [('code', ctypes.c_ushort), ('yes', ctypes.c_ubyte),
                ('no', ctypes.c_ubyte), ('value', ctypes.c_uint)]


class Program(ctypes.Structure):
    _fields_ = [('length', ctypes.c_ushort), ('filters', ctypes.POINTER(Filter))]


def restrict_path_writes():
    # Linux Landlock ABI 3. Handle all path mutation rights without
    # granting any. The inherited stdout pipe remains usable.
    write_file = 1 << 1
    remove_make_refer_truncate = ((1 << 15) - 1) ^ ((1 << 4) - 1)
    rules = ctypes.c_uint64(write_file | remove_make_refer_truncate)
    libc = ctypes.CDLL(None, use_errno=True)
    fd = libc.syscall(444, ctypes.byref(rules), ctypes.sizeof(rules), 0)
    if fd < 0:
        raise OSError(ctypes.get_errno(), 'Source export write guard unavailable')
    try:
        if libc.syscall(446, fd, 0) != 0:
            raise OSError(ctypes.get_errno(), 'Source export write guard unavailable')
    finally:
        os.close(fd)


def install(quota, *, read_only=False, archive_flags=False, release_only=False):
    if release_only and read_only:
        raise ValueError('Quota release and read-only access are exclusive')
    # Native syscall numbers and AUDIT_ARCH values from Linux UAPI.
    architectures = {
        'x86_64': (0xc000003e, 16, (59, 101, 179, 304, 310, 311, 312, 321, 322, 323)),
        'aarch64': (0xc00000b7, 29, (60, 117, 221, 265, 270, 271, 272, 280, 281, 282)),
    }
    native = architectures.get(platform.machine())
    if native is None or ctypes.sizeof(ctypes.c_void_p) != 8 or sys.byteorder != 'little':
        raise OSError(errno.ENOSYS, 'Allocator seccomp ABI unavailable')
    arch, ioctl, blocked = native
    load_word, equal, greater_equal, ret = 0x20, 0x15, 0x35, 0x06
    allow, deny, kill = 0x7fff0000, 0x00050000 | errno.EPERM, 0x80000000
    rules = [(load_word, 0, 0, 4), (equal, 1, 0, arch), (ret, 0, 0, kill),
             (load_word, 0, 0, 0), (greater_equal, 0, 1, 0x40000000), (ret, 0, 0, kill)]
    # Deny alternate quota entry, execution, process/FD capture, BPF,
    # userfaultfd and io_uring even if the outer service allows them.
    for number in (*blocked, 425, 426, 427, 438):
        rules.extend(((equal, 0, 1, number), (ret, 0, 0, deny)))
    ioctls = (quota.FSGETXATTR,) if read_only or release_only else (quota.FSGETXATTR, quota.FSSETXATTR)
    if archive_flags:
        if not read_only and not release_only:
            raise ValueError('Archive flags require read-only quota access')
        ioctls += (0x80086601,)  # FS_IOC_GETFLAGS, never SETFLAGS.
    quota_commands = (quota.Q_GETSTATE, quota.Q_GETQUOTA)
    if not read_only:
        quota_commands += (quota.Q_SETQUOTA,)
    for number, commands in ((ioctl, ioctls), (quota.QUOTACTL_FD, quota_commands)):
        rules.extend(((equal, 0, len(commands) + 3, number), (load_word, 0, 0, 24)))
        rules.extend((equal, len(commands) - index, 0, command) for index, command in enumerate(commands))
        rules.extend(((ret, 0, 0, deny), (ret, 0, 0, allow)))
    rules.append((ret, 0, 0, allow))
    instructions = (Filter * len(rules))(*(Filter(*rule) for rule in rules))
    program = Program(len(rules), instructions)
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(38, 1, 0, 0, 0) != 0 or libc.prctl(22, 2, ctypes.byref(program), 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'Allocator seccomp unavailable')
