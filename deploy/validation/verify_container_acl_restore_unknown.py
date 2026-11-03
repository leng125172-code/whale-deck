#!/usr/bin/env python3
"""Independent persisted restore Unknown proof; no replays or source writes."""
from pathlib import Path
import json
import os
import sys
import traceback
from acl_restore_unknown_proof import require_restore_unknown
from verify_container_acl_unknown import verify

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
        directory=Path(sys.argv[1]);require_restore_unknown(json.loads((directory/'baseline.json').read_bytes()))
        result=verify(directory);result.update(independentAclRestorationUnknown=True,sourceBytesRereadAfterCleanup=False)
        (directory/'restore-independent.json').write_text(json.dumps(result))
        print(json.dumps(dict(aclRestoreUnknownIndependent='passed',**result)))
    except BaseException as error:
        print(json.dumps(dict(aclRestoreUnknownIndependent='failed',failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])))
        raise SystemExit(1) from None
