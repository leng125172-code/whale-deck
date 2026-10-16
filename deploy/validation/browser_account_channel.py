#!/usr/bin/env python3
"""PRIVATE IPC broker for real browser login; never run with inherited stdout.

Parent must capture the encrypted SSH stdout in memory and MUST NOT log it.
Credentials only originate in run_with_ephemeral_admin's process environment.
EOF or 10 minutes removes the exact wrapper-owned account and verifies 404.
"""
import json
import os
import select
import sys
import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def main():
    if os.environ.get('VALIDATION_BROWSER_PRIVATE_CHANNEL') != '1' or sys.stdout.isatty() or sys.stdin.isatty():
        raise RuntimeError('private-captured-pipes-required')
    username = os.environ['VALIDATION_USERNAME']; identifier = os.environ['VALIDATION_USER_ID']
    if not username.startswith('whaledeck-validation-') or not identifier.isdecimal(): raise RuntimeError('owned-account-required')
    api = requests.Session(); api.trust_env = False
    api.headers['Authorization'] = 'Bearer ' + require(container_environment(), 'Authentication__Authentik__ApiToken')
    endpoint = AUTHENTIK_API + 'core/users/' + identifier + '/'
    response = api.get(endpoint, timeout=15); response.raise_for_status()
    if response.json().get('username') != username: raise RuntimeError('account-binding-mismatch')
    try:
        ordinary = os.environ.get('VALIDATION_BROWSER_ROLE') == 'ordinary'
        if ordinary: api.patch(endpoint, json={'groups': []}, timeout=15).raise_for_status()
        print(json.dumps({'privateBrowserAccount': True, 'username': username, 'password': os.environ['VALIDATION_PASSWORD'], 'ordinary': ordinary}), flush=True)
        if select.select([sys.stdin], [], [], 600)[0]: sys.stdin.readline()
    finally:
        removed = api.delete(endpoint, timeout=15)
        if removed.status_code not in (204, 404) or api.get(endpoint, timeout=15).status_code != 404: raise RuntimeError('owned-account-cleanup-failed')
        print(json.dumps({'browserFixtureRemoved': True}), flush=True)


if __name__ == '__main__':
    try: main()
    except Exception as error:
        # Do not print exception objects, environment, responses or credentials.
        print(json.dumps({'privateBrowserFixtureFailed': True, 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
