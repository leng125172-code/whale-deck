"""Narrow the fixed allocator's privileged syscall arguments before stdin."""

import ctypes
import errno
import platform
import sys


class Filter(ctypes.Structure):
    _fields_ = [('code', ctypes.c_ushort), ('yes', ctypes.c_ubyte),
                ('no', ctypes.c_ubyte), ('value', ctypes.c_uint)]


class Program(ctypes.Structure):
    _fields_ = [('length', ctypes.c_ushort), ('filters', ctypes.POINTER(Filter))]


def install(quota):
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
    for number, commands in (
            (ioctl, (quota.FSGETXATTR, quota.FSSETXATTR)),
            (quota.QUOTACTL_FD, (quota.Q_GETSTATE, quota.Q_GETQUOTA, quota.Q_SETQUOTA))):
        rules.extend(((equal, 0, len(commands) + 3, number), (load_word, 0, 0, 24)))
        rules.extend((equal, len(commands) - index, 0, command) for index, command in enumerate(commands))
        rules.extend(((ret, 0, 0, deny), (ret, 0, 0, allow)))
    rules.append((ret, 0, 0, allow))
    instructions = (Filter * len(rules))(*(Filter(*rule) for rule in rules))
    program = Program(len(rules), instructions)
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(38, 1, 0, 0, 0) != 0 or libc.prctl(22, 2, ctypes.byref(program), 0, 0) != 0:
        raise OSError(ctypes.get_errno(), 'Allocator seccomp unavailable')
