"""Pure filesystem/fake Engine tests; never invoke root helpers or a real Docker daemon."""
import contextlib
from collections import namedtuple
import importlib.util
import io
import json
import os
import tarfile
import tempfile
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[1] / 'deploy/host/whaledeck-application-backup.py'
SPEC = importlib.util.spec_from_file_location('application_backup', SOURCE)
backup = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(backup)
REAL_LOCK = backup.lock


class BackupHelperTests(unittest.TestCase):
    def create_archives(self, count=3):
        identities = [str(index) * 32 for index in range(1, count + 1)]
        for identity in identities:
            backup.run('demo', identity, 'live', backup.plan('demo', 'live')['fingerprint'])
        return identities

    def test_retention_removes_only_expired_sealed_pairs_and_keeps_newest(self):
        identities = self.create_archives()
        unknown = self.target / 'demo/unrelated.txt'
        unknown.write_text('retain me')
        planned = backup.cleanup_plan('demo', 14, 1)
        self.assertEqual(set(planned['removeBackupIds']), set(identities[:-1]))
        result = backup.cleanup('demo', 'd' * 32, 14, 1, planned['fingerprint'])
        self.assertEqual(set(result['removedBackupIds']), set(identities[:-1]))
        self.assertEqual(backup.archive_catalog('demo')[0]['backupId'], identities[-1])
        self.assertTrue(unknown.exists())
        self.assertEqual((self.config / 'data/probe.txt').read_text(), 'bind-proof')
        self.assertIn(self.marker, (self.config / '.env').read_text())
        self.assertFalse(self.member['State']['Paused'])

    def test_age_retention_still_preserves_one_archive(self):
        identities = self.create_archives()
        for index, identity in enumerate(identities):
            path = self.target / 'demo' / (identity + '.json')
            metadata = backup.read_json(path)
            metadata['createdAtUnix'] = int(backup.time.time()) - 30 * 86400 + index
            metadata['createdAtUnixNs'] = metadata['createdAtUnix'] * 1_000_000_000
            backup.private_json(path, metadata)
        planned = backup.cleanup_plan('demo', 14, 14)
        self.assertEqual(set(planned['removeBackupIds']), set(identities[:-1]))
        self.assertEqual(planned['retainedCount'], 1)

    def test_stale_retention_plan_does_not_delete_any_archive(self):
        identities = self.create_archives(2)
        planned = backup.cleanup_plan('demo', 14, 1)
        backup.run('demo', '3' * 32, 'live', backup.plan('demo', 'live')['fingerprint'])
        with self.assertRaises(backup.BackupError): backup.cleanup('demo', 'd' * 32, 14, 1, planned['fingerprint'])
        self.assertTrue((self.target / 'demo' / (identities[0] + '.tar.gz')).exists())

    def test_unverified_or_mismatched_metadata_blocks_retention(self):
        identities = self.create_archives(2)
        path = self.target / 'demo' / (identities[0] + '.json')
        metadata = backup.read_json(path)
        metadata['verified'] = False
        backup.private_json(path, metadata)
        with self.assertRaises(backup.BackupError): backup.cleanup_plan('demo', 14, 1)
        self.assertTrue((self.target / 'demo' / (identities[0] + '.tar.gz')).exists())

    def test_cleanup_recovery_finishes_only_the_started_pair_and_preserves_remaining(self):
        identities = self.create_archives()
        candidate = backup.archive_catalog('demo')[-1]
        job = 'd' * 32
        backup.private_json(backup.journal_path(job), {'slug': 'demo', 'job': job, 'paused': [], 'cleanup': True,
            'pendingCleanup': candidate, 'removedBackupIds': [], 'retentionDays': 14, 'retentionCount': 1})
        (self.target / 'demo' / (candidate['backupId'] + '.tar.gz')).unlink()
        backup.recover_job(job)
        receipt = backup.read_json(self.state / (job + '.done.json'))
        self.assertEqual(receipt['state'], 'Failed')
        self.assertEqual(receipt['result']['removedBackupIds'], [identities[0]])
        self.assertFalse((self.target / 'demo' / (identities[0] + '.json')).exists())
        self.assertEqual(len(backup.archive_catalog('demo')), 2)

    def test_recovery_refuses_a_replaced_archive_identity(self):
        self.create_archives(2)
        candidate = backup.archive_catalog('demo')[-1]
        artifact = self.target / 'demo' / (candidate['backupId'] + '.tar.gz')
        artifact.write_bytes(artifact.read_bytes() + b'changed')
        journal = {'slug': 'demo', 'pendingCleanup': candidate, 'removedBackupIds': []}
        with self.assertRaises(backup.BackupError): backup.finish_cleanup_entry(journal)
        self.assertTrue(artifact.exists())

    def test_invalid_archive_timestamp_blocks_retention_without_deletion(self):
        identities = self.create_archives(2)
        path = self.target / 'demo' / (identities[0] + '.json')
        for timestamp in ('invalid', -1, 0, True):
            metadata = backup.read_json(path)
            metadata['createdAtUnixNs'] = timestamp
            backup.private_json(path, metadata)
            with self.assertRaises(backup.BackupError): backup.cleanup_plan('demo', 14, 1)
            self.assertTrue((self.target / 'demo' / (identities[0] + '.tar.gz')).exists())

    def test_invalid_capability_key_blocks_cleanup_and_verification_plans(self):
        identities = self.create_archives(2)
        backup.KEY.write_bytes(b'invalid')
        with self.assertRaises(backup.BackupError): backup.cleanup_plan('demo', 14, 1)
        with self.assertRaises(backup.BackupError): backup.verify_plan('demo', identities[0])
        self.assertEqual(len(list((self.target / 'demo').glob('*.tar.gz'))), 2)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-backup-unit-')
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.apps = root / 'apps'
        self.target = root / 'hdd/whaledeck/application-backups'
        self.state = root / 'state'
        for directory in (self.apps / 'demo/data', self.target, self.state):
            directory.mkdir(parents=True)
        self.config = self.apps / 'demo'
        self.marker = 'SYNTHETIC_private_value_$NO_INTERPOLATION'
        (self.config / '.env').write_text('KEY=' + self.marker, encoding='utf-8')
        (self.config / 'compose.yml').write_text('name: whaledeck-app-demo', encoding='utf-8')
        (self.config / 'manifest.json').write_text('{"slug":"demo"}', encoding='utf-8')
        (self.config / 'data/probe.txt').write_text('bind-proof', encoding='utf-8')
        self.volume_name = 'whaledeck-app-demo-cache'
        self.volume_path = root / 'volumes' / self.volume_name / '_data'
        self.volume_path.mkdir(parents=True)
        (self.volume_path / 'probe.txt').write_text('volume-proof', encoding='utf-8')
        self.volume = {'Name': self.volume_name, 'Driver': 'local', 'Options': {},
            'Labels': {'com.docker.compose.project': 'whaledeck-app-demo'},
            'Mountpoint': str(self.volume_path), 'CreatedAt': '2026-10-13'}
        self.identity = 'a' * 64
        self.member = {'Id': self.identity, 'Name': '/whaledeck-app-demo', 'Image': 'sha256:' + 'b' * 64,
            'Config': {'Env': [self.marker], 'Labels': {'com.docker.compose.project': 'whaledeck-app-demo'}},
            'State': {'Running': True, 'Paused': False}, 'Mounts': [
                {'Type': 'bind', 'Source': str(self.config / 'data')},
                {'Type': 'volume', 'Name': self.volume_name}]}
        self.containers = [self.member]
        self.calls = []
        registry = root / 'resources.json'
        registry.write_text(json.dumps([{'id': 'whaledeck.apps', 'path': str(self.apps), 'allowedActions': ['backup']}]))
        key = root / 'key'
        key.write_bytes(b'k' * 32)
        for name, value in {'APPS': self.apps, 'TARGET': self.target, 'STATE': self.state,
            'REGISTRY': registry, 'KEY': key, 'docker': self.docker, 'lock': self.lock}.items():
            monkey = patch.object(backup, name, value)
            monkey.start()
            self.addCleanup(monkey.stop)

    @contextlib.contextmanager
    def lock(self, *arguments, **keywords):
        yield

    def docker(self, *arguments):
        self.calls.append(arguments)
        if arguments[:3] == ('ps', '--no-trunc', '-aq'):
            return '\n'.join(item['Id'] for item in self.containers).encode()
        if arguments[:3] == ('inspect', '--type', 'container'):
            return json.dumps([item for item in self.containers if item['Id'] in arguments[3:]]).encode()
        if arguments[:2] == ('inspect', '--format'):
            return '\n'.join(json.dumps({**item, 'Config': {'Labels': item['Config']['Labels']}})
                for item in self.containers if item['Id'] in arguments[5:]).encode()
        if arguments[:2] == ('volume', 'ls'):
            return self.volume_name.encode() if self.volume['Labels'].get('com.docker.compose.project') == 'whaledeck-app-demo' else b''
        if arguments[:2] == ('volume', 'inspect'):
            return json.dumps([self.volume]).encode()
        if arguments[0] in ('pause', 'unpause'):
            self.member['State']['Paused'] = arguments[0] == 'pause'
            return b''
        raise AssertionError('Unexpected fake Docker request')

    def test_plan_is_read_only_redacted_and_binds_configuration(self):
        before = set(self.target.iterdir())
        planned = backup.plan('demo', 'quiesced')
        self.assertNotIn(self.marker, json.dumps(planned))
        self.assertEqual(planned['namedVolumes'], [self.volume_name])
        self.assertEqual(before, set(self.target.iterdir()))
        self.assertFalse(any(item[0] in ('pause', 'unpause') for item in self.calls))
        (self.config / '.env').write_text('KEY=changed')
        self.assertNotEqual(planned['fingerprint'], backup.plan('demo', 'quiesced')['fingerprint'])

    def test_slug_bounds_match_registered_application_limits(self):
        self.assertIsNotNone(backup.SLUG.fullmatch('a' * 63))
        for value in ('a' * 64, '../demo', '/demo', 'Demo', 'demo_key', 'demo\n'):
            self.assertIsNone(backup.SLUG.fullmatch(value))

    def test_cli_quiesce_requires_exact_job_specific_systemd_cgroup(self):
        job = '4' * 32
        self.assertTrue(backup.approved_service(job,
            '0::/system.slice/whaledeck-application-backup-' + job + '.service'))
        for cgroup in ('0::/user.slice/session.scope',
            '0::/system.slice/whaledeck-application-backup-' + '5' * 32 + '.service',
            '0::/system.slice/prefix-whaledeck-application-backup-' + job + '.service',
            'malformed'):
            self.assertFalse(backup.approved_service(job, cgroup))

    def test_archive_includes_configuration_bind_and_named_volume_and_resumes(self):
        planned = backup.plan('demo', 'quiesced')
        result = backup.run('demo', 'c' * 32, 'quiesced', planned['fingerprint'])
        self.assertFalse(self.member['State']['Paused'])
        self.assertIn(('pause', self.identity), self.calls)
        self.assertIn(('unpause', self.identity), self.calls)
        self.assertNotIn(self.marker, json.dumps(result))
        self.assertTrue(result['verified'])
        self.assertFalse(result['transactionConsistent'])
        archive_path = self.target / result['relativePath']
        with tarfile.open(archive_path, 'r:gz') as archive:
            self.assertEqual(archive.extractfile('configuration/.env').read().decode(), 'KEY=' + self.marker)
            self.assertEqual(archive.extractfile('data/probe.txt').read(), b'bind-proof')
            self.assertEqual(archive.extractfile('volumes/' + self.volume_name + '/probe.txt').read(), b'volume-proof')
        self.assertEqual(result['checksum'], backup.verify('demo', 'c' * 32)['checksum'])
        if os.name == 'posix':
            self.assertEqual(archive_path.stat().st_mode & 0o777, 0o600)

    def test_stale_plan_does_not_pause_or_archive(self):
        planned = backup.plan('demo', 'quiesced')
        (self.config / '.env').write_text('KEY=changed')
        with self.assertRaises(backup.BackupError):
            backup.run('demo', 'd' * 32, 'quiesced', planned['fingerprint'])
        self.assertFalse(self.member['State']['Paused'])
        self.assertEqual(list(self.target.iterdir()), [])

    def test_shared_volume_is_denied_even_for_stopped_foreign_container(self):
        self.containers.append({'Id': 'e' * 64, 'Name': '/other', 'Config': {'Labels': {}},
            'State': {'Running': False}, 'Mounts': [{'Type': 'volume', 'Name': self.volume_name}]})
        with self.assertRaises(backup.BackupError):
            backup.plan('demo', 'quiesced')

    def test_bind_alias_or_ancestor_cannot_bypass_shared_volume_guard(self):
        foreign = {'Id': 'e' * 64, 'Name': '/other', 'Config': {'Labels': {}},
            'State': {'Running': False}, 'Mounts': []}
        self.containers.append(foreign)
        for source in (self.config / 'data/child', self.apps, self.volume_path):
            foreign['Mounts'] = [{'Type': 'bind', 'Source': str(source)}]
            with self.assertRaises(backup.BackupError):
                backup.plan('demo', 'quiesced')

    def test_member_restart_changes_plan_fingerprint(self):
        original = backup.plan('demo', 'live')['fingerprint']
        self.member['State']['StartedAt'] = '2026-10-13T01:00:00Z'
        self.assertNotEqual(original, backup.plan('demo', 'live')['fingerprint'])

    def test_stopped_unit_with_unresolved_pause_remains_running_until_recovery(self):
        job = '6' * 32
        self.member['State']['Paused'] = True
        backup.private_json(self.state / (job + '.json'), {'slug': 'demo', 'job': job, 'paused': [self.identity]})
        backup.private_json(self.state / (job + '.done.json'), {'slug': 'demo', 'job': job, 'state': 'Failed'})
        stopped = subprocess.CompletedProcess([], 0, stdout=b'ActiveState=inactive\nSubState=dead\nLoadState=loaded\n', stderr=b'')
        with patch.object(backup.subprocess, 'run', return_value=stopped):
            self.assertEqual(backup.status('demo', job)['phase'], 'WaitingForApplicationResume')
            partial = self.target / 'demo' / (job + '.partial')
            partial.parent.mkdir(parents=True)
            partial.write_bytes(b'partial-archive')
            backup.recover_job(job)
            self.assertFalse(partial.exists())
            self.assertEqual(backup.status('demo', job)['state'], 'Failed')
        self.assertFalse(self.member['State']['Paused'])

    def test_status_observation_error_is_not_a_terminal_job(self):
        failed = subprocess.CompletedProcess([], 1, stdout=b'', stderr=b'systemd unavailable')
        with patch.object(backup.subprocess, 'run', return_value=failed):
            with self.assertRaises(backup.BackupError):
                backup.status('demo', '7' * 32)

    def test_protected_container_driver_options_and_wrong_owner_are_denied(self):
        self.member['Config']['Labels']['io.whaledeck.protected'] = 'true'
        with self.assertRaises(backup.BackupError):
            backup.plan('demo', 'live')
        del self.member['Config']['Labels']['io.whaledeck.protected']
        self.volume['Options'] = {'device': '/etc'}
        with self.assertRaises(backup.BackupError):
            backup.plan('demo', 'live')
        self.volume['Options'] = {}
        self.volume['Labels']['com.docker.compose.project'] = 'whaledeck-app-demo-other'
        with self.assertRaises(backup.BackupError):
            backup.plan('demo', 'live')

    def test_failed_archive_resumes_services_and_removes_partial(self):
        planned = backup.plan('demo', 'quiesced')
        with patch.object(backup, 'verify_archive', side_effect=backup.BackupError('synthetic failure')):
            with self.assertRaises(backup.BackupError):
                backup.run('demo', 'f' * 32, 'quiesced', planned['fingerprint'])
        self.assertFalse(self.member['State']['Paused'])
        self.assertEqual(list((self.target / 'demo').iterdir()), [])
        self.assertEqual(backup.read_json(self.state / ('f' * 32 + '.json'))['paused'], [])

    def test_capacity_crossing_during_archive_resumes_and_removes_partial(self):
        planned = backup.plan('demo', 'quiesced')
        usage = namedtuple('usage', 'total used free')
        enough = usage(10 * 1024**3, 1 * 1024**3, 9 * 1024**3)
        exhausted = usage(10 * 1024**3, 9 * 1024**3, 1 * 1024**3)
        with patch.object(backup.shutil, 'disk_usage', side_effect=[enough, exhausted]):
            with self.assertRaises(backup.BackupError):
                backup.run('demo', '3' * 32, 'quiesced', planned['fingerprint'])
        self.assertFalse(self.member['State']['Paused'])
        self.assertEqual(list((self.target / 'demo').iterdir()), [])

    def test_recovery_unpauses_only_recorded_owned_container(self):
        self.member['State']['Paused'] = True
        backup.private_json(self.state / ('1' * 32 + '.json'), {'slug': 'demo', 'paused': [self.identity]})
        backup.recover_job('1' * 32)
        self.assertFalse(self.member['State']['Paused'])
        self.assertFalse((self.state / ('1' * 32 + '.json')).exists())

    def test_recovery_clears_deleted_container_only_after_successful_engine_listing(self):
        self.containers.clear()
        journal = {'slug': 'demo', 'paused': [self.identity]}
        self.assertTrue(backup.resume(journal))
        self.assertEqual(journal['paused'], [])
        self.assertNotIn(('unpause', self.identity), self.calls)

    @unittest.skipUnless(os.name == 'posix', 'POSIX flock semantics')
    def test_real_project_lock_serializes_and_rejects_path_traversal(self):
        self.member['State']['Paused'] = True
        journal = self.state / ('2' * 32 + '.json')
        backup.private_json(journal, {'slug': 'demo', 'paused': [self.identity]})
        with REAL_LOCK('demo'):
            with self.assertRaises(BlockingIOError):
                with REAL_LOCK('demo', blocking=False):
                    self.fail('A second project lock was unexpectedly granted')
            with patch.object(backup, 'lock', REAL_LOCK):
                backup.recover_job('2' * 32)
            self.assertTrue(self.member['State']['Paused'])
            self.assertTrue(journal.exists())
        with patch.object(backup, 'lock', REAL_LOCK):
            backup.recover_job('2' * 32)
        self.assertFalse(self.member['State']['Paused'])
        self.assertFalse(journal.exists())
        with REAL_LOCK('demo', blocking=False):
            pass
        with self.assertRaises(backup.BackupError):
            with REAL_LOCK('../../outside'):
                self.fail('Invalid lock path accepted')

    def test_unmounted_owned_volume_is_included(self):
        self.member['Mounts'] = [self.member['Mounts'][0]]
        self.assertEqual(backup.plan('demo', 'live')['namedVolumes'], [self.volume_name])

    def test_mounted_anonymous_image_volume_is_captured_but_not_inferred_from_orphan_name(self):
        anonymous = '8' * 64
        self.volume_path.parent.rename(self.volume_path.parent.with_name(anonymous))
        self.volume_path = self.volume_path.parent.with_name(anonymous) / '_data'
        self.volume_name = anonymous
        self.volume.update(Name=anonymous, Mountpoint=str(self.volume_path), Labels={})
        self.member['Mounts'][1]['Name'] = anonymous
        self.assertEqual(backup.plan('demo', 'live')['namedVolumes'], [anonymous])
        self.member['Mounts'] = [self.member['Mounts'][0]]
        self.assertEqual(backup.plan('demo', 'live')['namedVolumes'], [])

    def test_tampered_archive_fails_checksum_and_does_not_extract(self):
        planned = backup.plan('demo', 'live')
        result = backup.run('demo', '2' * 32, 'live', planned['fingerprint'])
        metadata = self.target / 'demo' / ('2' * 32 + '.json')
        value = json.loads(metadata.read_bytes())
        value['checksum'] = '0' * 64
        backup.private_json(metadata, value)
        with self.assertRaises(backup.BackupError):
            backup.verify('demo', '2' * 32)
        self.assertTrue((self.target / result['relativePath']).is_file())

    def test_archive_path_traversal_is_rejected_without_extracting(self):
        path = self.target / 'malicious.tar.gz'
        with tarfile.open(path, 'w:gz') as archive:
            member = tarfile.TarInfo('../outside')
            member.size = 4
            archive.addfile(member, io.BytesIO(b'data'))
        with self.assertRaises(backup.BackupError):
            backup.verify_archive(path)
        self.assertFalse((self.target.parent / 'outside').exists())

    def test_archive_budgets_reject_before_writing_unbounded_data(self):
        validator = backup.archive_filter()
        member = tarfile.TarInfo('data/oversized')
        member.size = backup.MAX_BYTES + 1
        with self.assertRaises(backup.BackupError):
            validator(member)
        member = tarfile.TarInfo('data/' + 'a' * 4096)
        with self.assertRaises(backup.BackupError):
            backup.archive_filter()(member)

    def test_recovery_does_not_unpause_protected_or_foreign_members(self):
        self.member['State']['Paused'] = True
        self.member['Config']['Labels']['io.whaledeck.protected'] = 'true'
        journal = {'slug': 'demo', 'paused': [self.identity]}
        self.assertFalse(backup.resume(journal))
        self.assertTrue(self.member['State']['Paused'])
        del self.member['Config']['Labels']['io.whaledeck.protected']
        self.member['Config']['Labels']['com.docker.compose.project'] = 'other'
        self.assertFalse(backup.resume(journal))
        self.assertTrue(self.member['State']['Paused'])

    @unittest.skipUnless(os.name == 'posix', 'POSIX symlink permission semantics')
    def test_symlinked_configuration_data_and_target_fail_closed(self):
        for path in (self.config / '.env', self.config / 'data', self.target):
            retained = path.with_name(path.name + '-real')
            path.rename(retained)
            path.symlink_to(retained, target_is_directory=retained.is_dir())
            try:
                with self.assertRaises(backup.BackupError):
                    backup.safe_path(path, directory=retained.is_dir())
            finally:
                path.unlink()
                retained.rename(path)


if __name__ == '__main__':
    unittest.main()
