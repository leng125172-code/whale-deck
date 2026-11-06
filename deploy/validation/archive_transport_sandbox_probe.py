"""Frozen QA fixture only: verify actual systemd RO mounts and CAP_CHOWN.

The parent creates a private /run fixture, then applies the candidate profile
with only its three writable paths translated into this disposable tree.
"""
import errno
import json
import os
from pathlib import Path
import re
import stat


def main():
    assert os.geteuid()==0;os.umask(0o077);root=Path(__file__).resolve().parent
    assert root.parent==Path('/run') and re.fullmatch('whaledeck-qa-archive-sandbox-[a-f0-9]{32}',root.name)
    assert root.stat().st_uid==0 and stat.S_IMODE(root.stat().st_mode)==0o700
    original=b'archive-sandbox-readonly\n';refused=[]
    for name in ('source','purge','hdd'):
        path=root/name/'original.bin';assert path.read_bytes()==original
        try:fd=os.open(path,os.O_WRONLY|os.O_TRUNC)
        except OSError as error:assert error.errno==errno.EROFS;refused.append(name)
        else:os.close(fd);raise AssertionError('READONLY_NAMESPACE_BECAME_WRITABLE')
        assert path.read_bytes()==original
    target=root/'apps'/'copy.bin';fd=os.open(target,os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
    try:
        assert os.write(fd,original)==len(original);os.fchown(fd,1000,1000);os.fchmod(fd,0o640);os.setxattr(fd,'user.archive',b'profile proof');os.fsync(fd)
    finally:os.close(fd)
    assert target.read_bytes()==original and (target.stat().st_uid,target.stat().st_gid,stat.S_IMODE(target.stat().st_mode))==(1000,1000,0o640) and os.getxattr(target,'user.archive')==b'profile proof'
    for folder in ('state','claims'):
        with (root/folder/'receipt.json').open('x') as out:out.write('{"qa":true}\n')
    fields=dict(line.split(':',1) for line in Path('/proc/self/status').read_text().splitlines() if ':' in line);caps=int(fields['CapBnd'].strip(),16)
    assert caps&1 and not caps&(1<<21)
    print(json.dumps(dict(archiveSandbox='passed',readonlyRefused=refused,chownAndAttributes=True,ownJournalAndClaimWritable=True,sysAdminAbsent=True)),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:print(json.dumps(dict(archiveSandbox='failed',errorType=type(error).__name__)),flush=True);raise SystemExit(1) from None
