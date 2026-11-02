"""Offline proof/cleanup boundaries on temporary owned data, never actual RPC/UI."""
import importlib.util
import json
from pathlib import Path
import os
import tempfile
import unittest
from unittest.mock import patch
import uuid
import validate_container_acl_browser as browser


def fingerprint(s):return (s.st_dev,s.st_ino,s.st_mode,s.st_uid,s.st_gid,s.st_nlink,s.st_size,s.st_mtime_ns,s.st_ctime_ns)


class Reader:
    fingerprint=staticmethod(fingerprint)


class Acl:
    XATTRS={'access':'system.posix_acl_access','default':'system.posix_acl_default'}
    @staticmethod
    def read_acl(fd,kind,mode):
        if kind=='default':return []
        return [{'tag':'user_obj','id':None,'permissions':mode>>6&7},{'tag':'group_obj','id':None,'permissions':mode>>3&7},{'tag':'other','id':None,'permissions':mode&7}]


class AclBrowserProofTests(unittest.TestCase):
    def job_fixture(self,execution=False):
        row={'id':str(uuid.uuid4())};plan={'command':{'aclType':'access'},'kind':'File','targetMode':0,'noChange':False}
        expected=browser.expected_job_summary(row,plan,execution);identifier=str(uuid.uuid4()) if execution else row['id']
        return {'id':identifier,'state':'Succeeded','jobType':'containers.files.acl' if execution else 'containers.files.acl-plan','resultJson':json.dumps(expected)},expected,identifier
    def test_summaries_prove_explicit_zero_false_and_null_without_raw_bindings(self):
        for execution in (False,True):
            job,expected,identifier=self.job_fixture(execution)
            self.assertEqual(browser.validate_job_summary(job,expected,identifier),expected)
            job['resultJson']=expected.copy();self.assertEqual(browser.validate_job_summary(job,expected,identifier),expected)
            self.assertEqual(expected['targetMode'],0);self.assertIs(expected['noChange'],False)
            self.assertEqual(expected['aclConfirmed'],True if execution else None)
    def test_ready_plan_remains_a_plan_even_when_the_intent_is_canceled(self):
        row={'id':str(uuid.uuid4()),'state':'Canceled'};plan={'command':{'aclType':'default'},'kind':'Directory','targetMode':416,'noChange':True}
        value=browser.expected_job_summary(row,plan);self.assertEqual(value['state'],'Ready');self.assertIsNone(value['aclConfirmed']);self.assertFalse(value['hasPublishedVersion'])
    def test_wrong_id_stage_state_or_type_cannot_match_original_job(self):
        for field,value in (('id',str(uuid.uuid4())),('state','Failed'),('jobType','containers.files.ownership')):
            job,expected,identifier=self.job_fixture();job[field]=value
            with self.subTest(field=field),self.assertRaises(AssertionError):browser.validate_job_summary(job,expected,identifier)
    def test_missing_extra_and_wrong_scalar_fields_are_rejected(self):
        for change in ('missing','extra','boolean-schema','integer-boolean','false-for-null'):
            job,expected,identifier=self.job_fixture();value=expected.copy()
            if change=='missing':del value['historyVerified']
            elif change=='extra':value['path']='/private'
            elif change=='boolean-schema':value['schemaVersion']=True
            elif change=='integer-boolean':value['noChange']=0
            else:value['aclConfirmed']=False
            job['resultJson']=json.dumps(value)
            with self.subTest(change=change),self.assertRaises(AssertionError):browser.validate_job_summary(job,expected,identifier)
    def test_duplicate_fields_and_unbounded_json_are_rejected(self):
        job,expected,identifier=self.job_fixture();original=job['resultJson']
        for value in (original[:-1]+',"schemaVersion":1}', ' '*4097+original):
            job['resultJson']=value
            with self.assertRaises(AssertionError):browser.validate_job_summary(job,expected,identifier)
    def fixture(self,root):
        folder=root/('wdv-container-publish-'+uuid.uuid4().hex);folder.mkdir();(folder/'acl-directory').mkdir()
        for name in ('acl-file.bin','acl-directory/existing.bin'):
            p=folder/name;p.write_bytes(b'exact-owned-bytes');os.setxattr(p,'user.whaledeck-acl',b'preserve ordinary attribute')
        return folder,b'exact-owned-bytes'
    def test_correct_cleanup_leaves_unrelated_original_untouched(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder,content=self.fixture(root);original=folder/'original.bin';original.write_bytes(b'original');before=fingerprint(original.stat());current=browser.inventory(folder,Reader,Acl,content)
            with patch.object(browser.owned.common,'APPS',root):browser.remove_proved(folder,Reader,Acl,content,current)
            self.assertEqual([p.name for p in folder.iterdir()],['original.bin']);self.assertEqual(fingerprint(original.stat()),before);self.assertEqual(original.read_bytes(),b'original')
    def test_unproved_content_refuses_before_any_cleanup(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder,content=self.fixture(root);current=browser.inventory(folder,Reader,Acl,content);(folder/'acl-file.bin').write_bytes(b'changed')
            with patch.object(browser.owned.common,'APPS',root),self.assertRaises(AssertionError):browser.remove_proved(folder,Reader,Acl,content,current)
            self.assertTrue((folder/'acl-directory/existing.bin').exists());self.assertEqual((folder/'acl-file.bin').read_bytes(),b'changed')
    def test_extra_directory_child_refuses_before_any_cleanup(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder,content=self.fixture(root);current=browser.inventory(folder,Reader,Acl,content);(folder/'acl-directory/unknown').write_bytes(b'keep')
            with patch.object(browser.owned.common,'APPS',root),self.assertRaises(AssertionError):browser.remove_proved(folder,Reader,Acl,content,current)
            self.assertTrue((folder/'acl-file.bin').exists());self.assertEqual((folder/'acl-directory/unknown').read_bytes(),b'keep')
    def test_symlink_root_cannot_select_another_owned_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder,content=self.fixture(root);current=browser.inventory(folder,Reader,Acl,content);alias=root/('wdv-container-publish-'+uuid.uuid4().hex);alias.symlink_to(folder,target_is_directory=True)
            with patch.object(browser.owned.common,'APPS',root),self.assertRaises(AssertionError):browser.remove_proved(alias,Reader,Acl,content,current)
            self.assertTrue((folder/'acl-file.bin').exists())
    def test_wrong_parent_cannot_select_data_outside_registered_apps(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder,content=self.fixture(root);current=browser.inventory(folder,Reader,Acl,content)
            with patch.object(browser.owned.common,'APPS',root/'different'),self.assertRaises(AssertionError):browser.remove_proved(folder,Reader,Acl,content,current)
            self.assertTrue((folder/'acl-file.bin').exists())
    def test_profiles_preserve_explicit_zero_default_clear_and_canonical_mask(self):
        module_path=Path(__file__).resolve().parents[1]/'host/whaledeck-container-acl.py';spec=importlib.util.spec_from_file_location('acl_browser_test_codec',module_path);codec=importlib.util.module_from_spec(spec);spec.loader.exec_module(codec)
        for label in browser.CASES:codec.validate_entries(browser.profile(label),empty=label=='clear-default')
        self.assertEqual(browser.profile('root')[1],{'tag':'user','id':0,'permissions':0});self.assertEqual(browser.profile('clear-default'),[])
        self.assertEqual(codec.acl_mode(browser.profile('mask')),0o660)


if __name__=='__main__':unittest.main()
