"""Real Linux filesystem boundaries for the internal container move candidate.

The ContainerRoot binding here is synthetic; these are not RPC/UI or installed
Docker acceptance. All original bytes, retained inodes and failure evidence are
checked in disposable directories.
"""
import json
import os
import stat
import unittest
from unittest.mock import patch
import uuid

import test_container_copy_scope as copied

module = copied.common.common.load('whaledeck-container-move-scope.py', 'container_move_scope')
scope_type = module.container_move_scope_type(copied.scope_module.ContainerCopyScope)
moves = copied.common.common.load('whaledeck-tree-moves.py', 'container_move_tree_engine')


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'isolated Linux root required')
class LinuxContainerMoveScopeTests(unittest.TestCase):
    def setUp(self):
        self.base = copied.LinuxContainerCopyScopeTests(); self.base.setUp(); self.addCleanup(self.base.doCleanups)
        self.root, self.session, self.fixture = self.base.root, self.base.session, self.base.fixture
        self.state = self.fixture.fixture.base / 'moves'; self.state.mkdir(mode=0o700)
        self.operation = uuid.uuid4().hex

    def scope(self, command=None, **kwargs):
        return scope_type(self.fixture.u, self.fixture.publisher, self.session,
            self.base.command() if command is None else command, self.state, operation_id=self.operation, **kwargs)

    def request(self, plan): return {'operationId': self.operation, 'owner': copied.common.common.OWNER, 'plan': plan}

    def test_file_plan_requires_both_writable_ends_without_creating_private_state(self):
        scope = self.scope(); before = set(self.root.iterdir()); info = self.root.stat()
        plan = copied.operations.FileOperations(scope).plan(scope.file_command())
        self.assertEqual('move', plan['command']['kind'])
        self.assertEqual(before, set(self.root.iterdir())); self.assertEqual([], list(self.state.iterdir()))
        self.assertEqual(info.st_mtime_ns, self.root.stat().st_mtime_ns)

    def test_file_move_preserves_full_bytes_original_inode_and_metadata_and_is_not_repeated(self):
        scope = self.scope(); engine = copied.operations.FileOperations(scope)
        source = self.base.source; original = source.stat(); request = self.request(engine.plan(scope.file_command()))
        result = engine.apply(request); target = self.root / '复制.bin'
        retained = self.root / scope.PRIVATE / (self.operation + '.mutation')
        self.assertEqual('Completed', result['state']); self.assertTrue(result['sourceRetained'])
        self.assertFalse(source.exists()); self.assertEqual(self.base.content, target.read_bytes())
        self.assertEqual(self.base.content, retained.read_bytes()); self.assertEqual(original.st_ino, retained.stat().st_ino)
        self.assertEqual((original.st_uid, original.st_gid, stat.S_IMODE(original.st_mode), original.st_mtime_ns),
            (target.stat().st_uid, target.stat().st_gid, stat.S_IMODE(target.stat().st_mode), target.stat().st_mtime_ns))
        target_inode = target.stat().st_ino
        with patch.object(scope, 'exchange', side_effect=AssertionError('must observe only')):
            self.assertEqual(result, engine.apply(request))
        self.assertEqual(target_inode, target.stat().st_ino)
        self.assertFalse((self.root / copied.scope_module.ContainerCopyScope.PRIVATE).exists())

    def test_directory_move_preserves_full_tree_empty_directories_and_original_file_inodes(self):
        source = self.base.tree(); originals = {str(p.relative_to(source)):p.stat() for p in source.rglob('*')}
        scope = self.scope(self.base.command('项目', '项目副本'))
        engine = moves.FileTreeMoves(scope, copied.trees.FileTreeManifest, copied.copies.FileTreeCopies)
        request = self.request(engine.plan(scope.tree_command())); result = engine.move(request)
        self.assertEqual('Completed', result['state']); self.assertTrue(result['sourceRemoved']); self.assertTrue(result['sourceRetained'])
        target = self.root / '项目副本'; retained = self.root / scope.PRIVATE / (self.operation + '.tree-move')
        self.assertFalse(source.exists()); self.assertTrue((target / 'empty').is_dir())
        self.assertEqual(self.base.content, (target / '模块/代码.txt').read_bytes())
        self.assertEqual(self.base.content, (retained / '模块/代码.txt').read_bytes())
        for relative, before in originals.items():
            info = (target / relative).stat()
            self.assertEqual((stat.S_IMODE(before.st_mode),before.st_mtime_ns),(stat.S_IMODE(info.st_mode),info.st_mtime_ns))
            if stat.S_ISREG(before.st_mode): self.assertEqual(before.st_ino,(retained / relative).stat().st_ino)
        with patch.object(scope, 'exchange', side_effect=AssertionError('no replay')):
            self.assertEqual(result, engine.move(request))

    def test_readonly_source_refused_even_if_destination_is_writable(self):
        source = self.root / 'read'; source.mkdir(); (source / 'data').write_bytes(self.base.content)
        (self.root / 'write').mkdir()
        self.session.container = {'HostConfig': {'ReadonlyRootfs': True}, 'Mounts': [{'Destination':'/read','RW':False},{'Destination':'/write','RW':True}]}
        scope = self.scope(self.base.command('read/data','write/data'))
        with self.assertRaises(self.fixture.f.Rejected):copied.operations.FileOperations(scope).plan(scope.file_command())
        self.assertEqual(self.base.content,(source / 'data').read_bytes());self.assertEqual([],list((self.root / 'write').iterdir()))

    def test_existing_destination_never_overwritten(self):
        (self.root / '复制.bin').write_bytes(b'foreign')
        scope=self.scope()
        with self.assertRaises(self.fixture.f.Rejected):copied.operations.FileOperations(scope).plan(scope.file_command())
        self.assertEqual(b'foreign',(self.root / '复制.bin').read_bytes());self.assertEqual(self.base.content,self.base.source.read_bytes())

    def test_wrong_source_parent_rejected_without_source_or_target_changes(self):
        scope=self.scope(source_parent=[self.root.stat().st_dev,self.root.stat().st_ino+1])
        with self.assertRaisesRegex(self.fixture.f.Rejected,'SOURCE_PARENT_CHANGED'):scope.file_command()
        self.assertEqual(self.base.content,self.base.source.read_bytes());self.assertFalse((self.root / '复制.bin').exists())

    def test_source_swap_after_destination_publish_is_retained_without_deleting_foreign_bytes(self):
        scope=self.scope();engine=copied.operations.FileOperations(scope);request=self.request(engine.plan(scope.file_command()));save=engine.save
        def replace(state,record):
            save(state,record)
            if record['state']=='SourceApplying':
                self.base.source.rename(self.root/'original-saved');self.base.source.write_bytes(b'foreign')
        with patch.object(engine,'save',side_effect=replace):result=engine.apply(request)
        self.assertEqual('Unknown',result['state']);self.assertEqual(self.base.content,(self.root/'original-saved').read_bytes())
        self.assertEqual(b'foreign',(self.root/scope.PRIVATE/(self.operation+'.mutation')).read_bytes())
        self.assertEqual(self.base.content,(self.root/'复制.bin').read_bytes())

    def test_foreign_root_fd_cannot_be_used_for_retention(self):
        scope=self.scope();engine=copied.operations.FileOperations(scope);engine.plan(scope.file_command())
        outside=self.fixture.fixture.base/'foreign';outside.mkdir();(outside/'中文.bin').write_bytes(b'keep')
        wrong=os.open(outside,os.O_RDONLY|os.O_DIRECTORY);parent=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY)
        private=scope.staging(parent)
        try:
            with self.assertRaisesRegex(self.fixture.f.Rejected,'SOURCE_PARENT_CHANGED'):
                scope.exchange(wrong,'中文.bin',private,self.operation+'.mutation',1)
            self.assertEqual(b'keep',(outside/'中文.bin').read_bytes())
        finally:os.close(private);os.close(parent);os.close(wrong)

    def test_wrong_operation_and_overwrite_flags_cannot_move_anything(self):
        scope=self.scope();engine=copied.operations.FileOperations(scope);engine.plan(scope.file_command())
        parent=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY);private=scope.staging(parent)
        try:
            for name,flag in ((uuid.uuid4().hex+'.mutation',1),(self.operation+'.mutation',2)):
                with self.assertRaises(self.fixture.f.Rejected):scope.exchange(parent,'中文.bin',private,name,flag)
            self.assertEqual(self.base.content,self.base.source.read_bytes())
        finally:os.close(private);os.close(parent)

    def test_cancel_before_apply_is_permanent_and_keeps_source(self):
        scope=self.scope();engine=copied.operations.FileOperations(scope);request=self.request(engine.plan(scope.file_command()))
        self.assertEqual('Aborted',engine.discard(request)['state'])
        with patch.object(scope,'exchange',side_effect=AssertionError('late apply must not move')):
            self.assertEqual('Aborted',engine.apply(request)['state'])
        self.assertEqual(self.base.content,self.base.source.read_bytes());self.assertFalse((self.root/'复制.bin').exists())

    def test_directory_cancel_before_apply_is_permanent(self):
        self.base.tree();scope=self.scope(self.base.command('项目','项目副本'))
        engine=moves.FileTreeMoves(scope,copied.trees.FileTreeManifest,copied.copies.FileTreeCopies)
        request=self.request(engine.plan(scope.tree_command()));self.assertEqual('Aborted',engine.discard(request)['state'])
        self.assertEqual('Aborted',engine.move(request)['state']);self.assertTrue((self.root/'项目/模块/代码.txt').is_file())
        self.assertFalse((self.root/'项目副本').exists())

    def test_read_only_plan_cannot_create_retention_without_operation_binding(self):
        scope=scope_type(self.fixture.u,self.fixture.publisher,self.session,self.base.command(),self.state)
        copied.operations.FileOperations(scope).plan(scope.file_command());parent=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY)
        try:
            with self.assertRaisesRegex(self.fixture.f.Rejected,'OPERATION_REQUIRED'):scope.staging(parent)
        finally:os.close(parent)

    def test_wrong_inner_operation_is_refused_before_any_receipt_or_retention(self):
        scope=self.scope();engine=copied.operations.FileOperations(scope);request=self.request(engine.plan(scope.file_command()))
        with self.assertRaisesRegex(self.fixture.f.Rejected,'OPERATION_MISMATCH'):
            engine.apply({**request,'operationId':uuid.uuid4().hex})
        self.assertEqual([],list(self.state.iterdir()));self.assertEqual(self.base.content,self.base.source.read_bytes())

    def test_directory_external_child_change_after_publish_leaves_unknown_and_both_copies(self):
        self.base.tree();scope=self.scope(self.base.command('项目','项目副本'))
        engine=moves.FileTreeMoves(scope,copied.trees.FileTreeManifest,copied.copies.FileTreeCopies)
        request=self.request(engine.plan(scope.tree_command()))
        def change(phase):
            if phase=='DestinationCompleted':(self.root/'项目/模块/代码.txt').write_bytes(b'changed externally')
        engine.checkpoint=change
        with self.assertRaises(self.fixture.f.Rejected):engine.move(request)
        result=engine.lookup({'operationId':self.operation,'owner':request['owner']})
        self.assertEqual('Unknown',result['state'])
        self.assertEqual(b'changed externally',(self.root/'项目/模块/代码.txt').read_bytes())
        self.assertEqual(self.base.content,(self.root/'项目副本/模块/代码.txt').read_bytes())

    def test_source_parent_replacement_does_not_mutate_the_new_parent(self):
        work=self.root/'work';work.mkdir();(work/'source').write_bytes(self.base.content)
        scope=self.scope(self.base.command('work/source','复制.bin'));engine=copied.operations.FileOperations(scope)
        request=self.request(engine.plan(scope.file_command()));work.rename(self.root/'original-work');work.mkdir();(work/'source').write_bytes(b'foreign')
        with self.assertRaisesRegex(self.fixture.f.Rejected,'SOURCE_PARENT_CHANGED'):engine.apply(request)
        self.assertEqual(b'foreign',(work/'source').read_bytes());self.assertEqual(self.base.content,(self.root/'original-work/source').read_bytes())

    def test_directory_unknown_after_source_retention_never_resumes_mutation(self):
        self.base.tree();scope=self.scope(self.base.command('项目','项目副本'))
        engine=moves.FileTreeMoves(scope,copied.trees.FileTreeManifest,copied.copies.FileTreeCopies)
        request=self.request(engine.plan(scope.tree_command()))
        def interrupt(phase):
            if phase=='SourceRetained':raise OSError('isolated interruption')
        engine.checkpoint=interrupt
        with self.assertRaises(OSError):engine.move(request)
        with patch.object(scope,'exchange',side_effect=AssertionError('must not resume')):
            result=engine.lookup({'operationId':self.operation,'owner':request['owner']})
            self.assertEqual('Unknown',result['state']);self.assertEqual(result,engine.move(request))
        self.assertEqual(self.base.content,(self.root/'项目副本/模块/代码.txt').read_bytes())
        self.assertEqual(self.base.content,(self.root/scope.PRIVATE/(self.operation+'.tree-move')/'模块/代码.txt').read_bytes())

    def test_tree_symlinks_hardlinks_and_special_files_are_rejected(self):
        tree=self.base.tree();child=tree/'foreign'
        for kind in ('symlink','hardlink','fifo'):
            if kind=='symlink':child.symlink_to(self.base.source)
            elif kind=='hardlink':os.link(self.base.source,child)
            else:os.mkfifo(child)
            try:
                scope=self.scope(self.base.command('项目','项目副本'))
                engine=moves.FileTreeMoves(scope,copied.trees.FileTreeManifest,copied.copies.FileTreeCopies)
                with self.assertRaises(self.fixture.f.Rejected):engine.plan(scope.tree_command())
            finally:child.unlink()
        self.assertFalse((self.root/'项目副本').exists());self.assertEqual(self.base.content,self.base.source.read_bytes())

    def test_directory_move_preserves_each_mixed_owner_instead_of_normalizing_to_container_user(self):
        source=self.base.tree();expected={}
        for index,relative in enumerate(('', 'empty', '模块', '模块/代码.txt')):
            path=source/relative if relative else source
            os.chown(path,2100+index,3100+index)
            expected[relative]=(path.stat().st_uid,path.stat().st_gid,stat.S_IMODE(path.stat().st_mode),path.stat().st_mtime_ns)
        scope=self.scope(self.base.command('项目','项目副本'))
        engine=moves.FileTreeMoves(scope,copied.trees.FileTreeManifest,copied.copies.FileTreeCopies)
        request=self.request(engine.plan(scope.tree_command()));result=engine.move(request)
        self.assertEqual('Completed',result['state'])
        for relative,metadata in expected.items():
            info=(self.root/'项目副本'/relative).stat()
            self.assertEqual(metadata,(info.st_uid,info.st_gid,stat.S_IMODE(info.st_mode),info.st_mtime_ns))
        kept=self.root/scope.PRIVATE/(self.operation+'.tree-move')/'模块/代码.txt'
        self.assertEqual(expected['模块/代码.txt'][:2],(kept.stat().st_uid,kept.stat().st_gid))
        self.assertEqual(self.base.content,kept.read_bytes())


if __name__=='__main__':unittest.main()
