#!/usr/bin/env python3
"""Provision one short-lived browser acceptance user over a private SSH stdin pipe.

The caller generates credentials in memory and writes one JSON line to stdin.
Only readiness / cleanup metadata is returned. EOF, close, timeout or signal
removes exactly this user's record. Never print credentials or HTTP exceptions.
"""
import json
import re
import select
import signal
import sys

import requests
from run_with_ephemeral_admin import AUTHENTIK_API, container_environment, require


def interrupted(_number, _frame):
    raise KeyboardInterrupt()


def main():
    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, interrupted)
    payload = json.loads(sys.stdin.readline(2049))
    assert set(payload) in ({'username', 'password'}, {'username', 'password', 'administrator'})
    username, password = payload['username'], payload['password']
    administrator = payload.get('administrator', True)
    assert isinstance(administrator, bool)
    assert isinstance(username, str) and re.fullmatch(r'wd-browser-[a-f0-9]{32}', username)
    assert isinstance(password, str) and re.fullmatch(r'[A-Za-z0-9_!.-]{40,96}', password)
    values = container_environment()
    session = requests.Session()
    session.trust_env = False
    session.headers.update({'Authorization': 'Bearer ' + require(values, 'Authentication__Authentik__ApiToken')})
    user_id = None
    try:
        created = session.post(AUTHENTIK_API + 'core/users/', json={
            'username': username, 'name': 'Whale Deck browser acceptance', 'email': '',
            'is_active': True, 'groups': [require(values, 'Authentication__Authentik__AdministratorGroupId')] if administrator else [],
        }, timeout=15)
        created.raise_for_status()
        result = created.json()
        assert result['username'] == username
        user_id = str(result['pk'])
        assert re.fullmatch(r'[0-9]{1,12}', user_id)
        changed = session.post(AUTHENTIK_API + f'core/users/{user_id}/set_password/',
            json={'password': password}, timeout=15)
        changed.raise_for_status()
        password = None
        payload.clear()
        print(json.dumps({'ready': True, 'lifetimeSeconds': 600}), flush=True)
        if select.select([sys.stdin], [], [], 600)[0]:
            sys.stdin.readline(64)
    finally:
        if user_id is not None:
            deleted = session.delete(AUTHENTIK_API + f'core/users/{user_id}/', timeout=15)
            assert deleted.status_code in (204, 404)
            print(json.dumps({'temporaryBrowserUserRemoved': True}), flush=True)
        session.close()


if __name__ == '__main__':
    try:
        main()
    except BaseException as error:
        print(json.dumps({'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
