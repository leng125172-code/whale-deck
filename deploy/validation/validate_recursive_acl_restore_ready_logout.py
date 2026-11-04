#!/usr/bin/env python3
"""Opt-in NEW capture, real Ready logout/new session isolation and natural expiry."""
import json
import validate_recursive_acl_restore_paging as gate

if __name__=='__main__':
    try:gate.main(ready_logout=True)
    except BaseException as error:
        print(json.dumps(dict(actualRecursiveRestoreReadyLogout='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
