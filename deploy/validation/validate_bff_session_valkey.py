#!/usr/bin/env python3
"""Owned, immutable-image Valkey with a private Unix socket; no production keys."""
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import stat
import subprocess
import sys
import time
import uuid

REPOSITORY=Path(__file__).resolve().parents[2]
IMAGE='valkey/valkey:9.1.2-alpine3.24@sha256:48332870af354a799964c0012ae1194a0bf2bf894eb508f945810596dc2d8d11'


def command(args):
    return subprocess.run(args,capture_output=True,check=True,timeout=30).stdout


def verify(value,identifier,name,folder,image,uid,gid):
    assert re.fullmatch('[a-f0-9]{64}',identifier) and value['Id']==identifier and value['Name']=='/'+name and value['Image']==image
    assert value['Config']['Labels'].get('io.whaledeck.validation')==name and value['Config']['User']==str(uid)+':'+str(gid)
    host=value['HostConfig'];assert host['NetworkMode']=='none' and host['ReadonlyRootfs'] is True and not host.get('PortBindings')
    assert host['CapDrop']==['ALL'] and 'no-new-privileges' in host['SecurityOpt'] and not host.get('Binds')
    assert len(host['Mounts'])==1 and host['Mounts'][0]['Type']=='bind' and host['Mounts'][0]['Source']==str(folder) and host['Mounts'][0]['Target']=='/fixture'
    assert len(value['Mounts'])==1 and value['Mounts'][0]['Source']==str(folder) and value['Mounts'][0]['Destination']=='/fixture' and value['Mounts'][0]['RW'] is True


def main():
    assert os.geteuid()==0
    git=['git','--no-optional-locks','-c','safe.directory='+str(REPOSITORY),'-C',str(REPOSITORY)]
    assert not command([*git,'status','--porcelain']).strip(), 'native attestation requires a clean committed source tree'
    revision=command([*git,'rev-parse','HEAD']).decode().strip();assert re.fullmatch('[a-f0-9]{40}',revision)
    user=pwd.getpwnam('user');assert user.pw_uid>0
    inspected_image=json.loads(command(['docker','image','inspect',IMAGE]))[0]
    assert not inspected_image['Config'].get('Volumes'), 'fixture image must not create anonymous data volumes'
    image=inspected_image['Id']
    marker='wd-bff-session-'+uuid.uuid4().hex[:16];folder=Path('/tmp')/marker
    folder.mkdir(mode=0o700);os.chown(folder,user.pw_uid,user.pw_gid)
    identity=folder.stat().st_dev,folder.stat().st_ino
    acl=b'user default reset on nopass ~whaledeck:auth:session:v2:* +@read +@write +@connection +@scripting -@dangerous -config -shutdown -acl +time\n'
    acl_path=folder/'users.acl';acl_path.write_bytes(acl);os.chown(acl_path,user.pw_uid,user.pw_gid);acl_path.chmod(0o600)
    acl_identity=acl_path.stat().st_dev,acl_path.stat().st_ino
    identifier=None;socket_identity=None;complete=False
    source=REPOSITORY/'src/WhaleDeck.Infrastructure/Identity/ValkeyBffSessionStorage.cs'
    source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
    try:
        evidence=REPOSITORY/'deploy/artifacts'/('bff-session-evidence-'+uuid.uuid4().hex)
        evidence.mkdir(mode=0o700)
        # Never let --no-build turn a failed/incomplete compile into apparently
        # current native evidence. Build as the checkout owner, not root; keep
        # MSBuild nodes private to this bounded invocation.
        build=subprocess.run(['/usr/sbin/runuser','-u','user','--','/home/user/.local/bin/dotnet','build',
            'tests/WhaleDeck.IntegrationTests/WhaleDeck.IntegrationTests.csproj','--configuration','Release','--no-restore','-m:2','-nr:false'],
            cwd=REPOSITORY,capture_output=True,timeout=180)
        build_log=evidence/'build.log';build_log.write_bytes(build.stdout+build.stderr);build_log.chmod(0o600)
        if build.returncode:
            print(build.stdout.decode(errors='replace')[-10000:]);raise AssertionError('native fixture requires a successful fresh build')
        identifier=command(['docker','run','--detach','--name',marker,'--network','none','--read-only',
            '--cap-drop','ALL','--security-opt','no-new-privileges','--user',str(user.pw_uid)+':'+str(user.pw_gid),
            '--pids-limit','32','--memory','64m','--cpus','0.5','--log-driver','local','--log-opt','max-size=1m','--log-opt','max-file=2',
            '--label','io.whaledeck.validation='+marker,'--mount','type=bind,src='+str(folder)+',dst=/fixture','--workdir','/fixture',
            '--entrypoint','/usr/local/bin/valkey-server',IMAGE,'--port','0','--unixsocket','/fixture/s.sock','--unixsocketperm','600',
            '--save','','--appendonly','no','--dir','/fixture','--maxmemory','32mb','--maxmemory-policy','noeviction','--loglevel','warning',
            '--aclfile','/fixture/users.acl']).decode().strip()
        verify(json.loads(command(['docker','inspect',identifier]))[0],identifier,marker,folder,image,user.pw_uid,user.pw_gid)
        socket=folder/'s.sock';deadline=time.monotonic()+15
        while not socket.exists() and time.monotonic()<deadline:time.sleep(0.05)
        info=socket.lstat();assert stat.S_ISSOCK(info.st_mode) and info.st_uid==user.pw_uid and stat.S_IMODE(info.st_mode)==0o600
        socket_identity=info.st_dev,info.st_ino
        environment=os.environ.copy();environment['WHALEDECK_NATIVE_BFF_SESSION_ENDPOINT']='unix:'+str(socket)
        result=subprocess.run(['/home/user/.local/bin/dotnet','test','tests/WhaleDeck.IntegrationTests/WhaleDeck.IntegrationTests.csproj',
            '--configuration','Release','--no-build','--filter','FullyQualifiedName~NativeBffSessionTests',
            '--logger','trx','--results-directory',str(evidence)],cwd=REPOSITORY,env=environment,capture_output=True,timeout=120)
        assert len(result.stdout)<=65536 and len(result.stderr)<=65536
        for filename,data in (('stdout.log',result.stdout),('stderr.log',result.stderr)):
            path=evidence/filename;path.write_bytes(data);path.chmod(0o600)
        if result.returncode:
            print(result.stdout.decode(errors='replace')[-10000:]);raise AssertionError('native BFF session gate failed')
        import xml.etree.ElementTree as ET
        reports=list(evidence.glob('*.trx'));assert len(reports)==1
        counters=ET.parse(reports[0]).getroot().find('{*}ResultSummary/{*}Counters').attrib
        assert all(counters[key]=='1' for key in ('total','executed','passed')) and counters['notExecuted']=='0' and counters['failed']=='0'
        assert source_hash==hashlib.sha256(source.read_bytes()).hexdigest()
        assert command([*git,'rev-parse','HEAD']).decode().strip()==revision and not command([*git,'status','--porcelain']).strip()
        complete=True
        print(json.dumps({'actualPrivateValkeyBffSessionGate':'passed','executed':1,'passed':1,'skipped':0,
            'noTcpOrProductionKeys':True,'revision':revision,'freshBuildRequired':True,'sourceSha256':source_hash,'evidence':str(evidence)}),flush=True)
    finally:
        lookup=subprocess.run(['docker','inspect',identifier or marker],capture_output=True,timeout=20)
        if lookup.returncode==0:
            actual=json.loads(lookup.stdout)[0]
            if identifier is not None:assert actual['Id']==identifier
            verify(actual,actual['Id'],marker,folder,image,user.pw_uid,user.pw_gid)
            command(['docker','rm','--force',actual['Id']])
            assert subprocess.run(['docker','inspect',actual['Id']],capture_output=True,timeout=10).returncode!=0
        else:
            assert identifier is None and b'No such object:' in lookup.stderr, 'owned container absence unproven'
        info=folder.lstat();assert folder.resolve(strict=True)==folder and (info.st_dev,info.st_ino)==identity and info.st_uid==user.pw_uid and stat.S_IMODE(info.st_mode)==0o700
        entries={path.name:path for path in folder.iterdir()};assert set(entries) in ({'users.acl'},{'users.acl','s.sock'})
        info=acl_path.lstat();assert stat.S_ISREG(info.st_mode) and (info.st_dev,info.st_ino)==acl_identity and info.st_uid==user.pw_uid and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600 and acl_path.read_bytes()==acl
        if 's.sock' in entries:
            assert socket_identity is not None
            info=entries['s.sock'].lstat();assert stat.S_ISSOCK(info.st_mode) and (info.st_dev,info.st_ino)==socket_identity and info.st_uid==user.pw_uid
            entries['s.sock'].unlink()
        acl_path.unlink()
        folder.rmdir();print(json.dumps({'ownedValkeyAndSocketDirectoryRemoved':True,'testSucceeded':complete}),flush=True)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        import traceback
        print(json.dumps({'actualPrivateValkeyBffSessionGate':'failed','failureType':type(error).__name__,
            'failureLocations':[{'file':Path(frame.filename).name,'line':frame.lineno} for frame in traceback.extract_tb(error.__traceback__)]}),file=sys.stderr)
        raise SystemExit(1) from None
