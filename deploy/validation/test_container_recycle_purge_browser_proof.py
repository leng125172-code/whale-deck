"""Pure negative/roundtrip fixtures, not actual identity, disk or UI acceptance."""
import copy
import unittest
import uuid
from recycle_purge_browser_proof import execution, managed_plan, managed_receipt, job_summary, retained_history
from container_recycle_purge_rpc_proof import digest
from test_container_recycle_purge_rpc_proof import plan, rehash, request, receipt
from test_container_recycle_browser_scope import managed as old_managed


def managed(p):
    value = {k[0].upper()+k[1:]: v for k, v in p.items() if k not in ('command', 'uid', 'gid')}
    s = p['command']['source']
    value['Command'] = dict(Source=dict(OperationId=str(uuid.UUID(s['operationId'])), Owner=s['owner'], Plan=old_managed(s['plan'])), HistoryDigest=p['command']['historyDigest'], Scope=p['command']['scope'])
    value['OwnerUid'], value['OwnerGid'] = p['uid'], p['gid']
    for k in ('ParentIdentity', 'PrivateIdentity', 'StoreIdentity'): value[k] = dict(zip(('Device', 'Inode'), value[k], strict=True))
    names = ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs')
    for k in ('RetainedIdentity', 'ArchiveIdentity'):
        frame = value[k]; value[k] = dict(zip(names, frame[:8], strict=True)); value[k.replace('Identity', 'ChangedAtUnixNs')] = frame[8]
    return value


def row(state='Succeeded'):
    intent, src = '291a9ea0-e736-4f2c-a090-1cce1138fbb3', '491a9ea0-e736-4f2c-a090-1cce1138fbb4'
    r = dict(id=intent, source_intent_id=src, execution_job_id=execution(intent), state=state, result_code='CONTAINER_RECYCLE_PURGE_COMPLETED',
        root_started_at_utc='2026-11-06T01:02:03Z', reservation_json={'state': 'Reserved'}, apply_started_at_utc='2026-11-06T01:02:04Z',
        root_state='Completed', purge_applied=True, source_fenced=True, owned_fence=True, publish_barrier=False)
    if state in ('Unknown', 'Acknowledged'):
        r.update(result_code='CONTAINER_RECYCLE_PURGE_'+('RESULT_UNKNOWN' if state=='Unknown' else 'UNKNOWN_ACKNOWLEDGED'), root_state='Unknown', purge_applied=None, publish_barrier=state=='Unknown')
    if state in ('Canceled', 'Failed'):
        r.update(result_code='CONTAINER_RECYCLE_PURGE_'+('CANCELED' if state=='Canceled' else 'INTERRUPTED'), root_state='Aborted', purge_applied=False, source_fenced=False, owned_fence=False)
    if state in ('Reserving', 'Applying'):
        r.update(result_code='CONTAINER_RECYCLE_PURGE_ACCEPTED', root_state=None, purge_applied=None, source_fenced=None, owned_fence=None, publish_barrier=True)
        if state=='Reserving': r['apply_started_at_utc']=None
    return r


def summary(r, execution_stage=True):
    state = r['state'] if execution_stage else 'Ready'
    code = r['result_code'] if execution_stage else 'CONTAINER_RECYCLE_PURGE_READY'
    if state=='Reserving': code='CONTAINER_RECYCLE_PURGE_'+('RESERVED' if r['reservation_json'] is not None else 'RESERVING')
    if state=='Applying': code='CONTAINER_RECYCLE_PURGE_APPLYING'
    return dict(schemaVersion=1,intentId=r['id'],jobId=execution(r['id']) if execution_stage else r['id'],sourceIntentId=r['source_intent_id'],sourceOperationId=execution(r['source_intent_id'],source=True),stage='execution' if execution_stage else 'planning',state=state,code=code,
        receiptState=r['root_state'] if execution_stage else None,reservationStarted=execution_stage and r['root_started_at_utc'] is not None,reservationRecorded=execution_stage and r['reservation_json'] is not None,applyStarted=execution_stage and r['apply_started_at_utc'] is not None,
        purgeApplied=r['purge_applied'] if execution_stage else None,sourceFenced=r['source_fenced'] if execution_stage else None,ownedFence=r['owned_fence'] if execution_stage else None,publishBarrier=r['publish_barrier'] if execution_stage else False)


def hdd():
    p=plan()['command']['source']['plan']
    h=dict(schemaVersion=1,resourceType='ContainerFileTrash',storageId='database-platform.backup.hdd',operationId='a'*32,owner='B'*64,plan=p,sizeBytes=3,sha256=p['sha256'],captureKind='Trash',state='Verified',captureState='Trashed',createdAtUnixMs=1,differentSourceDevice=False)
    h['historyDigest']=digest(b'whaledeck-container-recycle-history-v1\n',{k:v for k,v in h.items() if k not in ('state','captureState','historyDigest')})
    return dict(history=h,data=dict(identity=[2049,62,33152,0,0,1,3,2,3],attributes=[],sha256=p['sha256'],sizeBytes=3),storeIdentity=[2049,61],storageHash='F'*64)


class BrowserProofTests(unittest.TestCase):
    def test_managed_plan_roundtrip_preserves_large_unsigned_and_nanosecond_values(self):
        p=plan();p['storeIdentity']=[0,2**64-1];p['archiveIdentity'][0]=2**64-1;p['archiveIdentity'][7]=-2**63;p['archiveIdentity'][8]=2**63-1;rehash(p)
        self.assertEqual(p,managed_plan(managed(p)))

    def test_unknown_nested_managed_fields_cannot_be_silently_discarded(self):
        for path in ((),('Command',),('Command','Source'),('Command','Source','Plan'),('RetainedIdentity',),('ArchiveIdentity',),('StoreIdentity',)):
            m=managed(plan());node=m
            for key in path:node=node[key]
            node['privatePath']='/etc'
            with self.subTest(path=path),self.assertRaises(AssertionError):managed_plan(m)

    def test_numeric_coercion_and_substituted_source_are_not_a_plan(self):
        for key,value in (('ArchiveBytes',True),('RetainedChangedAtUnixNs',1.0),('OwnerUid',False),('Fingerprint','0'*64)):
            m=managed(plan());m[key]=value
            with self.subTest(key=key),self.assertRaises(AssertionError):managed_plan(m)

    def test_managed_nullable_receipt_keeps_original_operation_and_scope(self):
        q=request()
        for state in ('Completed','Aborted','Unknown'):
            native=receipt(q,state);dto={**native,'id':str(uuid.UUID(native['operationId'])),'sourceOperationId':str(uuid.UUID(native['sourceOperationId'])),'purgeApplied':native.get('purgeApplied')};dto.pop('operationId')
            self.assertEqual({**native,'purgeApplied':native.get('purgeApplied')},managed_receipt(dto,q))
            for patch in (dict(id=dto['sourceOperationId']),dict(sourceOperationId=dto['id']),dict(purgeApplied='true'),dict(privatePath='/etc')):
                with self.subTest(state=state,patch=patch),self.assertRaises(AssertionError):managed_receipt({**dto,**patch},q)

    def test_execution_domains_and_little_endian_guids_are_distinct(self):
        r=row();self.assertNotEqual(execution(r['id']),execution(r['id'],source=True))
        for bad in (uuid.UUID(r['id']).hex,r['id'].upper(),'00000000-0000-0000-0000-000000000000'):
            with self.subTest(bad=bad),self.assertRaises((AssertionError,ValueError)):execution(bad)

    def test_planning_stays_ready_when_intent_is_later_canceled_or_executed(self):
        for state in ('Canceled','Succeeded'):
            r=row(state);v=summary(r,False);self.assertEqual(v,job_summary(v,r,execution_stage=False))
            with self.assertRaises(AssertionError):job_summary({**v,'purgeApplied':False},r,execution_stage=False)

    def test_preparation_and_apply_do_not_invent_a_deleted_or_retained_outcome(self):
        for state in ('Reserving','Applying'):
            r=row(state);v=summary(r);self.assertEqual(v,job_summary(v,r,execution_stage=True))
            with self.assertRaises(AssertionError):job_summary({**v,'purgeApplied':False},r,execution_stage=True)
            broken={**r,'root_started_at_utc':None}
            with self.assertRaises(AssertionError):job_summary(summary(broken),broken,execution_stage=True)

    def test_terminal_and_unknown_facts_match_original_sql_without_false_defaults(self):
        for state in ('Succeeded','Canceled','Failed','Unknown','Acknowledged'):
            r=row(state);v=summary(r);self.assertEqual(v,job_summary(v,r,execution_stage=True))
            for patch in (dict(sourceOperationId=v['jobId']),dict(jobId=v['intentId']),dict(purgeApplied=False if v['purgeApplied'] is None else None),dict(privatePath='/etc')):
                with self.subTest(state=state,patch=patch),self.assertRaises(AssertionError):job_summary({**v,**patch},r,execution_stage=True)

    def test_no_receipt_unknown_can_only_hold_null_facts(self):
        r=row('Unknown');r.update(root_state=None,source_fenced=None,owned_fence=None);v=summary(r);job_summary(v,r,execution_stage=True)
        with self.assertRaises(AssertionError):job_summary({**v,'sourceFenced':False},r,execution_stage=True)

    def test_late_completed_proof_does_not_turn_acknowledgement_into_success(self):
        r=row();r.update(state='Acknowledged',result_code='CONTAINER_RECYCLE_PURGE_UNKNOWN_ACKNOWLEDGED');v=summary(r)
        job_summary(v,r,execution_stage=True)
        with self.assertRaises(AssertionError):job_summary({**v,'state':'Succeeded'},r,execution_stage=True)

    def test_post_cleanup_hdd_requires_full_independently_acquired_equality(self):
        before=hdd();self.assertTrue(retained_history(before,copy.deepcopy(before))['hddHistoryAndFullDataSnapshotEqual'])
        for kind in ('inode','bytes','attributes','store','storage','metadata'):
            after=copy.deepcopy(before)
            if kind=='inode':after['data']['identity'][1]+=1
            elif kind=='bytes':after['data']['sha256']='0'*64
            elif kind=='attributes':after['data']['attributes']=[['user.changed','eA==']]
            elif kind=='store':after['storeIdentity'][1]+=1
            elif kind=='storage':after['storageHash']='0'*64
            else:after['history']['createdAtUnixMs']+=1
            with self.subTest(kind=kind),self.assertRaises(AssertionError):retained_history(before,after)

    def test_empty_matching_objects_cannot_masquerade_as_retained_hdd(self):
        bad=dict(history={},data={},storeIdentity=[0,0],storageHash='0'*64)
        with self.assertRaises(AssertionError):retained_history(bad,copy.deepcopy(bad))

    def test_matching_invalid_frames_or_attributes_are_still_refused(self):
        for kind in ('inode', 'store', 'bool', 'attributes'):
            bad=hdd()
            if kind=='inode':bad['data']['identity'][1]=0
            elif kind=='store':bad['storeIdentity'][1]=0
            elif kind=='bool':bad['data']['identity'][3]=False
            else:bad['data']['attributes']=[['user.note',123]]
            with self.subTest(kind=kind),self.assertRaises(AssertionError):retained_history(bad,copy.deepcopy(bad))


if __name__=='__main__':unittest.main()
