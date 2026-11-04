#!/usr/bin/env python3
"""Opt-in new owned capture, real confirmed restore, bound prewrite cancellation."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(prewrite_cancel=True)
    except BaseException as error:
        print(json.dumps(dict(actualRecursiveRestorePrewrite='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
