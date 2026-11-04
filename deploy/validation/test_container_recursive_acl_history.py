"""Real isolated Linux ACL histories; container/HDD authority providers are fixtures."""
import copy
import errno
import hashlib
import json
import os
import unittest
import uuid
from unittest.mock import patch

import test_container_file_recursive_acl as recursive
from test_container_file_staging import load

common=load('whaledeck-container-acl-history.py','recursive_history_common_candidate')
module=load('whaledeck-container-recursive-acl-history.py','recursive_history_candidate')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RecursiveAclHistoryTests(unittest.TestCase):
    def setUp(self):
        self.base=recursive.RecursiveAclTests();self.base.setUp();self.addCleanup(self.base.doCleanups)
        self.engine=self.base.engine;self.session=self.base.base.session
        self.catalog=module.recursive_acl_history_reader_type(common)(self.engine)

    def listing(self,**extra):
        return {**self.session.request,'path':'tree','after':'','take':50,'expectedVersion':'',**extra}

    def capture(self,kind='access',directory=None):
        request=self.base.request(kind,directory);receipt=self.engine.apply(request,self.session)
        self.assertEqual(receipt['state'],'Completed');return request,receipt

    def read(self,request,receipt,**extra):
        return {**self.session.request,'path':'tree','captureId':request['operationId'],'expectedHistoryDigest':receipt['historyDigest'],'offset':0,'take':50,**extra}

    def path(self,request):
        return self.base.base.disk.target/'container-recursive-acl-v1'/(request['operationId']+'.json')

    def snapshot(self):
        result={}
        for root in (self.base.base.root,self.base.state,self.base.base.disk.target):
            if not root.exists():continue
            for p in (root,*root.rglob('*')):
                if p.is_symlink():continue
                info=p.lstat();result[str(p)]=(tuple(self.base.base.r.fingerprint(info)),None if p.is_dir() else p.read_bytes())
        return result

    def test_empty_namespace_is_read_only_and_other_domains_are_rejected(self):
        before=self.snapshot();page=self.catalog.listing(self.listing(),self.session)
        self.assertTrue(page['complete']);self.assertEqual(page['entries'],[]);self.assertEqual(before,self.snapshot())
        self.assertFalse((self.base.base.disk.target/'container-recursive-acl-v1').exists())
        with self.assertRaises(ValueError):module.recursive_acl_history_reader_type(common)(self.base.base.engine)
        with self.assertRaises(ValueError):common.ContainerAclHistoryReader(self.engine)

    def test_all_original_heterogeneous_acls_are_returned_not_uniform_new_targets(self):
        fd=os.open(self.base.tree/'one.bin',os.O_RDONLY)
        try:recursive.single.acl.write_acl(fd,'access',recursive.single.named_acl(uid=2202,mask=6))
        finally:os.close(fd)
        original=self.base.snapshot();request,receipt=self.capture();before=self.snapshot()
        with self.base.no_write(),patch.object(self.base.base.f,'save_receipt',side_effect=AssertionError('read must not journal')):
            page=self.catalog.read(self.read(request,receipt),self.session);listing=self.catalog.listing(self.listing(),self.session)
        self.assertEqual(before,self.snapshot());self.assertEqual(page['manifestDigest'],request['plan']['manifestDigest']);self.assertEqual(len(listing['entries']),1)
        self.assertEqual([v['relativePath'] for v in page['entries']],['sub/中文.bin','one.bin','sub',''])
        for entry in page['entries']:
            saved=original[entry['relativePath']];self.assertEqual(entry['accessAcl'],saved['access']);self.assertEqual(entry['defaultAcl'],saved['default']);self.assertEqual(entry['originalMode'],saved['identity'][2]&0o777)
            self.assertNotIn('sourceIdentity',entry);self.assertNotIn('owner',entry);self.assertNotIn('plan',entry)
        self.assertNotEqual(page['entries'][0]['accessAcl'],page['entries'][1]['accessAcl'])
        page['entries'][0]['accessAcl'][0]['permissions']=0
        self.assertEqual(self.catalog.read(self.read(request,receipt),self.session)['entries'][0]['accessAcl'],original['sub/中文.bin']['access'])

    def test_default_and_explicit_clear_keep_original_both_acl_sets(self):
        original=self.base.snapshot();request,receipt=self.capture('default',[])
        page=self.catalog.read(self.read(request,receipt),self.session)
        self.assertEqual(page['changedAclType'],'default');self.assertEqual(page['changeCount'],2)
        for entry in page['entries']:
            old=original[entry['relativePath']];self.assertEqual(entry['accessAcl'],old['access']);self.assertEqual(entry['defaultAcl'],old['default'])

    def test_partial_operation_keeps_full_preimage_including_unstarted_children(self):
        original=self.base.snapshot();request=self.base.request()
        class Cut(BaseException):pass
        def stop_after(phase,index):
            if phase=='AfterEntry' and index==0:raise Cut()
        with patch.object(self.engine,'checkpoint',side_effect=stop_after),self.assertRaises(Cut):self.engine.apply(request,self.session)
        receipt=self.engine.inspect(request,self.session);self.assertEqual(receipt['state'],'Partial')
        page=self.catalog.read(self.read(request,receipt),self.session);self.assertEqual(len(page['entries']),4)
        for entry in page['entries']:self.assertEqual(entry['accessAcl'],original[entry['relativePath']]['access'])

    def test_old_instance_history_can_be_read_only_in_same_container_and_path(self):
        request,receipt=self.capture();old=request['plan']['command']['instanceId'];self.session.request['instanceId']='123:2026-11-04T00:00:00Z'
        result=self.catalog.read(self.read(request,receipt),self.session);self.assertEqual(result['sourceInstanceId'],old);self.assertNotEqual(result['instanceId'],old)
        for extra in ({'path':'another'},{'containerId':'d'*64},{'expectedHistoryDigest':'F'*64}):
            with self.subTest(extra=extra),self.assertRaises(self.base.base.r.Rejected):self.catalog.read(self.read(request,receipt,**extra),self.session)

    def test_capture_and_entry_pagination_are_version_digest_bound_without_duplicates(self):
        requests=[self.capture() for _ in range(3)];after='';version='';actual=[]
        for _ in range(3):
            page=self.catalog.listing(self.listing(after=after,take=1,expectedVersion=version),self.session);version=page['version'];actual.extend(e['captureId'] for e in page['entries'])
            if page['complete']:break
            after=page['nextAfter']
        self.assertEqual(actual,sorted(r[0]['operationId'] for r in requests))
        request,receipt=requests[0];offset=0;paths=[]
        for _ in range(4):
            page=self.catalog.read(self.read(request,receipt,offset=offset,take=1),self.session);paths.extend(e['relativePath'] for e in page['entries'])
            if page['complete']:break
            offset=page['nextOffset']
        self.assertEqual(paths,['sub/中文.bin','one.bin','sub','']);self.assertEqual(len(set(paths)),4)
        self.capture()
        with self.assertRaisesRegex(self.base.base.r.Rejected,'VERSION_CONFLICT'):self.catalog.listing(self.listing(expectedVersion=version),self.session)

    def test_read_bounds_and_missing_session_reject_before_any_snapshot(self):
        request,receipt=self.capture()
        for extra in ({'offset':True},{'offset':-1},{'offset':4},{'offset':10000},{'take':True},{'take':0},{'take':51},{'captureId':'../bad'},{'expectedHistoryDigest':''},{'path':'proc/self'},{'unexpected':1}):
            with self.subTest(extra=extra),self.assertRaises((self.base.base.r.Rejected,self.base.base.f.Rejected)):self.catalog.read(self.read(request,receipt,**extra),self.session)
        with self.assertRaisesRegex(self.base.base.r.Rejected,'SESSION_REQUIRED'):self.catalog.read(self.read(request,receipt),None)

    def test_manifest_corruption_cannot_return_a_partially_valid_page(self):
        request,receipt=self.capture();path=self.path(request);raw=path.read_bytes()
        for change in (lambda v:v.update(historyDigest='F'*64),lambda v:v.update(resourceType='ContainerAclMetadata'),lambda v:v['entries'][0]['plan']['accessAcl'][0].update(permissions=0),lambda v:v['entries'].reverse(),lambda v:v.update(schemaVersion=True)):
            value=json.loads(raw);change(value);path.write_text(json.dumps(value))
            with self.subTest(value=value),self.assertRaises((self.base.base.r.Rejected,self.base.base.f.Rejected)):self.catalog.read(self.read(request,receipt),self.session)
            listing=self.catalog.listing(self.listing(),self.session);self.assertEqual(listing['entries'],[]);self.assertEqual(listing['invalidCount'],1)
        path.write_bytes(raw)

    def test_topology_rejects_missing_or_wrong_parent_and_parent_version_even_if_resigned(self):
        request,receipt=self.capture();value=self.engine.history.load(request)
        for change in (lambda v:v['entries'][0].update(relativePath='absent/file'),lambda v:v['entries'][0]['plan'].update(parentIdentity=[1,2]),lambda v:v['entries'][0]['plan'].update(parentVersion='F'*64),lambda v:v['entries'][2]['plan'].update(kind='File')):
            bad=copy.deepcopy(value);change(bad)
            with self.subTest(value=bad),self.assertRaises(self.base.base.r.Rejected):self.catalog.topology(bad)

    def test_self_consistent_hashes_cannot_authorize_a_manifest_with_a_missing_parent(self):
        request,receipt=self.capture();value=self.engine.history.load(request)
        entry=value['entries'][0];entry['relativePath']='absent/child.bin';child=entry['plan'];child['command']['path']='tree/absent/child.bin'
        child['command']['expectedVersion']=hashlib.sha256(b'whaledeck-container-file-v1\n'+json.dumps(
            [child['command']['containerId'],child['command']['instanceId'],child['command']['path'],*child['sourceIdentity']],ensure_ascii=False,separators=(',',':')).encode()).hexdigest().upper()
        child['fingerprint']=hashlib.sha256(b'whaledeck-container-acl-plan-v1\n'+common.encode({k:v for k,v in child.items() if k!='fingerprint'})).hexdigest().upper()
        value['plan']['manifestDigest']=self.engine.manifest_digest(value['entries']);value['plan']['fingerprint']=self.engine.digest(value['plan'])
        value['historyDigest']=hashlib.sha256(self.engine.history.DOMAIN+common.encode({k:v for k,v in value.items() if k!='historyDigest'})).hexdigest().upper()
        # Existing plan/hash validation accepts this; the reader must also
        # require every nested path's actual parent in the captured manifest.
        self.engine.request({k:value[k] for k in ('operationId','owner','plan')});self.engine.validate_manifest(value,value)
        self.path(request).write_text(json.dumps(value))
        with self.assertRaisesRegex(self.base.base.r.Rejected,'HISTORY_INVALID'):
            self.catalog.read(self.read(request,{**receipt,'historyDigest':value['historyDigest']}),self.session)

    def test_same_length_mutation_during_entry_projection_rejects_instead_of_success(self):
        request,receipt=self.capture();path=self.path(request);raw=path.read_bytes();project=self.catalog.entry
        def changed(value):
            result=project(value);path.write_bytes(raw);return result
        with patch.object(self.catalog,'entry',side_effect=changed),self.assertRaisesRegex(self.base.base.r.Rejected,'VERSION_CONFLICT'):self.catalog.read(self.read(request,receipt),self.session)

    def test_metadata_links_permissions_and_mount_failures_are_never_empty_success(self):
        request,receipt=self.capture();path=self.path(request);raw=path.read_bytes()
        for mode in ('symlink','hardlink','permissions'):
            path.unlink()
            if mode=='symlink':path.symlink_to(self.base.tree/'one.bin')
            elif mode=='hardlink':os.link(self.base.tree/'one.bin',path)
            else:path.write_bytes(raw);path.chmod(0o644)
            with self.subTest(mode=mode),self.assertRaises((self.base.base.r.Rejected,self.base.base.f.Rejected,OSError)):self.catalog.read(self.read(request,receipt),self.session)
        path.unlink();path.write_bytes(raw);path.chmod(0o600)
        for error in (OSError(errno.EIO,'fixture I/O'),self.base.base.f.Rejected('FILES_BACKUP_DEVICE_MISMATCH')):
            with patch.object(self.engine.history.storage,'attest',side_effect=error),self.assertRaises(type(error)):self.catalog.listing(self.listing(),self.session)

    def test_response_budget_and_deadline_are_bounded(self):
        request,receipt=self.capture();before=self.snapshot()
        with patch.object(common,'RESPONSE_BYTES',1800):
            page=self.catalog.read(self.read(request,receipt),self.session);self.assertLessEqual(len(common.encode(page)),1800);self.assertFalse(page['complete']);self.assertGreater(page['nextOffset'],0)
        original=self.catalog.metadata
        def stalled(*args,**kwargs):
            value=original(*args,**kwargs);clock[0]=6.;return value
        clock=[0.]
        with patch.object(common.time,'monotonic',side_effect=lambda:clock[0]),patch.object(self.catalog,'metadata',side_effect=stalled),self.assertRaisesRegex(self.base.base.r.Rejected,'TIMEOUT'):self.catalog.read(self.read(request,receipt),self.session)
        self.assertEqual(before,self.snapshot())
