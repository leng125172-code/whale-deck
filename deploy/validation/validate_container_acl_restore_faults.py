#!/usr/bin/env python3
"""Opt-in restore cancellation, authority and restart gates with NEW own captures."""
import json
from pathlib import Path
import traceback
import validate_container_acl_faults as faults

if __name__=='__main__':
    try:faults.main(restore=True)
    except BaseException as error:
        print(json.dumps(dict(aclRestoreFaults='failed',phase=faults.PHASE,failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])),flush=True)
        raise SystemExit(1) from None
