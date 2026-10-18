#!/usr/bin/env python3
"""Inspect actual binaries/images without starting app hosts or connecting to dependencies.

Revision/labels are provenance metadata, not a cryptographic supply-chain attestation.
Only fixed platform resources are inspected. No Docker environment/config dump is read.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess
from urllib.request import ProxyHandler, build_opener

REVISION = re.compile(r'^[a-f0-9]{40}$')
MAX_OUTPUT = 16 * 1024
CONTAINERS = {'api': 'WhaleDeck.Api', 'worker': 'WhaleDeck.Worker', 'gateway': 'WhaleDeck.Gateway'}
HOSTS = {'agent': 'WhaleDeck.Agent', 'maintenance': 'WhaleDeck.MaintenanceHost'}


def command(arguments):
    completed = subprocess.run(arguments, capture_output=True, timeout=30, check=False)
    if completed.returncode or len(completed.stdout) > MAX_OUTPUT:
        raise ValueError('BUILD_INSPECTION_FAILED')
    return completed.stdout.decode('utf-8').strip()


def identity(value, component, revision):
    if not isinstance(value, dict) or set(value) != {'component', 'version', 'sourceRevision'}:
        raise ValueError('BUILD_METADATA_INVALID')
    if value['component'] != component or value['version'] != '0.1.0' or value['sourceRevision'] != revision:
        raise ValueError('BUILD_IDENTITY_MISMATCH')
    return value


def container_snapshot(name, run):
    return run(['docker', 'inspect', '--format', '{{.Id}}|{{.Image}}', name])


def verify(revision, run=command, fetch=None):
    if not REVISION.fullmatch(revision):
        raise ValueError('BUILD_REVISION_INVALID')
    result = {}
    for service, component in CONTAINERS.items():
        name = 'whaledeck-' + service
        before = container_snapshot(name, run)
        image = before.split('|')
        if len(image) != 2 or not all(re.fullmatch(r'(?:sha256:)?[a-f0-9]{64}', value) for value in image):
            raise ValueError('BUILD_CONTAINER_ID_INVALID')
        label = run(['docker', 'image', 'inspect', '--format',
                     '{{index .Config.Labels "org.opencontainers.image.revision"}}', image[1]])
        if label != revision:
            raise ValueError('BUILD_IMAGE_REVISION_MISMATCH')
        args = ['cat', '/usr/share/nginx/build-info.json'] if service == 'gateway' else [
            'dotnet', component + '.dll', '--build-info']
        value = identity(json.loads(run(['docker', 'exec', name, *args])), component, revision)
        if container_snapshot(name, run) != before:
            raise ValueError('BUILD_CONTAINER_CHANGED_DURING_INSPECTION')
        result[service] = {**value, 'containerId': image[0], 'imageId': image[1]}
    for service, component in HOSTS.items():
        root = '/opt/whaledeck-agent' if service == 'agent' else '/opt/whaledeck-maintenance'
        path = Path(root) / component
        result[service] = identity(json.loads(run([str(path), '--build-info'])), component, revision)
    # This isolated CLI path exits before configuration/EF. It does not run migrations.
    migrator = json.loads(run(['docker', 'run', '--rm', '--network', 'none', '--read-only',
        '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges:true',
        'whaledeck-migrator', '--build-info']))
    result['migrator'] = identity(migrator, 'WhaleDeck.Migrator', revision)
    if fetch:
        api = fetch('/api/v1/system/status')
        identity(api.get('build'), 'WhaleDeck.Api', revision)
        identity(fetch('/build-info.json'), 'WhaleDeck.Gateway', revision)
        result['http'] = 'actual-api-and-gateway-match'
    return {'sourceRevision': revision, 'components': result, 'verified': True}


def local_http(path):
    opener = build_opener(ProxyHandler({}))
    with opener.open('http://127.0.0.1:18080' + path, timeout=5) as response:
        raw = response.read(MAX_OUTPUT + 1)
        if response.status != 200 or len(raw) > MAX_OUTPUT:
            raise ValueError('BUILD_HTTP_INSPECTION_FAILED')
        return json.loads(raw)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--revision', required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.revision, fetch=local_http), sort_keys=True))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, subprocess.SubprocessError):
        raise SystemExit('BUILD_PROVENANCE_FAILED: running components were not proven to match')
