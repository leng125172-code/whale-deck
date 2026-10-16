"""Real Linux filesystem tree/search checks, never a mocked permission pass."""
import json
import os
import stat
import subprocess
import unittest
from unittest.mock import patch
import test_file_transport as transport

driver = transport.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root filesystem validation')
class FileBrowserTests(unittest.TestCase):
    def setUp(self):
        self.fixture = transport.FileTransportTests(); self.fixture.setUp()
        self.root, self.base, self.state = self.fixture.root, self.fixture.base, self.fixture.state

    def tearDown(self): self.fixture.tearDown()

    def search(self, query='report', **values):
        return driver.search_files({'rootId': 'test.shared', 'path': '', 'query': query, 'kind': 'All',
                                    'matchCase': False, 'cursor': '', 'take': 50, **values})

    def all_hits(self, **values):
        hits = []; cursor = ''
        for _ in range(100):
            result = self.search(cursor=cursor, **values)
            self.assertLessEqual(len(json.dumps(result, ensure_ascii=False).encode()), 65536)
            hits.extend(hit['path'] for hit in result['entries']); cursor = result['nextCursor']
            if not cursor: return hits, result
        self.fail('search did not terminate')

    def test_tree_pages_ignore_files_protected_directories_and_links(self):
        for name in ('a', 'b', '中文'): (self.root / name).mkdir()
        (self.root / 'report.txt').write_bytes(b'x'); (self.root / '.git').mkdir()
        (self.root / 'link').symlink_to(self.root / 'a', target_is_directory=True)
        first = driver.list_directories({'rootId': 'test.shared', 'path': '', 'after': '', 'take': 2})
        second = driver.list_directories({'rootId': 'test.shared', 'path': '', 'after': first['nextCursor'], 'take': 2})
        self.assertEqual([x['name'] for x in first['entries'] + second['entries']], ['a', 'b', '中文'])
        self.assertEqual(first['version'], second['version'])
        self.assertTrue(all(x['kind'] == 'Directory' and not x['isProtected'] for x in first['entries']))

    def test_recursive_pages_resume_inside_matched_directories_without_duplicates(self):
        for directory in ('a-report', 'b', '中文'):
            (self.root / directory).mkdir(); (self.root / directory / 'report.txt').write_bytes(b'binary\x00\xff')
        (self.root / 'REPORT.md').write_bytes(b'x')
        paths, result = self.all_hits(take=1)
        self.assertEqual(paths, ['REPORT.md', 'a-report', 'a-report/report.txt', 'b/report.txt', '中文/report.txt'])
        self.assertTrue(result['complete']); self.assertFalse(result['depthLimited'])

    def test_literal_case_kind_and_unicode_filters(self):
        (self.root / 'Report').mkdir(); (self.root / 'Report' / 'report').write_bytes(b'x')
        (self.root / 'a[1]*.txt').write_bytes(b'x')
        self.assertEqual(self.all_hits(query='Report', matchCase=True)[0], ['Report'])
        self.assertEqual(self.all_hits(kind='Directory')[0], ['Report'])
        self.assertEqual(self.all_hits(kind='File')[0], ['Report/report'])
        self.assertEqual(self.all_hits(query='[1]*')[0], ['a[1]*.txt'])

    def test_protected_subtrees_links_hardlinks_and_fifo_never_appear(self):
        for name in ('.git', '.env.d', 'secrets', '.whaledeck-private'):
            (self.root / name).mkdir(); (self.root / name / 'report-secret').write_bytes(b'secret')
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'report-outside').write_bytes(b'secret')
        (self.root / 'report-link').symlink_to(outside, target_is_directory=True)
        os.link(outside / 'report-outside', self.root / 'report-hard'); os.mkfifo(self.root / 'report-fifo')
        (self.root / 'report-ok').write_bytes(b'public')
        paths, result = self.all_hits(); self.assertEqual(paths, ['report-ok']); self.assertTrue(result['complete'])
        self.assertGreaterEqual(result['excludedEntries'], 3)

    def test_same_device_bind_mount_is_not_searched_or_listed(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'report-secret').write_bytes(b'secret')
        target = self.root / 'report-mount'; target.mkdir()
        subprocess.run(['mount', '--bind', str(outside), str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            self.assertEqual(self.all_hits()[0], [])
            self.assertEqual(driver.list_directories({'rootId': 'test.shared', 'path': '', 'after': '', 'take': 100})['entries'], [])
        finally: subprocess.run(['umount', str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def test_file_bind_mount_is_not_a_searchable_regular_file(self):
        outside = self.base / 'report-source'; outside.write_bytes(b'secret')
        target = self.root / 'report-mount'; target.write_bytes(b'ordinary')
        subprocess.run(['mount', '--bind', str(outside), str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try: self.assertEqual(self.all_hits()[0], [])
        finally: subprocess.run(['umount', str(target)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def test_cursor_binds_mac_scope_expiry_and_all_ancestors(self):
        (self.root / 'a').mkdir(); (self.root / 'a' / 'report').write_bytes(b'x'); (self.root / 'z-report').write_bytes(b'x')
        cursor = self.search(take=1)['nextCursor']; self.assertTrue(cursor)
        for values in ({'query': 'other'}, {'kind': 'File'}, {'matchCase': True}, {'path': 'a'}, {'rootId': 'other'}):
            with self.subTest(values=values), self.assertRaises(driver.Rejected): self.search(cursor=cursor, **values)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_CURSOR_INVALID'): self.search(cursor=cursor[:-1] + ('a' if cursor[-1] != 'a' else 'b'))
        with patch.object(driver.time, 'time', return_value=driver.time.time() + 601), self.assertRaisesRegex(driver.Rejected, 'FILES_CURSOR_INVALID'): self.search(cursor=cursor)
        (self.root / 'unrelated-new-name').write_bytes(b'x')
        with self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.search(cursor=cursor)

    def test_scan_budget_resumes_instead_of_claiming_empty_complete(self):
        for index in range(130): (self.root / f'file-{index:03}').write_bytes(b'x')
        (self.root / 'z-report').write_bytes(b'x')
        with patch.object(driver, 'SEARCH_VISITS', 50):
            first = self.search(); self.assertEqual(first['scannedEntries'], 50)
            self.assertFalse(first['complete']); self.assertEqual(first['stopReason'], 'ScanBudget'); self.assertTrue(first['nextCursor'])
            self.assertEqual(self.all_hits()[0], ['z-report'])

    def test_oversized_folder_is_explicitly_incomplete(self):
        with patch.object(driver, 'browse_names', side_effect=driver.Rejected('FILES_DIRECTORY_SCAN_LIMIT')): result = self.search()
        self.assertFalse(result['complete']); self.assertEqual(result['stopReason'], 'DirectoryScanLimit'); self.assertEqual(result['nextCursor'], '')

    def test_depth_limit_is_preserved_across_pages(self):
        directory = self.root
        for _ in range(34): directory = directory / 'report'; directory.mkdir()
        (directory / 'report-file').write_bytes(b'x')
        paths, result = self.all_hits(take=2)
        self.assertEqual(len(paths), 33); self.assertFalse(result['complete']); self.assertTrue(result['depthLimited'])
        self.assertEqual(result['stopReason'], 'DepthLimit')

    def test_long_unicode_paths_have_bounded_pages_without_lost_hits(self):
        directory = self.root
        for _ in range(16): directory = directory / ('中' * 80); directory.mkdir()
        for index in range(20): (directory / f'report-{index}').write_bytes(b'x')
        paths, result = self.all_hits(); self.assertEqual(len(paths), 20); self.assertEqual(len(set(paths)), 20); self.assertTrue(result['complete'])

    def test_completed_empty_search_creates_no_key_and_resume_key_is_private(self):
        self.assertTrue(self.search()['complete']); self.assertFalse((self.state / 'browse-cursor.key').exists())
        for name in ('report-a', 'report-b'): (self.root / name).write_bytes(b'x')
        cursor = self.search(take=1)['nextCursor']; key = self.state / 'browse-cursor.key'
        self.assertEqual(stat.S_IMODE(key.stat().st_mode), 0o600); self.assertEqual(key.stat().st_size, 32); key.chmod(0o644)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_CURSOR_KEY_UNSAFE'): self.search(cursor=cursor)

    def test_bad_query_cursor_path_and_typed_limits(self):
        for values in ({'query': ''}, {'query': ' '}, {'query': 'a\n'}, {'query': 'a\u0085'}, {'query': '../'}, {'query': '中' * 86}, {'query': 'a\\b'},
                       {'take': True}, {'take': 101}, {'kind': 'Symlink'}, {'matchCase': 1}, {'cursor': 'x' * 32769},
                       {'cursor': 'not-signed'}, {'path': '../outside'}, {'path': 'secrets'}):
            with self.subTest(values=values), self.assertRaises(driver.Rejected): self.search(**values)

    def test_stat_only_registry_cannot_gain_tree_or_search_permission(self):
        values = json.loads(self.fixture.registry.read_text()); values[0]['allowedActions'] = ['files-stat']; self.fixture.registry.write_text(json.dumps(values))
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.search()
        with self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): driver.list_directories({'rootId': 'test.shared', 'path': '', 'after': '', 'take': 100})

    def test_mutation_during_enumeration_returns_conflict(self):
        (self.root / 'report').write_bytes(b'x'); original = driver.browse_names
        def mutate(*args, **kwargs):
            result = original(*args, **kwargs); (self.root / 'new-file').write_bytes(b'x'); return result
        with patch.object(driver, 'browse_names', side_effect=mutate), self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.search()


if __name__ == '__main__': unittest.main()
