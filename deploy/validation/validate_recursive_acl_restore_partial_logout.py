#!/usr/bin/env python3
"""Fresh capture only: original-session normal logout while partially restoring."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(partial_cancel=True,partial_logout=True,job_results=True)
    except BaseException as error:
        print(json.dumps(dict(actualRestorePartialLogout='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
