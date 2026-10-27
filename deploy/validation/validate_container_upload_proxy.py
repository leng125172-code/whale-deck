#!/usr/bin/env python3
"""Exercise the candidate nginx parser and exact upload route in an owned container.

Uses the installed gateway image ID without building/pulling or changing it.
No network, host ports, production volumes, credentials or Docker socket mount.
The route marker and local dummy upstream are QA-only; no SSO/Agent claim.
"""
import json
from pathlib import Path
import re
import subprocess
import tempfile
import time
import uuid


def command(args, data=None):
    return subprocess.run(args, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          timeout=20, check=True).stdout


def main():
    source = Path(__file__).resolve().parents[1] / 'gateway/nginx.conf'
    original = source.read_text()
    image = command(['docker', 'inspect', 'whaledeck-gateway', '--format', '{{.Image}}']).decode().strip()
    if not re.fullmatch(r'sha256:[a-f0-9]{64}', image): raise RuntimeError('PROXY_QA_IMAGE_INVALID')
    name = 'wdv-container-upload-proxy-' + uuid.uuid4().hex
    common = ['--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
              '--tmpfs', '/tmp:rw,noexec,nosuid,size=16m']
    with tempfile.TemporaryDirectory(prefix='wdv-container-proxy-') as temporary:
        config = Path(temporary) / 'nginx.conf'; config.write_text(original); config.chmod(0o444)
        mount = ['--mount', f'type=bind,src={config},dst=/etc/nginx/qa.conf,readonly']
        command(['docker', 'run', '--rm', *common, *mount, '--entrypoint', 'nginx', image,
                 '-t', '-c', '/etc/nginx/qa.conf'])
        marker = '    # Only canonical raw upload content'
        start = original.index(marker); end = original.index('    # Only exact ordinary-container download', start)
        location = original[start:end]
        assert location.count('      limit_except PUT { deny all; }') == 1
        marked = location.replace('      limit_except PUT', '      add_header X-QA-Container-Upload "true" always;\n      limit_except PUT', 1)
        config.chmod(0o644)
        config.write_text(original.replace(location, marked, 1).replace('worker_processes auto;', 'worker_processes 1;')
            .replace('http://api:8080', 'http://127.0.0.1:18081')
            .replace('  server {\n    listen 8081;', '  server { listen 18081; location / { return 204; } }\n\n  server {\n    listen 8081;', 1))
        started = False
        try:
            command(['docker', 'run', '--detach', '--name', name, *common, *mount, '--entrypoint', 'nginx', image,
                     '-c', '/etc/nginx/qa.conf', '-g', 'daemon off;']); started = True

            def request(method, path, length=0, body=b''):
                wire = (f'{method} {path} HTTP/1.1\r\nHost: proxy-qa.invalid\r\nConnection: close\r\n'
                        f'Content-Type: application/octet-stream\r\nContent-Length: {length}\r\n\r\n').encode() + body
                raw = command(['docker', 'exec', '-i', name, 'nc', '-w', '2', '127.0.0.1', '8080'], wire)
                headers = raw.split(b'\r\n\r\n', 1)[0]
                status = re.match(rb'HTTP/1\.[01] ([0-9]{3}) ', headers)
                if status is None: raise RuntimeError('PROXY_QA_RESPONSE_INVALID')
                return int(status[1]), b'x-qa-container-upload: true' in headers.lower()

            # Wait only on this newly owned live nginx, never restart on an observation timeout.
            deadline = time.monotonic() + 10
            while True:
                try: request('GET', '/api/qa-ready'); break
                except (subprocess.CalledProcessError, RuntimeError):
                    if time.monotonic() >= deadline: raise
                    assert command(['docker', 'inspect', name, '--format', '{{.State.Running}}']).strip() == b'true'
                    time.sleep(0.1)
            cid = 'a' * 64; intent = 'aa0a8865-343e-49bd-b01a-72680131bf39'
            path = f'/api/v1/containers/{cid}/files/uploads/{intent}/content'
            assert request('PUT', path) == (204, True)
            assert request('GET', path) == (403, True)
            assert request('POST', path) == (403, True)
            assert request('PUT', path, 1024**3 + 1) == (413, True)
            negative = [path.replace(cid, 'A' * 64), path.replace(cid, 'ordinary'),
                        path.replace(intent, intent.upper()), path.replace(intent, intent.replace('-', '')),
                        path + '/extra', path.replace('/content', '/cancel'),
                        f'/api/v1/containers/{cid}/files/uploads/plan', '/api/v1/metrics']
            for other in negative:
                assert request('PUT', other) == (204, False)
                # These unrelated routes still have nginx's bounded 1 MiB default.
                assert request('PUT', other, 1024**2 + 1) == (413, False)
            status, marked_route = request('PUT', path.replace(cid, 'a' * 63 + '%0A'))
            assert status in (204, 400) and not marked_route
            print(json.dumps({'image': image, 'nativeNginxSyntax': 'passed', 'canonicalPut': 'passed',
                              'getPostDenied': 'passed', 'uploadLimitGiB': 1, 'unrelatedLimitMiB': 1,
                              'negativeRoutes': len(negative) + 1, 'productionChanged': False}))
        finally:
            if started: command(['docker', 'rm', '--force', name])
            inspected = subprocess.run(['docker', 'inspect', name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
            if inspected.returncode == 0: raise RuntimeError('PROXY_QA_CLEANUP_FAILED')


if __name__ == '__main__': main()
