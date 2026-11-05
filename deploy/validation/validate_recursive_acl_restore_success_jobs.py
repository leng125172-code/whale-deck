#!/usr/bin/env python3
"""Full fresh fixture only; four successful restores and one pre-confirmation cancel."""
import json,subprocess,sys
from pathlib import Path
import validate_container_recursive_acl_browser as gate
if __name__=='__main__':
    try:
        name=gate.main(restore=True,restore_job_results=True)
        subprocess.run([sys.executable,str(Path(__file__).with_name('verify_recursive_acl_restore_success_jobs.py')),name],check=True,timeout=120)
    except BaseException as error:
        print(json.dumps(dict(actualRestoreSuccessJobs='failed',phase=gate.PHASE,type=type(error).__name__)),flush=True)
        raise SystemExit(1) from None
