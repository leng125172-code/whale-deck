#!/usr/bin/env python3
"""One private-stdin identity + owned file/container lease for real browser QA.

Only readiness and exact-target aggregate evidence leave stdout. No Cookie,
password, OIDC URL, token, arbitrary path, command or user-selected file target.
"""
import hashlib
import json
import select
import signal
import sys
import time

from ephemeral_browser_user import interrupted, temporary_identity
from validate_container_file_bff_extended import fixture, SIZE, CANCEL_SIZE


def evidence(identifier, digest):
    from validate_metric_continuity import query
    return query('''SELECT json_build_object(
        'completeDigestValidated',count(*) FILTER(WHERE result='Validated'
            AND detail_json->>'sizeBytes'='2097159' AND detail_json->>'sha256'=:'digest'),
        'cancelIncomplete',count(*) FILTER(WHERE result='Incomplete' AND detail_json->>'pathHash'=:'cancel'),
        'cancelValidated',count(*) FILTER(WHERE result='Validated' AND detail_json->>'pathHash'=:'cancel'))
        FROM audit_events WHERE target_id=:'target' AND action='containers.files.download';''',
        target=identifier, digest=digest, cancel=hashlib.sha256(b'work/cancel.bin').hexdigest().upper())


def hold(owned, stream=sys.stdin, emit=print, clock=time.monotonic, ready=select.select, seconds=500):
    assert seconds == 500
    deadline = clock() + seconds
    try:
        while True:
            remaining = deadline - clock()
            if remaining <= 0: return
            if not ready([stream], [], [], remaining)[0]: return
            line = stream.readline(65)
            if not line or line.strip() == 'close': return
            assert line == 'evidence\n', 'browser-lease-command-invalid'
            result = evidence(owned['identifier'], owned['proofs']['中文.bin'][1])
            emit(json.dumps({'browserFileEvidence': result}), flush=True)
    except KeyboardInterrupt:
        # A signal while holding an unchanged fixture still runs its full
        # post-yield source/core proofs before exact cleanup. Creation errors
        # remain failures and retain any unverified payload.
        return


def main():
    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP): signal.signal(number, interrupted)
    payload = json.loads(sys.stdin.readline(2049))
    with temporary_identity(payload), fixture() as owned:
        print(json.dumps({'ready': True, 'lifetimeSeconds': 500,
            'containerId': owned['identifier'], 'name': owned['folder'].name,
            'bytes': SIZE, 'cancelBytes': CANCEL_SIZE, 'sha256': owned['proofs']['中文.bin'][1]}), flush=True)
        hold(owned)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'containerFileBrowserLease': 'failed', 'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
