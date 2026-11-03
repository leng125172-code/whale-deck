#!/usr/bin/env python3
"""Collect only: prove shard test IDs form the full original suite, without a browser."""
import importlib.util
import hashlib
import re
import json
import os
from pathlib import Path
import subprocess

PATH = Path(__file__).with_name('run-frontend-layout-shard.py')
SPEC = importlib.util.spec_from_file_location('frontend_layout_shard', PATH)
SHARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SHARD)


def test_ids(report):
    values = []
    def walk(node):
        for spec in node.get('specs', []):
            for test in spec['tests']: values.append((spec['id'], test['projectId']))
        for suite in node.get('suites', []): walk(suite)
    walk(report)
    if not values or len(values) != len(set(values)):
        raise ValueError('Empty or duplicated frontend test collection')
    return set(values)


def prove(full, shards):
    if len(shards) != 6 or not full or any(not v for v in shards):
        raise ValueError('Six nonempty frontend shards are required')
    union = set()
    for shard in shards:
        if union & shard: raise ValueError('Frontend shards overlap')
        union |= shard
    if union != full: raise ValueError('Frontend shards omit or add tests')


def main():
    environment = {**os.environ, 'WHALEDECK_PRODUCTION_PREVIEW_URL': SHARD.PREVIEW}
    def collect(arguments):
        raw = subprocess.check_output(arguments, cwd=SHARD.WEB, env=environment, timeout=120)
        return test_ids(json.loads(raw))
    base = SHARD.command('1', True)
    original = [value for value in base if not value.startswith('--shard=')]
    full = collect(original)
    baseline_path = PATH.with_name('frontend-layout-baseline.json')
    baseline_raw = baseline_path.read_bytes()
    if hashlib.sha256(baseline_raw).hexdigest() != 'ac12a305920037fa6a18a12a27afce684c2706affc1e5c7280abeb06389db407':
        raise ValueError('Original frontend command baseline changed')
    legacy_files = [name for command in json.loads(baseline_raw)['commands'] for name in re.findall(r'e2e/[a-z0-9.-]+\.spec\.ts', command)]
    legacy = collect(['node', str(SHARD.WEB/'node_modules/@playwright/test/cli.js'), 'test', *legacy_files,
                      '--forbid-only', '--reporter=json', '--list'])
    if not legacy <= full: raise ValueError('Original command collection lost tests')
    parts = [collect(SHARD.command(str(index), True)) for index in range(1, 7)]
    prove(full, parts)
    print(json.dumps({'originalTests': len(legacy), 'collectedTests': len(full), 'addedTests': len(full-legacy), 'shardTests': [len(v) for v in parts],
                      'duplicates': 0, 'missing': 0, 'browsersStarted': False}))


if __name__ == '__main__': main()
