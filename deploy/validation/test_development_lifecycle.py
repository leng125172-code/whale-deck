import importlib.util
import json
import os
from pathlib import Path
import unittest
import uuid
from unittest.mock import patch

import test_development_storage as fixture


path = Path(__file__).resolve().parents[1] / 'host/whaledeck-development-lifecycle.py'
spec = importlib.util.spec_from_file_location('workspace_lifecycle', path)
lifecycle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lifecycle)


@unittest.skipUnless(os.geteuid() == 0, 'Requires private root-owned workspace fixtures')
class LifecycleTests(unittest.TestCase):
    def setUp(self):
        fixture.DevelopmentStorageTests.setUp(self)
        patch.object(lifecycle, 'GIT_CGROUP', self.mount / 'no-git-cgroup').start()
    write_config = fixture.DevelopmentStorageTests.write_config
    write_mountinfo = fixture.DevelopmentStorageTests.write_mountinfo
    enable_quota = fixture.DevelopmentStorageTests.enable_quota

    def provision(self):
        self.enable_quota()
        self.proof = self.engine.allocate(self.request)
        self.control = lifecycle.Lifecycle(self.engine)
        self.source = self.target / 'data' / 'source.txt'
        self.source.write_text('retained source')

    def plan(self, action='stop', epoch=1):
        return dict(storage=self.request, action=action, expectedEpoch=epoch,
                    operationId='', planSha256='', storageProofSha256='')

    def operation(self, action='stop', epoch=1):
        request = self.plan(action, epoch)
        plan = self.control.run(request)
        return dict(request, operationId=str(uuid.uuid4()), planSha256=plan['planSha256'],
                    storageProofSha256=plan['storageProofSha256'])

    def test_stop_resume_preserves_inode_code_and_quota_and_rejects_old_replay(self):
        self.provision()
        original = (self.source.stat().st_ino, self.target.stat().st_ino, dict(self.quota.projects))
        stop = self.operation()
        self.assertEqual('Stopped', self.control.run(stop, apply=True)['state'])
        self.assertEqual(2, self.control.run(stop, apply=True)['epoch'])
        with self.engine.attested_payload(self.request) as (_, proof):
            with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_STOPPED'):
                self.engine.require_active(self.request, proof)
        with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_STOPPED'):
            self.engine.allocate(self.request)
        changed = dict(stop, operationId=str(uuid.uuid4()))
        with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_PLAN_CHANGED'):
            self.control.run(changed, apply=True)
        resume = self.operation('resume', 2)
        self.assertEqual('Active', self.control.run(resume, apply=True)['state'])
        self.engine.require_active(self.request, self.proof)
        self.assertEqual(self.proof, self.engine.allocate(self.request))
        with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_PLAN_CHANGED'):
            self.control.run(stop, apply=True)
        self.assertEqual(original, (self.source.stat().st_ino, self.target.stat().st_ino, self.quota.projects))
        self.assertEqual('retained source', self.source.read_text())

    def test_busy_attested_operation_prevents_fence_publication(self):
        self.provision()
        stop = self.operation()
        with self.engine.attested_payload(self.request):
            with self.assertRaisesRegex(fixture.files.Rejected, 'STORAGE_BUSY'):
                self.control.run(stop, apply=True)
        self.assertFalse((self.root / (fixture.storage.LIFECYCLE + self.request['workspaceId'] + '.json')).exists())
        self.assertEqual('Stopped', self.control.run(stop, apply=True)['state'])

    def test_changed_plan_or_physical_binding_is_refused(self):
        self.provision()
        stop = self.operation()
        with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_PLAN_CHANGED'):
            self.control.run(dict(stop, planSha256='f' * 64), apply=True)
        self.control.run(stop, apply=True)
        marker = self.root / (fixture.storage.LIFECYCLE + self.request['workspaceId'] + '.json')
        data = json.loads(marker.read_text());data['storageProofSha256'] = 'f' * 64
        marker.write_text(json.dumps(data))
        with self.assertRaisesRegex(fixture.files.Rejected, 'LIFECYCLE_BINDING_CHANGED'):
            self.control.run(self.plan('resume', 2))

    def test_symlink_marker_is_refused_and_external_file_kept(self):
        self.provision()
        marker = self.root / (fixture.storage.LIFECYCLE + self.request['workspaceId'] + '.json')
        external = self.mount / 'external.json';external.write_text('original')
        marker.symlink_to(external)
        with self.assertRaises((fixture.files.Rejected, OSError)):
            self.control.run(self.plan())
        self.assertEqual('original', external.read_text())

    def test_invalid_requests_never_create_fence(self):
        self.provision()
        for change in ({'action': 'delete'}, {'expectedEpoch': True}, {'expectedEpoch': 0},
                       {'operationId': '../foreign'}, {'unknown': True}, {'storageProofSha256': 'f' * 64}):
            with self.subTest(change=change), self.assertRaises(fixture.files.Rejected):
                self.control.run(self.plan() | change)

    def test_live_git_cgroup_prevents_stop_until_real_population_is_empty(self):
        self.provision()
        stop = self.operation()
        group = self.mount / 'git-cgroup';group.mkdir()
        events = group / 'cgroup.events';events.write_text('populated 1\nfrozen 0\n')
        with patch.object(lifecycle, 'GIT_CGROUP', group):
            with self.assertRaisesRegex(fixture.files.Rejected, 'STORAGE_BUSY'):
                self.control.run(stop, apply=True)
            events.write_text('populated 0\nfrozen 0\n')
            self.assertEqual('Stopped', self.control.run(stop, apply=True)['state'])


if __name__ == '__main__':
    unittest.main()
