#!/usr/bin/env python3
"""Opt-in real Kestrel/systemd socket tests, confined to a loopback-only netns.

Never installs/restarts production units or changes host networking. Synthetic
upstream absence proves listener transport, not authenticated product workflows.
Requires a self-contained Release/linux-x64 MaintenanceHost publication.
"""
import concurrent.futures
import ctypes
import hashlib
import http.client
import json
import os
from pathlib import Path
import resource
import shutil
import socket
import stat
import subprocess
import sys
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / 'deploy/host'
PUBLISH = ROOT / 'src/WhaleDeck.MaintenanceHost/bin/Release/net10.0/linux-x64/publish'
NAME = 'whaledeck-maintenance-http'
LIBC = ctypes.CDLL(None, use_errno=True)


def require(value, message):
    if not value:
        raise RuntimeError(message)


def command(*args, check=True):
    return subprocess.run(args, check=check, capture_output=True, text=True, timeout=45).stdout.strip()


def netns(operation, value):
    args = (value,) if operation == 'unshare' else (value, 0x40000000)
    require(getattr(LIBC, operation)(*args) == 0,
            operation + ' failed: errno ' + str(ctypes.get_errno()))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def snapshot():
    # Identities and hashes only. Never read container environments or logs.
    units = command('systemctl', 'show', 'whaledeck-agent.service', 'whaledeck-maintenance.service',
                    '--property=LoadState,MainPID,InvocationID,ActiveState')
    containers = []
    if shutil.which('docker'):
        names = command('docker', 'ps', '--format', '{{.Names}}', check=False).splitlines()
        names = sorted(n for n in names if n.startswith(('whaledeck-', 'database-platform-')))
        if names:
            containers = command('docker', 'inspect', '--format',
                '{{.Id}} {{.Image}} {{.State.StartedAt}} {{if .State.Health}}{{.State.Health.Status}}{{end}}', *names).splitlines()
    files = [Path('/var/lib/whaledeck-agent/platform-maintenance/deployment-revisions.json'),
             Path('/etc/sudoers.d/whaledeck-agent')]
    files += list(Path('/etc/whaledeck').glob('*.json'))
    return dict(units=units, containers=containers,
                protected={str(p): [digest(p), p.stat().st_ino, stat.S_IMODE(p.stat().st_mode)]
                           for p in files if p.is_file()})


def freeze(target):
    require(PUBLISH.is_dir() and not PUBLISH.is_symlink(), 'publish self-contained host first')
    files = {}
    for path in sorted(PUBLISH.rglob('*')):
        info = path.lstat()
        require(not stat.S_ISLNK(info.st_mode), 'publication contains a symlink')
        if path.is_dir():
            continue
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1, 'publication contains a nonregular/hardlinked file')
        relative = path.relative_to(PUBLISH)
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
        original = digest(path)
        shutil.copyfile(path, destination)
        destination.chmod(0o755 if info.st_mode & 0o111 else 0o644)
        require(digest(destination) == original == digest(path), 'publication changed during freeze')
        files[str(relative)] = original
    require('WhaleDeck.MaintenanceHost' in files and 'libcoreclr.so' in files, 'not a self-contained publication')
    # sudo may inherit umask 0077; all frozen content is public application
    # code and must be traversable by the unprivileged test service.
    for directory in [target, *(p for p in target.rglob('*') if p.is_dir())]:
        directory.chmod(0o755)
    return files


def probe_http(port, path):
    started = time.monotonic()
    connection = http.client.HTTPConnection('127.0.0.1', port, timeout=10)
    try:
        connection.request('GET', path, headers={'Connection': 'close'})
        response = connection.getresponse()
        body = response.read(16385)
        require(len(body) <= 16384 and response.status == 503, 'unexpected isolated HTTP response')
        if path == '/':
            require('Whale Deck'.encode() in body and '维护'.encode() in body, 'maintenance HTML missing')
        else:
            require(json.loads(body)['phase'] == 'GatewayOffline', 'unexpected status JSON')
        return dict(port=port, path=path, status=response.status,
                    milliseconds=round((time.monotonic() - started) * 1000, 2))
    finally:
        connection.close()


def sockets_of(pid):
    values = set()
    for path in Path('/proc', str(pid), 'fd').iterdir():
        try:
            link = os.readlink(path)
            if link.startswith('socket:['):
                values.add(link)
        except FileNotFoundError:
            pass
    return values


def wait_ready(process=None):
    until = time.monotonic() + 20
    while time.monotonic() < until:
        require(process is None or process.poll() is None, 'candidate exited before HTTP readiness')
        try:
            return [probe_http(p, q) for p in (8080, 8081) for q in ('/', '/maintenance/status')]
        except (OSError, http.client.HTTPException):
            time.sleep(0.05)
    raise RuntimeError('candidate readiness timeout')


def fd_child(binary, mode, namespace):
    require(os.stat('/proc/self/ns/net').st_ino == int(namespace), 'FD child escaped private namespace')
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    handles = []
    if mode != 'missing-fds':
        for port in (8080, 8081):
            kind = socket.SOCK_DGRAM if mode == 'udp' else socket.SOCK_STREAM
            value = socket.socket(socket.AF_INET, kind)
            value.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            value.bind(('127.0.0.1' if mode == 'loopback' else '0.0.0.0', port))
            if kind == socket.SOCK_STREAM and mode != 'not-listening':
                value.listen(128)
            # Keep sources clear of targets 3/4 before any dup2.
            import fcntl
            handles.append(fcntl.fcntl(value.fileno(), fcntl.F_DUPFD_CLOEXEC, 20))
            value.close()
        if mode == 'reverse':
            handles.reverse()
        if mode == 'duplicate':
            handles[1], old = handles[0], handles[1]
            os.close(old)
        for destination, source in enumerate(handles, 3):
            os.dup2(source, destination, inheritable=True)
        for source in set(handles):
            os.close(source)
    environment = dict(PATH='/usr/bin:/bin', HOME='/nonexistent', DOTNET_PROCESSOR_COUNT='2',
        Maintenance__SocketActivation='required', Maintenance__StatusPath='/nonexistent/status.json',
        LISTEN_PID=str(os.getpid()), LISTEN_FDS='2', LISTEN_FDNAMES=NAME + ':' + NAME)
    if mode == 'missing-context':
        for key in ('LISTEN_PID', 'LISTEN_FDS', 'LISTEN_FDNAMES'):
            environment.pop(key)
    if mode == 'wrong-pid':
        environment['LISTEN_PID'] = str(os.getppid())
    if mode == 'wrong-count':
        environment['LISTEN_FDS'] = '1'
    if mode == 'wrong-name':
        environment['LISTEN_FDNAMES'] = NAME + ':other'
    if mode == 'disabled-context':
        environment['Maintenance__SocketActivation'] = 'disabled'
    if mode == 'invalid-mode':
        environment['Maintenance__SocketActivation'] = 'optional'
    os.chdir(Path(binary).parent)
    os.setgroups([])
    os.setgid(65534)
    os.setuid(65534)
    os.execve(binary, [binary], environment)


def fd_matrix(binary, evidence, namespace):
    results = {}
    modes = ('reverse', 'missing-context', 'wrong-pid', 'wrong-count', 'wrong-name',
             'duplicate', 'udp', 'not-listening', 'loopback', 'missing-fds', 'disabled-context', 'invalid-mode')
    for mode in modes:
        log = evidence / ('fd-' + mode + '.log')
        with log.open('wb') as output:
            process = subprocess.Popen(['/usr/bin/python3', '-I', '-B', str(Path(__file__).resolve()),
                '--fd-child', str(binary), mode, str(namespace)], stdout=output, stderr=output)
            try:
                if mode == 'reverse':
                    responses = wait_ready(process)
                    results[mode] = dict(passed=True, nonroot=True, responses=responses)
                else:
                    require(process.wait(timeout=15) != 0 and
                            'MAINTENANCE_SOCKET_ACTIVATION_INVALID' in log.read_text(),
                            'bad activation was not rejected: ' + mode)
                    results[mode] = dict(passed=True, refusedBeforeBind=True)
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
    return results


def main():
    require(sys.argv[1:] == ['--run'] and os.geteuid() == 0, 'explicit root --run required')
    require(Path('/proc/1/comm').read_text().strip() == 'systemd', 'real PID 1 systemd required')
    token = uuid.uuid4().hex
    unit = 'wd-maintenance-socket-qa-' + token
    runtime = Path('/run') / unit
    evidence = ROOT / 'deploy/artifacts' / ('maintenance-socket-native-' + token)
    evidence.mkdir(mode=0o700)
    runtime.mkdir(mode=0o755)
    runtime.chmod(0o755)
    # /run is deliberately noexec on the workstation. Freeze executable code
    # under a unique /opt path, matching the production service's executable FS.
    frozen = Path('/opt') / unit
    frozen.mkdir(mode=0o755)
    before = snapshot()
    original_net = os.open('/proc/self/ns/net', os.O_RDONLY | os.O_CLOEXEC)
    original_inode = os.fstat(original_net).st_ino
    namespace_path = runtime / 'network'
    service_path = Path('/run/systemd/system') / (unit + '.service')
    socket_path = service_path.with_suffix('.socket')
    mounted = False
    result = dict(schemaVersion=1, kind='real-kestrel-systemd-private-network', passed=False)
    try:
        result['publication'] = freeze(frozen)
        netns('unshare', 0x40000000)
        namespace = os.stat('/proc/self/ns/net').st_ino
        require(namespace != original_inode, 'network namespace was not isolated')
        command('ip', 'link', 'set', 'lo', 'up')
        interfaces = json.loads(command('ip', '-json', 'link'))
        require([v['ifname'] for v in interfaces] == ['lo'], 'namespace has an external interface')
        namespace_path.touch(mode=0o600)
        command('mount', '--bind', '/proc/self/ns/net', str(namespace_path))
        mounted = True
        result['namespace'] = dict(original=original_inode, isolated=namespace, interfaces=['lo'])
        result['fdMatrix'] = fd_matrix(frozen / 'WhaleDeck.MaintenanceHost', evidence, namespace)
        service = (HOST / 'whaledeck-maintenance.service').read_text()
        service = service.replace('whaledeck-maintenance.socket', unit + '.socket')
        service = service.replace('User=whaledeck-maintenance\nGroup=whaledeck', 'DynamicUser=yes')
        service = service.replace('/opt/whaledeck-maintenance', str(frozen))
        service = service.replace('Restart=always', 'Restart=no')
        service = service.replace('[Service]\n', '[Service]\nNetworkNamespacePath=' + str(namespace_path) +
            '\nCPUQuota=200%\nMemoryMax=512M\nTasksMax=128\nEnvironment=DOTNET_PROCESSOR_COUNT=2\n' +
            'Environment=Maintenance__StatusPath=' + str(runtime / 'absent-status.json') + '\n')
        listener = (HOST / 'whaledeck-maintenance.socket').read_text()
        listener = listener.replace('whaledeck-maintenance.service', unit + '.service')
        listener = listener.replace('[Socket]\n', '[Socket]\nNetworkNamespacePath=' + str(namespace_path) + '\n')
        for path, value in ((service_path, service), (socket_path, listener)):
            path.write_text(value)
            path.chmod(0o644)
            (evidence / path.name).write_text(value)
        command('systemd-analyze', 'verify', str(service_path), str(socket_path))
        command('systemctl', 'daemon-reload')
        command('systemctl', 'start', unit + '.socket', unit + '.service')
        result['initialResponses'] = wait_ready()
        require(command('systemctl', 'show', unit + '.socket', '-p', 'Accept', '--value') == 'no', 'Accept changed')
        require(command('systemctl', 'show', unit + '.socket', '-p', 'FileDescriptorName', '--value') == NAME,
                'descriptor name not applied')
        # Sockets is a configuration directive, exposed as TriggeredBy over D-Bus.
        require(unit + '.socket' in command('systemctl', 'show', unit + '.service', '-p', 'TriggeredBy', '--value').split(),
                'service is not associated with its socket')
        initial_pid = int(command('systemctl', 'show', unit + '.service', '-p', 'MainPID', '--value'))
        require(initial_pid > 1 and os.stat('/proc/' + str(initial_pid) + '/ns/net').st_ino == namespace,
                'service escaped isolated namespace')
        require(int(Path('/proc', str(initial_pid), 'status').read_text().split('Uid:')[1].split()[0]) != 0,
                'service is privileged')
        listeners = sockets_of(1) & sockets_of(initial_pid)
        require(len(listeners) == 2, 'PID 1 and Kestrel do not share exactly two sockets')
        result['listenerInodes'] = sorted(listeners)
        result['pids'] = [initial_pid]
        stop = threading.Event()
        observations, errors = [], []
        result['observations'] = observations
        result['errors'] = errors

        def observer(port, path):
            while not stop.is_set():
                try:
                    observations.append(probe_http(port, path))
                except Exception as error:
                    errors.append(dict(port=port, path=path, error=type(error).__name__,
                                       errno=getattr(error, 'errno', None), detail=str(error)[:256]))
                stop.wait(0.02)

        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
            tasks = [executor.submit(observer, p, q) for p in (8080, 8081) for q in ('/', '/maintenance/status')]
            try:
                for _ in range(5):
                    # Respect the unchanged systemd start limiter (five starts
                    # per ten seconds); do not weaken production hardening.
                    time.sleep(3)
                    command('systemctl', 'restart', unit + '.service')
                    wait_ready()
                    pid = int(command('systemctl', 'show', unit + '.service', '-p', 'MainPID', '--value'))
                    require(pid > 1 and pid not in result['pids'], 'maintenance process did not change')
                    require(os.stat('/proc/' + str(pid) + '/ns/net').st_ino == namespace, 'restart escaped namespace')
                    require(listeners <= sockets_of(1) & sockets_of(pid), 'listening socket changed during restart')
                    require(command('systemctl', 'is-active', unit + '.socket') == 'active', 'socket lost during restart')
                    result['pids'].append(pid)
                time.sleep(0.2)
            finally:
                stop.set()
                for task in tasks:
                    task.result(timeout=15)
        require(len(observations) >= 20 and not errors, 'HTTP continuity failed; see evidence')
        result['passed'] = True
    except Exception as error:
        result['error'] = type(error).__name__ + ': ' + str(error)
        raise
    finally:
        cleanup_errors = []
        for suffix in ('.service', '.socket'):
            try:
                command('systemctl', 'stop', unit + suffix)
            except subprocess.CalledProcessError:
                if (service_path if suffix == '.service' else socket_path).exists():
                    cleanup_errors.append('stop ' + suffix)
        (evidence / 'journal.log').write_text(command('journalctl', '-u', unit + '.service',
            '-u', unit + '.socket', '--no-pager', '-o', 'short-iso', check=False))
        for path in (service_path, socket_path):
            path.unlink(missing_ok=True)
        command('systemctl', 'daemon-reload')
        command('systemctl', 'reset-failed', unit + '.service', unit + '.socket', check=False)
        if mounted:
            command('umount', str(namespace_path))
        netns('setns', original_net)
        os.close(original_net)
        shutil.rmtree(runtime)
        shutil.rmtree(frozen)
        result['productionUnchanged'] = snapshot() == before
        result['resourcesRemoved'] = not any(p.exists() for p in (runtime, frozen, service_path, socket_path))
        result['cleanupErrors'] = cleanup_errors
        result['passed'] = result['passed'] and result['productionUnchanged'] and result['resourcesRemoved'] and not cleanup_errors
        (evidence / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
        print(json.dumps(dict(evidence=str(evidence), passed=result['passed'],
            productionUnchanged=result['productionUnchanged'], resourcesRemoved=result['resourcesRemoved'])))
        require(result['passed'], 'native maintenance socket gate failed; evidence retained')


if __name__ == '__main__':
    if len(sys.argv) == 5 and sys.argv[1] == '--fd-child':
        fd_child(*sys.argv[2:])
    else:
        main()
