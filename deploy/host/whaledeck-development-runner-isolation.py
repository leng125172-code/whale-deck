"""Private mount projection and syscall policy for untrusted workspace jobs."""

import contextlib
import ctypes
import errno
import os
import platform
import stat

CLONE_NEWNS = 0x00020000
MS_REC = 0x4000
MS_PRIVATE = 0x40000
AT_EMPTY_PATH = 0x1000
OPEN_TREE_CLONE = 1
MOVE_MOUNT_EMPTY = 0x44
OPEN_TREE = 428
MOVE_MOUNT = 429
MOUNT_SETATTR = 442
MOUNT_ATTR_NOSUID_NODEV = 0x06
MNT_DETACH = 0x02
PROJECTION = 'workspace'
DIR = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
LIBC = ctypes.CDLL(None, use_errno=True)
LIBC.syscall.restype = ctypes.c_long


class MountAttr(ctypes.Structure):
    _fields_ = [(name, ctypes.c_uint64) for name in ('set', 'clear', 'propagation', 'userns_fd')]


def checked(result):
    if result < 0:
        number = ctypes.get_errno()
        raise OSError(number, os.strerror(number))
    return result


def require_private_directory(fd):
    info = os.fstat(fd)
    if info.st_uid != 0 or not stat.S_ISDIR(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o700:
        raise PermissionError('Runner mount directory is not private')
    return info


class RunnerMountNamespace:
    """Enter a private namespace in a dedicated launcher process.

    The process must exit to dispose of this namespace. No mount made here
    should be passed back to a host-namespace Docker daemon.
    """

    def __init__(self):
        if platform.machine() not in ('x86_64', 'aarch64') or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise OSError(errno.ENOSYS, 'Runner mount ABI unavailable')
        before = os.stat('/proc/self/ns/mnt')
        checked(LIBC.unshare(ctypes.c_int(CLONE_NEWNS)))
        checked(LIBC.mount(None, ctypes.c_char_p(b'/'), None, ctypes.c_ulong(MS_REC | MS_PRIVATE), None))
        self.namespace = os.stat('/proc/self/ns/mnt')
        if os.path.samestat(before, self.namespace):
            raise PermissionError('Runner mount namespace did not change')

    @contextlib.contextmanager
    def project(self, source, bundle, expected_inode):
        """Expose an attested payload in a new root-only bundle slot."""
        if not os.path.samestat(self.namespace, os.stat('/proc/self/ns/mnt')):
            raise PermissionError('Runner mount namespace changed')
        info = require_private_directory(source)
        parent = require_private_directory(bundle)
        if info.st_ino != expected_inode or os.path.samestat(info, parent):
            raise PermissionError('Runner mount identity is invalid')

        os.mkdir(PROJECTION, 0o700, dir_fd=bundle)
        destination = os.open(PROJECTION, DIR, dir_fd=bundle)
        try:
            self._attach(source, destination)
        finally:
            os.close(destination)
        try:
            projected = os.open(PROJECTION, DIR, dir_fd=bundle)
            try:
                required = os.ST_NOSUID | os.ST_NODEV
                if (not os.path.samestat(info, require_private_directory(projected)) or
                        os.fstatvfs(projected).f_flag & required != required):
                    raise PermissionError('Runner projected mount is invalid')
                yield projected
            finally:
                os.close(projected)
        finally:
            target = os.fsencode(f'/proc/self/fd/{bundle}/{PROJECTION}')
            checked(LIBC.umount2(ctypes.c_char_p(target), ctypes.c_int(MNT_DETACH)))

    @staticmethod
    def _attach(source, destination):
        empty = ctypes.c_char_p(b'')
        flags = AT_EMPTY_PATH | OPEN_TREE_CLONE | os.O_CLOEXEC
        clone = checked(LIBC.syscall(ctypes.c_long(OPEN_TREE), ctypes.c_int(source), empty, ctypes.c_uint(flags)))
        try:
            attr = MountAttr(MOUNT_ATTR_NOSUID_NODEV, 0, 0, 0)
            checked(LIBC.syscall(ctypes.c_long(MOUNT_SETATTR), ctypes.c_int(clone), empty,
                                 ctypes.c_uint(AT_EMPTY_PATH), ctypes.byref(attr), ctypes.c_size_t(ctypes.sizeof(attr))))
            checked(LIBC.syscall(ctypes.c_long(MOVE_MOUNT), ctypes.c_int(clone), empty,
                                 ctypes.c_int(destination), empty, ctypes.c_uint(MOVE_MOUNT_EMPTY)))
        finally:
            os.close(clone)


# Native application ABI. Privileged operations, namespace creation, quota
# mutation, asynchronous syscall submission, and unrestricted ioctl are absent.
APPLICATION_SYSCALLS = """
accept accept4 access bind brk capget chdir chmod clock_getres clock_gettime
clock_nanosleep close close_range connect copy_file_range creat dup dup2 dup3
epoll_create epoll_create1 epoll_ctl epoll_pwait epoll_pwait2 epoll_wait eventfd
eventfd2 execve execveat exit exit_group faccessat faccessat2 fadvise64 fallocate
fchdir fchmod fchmodat fchmodat2 fcntl fdatasync fgetxattr flistxattr flock
fork fstat fstatfs fsync ftruncate futex futex_waitv getcpu getcwd getdents
getdents64 getegid geteuid getgid getgroups getitimer getpeername getpgid
getpgrp getpid getppid getpriority getrandom getresgid getresuid getrlimit
get_robust_list getrusage getsid getsockname getsockopt gettid gettimeofday
getuid getxattr inotify_add_watch inotify_init inotify_init1 inotify_rm_watch
kill lgetxattr link linkat listen listxattr llistxattr lseek lstat madvise
membarrier memfd_create mincore mkdir mkdirat mlock mlock2 mlockall mmap
mprotect mremap msync munlock munlockall munmap nanosleep newfstatat open
openat openat2 pause pipe pipe2 poll ppoll prctl pread64 preadv preadv2
prlimit64 pselect6 pwrite64 pwritev pwritev2 read readlink readlinkat readv
recvfrom recvmmsg recvmsg rename renameat renameat2 restart_syscall rmdir
rseq rt_sigaction rt_sigpending rt_sigprocmask rt_sigqueueinfo rt_sigreturn
rt_sigsuspend rt_sigtimedwait rt_tgsigqueueinfo sched_getaffinity sched_getattr
sched_getparam sched_getscheduler sched_get_priority_max sched_get_priority_min
sched_setaffinity sched_yield select sendfile sendmmsg sendmsg
sendto set_robust_list set_tid_address setitimer setpgid setrlimit setsid
setsockopt shutdown sigaltstack splice stat statfs statx symlink symlinkat
sync_file_range sysinfo tee tgkill time timer_create timer_delete
timer_getoverrun timer_gettime timer_settime timerfd_create timerfd_gettime
timerfd_settime times tkill truncate umask uname unlink unlinkat utime
utimensat utimes vfork vmsplice wait4 waitid write writev
""".split()


def seccomp_profile():
    allow = 'SCMP_ACT_ALLOW'
    rules = [{'names': APPLICATION_SYSCALLS, 'action': allow}]
    # arch_prctl is native x86-64 only.
    if platform.machine() == 'x86_64':
        rules.append({'names': ['arch_prctl'], 'action': allow})
    rules.append({'names': ['clone3'], 'action': 'SCMP_ACT_ERRNO', 'errnoRet': errno.ENOSYS})
    rules.append({'names': ['clone'], 'action': allow, 'args': [
        {'index': 0, 'value': 0x7e020080, 'valueTwo': 0, 'op': 'SCMP_CMP_MASKED_EQ'}]})
    rules.append({'names': ['sched_setscheduler'], 'action': allow, 'args': [
        {'index': 1, 'value': 0, 'op': 'SCMP_CMP_EQ'}]})  # SCHED_OTHER
    # Runtime diagnostic FIFOs are permitted. Device nodes still fail.
    for name, mode_argument in (('mknod', 1), ('mknodat', 2)):
        rules.append({'names': [name], 'action': allow, 'args': [
            {'index': mode_argument, 'value': 0o170000, 'valueTwo': 0o010000, 'op': 'SCMP_CMP_MASKED_EQ'}]})
    for family in (1, 2, 10):  # AF_UNIX, AF_INET, AF_INET6
        rules.append({'names': ['socket', 'socketpair'], 'action': allow, 'args': [
            {'index': 0, 'value': family, 'op': 'SCMP_CMP_EQ'}]})
    # Terminal and ordinary pipe/socket queries only. Both Linux filesystem
    # attribute-setting ioctl families remain forbidden, including compat forms.
    for command in (0x5401, 0x5402, 0x5403, 0x5404, 0x540f, 0x5410, 0x5413, 0x541b, 0x5421, 0x5450, 0x5451):
        rules.append({'names': ['ioctl'], 'action': allow, 'args': [
            {'index': 1, 'value': command, 'op': 'SCMP_CMP_EQ'}]})
    return {'defaultAction': 'SCMP_ACT_ERRNO', 'defaultErrnoRet': errno.EPERM, 'syscalls': rules}


def validate_process(argv, uid, cpu_millis, memory_mib):
    if (not isinstance(argv, list) or not 1 <= len(argv) <= 64
            or any(not isinstance(arg, str) or '\0' in arg or len(arg.encode('utf-8')) > 4096 for arg in argv)
            or not os.path.isabs(argv[0]) or argv[0].startswith('//') or os.path.normpath(argv[0]) != argv[0]
            or sum(len(arg.encode('utf-8')) + 1 for arg in argv) > 16384):
        raise ValueError('Runner argv is invalid')
    if (type(uid) is not int or not 10000 <= uid <= 0x7fffffff or
            type(cpu_millis) is not int or not 100 <= cpu_millis <= 64000 or
            type(memory_mib) is not int or not 128 <= memory_mib <= 1024 * 1024):
        raise ValueError('Runner identity or budget is invalid')


def oci_config(files, bundle, projected, argv, uid, cpu_millis, memory_mib, *, rootfs_policy, runtime_image):
    """Build the fixed OCI policy inside the trusted launcher's private namespace."""
    identity = require_private_directory(bundle)
    bundle_path = os.readlink(f'/proc/self/fd/{bundle}')
    if (not os.path.isabs(bundle_path) or not os.path.samestat(identity, os.stat(bundle_path, follow_symlinks=False))):
        raise PermissionError('Runner bundle identity changed')
    validate_process(argv, uid, cpu_millis, memory_mib)
    visible = files.open_at(bundle, PROJECTION, DIR, cross_mount=True)
    try:
        mount_flags = os.ST_NOSUID | os.ST_NODEV
        if (not os.path.samestat(require_private_directory(projected), os.fstat(visible))
                or os.fstatvfs(visible).f_flag & mount_flags != mount_flags):
            raise PermissionError('Runner projection changed')
    finally:
        os.close(visible)
    work = files.open_at(projected, 'work', DIR)
    try:
        info = os.fstat(work)
        if (info.st_uid != uid or info.st_gid != uid or stat.S_IMODE(info.st_mode) != 0o700):
            raise PermissionError('Runner work directory has the wrong identity')
    finally:
        os.close(work)
    rootfs_policy.require_materialized(files, bundle, runtime_image)

    memory = memory_mib * 1024**2
    source = os.path.join(bundle_path, PROJECTION, 'work')
    return {
        'ociVersion': '1.0.2',
        'hostname': 'whaledeck-runner',
        'root': {'path': 'rootfs', 'readonly': True},
        'process': {
            'terminal': False, 'user': {'uid': uid, 'gid': uid, 'additionalGids': []},
            'args': argv.copy(), 'cwd': '/work', 'noNewPrivileges': True,
            'env': ['PATH=/usr/local/bin:/usr/bin:/bin', 'HOME=/tmp', 'TMPDIR=/tmp',
                    'DOTNET_CLI_HOME=/tmp', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', 'DOTNET_NOLOGO=1'] + (
                        ['NUGET_PACKAGES=/work/.nuget/packages'] if argv[0] == '/usr/share/dotnet/dotnet' else []),
            'capabilities': {key: [] for key in ('bounding', 'effective', 'inheritable', 'permitted', 'ambient')},
            'rlimits': [{'type': 'RLIMIT_CORE', 'soft': 0, 'hard': 0},
                        {'type': 'RLIMIT_NOFILE', 'soft': 4096, 'hard': 4096}],
        },
        'mounts': [
            {'destination': '/proc', 'type': 'proc', 'source': 'proc', 'options': ['nosuid', 'nodev', 'noexec', 'ro']},
            {'destination': '/dev', 'type': 'tmpfs', 'source': 'tmpfs', 'options': ['nosuid', 'mode=755', 'size=4m']},
            {'destination': '/tmp', 'type': 'tmpfs', 'source': 'tmpfs',
             'options': ['nosuid', 'nodev', 'noexec', 'mode=1777', 'size=64m']},
            {'destination': '/work', 'type': 'bind', 'source': source, 'options': ['bind', 'rw', 'nosuid', 'nodev', 'rprivate']},
        ],
        'linux': {
            'namespaces': [{'type': kind} for kind in ('mount', 'pid', 'network', 'ipc', 'uts', 'cgroup')],
            'maskedPaths': ['/proc/acpi', '/proc/asound', '/proc/interrupts', '/proc/kcore', '/proc/keys',
                            '/proc/latency_stats', '/proc/sched_debug', '/proc/scsi', '/proc/timer_list',
                            '/proc/sysrq-trigger', '/sys/firmware'],
            'resources': {
                'devices': [{'allow': False, 'access': 'rwm'}] + [
                    {'allow': True, 'type': 'c', 'major': major, 'minor': minor, 'access': 'rw'}
                    for major, minor in ((1, 3), (1, 5), (1, 7), (1, 8), (1, 9), (5, 0))],
                'pids': {'limit': 128}, 'memory': {'limit': memory, 'swap': memory},
                'cpu': {'quota': cpu_millis * 100, 'period': 100000},
            },
            'seccomp': seccomp_profile(),
        },
    }
