"""Synthetic joins/negative fixtures only; never actual UI/identity/file acceptance."""
import copy
import json
import unittest
import uuid
from recycle_archive_browser_proof import managed_plan, public_receipt, receipt_join, job_summary, execution
from test_container_recycle_archive_rpc_proof import plan, request, receipt, rehash
from test_container_recycle_purge_browser_proof import managed as managed_purge
from test_container_recycle_purge_rpc_proof import rehash as rehash_purge
from container_recycle_archive_rpc_proof import proto_receipt

NAMES = ('Device', 'Inode', 'Mode', 'OwnerUid', 'OwnerGid', 'LinkCount', 'SizeBytes', 'ModifiedAtUnixNs')


def managed(value):
    p = copy.deepcopy(value); ref = p['command']['purge']
    result = {k[0].upper()+k[1:]: v for k, v in p.items() if k not in ('command', 'uid', 'gid')}
    result['Command'] = dict(Purge=dict(OperationId=str(uuid.UUID(ref['operationId'])), Owner=ref['owner'], Plan=managed_purge(ref['plan'])),
                             HistoryDigest=p['command']['historyDigest'], Mode=p['command']['mode'])
    result['OwnerUid'], result['OwnerGid'] = p['uid'], p['gid']
    for key in ('ParentIdentity', 'PrivateIdentity', 'StoreIdentity'):
        if result[key] is not None: result[key] = dict(zip(('Device', 'Inode'), result[key], strict=True))
    for key in ('DeletedIdentity', 'ArchiveIdentity'):
        values = result[key]; result[key] = dict(zip(NAMES, values[:8], strict=True)); result[key.replace('Identity', 'ChangedAtUnixNs')] = values[8]
    return result


def public(q, state='Completed'):
    wire = receipt(q, state, binding=True if state == 'Completed' else None)
    result = dict(id=str(uuid.UUID(wire['operationId'])), sourceOperationId=str(uuid.UUID(wire['sourceOperationId'])),
                  purgeOperationId=str(uuid.UUID(wire['purgeOperationId'])), fingerprint=wire['fingerprint'], state=state,
                  sourceFenced=wire['sourceFenced'], ownedFence=wire['ownedFence'], archiveCopied=wire.get('archiveCopied'),
                  bindingCurrent=wire.get('bindingCurrent'), publishedIdentity=None, publishedChangedAtUnixNs=None)
    if state == 'Completed':
        result['publishedIdentity'] = {k: (int(v) if k in ('mode', 'ownerUid', 'ownerGid', 'sizeBytes') else str(v)) for k, v in wire['publishedIdentity'].items()}
        result['publishedChangedAtUnixNs'] = str(wire['publishedChangedAtUnixNs'])
    return result


def rows():
    intent, purge, original = ('291a9ea0-e736-4f2c-a090-1cce1138fbb3', '491a9ea0-e736-4f2c-a090-1cce1138fbb4', '591a9ea0-e736-4f2c-a090-1cce1138fbb5')
    p = plan(); old = p['command']['purge']['plan']; old['command']['source']['operationId'] = uuid.UUID(execution(original, 'recycle')).hex; rehash_purge(old)
    p['command']['purge']['operationId'] = uuid.UUID(execution(purge, 'purge')).hex; rehash(p)
    row = dict(id=intent, source_intent_id=purge, execution_job_id=execution(intent), state='Succeeded', result_code='CONTAINER_RECYCLE_ARCHIVE_COMPLETED',
               command_json=json.dumps(managed(p)['Command']), root_started_at_utc='2026-11-07T00:00:00Z', reservation_json='reserved', apply_started_at_utc='2026-11-07T00:00:01Z',
               root_state='Completed', archive_copied=True, source_fenced=True, owned_fence=False, binding_current=False, publish_barrier=False)
    source = dict(id=purge, source_intent_id=original)
    summary = dict(schemaVersion=1,intentId=intent,jobId=execution(intent),sourceIntentId=purge,purgeOperationId=execution(purge,'purge'),originalIntentId=original,
                   sourceOperationId=execution(original,'recycle'),stage='execution',state='Succeeded',code='CONTAINER_RECYCLE_ARCHIVE_COMPLETED',receiptState='Completed',
                   reservationStarted=True,reservationRecorded=True,applyStarted=True,archiveCopied=True,sourceFenced=True,ownedFence=False,bindingCurrent=False,publishBarrier=False)
    return row, source, summary


class ArchiveBrowserProofTests(unittest.TestCase):
    def test_sql_plan_roundtrip_keeps_nullable_private_and_large_numbers(self):
        for private in (False, True):
            p = plan(private); self.assertEqual(p, managed_plan(managed(p)))
        p = plan(); p['storeIdentity'][0] = p['command']['purge']['plan']['storeIdentity'][0] = 2**64-1
        rehash_purge(p['command']['purge']['plan']); rehash(p); self.assertEqual(p, managed_plan(managed(p)))

    def test_unknown_sql_fields_at_every_layer_fail_closed(self):
        for path in ((), ('Command',), ('Command','Purge'), ('Command','Purge','Plan'), ('DeletedIdentity',), ('ArchiveIdentity',), ('StoreIdentity',)):
            value = managed(plan()); child = value
            for key in path: child = child[key]
            child['unexpected'] = True
            with self.subTest(path=path), self.assertRaises(AssertionError): managed_plan(value)

    def test_self_consistent_plan_changes_cannot_replace_the_source(self):
        for field in ('ArchiveBytes','DeletedChangedAtUnixNs','OwnerUid','Fingerprint'):
            value = managed(plan()); value[field] = False if field != 'Fingerprint' else '0'*64
            with self.subTest(field=field), self.assertRaises(AssertionError): managed_plan(value)

    def test_public_receipt_preserves_nullable_result_and_published_decimal_strings(self):
        q = request()
        for state in ('Completed','Aborted','Unknown','Reserved'):
            actual = public(q,state); self.assertEqual(public_receipt(actual,q),proto_receipt(receipt(q,state,binding=True if state=='Completed' else None),q))

    def test_decimal_coercion_or_duplicate_provenance_is_rejected(self):
        q = request()
        for value in (1.0, True, 1, '01', '+1', '1e3', '18446744073709551616'):
            r = public(q); r['publishedIdentity']['inode'] = value
            with self.subTest(value=value), self.assertRaises(AssertionError): public_receipt(r,q)
        r=public(q);r['purgeOperationId']=r['sourceOperationId']
        with self.assertRaises(AssertionError):public_receipt(r,q)

    def test_unknown_cannot_claim_false_copy_or_any_published_identity(self):
        q=request()
        for change in ('false','identity','time'):
            r=public(q,'Unknown')
            if change=='false':r['archiveCopied']=False
            if change=='identity':r['publishedIdentity']=public(q)['publishedIdentity']
            if change=='time':r['publishedChangedAtUnixNs']='1'
            with self.subTest(change=change),self.assertRaises(AssertionError):public_receipt(r,q)

    def test_sql_receipt_and_public_projection_must_be_exactly_joined(self):
        q=request();r=public(q);raw={k[0].upper()+k[1:]:v for k,v in r.items()}
        raw['PublishedIdentity']={k[0].upper()+k[1:]:(int(v) if k in ('device','inode','linkCount','modifiedAtUnixNs') else v) for k,v in r['publishedIdentity'].items()}
        raw['PublishedChangedAtUnixNs']=int(r['publishedChangedAtUnixNs']);row={'receipt_json':json.dumps(raw)}
        receipt_join(row,r,q);r['publishedIdentity']['inode']='5000'
        with self.assertRaises(AssertionError):receipt_join(row,r,q)

    def test_historical_completed_copy_and_original_planning_snapshot_are_distinct(self):
        row,source,summary=rows();job_summary(summary,row,source,execution_stage=True)
        planning={**summary,'jobId':row['id'],'stage':'planning','state':'Ready','code':'CONTAINER_RECYCLE_ARCHIVE_READY','receiptState':None,
                  'reservationStarted':False,'reservationRecorded':False,'applyStarted':False,'archiveCopied':None,'sourceFenced':None,'ownedFence':None,'bindingCurrent':None}
        job_summary(planning,row,source,execution_stage=False)

    def test_each_source_domain_and_summary_shape_cannot_be_replaced(self):
        for key in ('sourceIntentId','purgeOperationId','originalIntentId','sourceOperationId','jobId','extra'):
            row,source,summary=rows();summary[key]=row['id']
            with self.subTest(key=key),self.assertRaises(AssertionError):job_summary(summary,row,source,execution_stage=True)

    def test_unknown_missing_receipt_has_no_false_facts(self):
        row,source,summary=rows();row.update(state='Unknown',result_code='CONTAINER_RECYCLE_ARCHIVE_RESULT_UNKNOWN',root_state=None,archive_copied=None,source_fenced=None,owned_fence=None,binding_current=None,publish_barrier=True)
        summary.update(state='Unknown',code=row['result_code'],receiptState=None,archiveCopied=None,sourceFenced=None,ownedFence=None,bindingCurrent=None,publishBarrier=True)
        job_summary(summary,row,source,execution_stage=True);row['owned_fence']=summary['ownedFence']=False
        with self.assertRaises(AssertionError):job_summary(summary,row,source,execution_stage=True)


if __name__=='__main__':unittest.main()
