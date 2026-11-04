"""Offline strict proof/IO/scope tests; no real RPC, credentials or workstation writes."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from recursive_acl_history_rpc_proof import summary_proof,snapshot_proof,page_proof,verify_transcript,verify_metadata
from validate_container_recursive_acl_history_agent import Probe,MAX_WIRE_LINE
from verify_container_recursive_acl_agent import ready_proof


def digest(domain,value):return hashlib.sha256(domain+json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode()).hexdigest().upper()


def fixture(index=1):
    location=dict(containerId='a'*64,instanceId='777:2026-11-04T00:00:00Z',path='work/recursive-tree')
    acl=[dict(tag='user_obj',id=None,permissions=0),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=0),dict(tag='other',id=None,permissions=0)]
    paths=('sub/中文.bin','one.bin','sub','');before={};entries=[]
    for i,rel in enumerate(paths):
        kind='File' if rel.endswith('.bin') else 'Directory';identity=[1,i+2,0o100000 if kind=='File' else 0o040000,1000,1000,1,0,0,0]
        access=copy.deepcopy(acl);access[0]['permissions']=i;identity[2]|=i<<6
        default=[] if kind=='File' else copy.deepcopy(acl)
        before[rel]=dict(identity=identity,access=access,default=default)
        entries.append(dict(relativePath=rel,plan=dict(kind=kind,sourceIdentity=identity,accessAcl=access,defaultAcl=default)))
    plan=dict(command=dict(**location,aclType='default'),entryCount=4,fileCount=2,directoryCount=2,changeCount=0,
              manifestDigest=digest(b'whaledeck-container-recursive-acl-manifest-v1\n',entries))
    request=dict(operationId=f'{index:032x}',owner='B'*64,plan=plan)
    metadata=dict(schemaVersion=1,resourceType='ContainerRecursiveAclMetadata',storageId='database-platform.backup.hdd',**request,entries=entries,createdAtUnixMs=0)
    metadata['historyDigest']=digest(b'whaledeck-container-recursive-acl-history-v1\n',metadata)
    row=dict(request=request,result=dict(state='Completed',historyDigest=metadata['historyDigest']),before=before)
    summary=dict(captureId=request['operationId'],historyDigest=metadata['historyDigest'],createdAtUnixMs='0',sourceInstanceId=location['instanceId'],changedAclType='default',
        **{k:plan[k] for k in ('manifestDigest','entryCount','fileCount','directoryCount','changeCount')})
    items=[]
    for rel in paths:
        value=before[rel]
        def wire(acls):return [{k:v for k,v in entry.items() if v is not None} for entry in acls]
        items.append(dict(relativePath=rel,kind='File' if rel.endswith('.bin') else 'Directory',originalMode=value['identity'][2]&0o777,accessAcl=wire(value['access']),defaultAcl=wire(value['default'])))
    snapshot=dict(location=location,summary=summary,offset=0,entries=items,complete=True)
    page=dict(location=location,version='A'*64,entries=[summary],nextAfter='',scannedCount=1,invalidCount=0,complete=True)
    return SimpleNamespace(location=location,row=row,metadata=metadata,summary=summary,snapshot=snapshot,page=page)


class RecursiveHistoryAgentProofTests(unittest.TestCase):
    def setUp(self):self.f=fixture()
    def test_exact_heterogeneous_acl_preimages_zero_ids_modes_and_epoch(self):
        f=self.f;verify_metadata(f.metadata,f.row);summary_proof(f.summary,f.row,f.metadata)
        self.assertEqual(snapshot_proof(f.snapshot,f.row,f.location,f.metadata,0,50),4)
        self.assertEqual(page_proof(f.page,f.location,{f.summary['captureId']:f.row},{f.summary['captureId']:f.metadata},'',1,''),[f.summary['captureId']])
    def test_every_summary_field_is_required_even_zero(self):
        f=self.f
        for key in f.summary:
            value=copy.deepcopy(f.summary);del value[key]
            with self.subTest(key=key),self.assertRaises((AssertionError,KeyError)):summary_proof(value,f.row,f.metadata)
    def test_counts_are_strict_integers_and_int64_timestamp_canonical(self):
        f=self.f
        for key in ('entryCount','fileCount','directoryCount','changeCount'):
            for bad in (False,True,'0',0.0,-1,10001):
                value=copy.deepcopy(f.summary);value[key]=bad
                with self.subTest(key=key,bad=bad),self.assertRaises(AssertionError):summary_proof(value,f.row,f.metadata)
        for bad in (False,True,'00','1e0','9223372036854775808',0.0):
            value=copy.deepcopy(f.summary);value['createdAtUnixMs']=bad
            with self.subTest(bad=bad),self.assertRaises(AssertionError):summary_proof(value,f.row,f.metadata)
    def test_wrong_capture_digest_manifest_instance_and_private_fields_reject(self):
        f=self.f
        for key in ('captureId','historyDigest','manifestDigest','sourceInstanceId','changedAclType'):
            value=copy.deepcopy(f.summary);value[key]='altered'
            with self.subTest(key=key),self.assertRaises(AssertionError):summary_proof(value,f.row,f.metadata)
        value=copy.deepcopy(f.summary);value['owner']='private'
        with self.assertRaises(AssertionError):summary_proof(value,f.row,f.metadata)
    def test_aggregate_hash_and_manifest_are_verified_independently(self):
        f=self.f
        for change in ('digest','manifest','preimage','schema','owner','extra'):
            value=copy.deepcopy(f.metadata)
            if change=='digest':value['historyDigest']='F'*64
            elif change=='manifest':value['entries'].reverse()
            elif change=='preimage':value['entries'][0]['plan']['accessAcl'][0]['permissions']=7
            elif change=='schema':value['schemaVersion']=True
            elif change=='owner':value['owner']='C'*64
            else:value['privatePath']='/secret'
            with self.subTest(change=change),self.assertRaises(AssertionError):verify_metadata(value,f.row)
    def test_entry_page_progress_and_absent_final_next_offset(self):
        f=self.f
        for offset in range(4):
            page=copy.deepcopy(f.snapshot);page.update(offset=offset,entries=[page['entries'][offset]],complete=offset==3)
            if offset<3:page['nextOffset']=offset+1
            self.assertEqual(snapshot_proof(page,f.row,f.location,f.metadata,offset,1),offset+1)
        value=copy.deepcopy(f.snapshot);value['nextOffset']=None
        with self.assertRaises(AssertionError):snapshot_proof(value,f.row,f.location,f.metadata,0,50)
    def test_missing_zero_scalars_acl_qualifiers_and_wrong_page_binding_reject(self):
        f=self.f
        for field in ('offset','complete','entries','summary','location'):
            value=copy.deepcopy(f.snapshot);del value[field]
            with self.subTest(field=field),self.assertRaises((AssertionError,KeyError)):snapshot_proof(value,f.row,f.location,f.metadata,0,50)
        for change in ('permissions','id','mode','order','location','duplicate','private'):
            value=copy.deepcopy(f.snapshot)
            if change=='permissions':del value['entries'][0]['accessAcl'][0]['permissions']
            elif change=='id':del value['entries'][0]['accessAcl'][1]['id']
            elif change=='mode':value['entries'][0]['originalMode']=False
            elif change=='order':value['entries'].reverse()
            elif change=='location':value['location']['containerId']='b'*64
            elif change=='duplicate':value['entries'][1]=value['entries'][0]
            else:value['entries'][0]['owner']='private'
            with self.subTest(change=change),self.assertRaises((AssertionError,KeyError)):snapshot_proof(value,f.row,f.location,f.metadata,0,50)
    def test_catalog_requires_version_monotonic_cursor_and_exact_boolean_scalars(self):
        f=self.f
        for change in ('scanned','invalid','complete','cursor','version','duplicate'):
            page=copy.deepcopy(f.page)
            if change=='scanned':page['scannedCount']=True
            elif change=='invalid':page['invalidCount']=1
            elif change=='complete':page['complete']=1
            elif change=='cursor':page.update(complete=False,nextAfter='')
            elif change=='version':page['version']='B'*64
            else:page['entries']*=2
            with self.subTest(change=change),self.assertRaises(AssertionError):page_proof(page,f.location,{f.summary['captureId']:f.row},{f.summary['captureId']:f.metadata},'',1,'A'*64)
    def test_full_transcript_covers_all_captures_and_rejects_missing_or_replayed_page(self):
        fixtures=[fixture(i) for i in range(1,8)];rows=[f.row for f in fixtures];metadata={f.summary['captureId']:f.metadata for f in fixtures}
        pages=[];snapshots=[];location=fixtures[0].location;header=dict(revision='a'*40,probeRevision='b'*40)
        for i,f in enumerate(fixtures):
            page=copy.deepcopy(f.page);page.update(complete=i==6,nextAfter='' if i==6 else f.summary['captureId'])
            pages.append(dict(kind='page',page=json.dumps(page),boundaryRefusals=21 if i==0 else 0))
            for offset in range(4):
                item=copy.deepcopy(f.snapshot);item.update(offset=offset,entries=[item['entries'][offset]],complete=offset==3)
                if offset<3:item['nextOffset']=offset+1
                snapshots.append(dict(kind='snapshot',snapshot=json.dumps(item),boundaryRefusals=19 if i==0 and offset==0 else 0))
        transcript=[dict(kind='ready',**header,protocolMinor=54),*pages,*snapshots,dict(kind='done')]
        result=verify_transcript(pages,snapshots,rows,metadata,location,header,transcript);self.assertEqual(result['authorityRefusals'],40)
        for changed in (snapshots[:-1],snapshots[:1]+snapshots[:-1],list(reversed(snapshots))):
            with self.subTest(length=len(changed)),self.assertRaises((AssertionError,IndexError)):verify_transcript(pages,changed,rows,metadata,location,header,[transcript[0],*pages,*changed,transcript[-1]])
    def test_old_mutation_ready_accepts_53_and_54_without_accepting_bool_or_losing_revision(self):
        baseline=dict(revision='a'*40,probeRevision='b'*40)
        for minor in (53,54):ready_proof(dict(kind='ready',**baseline,protocolMinor=minor),baseline)
        for minor in (52,True,54.0,'54'):
            with self.subTest(minor=minor),self.assertRaises(AssertionError):ready_proof(dict(kind='ready',**baseline,protocolMinor=minor),baseline)
        with self.assertRaises(AssertionError):ready_proof(dict(kind='ready',revision='c'*40,probeRevision='b'*40,protocolMinor=54),baseline)
    def test_reader_accepts_large_escaped_wire_frame_and_rejects_over_budget(self):
        with tempfile.TemporaryDirectory() as temporary:
            probe=Probe.__new__(Probe);probe.transcript=Path(temporary)/'transcript';probe.buffer=json.dumps({'snapshot':'x'*200000}).encode()+b'\n'
            self.assertEqual(len(probe.receive()['snapshot']),200000)
            probe.buffer=b' '*MAX_WIRE_LINE+b' \n'
            with self.assertRaisesRegex(AssertionError,'OUTPUT_LIMIT'):probe.receive()
    def test_source_probe_fixed_uds_read_actions_zeroed_key_and_no_host_path_override(self):
        root=Path(__file__).resolve().parents[2];source=(root/'deploy/validation/TerminalProbe/ContainerRecursiveAclHistoryProbe.cs').read_text()
        for text in ('ContainerRecursiveAclHistoryBinding','docker.files.recursive-acl-history','caps.ProtocolMinor>=54','CryptographicOperations.ZeroMemory(key)','/run/whaledeck/agent.sock','path is "work/recursive-tree"'):
            self.assertIn(text,source)
        for text in ('client.ApplyAsync','client.DiscardAsync','client.PlanAsync','Console.WriteLine(key','hostPath'):
            self.assertNotIn(text,source)
        self.assertIn('if(action=="quit")',source)
        self.assertIn('if(action=="list")',source);self.assertIn('if(action=="read")',source)

if __name__=='__main__':unittest.main()
