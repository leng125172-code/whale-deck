"""Real temporary Linux ACL/HDD only; not installed RPC or Authentik/UI evidence."""
import copy,os,unittest
from pathlib import Path
from recursive_acl_restore_agent_io import read_bundle
from recursive_acl_restore_partial_record_proof import read_partial_first,partial_first_proof
from recursive_acl_restore_record_proof import records_proof
import test_recursive_acl_restore_record_probe_scope as native

@unittest.skipUnless(os.geteuid()==0,'temporary root-private Linux ACL files required')
class PartialRestoreRecordTests(unittest.TestCase):
    def setUp(self):
        self.f=native.RestoreRecordProofTests();self.f.setUp();self.addCleanup(self.f.doCleanups);k=self.f.k
        source,result,old=k.seed();self.source=dict(request=source,result=result,before=old,after=k.b.snapshot())
        bundle,self.frame=read_bundle(self.f.io,source,False,self.f.archive/'source',create=True);self.source['metadata']=bundle['metadata']
        self.assertEqual(records_proof(self.source,bundle,restore=False,attribute_digest=self.f.attrs),4)
        self.before=k.b.snapshot();self.request=k.request(source,result);flag=[False]
        k.engine.cancelled=lambda:flag[0];k.engine.checkpoint=lambda phase,index:flag.__setitem__(0,True) if phase=='AfterEntry' and index==0 else None
        with self.assertRaises(k.b.base.r.Rejected):k.engine.apply(self.request,k.b.base.session)
        self.receipt=k.engine.inspect(self.request,k.b.base.session);self.after=k.b.snapshot();self.folder=self.f.archive/'partial'
        self.bundle=read_partial_first(self.f.io,self.request,self.folder,create=True)

    def prove(self,bundle=None,before=None,after=None,receipt=None):
        return partial_first_proof(self.request,receipt or self.receipt,bundle or self.bundle,self.source,self.frame,before or self.before,after or self.after,self.f.attrs)

    def test_actual_cancel_after_first_restore_keeps_partial_and_all_other_entries(self):
        proof=self.prove();self.assertEqual(proof['counts'],[1,1,0,3,0]);self.assertEqual(proof['completedChildHdd'],1);self.assertEqual(proof['otherEntriesUnchanged'],3)

    def test_separate_reopen_does_not_resume_or_change_original_records(self):
        other=read_partial_first(self.f.io,self.request,self.folder);self.assertEqual(other,self.bundle);self.prove(bundle=other);self.assertEqual(self.after,self.f.k.b.snapshot())

    def test_completed_child_target_identity_and_original_hdd_cannot_be_substituted(self):
        for part,key,bad in (('root','actualIdentity',[0]*9),('root','state','Aborted'),('hdd','parentOperationId','a'*32),('hdd','historyDigest','A'*64),('hdd','storageId','other')):
            b=copy.deepcopy(self.bundle);b['children'][0][part][key]=bad
            with self.subTest(part=part,key=key),self.assertRaises(AssertionError):self.prove(bundle=b)

    def test_partial_cannot_be_counted_as_success_or_zero_modifications(self):
        for key,bad in (('state','Completed'),('completedCount',4),('changedCount',0),('unknownCount',1),('startedThrough',1),('finishedThrough',True)):
            b=copy.deepcopy(self.bundle);b['root'][key]=bad
            with self.subTest(key=key),self.assertRaises(AssertionError):self.prove(bundle=b)

    def test_untouched_entries_and_completed_acl_must_match_full_physical_tree(self):
        rel=self.prove()['firstRelativePath'];other=next(p for p in self.after if p!=rel)
        for target in (rel,other):
            after=copy.deepcopy(self.after);after[target]['identity'][3]+=1
            with self.subTest(target=target),self.assertRaises(AssertionError):self.prove(after=after)

    def test_extra_child_record_is_never_silently_ignored(self):
        op=self.request['operationId'];directory=self.f.io.restoreState/(op+'.entries');fake=directory/('a'*32+'.json')
        fake.write_bytes(b'{}');fake.chmod(0o600)
        with self.assertRaises(AssertionError):read_partial_first(self.f.io,self.request,self.folder)
        fake.unlink();self.prove()

    def test_archive_directory_symlink_is_rejected_before_reopening_records(self):
        alias=self.folder.parent/'archive-alias';alias.symlink_to(self.folder,target_is_directory=True)
        with self.assertRaises(AssertionError):read_partial_first(self.f.io,self.request,alias)

    def test_nonprivate_root_record_cannot_be_accepted(self):
        path=self.f.io.restoreState/(self.request['operationId']+'.json');path.chmod(0o644)
        try:
            with self.assertRaises(AssertionError):read_partial_first(self.f.io,self.request,self.folder)
        finally:path.chmod(0o600)

if __name__=='__main__':unittest.main()
