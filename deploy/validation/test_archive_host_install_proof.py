import copy
import unittest
from archive_host_install_proof import preserved_install,REWRITTEN,KEY,HOSTS


class ArchiveHostInstallTests(unittest.TestCase):
    def data(self):
        a=dict(hosts={u:dict(pid=10+i,sourceRevision='a'*40) for i,u in enumerate(sorted(HOSTS))},protected={p:dict(identity=[1,2,0o100640,0,0,1,32,100,100],sha256='unchanged') for p in (*sorted(REWRITTEN),KEY,'/etc/sudoers.d/90-whaledeck-development-user')},core={'cid':'old'},recycleHdd={'old':'old'},purgeRoot={'old':'old'});b=copy.deepcopy(a)
        for v in b['hosts'].values():v.update(pid=v['pid']+100,sourceRevision='b'*40)
        for p in REWRITTEN:b['protected'][p]['identity'][7:]=[200,200]
        b['protected'][KEY]['identity'][8]=200
        return a,b
    def test_exact_standard_host_only_delta_passes(self):
        a,b=self.data();self.assertTrue(preserved_install(a,b,'a'*40,'b'*40)['protectedContentPreserved'])
    def test_no_content_inode_owner_size_or_key_mtime_changes_are_hidden(self):
        for p,index in ((KEY,7),(KEY,1),(next(iter(REWRITTEN)),1),(next(iter(REWRITTEN)),3),(next(iter(REWRITTEN)),6)):
            a,b=self.data();b['protected'][p]['identity'][index]+=1
            with self.subTest(path=p,index=index),self.assertRaises(AssertionError):preserved_install(a,b,'a'*40,'b'*40)
        a,b=self.data();b['protected'][KEY]['sha256']='rotated'
        with self.assertRaises(AssertionError):preserved_install(a,b,'a'*40,'b'*40)
    def test_sudo_development_rule_and_other_records_must_remain_byte_identical(self):
        a,b=self.data();b['protected']['/etc/sudoers.d/90-whaledeck-development-user']['identity'][8]+=1
        with self.assertRaises(AssertionError):preserved_install(a,b,'a'*40,'b'*40)
    def test_core_hdd_missing_keys_or_same_host_pid_do_not_pass(self):
        for kind in ('core','hdd','keys','host'):
            a,b=self.data()
            if kind=='core':b['core']['cid']='changed'
            elif kind=='hdd':b['recycleHdd']['new']='new'
            elif kind=='keys':b['protected'].pop(KEY)
            else:b['hosts'][next(iter(HOSTS))]['pid']=a['hosts'][next(iter(HOSTS))]['pid']
            with self.subTest(kind=kind),self.assertRaises(AssertionError):preserved_install(a,b,'a'*40,'b'*40)
