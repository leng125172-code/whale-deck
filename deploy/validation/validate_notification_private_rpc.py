"""Real UDS read/rejection acceptance. Private capability uses hidden curl stdin.

Only random UUID request bytes and non-secret headers use a fresh scratch dir.
Never creates a recipient and verifies the production registry is unchanged.
"""
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import tempfile
import time
import uuid

SERVICE = '/whaledeck.agent.v1.NotificationConfigurationService/'


def integer(number):
    encoded = bytearray()
    while number > 127:
        encoded.append(number & 127 | 128)
        number >>= 7
    encoded.append(number)
    return bytes(encoded)


def field(number, value):
    value = value.encode() if isinstance(value, str) else value
    return integer(number * 8 + 2) + integer(len(value)) + value


def main():
    assert os.geteuid() == 0, 'Root required for fixed capability file'
    registry = Path('/etc/whaledeck/notifications/registry.json')
    def fingerprint():
        if not registry.exists():
            return None
        assert not registry.is_symlink() and registry.stat().st_size <= 65536
        return hashlib.sha256(registry.read_bytes()).digest()
    before = fingerprint()
    key = Path('/etc/whaledeck/agent-capability.key').read_bytes()
    channel = str(uuid.uuid4())
    def token(method):
        prefix = str(int(time.time()) + 45) + '.' + secrets.token_hex(16)
        signature = hmac.new(key, (prefix + '.' + SERVICE + method).encode(), hashlib.sha256).hexdigest().upper()
        return prefix + '.' + signature
    with tempfile.TemporaryDirectory(prefix='whaledeck-private-rpc-') as scratch:
        payload = Path(scratch) / 'body'
        headers = Path(scratch) / 'headers'
        def call(method, body, capability=None):
            payload.write_bytes(b'\0' + len(body).to_bytes(4, 'big') + body)
            config = '\n'.join(['http2-prior-knowledge', 'silent', 'show-error', 'max-time = 15',
                'unix-socket = "/run/whaledeck/agent.sock"', 'url = "http://localhost' + SERVICE + method + '"',
                'header = "Content-Type: application/grpc"', 'header = "TE: trailers"',
                'data-binary = "@' + str(payload) + '"', 'dump-header = "' + str(headers) + '"',
                *(['header = "x-whaledeck-capability: ' + capability + '"'] if capability else [])])
            response = subprocess.run(['curl', '--config', '-'], input=config.encode(), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
            assert response.returncode == 0, 'UDS transport failed'
            statuses = re.findall(r'grpc-status:\s*(\d+)', headers.read_text(), re.I)
            assert statuses, 'No gRPC status'
            return int(statuses[-1]), response.stdout
        body = field(1, channel)
        assert call('Inspect', body)[0] == 7, 'Missing capability was accepted'
        assert call('Inspect', body, token('Apply'))[0] == 7, 'Wrong-method capability was accepted'
        capability = token('Inspect')
        status, response = call('Inspect', body, capability)
        assert status == 0 and ('wd-' + uuid.UUID(channel).hex).encode() in response, 'Inspect contract failed'
        assert call('Inspect', body, capability)[0] == 7, 'Capability replay was accepted'
        invalid = field(1, channel) + field(2, str(uuid.uuid4())) + field(3, 'EMPTY') + field(4, 'remove') + field(5, '{}')
        assert call('Apply', invalid, token('Apply'))[0] == 3, 'Missing request actor was accepted'
        private = json.dumps({'target': {'provider': 'Webhook', 'url': 'https://127.0.0.1/events', 'allowedAddresses': ['127.0.0.1'], 'allowPrivateNetwork': True}, 'credentialMode': 'remove'})
        assert len(response) >= 7 and response[0] == 0 and response[5] == 10
        length = response[6]
        assert length in (5, 64)
        revision = response[7:7 + length].decode('ascii')
        denied = field(1, channel) + field(2, str(uuid.uuid4())) + field(3, revision) + field(4, 'apply') + field(5, private) + field(6, field(2, 'system:private-rpc-validation'))
        assert call('Apply', denied, token('Apply'))[0] == 3, 'Unsafe target was accepted by fixed root helper'
    assert before == fingerprint(), 'Production private registry changed'
    print(json.dumps({'notificationPrivateRpc': 'passed', 'capabilityAndMethodBoundReplay': 'passed', 'rootUnsafeDestinationRejected': True, 'productionRegistryUnchanged': True}))


if __name__ == '__main__':
    main()
