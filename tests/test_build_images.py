"""Real Git/archive shell flow with a fake Docker boundary; never build/install here."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == 'posix', 'Linux shell execution required')
class FrozenImageBuildTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-build-inputs-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'deploy').mkdir()
        shutil.copyfile(ROOT / 'deploy/build-images.sh', self.root / 'deploy/build-images.sh')
        (self.root / 'compose.yml').write_text('name: whaledeck\nservices: {}\n')
        (self.root / '.gitignore').write_text('.env\n.images.env\ndeploy/artifacts/\n')
        self.git('init', '-q')
        self.git('add', '.')
        self.git('-c', 'user.name=Build Test', '-c', 'user.email=test@example.invalid', 'commit', '-qm', 'fixture')
        self.revision = self.git('rev-parse', 'HEAD').strip()
        (self.root / '.env').write_text('PRIVATE_FIXTURE=do-not-copy\n')
        (self.root / '.images.env').write_text(''.join(
            key + '=example.invalid/fixture@sha256:' + 'a' * 64 + '\n'
            for key in ['DOTNET_SDK_IMAGE', 'ASPNET_IMAGE', 'DOTNET_RUNTIME_IMAGE', 'NODE_IMAGE', 'NGINX_IMAGE']))
        self.bin = self.root / 'deploy/artifacts/fake-bin'
        self.bin.mkdir(parents=True)
        fake = self.bin / 'docker'
        fake.write_text('''#!/usr/bin/env bash
set -eu
[[ $1 == compose && $2 == --project-directory ]]
[[ -f $3/compose.yml && ! -e $3/.env && ! -e $3/.images.env && ! -e $3/.git ]]
[[ $WHALEDECK_BUILD_REVISION == "$EXPECTED_REVISION" ]]
printf 'FROZEN_CONTEXT_VALIDATED\\n'
[[ ${FAIL_BUILD:-0} == 0 ]]
''')
        fake.chmod(0o700)

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.root), *args], text=True)

    def run_build(self, **extra):
        env = {**os.environ, 'PATH': str(self.bin) + ':' + os.environ['PATH'],
               'EXPECTED_REVISION': self.revision, **extra}
        env.pop('WHALEDECK_BUILD_REVISION', None)
        env.update(extra)
        return subprocess.run(['bash', str(self.root / 'deploy/build-images.sh')], env=env,
                              capture_output=True, text=True, timeout=20)

    def assert_cleaned(self):
        self.assertEqual(list((self.root / 'deploy/artifacts').glob('image-source.*')), [])

    def test_freezes_commit_and_excludes_all_private_files(self):
        result = self.run_build()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('FROZEN_CONTEXT_VALIDATED', result.stdout)
        self.assertIn(self.revision, result.stdout)
        self.assert_cleaned()

    def test_dirty_tracked_source_is_rejected_before_docker(self):
        (self.root / 'compose.yml').write_text('changed\n')
        result = self.run_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('FROZEN_CONTEXT_VALIDATED', result.stdout)

    def test_untracked_source_is_rejected_before_docker(self):
        (self.root / 'uncommitted-source').write_text('changed\n')
        self.assertNotEqual(self.run_build().returncode, 0)

    def test_revision_override_is_not_trusted(self):
        self.assertNotEqual(self.run_build(WHALEDECK_BUILD_REVISION='a' * 40).returncode, 0)

    def test_floating_or_missing_base_image_is_rejected(self):
        (self.root / '.images.env').write_text('DOTNET_SDK_IMAGE=latest\n')
        self.assertNotEqual(self.run_build().returncode, 0)

    def test_build_failure_removes_only_own_frozen_context(self):
        result = self.run_build(FAIL_BUILD='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue((self.bin / 'docker').is_file())
        self.assertTrue((self.root / '.env').is_file())
        self.assert_cleaned()


if __name__ == '__main__':
    unittest.main()
