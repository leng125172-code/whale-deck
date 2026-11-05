#!/usr/bin/env python3
"""Fresh source and original Partial cancellation, then second-admin UI ACK."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(partial_cancel=True,job_results=True)
    except BaseException as error:
        print(json.dumps(dict(actualRestorePartialCancel='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
