"""Landlock write confinement for the fixed, non-executing archive helper.

Source FDs are opened only after this rule is installed. Writable paths are
passed by trusted internal code as already checked directory FDs, never stdin.
Metadata-changing syscalls are independently denied by the systemd unit.
"""
import ctypes
import os
import platform

WRITE_RIGHTS = (1 << 1) | (((1 << 15) - 1) ^ ((1 << 4) - 1))


class PathBeneath(ctypes.Structure):
    _pack_ = 1
    _fields_ = [('allowed_access', ctypes.c_uint64), ('parent_fd', ctypes.c_int32)]


def install(directories):
    if platform.machine() not in ('x86_64', 'aarch64') or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise OSError('Archive write confinement unavailable')
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.syscall(444, 0, 0, 1) < 3:
        raise OSError('Archive write confinement unavailable')
    attr = ctypes.c_uint64(WRITE_RIGHTS)
    ruleset = libc.syscall(444, ctypes.byref(attr), ctypes.sizeof(attr), 0)
    if ruleset < 0:
        raise OSError(ctypes.get_errno(), 'Archive write confinement unavailable')
    try:
        for directory in directories:
            rule = PathBeneath(WRITE_RIGHTS, directory)
            if libc.syscall(445, ruleset, 1, ctypes.byref(rule), 0) != 0:
                raise OSError(ctypes.get_errno(), 'Archive write confinement unavailable')
        if libc.prctl(38, 1, 0, 0, 0) != 0 or libc.syscall(446, ruleset, 0) != 0:
            raise OSError(ctypes.get_errno(), 'Archive write confinement unavailable')
    finally:
        os.close(ruleset)
