"""Independent wire and actual temporary Root byte joins; no installed RPC.

Namespace/storage and expiry-clock doubles are explicit in native fixtures.
Consistency of metadata never proves physical removal or authorization.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import unittest
import uuid
from unittest.mock import patch
import container_recycle_archive_retirement_rpc_proof as proof
import test_container_recycle_archive_rpc_proof as old
import test_container_file_staging as staging
from recursive_acl_restore_agent_io import read_at,snapshot


def rehash(p):p['fingerprint']=proof.digest(b'whaledeck-container-recycle-archive-retention-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'});return p


def plan():
    a=old.request();ap=a['plan'];published=ap['deletedIdentity'][:];published[:2]=[ap['parentIdentity'][0],9000];published[8]=100
    return rehash(dict(schemaVersion=1,command=dict(archive=a,retainDays=30,mode='ArchiveData'),archiveRecordDigest='A'*64,sourceRecordDigest=ap['sourceRecordDigest'],purgeRecordDigest=ap['purgeRecordDigest'],purgeMarkerDigest=ap['purgeMarkerDigest'],claimDigest='B'*64,historyMetadataDigest=ap['historyMetadataDigest'],historyCreatedAtUnixMs=ap['command']['purge']['plan']['historyCreatedAtUnixMs'],notBeforeUnixMs=ap['command']['purge']['plan']['historyCreatedAtUnixMs']+30*86400000,retentionElapsed=False,bindingCurrent=True,retirementEligible=False,storageHash=ap['storageHash'],storeIdentity=ap['storeIdentity'][:],archiveIdentity=ap['archiveIdentity'][:],archiveBytes=ap['archiveBytes'],archiveSha256=ap['archiveSha256'],publishedIdentity=published))


def wire_plan(value):
    p=copy.deepcopy(value);p['command']['archive']['plan']=old.wire_plan(p['command']['archive']['plan']);p['storeIdentity']=dict(zip(('device','inode'),map(str,p['storeIdentity']),strict=True))
    for k,changed in (('archiveIdentity','archiveChangedAtUnixNs'),('publishedIdentity','publishedChangedAtUnixNs')):
        i=p[k];p[k]={n:v if n in ('mode','ownerUid','ownerGid') else str(v) for n,v in zip(proof.archive.purge.FRAME[:8],i[:8],strict=True)};p[changed]=str(i[8])
    for k in ('historyCreatedAtUnixMs','notBeforeUnixMs','archiveBytes'):p[k]=str(p[k])
    return p


def request():return dict(operationId='9'*32,owner='E'*64,plan=plan())


def wire_request(q):return dict(operationId=q['operationId'],owner=q['owner'],plan=wire_plan(q['plan']),context=dict(actorSubject='installed-recycle-retirement-probe',traceId='owned-retirement-validation',jobId=q['operationId'],idempotencyKey='8'*32,capability=''))


def receipt(q,state='Reserved'):
    p=q['plan'];a=p['command']['archive'];r=dict(operationId=q['operationId'],sourceOperationId=proof.source_ref(p)['operationId'],archiveOperationId=a['operationId'],fingerprint=p['fingerprint'],state=state,sourceFenced=state!='Aborted',ownedFence=state!='Aborted',notBeforeUnixMs=p['notBeforeUnixMs'])
    if state!='Unknown':r['retirementApplied']=state=='Completed'
    return r


class RetirementRpcWireTests(unittest.TestCase):
    def test_complete_plan_and_request_roundtrip_keep_all_twenty_fields(self):
        p=plan();self.assertEqual(proof.proto_plan(wire_plan(p)),p);self.assertEqual(len(p),20)
        q=request();native,context=proof.proto_request(wire_request(q));self.assertEqual(native,q);self.assertEqual(context['jobId'],q['operationId'])
        p['publishedIdentity'][7]=-(2**63);p['command']['archive']['plan']['deletedIdentity'][7]=-(2**63)
        # Altering original facts requires all ancestors to remain consistent;
        # the independent projection must reject an outer-only rehash.
        rehash(p)
        with self.assertRaises(AssertionError):proof.proto_plan(wire_plan(p))

    def test_canonical_int64_strings_retain_nanosecond_precision(self):
        p=plan();p['publishedIdentity'][8]=2**63-1;rehash(p)
        self.assertEqual(proof.proto_plan(wire_plan(p)),p)
        wire=wire_plan(p);wire['publishedChangedAtUnixNs']=float(2**63-1)
        with self.assertRaises(AssertionError):proof.proto_plan(wire)

    def test_rehashed_invalid_facts_and_bool_numbers_are_rejected(self):
        for field in ('sourceRecordDigest','purgeRecordDigest','purgeMarkerDigest','historyMetadataDigest','storageHash','archiveBytes','notBeforeUnixMs','retirementEligible','publishedIdentity','retainDays','mode'):
            p=plan()
            if field in ('retainDays','mode'):p['command'][field]=True if field=='retainDays' else 'ArchiveCopy'
            elif field=='publishedIdentity':p[field]=p['archiveIdentity'][:]
            elif field=='retirementEligible':p[field]=True
            elif field in ('archiveBytes','notBeforeUnixMs'):p[field]+=1
            else:p[field]='E'*64 if p[field]!='E'*64 else 'D'*64
            rehash(p)
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.native_plan_proof(p)

    def test_wire_unknown_fields_lossy_numbers_and_explicit_null_are_rejected(self):
        for field,value in (('archiveBytes',True),('notBeforeUnixMs','01'),('archiveChangedAtUnixNs',1.0),('retentionElapsed',0),('schemaVersion',True),('publishedIdentity',None),('extra',1)):
            p=wire_plan(plan());p[field]=value
            with self.subTest(field=field),self.assertRaises((AssertionError,TypeError)):proof.proto_plan(p)
        for raw in ('{"x":1,"x":2}','{"x":NaN}','{"x":1e999}'):
            with self.assertRaises(AssertionError):proof.unique_json(raw)

    def test_optional_unknown_absent_and_false_stay_distinct_but_null_is_not_a_bool(self):
        q=request();r=receipt(q,'Unknown');self.assertIsNone(proof.proto_receipt(r,q)['retirementApplied'])
        self.assertIs(proof.proto_receipt({**r,'retirementApplied':False},q)['retirementApplied'],False)
        for value in (None,True,0,'false'):
            with self.subTest(value=value),self.assertRaises(AssertionError):proof.proto_receipt({**r,'retirementApplied':value},q)
        for state in ('Reserved','Aborted','Completed'):
            r=receipt(q,state);r.pop('retirementApplied')
            with self.subTest(state=state),self.assertRaises(AssertionError):proof.proto_receipt(r,q)

    def test_receipt_source_archive_due_fences_and_state_bind_exactly(self):
        q=request();r=receipt(q,'Completed');self.assertTrue(proof.proto_receipt(r,q)['retirementApplied'])
        for field,value in (('sourceOperationId',q['operationId']),('archiveOperationId',q['operationId']),('operationId','1'*32),('notBeforeUnixMs',1),('fingerprint','F'*64),('state','Removing'),('ownedFence',False),('sourceFenced',False)):
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.proto_receipt({**r,field:value},q)

    def test_context_cannot_change_or_carry_capability_material(self):
        q=request()
        for field,value in (('actorSubject','other'),('traceId','other'),('jobId','1'*32),('idempotencyKey','0'*32),('capability','c2VjcmV0'),('extra','x')):
            v=wire_request(q);v['context'][field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.proto_request(v)
        a=q['plan']['command']['archive'];ids=(a['operationId'],a['plan']['command']['purge']['operationId'],proof.source_ref(q['plan'])['operationId'])
        for op in ids:
            with self.subTest(id=op),self.assertRaises(AssertionError):proof.request_proof({**q,'operationId':op})

    def test_parent_frames_keep_approved_source_and_same_context(self):
        q=request();p=q['plan'];c=proof.source_ref(p)['plan']['command'];header={k:c[k] for k in ('containerId','instanceId')}
        f=dict(kind='retirement-plan',plan=json.dumps(wire_plan(p)),replayRefused=True);self.assertEqual(proof.plan_frame(f,header,p['command']['archive']),p)
        v=wire_request(q);f=dict(kind='retirement-receipt',action='reserve',operationId=q['operationId'],request=json.dumps(v),result=json.dumps(receipt(q)))
        joined=proof.receipt_frame(f,q);self.assertEqual(joined['request'],q)
        changed={**joined['context'],'idempotencyKey':'7'*32}
        with self.assertRaises(AssertionError):proof.receipt_frame(f,q,expected_context=changed)

    def test_capability_failure_requires_only_header_sent_and_exact_old_gate(self):
        h=dict(revision='a'*40,probeRevision='b'*40,containerId='c'*64,instanceId='777:2026-11-09T00:00:00Z',coreId='d'*64)
        e=dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None,capabilityObservation=dict(protocolMajor=1,protocolMinor=59,retirementCapability=False))
        self.assertFalse(proof.capability_refusal(1,'',e,sent_lines=[json.dumps(h)])['physicalAcceptance'])
        for bad in ([json.dumps(h),'{}'],[]):
            with self.assertRaises(AssertionError):proof.capability_refusal(1,'',e,sent_lines=bad)
        with self.assertRaises(AssertionError):proof.capability_refusal(1,'',{**e,'phase':'signing-key'},sent_lines=[json.dumps(h)])

    def test_import_does_not_load_any_product_host_module(self):
        folder=Path(__file__).resolve().parent
        code="import sys,pathlib;sys.path.insert(0,sys.argv[1]);import container_recycle_archive_retirement_rpc_proof;assert not any('/deploy/host/' in str(getattr(m,'__file__','')) for m in sys.modules.values())"
        subprocess.run([sys.executable,'-c',code,str(folder)],check=True,capture_output=True,env={**os.environ,'PYTHONDONTWRITEBYTECODE':'1'})


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementRootJoinTests(unittest.TestCase):
    def setUp(self):
        self.f=old.ArchiveRecordJoinTests();self.f.setUp();self.addCleanup(self.f.doCleanups);f=self.f
        f.engine.reserve(f.q,f.f.session);f.engine.apply(f.q,f.f.session)
        retention=staging.load('whaledeck-container-recycle-archive-retention.py','rpc_retention_join').ContainerRecycleArchiveRetentionPreflight(f.engine)
        directory=old.old.native.directories.ContainerDirectories
        fence=staging.load('whaledeck-container-recycle-archive-retirement-fences.py','rpc_retirement_join_fences').retirement_fences_type(directory)
        self.state=f.f.base.disk.disk/'retirement-rpc-state';self.state.mkdir(mode=0o700);(self.state/'claims').mkdir(mode=0o700)
        self.engine=staging.load('whaledeck-container-recycle-archive-retirements.py','rpc_retirement_join_executor').archive_retirements_type(fence)(retention,self.state)
        p=self.engine.plan(dict(archive=f.q,retainDays=30,mode='ArchiveData'),f.f.session);self.q=dict(operationId=uuid.uuid4().hex,owner='E'*64,plan=p);self.ref=proof.source_ref(p)
        self.before,self.frames_before=self.originals();self.data=f.f.base.target/(self.ref['operationId']+'.data');self.observed=proof.MAX_TIME

    def originals(self):
        f=self.f;ref=f.f.ref;paths=dict(archive=f.state/(f.q['operationId']+'.json'),restoreClaim=f.engine.source.restore_claims_path/(ref['operationId']+'.json'),source=f.f.base.state/(ref['operationId']+'.json'),purge=f.f.state/(f.f.q['operationId']+'.json'),purgeMarker=f.f.base.state/(ref['operationId']+'.purge.json'),history=f.f.base.target/(ref['operationId']+'.json'))
        records={};frames={}
        for key,path in paths.items():
            raw,record,identity=read_at(None,str(path));records[key]=record;frames[key]=dict(identity=identity,sha256=hashlib.sha256(raw).hexdigest())
        records.update(storeIdentity=self.q['plan']['storeIdentity'],storageHash=self.q['plan']['storageHash'])
        return records,frames

    def capture(self,r):
        originals,frames=self.originals();registry={p.stem:read_at(None,str(p))[1] for p in self.state.glob('*.json')};root=registry[self.q['operationId']]
        path=self.state/'claims'/(self.ref['operationId']+'.json');marker=read_at(None,str(path))[1] if path.exists() else None
        return dict(q=self.q,receipt=r,root=root,marker=marker,originals=originals,originals_before=self.before,original_frames=frames,frames_before=self.frames_before,registry=registry,root_frames=snapshot(self.state),observed_at=self.observed)

    def test_real_reserved_cancel_and_metadata_bytes_join(self):
        r=self.engine.reserve(self.q,self.f.f.session);result=proof.record_proof(**self.capture(r));self.assertTrue(result['rootRecordsJoined']);self.assertFalse(result['physicalDataDeletionProved'])
        r=self.engine.discard(self.q,self.f.f.session);self.assertTrue(proof.record_proof(**self.capture(r))['originalRecordsPreserved']);self.assertTrue(self.data.exists())

    def test_real_completed_certificate_binds_but_does_not_prove_data_absence(self):
        self.engine.reserve(self.q,self.f.f.session);due=self.q['plan']['notBeforeUnixMs']
        with patch('time.time_ns',return_value=(due+1000)*1000000):r=self.engine.apply(self.q,self.f.f.session)
        self.observed=due+2000;result=proof.record_proof(**self.capture(r));self.assertTrue(result['completionCertificateBound']);self.assertFalse(result['physicalDataDeletionProved']);self.assertFalse(self.data.exists())
        self.data.write_bytes(b'conflicting new bytes')
        self.assertFalse(proof.record_proof(**self.capture(r))['physicalDataDeletionProved'])

    def test_corrupted_or_future_certificate_even_rehashed_is_rejected(self):
        self.engine.reserve(self.q,self.f.f.session);due=self.q['plan']['notBeforeUnixMs']
        with patch('time.time_ns',return_value=(due+1000)*1000000):r=self.engine.apply(self.q,self.f.f.session)
        self.observed=due+2000;actual=self.capture(r)
        for field,value in (('unlinkConfirmed',False),('completedAtUnixMs',due+3000)):
            v=copy.deepcopy(actual);certificate=v['root']['completion'];certificate[field]=value;certificate['proofDigest']=proof.digest(b'whaledeck-container-recycle-archive-retirement-completion-v1\n',{k:x for k,x in certificate.items() if k!='proofDigest'})
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.record_proof(**v)

    def test_complete_registry_frame_names_and_empty_locks_are_required(self):
        r=self.engine.reserve(self.q,self.f.f.session);actual=self.capture(r)
        for field in ('path','lock'):
            v=copy.deepcopy(actual)
            if field=='path':v['root_frames']['../foreign.json']=next(iter(v['root_frames'].values()))
            else:v['root_frames'][self.q['operationId']+'.lock']['identity'][6]=1
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.record_proof(**v)

    def test_foreign_unknown_marker_keeps_source_fence_but_not_ownership(self):
        self.engine.reserve(self.q,self.f.f.session)
        foreign={**self.q,'operationId':uuid.uuid4().hex,'owner':'F'*64};self.engine.write_marker(foreign,'Unknown')
        r=self.engine.inspect(self.q,self.f.f.session);self.assertEqual(r['state'],'Unknown');self.assertFalse(r['ownedFence']);self.assertTrue(proof.record_proof(**self.capture(r))['rootRecordsJoined'])

    def test_unreconciled_v1_unknown_with_retired_marker_is_not_an_rpc_result(self):
        self.engine.reserve(self.q,self.f.f.session);path=self.state/(self.q['operationId']+'.json')
        record=read_at(None,str(path))[1];record['state']='Unknown';path.write_text(json.dumps(record,separators=(',',':')));self.engine.write_marker(self.q,'Retired')
        r=receipt(self.q,'Unknown');r['retirementApplied']=False
        with self.assertRaises(AssertionError):proof.record_proof(**self.capture(r))

    def test_raw_or_original_substitution_cannot_be_joined(self):
        r=self.engine.reserve(self.q,self.f.f.session);actual=self.capture(r)
        for field in ('root-byte','original-byte','original-record','omitted-registry'):
            v=copy.deepcopy(actual)
            if field=='root-byte':v['root_frames'][self.q['operationId']+'.json']['sha256']='0'*64
            elif field=='original-byte':v['original_frames']['archive']['sha256']='0'*64
            elif field=='original-record':v['originals']['source']['state']='Restored'
            else:v['registry']={}
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.record_proof(**v)


if __name__=='__main__':unittest.main()
