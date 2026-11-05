#!/usr/bin/env python3
"""Fresh source/execution only; Ready and zero-write Canceled Jobs summaries."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(inflight_logout=True,job_results=True)
    except BaseException as error:
        print(json.dumps(dict(actualRestoreJobResults='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
