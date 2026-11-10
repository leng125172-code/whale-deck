"""Opt-in real fixed retirement helper in new NVMe/HDD QA filesystems only.

Caller owns image attestation and exact teardown. Root QA uses path-rewritten
copies, with unchanged helper logic and stricter CPU/runtime limits. No
production helper, configuration, identity, source or business row is changed.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid

import validate_development_archive_helper_native as shared_probe
import validate_development_retirement_execution_native as execution_probe


def rpc_publication():
    option = os.environ.get('WHALEDECK_RETIREMENT_RPC_PROBE')
    if option is None: return None
    if option != '1': raise ValueError('NATIVE_RETIREMENT_RPC_OPTION_INVALID')
    return Path(__file__).resolve().parent / 'DevelopmentRetirementRpcProbe/bin/Release/net10.0/publish'


def exercise(probe, driver, engine, request, evidence, payload, runtime_home, configuration, base, layout):
    sources = Path(__file__).resolve().parents[1] / 'host'
    private = runtime_home / 'retirement-helper-native'; private.mkdir(mode=0o700)
    library = private / 'libexec'; library.mkdir(mode=0o700)
    journal = private / 'journal'; journal.mkdir(mode=0o700)
    hdd = Path(engine.target.declaration()['mountPoint'])
    replacements = {'/usr/local/libexec':str(library),
        '/var/lib/whaledeck-development-retirement-operations':str(journal),
        '/var/lib/whaledeck-development-disks/layout.json':str(layout),
        '/etc/whaledeck/development-storage.json':str(configuration),
        '/var/lib/whaledeck-development':str(base), '/data/DevSpace':str(hdd)}
    names = ('whaledeck-privileged','whaledeck-files.py','whaledeck-development-storage.py',
        'whaledeck-development-project-quota.py','whaledeck-development-quota-guard.py',
        'whaledeck-development-lifecycle.py','whaledeck-development-archive-guard.py',
        'whaledeck-development-archives.py','whaledeck-development-archive-operations.py',
        'whaledeck-development-archive-control.py','whaledeck-development-archive-transport.py',
        'whaledeck-file-tree.py','whaledeck-tree-archive.py','whaledeck-development-retirement.py',
        'whaledeck-development-retirement-execution.py','whaledeck-development-retirement-operations.py',
        'whaledeck-development-retirement-transport.py')
    records = {}
    for name in names:
        original=(sources/name).read_bytes()
        text=shared_probe.isolate_text(original.decode(),replacements)
        text=text.replace('RuntimeMaxSec=7200','RuntimeMaxSec=180')
        text=text.replace('MemoryMax=512M --property=CPUQuota=100%','MemoryMax=512M --property=CPUQuota=5%')
        p=library/name;p.write_text(text);p.chmod(0o750)
        records[name]=dict(sourceSha256=hashlib.sha256(original).hexdigest(),
            isolatedSha256=hashlib.sha256(p.read_bytes()).hexdigest())
    probe.save(evidence/'retirement-helper-source-copies.json',records)
    shutil.copyfile(__file__,evidence/'retirement-helper-probe.py');(evidence/'retirement-helper-probe.py').chmod(0o600)
    retained=evidence/'retirement-helper-libexec';shutil.copytree(library,retained)
    for p in retained.iterdir():p.chmod(0o600)
    control=shared_probe.load(library/'whaledeck-development-archive-control.py')
    execution=control.Execution(driver,kind='retirement')
    planner_module=probe.load(sources,'development-retirement')
    lifecycle=probe.load(sources,'development-lifecycle')
    planner=planner_module.DevelopmentRetirementPlans(engine)
    original=probe.physical(driver,driver.quota,request,payload)
    root=payload.parent.parent
    owned,values,calls={},{},[]
    accepted,bridge=None,None

    def call(action,value,attempt=0):
        values[value['taskId']]=value
        wire=None
        if bridge is None:
            result=subprocess.run([str(library/'whaledeck-privileged'),'development-retirement-'+action,uuid.uuid4().hex],
                input=json.dumps(value),capture_output=True,text=True,timeout=25)
        else:
            wire=bridge.call(action,value)
            result=subprocess.CompletedProcess([],0 if wire['rpcStatus']=='OK' else 2,wire['stdout'],wire['stderr'])
        calls.append(dict(action=action,request=value,returncode=result.returncode,stdout=result.stdout,stderr=result.stderr,signedRpc=wire))
        probe.save(evidence/('retirement-helper-call-'+str(len(calls))+'.json'),calls[-1])
        if action=='start':
            path='/system.slice/whaledeck-development-retirement-run-'+value['taskId'].replace('-','')+'.service'
            try:info=os.stat(control.CGROUP/path[1:])
            except FileNotFoundError:pass
            else:owned[path]=dict(bootId=execution.boot(),path=path,device=info.st_dev,inode=info.st_ino)
        busy=(wire is None and result.stderr.strip()=='WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY' or
            wire is not None and wire['rpcStatus']=='Unavailable' and result.stderr=='WORKSPACE_RETIREMENT_HOST_BUSY')
        if (result.returncode==2 and busy
                and action in ('observe','cancel') and attempt<20):
            time.sleep(0.05);return call(action,value,attempt+1)
        assert result.returncode==0,'NATIVE_RETIREMENT_HELPER_'+action.upper()+'_FAILED'
        if wire is not None:assert wire['signatureRefusalsPassed'] is True
        receipt=json.loads(result.stdout);assert receipt['taskId']==value['taskId']
        return receipt

    def finish(value):
        receipt=call('start',value);deadline=time.monotonic()+160
        while receipt['status'] in ('Pending','Running','Finishing'):
            assert time.monotonic()<deadline,'NATIVE_RETIREMENT_HELPER_DID_NOT_SETTLE'
            time.sleep(0.1);receipt=call('observe',value)
        assert receipt['status']=='Succeeded','NATIVE_RETIREMENT_HELPER_RESULT_REJECTED'
        assert call('start',value)==receipt
        return receipt

    def prepare(count):
        other=dict(request,workspaceId=str(uuid.uuid4()));driver.allocate(other)
        source=root/other['workspaceId']/'data'/'private-source';source.mkdir(mode=0o700)
        for index in range(count):
            p=source/('file-'+str(index)+'.bin');p.write_bytes(bytes(range(256))*16);p.chmod(0o640);os.chown(p,65534,65534)
        os.chown(source,65534,65534)
        manager=lifecycle.Lifecycle(driver)
        transition=dict(storage=other,action='stop',expectedEpoch=1,operationId='',planSha256='',storageProofSha256='')
        planned=manager.run(transition)
        manager.run(dict(transition,operationId=str(uuid.uuid4()),planSha256=planned['planSha256'],
            storageProofSha256=planned['storageProofSha256']),apply=True)
        archive_plan=engine.plan(other,2)
        archive=dict(storage=other,expectedEpoch=2,operationId=str(uuid.uuid4()),planSha256=archive_plan['planSha256'])
        copied=engine.capture(archive)
        prerequisite=dict(schemaVersion=1,storage=other,expectedEpoch=2,archiveId=archive['operationId'],
            archivePlanSha256=archive['planSha256'],captureReceiptSha256=copied['receiptSha256'])
        value=dict(schemaVersion=1,taskId=str(uuid.uuid4()),phase='plan',retirementId=str(uuid.uuid4()),
            prerequisite=prerequisite,retirementPlanSha256='')
        return other,archive,copied,value

    def verify_gone(other,archive,copied,result):
        assert result['result']['sourceRemoved'] and result['result']['quotaReleased'] and result['result']['claimReleased']
        assert not result['result']['releasesApplicationBudget'] and result['result']['retainsHdd']
        assert not (root/other['workspaceId']).exists()
        assert not (root/('.quota-'+str(result['result']['projectId'])+'.json')).exists()
        with driver.locked_root(other['rootId'],exclusive=False) as (parent,_):
            driver.quota.require_unused(parent,result['result']['projectId'])
            assert driver.quota.read_quota(parent,result['result']['projectId'])==(0,)*8
        assert copied==engine.verify(archive,copied['receiptSha256'])
        try:driver.allocate(other)
        except driver.files.Rejected as error:assert str(error)=='WORKSPACE_STORAGE_RETIRED'
        else:raise AssertionError('RETIRED_ALLOCATION_RECREATED')

    try:
        published=rpc_publication()
        if published is not None:bridge=shared_probe.RpcBridge(published,library,evidence,kind='retirement')
        other,archive,copied,value=prepare(4)
        hdd_before=execution_probe.snapshot(hdd)
        plan=finish(value)
        assert plan['result']==planner.plan(value['prerequisite'])
        execute=dict(value,taskId=str(uuid.uuid4()),phase='execute',retirementPlanSha256=plan['result']['retirementPlanSha256'])
        completed=finish(execute);verify_gone(other,archive,copied,completed)
        assert call('cancel',execute)==completed
        assert hdd_before==execution_probe.snapshot(hdd)
        # Prestart cancel has no storage work or execution proof, including
        # when its original signed request eventually reaches Start.
        canceled=dict(value,taskId=str(uuid.uuid4()))
        assert call('cancel',canceled)['status']=='Stopped'
        assert call('start',canceled)['status']=='Stopped'
        assert not (journal/canceled['taskId']/'execution.json').exists()
        long_storage,long_archive,long_copy,long_value=prepare(300)
        long_plan=finish(long_value)
        long_execute=dict(long_value,taskId=str(uuid.uuid4()),phase='execute',
            retirementPlanSha256=long_plan['result']['retirementPlanSha256'])
        hdd_before=execution_probe.snapshot(hdd)
        call('start',long_execute)
        deadline=time.monotonic()+30
        while not (journal/long_execute['taskId']/'execution.json').exists():
            assert time.monotonic()<deadline,'NATIVE_RETIREMENT_NO_EXECUTION_PROOF'
            time.sleep(0.01)
        assert call('observe',long_execute)['status']=='Running'
        assert call('cancel',long_execute)['status']=='Stopped'
        proof=json.loads((journal/long_execute['taskId']/'execution.json').read_bytes())
        assert execution.settled(proof)
        assert not (journal/long_execute['taskId']/'result.json').exists()
        assert call('start',long_execute)['status']=='Stopped'
        # Same engine request, explicitly new task ID after exact stop proof.
        resumed=finish(dict(long_execute,taskId=str(uuid.uuid4())))
        verify_gone(long_storage,long_archive,long_copy,resumed)
        assert hdd_before==execution_probe.snapshot(hdd)
        assert original==probe.physical(driver,driver.quota,request,payload)
        accepted=dict(passed=True,realSystemdLandlockSeccomp=True,newQaAllocationsRetired=2,
            rootlessOwnedPrivateSourceRemoved=True,kernelQuotaLimitsAndUsageZero=True,hddUnchanged=True,
            prestartCancellation=True,activeExecutionPreciselyStopped=True,sameTaskNeverRedispatched=True,
            explicitNewTaskResumesOriginalEngineRequest=True,originalSourceAndQuotaPreserved=True,
            signedAgentRpc=bridge is not None,productWorkflow=False,releasesApplicationBudget=False)
        for name,record in records.items():
            assert record['sourceSha256']==hashlib.sha256((sources/name).read_bytes()).hexdigest()
    finally:
        bridge_error=None
        if bridge is not None:
            try:bridge.close()
            except BaseException as error:bridge_error=error
        shared=shared_probe.load(library/'whaledeck-development-archive-operations.py')
        operations=shared_probe.load(library/'whaledeck-development-retirement-operations.py')
        executor=probe.load(sources,'development-retirement-execution').DevelopmentRetirementExecution(engine,
            probe.load(sources,'development-archives').PayloadFiles)
        tasks=operations.create(shared,driver,planner,executor,execution)
        # Fence even an ambiguous PID 1 submission before source mounts may
        # be torn down. Only these exact generated requests are admissible.
        for value in values.values():
            deadline=time.monotonic()+3
            while True:
                try:tasks.cancel(value);break
                except driver.files.Rejected as error:
                    if str(error)!='WORKSPACE_STORAGE_RETIREMENT_TASK_BUSY' or time.monotonic()>=deadline:raise
                    time.sleep(0.025)
        for p in journal.glob('*/execution.json'):
            proof=json.loads(p.read_bytes());owned.setdefault(proof['path'],proof)
        for proof in owned.values():
            assert execution.settled(proof,stop=True),'NATIVE_RETIREMENT_CLEANUP_UNCONFIRMED'
        # Keep PID 1 failures even when execution never acquired its journal
        # lock and therefore has no execution.json/cgroup proof to retain.
        for task in values:
            unit='whaledeck-development-retirement-run-'+task.replace('-','')+'.service'
            result=subprocess.run(['journalctl','-u',unit,'--no-pager','-o','json','-n','30'],capture_output=True,text=True,timeout=10)
            probe.save(evidence/('retirement-helper-unit-'+unit+'.json'),dict(returncode=result.returncode,stdout=result.stdout,stderr=result.stderr))
        probe.save(evidence/'retirement-helper-owned-executions.json',list(owned.values()))
        retained=evidence/'retirement-helper-journal';shutil.copytree(journal,retained)
        for p in retained.rglob('*'):p.chmod(0o700 if p.is_dir() else 0o600)
        probe.save(evidence/'retirement-helper-commands.json',calls)
        if accepted is not None and bridge_error is None:
            accepted['executionResourcesSettled']=True
            probe.save(evidence/'retirement-helper-accepted.json',accepted)
        if bridge_error is not None:raise bridge_error
    return accepted
