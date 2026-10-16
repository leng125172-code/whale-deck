#!/usr/bin/env python3
"""Reject stale self-contained runtimes independently of the NuGet dependency graph."""
import json
from pathlib import Path
import re
import sys

MINIMUM = (10, 0, 13)
FRAMEWORKS = frozenset(('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'))


def checked_version(value):
    if not isinstance(value, str) or not re.fullmatch(r'10\.0\.[0-9]{1,4}', value):
        raise ValueError('HOST_RUNTIME_VERSION_UNSUPPORTED')
    if tuple(map(int, value.split('.'))) < MINIMUM:
        raise ValueError('HOST_RUNTIME_VERSION_UNSAFE')
    return value


def bounded_file(path, limit):
    if path.is_symlink() or not path.is_file() or path.stat().st_size > limit:
        raise ValueError('HOST_RUNTIME_ARTIFACT_INVALID')
    with path.open('rb') as stream:
        content = stream.read(limit + 1)
    if len(content) > limit:
        raise ValueError('HOST_RUNTIME_ARTIFACT_INVALID')
    return content


def validate_component(root, application):
    config = json.loads(bounded_file(root / (application + '.runtimeconfig.json'), 64 * 1024))
    frameworks = config.get('runtimeOptions', {}).get('includedFrameworks', [])
    if not isinstance(frameworks, list) or len(frameworks) != len(FRAMEWORKS):
        raise ValueError('HOST_RUNTIME_FRAMEWORK_SET_INVALID')
    versions = {}
    for framework in frameworks:
        name = framework.get('name')
        if name not in FRAMEWORKS or name in versions:
            raise ValueError('HOST_RUNTIME_FRAMEWORK_SET_INVALID')
        versions[name] = checked_version(framework.get('version'))
    data = bounded_file(root / 'System.Security.Cryptography.Pkcs.dll', 4 * 1024 * 1024)
    products = {value.decode('ascii') for value in re.findall(rb'(10\.0\.[0-9]+)\+[a-f0-9]{40}', data)}
    if not data.startswith(b'MZ') or not products:
        raise ValueError('HOST_PKCS_PRODUCT_VERSION_MISSING')
    for version in products:
        checked_version(version)
    return {'application': application, 'frameworks': versions, 'pkcsVersions': sorted(products)}


def main(arguments):
    installed = len(arguments) == 2 and arguments[0] == '--installed'
    if len(arguments) != (2 if installed else 1):
        raise ValueError('Usage: validate-runtime.py [--installed] <root>')
    root = Path(arguments[-1]).resolve()
    results = [validate_component(root / ('whaledeck-' + name if installed else name), app)
        for name, app in (('agent', 'WhaleDeck.Agent'), ('maintenance', 'WhaleDeck.MaintenanceHost'))]
    print(json.dumps({'hostSelfContainedRuntimes': results}))


if __name__ == '__main__':
    try:
        main(sys.argv[1:])
    except Exception as error:
        # Runtime artifacts are metadata, but never dump malformed file content.
        print('Host runtime validation failed: ' + type(error).__name__, file=sys.stderr)
        raise SystemExit(1) from None
