#!/usr/bin/env python3
"""Encrypted temporary browser identity with EOF/signal/timeout cleanup.

Only the caller's RSA public key and approved role are arguments. Password and
API token stay in process memory; stdout contains only an RSA-OAEP envelope and
public readiness/removal metadata. No credentials in files, args or diagnostics.
"""
import base64
import json
import re
import secrets
import select
import signal
import sys

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
import requests

# The ignored validation-artifacts copy imports the reviewed repository helper.
from pathlib import Path
if Path(__file__).parent.name == 'unpacking-browser-validation':
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'validation'))
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def interrupted(_number, _frame):
    raise KeyboardInterrupt()


def main():
    assert len(sys.argv) == 3 and sys.argv[2] in ('admin', 'ordinary') and not sys.stdout.isatty() and not sys.stdin.isatty()
    key = serialization.load_der_public_key(base64.b64decode(sys.argv[1], validate=True))
    assert isinstance(key, rsa.RSAPublicKey) and key.key_size >= 3072
    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, interrupted)
    values = container_environment()
    session = requests.Session()
    session.trust_env = False
    session.headers['Authorization'] = 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')
    username, password = 'wd-browser-' + secrets.token_hex(16), 'Wd9!' + secrets.token_hex(30)
    identifier = None
    try:
        created = session.post(AUTHENTIK_API + 'core/users/', json={'username': username, 'name': 'Whale Deck ephemeral browser QA',
            'email': '', 'is_active': True, 'groups': [require(values, 'Authentication__Authentik__AdministratorGroupId')] if sys.argv[2] == 'admin' else []}, timeout=15)
        created.raise_for_status()
        result = created.json()
        assert result['username'] == username
        identifier = str(result['pk'])
        assert re.fullmatch('[0-9]{1,12}', identifier)
        changed = session.post(AUTHENTIK_API + f'core/users/{identifier}/set_password/', json={'password': password}, timeout=15)
        changed.raise_for_status()
        message = json.dumps({'userId': identifier, 'username': username, 'password': password}).encode()
        encrypted = key.encrypt(message, padding.OAEP(mgf=padding.MGF1(hashes.SHA256()), algorithm=hashes.SHA256(), label=None))
        message, password = None, None
        print(json.dumps({'ready': True, 'envelope': base64.b64encode(encrypted).decode(), 'lifetimeSeconds': 600}), flush=True)
        if select.select([sys.stdin], [], [], 600)[0]:
            sys.stdin.readline(64)
    finally:
        password = None
        if identifier is not None:
            endpoint = AUTHENTIK_API + f'core/users/{identifier}/'
            deleted = session.delete(endpoint, timeout=15)
            assert deleted.status_code in (204, 404) and session.get(endpoint, timeout=15).status_code == 404
            print(json.dumps({'temporaryBrowserUserRemoved': True}), flush=True)
        session.close()


if __name__ == '__main__':
    try:
        main()
    except BaseException as error:
        print(json.dumps({'privateBrowserChannelFailed': True, 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
