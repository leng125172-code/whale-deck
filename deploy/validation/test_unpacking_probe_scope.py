"""Real disposable Git: QA-only client changes never mask product/renamed paths."""
from pathlib import Path
import os
import subprocess
import tempfile
import time
import unittest
from validate_tree_unpacking_agent import validation_only_changes


class UnpackingProbeScopeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-unpack-scope-')
        self.root = Path(self.temporary.name)
        self.git = ['git', '-C', str(self.root), '-c', 'user.name=WhaleDeck Test', '-c', 'user.email=validation@example.invalid',
            '-c', 'core.autocrlf=false', '-c', 'core.fsmonitor=false', '-c', 'maintenance.auto=false', '-c', 'gc.auto=0']
        self.run_git('init', '--quiet')
        (self.root / 'src').mkdir(); (self.root / 'src/product.txt').write_text('product\n')
        self.initial = self.commit()

    def tearDown(self):
        if os.name == 'nt':
            # Only this test repo's possible inherited Git-for-Windows daemon.
            subprocess.run([*self.git, 'fsmonitor--daemon', 'stop'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10, check=False)
        # Antivirus/indexers can briefly retain a Windows directory handle.
        # Keep all assertions and cleanup mandatory; never ignore a failure.
        for attempt in range(50):
            try:
                self.temporary.cleanup(); return
            except PermissionError:
                if attempt == 49: raise
                time.sleep(0.1)
    def run_git(self, *arguments):
        return subprocess.check_output([*self.git, *arguments], text=True, stderr=subprocess.PIPE).strip()
    def commit(self):
        self.run_git('add', '.'); self.run_git('commit', '--quiet', '-m', 'isolated test')
        return self.run_git('rev-parse', 'HEAD')

    def test_same_revision_and_only_explicit_qa_documentation_metadata_paths_are_allowed(self):
        self.assertTrue(validation_only_changes(self.git, self.initial, self.initial))
        for name in ('deploy/validation/client.txt', 'docs/evidence.txt', '.project-atlas.json', '.github/workflows/ci.yml'):
            path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('test-only\n')
        latest = self.commit(); self.assertTrue(validation_only_changes(self.git, self.initial, latest))

    def test_product_rename_into_documentation_does_not_hide_deletion(self):
        (self.root / 'docs').mkdir()
        self.run_git('mv', 'src/product.txt', 'docs/product.txt'); latest = self.commit()
        self.assertFalse(validation_only_changes(self.git, self.initial, latest))

    def test_actual_product_or_root_build_changes_are_not_qa_only(self):
        (self.root / 'src/product.txt').write_text('changed product\n'); latest = self.commit()
        self.assertFalse(validation_only_changes(self.git, self.initial, latest))

    def test_root_build_change_alone_is_not_qa_only(self):
        (self.root / 'Directory.Build.props').write_text('changed build inputs\n'); latest = self.commit()
        self.assertFalse(validation_only_changes(self.git, self.initial, latest))

    def test_other_workflows_installers_and_dependencies_are_not_qa_only(self):
        for name in ('.github/workflows/publish.yml', 'deploy/build-images.sh', 'web/package.json'):
            with self.subTest(path=name):
                path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('changed\n')
                latest = self.commit(); self.assertFalse(validation_only_changes(self.git, self.initial, latest))
                path.unlink(); self.commit()

    def test_ci_change_does_not_hide_a_product_change(self):
        path = self.root / '.github/workflows/ci.yml'; path.parent.mkdir(parents=True); path.write_text('qa-only\n')
        latest = self.commit(); self.assertTrue(validation_only_changes(self.git, self.initial, latest))
        (self.root / 'src/product.txt').write_text('changed runtime\n')
        latest = self.commit(); self.assertFalse(validation_only_changes(self.git, self.initial, latest))


if __name__ == '__main__': unittest.main()
