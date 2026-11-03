"""Isolated actual filesystem/crash tests; NO product services or HDD fault."""
import copy
import json
import os
from pathlib import Path
import select
import signal
import subprocess
import sys
import unittest
from unittest.mock import patch
import test_owned_acl_source_quarantine as fixture
import owned_acl_source_replacement as replacement
import owned_acl_source_quarantine as quarantine


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class ReplacementTests(unittest.TestCase):
    def setUp(self):
        fixture.OwnedSourceQuarantineTests.setUp(self);fixture.OwnedSourceQuarantineTests.arm(self)
        self.replacement=replacement.OwnedSourceReplacement(self.tool)

    def test_valid_different_digest_same_capture_and_precise_original_restoration(self):
        before=quarantine.catalogue(self.fd)
        with self.replacement.replaced() as proof:
            current=self.path.read_bytes();value=json.loads(current)
            self.assertEqual(value['operationId'],self.capture);self.assertNotEqual(value['historyDigest'],self.metadata['historyDigest'])
            self.assertEqual(value['createdAtUnixMs'],self.metadata['createdAtUnixMs']+1)
            self.assertEqual(value['plan'],self.metadata['plan']);self.assertEqual(value['owner'],self.metadata['owner'])
            self.assertNotEqual(quarantine.read_file(self.fd,self.name)[0]['frame'][:2],before[self.name]['frame'][:2])
            self.assertTrue(proof['canonicalSourceExists']);self.assertTrue(proof['sameCaptureId'])
            self.assertEqual(quarantine.catalogue(self.fd)[self.oldname],self.old[self.oldname])
        after=quarantine.catalogue(self.fd)
        self.assertEqual(after[self.oldname],before[self.oldname]);self.assertEqual(self.path.read_bytes(),self.raw)
        self.assertEqual(after[self.name]['frame'][:8],before[self.name]['frame'][:8])
        self.assertEqual(set(os.listdir(self.namespace)),{self.name,self.oldname})
        self.assertTrue((self.journal/replacement.ARCHIVE).is_file())
        self.assertTrue(self.replacement.restore()['alreadyPresent'])

    def test_python_failure_after_publication_recovers_original_and_archives_only_replacement(self):
        with self.assertRaisesRegex(RuntimeError,'own test failure'):
            with self.replacement.replaced():raise RuntimeError('own test failure')
        self.assertEqual(self.path.read_bytes(),self.raw)
        self.assertNotEqual((self.journal/replacement.ARCHIVE).read_bytes(),self.raw)

    def test_sigkill_after_publication_fresh_process_recovers_durable_journals(self):
        program='''import os,sys,signal
from owned_acl_source_quarantine import OwnedSourceQuarantine
from owned_acl_source_replacement import OwnedSourceReplacement
fd=os.open(sys.argv[1],os.O_RDONLY|os.O_DIRECTORY);j=os.open(sys.argv[2],os.O_RDONLY|os.O_DIRECTORY)
tool=OwnedSourceReplacement(OwnedSourceQuarantine(fd,j,lambda fd:None))
tool.prepare();tool.q.hold();tool.publish();print('published',flush=True);signal.pause()
'''
        child=subprocess.Popen([sys.executable,'-c',program,str(self.namespace),str(self.journal)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,cwd=Path(__file__).parent)
        try:
            self.assertTrue(select.select([child.stdout],[],[],8)[0]);self.assertEqual(child.stdout.readline(),'published\n')
            self.assertNotEqual(self.path.read_bytes(),self.raw);child.kill();self.assertEqual(child.wait(timeout=5),-signal.SIGKILL)
        finally:
            if child.poll() is None:child.kill();child.wait(timeout=5)
            child.stdout.close();child.stderr.close()
        recovery=program[:program.index("tool.prepare()")]+"print(tool.restore());os.close(fd);os.close(j)\n"
        result=subprocess.run([sys.executable,'-c',recovery,str(self.namespace),str(self.journal)],capture_output=True,text=True,timeout=10,cwd=Path(__file__).parent)
        self.assertEqual(result.returncode,0,result.stderr);self.assertEqual(self.path.read_bytes(),self.raw)
        self.assertEqual(quarantine.catalogue(self.fd)[self.oldname],self.old[self.oldname])

    def test_prepared_but_not_published_and_hidden_but_not_published_are_recoverable(self):
        self.replacement.prepare();self.tool.hold();self.replacement.restore()
        self.assertEqual(self.path.read_bytes(),self.raw)

    def test_prepared_only_cleanup_never_moves_original(self):
        before=quarantine.catalogue(self.fd);self.replacement.prepare();self.replacement.restore()
        self.assertEqual(quarantine.catalogue(self.fd),before)

    def test_publish_requires_exact_original_held_directory_and_inode_proof(self):
        self.replacement.prepare();self.path.rename(self.folder/'preserved-outside')
        with self.assertRaises(FileNotFoundError):self.replacement.publish()
        self.assertFalse(self.path.exists());self.assertEqual((self.folder/'preserved-outside').read_bytes(),self.raw)

    def test_foreign_canonical_occupant_preserved_original_stays_held(self):
        self.replacement.prepare();self.tool.hold();self.path.write_bytes(b'foreign');self.path.chmod(0o600)
        with self.assertRaises(AssertionError):self.replacement.restore()
        self.assertEqual(self.path.read_bytes(),b'foreign')
        self.assertEqual((self.namespace/('.wd-qa-acl-source-'+self.capture)/self.name).read_bytes(),self.raw)

    def test_modified_replacement_not_withdrawn_or_deleted(self):
        self.replacement.prepare();self.tool.hold();self.replacement.publish();self.path.write_bytes(b'changed')
        with self.assertRaises(AssertionError):self.replacement.restore()
        self.assertEqual(self.path.read_bytes(),b'changed')

    def test_preexisting_stage_not_reused_or_cleaned(self):
        stage=self.namespace/('.wd-qa-acl-replacement-'+self.capture);stage.mkdir(mode=0o700);(stage/'keep').write_bytes(b'foreign')
        with self.assertRaises(FileExistsError):
            with self.replacement.replaced():self.fail('must not enter')
        self.assertEqual((stage/'keep').read_bytes(),b'foreign');self.assertEqual(self.path.read_bytes(),self.raw)

    def test_changed_stage_identity_refuses_mutation(self):
        state=self.replacement.prepare();self.tool.hold();self.replacement.publish()
        stage=self.namespace/state['stage'];stage.rename(self.namespace/'original-stage');stage.mkdir(mode=0o700)
        with self.assertRaisesRegex(AssertionError,'STAGE_CHANGED'):self.replacement.restore()
        self.assertNotEqual(self.path.read_bytes(),self.raw)

    def test_unrelated_old_capture_change_refuses_publication_or_cleanup(self):
        self.replacement.prepare();self.tool.hold();(self.namespace/self.oldname).write_bytes(b'changed')
        with self.assertRaisesRegex(AssertionError,'UNRELATED_CAPTURE'):self.replacement.publish()
        with self.assertRaisesRegex(AssertionError,'UNRELATED_CAPTURE'):self.replacement.restore()

    def test_candidate_link_or_digest_tamper_is_not_cleaned(self):
        state=self.replacement.prepare();stage=self.namespace/state['stage'];file=stage/replacement.LEAF
        os.link(file,self.folder/'link')
        with self.assertRaises(AssertionError):self.replacement.restore()
        self.assertTrue(file.exists());self.assertEqual(self.path.read_bytes(),self.raw)

    def test_foreign_staging_entry_is_preserved(self):
        state=self.replacement.prepare();stage=self.namespace/state['stage'];(stage/'foreign').write_bytes(b'keep')
        with self.assertRaisesRegex(AssertionError,'FOREIGN_ENTRY'):self.replacement.restore()
        self.assertEqual((stage/'foreign').read_bytes(),b'keep')

    def test_archive_conflict_keeps_candidate_after_original_recovered(self):
        self.replacement.prepare();self.tool.hold();self.replacement.publish()
        archive=self.journal/replacement.ARCHIVE;archive.write_bytes(b'foreign');archive.chmod(0o600)
        with self.assertRaises(AssertionError):self.replacement.restore()
        self.assertEqual(self.path.read_bytes(),self.raw);self.assertEqual(archive.read_bytes(),b'foreign')

    def test_changed_metadata_does_not_accept_bad_original_digest_or_overflow(self):
        for change in ({'historyDigest':'F'*64},{'createdAtUnixMs':9223372036854775807},{'createdAtUnixMs':True}):
            with self.subTest(change=change),self.assertRaises(AssertionError):replacement.changed_metadata(json.dumps({**self.metadata,**change}).encode())

    def test_crash_after_candidate_unlink_recovers_empty_proved_stage(self):
        self.replacement.prepare();self.tool.hold();self.replacement.publish()
        real_rmdir=os.rmdir
        def fail_own_stage(path,*args,**kwargs):
            if str(path).startswith('.wd-qa-acl-replacement-'):raise RuntimeError('after unlink')
            return real_rmdir(path,*args,**kwargs)
        with patch('owned_acl_source_replacement.os.rmdir',side_effect=fail_own_stage),self.assertRaisesRegex(RuntimeError,'after unlink'):self.replacement.restore()
        self.assertEqual(self.path.read_bytes(),self.raw);self.replacement.restore()
        self.assertEqual(set(os.listdir(self.namespace)),{self.name,self.oldname})


if __name__=='__main__':unittest.main()
