"""Temporary real ACL/rename/format records; container and HDD are explicit fixtures."""
import copy,os,unittest
from recursive_acl_restore_agent_io import read_bundle
from recursive_acl_restore_partial_record_proof import read_applying_first,read_unknown_first
from recursive_acl_restore_record_proof import records_proof
from recursive_acl_restore_unknown_record_proof import unknown_renamed_first_proof,prefix_archives_unchanged
import test_recursive_acl_restore_record_probe_scope as native

@unittest.skipUnless(os.geteuid()==0,'temporary private Linux ACL/HDD records required')
class UnknownRestoreRecordTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreRecordProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups);k=self.f.k
        source,result,old=k.seed();self.source=dict(request=source,result=result,before=old,after=k.b.snapshot())
        bundle,self.frame=read_bundle(self.f.io,source,False,self.f.archive/'source',create=True);self.source['metadata']=bundle['metadata'];self.assertEqual(records_proof(self.source,bundle,restore=False,attribute_digest=self.f.attrs),4)
        self.before=k.b.snapshot();self.request=k.request(source,result);self.checkpoints=0
        def checkpoint(phase,index):
            if phase=='AfterEntry' and index==0:
                self.checkpoints+=1;self.cut_bundle=read_applying_first(self.f.io,self.request,self.f.archive/'cut',create=True);self.cut=k.b.snapshot()
                (k.b.tree/'one.bin').rename(k.b.tree/'renamed.bin');k.b.paths=tuple('renamed.bin' if p=='one.bin' else p for p in k.b.paths);self.after=k.b.snapshot()
        k.engine.checkpoint=checkpoint
        with self.assertRaises((OSError,k.b.base.r.Rejected)):k.engine.apply(self.request,k.b.base.session)
        with k.no_write():self.receipt=k.engine.inspect(self.request,k.b.base.session)
        self.bundle=read_unknown_first(self.f.io,self.request,self.f.archive/'unknown',create=True)

    def prove(self,bundle=None,after=None,receipt=None,old='one.bin',new='renamed.bin'):
        return unknown_renamed_first_proof(self.request,receipt or self.receipt,self.cut_bundle,bundle or self.bundle,self.source,self.frame,self.before,self.cut,after or self.after,self.f.attrs,old,new)

    def test_real_target_rename_keeps_known_write_separate_from_aggregate_unknown(self):
        result=self.prove();self.assertEqual(result['aggregateCounts'],[0,0,0,0,4]);self.assertEqual(result['knownActualChanges'],1);self.assertTrue(result['aggregateUnknownIsNotZeroActualChanges'])
        self.assertEqual(prefix_archives_unchanged(self.f.archive/'cut',self.f.archive/'unknown',self.request['operationId']),3)
    def test_late_apply_and_inspect_never_resume_writer(self):
        k=self.f.k
        with k.no_write():late=k.engine.apply(self.request,k.b.base.session);observed=k.engine.inspect(self.request,k.b.base.session)
        self.assertEqual(late['state'],observed['state']);self.assertEqual(late['state'],'Unknown');self.assertEqual(self.checkpoints,1);self.assertEqual(k.b.snapshot(),self.after)
        self.prove(bundle=read_unknown_first(self.f.io,self.request,self.f.archive/'unknown'))
    def test_unknown_cannot_be_relabelled_as_partial_or_zero_unknown(self):
        for key,bad in (('state','Partial'),('unknownCount',0),('changedCount',1),('outcomeDigest','A'*64),('finishedThrough',1),('startedThrough',2)):
            b=copy.deepcopy(self.bundle);b['root'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(bundle=b)
    def test_completed_child_and_hdd_originals_must_remain_exact(self):
        for part,key,bad in (('root','state','Unknown'),('root','historyDigest','A'*64),('hdd','parentOperationId','a'*32)):
            b=copy.deepcopy(self.bundle);b['children'][0][part][key]=bad
            with self.subTest(part=part,key=key),self.assertRaises(AssertionError):self.prove(bundle=b)
    def test_rename_must_keep_original_inode_scope_and_all_other_objects(self):
        for name in ('renamed.bin','sub/中文.bin',''):
            after=copy.deepcopy(self.after);after[name]['identity'][1]+=1
            with self.subTest(name=name),self.assertRaises(AssertionError):self.prove(after=after)
        for old,new in (('..','renamed.bin'),('one.bin','../escape'),('one.bin','sub/renamed.bin'),('one.bin','one.bin')):
            with self.subTest(old=old,new=new),self.assertRaises(AssertionError):self.prove(old=old,new=new)
    def test_unknown_reader_does_not_ignore_extra_child_or_rename_source_back(self):
        directory=self.f.io.restoreState/(self.request['operationId']+'.entries');fake=directory/('a'*32+'.json');fake.write_bytes(b'{}');fake.chmod(0o600)
        with self.assertRaises(AssertionError):read_unknown_first(self.f.io,self.request,self.f.archive/'unknown')
        fake.unlink();self.assertFalse((self.f.k.b.tree/'one.bin').exists());self.prove()

    def test_known_original_byte_changes_cannot_hide_behind_equivalent_json(self):
        path=self.f.archive/'unknown'/'hdd.json';path.write_bytes(path.read_bytes()+b'\n')
        with self.assertRaisesRegex(AssertionError,'ORIGINAL_CHANGED'):prefix_archives_unchanged(self.f.archive/'cut',self.f.archive/'unknown',self.request['operationId'])

    def test_archive_mode_is_private_even_with_permissive_process_umask(self):
        from recursive_acl_restore_agent_io import archive_raw
        folder=self.f.archive/'umask-proof';folder.mkdir(mode=0o700)
        prior=os.umask(0o022)
        try:archive_raw(folder,'record.json',b'{}',True)
        finally:os.umask(prior)
        path=folder/'record.json';self.assertEqual(path.stat().st_mode&0o777,0o600)
        path.chmod(0o644)
        with self.assertRaisesRegex(AssertionError,'ARCHIVE_MODE'):archive_raw(folder,'record.json',b'{}',False)

    def test_untested_root_leaf_names_are_not_silently_accepted_as_this_case(self):
        with self.assertRaisesRegex(AssertionError,'SCENARIO_MISMATCH'):self.prove(old='another.bin',new='other.bin')

if __name__=='__main__':unittest.main()
