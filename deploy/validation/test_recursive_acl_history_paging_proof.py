"""Offline corruption and fixture boundaries, never installed UI acceptance."""
import ast
import copy
import hashlib
import os
from pathlib import Path
import tempfile
import types
import unittest

from recursive_acl_history_paging_proof import FILES,ORDER,PATHS,content,inventory,remove_proved,summary,prove_page,verify_capture
from recursive_acl_history_browser_proof import location,endpoint
from validate_recursive_acl_history_paging import wait_case,SOURCES
from validate_container_recursive_acl_agent import Probe
from verify_recursive_acl_history_paging import verify

ROOT=Path(__file__).resolve().parent

def fixture(index=0):
    b=dict(containerId='a'*64,instanceId='123:2026-11-04T00:00:00Z')
    access=[dict(tag='user_obj',id=None,permissions=6),dict(tag='user',id=0,permissions=0),dict(tag='group_obj',id=None,permissions=0),dict(tag='mask',id=None,permissions=4),dict(tag='other',id=None,permissions=0)]
    items=[dict(relativePath=p,plan=dict(kind=PATHS[p],sourceIdentity=[1,2,0o100640 if p else 0o40750],accessAcl=copy.deepcopy(access),defaultAcl=copy.deepcopy(access) if not p else [])) for p in ORDER]
    row=dict(request=dict(operationId='1'*32,plan=dict(command=dict(instanceId=b['instanceId']),manifestDigest='B'*64)))
    metadata=dict(historyDigest='A'*64,createdAtUnixMs=1700000000000,entries=items)
    s=summary(row,metadata);offset=25 if index%2 else 0;end=min(offset+25,26)
    snapshot=dict(location=location(b),summary=s,offset=offset,entries=[dict(relativePath=e['relativePath'],kind=e['plan']['kind'],originalMode=e['plan']['sourceIdentity'][2]&0o777,accessAcl=e['plan']['accessAcl'],defaultAcl=e['plan']['defaultAcl']) for e in items[offset:end]],complete=end==26,nextOffset=None if end==26 else 25)
    request=dict(method='GET',pathname=endpoint(b)+'/'+'1'*32,query=dict(instanceId=b['instanceId'],path='work/recursive-tree',expectedHistoryDigest='A'*64,offset=str(offset),take='25'),status=200,noStore=True)
    return b,row,metadata,copy.deepcopy(dict(index=index,subject='real-subject',snapshot=snapshot,request=request,screenshot=f'paging-{index}.png'))

class RecursiveAclHistoryPagingProofTests(unittest.TestCase):
    def test_capture_cannot_substitute_initial_state_before_any_filesystem_access(self):
        b=dict(original={'one':{'mode':0o640}})
        for replacement in ({'one':{'mode':0o600}},{'other':{'mode':0o640}},{'one':{'mode':False}}):
            with self.subTest(replacement=replacement),self.assertRaises(AssertionError):verify_capture(None,b,dict(before=replacement),None)
    def test_actual_twenty_five_plus_root_shape_next_back_next(self):
        self.assertEqual(25,len(FILES));self.assertEqual(26,len(ORDER));self.assertEqual('',ORDER[-1]);self.assertEqual('末项.bin',ORDER[-2]);self.assertEqual(26,len(set(ORDER)))
        for index in range(4):
            b,r,m,s=fixture(index);prove_page(s,b,r,m,index)
            self.assertEqual(1 if index%2 else 25,len(s['snapshot']['entries']))
            self.assertEqual(index%2==1,s['snapshot']['complete'])
        self.assertEqual({'tag':'user','id':0,'permissions':0},fixture()[3]['snapshot']['entries'][0]['accessAcl'][1])

    def test_offsets_continuation_and_root_cannot_be_shifted_or_duplicated(self):
        changes=[lambda s:s['snapshot'].update(offset=1),lambda s:s['snapshot'].update(offset=False),lambda s:s['snapshot'].update(nextOffset=26),
                 lambda s:s['snapshot'].update(complete=True),lambda s:s['snapshot']['entries'].pop(),lambda s:s['snapshot']['entries'].reverse(),
                 lambda s:s['snapshot']['entries'][0].update(relativePath=''),lambda s:s['snapshot']['entries'][0].update(originalMode=511),
                 lambda s:s['snapshot']['entries'][0]['accessAcl'][1].update(id=None),lambda s:s['snapshot']['entries'][0]['accessAcl'][1].update(permissions=False)]
        for change in changes:
            b,r,m,s=fixture();change(s)
            with self.subTest(change=changes.index(change)),self.assertRaises(AssertionError):prove_page(s,b,r,m,0)

    def test_last_page_is_one_root_with_null_end_and_original_default(self):
        for change in (lambda s:s['snapshot'].update(nextOffset=26),lambda s:s['snapshot']['entries'][0].update(kind='File'),lambda s:s['snapshot']['entries'][0]['defaultAcl'].clear()):
            b,r,m,s=fixture(1);s=copy.deepcopy(s);change(s)
            with self.assertRaises(AssertionError):prove_page(s,b,r,m,1)

    def test_request_range_method_digest_scope_and_explicit_take_are_bound(self):
        for change in (lambda s:s['request']['query'].update(take='1'),lambda s:s['request']['query'].update(offset='0'),lambda s:s['request']['query'].update(expectedHistoryDigest='D'*64),lambda s:s['request']['query'].update(instanceId='other'),lambda s:s['request'].update(method='POST'),lambda s:s['request'].update(noStore=False)):
            b,r,m,s=fixture(1);change(s)
            with self.assertRaises(AssertionError):prove_page(s,b,r,m,1)

    def test_invalid_selection_fails_before_dependency_or_process_access(self):
        with tempfile.TemporaryDirectory() as folder:
            for name in ('../escape','paging-0','paging-first/result','paging-cancel'):
                with self.assertRaises(AssertionError):wait_case(None,Path(folder),name)
        for value in ('../escape','recursive-acl-history-paging-'+'A'*32,'container-recursive-acl-browser-'+'a'*32):
            with self.assertRaises(AssertionError):verify(value)
        for mode in ('shell','container-acl','container-recursive-acl;echo'):
            with self.assertRaises(AssertionError):Probe(None,None,None,mode)

    def test_content_is_fixed_bounded_and_resource_specific(self):
        self.assertEqual(65543,len(content('a'*64)));self.assertNotEqual(content('a'*64),content('b'*64))
        for value in ('../escape','a'*63,'A'*64):
            with self.assertRaises(AssertionError):content(value)

    def test_tree_rejects_unexpected_file_symlink_hardlink_or_changed_bytes_before_cleanup(self):
        def frame(s):return (s.st_dev,s.st_ino,s.st_mode,s.st_uid,s.st_gid,s.st_nlink,s.st_size,s.st_mtime_ns,s.st_ctime_ns)
        reader=types.SimpleNamespace(fingerprint=frame);acl=types.SimpleNamespace(XATTRS={},read_acl=lambda *_:[])
        for kind in ('extra','symlink','hardlink','bytes'):
            with tempfile.TemporaryDirectory() as folder:
                root=Path(folder)/'tree';root.mkdir();data=content('a'*64)
                for name in FILES:(root/name).write_bytes(data)
                for relative in PATHS:os.setxattr(root/relative,'user.whaledeck-recursive',b'preserve ordinary attribute')
                before=inventory(root,reader,data,acl)
                if kind=='extra':(root/'extra').write_bytes(b'extra')
                elif kind=='symlink':(root/FILES[0]).unlink();(root/FILES[0]).symlink_to(root/FILES[1])
                elif kind=='hardlink':(root/FILES[0]).unlink();os.link(root/FILES[1],root/FILES[0])
                else:(root/FILES[0]).write_bytes(b'changed')
                with self.subTest(kind=kind),self.assertRaises(AssertionError):remove_proved(root,reader,data,acl,before)
                self.assertEqual(25,len([p for p in root.iterdir() if p.name in FILES]))

    def test_only_real_controls_no_interception_take_override_or_extra_capture_loop(self):
        source=(ROOT/'validate_recursive_acl_history_paging.mjs').read_text()
        for forbidden in ('.route(','.addCookies(','storageState:','localStorage.setItem','sessionStorage.setItem','request.post(','request.put(','take:\'1\''):
            self.assertNotIn(forbidden,source)
        for required in ('下一页前像','上一页前像','value.entries.length,index%2?1:25','realSnapshotPagination:true','inventoryMultipleCaptures:false','writeSignal','waitSignal','logoutFromUserMenu'):
            self.assertIn(required,source)
        self.assertTrue(all((ROOT/name).is_file() for name in SOURCES))
        probe=(ROOT/'TerminalProbe/ContainerRecursiveAclProbe.cs').read_text()
        for required in ('paging?26U:4U','paging?25U:2U','paging?1U:2U','action is "plan" or "apply" or "quit"'):
            self.assertIn(required,probe)

    def test_queries_are_read_only_and_synthetic_data_never_replaces_audit_save(self):
        tree=ast.parse((ROOT/'validate_recursive_acl_history_paging.py').read_text())
        for node in ast.walk(tree):
            if isinstance(node,ast.Call) and isinstance(node.func,ast.Name) and node.func.id=='query':
                self.assertTrue(node.args[0].value.startswith('SELECT '));self.assertNotRegex(node.args[0].value,r'(?i)\b(UPDATE|DELETE|INSERT|ALTER|TRUNCATE)\b')

if __name__=='__main__':unittest.main()
