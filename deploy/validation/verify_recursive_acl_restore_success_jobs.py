#!/usr/bin/env python3
"""Separate read-only original successful restore Job/SQL/file-HDD proof."""
import json,os,sys
from pathlib import Path
from verify_container_recursive_acl_restore_browser import live_verify,verify
if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live')
        os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live']
        value=(live_verify if live else verify)(sys.argv[1],job_results=True)
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('restore-live-independent.json' if live else 'restore-independent.json')
        with p.open('x') as stream:json.dump(value,stream)
        print(json.dumps(dict(actualRestoreSuccessJobsIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(actualRestoreSuccessJobsIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
