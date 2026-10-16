"""Real OIDC/Cookie -> stable proxy -> Gateway -> BFF -> UDS -> restricted PTY.

Only private in-memory cookies/tokens; never prints terminal text or secrets.
No container, host setting or production user is modified. Session metadata and
immutable audit are deliberately retained; every issued test session is closed.
"""
import base64
import hashlib
import json
import os
import re
import socket
import struct
import time
import urllib.parse
import uuid

from validate_oidc import authenticate, required_environment

PHASE = 'startup'


class WireSocket:
    """Small bounded RFC6455 validation client, not production transport."""
    def __init__(self, base, path, cookie, origin=None, protocol='whaledeck.terminal.v1'):
        target = urllib.parse.urlsplit(base)
        assert target.scheme == 'http' and not target.username and not target.password
        self.stream = socket.create_connection((target.hostname, target.port or 80), timeout=15)
        self.stream.settimeout(15)
        self.reader = self.stream.makefile('rb', buffering=0)
        key = base64.b64encode(os.urandom(16)).decode()
        headers = ['GET ' + path + ' HTTP/1.1', 'Host: ' + target.netloc,
                   'Upgrade: websocket', 'Connection: Upgrade', 'Sec-WebSocket-Version: 13',
                   'Sec-WebSocket-Key: ' + key, 'Sec-WebSocket-Protocol: ' + protocol,
                   'Origin: ' + (origin or base), 'Cookie: ' + cookie]
        assert all('\r' not in row and '\n' not in row for row in headers)
        self.stream.sendall(('\r\n'.join(headers) + '\r\n\r\n').encode())
        first = self.reader.readline(4096)
        self.status = int(first.split()[1])
        response = {}
        size = len(first)
        while True:
            line = self.reader.readline(4096); size += len(line)
            assert size <= 32768 and line
            if line == b'\r\n': break
            name, value = line.decode('ascii').split(':', 1)
            response[name.lower()] = value.strip()
        if self.status == 101:
            expected = base64.b64encode(hashlib.sha1((key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11').encode()).digest()).decode()
            assert response.get('sec-websocket-accept') == expected
            assert response.get('sec-websocket-protocol') == protocol

    def send(self, value, opcode=1):
        body = json.dumps(value, separators=(',', ':')).encode() if opcode == 1 else value
        assert len(body) <= 8192
        mask = os.urandom(4)
        header = bytes([0x80 | opcode, 0x80 | len(body)]) if len(body) < 126 else bytes([0x80 | opcode, 0x80 | 126]) + struct.pack('!H', len(body))
        self.stream.sendall(header + mask + bytes(byte ^ mask[index % 4] for index, byte in enumerate(body)))

    def exact(self, size):
        output = bytearray()
        while len(output) < size:
            piece = self.reader.read(size - len(output))
            if not piece: raise RuntimeError('TERMINAL_TRANSPORT_EOF')
            output.extend(piece)
        return bytes(output)

    def read(self):
        message = bytearray()
        while True:
            first, second = self.exact(2); length = second & 127
            assert not second & 128 and first & 112 == 0
            if length == 126: length = struct.unpack('!H', self.exact(2))[0]
            if length == 127: length = struct.unpack('!Q', self.exact(8))[0]
            assert length <= 16384 and len(message) + length <= 16384
            body = self.exact(length); opcode = first & 15
            if opcode == 8:
                self.send(body, 8)  # RFC6455 close acknowledgment, not an EOF fallback.
                return None
            if opcode == 9: self.send(body, 10); continue
            if opcode == 10: continue
            assert opcode in (0, 1)
            message.extend(body)
            if first & 128: return json.loads(message)

    def close(self):
        self.reader.close(); self.stream.close()


def main():
    global PHASE
    PHASE = 'authenticate'
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    csrf = session.get(base + '/api/v1/auth/csrf', timeout=15).json()
    headers = {csrf['headerName']: csrf['token'], 'Origin': base}
    endpoint = base + '/api/v1/terminals'
    container_id = os.environ.get('VALIDATION_CONTAINER_ID')
    target = None
    if container_id:
        assert re.fullmatch(r'[a-f0-9]{64}', container_id), 'TERMINAL_CONTAINER_ID_INVALID'
        inspected = session.get(endpoint + '/containers/' + container_id, timeout=15)
        assert inspected.status_code == 200, 'TERMINAL_CONTAINER_INSPECTION_FAILED'
        target = inspected.json()
        assert target['containerId'] == container_id and target['configuredUser'] == '1000:1000', 'TERMINAL_CONTAINER_TARGET_INVALID'
        assert 'env' not in target and 'environment' not in target
    grant_path = '/containers/grants' if target else '/host/grants'
    cookie = '; '.join(item.name + '=' + item.value for item in session.cookies)
    issued = []
    def grant():
        key = str(uuid.uuid4()); body = {'confirmed': True, 'idempotencyKey': key}
        if target: body.update(containerId=container_id, instanceId=target['instanceId'], shell='sh')
        request_headers = {**headers, 'Idempotency-Key': key}
        response = session.post(endpoint + grant_path, json=body, headers=request_headers, timeout=15)
        assert response.status_code == 200, 'TERMINAL_GRANT_FAILED'
        first = response.json(); issued.append(str(uuid.UUID(first['sessionId'])))
        replay = session.post(endpoint + grant_path, json=body, headers=request_headers, timeout=15)
        assert replay.status_code == 200 and replay.json() == first, 'TERMINAL_GRANT_RETRY_CHANGED'
        if target:
            assert first['targetKind'] == 'Container' and first['targetId'] == container_id and first['instanceId'] == target['instanceId'] and first['shell'] == 'sh', 'TERMINAL_CONTAINER_SCOPE_MISMATCH'
            wrong_shell = session.post(endpoint + grant_path, json={**body, 'shell': 'bash'}, headers=request_headers, timeout=15)
            assert wrong_shell.status_code == 409, 'TERMINAL_SCOPE_CONFLICT_NOT_REJECTED'
            wrong_host = session.post(endpoint + '/host/grants', json={'confirmed': True, 'idempotencyKey': key}, headers=request_headers, timeout=15)
            assert wrong_host.status_code == 409, 'TERMINAL_HOST_SCOPE_CONFLICT_NOT_REJECTED'
        return first
    def status(identifier):
        response = session.get(endpoint + '/' + identifier, timeout=15)
        assert response.status_code == 200
        value = response.json()
        assert 'grantHash' not in value and 'connectToken' not in value and 'loginBinding' not in value
        return value
    try:
        PHASE = 'grant-and-idempotency'
        ticket = grant(); identifier = ticket['sessionId']; path = '/api/v1/terminals/' + identifier + '/connect'
        PHASE = 'origin-and-query-rejection'
        wrong = WireSocket(base, path, cookie, origin='http://example.invalid')
        try: assert wrong.status == 403, 'TERMINAL_ORIGIN_NOT_REJECTED'
        finally: wrong.close()
        bad_query = WireSocket(base, path + '?token=not-a-real-token', cookie)
        try: assert bad_query.status == 403, 'TERMINAL_URL_TOKEN_NOT_REJECTED'
        finally: bad_query.close()
        assert status(identifier)['state'] == 'Issued'
        stream = WireSocket(base, path, cookie)
        PHASE = 'ready-resize-output-exit'
        marker = 'wd-bff-terminal-' + uuid.uuid4().hex
        transcript = bytearray()
        try:
            assert stream.status == 101, 'TERMINAL_UPGRADE_FAILED'
            stream.send({'token': ticket['connectToken']})
            assert stream.read()['kind'] == 'ready', 'TERMINAL_READY_MISSING'
            stream.send({'kind': 'resize', 'columns': 120, 'rows': 40})
            command = "printf '%s\\n' '" + marker + "'; printf 'WD_UID=%s\\n' \"$(id -u)\"; stty size; exit 7\n"
            stream.send({'kind': 'input', 'data': base64.b64encode(command.encode()).decode()})
            exited = False
            while True:
                row = stream.read()
                if row is None: break
                if row['kind'] == 'output': transcript.extend(base64.b64decode(row['data']))
                assert len(transcript) <= 65536
                if row['kind'] == 'exit': assert row['exitCode'] == 7; exited = True; break
            uid = re.search(rb'WD_UID=([0-9]+)\r?\n', transcript)
            assert exited and marker.encode() in transcript and b'40 120' in transcript and uid and int(uid[1]) > 0, 'TERMINAL_OUTPUT_ASSERTION'
        finally: stream.close()
        for _ in range(40):
            PHASE = 'durable-close'
            result = status(identifier)
            if result['state'] == 'Closed': break
            time.sleep(.25)
        assert result['state'] == 'Closed' and result['exitCode'] == 7, 'TERMINAL_CLOSE_NOT_DURABLE'
        replay = WireSocket(base, path, cookie)
        PHASE = 'consumed-grant-replay'
        try:
            assert replay.status == 101
            replay.send({'token': ticket['connectToken']})
            assert replay.read() is None, 'TERMINAL_CONSUMED_GRANT_REOPENED'
        finally: replay.close()
        assert status(identifier)['exitCode'] == 7
        # Explicit close is observed by the live-session lease monitor.
        live = grant(); live_id = live['sessionId']
        PHASE = 'explicit-close-protocol-frame'
        active = WireSocket(base, '/api/v1/terminals/' + live_id + '/connect', cookie)
        try:
            assert active.status == 101; active.send({'token': live['connectToken']}); assert active.read()['kind'] == 'ready'
            # The stable proxy previously aborted silent terminal streams at
            # five seconds, before the ten-second revocation monitor ran.
            time.sleep(6)
            closed = session.post(endpoint + '/' + live_id + '/close', headers=headers, timeout=15); assert closed.status_code == 204
            deadline = time.monotonic() + 15
            while active.read() is not None: assert time.monotonic() < deadline
        finally: active.close()
        print(json.dumps({'realBffContainerTerminal' if target else 'realBffHostTerminal': 'passed', 'oidcCookieProxyGatewayUdsPty': 'passed',
                          'originQueryRejectionAndGrantReplay': 'passed', 'resizeExitDurableCloseAndRevocation': 'passed',
                          'productionContainersModified': False, 'terminalTranscriptPrinted': False}))
    finally:
        for identifier in issued:
            response = session.post(endpoint + '/' + identifier + '/close', headers=headers, timeout=15)
            assert response.status_code == 204, 'TERMINAL_TEST_SESSION_CLEANUP_FAILED'
        print(json.dumps({'terminalTestSessionCleanup': 'passed', 'immutableAuditRetained': True}))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        candidate = error.args[0] if error.args else None
        code = candidate if isinstance(candidate, str) and re.fullmatch(r'TERMINAL_[A-Z_]{1,80}', candidate) else None
        print(json.dumps({'hostTerminalAcceptance': 'failed', 'phase': PHASE, 'code': code, 'failureType': type(error).__name__}))
        raise SystemExit(1)
