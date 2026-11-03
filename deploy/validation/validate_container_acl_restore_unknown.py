#!/usr/bin/env python3
"""Opt-in new-capture restore Unknown/explicit-ack/shared-barrier real gate."""
from pathlib import Path
import json
import traceback
import validate_container_acl_unknown as gate

if __name__=='__main__':
    try:gate.main(restore=True)
    except BaseException as error:
        print(json.dumps(dict(aclRestoreUnknown='failed',phase=gate.PHASE,failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])),flush=True)
        raise SystemExit(1) from None
