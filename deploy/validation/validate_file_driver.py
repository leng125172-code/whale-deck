#!/usr/bin/env python3
"""Actual installed/sandboxed fixed file helper, isolated owned upload only.

Never prints payload, paths, owners, private configuration or arbitrary stderr.
Root receipts remain as anti-replay history; only the exact fixture is removed.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import uuid


def main():
    if os.geteuid() != 0:
        raise RuntimeError('root-required')
    operation = uuid.uuid4().hex
    name = 'wdv-driver-' + operation + '.bin'
    target = Path('/data/WhaleDeck/files') / name
    payload = os.urandom(2 * 1024 * 1024 + 7)
    digest = hashlib.sha256(payload).hexdigest().upper()
    request = dict(rootId='whaledeck.files.shared', path=name, operationId=operation,
                   owner=hashlib.sha256(operation.encode()).hexdigest().upper(),
                   sizeBytes=len(payload), sha256=digest, expectedVersion='ABSENT')
    lines = [request]
    for offset in range(0, len(payload), 32768):
        lines.append(dict(type='chunk', offset=offset, data=base64.b64encode(payload[offset:offset + 32768]).decode('ascii')))
    lines.append(dict(type='commit'))
    content = b''.join(json.dumps(item, separators=(',', ':')).encode() + b'\n' for item in lines)
    try:
        call = subprocess.run(['/usr/local/libexec/whaledeck-privileged', 'file-transport', 'upload', uuid.uuid4().hex],
                              input=content, capture_output=True, timeout=70, check=False)
        errors = []
        for line in call.stderr.splitlines():
            try:
                value = json.loads(line).get('errorCode')
                if isinstance(value, str) and re.fullmatch(r'FILES_[A-Z_]{1,64}', value): errors.append(value)
            except (ValueError, AttributeError):
                pass
        rows = [json.loads(row) for row in call.stdout.splitlines()]
        result = rows[-1] if rows else {}
        passed = call.returncode == 0 and result.get('state') == 'Completed' and result.get('sha256') == digest
        print(json.dumps(dict(installedSandboxedFileHelper='passed' if passed else 'failed',
                              exitCode=call.returncode, errorCodes=errors,
                              ready=bool(rows and rows[0].get('type') == 'driver'),
                              receiptState=result.get('state'), binaryBytes=len(payload))))
        if not passed:
            raise RuntimeError('helper-upload-failed')
        if hashlib.sha256(target.read_bytes()).hexdigest().upper() != digest:
            raise RuntimeError('checksum-failed')
    finally:
        if target.exists():
            if target.is_symlink() or hashlib.sha256(target.read_bytes()).hexdigest().upper() != digest:
                raise RuntimeError('owned-cleanup-refused')
            target.unlink()
        print(json.dumps(dict(ownedDriverFileRemoved=not target.exists(), privateReceiptsRetained=True)))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # These are our fixed diagnostics, never exception text or child output.
        print(json.dumps(dict(fileDriverValidation='failed', failureType=type(error).__name__)))
        raise SystemExit(1) from None
