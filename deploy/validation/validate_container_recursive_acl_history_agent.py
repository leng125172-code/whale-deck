#!/usr/bin/env python3
"""Opt-in installed signed reads before exact cleanup of eight NEW owned ACL cases.

No restore, human BFF or UI. Live independent proof precedes original cleanup.
"""
import hashlib
import json
import os
from pathlib import Path
import select
import subprocess
import sys
import time

import validate_container_recursive_acl_agent as acl
from validate_container_recursive_acl_faults import preservation_snapshot
from recursive_acl_history_rpc_proof import page_proof,snapshot_proof,verify_transcript,verify_metadata

NAMES=('whaledeck-container-acl-history.py','whaledeck-container-recursive-acl-history.py','whaledeck-container-recursive-acl-history-transport.py')
PROBE_SOURCES=('deploy/validation/TerminalProbe/Program.cs','deploy/validation/TerminalProbe/ContainerRecursiveAclHistoryProbe.cs')
MAX_WIRE_LINE=1024*1024


class Probe(acl.Probe):
    def __init__(self,binary,evidence,header):
        self.error=(evidence/'history-rpc.stderr').open('wb');self.transcript=evidence/'history-rpc.jsonl';self.buffer=b''
        self.process=subprocess.Popen([str(binary),'container-recursive-acl-history'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error)
        try:
            self.send(header);ready=self.receive()
            assert set(ready)=={'kind','revision','probeRevision','protocolMinor'} and ready['kind']=='ready' and ready['revision']==header['revision'] and ready['probeRevision']==header['probeRevision']
            assert type(ready['protocolMinor']) is int and ready['protocolMinor']>=54
        except BaseException:self.close(False);raise
    def receive(self):
        deadline=time.monotonic()+45
        while b'\n' not in self.buffer:
            assert time.monotonic()<deadline,'HISTORY_PROBE_RESPONSE_DEADLINE'
            ready=select.select([self.process.stdout],[],[],max(0,deadline-time.monotonic()))[0];assert ready,'HISTORY_PROBE_RESPONSE_DEADLINE'
            data=os.read(self.process.stdout.fileno(),65536);assert data,'HISTORY_PROBE_EOF';self.buffer+=data
            assert len(self.buffer)<=MAX_WIRE_LINE+65536,'HISTORY_PROBE_OUTPUT_LIMIT'
        line,self.buffer=self.buffer.split(b'\n',1);assert len(line)<=MAX_WIRE_LINE,'HISTORY_PROBE_OUTPUT_LIMIT'
        value=json.loads(line)
        with self.transcript.open('ab') as stream:stream.write(line+b'\n')
        return value


def metadata_rows(storage,rows):
    result={}
    with storage.target(create_namespace=False,read_only=True) as (configuration,base):
        fd=storage.f.open_at(base,'container-recursive-acl-v1',os.O_RDONLY|os.O_DIRECTORY)
        try:
            storage.attest(configuration,fd,read_only=True)
            for row in rows:
                if row['result']['state']!='Completed':continue
                capture=row['request']['operationId'];assert capture not in result
                value=storage.read_json(fd,capture+'.json',16*1024**2)
                assert value['operationId']==capture and value['plan']==row['request']['plan'] and value['historyDigest']==row['result']['historyDigest']
                result[capture]=verify_metadata(value,row)
        finally:os.close(fd)
    return result


def exercise(binary,evidence,header,rows,observe,storage):
    original=observe();preserved=preservation_snapshot(storage);metadata=metadata_rows(storage,rows)
    expected={r['request']['operationId']:r for r in rows if r['result']['state']=='Completed'}
    assert len(expected)==7 and len(rows)==8
    (evidence/'history-read-preservation.json').open('x').write(json.dumps(preserved))
    def unchanged():
        assert observe()==original and preservation_snapshot(storage)==preserved and metadata_rows(storage,rows)==metadata
    location={**{k:header[k] for k in ('containerId','instanceId')},'path':'work/recursive-tree'}
    probe=Probe(binary,evidence,header);pages=[];snapshots=[]
    try:
        after='';version='';seen=[]
        for index in range(100):
            response=probe.call(dict(action='list',path=location['path'],after=after,take=1,expectedVersion=version,boundaries=index==0))
            assert response['kind']=='page' and response['boundaryRefusals']==(21 if index==0 else 0)
            page=json.loads(response['page']);seen+=page_proof(page,location,expected,metadata,after,1,version);pages.append(response);unchanged()
            (evidence/'history-pages.json').write_text(json.dumps(pages))
            if page['complete']:break
            after=page['nextAfter'];version=page['version']
        else:raise AssertionError('HISTORY_CATALOG_INCOMPLETE')
        assert seen==sorted(expected)
        for capture in sorted(expected):
            offset=0
            for _ in range(10000):
                response=probe.call(dict(action='read',path=location['path'],captureId=capture,historyDigest=expected[capture]['result']['historyDigest'],offset=offset,take=1,boundaries=not snapshots))
                assert response['kind']=='snapshot' and response['boundaryRefusals']==(19 if not snapshots else 0)
                value=json.loads(response['snapshot']);offset=snapshot_proof(value,expected[capture],location,metadata[capture],offset,1);snapshots.append(response);unchanged()
                (evidence/'history-snapshots.json').write_text(json.dumps(snapshots))
                if value['complete']:break
            else:raise AssertionError('HISTORY_ENTRIES_INCOMPLETE')
        closing=probe;probe=None;closing.close(True);unchanged()
    finally:
        if probe is not None:probe.close(False)
    transcript=[json.loads(line) for line in (evidence/'history-rpc.jsonl').read_bytes().splitlines()]
    result=verify_transcript(pages,snapshots,rows,metadata,location,header,transcript)
    result.update(installedRecursiveAclHistoryRpc='passed',sourceRootAndHddUnchanged=True,humanBffOrUi=False,restorationExecuted=False)
    (evidence/'history-verified.json').open('x').write(json.dumps(result));assert not (evidence/'history-rpc.stderr').read_bytes()
    # Separate process reopens the LIVE pinned fixture and actual HDD before cleanup.
    verifier=Path(__file__).with_name('verify_container_recursive_acl_history_agent.py')
    independent=subprocess.run([sys.executable,str(verifier),'--live',evidence.name],capture_output=True,text=True,timeout=90)
    (evidence/'history-independent-live.stdout').open('x').write(independent.stdout);(evidence/'history-independent-live.stderr').open('x').write(independent.stderr)
    assert independent.returncode==0,'INDEPENDENT_LIVE_HISTORY_RPC_FAILED';unchanged()
    return result


if __name__=='__main__':acl.main(history=True)
