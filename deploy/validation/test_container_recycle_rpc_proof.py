"""Wire negatives plus real temporary I/O with explicit namespace/HDD attestation doubles."""
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import unittest
import uuid
from container_recycle_rpc_proof import proto_plan,proto_receipt,ready_proof,plan_frame,receipt_frame,record_proof,capability_refusal,native_plan_proof,PATH,FRAME,digest,file_version
import test_container_file_recycling as native


def wire_plan(plan):
    p=copy.deepcopy(plan);p['command']=dict(location={k:p['command'][k] for k in ('containerId','instanceId','path')},expectedVersion=p['command']['expectedVersion'])
    p['ownerUid']=p.pop('uid');p['ownerGid']=p.pop('gid');identity=p['sourceIdentity']
    p['sourceIdentity']={k:v if k in ('mode','ownerUid','ownerGid') else str(v) for k,v in zip(FRAME[:8],identity[:8],strict=True)}
    p['sourceChangedAtUnixNs']=str(identity[8]);p['parentIdentity']=dict(zip(('device','inode'),map(str,p['parentIdentity']),strict=True))
    p['attributes']=[dict(name=n,value=v) for n,v in p['attributes']];return p


def plan():
    c=dict(containerId='a'*64,instanceId='777:2026-11-05T01:02:03Z',path=PATH)
    identity=[2049,45,0o100640,1000,1000,1,3,-1,2]
    c['expectedVersion']=file_version(c,PATH,identity)
    p=dict(schemaVersion=1,command=c,targetProof='A'*64,parentIdentity=[2049,41],parentVersion='B'*64,uid=1000,gid=1000,
        sourceIdentity=identity,sha256=hashlib.sha256(b'abc').hexdigest().upper(),attributes=[['user.note',base64.b64encode(b'bytes').decode()]])
    p['fingerprint']=digest(b'whaledeck-container-recycle-plan-v1\n',p);return p


class RecycleWireProofTests(unittest.TestCase):
    def test_transport_or_other_startup_failures_are_not_capability_refusals(self):
        e=dict(terminalProbe='failed',phase='capabilities',code='CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE',failureType='InvalidOperationException',rpcStatus=None,fileCode=None)
        self.assertEqual(capability_refusal(1,'',e)['operationFramesSent'],0)
        for key,bad in [('code',''),('rpcStatus','Unavailable'),('failureType','RpcException'),('phase','startup'),('fileCode','CONTAINER_RECYCLE_UNAVAILABLE')]:
            changed={**e,key:bad}
            with self.subTest(key=key),self.assertRaises(AssertionError):capability_refusal(1,'',changed)

    def test_complete_plan_roundtrip_and_domain_hash(self):
        p=plan();self.assertEqual(proto_plan(wire_plan(p)),p)
        changed=wire_plan(p);changed['fingerprint']='C'*64
        with self.assertRaises(AssertionError):proto_plan(changed)

    def test_native_record_plan_does_not_skip_strict_validation_when_rehashed(self):
        native_plan_proof(plan())
        for kind in ('string-size','boolean-owner','extra'):
            p=plan()
            if kind=='string-size':p['sourceIdentity'][6]='3'
            elif kind=='boolean-owner':p['uid']=True
            else:p['hostPath']='/etc'
            p['command']['expectedVersion']=file_version(p['command'],PATH,p['sourceIdentity'])
            p['fingerprint']=digest(b'whaledeck-container-recycle-plan-v1\n',{k:v for k,v in p.items() if k!='fingerprint'})
            with self.subTest(kind=kind),self.assertRaises(AssertionError):native_plan_proof(p)

    def test_exact_fields_and_zero_presence_cannot_be_silently_dropped(self):
        for path in ((),('command',),('command','location'),('sourceIdentity',),('parentIdentity',)):
            source=wire_plan(plan());node=source
            for key in path:node=node[key]
            for key in node:
                changed=copy.deepcopy(source);target=changed
                for k in path:target=target[k]
                del target[key]
                with self.subTest(path=path,key=key),self.assertRaises(AssertionError):proto_plan(changed)
        p=wire_plan(plan());p['hostPath']='/etc'
        with self.assertRaises(AssertionError):proto_plan(p)

    def test_numeric_types_size_links_modes_and_source_version_are_bound(self):
        for key,value in [('sizeBytes','1073741825'),('sizeBytes',True),('inode','00'),('linkCount','2'),('mode',0o104640),('ownerUid','1000'),('modifiedAtUnixNs','-0')]:
            p=wire_plan(plan());p['sourceIdentity'][key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):proto_plan(p)
        p=wire_plan(plan());p['sourceChangedAtUnixNs']='3'
        with self.assertRaises(AssertionError):proto_plan(p)

    def test_fixed_scope_canonical_attributes_and_limits(self):
        for path in ('../escape','work/other','work/recycle-rpc.bin/child'):
            p=wire_plan(plan());p['command']['location']['path']=path
            with self.subTest(path=path),self.assertRaises(AssertionError):proto_plan(p)
        for attrs in ([dict(name='security.capability',value='')],[dict(name='user.x',value='YQ==\n')],[dict(name='user.x',value='')]*2,[dict(name='user.x',value=base64.b64encode(b'x'*4096).decode())]):
            p=wire_plan(plan());p['attributes']=attrs
            with self.assertRaises((AssertionError,ValueError)):proto_plan(p)

    def test_minor56_and_exact_revisions_precede_real_operation_claim(self):
        h=dict(revision='a'*40,probeRevision='b'*40,containerId='c'*64,instanceId='777:2026-11-05T01:02:03Z',coreId='d'*64)
        ready_proof(dict(kind='ready',revision=h['revision'],probeRevision=h['probeRevision'],protocolMinor=56),h)
        for minor in (55,False,'56',56.0):
            with self.assertRaises(AssertionError):ready_proof(dict(kind='ready',revision=h['revision'],probeRevision=h['probeRevision'],protocolMinor=minor),h)

    def test_restore_frame_requires_new_job_and_other_frames_bind_original(self):
        q=dict(operationId='a'*32,owner='B'*64,plan=plan())
        r=dict(operationId=q['operationId'],containerId=q['plan']['command']['containerId'],instanceId=q['plan']['command']['instanceId'],fingerprint=q['plan']['fingerprint'],targetProof=q['plan']['targetProof'],state='Unknown',bindingCurrent=False,replayed=True,historyMayExist=True,historyVerified=False,historyDigest='',historyAvailable=False)
        p=dict(kind='receipt',action='restore',operationId=q['operationId'],jobId='c'*32,result=json.dumps(r));receipt_frame(p,q)
        p['jobId']=q['operationId']
        with self.assertRaises(AssertionError):receipt_frame(p,q)
        p['action']='apply';receipt_frame(p,q)
        p['jobId']='c'*32
        with self.assertRaises(AssertionError):receipt_frame(p,q)
        p['jobId']=q['operationId'];p['result']='{}'
        with self.assertRaises(AssertionError):receipt_frame(p,q)
        p['result']=json.dumps(r)[:-1]+',"state":"Unknown"}'
        with self.assertRaises(AssertionError):receipt_frame(p,q)

    def test_boundary_refusals_are_exact_and_bound_to_this_container(self):
        p=plan();h=p['command'];v=dict(kind='plan',plan=json.dumps(wire_plan(p)),boundaryRefusals=17)
        self.assertEqual(plan_frame(v,h,boundaries=True),p)
        for count in (16,18,True):
            v['boundaryRefusals']=count
            with self.assertRaises(AssertionError):plan_frame(v,h,boundaries=True)

    def test_probe_source_has_no_mutation_fallback_and_requires_current_plan_registration(self):
        root=Path(__file__).parent;code=(root/'TerminalProbe/ContainerRecycleProbe.cs').read_text()
        for forbidden in ('Process.Start','File.WriteAll','docker exec','"/data/'):
            self.assertNotIn(forbidden,code)
        self.assertLess(code.index('caps.ProtocolMinor>=56'),code.index('File.ReadAllBytesAsync'))
        for required in ('plans.TryGetValue','supplied.Equals(approved)','operation.Plan.Equals(supplied)','CryptographicOperations.ZeroMemory(key)','restores.TryGetValue','current.Context.JobId!=operationId'):
            self.assertIn(required,code)
        self.assertIn('"container-recycling"',(root/'TerminalProbe/Program.cs').read_text())


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'real temporary root I/O required')
class RecycleRecordProofTests(unittest.TestCase):
    def setUp(self):
        self.f=native.LinuxContainerRecycleTests();self.f.setUp();self.addCleanup(self.f.doCleanups)
        folder=self.f.root/'work';folder.mkdir();self.f.leaf=self.f.leaf.rename(folder/'recycle-rpc.bin')
        c={**self.f.session.request,'path':PATH,'expectedVersion':self.f.r.version(self.f.session.request,PATH,self.f.leaf.stat())}
        self.q=dict(operationId=uuid.uuid4().hex,owner='B'*64,plan=self.f.engine.plan(c,self.f.session))
        self.assertEqual(proto_plan(wire_plan(self.q['plan'])),self.q['plan'])
        self.receipt=self.f.engine.apply(self.q,self.f.session)

    def prove(self,receipt=None,root=None,history=None):
        r=receipt or self.receipt;self.assertEqual(proto_receipt({k:v for k,v in r.items() if v is not None},self.q),r)
        data=self.f.target/(self.q['operationId']+'.data')
        return record_proof(self.q,r,root or self.f.record(self.q),history or self.f.saved(self.q),data_sha256=hashlib.sha256(data.read_bytes()).hexdigest().upper(),data_size=data.stat().st_size)

    def test_actual_trash_and_restore_originals_are_distinct_from_namespace_proof(self):
        self.assertEqual(self.prove()['state'],'Trashed')
        self.receipt=self.f.engine.restore(self.q,self.f.session)
        self.assertEqual(self.prove()['state'],'Restored');self.assertFalse(self.prove()['physicalNamespaceChecked'])

    def test_root_identity_owner_plan_and_history_cannot_be_replaced(self):
        for key,bad in [('owner','C'*64),('operationId','c'*32),('historyDigest','D'*64),('actualIdentity',None),('state','Restored')]:
            r=self.f.record(self.q);r[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(root=r)

    def test_overwrite_history_wrong_state_or_payload_is_not_trash_evidence(self):
        for key,bad in [('resourceType','ContainerFile'),('captureKind','Overwrite'),('captureState','Pending'),('state','Preparing'),('sizeBytes',True),('sha256','E'*64)]:
            h=self.f.saved(self.q);h[key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(history=h)

    def test_unknown_wire_does_not_allow_false_or_explicit_null_presence(self):
        r={**self.receipt,'state':'Unknown','bindingCurrent':False};r.pop('sourceRetained');r.pop('sourceRestored')
        normalized=proto_receipt(r,self.q);self.assertIsNone(normalized['sourceRetained'])
        for value in (False,None,True):
            with self.assertRaises(AssertionError):proto_receipt({**r,'sourceRetained':value},self.q)


if __name__=='__main__':unittest.main()
