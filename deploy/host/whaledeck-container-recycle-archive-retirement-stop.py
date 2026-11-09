#!/usr/bin/env python3
"""Cancel only a pinned native driver process, never stop a reusable unit name.

The unit, cgroup, root process and initial INVOCATION_ID are bound before any
terminating signal. No request/body/key input or environment contents are logged. Main PID
exit lets the fixed KillMode=control-group/Restart=no service finish cleanup.
"""
import os
import re
import select
import signal
import stat
import subprocess
import sys

PREFIX='whaledeck-container-recycle-archive-retirements-'


class Rejected(Exception):pass


def identity(value):
    if type(value) is not str or not re.fullmatch('[a-f0-9]{32}',value) or value=='0'*32:raise Rejected('RETIREMENT_STOP_ID_INVALID')


def lookup(driver):
    unit=PREFIX+driver+'.service'
    result=subprocess.run(['/usr/bin/systemctl','show',unit,'--property=InvocationID','--property=MainPID','--property=ControlGroup','--no-pager'],capture_output=True,timeout=2)
    if result.returncode or len(result.stdout)>4096:raise Rejected('RETIREMENT_STOP_LOOKUP_FAILED')
    value={}
    for line in result.stdout.decode('utf-8').splitlines():
        key,separator,text=line.partition('=')
        if not separator or key in value:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
        value[key]=text
    if set(value)!={'InvocationID','MainPID','ControlGroup'} or not re.fullmatch('0|[1-9][0-9]{0,9}',value['MainPID']):raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
    pid=int(value['MainPID'])
    if pid>2**31-1:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
    if value['InvocationID']:identity(value['InvocationID'])
    if pid and not value['InvocationID']:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
    if pid and (pid<=1 or value['ControlGroup']!='/system.slice/'+unit):raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
    return value['InvocationID'],pid,value['ControlGroup']


class PinnedProcess:
    def __init__(self,pid):
        self.pid=pid;self.proc=self.pidfd=None
        try:
            self.proc=os.open('/proc/'+str(pid),os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC)
            i=os.fstat(self.proc)
            if (i.st_uid,i.st_gid)!=(0,0):raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
            self.pidfd=os.pidfd_open(pid)
            self.start=self.read('stat',4096).rsplit(b')',1)[1].split()[19]
        except BaseException:self.close();raise

    def read(self,name,limit):
        fd=os.open(name,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK|os.O_CLOEXEC,dir_fd=self.proc)
        raw=bytearray()
        try:
            while len(raw)<=limit:
                part=os.read(fd,min(4096,limit+1-len(raw)))
                if not part:break
                raw.extend(part)
            if len(raw)>limit:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
            return raw
        except BaseException:
            if name=='environ':raw[:]=b'\0'*len(raw)
            raise
        finally:os.close(fd)

    def verify(self,invocation,cgroup):
        signal.pidfd_send_signal(self.pidfd,0)
        if self.read('stat',4096).rsplit(b')',1)[1].split()[19]!=self.start:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
        if not os.path.samestat(os.fstat(self.proc),os.stat('/proc/'+str(self.pid),follow_symlinks=False)):raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
        if b'0::'+cgroup.encode()+b'\n' not in self.read('cgroup',65536).splitlines(keepends=True):raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
        raw=self.read('environ',65536)
        try:
            marker=b'INVOCATION_ID=';positions=[];cursor=0
            while True:
                at=raw.find(marker,cursor)
                if at<0:break
                if at==0 or raw[at-1]==0:positions.append(at)
                cursor=at+len(marker)
            token=marker+invocation.encode()+b'\0'
            if len(positions)!=1 or raw[positions[0]:positions[0]+len(token)]!=token:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
        finally:raw[:]=b'\0'*len(raw)

    def exited(self,seconds=0):return bool(select.select([self.pidfd],[],[],seconds)[0])

    def send(self,number):signal.pidfd_send_signal(self.pidfd,number)

    def close(self):
        for name in ('pidfd','proc'):
            fd=getattr(self,name,None)
            if fd is not None:os.close(fd);setattr(self,name,None)


def terminate(driver,invocation):
    identity(driver);identity(invocation);initial=lookup(driver)
    if not initial[0]:return
    if initial[0]!=invocation:raise Rejected('RETIREMENT_STOP_BINDING_INVALID')
    if not initial[1]:return
    process=None
    try:
        try:process=PinnedProcess(initial[1])
        except (FileNotFoundError,ProcessLookupError):return
        process.verify(invocation,initial[2])
        if lookup(driver)!=initial:raise Rejected('RETIREMENT_STOP_BINDING_CHANGED')
        process.verify(invocation,initial[2])
        process.send(signal.SIGTERM)
        if not process.exited(1):
            process.send(signal.SIGKILL)
            if not process.exited(2):raise Rejected('RETIREMENT_STOP_TIMEOUT')
    except (FileNotFoundError,ProcessLookupError):
        # Missing metadata/dependencies are not evidence of process exit.
        if process is None or process.exited():return
        raise Rejected('RETIREMENT_STOP_BINDING_INVALID') from None
    finally:
        if process is not None:process.close()


if __name__=='__main__':
    try:
        if os.geteuid()!=0 or len(sys.argv)!=3:raise Rejected('RETIREMENT_STOP_ARGUMENT_INVALID')
        terminate(sys.argv[1],sys.argv[2])
    except BaseException:
        # Fixed status only; no process environment, stderr, paths or key data.
        raise SystemExit(2) from None
