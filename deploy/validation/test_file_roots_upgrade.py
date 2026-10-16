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


if __name__ == '__main__': unittest.main()
