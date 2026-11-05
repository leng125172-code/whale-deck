#!/usr/bin/env python3
"""New owned target rename after first real restore, then distinct-admin UI ACK."""
import json
import validate_recursive_acl_restore_paging as gate
if __name__=='__main__':
    try:gate.main(unknown_rename=True,job_results=True)
    except BaseException as error:
        print(json.dumps(dict(actualRestoreUnknownRename='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
