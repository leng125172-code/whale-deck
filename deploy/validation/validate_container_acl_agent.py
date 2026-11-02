#!/usr/bin/env python3
"""Installed signed ACL UDS gate, exact owned HDD fixture, no human business/UI.

Physical bytes and both ACLs are verified after every RPC/replay before cleanup.
Failure leaves unproved source data, never fabricates or rewrites Root history.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import stat
import subprocess
import sys
import time
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_file_write_agent import trusted
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity
from validate_container_acl import inventory, unchanged_except_target, PATHS, NAMES

PHASE='startup'
STATE=Path('/var/lib/whaledeck-container-acl')


def number(value):
    assert type(value) is int or type(value) is str and re.fullmatch('-?(0|[1-9][0-9]*)',value)
    return int(value)


def managed_entries(values):
    assert type(values) is list and len(values)<=64
    output=[]
    for e in values:
        assert type(e) is dict and set(e) in ({'tag','permissions'},{'tag','permissions','id'})
        assert type(e['tag']) is str and type(e['permissions']) is int and 0<=e['permissions']<=7
        assert 'id' not in e or type(e['id']) is int and 0<=e['id']<4294967295
        output.append({'tag':e['tag'],'id':e.get('id'),'permissions':e['permissions']})
    return output


def managed_plan(value):
    q=json.loads(value) if type(value) is str else value;c=q['command'];p=q['parentIdentity'];s=q['sourceIdentity'];location=c['location']
    return {'schemaVersion':q['schemaVersion'],'command':{'containerId':location['containerId'],'instanceId':location['instanceId'],'path':location['path'],
            'aclType':c['aclType'],'entries':managed_entries(c['entries']),'expectedVersion':c['expectedVersion']},
            'targetProof':q['targetProof'],'parentIdentity':[number(p['device']),number(p['inode'])],'parentVersion':q['parentVersion'],
            'uid':q['ownerUid'],'gid':q['ownerGid'],'fingerprint':q['fingerprint'],'kind':q['kind'],
            'sourceIdentity':[number(s[k]) for k in ('device','inode','mode','ownerUid','ownerGid','linkCount','sizeBytes','modifiedAtUnixNs')]+[number(q['sourceChangedAtUnixNs'])],
            'attributesDigest':q['attributesDigest'],'noChange':q['noChange'],'accessAcl':managed_entries(q['accessAcl']),
            'defaultAcl':managed_entries(q['defaultAcl']),'targetMode':q['targetMode']}


class Probe:
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'rpc.stderr').open('wb');self.transcript=evidence/'rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-acl'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:self.send(header);ready=self.receive();assert ready['kind']=='ready' and ready['revision']==header['revision'] and ready['protocolMinor']>=51
        except BaseException:self.close(False);raise
    def send(self,value):
        data=json.dumps(value,ensure_ascii=False,separators=(',',':')).encode()+b'\n';assert len(data)<=65536
        self.process.stdin.write(data);self.process.stdin.flush()
    def receive(self):
        deadline=time.monotonic()+45
        while b'\n' not in self.buffer:
            assert time.monotonic()<deadline,'PROBE_RESPONSE_DEADLINE'
            ready=select.select([self.process.stdout],[],[],max(0,deadline-time.monotonic()))[0];assert ready,'PROBE_RESPONSE_DEADLINE'
            raw=os.read(self.process.stdout.fileno(),65536);assert raw,'PROBE_EOF';self.buffer+=raw;assert len(self.buffer)<=131072
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=65536;value=json.loads(line)
        with self.transcript.open('ab') as stream:stream.write(line+b'\n')
        return value
    def call(self,value):self.send(value);return self.receive()
    def close(self,success):
        try:
            if success:assert self.call({'action':'quit'})=={'kind':'done'}
            self.process.stdin.close()
            try:self.process.wait(timeout=8)
            except subprocess.TimeoutExpired:self.process.kill();self.process.wait(timeout=5)
            if success:assert self.process.returncode==0 and not self.buffer and not self.process.stdout.read(1)
        finally:self.process.stdout.close();self.error.close()


def archive(evidence,operation,storage):
    path=STATE/(operation+'.json');info=trusted(path);assert stat.S_IMODE(info.st_mode)==0o600 and info.st_size<=16384
    raw=path.read_bytes();record=json.loads(raw);(evidence/(operation+'.root.json')).write_bytes(raw)
    if record['historyVerified']:
        with storage.target(create_namespace=False,read_only=True) as (configuration,base):
            fd=storage.f.open_at(base,'container-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
            try:
                storage.attest(configuration,fd,read_only=True);saved=storage.read_json(fd,operation+'.json');(evidence/(operation+'.hdd.json')).write_text(json.dumps(saved))
            finally:os.close(fd)
        assert saved['plan']==record['plan'] and saved['operationId']==operation and saved['owner']==record['owner']
        assert saved['resourceType']=='ContainerAclMetadata' and saved['storageId']=='database-platform.backup.hdd'
        assert saved['historyDigest']==record['historyDigest']==hashlib.sha256(b'whaledeck-container-acl-history-v1\n'+json.dumps({k:v for k,v in saved.items() if k!='historyDigest'},ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()).hexdigest().upper()
    return record,hashlib.sha256(raw).hexdigest()


def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==3;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision=sys.argv[1];assert re.fullmatch('[a-f0-9]{40}',revision);repo=Path(__file__).resolve().parents[2];binary=Path(sys.argv[2])
    assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe'
    trusted(binary);trusted(binary.parent,True);subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True,timeout=20)
    names=(*NAMES,'whaledeck-container-acl-transport.py','whaledeck-privileged');hashes={}
    for name in names:
        path=Path('/usr/local/libexec')/name;info=trusted(path);assert stat.S_IMODE(info.st_mode)==0o750
        raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert raw==path.read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
    assert stat.S_IMODE(trusted(STATE,True).st_mode)==0o700
    no_active_work();core=owned.common.core_snapshot();hosts=host_identity(revision)
    evidence=repo/'deploy/artifacts'/('container-acl-agent-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700)
    print(json.dumps({'installedAclRpc':'started','evidence':evidence.name}),flush=True)
    reader=load('/usr/local/libexec/whaledeck-container-files.py','acl_installed_reader');files=reader.trusted_module('whaledeck-files.py','acl_installed_files')
    module=load('/usr/local/libexec/whaledeck-container-acl.py','acl_installed_module');storage=load('/usr/local/libexec/whaledeck-file-backups.py','acl_installed_storage').FileBackups(files)
    probe=None;rows=[]
    try:
        with owned.fixture() as fixture:
            folder=fixture['folder'];(folder/'acl-directory').mkdir(mode=0o750)
            content=hashlib.shake_256(('installed-acl:'+fixture['identifier']).encode()).digest(2*1024**2+7)
            for relative,kind in PATHS.items():
                p=folder/relative
                if kind=='File':p.write_bytes(content);os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
                os.chown(p,2100,3100);p.chmod(0o640 if kind=='File' else 0o750)
            for relative in PATHS:os.utime(folder/relative,ns=(1700000000123456789,1700000000123456789))
            actual=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0]
            location={'containerId':fixture['identifier'],'instanceId':str(actual['State']['Pid'])+':'+actual['State']['StartedAt']}
            baseline={'revision':revision,**location,'source':str(folder),'original':inventory(folder,reader,module,content),'sha256':hashlib.sha256(content).hexdigest().upper(),
                      'bytesPerFile':len(content),'coreBefore':core,'hostBefore':hosts,'installedSourceSha256':hashes,'candidateOnly':False}
            (evidence/'baseline.json').write_text(json.dumps(baseline));probe=Probe(binary,evidence,{'revision':revision,**location,'coreId':core['/whaledeck-api'][0]})
            cases=[('access','acl-file.bin','access','named'),('noop','acl-file.bin','access','named'),('mask','acl-file.bin','access','mask'),('root','acl-file.bin','access','root'),
                   ('base','acl-file.bin','access','base'),('default','acl-directory','default','named'),('default-noop','acl-directory','default','named'),
                   ('clear-default','acl-directory','default','clear'),('cancel','acl-file.bin','access','mask')]
            for index,(label,relative,kind,profile) in enumerate(cases):
                PHASE=label;before=inventory(folder,reader,module,content);path='work/'+relative
                response=probe.call({'action':'plan','path':path,'aclType':kind,'profile':profile,'version':reader.version(location,path,(folder/relative).stat()),'boundaries':index==0})
                assert response['kind']=='plan' and response['boundaryRefusals']==(13 if index==0 else 0);plan=managed_plan(response['plan'])
                assert plan['command']['containerId']==location['containerId'] and plan['command']['instanceId']==location['instanceId'] and plan['command']['path']==path
                assert plan['command']['aclType']==kind and plan['sourceIdentity']==before[relative]['identity'] and plan['accessAcl']==before[relative]['access'] and plan['defaultAcl']==before[relative]['default']
                assert plan['uid']==plan['gid']==1000 and plan['kind']==PATHS[relative] and plan['noChange']==(label in ('noop','default-noop'))
                assert plan['fingerprint']==hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+module.encode({k:v for k,v in plan.items() if k!='fingerprint'})).hexdigest().upper()
                assert inventory(folder,reader,module,content)==before
                output=probe.call({'action':'discard' if label=='cancel' else 'apply','plan':response['plan'],'boundaries':index==0})
                assert output['kind']=='receipt' and output['boundaryRefusals']==(4 if index==0 else 0);operation=output['operationId'];assert re.fullmatch('[a-f0-9]{32}',operation)
                result=json.loads(output['result']);record,root_sha=archive(evidence,operation,storage);expected='Aborted' if label=='cancel' else 'Completed'
                assert record['operationId']==operation and record['owner']=='B'*64 and record['plan']==plan and record['state']==result['state']==expected
                assert result['aclConfirmed']==(label!='cancel') and result['aclType']==kind and number(result['targetMode'])==plan['targetMode'] and result['fingerprint']==plan['fingerprint']
                after=inventory(folder,reader,module,content);unchanged_except_target(before,after,relative,plan['targetMode'] if label!='cancel' else before[relative]['identity'][2]&0o777)
                if label!='cancel':
                    assert after[relative][kind]==plan['command']['entries'] and record['actualIdentity']==after[relative]['identity'] and result['bindingCurrent']
                    assert after[relative]['default' if kind=='access' else 'access']==before[relative]['default' if kind=='access' else 'access']
                    assert record['version']==result['version']==reader.version(location,path,(folder/relative).stat())
                if label in ('noop','default-noop','cancel'):assert after==before
                if label=='root':assert any(e['tag']=='user' and e['id']==0 for e in after[relative]['access'])
                for action in ('inspect','replay','discard-old'):
                    replay=probe.call({'action':action,'operationId':operation});r=json.loads(replay['result']);assert r['replayed'] and r['state']==expected and r['version']==result['version']
                    assert inventory(folder,reader,module,content)==after and hashlib.sha256((STATE/(operation+'.json')).read_bytes()).hexdigest()==root_sha
                rows.append({'label':label,'request':{'operationId':operation,'owner':'B'*64,'plan':plan},'result':result,'before':before,'after':after,'rootSha256':root_sha})
                (evidence/'results.json').write_text(json.dumps(rows))
            final=inventory(folder,reader,module,content);first=rows[0];late=probe.call({'action':'replay','operationId':first['request']['operationId']});late=json.loads(late['result'])
            assert late['state']=='Completed' and late['replayed'] and not late['bindingCurrent'];assert inventory(folder,reader,module,content)==final
            assert hashlib.sha256((STATE/(first['request']['operationId']+'.json')).read_bytes()).hexdigest()==first['rootSha256']
            (evidence/'late-signed-rpc.json').write_text(json.dumps(late));(evidence/'physical-final.json').write_text(json.dumps(final))
            closing=probe;probe=None;closing.close(True)
            (evidence/'getfacl-final.txt').write_text(subprocess.check_output(['/usr/bin/getfacl','--absolute-names','--numeric',str(folder/'acl-file.bin'),str(folder/'acl-directory')],text=True))
            PHASE='proved-cleanup';assert {p.name for p in folder.iterdir()}=={'original.bin','acl-file.bin','acl-directory'}
            for relative in ('acl-directory/existing.bin','acl-file.bin'):
                assert list(reader.fingerprint((folder/relative).lstat()))==final[relative]['identity'];(folder/relative).unlink()
            directory=folder/'acl-directory';assert not list(directory.iterdir()) and list(reader.fingerprint(directory.lstat()))[:5]==final['acl-directory']['identity'][:5];directory.rmdir()
        assert not folder.exists() and owned.common.core_snapshot()==core and host_identity(revision)==hosts;no_active_work()
        units=subprocess.check_output(['systemctl','list-units','--all','--no-legend','whaledeck-container-acl-*'],text=True,timeout=10);assert not units.strip()
        result={'installedAclRpc':'passed','revision':revision,'operations':9,'completed':8,'aborted':1,'hddMetadata':8,'planBoundaryRefusals':13,'executionBoundaryRefusals':4,
                'fullBytesOriginalInodesOwnersMtimeOrdinaryAttributes':True,'nonselectedAclAndChildrenPreserved':True,'lateSignedRpcDidNotReapply':True,'exactSourceAndContainerRemoved':True,'coreAndHostsUnchanged':True,'humanBffOrUi':False}
        (evidence/'verified.json').write_text(json.dumps(result));print(json.dumps({**result,'evidence':evidence.name}),flush=True)
    except BaseException as error:
        (evidence/'failure.json').write_text(json.dumps({'phase':PHASE,'type':type(error).__name__,'completedOperations':len(rows)}));raise
    finally:
        if probe is not None:probe.close(False)


if __name__=='__main__':main()
