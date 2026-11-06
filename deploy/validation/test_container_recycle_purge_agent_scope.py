"""Installed gate framing and exact namespace preservation; no production IO."""
import copy
import os
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from validate_container_recycle_purge_agent import Probe,preserved


class PurgeInstalledScopeTests(unittest.TestCase):
    def test_only_exact_three_source_two_hdd_and_two_operation_names_may_be_added(self):
        before=dict(hosts={'unchanged':1},recycleRoot={'old':1},recycleHdd={'old':2},purgeRoot={'old':3});s='a'*32;ops=('b'*32,'c'*32);after=copy.deepcopy(before)
        after['recycleRoot'].update({s+n:1 for n in ('.json','.lock','.purge.json')});after['recycleHdd'].update({s+n:1 for n in ('.json','.data')});after['purgeRoot'].update({op+n:1 for op in ops for n in ('.json','.lock')});preserved(before,after,s,ops)
        for key in ('hosts','recycleRoot','recycleHdd','purgeRoot'):
            bad=copy.deepcopy(after)
            if key=='hosts':bad[key]['unchanged']=2
            else:bad[key]['old']=9
            with self.subTest(key=key),self.assertRaises(AssertionError):preserved(before,bad,s,ops)
        for key,name in (('recycleRoot',ops[0]+'.json'),('recycleHdd',s+'.partial'),('purgeRoot','d'*32+'.json')):
            bad=copy.deepcopy(after);bad[key][name]=1
            with self.subTest(name=name),self.assertRaises(AssertionError):preserved(before,bad,s,ops)

    def receive(self,line):
        folder=tempfile.TemporaryDirectory();self.addCleanup(folder.cleanup);r,w=os.pipe();self.addCleanup(os.close,w);os.write(w,line);stream=os.fdopen(r,'rb');self.addCleanup(stream.close);probe=object.__new__(Probe);probe.process=SimpleNamespace(stdout=stream);probe.buffer=b'';probe.transcript=Path(folder.name)/'rpc.jsonl';return probe

    def test_duplicate_reply_is_rejected_before_archiving_or_next_mutation(self):
        probe=self.receive(b'{"state":"Completed","state":"Unknown"}\n')
        with self.assertRaises(AssertionError):probe.receive()
        self.assertFalse(probe.transcript.exists())

    def test_valid_single_reply_is_archived_exactly_and_leaves_no_extra_bytes(self):
        raw=b'{"kind":"done"}\n';probe=self.receive(raw);self.assertEqual(probe.receive(),{'kind':'done'});self.assertEqual(probe.transcript.read_bytes(),raw);self.assertEqual(probe.buffer,b'')


if __name__=='__main__':unittest.main()
