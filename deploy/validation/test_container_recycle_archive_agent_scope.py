"""Gate ownership, exact deltas and phase ordering; no live production calls."""
import copy
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from validate_container_recycle_archive_agent import Probe,preserved


class ArchiveInstalledScopeTests(unittest.TestCase):
    def test_only_five_owned_operations_and_one_shared_claim_may_be_added(self):
        before={key:{'old':key} for key in ('hosts','recycleRoot','recycleHdd','purgeRoot','archiveRoot','recycleRestoreRoot')};after=copy.deepcopy(before);source='a'*32;purges=('b'*32,'c'*32);archives=('d'*32,'e'*32)
        for name in ('.json','.lock','.purge.json'):after['recycleRoot'][source+name]=1
        for name in ('.json','.data'):after['recycleHdd'][source+name]=1
        for key,ops in (('purgeRoot',purges),('archiveRoot',archives)):
            for op in ops:
                for suffix in ('.json','.lock'):after[key][op+suffix]=1
        after['recycleRestoreRoot']['claims/'+source+'.json']=1;preserved(before,after,source,purges,archives)
        for key in before:
            broken=copy.deepcopy(after);broken[key]['old']='changed'
            with self.subTest(key=key),self.assertRaises(AssertionError):preserved(before,broken,source,purges,archives)
        for key,name in (('archiveRoot',source+'.json'),('recycleHdd',source+'.partial'),('recycleRestoreRoot','claims/'+archives[0]+'.json')):
            broken=copy.deepcopy(after);broken[key][name]=1
            with self.subTest(name=name),self.assertRaises(AssertionError):preserved(before,broken,source,purges,archives)
        with self.assertRaises(AssertionError):preserved(before,after,source,purges,(purges[0],archives[0]))

    def receive(self,raw):
        t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup);read,write=os.pipe();os.write(write,raw);self.addCleanup(os.close,write);stream=os.fdopen(read,'rb');self.addCleanup(stream.close);probe=object.__new__(Probe);probe.process=SimpleNamespace(stdout=stream);probe.buffer=b'';probe.transcript=Path(t.name)/'rpc.jsonl';return probe

    def test_duplicate_fields_cannot_be_archived_as_a_success(self):
        probe=self.receive(b'{"state":"Completed","state":"Unknown"}\n')
        with self.assertRaises(AssertionError):probe.receive()
        self.assertFalse(probe.transcript.exists())

    def test_read_one_frame_keeps_extra_frame_buffered_for_protocol_checks(self):
        raw=b'{"kind":"one"}\n{"kind":"two"}\n';probe=self.receive(raw);self.assertEqual(probe.receive(),{'kind':'one'});self.assertEqual(probe.buffer,b'{"kind":"two"}\n');self.assertEqual(probe.receive(),{'kind':'two'});self.assertEqual(probe.transcript.read_bytes(),raw)

    def test_independent_live_proof_precedes_deleting_only_the_proven_new_copy(self):
        source=Path(__file__).with_name('validate_container_recycle_archive_agent.py').read_text();start=source.index("last=steps[-1]");cleanup=source.index('cleanup_new_copy(folder',start);live=source.index('independent(repo,e,True)',source.index("save('live-summary.json'"));self.assertLess(live,cleanup)
        self.assertIn("os.unlink('recycle-rpc.bin',dir_fd=parent)",source);self.assertIn("receipt['bindingCurrent'] is False",source);self.assertIn("f['retain_unsettled']=True",source);self.assertIn("assert summary['afterCleanupBindingObserved']",source)


if __name__=='__main__':unittest.main()
