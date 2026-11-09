#!/usr/bin/env python3
"""Allocate a registered workspace through the fixed, isolated root helper."""
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys


def load(filename):
    path = Path(__file__).with_name(filename)
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1
            or info.st_mode & 0o7022 or not 0 < info.st_size <= 256 * 1024):
        raise PermissionError()
    spec = importlib.util.spec_from_file_location(filename[:-3], path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def allocate(source, files, storage, quota, *, attest=False):
    raw = source.read(1025)
    if not 0 < len(raw) <= 1024:
        raise files.Rejected('WORKSPACE_STORAGE_REQUEST_INVALID')
    try:
        request = json.loads(raw.decode('utf-8'), object_pairs_hook=files.unique_object)
    except (ValueError, RecursionError, files.Rejected):
        raise files.Rejected('WORKSPACE_STORAGE_REQUEST_INVALID') from None
    driver = storage.WorkspaceStorage(files, sandboxed=True, quota=quota)
    if attest:
        with driver.attested_payload(request) as (_, proof):
            return proof
    return driver.allocate(request)


def main():
    try:
        if os.geteuid() != 0 or sys.argv[1:] not in ([], ['attest']):
            raise PermissionError()
        attest = sys.argv[1:] == ['attest']
        files = load('whaledeck-files.py')
        storage = load('whaledeck-development-storage.py')
        quota = load('whaledeck-development-project-quota.py')
        guard = load('whaledeck-development-quota-guard.py')
        guard.install(quota, read_only=attest)
    except (OSError, ImportError):
        print('WORKSPACE_STORAGE_HELPER_UNAVAILABLE', file=sys.stderr)
        return 2
    try:
        print(json.dumps(allocate(sys.stdin.buffer, files, storage, quota, attest=attest), separators=(',', ':')))
        return 0
    except files.Rejected as error:
        code = str(error)
        if not re.fullmatch(r'WORKSPACE_STORAGE_[A-Z_]{1,40}', code):
            code = 'WORKSPACE_STORAGE_STATE_UNSAFE'
    except FileNotFoundError:
        code = 'WORKSPACE_STORAGE_NOT_CONFIGURED'
    except quota.QuotaError as error:
        code = 'WORKSPACE_STORAGE_' + str(error)
        if not re.fullmatch(r'WORKSPACE_STORAGE_QUOTA_[A-Z_]{1,34}', code):
            code = 'WORKSPACE_STORAGE_QUOTA_UNAVAILABLE'
    except (OSError, ValueError, RuntimeError):
        code = 'WORKSPACE_STORAGE_STATE_UNSAFE'
    print(code, file=sys.stderr)
    return 2


if __name__ == '__main__':
    sys.exit(main())
