#!/usr/bin/env python3
"""Root-owned Docker Exec bridge; no host command, namespace entry or transcript storage.

Only immutable container IDs, an inspected startup instance and fixed shell
choices are accepted. Closing ends this interactive Exec, never the container.
Deliberately detached background services remain container-owned (Unix semantics).
"""
import base64
import http.client
import importlib.util
import json
import os
from pathlib import Path
import re
import selectors
import signal
import socket
import stat
import sys
import tempfile
import time

SHELLS = {'sh': '/bin/sh', 'bash': '/bin/bash', 'ash': '/bin/ash'}
STATE = Path('/var/lib/whaledeck-terminal')
REGISTRY = Path('/etc/whaledeck/resources.json')
ID = re.compile(r'[a-f0-9]{64}')


def base_driver():
    path = Path(__file__).with_name('whaledeck-host-terminal.py')
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        raise PermissionError('DRIVER_UNSAFE')
    spec = importlib.util.spec_from_file_location('whaledeck_fixed_host_terminal', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def registry():
    info = REGISTRY.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022 or info.st_size > 65536:
        raise PermissionError('REGISTRY_UNSAFE')
    rows = json.loads(REGISTRY.read_bytes())
    if not isinstance(rows, list) or any(not isinstance(row, dict) for row in rows):
        raise PermissionError('REGISTRY_UNSAFE')
    return rows


def target(container, identifier, resources):
    if not isinstance(identifier, str) or not ID.fullmatch(identifier) or container.get('Id') != identifier:
        raise ValueError('CONTAINER_ID_REQUIRED')
    name = container.get('Name', '').lstrip('/')
    labels = (container.get('Config') or {}).get('Labels') or {}
    protected = {row.get('externalId') for row in resources if row.get('type') == 'Container'}
    if name in protected or identifier in protected or str(labels.get('io.whaledeck.protected', '')).lower() == 'true':
        raise PermissionError('CONTAINER_PROTECTED')
    state = container.get('State') or {}
    if state.get('Running') is not True or any(state.get(key) for key in ('Paused', 'Restarting', 'Dead')):
        raise ValueError('CONTAINER_NOT_RUNNING')
    host = container.get('HostConfig') or {}
    if host.get('Privileged') or host.get('CapAdd') or host.get('Devices') or host.get('DeviceRequests') or host.get('DeviceCgroupRules'):
        raise PermissionError('CONTAINER_UNSAFE_ISOLATION')
    if any(host.get(key) == 'host' or str(host.get(key, '')).startswith('container:')
           for key in ('PidMode', 'IpcMode', 'NetworkMode', 'UTSMode', 'UsernsMode', 'CgroupnsMode')):
        raise PermissionError('CONTAINER_UNSAFE_ISOLATION')
    if any(value in ('seccomp=unconfined', 'apparmor=unconfined', 'label=disable') for value in host.get('SecurityOpt') or []):
        raise PermissionError('CONTAINER_UNSAFE_ISOLATION')
    roots = [row.get('path') for row in resources if row.get('id') == 'whaledeck.apps'
             and row.get('type') == 'ManagedDirectory' and isinstance(row.get('path'), str)]
    if len(roots) != 1 or not roots[0].startswith('/'):
        raise PermissionError('REGISTRY_UNSAFE')
    approved = Path(roots[0])
    if approved != approved.resolve(): raise PermissionError('REGISTRY_UNSAFE')
    for mount in container.get('Mounts') or []:
        if mount.get('Type') == 'bind':
            source = Path(mount.get('Source', ''))
            if not source.is_absolute() or not source.is_relative_to(approved) or not source.resolve().is_relative_to(approved):
                raise PermissionError('CONTAINER_UNSAFE_MOUNT')
        elif mount.get('Type') == 'volume':
            if not re.fullmatch(r'whaledeck-app-[a-z0-9][a-z0-9_.-]{0,127}', mount.get('Name', '')):
                raise PermissionError('CONTAINER_UNSAFE_MOUNT')
        elif mount.get('Type') != 'tmpfs':
            raise PermissionError('CONTAINER_UNSAFE_MOUNT')
    pid, started = state.get('Pid'), state.get('StartedAt')
    if type(pid) is not int or pid <= 1 or not isinstance(started, str) or not 1 <= len(started) <= 64:
        raise ValueError('CONTAINER_INSTANCE_INVALID')
    return {'containerId': identifier, 'name': name, 'instanceId': str(pid) + ':' + started,
            'imageId': container.get('Image', ''), 'user': (container.get('Config') or {}).get('User') or ''}


def actual_mounts(container):
    # The source path may have been replaced after Docker established a bind.
    # Check the actual mounted object, not just today's source-path spelling.
    pid = container['State']['Pid']
    for mount in container.get('Mounts') or []:
        if mount.get('Type') != 'bind': continue
        destination = mount.get('Destination', '')
        if not isinstance(destination, str) or not destination.startswith('/') or '..' in destination.split('/'):
            raise PermissionError('CONTAINER_UNSAFE_MOUNT')
        source = Path(mount['Source']).stat()
        mounted = (Path(f'/proc/{pid}/root') / destination.lstrip('/')).stat()
        if (source.st_dev, source.st_ino) != (mounted.st_dev, mounted.st_ino):
            raise PermissionError('CONTAINER_MOUNT_IDENTITY_CHANGED')


class DockerConnection(http.client.HTTPConnection):
    def __init__(self):
        super().__init__('localhost', timeout=5)

    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        try: self.sock.connect('/var/run/docker.sock')
        except BaseException: self.sock.close(); raise


class DockerFailure(ValueError):
    def __init__(self, status):
        super().__init__('DOCKER_REQUEST_FAILED')
        self.status = status


def api(method, path, value=None):
    connection = DockerConnection()
    try:
        body = json.dumps(value).encode() if value is not None else None
        connection.request(method, '/v1.44' + path, body, {'Content-Type': 'application/json'})
        response = connection.getresponse()
        raw = response.read(2 * 1024 * 1024 + 1)
        if response.status not in (200, 201, 204): raise DockerFailure(response.status)
        if len(raw) > 2 * 1024 * 1024: raise ValueError('DOCKER_RESPONSE_LIMIT')
        return json.loads(raw) if raw else None
    finally: connection.close()


def start_stream(exec_id):
    connection = DockerConnection()
    connection.connect()
    stream = connection.sock
    reader = stream.makefile('rb', buffering=0)
    try:
        body = b'{"Detach":false,"Tty":true}'
        stream.sendall(('POST /v1.44/exec/' + exec_id + '/start HTTP/1.1\r\nHost: localhost\r\n'
                        'Connection: Upgrade\r\nUpgrade: tcp\r\nContent-Type: application/json\r\n'
                        'Content-Length: ' + str(len(body)) + '\r\n\r\n').encode() + body)
        status = reader.readline(4096)
        size = len(status)
        if status.split()[:2] != [b'HTTP/1.1', b'101']:
            raise ValueError('DOCKER_UPGRADE_FAILED')
        while True:
            line = reader.readline(4096); size += len(line)
            if not line or size > 16384: raise ValueError('DOCKER_UPGRADE_FAILED')
            if line == b'\r\n': break
        connection.sock = None
        return stream
    except BaseException: stream.close(); raise
    finally: reader.close(); connection.close()


def record(session):
    path = STATE / (session + '.json')
    info = path.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_size > 4096:
        raise PermissionError('STATE_UNSAFE')
    row = json.loads(path.read_bytes())
    if row.get('invocationId') != os.environ.get('INVOCATION_ID'):
        raise PermissionError('INSTANCE_CHANGED')
    return row


def save(session, row):
    # Root-only, single unit writer; no terminal bytes or environment values.
    temporary = None
    try:
        fd, temporary = tempfile.mkstemp(prefix=session + '.', suffix='.tmp', dir=STATE)
        with os.fdopen(fd, 'w') as output:
            json.dump(row, output); output.flush(); os.fsync(output.fileno())
        os.replace(temporary, STATE / (session + '.json')); temporary = None
        directory = os.open(STATE, os.O_DIRECTORY | os.O_NOFOLLOW)
        try: os.fsync(directory)
        finally: os.close(directory)
    finally:
        if temporary is not None: os.unlink(temporary)


def process_identity(pid):
    text = Path(f'/proc/{pid}/stat').read_text()
    start = text[text.rfind(')') + 2:].split()[19]
    namespace = Path(f'/proc/{pid}/ns/pid').stat().st_ino
    return start, namespace


def started_exec(exec_id, container_id):
    # Docker sends the HTTP upgrade before runc has necessarily published a
    # running process/PID. A hijacked stream alone is not terminal readiness.
    deadline = time.monotonic() + 3
    while True:
        status = api('GET', '/exec/' + exec_id + '/json')
        if status.get('ContainerID') != container_id: raise ValueError('EXEC_IDENTITY_CHANGED')
        pid = status.get('Pid')
        if status.get('Running') is True and type(pid) is int and pid > 1: return status
        if time.monotonic() >= deadline: raise ValueError('EXEC_START_FAILED')
        time.sleep(.05)


def stop_exec(row):
    """Signal only the saved Exec instance via pidfd, never kill/restart its container."""
    exec_id, container_id = row.get('execId'), row.get('containerId')
    if not exec_id: return
    if not ID.fullmatch(exec_id) or not ID.fullmatch(container_id or ''):
        raise PermissionError('STATE_UNSAFE')
    try: current = api('GET', '/exec/' + exec_id + '/json')
    except DockerFailure as error:
        if error.status == 404: return
        raise
    if current.get('Running') is not True: return
    pid = row.get('execPid', current.get('Pid'))
    if type(pid) is not int or pid <= 1 or current.get('Pid') != pid or current.get('ContainerID') != container_id:
        raise PermissionError('EXEC_IDENTITY_UNAVAILABLE')
    try: descriptor = os.pidfd_open(pid)
    except ProcessLookupError: return
    try:
        start, namespace = process_identity(pid)
        if namespace != row.get('pidNamespace') or (row.get('execStart') is not None and start != row['execStart']):
            raise PermissionError('EXEC_IDENTITY_CHANGED')
        if row.get('execStart') is None:
            # Crash between ExecStart and the process receipt: recover only
            # this startup's explicit non-secret ownership marker in the
            # previously bound container PID namespace, never an arbitrary PID.
            session = row.get('sessionId', '')
            if not re.fullmatch(r'[a-f0-9]{32}', session): raise PermissionError('STATE_UNSAFE')
            with open(f'/proc/{pid}/environ', 'rb') as environment:
                content = environment.read(65537)
            marker = ('WHALEDECK_TERMINAL_SESSION=' + session).encode()
            if len(content) > 65536 or marker not in content.split(b'\0'):
                raise PermissionError('EXEC_IDENTITY_UNAVAILABLE')
        container = api('GET', '/containers/' + container_id + '/json')
        initial = (container.get('State') or {}).get('Pid')
        if type(initial) is not int or initial == pid or initial <= 1 or Path(f'/proc/{initial}/ns/pid').stat().st_ino != row['pidNamespace']:
            raise PermissionError('EXEC_IDENTITY_CHANGED')
        signal.pidfd_send_signal(descriptor, signal.SIGHUP)
        with selectors.DefaultSelector() as exited:
            exited.register(descriptor, selectors.EVENT_READ)
            if not exited.select(.5): signal.pidfd_send_signal(descriptor, signal.SIGKILL)
            if not exited.select(3): raise TimeoutError('EXEC_CLOSE_INCOMPLETE')
    except FileNotFoundError:
        # Only a demonstrably exited pidfd may turn a disappearing /proc
        # identity into successful cleanup; otherwise retain the failure.
        with selectors.DefaultSelector() as exited:
            exited.register(descriptor, selectors.EVENT_READ)
            if not exited.select(0): raise
    except ProcessLookupError: pass
    finally: os.close(descriptor)


def open_frame(base, raw):
    # Reuse strict duplicate/size/type checks without permitting a host open.
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError('FRAME_INVALID')
            result[key] = value
        return result
    if len(raw) > 8192: raise ValueError('FRAME_INVALID')
    value = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(value, dict) or set(value) != {'type', 'columns', 'rows', 'containerId', 'instanceId', 'shell'}:
        raise ValueError('FRAME_INVALID')
    base.frame(json.dumps({key: value[key] for key in ('type', 'columns', 'rows')}).encode())
    if value['type'] != 'open' or not isinstance(value['containerId'], str) or not ID.fullmatch(value['containerId']) or value['shell'] not in SHELLS:
        raise ValueError('FRAME_INVALID')
    if not isinstance(value['instanceId'], str) or not re.fullmatch(r'[1-9][0-9]{0,9}:[0-9TZ:.+-]{1,64}', value['instanceId']):
        raise ValueError('FRAME_INVALID')
    return value


def run(base, session):
    base.invocation()
    row = record(session)
    os.set_blocking(0, False); os.set_blocking(1, False)
    stream = None
    started = last_input = heartbeat = time.monotonic()
    buffer = bytearray(); incoming = outgoing = 0
    reason, exit_code = 'TERMINAL_CLOSED', -1
    stage = 'first-frame'
    with selectors.DefaultSelector() as selector:
        selector.register(0, selectors.EVENT_READ)
        try:
            while True:
                now = time.monotonic()
                if now - started > 600: reason = 'TERMINAL_EXPIRED'; break
                if now - last_input > (120 if stream else 5): reason = 'TERMINAL_IDLE'; break
                if stream and now - heartbeat >= 5:
                    base.emit({'type': 'heartbeat'}); heartbeat = now
                for event, _ in selector.select(.2):
                    if stream and event.fileobj is stream:
                        data = stream.recv(4096)
                        if not data:
                            status = api('GET', '/exec/' + row['execId'] + '/json')
                            deadline = time.monotonic() + 2
                            while status.get('Running') and time.monotonic() < deadline:
                                time.sleep(.05)
                                status = api('GET', '/exec/' + row['execId'] + '/json')
                            if status.get('Running'): raise ValueError('EXEC_STREAM_LOST')
                            exit_code = status.get('ExitCode', -1); return
                        outgoing += len(data)
                        if outgoing > 16 * 1024 * 1024: reason = 'TERMINAL_OUTPUT_LIMIT'; return
                        base.emit({'type': 'output', 'data': base64.b64encode(data).decode()})
                        continue
                    raw = os.read(0, 8193)
                    if not raw: return
                    buffer.extend(raw)
                    while b'\n' in buffer:
                        line, _, tail = buffer.partition(b'\n'); buffer = bytearray(tail)
                        if stream is None:
                            stage = 'open-frame'
                            command = open_frame(base, line)
                            identifier = command['containerId']
                            inspected = api('GET', '/containers/' + identifier + '/json')
                            info = target(inspected, identifier, registry())
                            actual_mounts(inspected)
                            if info['instanceId'] != command['instanceId']: raise ValueError('CONTAINER_INSTANCE_CHANGED')
                            namespace = Path(f'/proc/{inspected["State"]["Pid"]}/ns/pid').stat().st_ino
                            if namespace == Path('/proc/1/ns/pid').stat().st_ino:
                                raise PermissionError('CONTAINER_UNSAFE_ISOLATION')
                            stage = 'exec-create'
                            created = api('POST', '/containers/' + identifier + '/exec', {
                                'AttachStdin': True, 'AttachStdout': True, 'AttachStderr': True, 'Tty': True,
                                'Privileged': False, 'Cmd': [SHELLS[command['shell']], '-i'],
                                'Env': ['TERM=xterm-256color', 'HISTFILE=/dev/null', 'WHALEDECK_TERMINAL_SESSION=' + session],
                                'ConsoleSize': [command['rows'], command['columns']]})
                            exec_id = created.get('Id')
                            if not isinstance(exec_id, str) or not ID.fullmatch(exec_id): raise ValueError('EXEC_ID_INVALID')
                            row.update(execId=exec_id, containerId=identifier, instanceId=info['instanceId'], pidNamespace=namespace)
                            save(session, row)
                            stage = 'exec-upgrade'
                            stream = start_stream(exec_id)
                            stage = 'exec-inspect'
                            status = started_exec(exec_id, identifier)
                            pid = status.get('Pid')
                            stage = 'pid-identity'
                            process_start, namespace = process_identity(pid)
                            current_container = api('GET', '/containers/' + identifier + '/json')
                            current_target = target(current_container, identifier, registry())
                            if pid == current_container['State']['Pid'] or namespace != Path(f'/proc/{current_container["State"]["Pid"]}/ns/pid').stat().st_ino or namespace == Path('/proc/1/ns/pid').stat().st_ino:
                                raise ValueError('EXEC_IDENTITY_CHANGED')
                            row.update(execPid=pid, execStart=process_start, pidNamespace=namespace)
                            save(session, row)
                            # Reinspect startup after launch; a concurrent restart must
                            # not silently redirect the grant into a new instance.
                            if current_target['instanceId'] != info['instanceId']:
                                raise ValueError('CONTAINER_INSTANCE_CHANGED')
                            stream.setblocking(False); selector.register(stream, selectors.EVENT_READ)
                            stage = 'initial-resize'
                            api('POST', '/exec/' + exec_id + '/resize?h=' + str(command['rows']) + '&w=' + str(command['columns']))
                            base.emit({'type': 'ready', 'invocationId': base.invocation()})
                            stage = 'stream'
                            continue
                        command = base.frame(line)
                        if command['type'] == 'close': return
                        if command['type'] == 'resize':
                            api('POST', '/exec/' + row['execId'] + '/resize?h=' + str(command['rows']) + '&w=' + str(command['columns']))
                        elif command['type'] == 'input':
                            incoming += len(command['data']); last_input = time.monotonic()
                            if incoming > 1024 * 1024: reason = 'TERMINAL_INPUT_LIMIT'; return
                            pending = command['data']; deadline = time.monotonic() + 2
                            while pending:
                                try:
                                    count = stream.send(pending)
                                    if count <= 0: raise BrokenPipeError()
                                    pending = pending[count:]
                                except BlockingIOError:
                                    if time.monotonic() > deadline: raise TimeoutError('INPUT_BLOCKED')
                                    time.sleep(.01)
                        else: raise ValueError('FRAME_INVALID')
                    if len(buffer) > 8192: raise ValueError('FRAME_INVALID')
        except Exception as error:
            # Stable diagnostic metadata only, never Docker's response body,
            # exception text, terminal bytes, credentials or environment.
            code = error.args[0] if error.args and isinstance(error.args[0], str) and re.fullmatch(r'[A-Z_]{1,64}', error.args[0]) else None
            row['failure'] = {'stage': stage, 'type': type(error).__name__, 'code': code,
                              'errno': error.errno if isinstance(error, OSError) else None,
                              'httpStatus': error.status if isinstance(error, DockerFailure) else None}
            save(session, row)
            raise
        finally:
            if stream is not None: stream.close()
            stop_exec(row)
            if sys.exception() is None:
                base.emit({'type': 'exit', 'code': reason, 'exitCode': exit_code})


def main():
    if os.geteuid() != 0 or len(sys.argv) != 3 or not re.fullmatch(r'[a-f0-9]{32}', sys.argv[2]):
        raise PermissionError('CONTAINER_TERMINAL_DENIED')
    base = base_driver(); base.invocation()
    action, session = sys.argv[1:]
    if action == 'reserve': registry(); base.reserve(session)
    elif action == 'run': run(base, session)
    elif action == 'finish':
        row = record(session)
        stop_exec(row)
        base.finish(session)
    else: raise ValueError('CONTAINER_TERMINAL_DENIED')


if __name__ == '__main__':
    try: main()
    except Exception:
        # Never emit Docker errors, command output, private configuration or traceback.
        try: os.write(1, b'{"type":"error","code":"TERMINAL_REJECTED"}\n')
        except OSError: pass
        raise SystemExit(2)
