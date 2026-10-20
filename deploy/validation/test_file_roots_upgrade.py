"""Actual root ownership/atomic configuration tests in one disposable folder."""
import importlib.util
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('file_roots_upgrade', Path(__file__).resolve().parents[1] / 'host/upgrade-file-roots.py')
upgrade = importlib.util.module_from_spec(spec); spec.loader.exec_module(upgrade)


@unittest.skipUnless(os.geteuid() == 0, 'Requires real Linux ownership and rename semantics')
class FileRootUpgradeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-file-roots-')
        self.folder = Path(self.temporary.name); self.current = self.folder / 'file-roots.json'
        source = Path(__file__).resolve().parents[1] / 'host'
        self.baseline = json.loads((source / 'file-roots.v1.json').read_text())
        self.desired = json.loads((source / 'file-roots.json').read_text())
        self.current.write_text(json.dumps(self.baseline)); self.current.chmod(0o640); os.chown(self.current, 0, 12345)

    def tearDown(self): self.temporary.cleanup()
    def apply(self): return upgrade.upgrade(self.current, self.baseline, self.desired)

    def test_only_known_default_is_upgraded_once_with_original_backup_and_metadata(self):
        original = self.current.read_bytes(); self.assertEqual(self.apply(), 'upgraded-defaults')
        self.assertEqual(json.loads(self.current.read_text()), self.desired)
        info = self.current.stat(); self.assertEqual(info.st_gid, 12345); self.assertEqual(stat.S_IMODE(info.st_mode), 0o640)
        backup = self.folder / 'file-roots.json.pre-mutations.json'; self.assertEqual(backup.read_bytes(), original)
        self.assertEqual(stat.S_IMODE(backup.stat().st_mode), 0o600)
        inode = self.current.stat().st_ino; self.assertEqual(self.apply(), 'unchanged'); self.assertEqual(self.current.stat().st_ino, inode)

    def test_tightened_or_custom_configuration_is_never_automatically_expanded(self):
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'), ('allowedActions', ['files-list'])):
            custom = json.loads(json.dumps(self.baseline)); custom[0][field] = value; self.current.write_text(json.dumps(custom))
            original = self.current.read_bytes(); self.assertEqual(self.apply(), 'custom-preserved'); self.assertEqual(self.current.read_bytes(), original)
        self.assertFalse((self.folder / 'file-roots.json.pre-mutations.json').exists())

    def test_unsafe_permissions_owner_symlink_and_hardlink_are_rejected(self):
        self.current.chmod(0o666)
        with self.assertRaises(ValueError): self.apply()
        self.current.chmod(0o640); os.chown(self.current, 12345, 12345)
        with self.assertRaises(ValueError): self.apply()
        os.chown(self.current, 0, 12345); os.link(self.current, self.folder / 'linked')
        with self.assertRaises(ValueError): self.apply()
        (self.folder / 'linked').unlink(); self.current.rename(self.folder / 'real'); self.current.symlink_to(self.folder / 'real')
        with self.assertRaises(OSError): self.apply()

    def test_duplicate_fields_and_conflicting_backup_fail_without_overwriting(self):
        self.current.write_text('[{"id":"a","id":"b"}]')
        with self.assertRaises(ValueError): self.apply()
        self.current.write_text(json.dumps(self.baseline)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-mutations.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): self.apply()
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_text(), '[]')

    def test_failed_atomic_replacement_leaves_current_policy_unchanged(self):
        original = self.current.read_bytes()
        with patch.object(os, 'replace', side_effect=OSError('synthetic-rename-failure')), self.assertRaises(OSError): self.apply()
        self.assertEqual(self.current.read_bytes(), original)
        self.assertFalse(any(item.name.startswith('.file-roots.json-') for item in self.folder.iterdir()))

    def test_previous_mutation_default_upgrades_without_conflicting_with_its_old_backup(self):
        previous = json.loads((Path(__file__).resolve().parents[1] / 'host/file-roots.v1.25.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        old = self.folder / 'file-roots.json.pre-mutations.json'; old.write_text(json.dumps(self.baseline)); old.chmod(0o600)
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-archives.json'), 'upgraded-defaults')
        self.assertEqual(json.loads(old.read_bytes()), self.baseline)
        self.assertEqual((self.folder / 'file-roots.json.pre-archives.json').read_bytes(), original)
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-archives.json'), 'unchanged')

    def test_tightened_previous_default_is_preserved_and_new_backup_conflict_fails_closed(self):
        previous = json.loads((Path(__file__).resolve().parents[1] / 'host/file-roots.v1.25.json').read_text())
        current = json.loads(json.dumps(previous)); current[0]['allowedActions'].remove('files-mutate')
        self.current.write_text(json.dumps(current)); original = self.current.read_bytes()
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-archives.json'), 'custom-preserved')
        self.assertEqual(self.current.read_bytes(), original)
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        conflict = self.folder / 'file-roots.json.pre-archives.json'; conflict.write_text('[]'); conflict.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade(self.current, previous, self.desired, '.pre-archives.json')
        self.assertEqual(self.current.read_bytes(), original)


    def test_archive_defaults_gain_only_explicit_tree_permission_with_a_separate_backup(self):
        previous = json.loads((Path(__file__).resolve().parents[1] / 'host/file-roots.v1.26.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        prior = self.folder / 'file-roots.json.pre-archives.json'; prior.write_text('[]'); prior.chmod(0o600)
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-tree-copy.json'), 'upgraded-defaults')
        self.assertEqual((self.folder / 'file-roots.json.pre-tree-copy.json').read_bytes(), original)
        self.assertEqual(prior.read_text(), '[]')
        for before, after in zip(previous, json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-copy', 'files-tree-move', 'files-tree-pack', 'files-tree-unpack', 'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key:value for key,value in before.items() if key != 'allowedActions'}, {key:value for key,value in after.items() if key != 'allowedActions'})
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-tree-copy.json'), 'unchanged')

    def test_custom_archive_root_is_not_silently_granted_bulk_copy(self):
        previous = json.loads((Path(__file__).resolve().parents[1] / 'host/file-roots.v1.26.json').read_text())
        custom = json.loads(json.dumps(previous)); custom[0]['allowedActions'].remove('files-upload')
        self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
        self.assertEqual(upgrade.upgrade(self.current, previous, self.desired, '.pre-tree-copy.json'), 'custom-preserved')
        self.assertEqual(self.current.read_bytes(), original)
        self.assertFalse((self.folder / 'file-roots.json.pre-tree-copy.json').exists())

    def test_copy_defaults_gain_only_move_permission_with_original_backup_and_cli_selection(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.27.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        old = self.folder / 'file-roots.json.pre-tree-copy.json'; old.write_text('[]'); old.chmod(0o600)
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        self.assertEqual((self.folder / 'file-roots.json.pre-tree-move.json').read_bytes(), original); self.assertEqual(old.read_bytes(), b'[]')
        for before, after in zip(previous, json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-move','files-tree-pack','files-tree-unpack','files-tree-trash','files-tree-restore','files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key:value for key,value in before.items() if key != 'allowedActions'}, {key:value for key,value in after.items() if key != 'allowedActions'})
        inode = self.current.stat().st_ino
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged'); self.assertEqual(self.current.stat().st_ino, inode)

    def test_custom_copy_roots_and_conflicting_move_backup_are_not_overwritten(self):
        source = Path(__file__).resolve().parents[1] / 'host'; previous = json.loads((source / 'file-roots.v1.27.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'), ('allowedActions', ['files-list'])):
            custom = json.loads(json.dumps(previous)); custom[0][field] = value; self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
            self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved'); self.assertEqual(self.current.read_bytes(), original)
        self.assertFalse((self.folder / 'file-roots.json.pre-tree-move.json').exists())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-move.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')


    def test_known_move_defaults_only_add_packing_and_preserve_original_backup(self):
        source=Path(__file__).resolve().parents[1]/'host'; previous=json.loads((source/'file-roots.v1.29.json').read_text())
        self.current.write_text(json.dumps(previous)); original=self.current.read_bytes()
        self.assertEqual(upgrade.upgrade_defaults(self.current,source),'upgraded-defaults')
        backup=self.folder/'file-roots.json.pre-tree-pack.json'; self.assertEqual(backup.read_bytes(),original)
        for before,after in zip(previous,json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions'])-set(before['allowedActions']),{'files-tree-pack','files-tree-unpack','files-tree-trash','files-tree-restore','files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key:item for key,item in before.items() if key!='allowedActions'},{key:item for key,item in after.items() if key!='allowedActions'})
        inode=self.current.stat().st_ino; self.assertEqual(upgrade.upgrade_defaults(self.current,source),'unchanged'); self.assertEqual(self.current.stat().st_ino,inode)

    def test_custom_move_roots_and_conflicting_packing_backup_are_preserved(self):
        source=Path(__file__).resolve().parents[1]/'host'; previous=json.loads((source/'file-roots.v1.29.json').read_text())
        for field,value in (('ownerUid',12345),('path','/data/WhaleDeck/custom'),('allowedActions',['files-list'])):
            custom=json.loads(json.dumps(previous)); custom[0][field]=value; self.current.write_text(json.dumps(custom)); original=self.current.read_bytes()
            self.assertEqual(upgrade.upgrade_defaults(self.current,source),'custom-preserved'); self.assertEqual(self.current.read_bytes(),original)
        self.current.write_text(json.dumps(previous)); original=self.current.read_bytes()
        backup=self.folder/'file-roots.json.pre-tree-pack.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError,'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade_defaults(self.current,source)
        self.assertEqual(self.current.read_bytes(),original); self.assertEqual(backup.read_bytes(),b'[]')

    def test_known_packing_defaults_add_only_unpack_once_and_preserve_original(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.30.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        self.assertEqual((self.folder / 'file-roots.json.pre-tree-unpack.json').read_bytes(), original)
        for before, after in zip(previous, json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-unpack', 'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key: item for key, item in before.items() if key != 'allowedActions'}, {key: item for key, item in after.items() if key != 'allowedActions'})
        inode = self.current.stat().st_ino
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged'); self.assertEqual(self.current.stat().st_ino, inode)

    def test_custom_packing_defaults_and_conflicting_unpack_backup_are_preserved(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.30.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'), ('allowedActions', ['files-list'])):
            custom = json.loads(json.dumps(previous)); custom[0][field] = value
            self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
            self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved'); self.assertEqual(self.current.read_bytes(), original)
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-unpack.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')

    def test_known_unpack_defaults_add_recycle_and_restore_once_with_precise_backup(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.31.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        backup = self.folder / 'file-roots.json.pre-tree-trash.json'
        self.assertEqual(backup.read_bytes(), original); self.assertEqual(stat.S_IMODE(backup.stat().st_mode), 0o600)
        for before, after in zip(previous, json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-trash', 'files-tree-restore', 'files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key: item for key, item in before.items() if key != 'allowedActions'}, {key: item for key, item in after.items() if key != 'allowedActions'})
        inode = self.current.stat().st_ino
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged'); self.assertEqual(self.current.stat().st_ino, inode)

    def test_custom_unpack_defaults_and_conflicting_recycle_backup_remain_untouched(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.31.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'), ('allowedActions', ['files-list'])):
            custom = json.loads(json.dumps(previous)); custom[0][field] = value
            self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
            self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved'); self.assertEqual(self.current.read_bytes(), original)
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-trash.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')

    def test_known_recycling_defaults_gain_only_restore_with_original_backup(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.32.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        old = self.folder / 'file-roots.json.pre-tree-trash.json'; old.write_text('[]'); old.chmod(0o600)
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        backup = self.folder / 'file-roots.json.pre-tree-restore.json'
        self.assertEqual(backup.read_bytes(), original); self.assertEqual(stat.S_IMODE(backup.stat().st_mode), 0o600)
        self.assertEqual(old.read_bytes(), b'[]')
        for before, after in zip(previous, json.loads(self.current.read_bytes())):
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-restore', 'files-tree-overwrite-restore', 'files-tree-purge'})
            self.assertEqual({key: value for key, value in before.items() if key != 'allowedActions'}, {key: value for key, value in after.items() if key != 'allowedActions'})
        inode = self.current.stat().st_ino
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged'); self.assertEqual(self.current.stat().st_ino, inode)

    def test_custom_recycling_policy_and_conflicting_restore_backup_are_preserved(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.32.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'), ('allowedActions', ['files-list'])):
            custom = json.loads(json.dumps(previous)); custom[0][field] = value
            self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
            self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved'); self.assertEqual(self.current.read_bytes(), original)
        self.assertFalse((self.folder / 'file-roots.json.pre-tree-restore.json').exists())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-restore.json'; backup.write_text('[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, 'FILES_REGISTRY_BACKUP_CONFLICT'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')

    def test_known_restoring_defaults_gain_overwrite_restore_and_purge_with_original_backup_permissions_and_idempotence(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.33.json').read_text())
        self.assertEqual(len(previous), len(self.desired))
        for before, after in zip(previous, self.desired):
            self.assertEqual(after, {**before, 'allowedActions': before['allowedActions'] + ['files-tree-overwrite-restore', 'files-tree-purge']})
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        old = self.folder / 'file-roots.json.pre-tree-restore.json'; old.write_bytes(b'[]'); old.chmod(0o600)
        old_identity = (old.stat().st_ino, old.stat().st_mtime_ns, old.read_bytes())
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        self.assertEqual(json.loads(self.current.read_bytes()), self.desired)
        info = self.current.stat()
        self.assertEqual((info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode)), (0, 12345, 0o640))
        backup = self.folder / 'file-roots.json.pre-tree-overwrite-restore.json'
        self.assertEqual(backup.read_bytes(), original)
        backup_info = backup.stat()
        self.assertEqual((backup_info.st_uid, backup_info.st_gid, stat.S_IMODE(backup_info.st_mode), backup_info.st_nlink), (0, 12345, 0o600, 1))
        self.assertEqual((old.stat().st_ino, old.stat().st_mtime_ns, old.read_bytes()), old_identity)
        identities = (info.st_ino, info.st_mtime_ns, backup_info.st_ino, backup_info.st_mtime_ns)
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged')
        self.assertEqual((self.current.stat().st_ino, self.current.stat().st_mtime_ns, backup.stat().st_ino, backup.stat().st_mtime_ns), identities)
        self.assertEqual(backup.read_bytes(), original)
        self.assertFalse(any(item.name.startswith('.file-roots.json-') for item in self.folder.iterdir()))

    def test_custom_restoring_roots_never_gain_overwrite_restore_or_allocate_backup(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.33.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'),
                ('allowedActions', [action for action in previous[0]['allowedActions'] if action != 'files-tree-restore']),
                ('allowedActions', previous[0]['allowedActions'] + ['files-tree-overwrite-restore'])):
            with self.subTest(field=field, value=value):
                custom = json.loads(json.dumps(previous)); custom[0][field] = value
                self.current.write_text(json.dumps(custom)); original = self.current.read_bytes()
                identity = (self.current.stat().st_ino, self.current.stat().st_mtime_ns)
                self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved')
                self.assertEqual(self.current.read_bytes(), original)
                self.assertEqual((self.current.stat().st_ino, self.current.stat().st_mtime_ns), identity)
                self.assertFalse((self.folder / 'file-roots.json.pre-tree-overwrite-restore.json').exists())

    def test_conflicting_overwrite_restore_backup_fails_closed_and_preserves_both_files(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.33.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-overwrite-restore.json'; backup.write_bytes(b'[]'); backup.chmod(0o600)
        before = (self.current.stat().st_ino, backup.stat().st_ino, backup.stat().st_mtime_ns)
        with self.assertRaisesRegex(ValueError, '^FILES_REGISTRY_BACKUP_CONFLICT$'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')
        self.assertEqual((self.current.stat().st_ino, backup.stat().st_ino, backup.stat().st_mtime_ns), before)
        self.assertFalse(any(item.name.startswith('.file-roots.json-') for item in self.folder.iterdir()))

    def test_matching_existing_overwrite_restore_backup_is_reused_without_rewriting_history(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.33.json').read_text())
        self.current.write_text(json.dumps(previous))
        backup = self.folder / 'file-roots.json.pre-tree-overwrite-restore.json'
        original = json.dumps(previous, ensure_ascii=False, separators=(',', ':')).encode()
        backup.write_bytes(original); backup.chmod(0o600)
        identity = (backup.stat().st_ino, backup.stat().st_mtime_ns, stat.S_IMODE(backup.stat().st_mode))
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        self.assertEqual(json.loads(self.current.read_bytes()), self.desired)
        self.assertEqual(backup.read_bytes(), original)
        self.assertEqual((backup.stat().st_ino, backup.stat().st_mtime_ns, stat.S_IMODE(backup.stat().st_mode)), identity)

    def test_known_overwrite_defaults_add_only_purge_with_backup_metadata_and_idempotence(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.35.json').read_text())
        self.assertEqual(len(previous), len(self.desired))
        for before, after in zip(previous, self.desired):
            self.assertEqual(after, {**before, 'allowedActions': before['allowedActions'] + ['files-tree-purge']})
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        older = self.folder / 'file-roots.json.pre-tree-overwrite-restore.json'
        older.write_bytes(b'[]'); older.chmod(0o600)
        old_identity = (older.stat().st_ino, older.stat().st_mtime_ns, older.read_bytes())
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        backup = self.folder / 'file-roots.json.pre-tree-purge.json'
        self.assertEqual(backup.read_bytes(), original)
        self.assertEqual(json.loads(self.current.read_bytes()), self.desired)
        self.assertEqual((self.current.stat().st_uid, self.current.stat().st_gid, stat.S_IMODE(self.current.stat().st_mode)), (0, 12345, 0o640))
        self.assertEqual((backup.stat().st_uid, backup.stat().st_gid, stat.S_IMODE(backup.stat().st_mode), backup.stat().st_nlink), (0, 12345, 0o600, 1))
        identities = (self.current.stat().st_ino, self.current.stat().st_mtime_ns, backup.stat().st_ino, backup.stat().st_mtime_ns)
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'unchanged')
        self.assertEqual((self.current.stat().st_ino, self.current.stat().st_mtime_ns, backup.stat().st_ino, backup.stat().st_mtime_ns), identities)
        self.assertEqual((older.stat().st_ino, older.stat().st_mtime_ns, older.read_bytes()), old_identity)

    def test_custom_or_mixed_overwrite_defaults_do_not_gain_purge(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.35.json').read_text())
        for field, value in (('ownerUid', 12345), ('path', '/data/WhaleDeck/custom'),
                ('allowedActions', [action for action in previous[0]['allowedActions'] if action != 'files-tree-overwrite-restore']),
                ('allowedActions', previous[0]['allowedActions'] + ['files-tree-purge'])):
            with self.subTest(field=field):
                custom = json.loads(json.dumps(previous)); custom[0][field] = value
                self.current.write_text(json.dumps(custom)); original = self.current.read_bytes(); identity = self.current.stat().st_ino
                self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'custom-preserved')
                self.assertEqual(self.current.read_bytes(), original); self.assertEqual(self.current.stat().st_ino, identity)
                self.assertFalse((self.folder / 'file-roots.json.pre-tree-purge.json').exists())

    def test_conflicting_purge_backup_and_failed_atomic_replace_preserve_current_policy(self):
        source = Path(__file__).resolve().parents[1] / 'host'
        previous = json.loads((source / 'file-roots.v1.35.json').read_text())
        self.current.write_text(json.dumps(previous)); original = self.current.read_bytes()
        backup = self.folder / 'file-roots.json.pre-tree-purge.json'; backup.write_bytes(b'[]'); backup.chmod(0o600)
        with self.assertRaisesRegex(ValueError, '^FILES_REGISTRY_BACKUP_CONFLICT$'): upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), b'[]')
        backup.unlink()
        with patch.object(os, 'replace', side_effect=OSError('synthetic-rename-failure')), self.assertRaises(OSError):
            upgrade.upgrade_defaults(self.current, source)
        self.assertEqual(self.current.read_bytes(), original); self.assertEqual(backup.read_bytes(), original)
        before = (backup.stat().st_ino, backup.stat().st_mtime_ns, backup.read_bytes())
        self.assertEqual(upgrade.upgrade_defaults(self.current, source), 'upgraded-defaults')
        self.assertEqual((backup.stat().st_ino, backup.stat().st_mtime_ns, backup.read_bytes()), before)
        self.assertFalse(any(item.name.startswith('.file-roots.json-') for item in self.folder.iterdir()))

if __name__ == '__main__': unittest.main()
