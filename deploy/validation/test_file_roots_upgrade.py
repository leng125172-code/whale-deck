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
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-copy', 'files-tree-move'})
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
            self.assertEqual(set(after['allowedActions']) - set(before['allowedActions']), {'files-tree-move'})
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


if __name__ == '__main__': unittest.main()
