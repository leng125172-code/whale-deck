#!/usr/bin/env python3
"""Run a complete, fixed browser shard only against the local production preview."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / 'web'
PREVIEW = 'http://127.0.0.1:5175'
MANIFEST = Path(__file__).with_name('frontend-layout-suites.json')


def unique(items):
    value = {}
    for key, item in items:
        if key in value: raise ValueError('Duplicate frontend manifest key')
        value[key] = item
    return value


def suites(path=MANIFEST):
    value = json.loads(path.read_bytes(), object_pairs_hook=unique)
    if type(value) is not dict or set(value) != {'schemaVersion', 'shards', 'suites'} or type(value['schemaVersion']) is not int or value['schemaVersion'] != 1 or type(value['shards']) is not int or value['shards'] != 6:
        raise ValueError('Invalid frontend suite manifest')
    values = value['suites']
    if type(values) is not list or not values or any(type(v) is not str or not re.fullmatch(r'e2e/[a-z0-9][a-z0-9.-]*\.spec\.ts', v) or not (WEB/v).is_file() for v in values) or len(values) != len(set(values)):
        raise ValueError('Invalid, duplicated or missing frontend test file')
    return values


def command(index, listing=False):
    if type(index) is not str or not re.fullmatch('[1-6]', index) or type(listing) is not bool:
        raise ValueError('A single shard index from 1 to 6 is required')
    return ['node', str(WEB/'node_modules/@playwright/test/cli.js'), 'test',
            *(re.escape(v)+'$' for v in suites()), '--shard='+index+'/6', '--forbid-only',
            '--reporter='+('json' if listing else 'line'), *(['--list'] if listing else [])]


def main(arguments):
    if len(arguments) not in (1, 2) or len(arguments) == 2 and arguments[1] != '--list':
        raise ValueError('Usage: run-frontend-layout-shard.py <1..6> [--list]')
    if Path.cwd().resolve() != WEB or os.environ.get('WHALEDECK_PRODUCTION_PREVIEW_URL', PREVIEW) != PREVIEW:
        raise ValueError('Frontend shards require the local web production preview')
    result = subprocess.run(command(arguments[0], len(arguments) == 2), cwd=WEB,
                            env={**os.environ, 'WHALEDECK_PRODUCTION_PREVIEW_URL': PREVIEW}, check=False)
    return result.returncode if result.returncode >= 0 else 128-result.returncode


if __name__ == '__main__':
    try: raise SystemExit(main(sys.argv[1:]))
    except (ValueError, OSError, json.JSONDecodeError):
        print('Frontend shard configuration or execution failed; no automatic retry.', file=sys.stderr)
        raise SystemExit(2) from None
