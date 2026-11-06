"""Installed browser gate safety contract; no installed gate is executed here."""
import ast
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parent


class ArchiveBrowserScopeTests(unittest.TestCase):
    def test_frozen_sources_are_verified_before_fixture_and_login(self):
        text=(ROOT/'validate_container_recycle_archive_browser.py').read_text()
        self.assertLess(text.index('closure=source_closure'),text.index('with owned.fixture()'))
        self.assertLess(text.index('with owned.fixture()'),text.index('with temporary_user(True)'))
        self.assertIn("f['retain_unsettled']=True",text)
        self.assertIn("'credentials':dict(username=username,password=password)",text)
        self.assertNotIn("'VALIDATION_PASSWORD':password",text)

    def test_browser_has_visible_actions_without_mocked_responses_or_raw_apply(self):
        text=(ROOT/'validate_container_recycle_archive_browser.mjs').read_text()
        for prohibited in ('page.route(', 'context.route(', 'route.fulfill(', 'storageState(', 'tracing.start(', 'ReserveContainerRecycleArchive', 'ApplyContainerRecycleArchive'):
            self.assertNotIn(prohibited,text)
        for expected in ('loginVisible(', '确认复制为新文件', 'archive-final-confirm', 'actual-archive-job-', 'archiveKeys', 'normal-dual-logout'):
            self.assertIn(expected,text)
        self.assertIn('delete fixture.credentials',text)

    def test_independent_verifier_has_only_readonly_acquisition(self):
        text=(ROOT/'verify_container_recycle_archive_browser.py').read_text()
        tree=ast.parse(text)
        calls=[ast.unparse(n.func) for n in ast.walk(tree) if isinstance(n,ast.Call)]
        for prohibited in ('cleanup_new_copy','requests.post','os.unlink','os.rmdir','subprocess.Popen'):
            self.assertNotIn(prohibited,calls)
        for expected in ('archive_io.read_operation','archive_io.physical','archive_io.physical_proof','hdd_observation','job_summary','source_closure'):
            self.assertIn(expected,calls)

    def test_cleanup_requires_independent_live_proof_and_post_cleanup_hdd_reread(self):
        text=(ROOT/'validate_container_recycle_archive_browser.py').read_text()
        self.assertLess(text.index('independent(repo,e,True)'),text.index('cleanup_new_copy(f['))
        self.assertLess(text.index('cleanup_new_copy(f['),text.index("f['retain_unsettled']=False"))
        self.assertLess(text.index("f['retain_unsettled']=False"),text.index('after_hdd=hdd_observation'))
        self.assertIn('independent(repo,e,False)',text)
        self.assertIn("uuid.UUID(archive_execution(v['id'])).hex for v in browser['archives']",text)


if __name__=='__main__':unittest.main()
