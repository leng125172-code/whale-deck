"""Wire/immutable-state proof tests; synthetic frames are not installed RPC."""
import copy
import json
import os
import unittest
import uuid
import container_recycle_archive_rpc_proof as proof
import test_container_recycle_purge_rpc_proof as old
import test_container_file_staging as staging


def rehash(p):p['fingerprint']=proof.digest(b'whaledeck-container-recycle-archive-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'});return p


def plan(private=False):
    p=old.plan();return rehash(dict(schemaVersion=1,command=dict(purge=dict(operationId='d'*32,owner='C'*64,plan=p),historyDigest=p['command']['historyDigest'],mode='ArchiveCopy'),targetProof=p['targetProof'],parentIdentity=p['parentIdentity'][:],parentVersion='A'*64,uid=p['uid'],gid=p['gid'],privateIdentity=p['privateIdentity'][:] if private else None,deletedIdentity=p['retainedIdentity'][:],sourceRecordDigest='1'*64,purgeRecordDigest='2'*64,purgeMarkerDigest='3'*64,historyMetadataDigest='4'*64,storageHash=p['storageHash'],storeIdentity=p['storeIdentity'][:],archiveIdentity=p['archiveIdentity'][:],archiveBytes=p['archiveBytes'],archiveSha256=p['archiveSha256']))


def wire_plan(value):
    p=copy.deepcopy(value);p['command']['purge']['plan']=old.wire_plan(p['command']['purge']['plan']);p['ownerUid']=p.pop('uid');p['ownerGid']=p.pop('gid')
    for key in ('parentIdentity','privateIdentity','storeIdentity'):
        if p[key] is not None:p[key]=dict(zip(('device','inode'),map(str,p[key]),strict=True))
    for key,changed in (('deletedIdentity','deletedChangedAtUnixNs'),('archiveIdentity','archiveChangedAtUnixNs')):
        i=p[key];p[key]={n:v if n in ('mode','ownerUid','ownerGid') else str(v) for n,v in zip(proof.purge.FRAME[:8],i[:8],strict=True)};p[changed]=str(i[8])
    p['archiveBytes']=str(p['archiveBytes']);return p


def request(p=None,operation='f'*32):return dict(operationId=operation,owner='D'*64,plan=p or plan())


def receipt(q,state='Completed',binding=None):
    p=q['plan'];ref=p['command']['purge'];v=dict(operationId=q['operationId'],sourceOperationId=ref['plan']['command']['source']['operationId'],purgeOperationId=ref['operationId'],fingerprint=p['fingerprint'],state=state,sourceFenced=state!='Aborted',ownedFence=state!='Aborted')
    if state!='Unknown':v['archiveCopied']=state=='Completed'
    if binding is not None:v['bindingCurrent']=binding
    if state=='Completed':
        i=p['deletedIdentity'][:];i[:2]=[p['parentIdentity'][0],4000];i[8]+=1000
        v['publishedIdentity']={n:value if n in ('mode','ownerUid','ownerGid') else str(value) for n,value in zip(proof.purge.FRAME[:8],i[:8],strict=True)};v['publishedChangedAtUnixNs']=str(i[8])
    return v


class ArchiveWireProofTests(unittest.TestCase):
    def test_roundtrip_keeps_private_null_presence_unicode_and_signed_nanoseconds(self):
        for present in (False,True):
            p=plan(present);self.assertEqual(proof.proto_plan(wire_plan(p)),p)
        p=plan();p['deletedIdentity'][8]=2**63-1;p['command']['purge']['plan']['retainedIdentity'][8]=2**63-1;old.rehash(p['command']['purge']['plan']);rehash(p);self.assertEqual(proof.proto_plan(wire_plan(p)),p)
        absent=wire_plan(plan());absent.pop('privateIdentity');self.assertIsNone(proof.proto_plan(absent)['privateIdentity'])

    def test_self_consistent_rehash_cannot_change_source_immutable_data_or_mode(self):
        for key in ('uid','parent','private','deleted','storage','archive','bytes','mode','history','bool'):
            p=plan()
            if key=='uid':p['uid']+=1
            elif key=='parent':p['parentIdentity'][1]+=1
            elif key=='private':p['privateIdentity']=[1,2]
            elif key=='deleted':p['deletedIdentity'][1]+=1
            elif key=='storage':p['storageHash']='A'*64
            elif key=='archive':p['archiveIdentity'][1]+=1
            elif key=='bytes':p['archiveBytes']+=1
            elif key=='mode':p['command']['mode']='Originals'
            elif key=='history':p['command']['historyDigest']='A'*64
            else:p['uid']=True
            rehash(p)
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.native_plan_proof(p)

    def test_optional_unknown_and_binding_do_not_become_false_or_success(self):
        q=request();v=receipt(q,'Unknown');self.assertIsNone(proof.proto_receipt(v,q)['archiveCopied'])
        for bad in (False,True,None,0):
            with self.subTest(bad=bad),self.assertRaises(AssertionError):proof.proto_receipt({**v,'archiveCopied':bad},q)
        for state in ('Reserved','Aborted','Completed'):
            v=receipt(q,state);v.pop('archiveCopied')
            with self.subTest(state=state),self.assertRaises(AssertionError):proof.proto_receipt(v,q)
        v=receipt(q);v['ownedFence']=False;v['bindingCurrent']=False;self.assertTrue(proof.proto_receipt(v,q)['archiveCopied']);v['bindingCurrent']=True
        with self.assertRaises(AssertionError):proof.proto_receipt(v,q)

    def test_publication_requires_new_inode_full_metadata_and_matching_three_ids(self):
        q=request();v=receipt(q,'Completed',True);self.assertTrue(proof.proto_receipt(v,q)['archiveCopied'])
        for key,bad in (('operationId','1'*32),('sourceOperationId',q['operationId']),('purgeOperationId',q['operationId']),('fingerprint','F'*64),('state','Copying'),('sourceFenced',False),('publishedChangedAtUnixNs',None)):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.proto_receipt({**v,key:bad},q)
        for field,value in (('inode',q['plan']['deletedIdentity'][1]),('inode',q['plan']['archiveIdentity'][1]),('device',999),('ownerUid',999),('mode',0),('sizeBytes',999),('modifiedAtUnixNs',999),('linkCount',2)):
            broken=copy.deepcopy(v);broken['publishedIdentity'][field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):proof.proto_receipt(broken,q)

    def test_extra_duplicate_fields_and_lossy_numbers_fail(self):
        for key in ('archiveBytes','ownerUid'):
            v=wire_plan(plan());v[key]=True
            with self.assertRaises(AssertionError):proof.proto_plan(v)
        for value in ('1.5','9007199254740993.0',1.0):
            v=wire_plan(plan());v['deletedIdentity']['inode']=value
            with self.assertRaises(AssertionError):proof.proto_plan(v)
        for text in ('{"x":1,"x":2}','{"x":NaN}'):
            with self.assertRaises(AssertionError):proof.unique_json(text)

    def test_exact_capability_failure_proves_no_operation_frames(self):
        error=dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None,capabilityObservation=dict(protocolMajor=1,protocolMinor=58,sourceCapability=True,restoreCapability=True,purgeCapability=True,archiveCapability=False));self.assertEqual(proof.capability_refusal(1,'',error)['operationFramesSent'],0)
        for key,bad in (('phase','archive-apply'),('code','CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE'),('rpcStatus','Unavailable')):
            with self.subTest(key=key),self.assertRaises(AssertionError):proof.capability_refusal(1,'',{**error,key:bad})


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'temporary Linux root required')
class ArchiveRecordJoinTests(unittest.TestCase):
    def setUp(self):
        self.f=old.PurgeAcquisitionTests();self.f.setUp();self.addCleanup(self.f.doCleanups);f=self.f
        f.engine.reserve(f.q,f.session);f.engine.apply(f.q,f.session)
        preflight=staging.load('whaledeck-container-recycle-archive-plan.py','archive_join_plan').ContainerRecycleArchivePreflight(f.engine);state=f.base.disk.disk/'archive-state';state.mkdir(mode=0o700)
        fence=staging.load('whaledeck-container-recycle-archive-fences.py','archive_join_fences').archive_fences_type(old.native.directories.ContainerDirectories)
        self.engine=staging.load('whaledeck-container-recycle-archive-copies.py','archive_join_copies').archive_copies_type(fence)(preflight,state)
        self.q=dict(operationId=uuid.uuid4().hex,owner='D'*64,plan=preflight.plan(dict(purge=f.q,historyDigest=f.command['historyDigest'],mode='ArchiveCopy'),f.session))
        self.source=f.base.record(f.ref);self.purge=json.loads((f.state/(f.q['operationId']+'.json')).read_bytes());self.marker=json.loads((f.base.state/(f.ref['operationId']+'.purge.json')).read_bytes());self.history=f.base.saved(f.ref);self.data=old.io.source_io.file_snapshot(f.base.target/(f.ref['operationId']+'.data'));self.state=state
        with f.source.history.target(False) as (config,fd):self.store=[os.fstat(fd).st_dev,os.fstat(fd).st_ino];self.storage_hash=proof.digest(b'',config)

    def joined(self,r):
        root=json.loads((self.state/(self.q['operationId']+'.json')).read_bytes());claim=self.f.source.read_restore_claim(self.f.ref)
        result=proof.record_proof(self.q,r,root,claim,self.f.base.record(self.f.ref),json.loads((self.f.state/(self.f.q['operationId']+'.json')).read_bytes()),json.loads((self.f.base.state/(self.f.ref['operationId']+'.purge.json')).read_bytes()),self.f.base.saved(self.f.ref),old.io.source_io.file_snapshot(self.f.base.target/(self.f.ref['operationId']+'.data')),source_before=self.source,purge_before=self.purge,marker_before=self.marker,history_before=self.history,data_before=self.data,store_identity=self.store,storage_hash=self.storage_hash)
        return result,root,claim

    def test_actual_reservation_and_new_inode_copy_join_keeps_old_lifecycles(self):
        r=self.engine.reserve(self.q,self.f.session);self.assertTrue(self.joined(r)[0]['sourceClaimMatches']);r=self.engine.apply(self.q,self.f.session);result,_,_=self.joined(r);self.assertFalse(result['physicalNamespaceChecked']);self.assertEqual(self.f.leaf.read_bytes(),self.f.base.content)

    def test_changed_actual_source_or_hdd_before_snapshot_cannot_be_substituted(self):
        r=self.engine.reserve(self.q,self.f.session);_,root,claim=self.joined(r);changed=copy.deepcopy(self.source);changed['state']='Restored'
        with self.assertRaises(AssertionError):proof.record_proof(self.q,r,root,claim,changed,self.purge,self.marker,self.history,self.data,source_before=self.source,purge_before=self.purge,marker_before=self.marker,history_before=self.history,data_before=self.data,store_identity=self.store,storage_hash=self.storage_hash)


if __name__=='__main__':unittest.main()
