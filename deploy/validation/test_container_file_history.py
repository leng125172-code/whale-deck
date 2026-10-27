"""Real Linux private-file I/O with explicitly synthetic mount/namespace policy.

No RPC, production HDD, caller authority or UI completion is claimed here.
The existing capture fixture is composed, not imported as collected TestCases.
"""
import copy
import hashlib
import json
import os
import stat
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

import test_container_file_backups as backup_tests
import test_container_file_staging as staging

candidate=staging.load('whaledeck-container-history.py','container_history_candidate')


@unittest.skipUnless(os.name=='posix' and hasattr(os,'geteuid') and os.geteuid()==0,'requires isolated Linux root')
class LinuxContainerHistoryTests(unittest.TestCase):
    def setUp(self):
        self.fixture=backup_tests.LinuxContainerBackupTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.engine=candidate.ContainerHistory(self.fixture.upload,self.fixture.storage)
        self.session=self.fixture.session;self.target=self.fixture.target
        self.scope=None

    def capture(self):
        _,scope=self.fixture.stage();self.fixture.engine.capture(scope,self.session);self.scope=scope
        return scope['operationId']

    def metadata(self,identifier):return json.loads((self.target/(identifier+'.json')).read_bytes())

    def put(self,identifier,value):
        path=self.target/(identifier+'.json');path.write_text(json.dumps(value,ensure_ascii=False));path.chmod(0o600)

    def listing(self,**extra):
        return self.engine.listing({**self.session.request,'path':'','after':'','take':50,'expectedVersion':'',**extra},self.session)

    def source(self,identifier,**extra):
        return self.engine.verified_source({**self.session.request,'path':self.fixture.leaf.name,'captureId':identifier,
            'expectedCaptureHash':self.engine.digest('capture',self.metadata(identifier)),**extra},self.session)

    def test_full_binary_source_is_verified_rewound_private_and_never_published(self):
        identifier=self.capture();before=self.fixture.r.fingerprint(self.fixture.leaf.stat())
        with self.source(identifier) as (value,descriptor):
            actual=bytearray()
            while chunk:=os.read(descriptor,32768):actual.extend(chunk)
            self.assertEqual(bytes(actual),self.fixture.original)
            self.assertEqual(value['sha256'],hashlib.sha256(actual).hexdigest().upper())
            self.assertEqual(os.fstat(descriptor).st_mode&0o777,0o600)
        with self.assertRaises(OSError):os.fstat(descriptor)
        self.assertEqual(self.fixture.r.fingerprint(self.fixture.leaf.stat()),before)
        self.assertEqual(self.fixture.leaf.read_bytes(),self.fixture.original)
        self.assertEqual(self.fixture.upload.inspect(self.scope,self.session)['state'],'Staged')

    def test_empty_capture_is_a_verified_empty_file_not_an_incomplete_placeholder(self):
        self.fixture.leaf.write_bytes(b'');identifier=self.capture()
        self.assertEqual(self.listing()['entries'][0]['sizeBytes'],0)
        with self.source(identifier) as (value,descriptor):
            self.assertEqual(value['sha256'],hashlib.sha256(b'').hexdigest().upper())
            self.assertEqual(os.read(descriptor,1),b'')

    def test_history_reads_accept_read_only_mount_but_capture_writers_stay_closed(self):
        identifier=self.capture();original_meta=(self.target/(identifier+'.json')).read_bytes()
        with patch.object(os,'fstatvfs',return_value=SimpleNamespace(f_flag=os.ST_RDONLY)):
            with self.assertRaisesRegex(self.fixture.f.Rejected,'FILES_BACKUP_READ_ONLY'):
                with self.fixture.storage.target():self.fail('writer must not open read-only target')
            self.assertEqual(self.listing()['entries'][0]['captureId'],identifier)
            with self.source(identifier) as (_,descriptor):
                actual=bytearray()
                while chunk:=os.read(descriptor,32768):actual.extend(chunk)
                self.assertEqual(bytes(actual),self.fixture.original)
            self.fixture.disk.udev_record.write_text('E:ID_FS_UUID=00000000-0000-0000-0000-000000000002\nE:ID_FS_USAGE=filesystem\n')
            with self.assertRaisesRegex(self.fixture.f.Rejected,'FILES_BACKUP_DEVICE_MISMATCH'):self.listing()
        self.assertEqual((self.target/(identifier+'.json')).read_bytes(),original_meta)

    def test_inventory_does_not_expose_original_owner_inode_or_host_aliases(self):
        identifier=self.capture();page=self.listing();self.assertTrue(page['complete'])
        entry=page['entries'][0]
        self.assertEqual(entry['captureId'],identifier);self.assertEqual(entry['availability'],'Present')
        self.assertEqual(entry['originalInstanceId'],staging.INSTANCE)
        self.assertEqual(set(entry),{'captureId','containerId','originalInstanceId','path','sourceVersion','sizeBytes','sha256',
            'createdAtUnixMs','differentSourceDevice','availability','captureHash'})
        self.assertEqual(entry['captureHash'],self.engine.digest('capture',self.metadata(identifier)))

    def test_current_restart_can_read_same_container_old_instance_capture(self):
        identifier=self.capture();old=self.session.request['instanceId']
        self.session.request={**self.session.request,'instanceId':'779:2026-10-27T01:02:03.987654321Z'}
        self.session.identity=('779',777,999)
        self.assertEqual(self.listing()['entries'][0]['originalInstanceId'],old)
        with self.source(identifier) as (_,descriptor):self.assertEqual(os.read(descriptor,100),self.fixture.original[:100])

    def test_original_capture_owner_is_not_the_current_admin_identity(self):
        identifier=self.capture();value=self.metadata(identifier);value['owner']='F'*64;self.put(identifier,value)
        with self.source(identifier) as (value,descriptor):
            self.assertEqual(value['owner'],'F'*64);self.assertEqual(os.read(descriptor,32),self.fixture.original[:32])

    def test_foreign_container_capture_is_never_listed_or_opened(self):
        identifier=self.capture();value=self.metadata(identifier);value['containerId']='d'*64;self.put(identifier,value)
        self.assertEqual(self.listing()['entries'],[])
        with self.assertRaisesRegex(self.fixture.r.Rejected,'SCOPE_MISMATCH'):
            with self.source(identifier):self.fail('foreign content exposed')

    def test_protected_record_names_are_hidden_even_in_an_unfiltered_inventory(self):
        identifier=self.capture();value=self.metadata(identifier);value['path']='work/.env';self.put(identifier,value)
        page=self.listing();self.assertEqual(page['entries'],[]);self.assertEqual(page['invalidRecords'],0)
        with self.assertRaisesRegex(self.fixture.r.Rejected,'PROTECTED'):
            with self.source(identifier,path='work/.env'):self.fail('secret capture exposed')

    def test_wrong_current_scope_target_proof_and_protected_paths_fail(self):
        identifier=self.capture()
        for changed in ({'containerId':'d'*64},{'instanceId':'779:2026-10-27T01:02:03Z'},{'path':'.env'},
                        {'path':'proc/1/environ'},{'path':'work/../private'}):
            with self.subTest(changed=changed),self.assertRaises(self.fixture.r.Rejected):self.listing(**changed)
        original=self.session.current
        self.session.current=lambda:setattr(self.session,'mounts',b'N'*32)
        try:
            with self.assertRaisesRegex(self.fixture.r.Rejected,'TARGET_CHANGED'):
                with self.source(identifier):self.fail('changed namespace exposed')
        finally:self.session.current=original

    def test_cursor_is_bound_to_original_container_instance_filter_and_snapshot(self):
        first=self.capture();self.capture();page=self.listing(take=1)
        self.assertFalse(page['complete']);self.assertTrue(page['nextCursor'])
        second=self.listing(take=1,after=page['nextCursor'],expectedVersion=page['version'])
        self.assertTrue(second['complete']);self.assertNotEqual(second['entries'][0]['captureId'],page['entries'][0]['captureId'])
        for changed in ({'path':self.fixture.leaf.name},{'expectedVersion':'A'*64}):
            with self.subTest(changed=changed),self.assertRaisesRegex(self.fixture.r.Rejected,'VERSION_CONFLICT'):
                self.listing(**{'after':page['nextCursor'],'expectedVersion':page['version'],**changed})
        with self.assertRaisesRegex(self.fixture.r.Rejected,'VERSION_REQUIRED'):self.listing(after=first)
        self.capture()
        with self.assertRaisesRegex(self.fixture.r.Rejected,'VERSION_CONFLICT'):
            self.listing(after=page['nextCursor'],expectedVersion=page['version'])

    def test_scan_budget_returns_a_resumable_cursor_even_without_visible_entries(self):
        self.capture()
        for index in range(501):self.put(format(index+1,'032x'),{})
        page=self.listing()
        self.assertEqual(page['scanned'],500);self.assertFalse(page['complete']);self.assertEqual(page['invalidRecords'],500)
        self.assertEqual(page['nextCursor'],format(500,'032x'))
        after=self.listing(after=page['nextCursor'],expectedVersion=page['version'])
        self.assertTrue(after['complete']);self.assertEqual(len(after['entries']),1)

    def test_preparing_present_missing_and_invalid_are_not_content_verification(self):
        identifier=self.capture();value=self.metadata(identifier);data=self.target/(identifier+'.data')
        value['state']='Preparing';self.put(identifier,value)
        self.assertEqual(self.listing()['entries'][0]['availability'],'Incomplete')
        with self.assertRaisesRegex(self.fixture.r.Rejected,'INCOMPLETE'):
            with self.source(identifier):self.fail('incomplete capture exposed')
        value['state']='Verified';self.put(identifier,value);data.chmod(0o644)
        self.assertEqual(self.listing()['entries'][0]['availability'],'Invalid');data.unlink()
        self.assertEqual(self.listing()['entries'][0]['availability'],'Missing')

    def test_same_size_corruption_remains_only_present_then_fails_full_verification(self):
        identifier=self.capture();data=self.target/(identifier+'.data');data.write_bytes(b'x'*len(self.fixture.original))
        self.assertEqual(self.listing()['entries'][0]['availability'],'Present')
        with self.assertRaisesRegex(self.fixture.r.Rejected,'CONTENT_INVALID'):
            with self.source(identifier):self.fail('corrupt bytes exposed')
        self.assertEqual(data.read_bytes(),b'x'*len(self.fixture.original))

    def test_metadata_and_contents_are_rechecked_after_consumer_reads(self):
        identifier=self.capture();data=self.target/(identifier+'.data')
        with self.assertRaisesRegex(self.fixture.r.Rejected,'CONTENT_CHANGED'):
            with self.source(identifier) as (_,descriptor):
                self.assertEqual(os.read(descriptor,16),self.fixture.original[:16]);data.write_bytes(b'x'*len(self.fixture.original))
        data.write_bytes(self.fixture.original)
        with self.assertRaisesRegex(self.fixture.r.Rejected,'CONTENT_CHANGED'):
            with self.source(identifier):
                value=self.metadata(identifier);value['createdAtUnixMs']+=1;self.put(identifier,value)

    def test_source_hash_change_is_rejected_before_any_descriptor_is_exposed(self):
        identifier=self.capture()
        with self.assertRaisesRegex(self.fixture.r.Rejected,'CAPTURE_CHANGED'):
            with self.source(identifier,expectedCaptureHash='A'*64):self.fail('changed capture exposed')

    def test_host_or_unknown_schema_and_malformed_metadata_are_not_adopted(self):
        identifier=self.capture();original=self.metadata(identifier)
        cases=[{'schemaVersion':1},{'schemaVersion':True},{'resourceType':'HostFile'},{'rootId':'test.shared'},
            {'operationId':uuid.uuid4().hex},{'sizeBytes':True},{'createdAtUnixMs':True},
            {'createdAtUnixMs':253402300800000},{'differentSourceDevice':1},{'captureKind':'Trash'},
            {'captureState':'Restored'},{'owner':'A'*64+'\n'},{'containerId':'D'*64},{'instanceId':'1:2026-10-27T01:02:03Z'},
            {'path':'../private'},{'sourceIdentity':[1,1,stat.S_IFREG|0o640,0,0,2,len(self.fixture.original),0]}]
        for changes in cases:
            with self.subTest(changes=changes):
                self.put(identifier,{**copy.deepcopy(original),**changes})
                page=self.listing();self.assertEqual(page['entries'],[]);self.assertEqual(page['invalidRecords'],1)
                with self.assertRaises((self.fixture.r.Rejected,self.fixture.f.Rejected)):
                    with self.source(identifier):self.fail('invalid capture exposed')

    def test_duplicate_keys_or_untrusted_metadata_permissions_are_rejected(self):
        identifier=self.capture();value=self.metadata(identifier);path=self.target/(identifier+'.json')
        raw=json.dumps(value);path.write_text(raw[:-1]+',"owner":"'+('F'*64)+'"}')
        self.assertEqual(self.listing()['invalidRecords'],1)
        with self.assertRaisesRegex(self.fixture.r.Rejected,'METADATA_INVALID'):
            with self.source(identifier):self.fail('duplicate record exposed')
        self.put(identifier,value);path.chmod(0o644)
        self.assertEqual(self.listing()['invalidRecords'],1)

    def test_symlinks_and_hardlinks_are_not_followed_or_modified(self):
        identifier=self.capture();data=self.target/(identifier+'.data');old=self.fixture.leaf.read_bytes()
        data.unlink();data.symlink_to(self.fixture.leaf)
        self.assertEqual(self.listing()['entries'][0]['availability'],'Invalid')
        with self.assertRaises((OSError,self.fixture.r.Rejected,self.fixture.f.Rejected)):
            with self.source(identifier):self.fail('symlink exposed')
        data.unlink();os.link(self.fixture.leaf,data)
        self.assertEqual(self.listing()['entries'][0]['availability'],'Invalid')
        with self.assertRaises((OSError,self.fixture.r.Rejected,self.fixture.f.Rejected)):
            with self.source(identifier):self.fail('hardlinked source exposed')
        self.assertEqual(self.fixture.leaf.read_bytes(),old)

    def test_missing_namespace_and_wrong_physical_policy_fail_without_creation_or_fallback(self):
        with self.assertRaises(FileNotFoundError):self.listing()
        self.assertFalse(self.target.exists())
        identifier=self.capture()
        for path,changed in ((self.fixture.disk.mountinfo,b''),(self.fixture.disk.rotational,b'0\n')):
            old=path.read_bytes()
            try:
                path.write_bytes(changed)
                with self.assertRaises(self.fixture.f.Rejected):self.listing()
                with self.assertRaises(self.fixture.f.Rejected):
                    with self.source(identifier):self.fail('unattested source exposed')
            finally:path.write_bytes(old)

    def test_list_and_source_reject_wrong_shapes_and_noncanonical_versions(self):
        identifier=self.capture()
        for values in ({'take':True},{'take':0},{'take':51},{'after':identifier+'\n'},{'expectedVersion':'A'*64+'\n'},
                       {'rootId':'test.shared'}):
            with self.subTest(values=values),self.assertRaises((self.fixture.r.Rejected,self.fixture.f.Rejected)):
                self.listing(**values)
        with self.assertRaisesRegex(self.fixture.r.Rejected,'REQUEST_INVALID'):
            with self.source(identifier,expectedCaptureHash='A'*64+'\n'):self.fail('noncanonical hash accepted')

    def test_history_requires_its_separate_container_namespace_and_engine_identity(self):
        with self.assertRaisesRegex(self.fixture.r.Rejected,'ENGINE_MISMATCH'):
            candidate.ContainerHistory(self.fixture.upload,self.fixture.disk.engine)


if __name__=='__main__':unittest.main()
