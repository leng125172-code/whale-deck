#!/usr/bin/env python3
"""Independent read-only original logout execution proof, before/after cleanup."""
import json,os,sys
from pathlib import Path
from verify_recursive_acl_restore_prewrite import verify
if __name__=='__main__':
    try:
        assert os.geteuid()==0 and (len(sys.argv)==2 or len(sys.argv)==3 and sys.argv[2]=='--live')
        os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0';live=sys.argv[2:]==['--live']
        value=verify(sys.argv[1],live,logout=True)
        p=Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as stream:json.dump(value,stream)
        print(json.dumps(dict(actualRestoreInflightLogoutIndependent='passed',**value)))
    except BaseException as error:
        import traceback
        f=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(actualRestoreInflightLogoutIndependent='failed',type=type(error).__name__,file=Path(f.filename).name,line=f.lineno)));raise SystemExit(1) from None
