"""Independent plan/record negatives; real temp IO has explicit namespace doubles."""
import copy
import hashlib
import json
import os
import unittest
import uuid
from types import SimpleNamespace
from unittest.mock import patch
import container_recycle_purge_rpc_proof as proof
import container_recycle_purge_agent_io as io
import test_container_recycle_rpc_proof as old
import test_container_file_recycling as native
import test_container_file_recycle_restores as restore
import test_container_recycle_purge_plan as preflight
import test_container_recycle_purge_fences as fences
import test_container_recycle_purges as purge


def rehash(p):
    p['fingerprint']=proof.digest(b'whaledeck-container-recycle-purge-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'});return p


def plan():
    src=old.plan()
    return rehash(dict(schemaVersion=1,command=dict(source=dict(operationId='a'*32,owner='B'*64,plan=src),historyDigest='C'*64,scope='Originals'),targetProof=src['targetProof'],parentIdentity=src['parentIdentity'][:],parentVersion=src['parentVersion'],uid=src['uid'],gid=src['gid'],privateIdentity=[2049,60],retainedIdentity=src['sourceIdentity'][:],sourceRecordDigest='D'*64,historyMetadataDigest='E'*64,historyCreatedAtUnixMs=1,storageHash='F'*64,storeIdentity=[2049,61],archiveIdentity=[2049,62,0o100600,0,0,1,3,2,3],archiveBytes=3,archiveSha256=src['sha256']))


def wire_plan(p):
    p=copy.deepcopy(p);p['command']['source']['plan']=old.wire_plan(p['command']['source']['plan']);p['ownerUid']=p.pop('uid');p['ownerGid']=p.pop('gid')
    for k in ('parentIdentity','privateIdentity','storeIdentity'):p[k]=dict(zip(('device','inode'),map(str,p[k]),strict=True))
    for k,changed in (('retainedIdentity','retainedChangedAtUnixNs'),('archiveIdentity','archiveChangedAtUnixNs')):
        i=p[k];p[k]={n:v if n in ('mode','ownerUid','ownerGid') else str(v) for n,v in zip(proof.FRAME[:8],i[:8],strict=True)};p[changed]=str(i[8])
    for k in ('historyCreatedAtUnixMs','archiveBytes'):p[k]=str(p[k])
    return p


def request(p=None):return dict(operationId='c'*32,owner='C'*64,plan=p or plan())


def receipt(q,state='Completed'):
    v=dict(operationId=q['operationId'],sourceOperationId=q['plan']['command']['source']['operationId'],fingerprint=q['plan']['fingerprint'],state=state,sourceFenced=state!='Aborted',ownedFence=state!='Aborted')
    if state!='Unknown':v['purgeApplied']=state=='Completed'
    return v


class PurgeProofTests(unittest.TestCase):
    def test_exact_wire_roundtrip_keeps_zero_unsigned_max_and_nanoseconds(self):
        p=plan();self.assertEqual(proof.proto_plan(wire_plan(p)),p);p['storeIdentity']=[0,2**64-1];p['archiveIdentity'][0]=2**64-1;p['archiveIdentity'][7]=-2**63;p['archiveIdentity'][8]=2**63-1;rehash(p);self.assertEqual(proof.proto_plan(wire_plan(p)),p)

    def test_self_consistent_hash_never_validates_wrong_source_archive_or_scope(self):
        for field in ('scope','uid','parent','target','retained','archive-mode','archive-owner','archive-links','archive-size','archive-hash','bool-store','bool-bytes','float-ctime','extra'):
            p=plan()
            if field=='scope':p['command']['scope']='All'
            elif field=='uid':p['uid']+=1
            elif field=='parent':p['parentIdentity'][1]+=1
            elif field=='target':p['targetProof']='F'*64
            elif field=='retained':p['retainedIdentity'][1]+=1
            elif field=='archive-mode':p['archiveIdentity'][2]=0o100644
            elif field=='archive-owner':p['archiveIdentity'][3]=1000
            elif field=='archive-links':p['archiveIdentity'][5]=2
            elif field=='archive-size':p['archiveIdentity'][6]+=1
            elif field=='archive-hash':p['archiveSha256']='F'*64
            elif field=='bool-store':p['storeIdentity'][0]=True
            elif field=='bool-bytes':p['archiveBytes']=True
            elif field=='float-ctime':p['archiveIdentity'][8]=3.0
            else:p['hostPath']='/etc'
            rehash(p)
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.native_plan_proof(p)

    def test_nested_unknown_missing_and_duplicate_fields_are_not_discarded(self):
        for path in ((),('command',),('command','source'),('command','source','plan'),('archiveIdentity',),('storeIdentity',)):
            p=wire_plan(plan());at=p
            for k in path:at=at[k]
            at['unexpected']='private'
            with self.subTest(path=path),self.assertRaises(AssertionError):proof.proto_plan(p)
        for k in proof.PLAN:
            p=wire_plan(plan());del p[k]
            with self.subTest(k=k),self.assertRaises(AssertionError):proof.proto_plan(p)
        for text in ('{"x":1,"x":2}','{"x":NaN}'):
            with self.assertRaises(AssertionError):proof.unique_json(text)

    def test_unknown_is_absence_not_false_null_or_success(self):
        q=request();v=receipt(q,'Unknown');self.assertIsNone(proof.proto_receipt(v,q)['purgeApplied'])
        for bad in (False,True,None,0):
            with self.subTest(bad=bad),self.assertRaises(AssertionError):proof.proto_receipt({**v,'purgeApplied':bad},q)
        for state in ('Reserved','Aborted','Completed'):
            v=receipt(q,state);v.pop('purgeApplied')
            with self.subTest(state=state),self.assertRaises(AssertionError):proof.proto_receipt(v,q)

    def test_operation_identity_fingerprint_and_fence_cannot_be_forged(self):
        q=request();v=receipt(q)
        for k,bad in (('operationId','d'*32),('sourceOperationId',q['operationId']),('fingerprint','F'*64),('sourceFenced',False),('ownedFence',False),('purgeApplied',False),('state','Removing')):
            with self.subTest(k=k),self.assertRaises(AssertionError):proof.proto_receipt({**v,k:bad},q)
        q['operationId']=q['plan']['command']['source']['operationId']
        with self.assertRaises(AssertionError):proof.request_proof(q)

    def test_old_host_transport_error_is_not_a_capability_rejection(self):
        error=dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None,capabilityObservation=dict(protocolMajor=1,protocolMinor=57,sourceCapability=True,restoreCapability=True,purgeCapability=False))
        self.assertEqual(proof.capability_refusal(1,'',error)['operationFramesSent'],0)
        for k,bad in (('phase','startup'),('code','CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE'),('rpcStatus','Unavailable'),('failureType','RpcException')):
            with self.subTest(k=k),self.assertRaises(AssertionError):proof.capability_refusal(1,'',{**error,k:bad})

    def test_frame_counts_and_job_are_bound_to_the_right_operation(self):
        p=plan();q=request(p);h=dict(revision='a'*40,probeRevision='b'*40,containerId=p['command']['source']['plan']['command']['containerId'],instanceId=p['command']['source']['plan']['command']['instanceId'],coreId='b'*64)
        self.assertEqual(proof.plan_frame(dict(kind='plan',plan=json.dumps(wire_plan(p)),boundaryRefusals=18),h,boundaries=True),p)
        f=dict(kind='receipt',action='reserve',operationId=q['operationId'],jobId=q['operationId'],result=json.dumps(receipt(q,'Reserved')));self.assertEqual(proof.receipt_frame(f,q)['state'],'Reserved')
        with self.assertRaises(AssertionError):proof.receipt_frame({**f,'jobId':'d'*32},q)

    def test_purged_source_receipt_cannot_claim_a_live_or_restored_original(self):
        ref=plan()['command']['source'];v=dict(operationId=ref['operationId'],containerId=ref['plan']['command']['containerId'],instanceId=ref['plan']['command']['instanceId'],fingerprint=ref['plan']['fingerprint'],targetProof=ref['plan']['targetProof'],state='Purged',bindingCurrent=False,replayed=True,historyMayExist=True,historyVerified=True,historyAvailable=True,historyDigest='A'*64,sourceRetained=False,sourceRestored=False)
        f=dict(kind='source-receipt',action='source-inspect',operationId=ref['operationId'],jobId=ref['operationId'],result=json.dumps(v));self.assertEqual(proof.source_receipt_frame(f,ref)['state'],'Purged')
        for key,bad in (('bindingCurrent',True),('sourceRetained',True),('sourceRestored',True),('historyVerified',False),('sourceRetained',None)):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.source_receipt_frame({**f,'result':json.dumps({**v,key:bad})},ref)


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class PurgeAcquisitionTests(unittest.TestCase):
    def setUp(self):
        self.base=native.LinuxContainerRecycleTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        work=self.base.root/'work';work.mkdir();self.base.leaf.rename(work/'recycle-rpc.bin');self.leaf=work/'recycle-rpc.bin';self.work=work
        self.original=io.source_io.file_snapshot(self.leaf);self.source=self.base.engine;self.session=self.base.session
        c={**self.session.request,'path':'work/recycle-rpc.bin','expectedVersion':self.base.r.version(self.session.request,'work/recycle-rpc.bin',self.leaf.stat())}
        self.ref=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=self.source.plan(c,self.session));self.source.apply(self.ref,self.session)
        rs=self.base.disk.disk/'restore-state';rs.mkdir(mode=0o700);(rs/'claims').mkdir(mode=0o700);restorer=restore.candidate.restores_type(native.directories.ContainerDirectories)(self.source,rs)
        planner=preflight.candidate.ContainerRecyclePurgePreflight(restorer);self.state=self.base.disk.disk/'purge-state';self.state.mkdir(mode=0o700)
        self.engine=purge.candidate.purges_type(fences.candidate.purge_fences_type(native.directories.ContainerDirectories))(planner,self.state)
        self.command=dict(source=self.ref,historyDigest=self.base.record(self.ref)['historyDigest'],scope='Originals');self.source_before=self.base.record(self.ref);self.history_before=self.base.saved(self.ref)
        self.q=dict(operationId=uuid.uuid4().hex,owner='C'*64,plan=planner.plan(self.command,self.session));self.archive_before=io.source_io.file_snapshot(self.base.target/(self.ref['operationId']+'.data'))
        for p in (patch.object(io,'STATE',self.state),patch.object(io.source_io,'STATE',self.base.state),patch.object(io,'installed',return_value=SimpleNamespace(storage=self.base.disk.engine,files=self.base.f))):p.start();self.addCleanup(p.stop)

    def acquire(self,result,name):
        v=io.read_operation(self.q,result,self.source_before,self.history_before,self.base.disk.disk/name,create=True);physical=io.physical(self.work,self.q);io.physical_proof(physical,self.original,self.q,v,result);return v,physical

    def test_reserved_cancelled_and_completed_real_temp_records_join_to_physical_archive(self):
        self.acquire(self.engine.reserve(self.q,self.session),'reserved');self.acquire(self.engine.discard(self.q,self.session),'cancelled')
        self.q=dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=self.engine.preflight.plan(self.command,self.session));self.engine.reserve(self.q,self.session);result=self.engine.apply(self.q,self.session);v,physical=self.acquire(result,'completed')
        self.assertTrue(v['root']['unlinkConfirmed']);self.assertIsNone(physical['retained']);self.assertEqual(v['data'],self.archive_before)

    def test_coherent_labels_cannot_hide_a_source_root_hdd_or_store_change(self):
        result=self.engine.reserve(self.q,self.session);v,_=self.acquire(result,'original')
        for key in ('root','marker','sourceRoot','history','storeIdentity','storageHash','data'):
            bad=copy.deepcopy(v)
            if key=='root':bad[key]['unlinkConfirmed']=True
            elif key=='marker':bad[key]['owner']='F'*64
            elif key=='sourceRoot':bad[key]['actualIdentity'][1]+=1
            elif key=='history':bad[key]['createdAtUnixMs']+=1
            elif key=='storeIdentity':bad[key][1]+=1
            elif key=='storageHash':bad[key]='A'*64
            else:bad[key]['identity'][1]+=1
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.record_proof(self.q,result,bad['root'],bad['marker'],bad['sourceRoot'],bad['history'],self.source_before,self.history_before,bad['data'],store_identity=bad['storeIdentity'],storage_hash=bad['storageHash'])

    def test_missing_private_namespace_or_extra_pending_file_cannot_prove_completion(self):
        result=self.engine.reserve(self.q,self.session);v,physical=self.acquire(result,'initial');bad=copy.deepcopy(physical);bad['retained']=None
        with self.assertRaises(AssertionError):io.physical_proof(bad,self.original,self.q,v,result)
        private=self.work/io.source_io.PRIVATE;(private/'unrelated.bin').write_bytes(b'keep foreign')
        with self.assertRaises(AssertionError):io.physical(self.work,self.q)
        self.assertEqual((private/'unrelated.bin').read_bytes(),b'keep foreign')

    def test_unknown_is_not_accepted_as_conclusive_absence_or_zero_modification(self):
        result=self.engine.reserve(self.q,self.session);v,physical=self.acquire(result,'initial');unknown={**result,'state':'Unknown','purgeApplied':None}
        with self.assertRaisesRegex(AssertionError,'UNCERTAIN_PURGE'):io.physical_proof(physical,self.original,self.q,v,unknown)

    def test_self_consistent_completed_labels_cannot_replace_physical_absence(self):
        result=self.engine.reserve(self.q,self.session);v,physical=self.acquire(result,'reserved');fake=copy.deepcopy(v);complete=receipt(self.q)
        fake['root'].update(state='Completed',unlinkConfirmed=True,pendingIdentity=self.q['plan']['retainedIdentity'][:]);fake['marker']['state']='Purged';fake['sourceRoot'].update(state='Purged',actualIdentity=None);fake['history']['captureState']='Purged'
        joined=proof.record_proof(self.q,complete,fake['root'],fake['marker'],fake['sourceRoot'],fake['history'],self.source_before,self.history_before,fake['data'],store_identity=fake['storeIdentity'],storage_hash=fake['storageHash'])
        self.assertFalse(joined['physicalNamespaceChecked'])
        with self.assertRaises(AssertionError):io.physical_proof(physical,self.original,self.q,fake,complete)


if __name__=='__main__':unittest.main()
