#!/usr/bin/env python3
"""New owned source and execution; normal original-session UI logout only."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(inflight_logout=True)
    except BaseException as error:
        print(json.dumps(dict(actualRecursiveRestoreInflightLogout='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
