#!/usr/bin/env python3
"""Explicitly authorized development-only grant; not installed or called by Whale Deck."""
import json
import re
import subprocess
import sys


CAPSULE = r'''
import os,stat,sys
from pathlib import Path
target=Path('/sudoers/90-whaledeck-development-user')
content=b'user ALL=(ALL:ALL) NOPASSWD: ALL\n'
assert os.geteuid()==0 and not target.is_symlink()
if sys.argv[1]=='enable':
    created=not target.exists()
    if target.exists():
        assert target.is_file() and target.read_bytes()==content
        assert stat.S_IMODE(target.stat().st_mode)==0o440 and target.stat().st_uid==0
    else:
        pending=target.with_name('.whaledeck-development-user.candidate')
        fd=os.open(pending,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o440)
        try:
            with os.fdopen(fd,'wb') as stream:
                stream.write(content); stream.flush(); os.fsync(stream.fileno())
            os.chown(pending,0,0)
            os.replace(pending,target)
        finally:
            pending.unlink(missing_ok=True)
    print(str(created).lower())
elif sys.argv[1]=='undo-new-grant':
    assert target.is_file() and target.read_bytes()==content and target.stat().st_uid==0
    target.unlink()
else:
    raise RuntimeError('Invalid fixed action')
'''


def main():
    if sys.argv[1:] != ['--enable-user-nopasswd-all']:
        raise SystemExit('Requires explicit --enable-user-nopasswd-all authorization; not a production installer')
    if subprocess.check_output(['id','-un'],text=True).strip()!='user':
        raise RuntimeError('This development grant is exclusively for workstation user')
    image=subprocess.check_output(['docker','inspect','--format','{{.Image}}',
                                   'database-platform-authentik-server'],text=True).strip()
    if not re.fullmatch(r'sha256:[a-f0-9]{64}',image):
        raise RuntimeError('Immutable local validation runtime not found')
    def capsule(action):
        return subprocess.run(['docker','run','--rm','-i','--network','none','--read-only','--user','0:0',
            '--cap-drop','ALL','--security-opt','no-new-privileges','--memory','128m','--cpus','0.5',
            '--pids-limit','32','--log-driver','local','--log-opt','max-size=1m','--log-opt','max-file=2',
            '--mount','type=bind,source=/etc/sudoers.d,target=/sudoers','--entrypoint','python3',
            image,'-I','-',action],input=CAPSULE,text=True,check=True,timeout=30,
            stdout=subprocess.PIPE,stderr=subprocess.PIPE).stdout.strip()
    created=capsule('enable')=='true'
    try:
        subprocess.run(['sudo','-n','/usr/sbin/visudo','-cf','/etc/sudoers.d/90-whaledeck-development-user'],
                       check=True,timeout=15)
        subprocess.run(['sudo','-n','/usr/sbin/visudo','-c'],check=True,timeout=15,
                       stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        subprocess.run(['sudo','-n','true'],check=True,timeout=10)
    except Exception:
        if created:
            capsule('undo-new-grant')
        raise
    print(json.dumps({'developmentSudo':'user-NOPASSWD-ALL-enabled',
                      'userResponsibleForRevocation':True,'agentSudoPolicy':'unchanged'}))


if __name__=='__main__':
    main()
