"""Installed-gate proofs against real temporary files; no installed operations."""
import copy
import hashlib
import os
from pathlib import Path
from contextlib import contextmanager
import tempfile
import unittest
from unittest.mock import patch
import container_recycle_agent_io as io
from validate_container_recycle_agent import preserved


class PreservationTests(unittest.TestCase):
    def test_only_this_runs_exact_records_can_be_added(self):
        before=dict(core=['unchanged'],recycleRoot={'old':{'hash':'a'}},recycleHdd={})
        after=copy.deepcopy(before);after['recycleRoot']['a'*32+'.json']={};after['recycleHdd']['a'*32+'.data']={}
        preserved(before,after,['a'*32])
        for key,name in [('recycleRoot','b'*32+'.json'),('recycleHdd','a'*32+'.partial')]:
            changed=copy.deepcopy(after);changed[key][name]={}
            with self.subTest(key=key),self.assertRaises(AssertionError):preserved(before,changed,['a'*32])

    def test_old_content_and_core_may_not_change(self):
        before=dict(core=['unchanged'],recycleRoot={'old':{'hash':'a'}},recycleHdd={})
        for change in ('core','old','missing'):
            after=copy.deepcopy(before)
            if change=='core':after['core']=['changed']
            elif change=='old':after['recycleRoot']['old']['hash']='b'
            else:del after['recycleRoot']['old']
            with self.subTest(change=change),self.assertRaises(AssertionError):preserved(before,after,[])


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'root temporary-file metadata required')
class InstalledIoTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.op='a'*32;self.leaf=self.root/'recycle-rpc.bin'
        self.leaf.write_bytes(bytes(range(256))*500);self.leaf.chmod(0o640);os.chown(self.leaf,1000,1000)
        os.setxattr(self.leaf,'user.note',b'\x00\xff raw');self.original=io.file_snapshot(self.leaf)

    def test_binary_payload_snapshot_does_not_parse_json(self):
        namespace=self.root/'hdd';namespace.mkdir(mode=0o700)
        data=namespace/(self.op+'.data');data.write_bytes(bytes(range(256))*500);data.chmod(0o600)
        lock=namespace/'history.lock';lock.touch(mode=0o600)
        @contextmanager
        def target(storage):
            fd=os.open(namespace,os.O_RDONLY|os.O_DIRECTORY)
            try:yield fd
            finally:os.close(fd)
        with patch.object(io,'namespace',target):
            value=io.hdd_snapshot(None);self.assertEqual(value[data.name]['sha256'],hashlib.sha256(data.read_bytes()).hexdigest().upper())
            self.assertEqual(value[lock.name]['sizeBytes'],0)
            data.chmod(0o644)
            with self.assertRaises(AssertionError):io.hdd_snapshot(None)

    def test_aborted_source_is_exact_and_private_absent(self):
        value=io.physical(self.root,self.op);io.physical_proof(value,self.original,{},dict(state='Aborted'))
        changed=copy.deepcopy(value);changed['source']['sha256']='0'*64
        with self.assertRaises(AssertionError):io.physical_proof(changed,self.original,{},dict(state='Aborted'))
        (self.root/io.PRIVATE).mkdir(mode=0o700)
        with self.assertRaises(AssertionError):io.physical_proof(io.physical(self.root,self.op),self.original,{},dict(state='Aborted'))

    def test_actual_rename_and_restore_match_inode_bytes_and_attributes(self):
        private=self.root/io.PRIVATE;private.mkdir(mode=0o700);retained=private/(self.op+'.trash');self.leaf.rename(retained)
        actual=io.physical(self.root,self.op);record=dict(actualIdentity=actual['retained']['identity'],privateIdentity=actual['privateIdentity'])
        io.physical_proof(actual,self.original,record,dict(state='Trashed',bindingCurrent=True))
        retained.rename(self.leaf);actual=io.physical(self.root,self.op);record['actualIdentity']=actual['source']['identity']
        io.physical_proof(actual,self.original,record,dict(state='Restored',bindingCurrent=True))
        record['actualIdentity'][1]+=1
        with self.assertRaises(AssertionError):io.physical_proof(actual,self.original,record,dict(state='Restored',bindingCurrent=True))

    def test_links_and_other_private_entries_are_rejected(self):
        linked=self.root/'linked';os.link(self.leaf,linked)
        with self.assertRaises(AssertionError):io.file_snapshot(self.leaf)
        linked.unlink();linked.symlink_to(self.leaf)
        with self.assertRaises(OSError):io.file_snapshot(linked)
        private=self.root/io.PRIVATE;private.mkdir(mode=0o700);(private/'other.trash').touch()
        with self.assertRaises(AssertionError):io.physical(self.root,self.op)

    def test_invalid_operation_rejected_before_any_installed_read(self):
        for op in ('../outside','A'*32,'a'*31,'a'*33):
            with self.subTest(op=op),patch.object(io,'installed',side_effect=AssertionError('unexpected installed read')) as installed:
                with self.assertRaises(AssertionError):io.read_operation(dict(operationId=op),{},self.root)
                installed.assert_not_called()

    def test_dangling_private_symlink_is_rejected(self):
        (self.root/io.PRIVATE).symlink_to(self.root/'absent')
        with self.assertRaises(AssertionError):io.physical(self.root,self.op)


if __name__=='__main__':unittest.main()
