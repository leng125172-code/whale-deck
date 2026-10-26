"""Real root metadata/locks for owned QA cleanup, not installed RPC evidence."""
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest

import validate_container_file_write_agent as observer


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires actual isolated Linux root metadata')
class WriteProbeCleanupTests(unittest.TestCase):
    @contextlib.contextmanager
    def fixture(self, state='Published'):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary); directory.chmod(0o700)
            operation = 'a' * 32; body = b'owned-QA-only\x00\xff'
            binding = {'operationId': operation, 'containerId': 'b'*64, 'instanceId': '123:2026-10-26T01:02:03.123456789Z',
                'owner': 'B'*64, 'targetProof': 'C'*64, 'state': state}
            request = {key: binding[key] for key in ('operationId', 'containerId', 'instanceId', 'owner')}
            request.update(path='work/original.bin' if state == 'Published' else 'work/canceled.bin', sizeBytes=len(body), sha256=hashlib.sha256(body).hexdigest().upper())
            record = {**binding, 'schemaVersion': 1, 'request': request,
                'fingerprint': hashlib.sha256(b'whaledeck-container-upload-stage-v1\n' + json.dumps(request, sort_keys=True,
                    separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()}
            data = directory/(operation+'.data')
            if state == 'Published':
                data.write_bytes(body); data.chmod(0o600)
                record['payloadIdentity'] = [data.stat().st_dev, data.stat().st_ino]
            metadata = directory/(operation+'.json'); metadata.write_text(json.dumps(record)); metadata.chmod(0o600)
            lock = directory/(operation+'.lock'); lock.touch(mode=0o600)
            yield directory, operation, binding, body, record

    def test_exact_published_cleanup_preserves_unrelated_objects(self):
        with self.fixture() as (directory, operation, binding, body, _):
            outside = directory/'other-operation.json'; outside.write_bytes(b'preserve')
            observer.cleanup_receipt(directory, operation, binding, body)
            self.assertEqual({'other-operation.json'}, {p.name for p in directory.iterdir()})
            self.assertEqual(b'preserve', outside.read_bytes())

    def test_terminal_discarded_receipt_without_payload_is_removed(self):
        with self.fixture('Discarded') as (directory, operation, binding, body, _):
            observer.cleanup_receipt(directory, operation, binding, body)
            self.assertEqual([], list(directory.iterdir()))

    def test_foreign_scope_is_rejected_before_any_unlink(self):
        with self.fixture() as (directory, operation, binding, body, _):
            before = {p.name: p.read_bytes() for p in directory.iterdir()}
            for key in ('containerId', 'instanceId', 'owner', 'operationId', 'targetProof', 'state'):
                with self.subTest(key=key), self.assertRaises(AssertionError):
                    observer.cleanup_receipt(directory, operation, {**binding, key:'foreign'}, body)
                self.assertEqual(before, {p.name:p.read_bytes() for p in directory.iterdir()})

    def test_extra_partial_is_preserved_and_prevents_all_cleanup(self):
        with self.fixture() as (directory, operation, binding, body, _):
            (directory/(operation+'.partial')).write_bytes(b'unknown')
            before={p.name:p.read_bytes() for p in directory.iterdir()}
            with self.assertRaises(AssertionError): observer.cleanup_receipt(directory, operation, binding, body)
            self.assertEqual(before, {p.name:p.read_bytes() for p in directory.iterdir()})

    def test_full_payload_corruption_or_wrong_inode_prevents_cleanup(self):
        for kind in ('content','inode'):
            with self.subTest(kind=kind), self.fixture() as (directory, operation, binding, body, _):
                data=directory/(operation+'.data')
                if kind=='content': data.write_bytes(b'X'*len(body))
                else:
                    replacement=directory/'new'; replacement.write_bytes(body);replacement.chmod(0o600);replacement.replace(data)
                before={p.name:p.read_bytes() for p in directory.iterdir()}
                with self.assertRaises(AssertionError): observer.cleanup_receipt(directory, operation, binding, body)
                self.assertEqual(before, {p.name:p.read_bytes() for p in directory.iterdir()})

    def test_linked_metadata_or_unsafe_permissions_prevent_cleanup(self):
        for kind in ('symlink','hardlink','permissions'):
            with self.subTest(kind=kind), self.fixture() as (directory, operation, binding, body, _):
                metadata=directory/(operation+'.json')
                if kind=='symlink':
                    outside=directory/'outside';metadata.rename(outside);metadata.symlink_to(outside)
                elif kind=='hardlink': os.link(metadata,directory/'outside')
                else: metadata.chmod(0o666)
                with self.assertRaises(AssertionError): observer.cleanup_receipt(directory, operation, binding, body)
                self.assertTrue(metadata.exists());self.assertTrue((directory/(operation+'.data')).exists())

    def test_live_operation_lock_prevents_cleanup(self):
        with self.fixture() as (directory, operation, binding, body, _):
            with (directory/(operation+'.lock')).open('rb') as held:
                fcntl.flock(held,fcntl.LOCK_EX|fcntl.LOCK_NB)
                with self.assertRaises(BlockingIOError): observer.cleanup_receipt(directory, operation, binding, body)
                self.assertTrue((directory/(operation+'.json')).exists())

    def test_changed_request_fingerprint_is_not_accepted(self):
        with self.fixture() as (directory, operation, binding, body, record):
            record['request']['path']='work/other.bin'
            (directory/(operation+'.json')).write_text(json.dumps(record))
            with self.assertRaises(AssertionError): observer.cleanup_receipt(directory, operation, binding, body)
            self.assertTrue((directory/(operation+'.data')).exists())

    @staticmethod
    def fence(directory,operation,binding):
        expected={key:value for key,value in binding.items() if key!='state'}
        record={**expected,'schemaVersion':2,'resourceType':'ContainerUploadCancellation','state':'Prevented','createdAtUnixMs':1792990471366}
        (directory/(operation+'.json')).write_text(json.dumps(record))
        return expected

    def test_quiescent_owned_fence_cleanup_keeps_unrelated_records(self):
        with self.fixture('Discarded') as (directory,operation,binding,_,_):
            expected=self.fence(directory,operation,binding)
            outside=directory/'unrelated.json';outside.write_bytes(b'preserve')
            observer.cleanup_fence(directory,operation,expected)
            self.assertEqual({'unrelated.json'},{p.name for p in directory.iterdir()})
            self.assertEqual(outside.read_bytes(),b'preserve')

    def test_fence_cleanup_refuses_extra_payload_and_preserves_everything(self):
        with self.fixture('Discarded') as (directory,operation,binding,_,_):
            expected=self.fence(directory,operation,binding)
            (directory/(operation+'.partial')).write_bytes(b'unknown')
            before={p.name:p.read_bytes() for p in directory.iterdir()}
            with self.assertRaises(AssertionError):observer.cleanup_fence(directory,operation,expected)
            self.assertEqual(before,{p.name:p.read_bytes() for p in directory.iterdir()})

    def test_fence_cleanup_refuses_foreign_scope_and_fake_schema(self):
        with self.fixture('Discarded') as (directory,operation,binding,_,_):
            expected=self.fence(directory,operation,binding)
            with self.assertRaises(AssertionError):observer.cleanup_fence(directory,operation,{**expected,'owner':'C'*64})
            metadata=directory/(operation+'.json');original=json.loads(metadata.read_bytes())
            for change in ({'schemaVersion':1},{'state':'Discarded'},{'createdAtUnixMs':True},{'resourceType':'ContainerFile'}):
                metadata.write_text(json.dumps({**original,**change}))
                with self.subTest(change=change),self.assertRaises(AssertionError):observer.cleanup_fence(directory,operation,expected)
                self.assertTrue(metadata.exists())

    def test_live_fence_owner_lock_blocks_qa_cleanup(self):
        with self.fixture('Discarded') as (directory,operation,binding,_,_):
            expected=self.fence(directory,operation,binding)
            with (directory/(operation+'.lock')).open('rb') as held:
                fcntl.flock(held,fcntl.LOCK_EX|fcntl.LOCK_NB)
                with self.assertRaises(BlockingIOError):observer.cleanup_fence(directory,operation,expected)
                self.assertTrue((directory/(operation+'.json')).exists())


if __name__ == '__main__': unittest.main()
