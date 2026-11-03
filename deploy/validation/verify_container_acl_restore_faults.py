#!/usr/bin/env python3
"""Independent post-cleanup observer; does not replay a capture or restoration."""
import json
import os
from pathlib import Path
import sys
import traceback
import verify_container_acl_faults as faults

if __name__=='__main__':
    try:
        assert os.geteuid()==0 and len(sys.argv)==2;os.umask(0o077)
        directory=Path(sys.argv[1]);provenance=json.loads((directory/'validation-source.json').read_bytes())
        assert (provenance['schemaVersion'],provenance['aclRestoreFaultVersion']) in ((7,1),(8,2),(9,3),(10,4),(11,5),(12,6))
        result=faults.verify(directory)
        result.update(independentAclRestorationFaults=True,sourceCaptures=len(result['cases']),
            currentAclPreimages=len(result['cases']),sourceBytesRereadAfterCleanup=False)
        (directory/'restore-independent.json').write_text(json.dumps(result))
        print(json.dumps(dict(aclRestoreFaultIndependent='passed',**result)))
    except BaseException as error:
        print(json.dumps(dict(aclRestoreFaultIndependent='failed',failureType=type(error).__name__,
            locations=[dict(file=Path(f.filename).name,line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])))
        raise SystemExit(1) from None
