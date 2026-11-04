"""Offline ACL-specific partial observer proofs; no live cut or mutation."""
import base64
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

import bound_container_recursive_acl_partial_cut as cut
from validate_container_recursive_acl import PATHS, file_acl, directory_acl
from verify_container_recursive_acl import file_version, acl_mode
from verify_container_recursive_permissions import digest, unsigned


def fixture():
    basic=[dict(tag='user_obj',id=None,permissions=6),dict(tag='group_obj',id=None,permissions=0),dict(tag='other',id=None,permissions=0)]
    before={p:dict(identity=[1,n+10,0o100600 if k=='File' else 0o40750,2100,3100,1 if k=='File' else 2,7 if k=='File' else 4096,1000,2000],access=copy.deepcopy(basic),default=[]) for n,(p,k) in enumerate(PATHS.items())}
    command=dict(containerId='a'*64,instanceId='123:instance',path='work/recursive-tree',expectedVersion='A'*64,aclType='access',fileEntries=file_acl(),directoryEntries=directory_acl())
    record=dict(operationId='b'*32,owner='C'*64,state='Applying',historyMayExist=True,historyVerified=True,historyDigest='D'*64,startedThrough=0,finishedThrough=0,
        plan=dict(command=command,entryCount=4,changeCount=4,fileCount=2,directoryCount=2,rootIdentity=before['']['identity']))
    relative='sub/中文.bin';path='work/recursive-tree/'+relative;old=before[relative];after=copy.deepcopy(before)
    after[relative]['identity'][2]=0o100640;after[relative]['identity'][8]+=1;after[relative]['access']=file_acl()
    cp=dict(schemaVersion=1,command=dict(containerId=command['containerId'],instanceId=command['instanceId'],path=path,expectedVersion=file_version(command,path,old['identity']),aclType='access',entries=file_acl()),kind='File',noChange=False,
        sourceIdentity=old['identity'],accessAcl=old['access'],defaultAcl=old['default'],targetMode=acl_mode(file_acl()),attributesDigest=digest(b'whaledeck-container-acl-attributes-v1\n',[['user.whaledeck-recursive',base64.b64encode(b'preserve ordinary attribute').decode('ascii')]]))
    cp['fingerprint']=digest(b'whaledeck-container-acl-plan-v1\n',unsigned(cp,'fingerprint'))
    child=dict(schemaVersion=1,operationId=cut.child_id(record['operationId'],0),owner=record['owner'],plan=cp,state='Completed',aclConfirmed=True,historyMayExist=True,historyVerified=True,historyDigest='E'*64,actualIdentity=after[relative]['identity'],version=file_version(command,path,after[relative]['identity']))
    return record,before,after,child


class RecursiveAclPartialProbeScope(unittest.TestCase):
    def test_only_actual_completed_acl_change_and_original_other_entries_prove_cut(self):
        r,b,a,c=fixture()
        with patch.object(cut,'receipt_json',return_value=c):v=cut.partial_proof(r,b,a)
        self.assertTrue(v['otherThreeEntriesOriginal']);self.assertEqual('sub/中文.bin',v['firstRelativePath']);self.assertEqual(a['sub/中文.bin']['identity'],v['firstChangedIdentity'])

    def test_before_first_entry_or_later_or_unverified_history_never_counts_partial(self):
        r,_,_,_=fixture()
        self.assertTrue(cut.partial_boundary({**r,'startedThrough':1}))
        for key,value in [('state','Partial'),('state','Completed'),('startedThrough',-1),('startedThrough',2),('finishedThrough',-1),('finishedThrough',1),('historyVerified',False),('historyMayExist',False),('historyDigest','a'*64)]:
            with self.subTest(key=key,value=value):self.assertFalse(cut.partial_boundary({**r,key:value}))
        with self.assertRaises(AssertionError):cut.partial_boundary({**r,'startedThrough':False})
        for key in ('entryCount','changeCount','fileCount','directoryCount'):
            bad=copy.deepcopy(r);bad['plan'][key]=float(bad['plan'][key])
            with self.subTest(key=key),self.assertRaises(AssertionError):cut.partial_boundary(bad)

    def test_acl_default_owner_identity_and_non_target_changes_cannot_pass(self):
        r,b,a,c=fixture()
        changes=[lambda x:x['sub/中文.bin']['access'].reverse(),lambda x:x['sub/中文.bin'].update(default=file_acl()),lambda x:x['sub/中文.bin']['identity'].__setitem__(3,0),lambda x:x['sub/中文.bin']['identity'].__setitem__(1,999),lambda x:x['sub/中文.bin']['identity'].__setitem__(8,2000)]
        changes += [lambda x,p=p:x[p]['identity'].__setitem__(8,3000) for p in ('one.bin','sub','')]
        for change in changes:
            value=copy.deepcopy(a);change(value)
            with patch.object(cut,'receipt_json',return_value=c),self.assertRaises(AssertionError):cut.partial_proof(r,b,value)

    def test_child_receipt_must_bind_manifest_command_history_and_actual_version(self):
        r,b,a,c=fixture()
        for key,value in [('owner','F'*64),('aclConfirmed',False),('historyVerified',False),('historyDigest',''),('version','0'*64),('operationId','0'*32),('actualIdentity',b['sub/中文.bin']['identity'])]:
            bad={**c,key:value}
            with self.subTest(key=key),patch.object(cut,'receipt_json',return_value=bad),self.assertRaises(AssertionError):cut.partial_proof(r,b,a)
        for phase in ('Preparing','Applying','Aborted','Unknown'):
            with patch.object(cut,'receipt_json',return_value={**c,'state':phase}):self.assertIsNone(cut.partial_proof(r,b,a))
        bad=copy.deepcopy(c);bad['plan']['command']['path']='work/one.bin'
        with patch.object(cut,'receipt_json',return_value=bad),self.assertRaises(AssertionError):cut.partial_proof(r,b,a)

    def test_arbitrary_host_path_rejects_before_signal_or_descriptor_access(self):
        row=dict(container_id='a'*64,instance_id='123:instance')
        with patch.object(cut.os,'geteuid',return_value=0),patch.object(cut.os,'pidfd_open') as opened:
            for source in ('/etc','/data/WhaleDeck/apps/not-owned','/data/WhaleDeck/apps/wdv-container-publish-'+'A'*32):
                with self.subTest(source=source),self.assertRaises(AssertionError):cut.freeze_partial_acl(row,source,123,{},lambda:{})
            opened.assert_not_called()
        code=(Path(__file__).parent/'bound_container_recursive_acl_partial_cut.py').read_text()
        for forbidden in ('SIGKILL','os.kill(','rmtree(','subprocess.run('):self.assertNotIn(forbidden,code)


if __name__=='__main__':unittest.main()
