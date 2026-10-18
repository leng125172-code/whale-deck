"""Actual Linux files for UTF-8 literal search. No host service/production root mutation."""
import io
import json
import os
import stat
import subprocess
import unittest
from unittest.mock import patch
import test_file_transport as transport

driver = transport.driver


@unittest.skipUnless(os.name == 'posix' and os.geteuid() == 0, 'requires isolated Linux root filesystem validation')
class FileContentSearchTests(unittest.TestCase):
    def setUp(self):
        self.fixture = transport.FileTransportTests(); self.fixture.setUp()
        self.root, self.base, self.state = self.fixture.root, self.fixture.base, self.fixture.state

    def tearDown(self): self.fixture.tearDown()

    def search(self, query='needle', **values):
        return driver.search_content({'rootId': 'test.shared', 'path': '', 'query': query,
            'matchCase': False, 'cursor': '', 'take': 50, 'maxFileBytes': 16 * 1024 * 1024, **values})

    def collect(self, **values):
        hits = []; cursor = ''; pages = []
        for _ in range(100):
            result = self.search(cursor=cursor, **values); pages.append(result)
            output = io.BytesIO(); driver.emit(result, output)
            self.assertLessEqual(len(output.getvalue()), 65536)
            hits.extend(result['entries']); cursor = result['nextCursor']
            if not cursor: return hits, result, pages
        self.fail('content search did not terminate')

    def test_utf8_bom_crlf_cr_lines_and_one_first_match_per_file(self):
        (self.root / '中文').mkdir()
        (self.root / '中文' / 'text.cs').write_bytes(b'\xef\xbb\xbf' + 'first\r\n第二行 NEEDLE\rthird needle'.encode())
        rows, result, _ = self.collect(take=1)
        self.assertEqual(len(rows), 1); self.assertEqual(rows[0]['lineNumber'], 2)
        self.assertEqual(rows[0]['excerpt'], '第二行 NEEDLE'); self.assertFalse(rows[0]['excerptTruncated'])
        self.assertEqual(rows[0]['entry']['kind'], 'File'); self.assertTrue(result['complete'])

    def test_literal_slashes_regex_metacharacters_case_and_expanding_casefold(self):
        (self.root / 'text.txt').write_text('Straße\nApi/V1\\[test]*\n', encoding='utf-8')
        self.assertEqual(self.search(query='STRASSE')['entries'][0]['lineNumber'], 1)
        self.assertEqual(self.search(query='SSE')['entries'][0]['excerpt'], 'Straße')
        self.assertEqual(self.search(query='Api/V1\\[test]*', matchCase=True)['entries'][0]['lineNumber'], 2)
        self.assertEqual(self.search(query='strasse', matchCase=True)['entries'], [])

    def test_no_prefix_scan_of_oversized_or_binary_invalid_utf8_files(self):
        (self.root / 'a-large').write_bytes(b'needle' + b'x' * 100)
        (self.root / 'b-binary').write_bytes(b'needle\x00')
        (self.root / 'c-invalid').write_bytes(b'needle\xff')
        (self.root / 'd-text').write_bytes(b'needle')
        result = self.search(maxFileBytes=64)
        self.assertEqual([row['path'] for row in result['entries']], ['d-text'])
        self.assertEqual(result['skippedOversizedFiles'], 1); self.assertEqual(result['skippedNonTextFiles'], 2)
        self.assertEqual(result['scannedFiles'], 3); self.assertEqual(result['readBytes'], 20)

    def test_pages_resume_without_duplicate_or_lost_file_matches(self):
        for folder in ('a', 'b', '中'):
            (self.root / folder).mkdir()
            for index in range(3): (self.root / folder / f'{index}.txt').write_bytes(b'needle')
        rows, result, _ = self.collect(take=1)
        self.assertEqual(len(rows), 9); self.assertEqual(len({row['path'] for row in rows}), 9)
        self.assertTrue(result['complete'])

    def test_private_subtrees_links_hardlinks_fifo_are_not_read(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'text').write_bytes(b'needle')
        for name in ('.git', 'secrets', '.env.d'):
            (self.root / name).mkdir(); (self.root / name / 'text').write_bytes(b'needle')
        (self.root / '.env').write_bytes(b'needle')
        (self.root / 'link').symlink_to(outside, target_is_directory=True)
        os.link(outside / 'text', self.root / 'hard'); os.mkfifo(self.root / 'fifo')
        (self.root / 'normal.txt').write_bytes(b'needle')
        rows, result, _ = self.collect()
        self.assertEqual([row['path'] for row in rows], ['normal.txt'])
        self.assertEqual(result['readBytes'], 6); self.assertEqual(result['scannedFiles'], 1)

    def test_same_device_directory_and_file_bind_mounts_are_not_read(self):
        outside = self.base / 'outside'; outside.mkdir(); (outside / 'text').write_bytes(b'needle')
        directory = self.root / 'mount'; directory.mkdir(); leaf = self.root / 'file'; leaf.write_bytes(b'original')
        subprocess.run(['mount', '--bind', str(outside), str(directory)], check=True, stdout=subprocess.DEVNULL)
        subprocess.run(['mount', '--bind', str(outside / 'text'), str(leaf)], check=True, stdout=subprocess.DEVNULL)
        try:
            result = self.search(); self.assertEqual(result['entries'], []); self.assertEqual(result['readBytes'], 0)
        finally:
            subprocess.run(['umount', str(leaf)], check=True, stdout=subprocess.DEVNULL)
            subprocess.run(['umount', str(directory)], check=True, stdout=subprocess.DEVNULL)

    def test_list_only_and_download_only_registry_cannot_read_content(self):
        initial = json.loads(self.fixture.registry.read_text())
        for actions in (['files-list'], ['files-download'], ['files-stat']):
            initial[0]['allowedActions'] = actions; self.fixture.registry.write_text(json.dumps(initial))
            with self.subTest(actions=actions), self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_FORBIDDEN'): self.search()

    def test_cursor_binds_content_domain_query_case_size_root_and_expiry(self):
        for name in ('a', 'b'): (self.root / name).write_bytes(b'needle')
        cursor = self.search(take=1)['nextCursor']; self.assertTrue(cursor)
        with self.assertRaisesRegex(driver.Rejected, 'FILES_CURSOR_INVALID'):
            driver.search_files({'rootId': 'test.shared', 'path': '', 'query': 'needle', 'kind': 'File', 'matchCase': False, 'cursor': cursor, 'take': 1})
        for change in ({'query': 'other'}, {'matchCase': True}, {'maxFileBytes': 64}, {'path': 'a'}, {'rootId': 'other'}):
            with self.subTest(change=change), self.assertRaises(driver.Rejected): self.search(cursor=cursor, **change)
        with patch.object(driver.time, 'time', return_value=driver.time.time() + 601), self.assertRaisesRegex(driver.Rejected, 'FILES_CURSOR_INVALID'): self.search(cursor=cursor)

    def test_root_download_permission_revoked_during_scan_suppresses_snippets(self):
        (self.root / 'text.txt').write_bytes(b'needle')
        original = driver.content_match
        def revoke(*arguments):
            result = original(*arguments)
            registry = json.loads(self.fixture.registry.read_text())
            registry[0]['allowedActions'] = ['files-list']
            self.fixture.registry.write_text(json.dumps(registry))
            return result
        with patch.object(driver, 'content_match', side_effect=revoke), self.assertRaisesRegex(driver.Rejected, 'FILES_ROOT_POLICY_CHANGED'): self.search()

    def test_byte_budget_stops_before_next_file_and_continues_it(self):
        for name in ('a', 'b', 'c'): (self.root / name).write_bytes(b'needle')
        with patch.object(driver, 'CONTENT_PAGE_BYTES', 13):
            first = self.search(); self.assertEqual(first['stopReason'], 'ContentByteBudget')
            self.assertFalse(first['complete']); self.assertEqual(len(first['entries']), 2)
            rows, final, pages = self.collect()
            self.assertEqual([row['path'] for row in rows], ['a', 'b', 'c']); self.assertTrue(final['complete'])
            self.assertTrue(all(page['readBytes'] <= 13 for page in pages))

    def test_unread_file_deadline_does_not_claim_empty_complete_or_emit_resume_loop(self):
        (self.root / 'a').write_bytes(b'needle')
        with patch.object(driver, 'content_match', return_value='FileReadTimeLimit'):
            result = self.search()
        self.assertFalse(result['complete']); self.assertEqual(result['nextCursor'], '')
        self.assertEqual(result['stopReason'], 'FileReadTimeLimit')

    def test_same_file_modified_during_content_read_returns_conflict(self):
        file = self.root / 'a'; file.write_bytes(b'needle')
        original = driver.os.read
        def changed(descriptor, size):
            data = original(descriptor, size)
            if stat.S_ISREG(os.fstat(descriptor).st_mode) and os.fstat(descriptor).st_ino == file.stat().st_ino:
                file.write_bytes(b'changed')
            return data
        with patch.object(driver.os, 'read', side_effect=changed), self.assertRaisesRegex(driver.Rejected, 'FILES_VERSION_CONFLICT'): self.search()

    def test_long_lines_have_bounded_escaped_excerpts_and_complete_version_checks(self):
        (self.root / 'quote.txt').write_text('"' * 5000 + '<script>needle</script>' + '中' * 5000, encoding='utf-8')
        before = (self.root / 'quote.txt').stat()
        row = self.search()['entries'][0]
        self.assertTrue(row['excerptTruncated']); self.assertIn('<script>needle</script>', row['excerpt'])
        self.assertLessEqual(len(row['excerpt']), 320)
        self.assertEqual(row['entry']['version'], driver.version(before))

    def test_query_and_typed_bounds_are_rejected(self):
        for change in ({'query': ''}, {'query': ' '}, {'query': '\x00'}, {'query': '\ud800'}, {'query': '中' * 86},
            {'maxFileBytes': True}, {'maxFileBytes': 0}, {'maxFileBytes': 16777217}, {'take': True}, {'take': 101}, {'cursor': '../bad'}, {'path': '../outside'}):
            with self.subTest(change=change), self.assertRaises(driver.Rejected): self.search(**change)

    def test_full_sixteen_mib_file_matches_at_the_end_not_only_a_prefix(self):
        data = b'x' * (16 * 1024 * 1024 - 6) + b'needle'
        file = self.root / 'large.txt'; file.write_bytes(data); before = file.stat()
        result = self.search(matchCase=True)
        self.assertEqual(result['readBytes'], len(data)); self.assertEqual(len(result['entries']), 1)
        self.assertTrue(result['complete']); self.assertIn('needle', result['entries'][0]['excerpt'])
        self.assertEqual(result['entries'][0]['entry']['version'], driver.version(before))
        self.assertEqual(driver.version(file.stat()), driver.version(before))

    def test_expanding_unicode_fold_in_a_large_line_keeps_the_original_snippet(self):
        file = self.root / 'large-unicode.txt'; file.write_text('ß' * 1000000 + 'needle END', encoding='utf-8')
        row = self.search()['entries'][0]
        self.assertIn('ß', row['excerpt']); self.assertIn('needle END', row['excerpt'])
        self.assertLessEqual(len(row['excerpt']), 320)

    def test_content_scan_and_cancelled_deadline_do_not_leak_descriptors(self):
        (self.root / 'text').write_bytes(b'needle')
        before = len(os.listdir('/proc/self/fd'))
        for _ in range(10): self.search()
        original = driver.content_match
        def expired(descriptor, before, query, match_case, stats, deadline):
            with patch.object(driver.time, 'monotonic', return_value=deadline + 1):
                return original(descriptor, before, query, match_case, stats, deadline)
        with patch.object(driver, 'content_match', side_effect=expired): result = self.search()
        self.assertEqual(result['stopReason'], 'FileReadTimeLimit'); self.assertFalse(result['complete'])
        self.assertEqual(len(os.listdir('/proc/self/fd')), before)


if __name__ == '__main__': unittest.main()
