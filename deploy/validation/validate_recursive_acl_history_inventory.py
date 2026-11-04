#!/usr/bin/env python3
"""26 fresh signed captures, real inventory continuation/cache pages, exact cleanup."""
from contextlib import ExitStack
import hashlib,json,os,re,subprocess,sys,time,uuid
from pathlib import Path

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_upload_bff import temporary_user
from validate_container_upload_recovery_agent import trusted
from validate_container_recursive_acl_agent import Probe,host_identity,STATE
from validate_container_recursive_acl_browser import no_active_work
from validate_container_recursive_acl_faults import preservation_snapshot
from recursive_acl_history_probe import NAMES,root_snapshot
from validate_container_recursive_acl import PATHS,inventory,remove_proved,directory_acl
from validate_container_recursive_acl_history_browser import audit_rows,acknowledge
from recursive_acl_history_browser_proof import prove_audits
from recursive_acl_history_paging_proof import verify_capture
from recursive_acl_inventory_proof import LABELS,verify_chain,prove_signal
from validate_recursive_acl_history_paging import database,PROBE_SOURCES
from verify_container_recursive_acl_agent import proto_plan
from acl_source_apply_missing_proof import source_closure

PHASE='startup'
SOURCES=('validate_recursive_acl_history_inventory.py','validate_recursive_acl_history_inventory.mjs','recursive_acl_inventory_proof.py','verify_recursive_acl_history_inventory.py','test_recursive_acl_inventory_proof.py')

def content(identifier):
    assert re.fullmatch('[a-f0-9]{64}',identifier)
    return hashlib.shake_256(('recursive-inventory:'+identifier).encode()).digest(65536+7)

def wait_case(process,evidence,name):
    assert name in tuple('inventory-'+v for v in LABELS)+('inventory-auth',)
    deadline=time.monotonic()+150;p=evidence/(name+'-result.json')
    while not p.exists():
        assert process.poll() is None,'REAL_INVENTORY_BROWSER_EXITED'
        assert time.monotonic()<deadline,'INVENTORY_BROWSER_DEADLINE';time.sleep(.1)
    trusted(p);assert p.stat().st_size<=256*1024;return json.loads(p.read_bytes())

def unchanged(evidence,b,storage):
    actual=preservation_snapshot(storage);assert len(actual)==len(b['protectedFiles'])+260 and all(actual[k]==v for k,v in b['protectedFiles'].items())
    assert actual==json.loads((evidence/'captures-preservation.json').read_bytes())
    assert root_snapshot(Path('/var/lib/whaledeck-container-directories'))==b['directoryRoot']
    assert owned.common.core_snapshot()=={k:tuple(v) for k,v in b['coreBefore'].items()} and host_identity(b['revision'])==b['hostIdentity'] and database()==b['database'];no_active_work();return actual

def main():
    global PHASE
    assert os.geteuid()==0 and len(sys.argv)==4;os.umask(0o077);os.environ['GIT_OPTIONAL_LOCKS']='0'
    revision,binary_name,probe_revision=sys.argv[1:];assert all(re.fullmatch('[a-f0-9]{40}',v) for v in (revision,probe_revision));repo=Path(__file__).resolve().parents[2];binary=Path(binary_name)
    assert binary.parent.parent==repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+',binary.parent.name) and binary.name=='TerminalProbe';trusted(binary);trusted(binary.parent,True)
    subprocess.run(['sha256sum','--quiet','-c','SHA256SUMS'],cwd=binary.parent,check=True,capture_output=True)
    assert not subprocess.check_output(['git','-C',str(repo),'status','--porcelain']).strip(),'FREEZE_INVENTORY_GATE_FIRST'
    validation_revision=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip();hashes=source_closure(repo,validation_revision,seeds=SOURCES)
    assert all(hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest()==v for n,v in hashes.items())
    probe_hashes={};host_hashes={}
    for name in PROBE_SOURCES:
        raw=subprocess.check_output(['git','-C',str(repo),'show',probe_revision+':'+name]);assert raw==(repo/name).read_bytes();probe_hashes[name]=hashlib.sha256(raw).hexdigest()
    for name in (*NAMES,'whaledeck-container-acl-transport.py','whaledeck-container-recursive-acl-transport.py','whaledeck-container-recursive-acl-history-transport.py','whaledeck-privileged'):
        path=Path('/usr/local/libexec')/name;trusted(path);raw=subprocess.check_output(['git','-C',str(repo),'show',revision+':deploy/host/'+name]);assert path.read_bytes()==raw;host_hashes[name]=hashlib.sha256(raw).hexdigest()
    reader=load('/usr/local/libexec/whaledeck-container-files.py','inventory_reader');f=reader.trusted_module('whaledeck-files.py','inventory_files');acl=load('/usr/local/libexec/whaledeck-container-acl.py','inventory_acl');storage=load('/usr/local/libexec/whaledeck-file-backups.py','inventory_hdd').FileBackups(f)
    protected=preservation_snapshot(storage);directories=root_snapshot(Path('/var/lib/whaledeck-container-directories'));core=owned.common.core_snapshot();hosts=host_identity(revision);sql=database();no_active_work()
    evidence=repo/'deploy/artifacts'/('recursive-acl-history-inventory-'+uuid.uuid4().hex);evidence.mkdir(mode=0o700);(evidence/'captures').mkdir(mode=0o700)
    print(json.dumps(dict(actualRecursiveHistoryInventory='started',evidence=evidence.name)),flush=True);probe=None;rows=[]
    try:
        with owned.fixture() as fixture:
            PHASE='owned-four-object-tree';root=fixture['folder']/'recursive-tree';root.mkdir(mode=0o750);(root/'sub').mkdir(mode=0o700);data=content(fixture['identifier'])
            for index,(relative,kind) in enumerate(PATHS.items()):
                path=root/relative
                if kind=='File':path.write_bytes(data)
                os.chown(path,2100+index,3100+index);path.chmod(0o750 if kind=='Directory' else 0o640 if relative=='one.bin' else 0o600);os.setxattr(path,'user.whaledeck-recursive',b'preserve ordinary attribute')
                if kind=='Directory':
                    fd=os.open(path,os.O_RDONLY|os.O_DIRECTORY)
                    try:acl.write_acl(fd,'default',directory_acl())
                    finally:os.close(fd)
            for relative in PATHS:os.utime(root/relative,ns=(1700000000123456789,1700000000123456789))
            inspected=json.loads(owned.common.command(['docker','inspect',fixture['identifier']]))[0];location=dict(containerId=fixture['identifier'],instanceId=str(inspected['State']['Pid'])+':'+inspected['State']['StartedAt'])
            b=dict(revision=revision,probeRevision=probe_revision,validationSourceRevision=validation_revision,validationSourceSha256=hashes,probeSourceSha256=probe_hashes,probeBinaryPath=str(binary),probeBinarySha256=hashlib.sha256(binary.read_bytes()).hexdigest(),sourceHashes=host_hashes,**location,
                containerName=inspected['Name'].lstrip('/'),source=str(fixture['folder']),root=str(root),evidence=str(evidence),original=inventory(root,reader,data,acl),bytesPerFile=len(data),sha256=hashlib.sha256(data).hexdigest().upper(),coreBefore=core,hostIdentity=hosts,protectedFiles=protected,directoryRoot=directories,database=sql,sourceDevice=root.stat().st_dev)
            with storage.target(create_namespace=False,read_only=True) as (cfg,fd):storage.attest(cfg,fd,read_only=True);b['hddDevice']=os.fstat(fd).st_dev
            (evidence/'baseline.json').write_text(json.dumps(b));current=b['original'];previous_protected=protected
            probe=Probe(binary,evidence,dict(revision=revision,probeRevision=probe_revision,**location,coreId=core['/whaledeck-api'][0]))
            for index in range(26):
                PHASE='fresh-capture-'+str(index);profile='mask' if index%2 else 'named'
                planned=probe.call(dict(action='plan',aclType='access',profile=profile,version=reader.version(location,'work/recursive-tree',root.stat()),boundaries=False));assert planned['kind']=='plan' and planned['boundaryRefusals']==0;wire=json.loads(planned['plan']);plan=proto_plan(wire)
                assert [plan[k] for k in ('entryCount','fileCount','directoryCount','changeCount')]==[4,2,2,4 if index==0 else 2] and inventory(root,reader,data,acl)==current
                operation=uuid.uuid4().hex;assert not os.path.lexists(STATE/(operation+'.json')) and not os.path.lexists(STATE/(operation+'.entries'))
                response=probe.call(dict(action='apply',operationId=operation,plan=wire));assert response['kind']=='receipt' and response['operationId']==operation
                row=dict(index=index,profile='inventory-'+profile,request=dict(operationId=operation,owner='B'*64,plan=plan),result=json.loads(response['result']),before=current,after=inventory(root,reader,data,acl));target=evidence/'captures'/operation;target.mkdir(mode=0o700)
                verify_capture(target,{**b,'original':current},row,storage,archive=True,profile=row['profile']);rows.append(row);current=row['after'];(evidence/'captures.json').write_text(json.dumps(rows))
                actual=preservation_snapshot(storage);assert len(actual)==len(protected)+10*(index+1) and all(actual[k]==v for k,v in previous_protected.items());previous_protected=actual
                print(json.dumps(dict(actualRecursiveHistoryInventory='capture-verified',index=index,changedCount=plan['changeCount'])),flush=True)
            closing=probe;probe=None;closing.close(True);(evidence/'captures-preservation.json').write_text(json.dumps(previous_protected));metadata=verify_chain(evidence,b,rows,storage);unchanged(evidence,b,storage)
            fixture_packet={k:b[k] for k in ('revision','containerId','instanceId','containerName','evidence')};fixture_packet['captures']=sorted(metadata);signals=[]
            with ExitStack() as users:
                admin=users.enter_context(temporary_user(True));ordinary=users.enter_context(temporary_user(False));env={**os.environ};env.pop('VALIDATION_USERNAME',None);env.pop('VALIDATION_PASSWORD',None)
                with (evidence/'browser.stdout').open('w') as out,(evidence/'browser.stderr').open('w') as err:
                    process=subprocess.Popen(['node',str(repo/'deploy/validation/validate_recursive_acl_history_inventory.mjs'),revision],stdin=subprocess.PIPE,stdout=out,stderr=err,env=env,text=True)
                    try:
                        process.stdin.write(json.dumps(dict(fixture=fixture_packet,credentials=dict(admin=dict(zip(('username','password'),admin)),ordinary=dict(zip(('username','password'),ordinary))))));process.stdin.close()
                        for label in LABELS:
                            PHASE='actual-ui-'+label;signal=wait_case(process,evidence,'inventory-'+label);prove_signal(signal,b,rows,metadata,label);signals.append(signal)
                            assert all(s['subject']==signals[0]['subject'] for s in signals) and inventory(root,reader,data,acl)==current and verify_chain(evidence,b,rows,storage)==metadata;unchanged(evidence,b,storage)
                            audits=audit_rows(signal['subject']);prove_audits(audits,signal['subject'],b,signal['pages'],[s for s in signals if s['label']=='selected'])
                            with (evidence/('inventory-'+label+'.audit.json')).open('x') as stream:json.dump(audits,stream)
                            acknowledge(evidence,'inventory-'+label,dict(originalsAndAuditVerified=True))
                        PHASE='normal-dual-logout';auth=wait_case(process,evidence,'inventory-auth');assert auth['signals']==signals and auth['subject']==signals[0]['subject'];other=auth['ordinary']
                        assert other['subject']!=auth['subject'] and other['denied']==[403,403] and other['anonymous']==auth['adminAnonymous']==[401,401] and other['uiRequests']==0 and audit_rows(other['subject'])==[]
                        for logout in (auth['adminLogout'],other['logout']):assert logout['normalUiLogout'] and logout['providerNavigationObserved'] and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
                        assert inventory(root,reader,data,acl)==current and verify_chain(evidence,b,rows,storage)==metadata;unchanged(evidence,b,storage)
                        acknowledge(evidence,'inventory-auth',dict(authorityAndOriginalsVerified=True));assert process.wait(timeout=90)==0,'ACTUAL_INVENTORY_BROWSER_FAILED'
                    finally:
                        if process.poll() is None:
                            process.terminate()
                            try:process.wait(timeout=15)
                            except subprocess.TimeoutExpired:process.kill();process.wait(timeout=10)
            PHASE='independent-live'
            with (evidence/'independent-live.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_history_inventory.py'),evidence.name,'--live'],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
            assert inventory(root,reader,data,acl)==current;remove_proved(root,reader,data,current,acl)
        PHASE='exact-cleanup';assert not fixture['folder'].exists();final=unchanged(evidence,b,storage);(evidence/'final-preservation.json').write_text(json.dumps(final));(evidence/'verified.json').write_text(json.dumps(dict(revision=revision,captures=26,childMetadata=104,realInventoryPagination=True,realSnapshotPagination=False,historyAudits=3,exactSourceAndContainerAbsent=True)))
        PHASE='independent-final'
        with (evidence/'independent-final.log').open('x') as stream:subprocess.run([sys.executable,str(repo/'deploy/validation/verify_recursive_acl_history_inventory.py'),evidence.name],stdout=stream,stderr=subprocess.STDOUT,check=True,timeout=120)
        print(json.dumps(dict(actualRecursiveHistoryInventory='passed',evidence=evidence.name,captures=26,historyAudits=3)),flush=True)
    except BaseException as error:
        import traceback
        last=traceback.extract_tb(error.__traceback__)[-1];(evidence/'failure.json').write_text(json.dumps(dict(phase=PHASE,type=type(error).__name__,file=Path(last.filename).name,line=last.lineno,verifiedCaptures=len(rows))));print(json.dumps(dict(actualRecursiveHistoryInventory='failed',phase=PHASE,evidence=evidence.name,type=type(error).__name__)),flush=True);raise SystemExit(1) from None
    finally:
        if probe is not None:probe.close(False)

if __name__=='__main__':main()
