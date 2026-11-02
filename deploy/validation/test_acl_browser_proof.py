"""Offline proof/cleanup boundaries on temporary owned data, never actual RPC/UI."""
import importlib.util
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
