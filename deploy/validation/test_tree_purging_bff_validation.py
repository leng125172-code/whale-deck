#!/usr/bin/env python3
"""Pure acceptance-client validation tests; no workstation or HTTP calls."""
import copy
from pathlib import Path
import unittest
from unittest.mock import patch

import validate_file_tree_purging_bff as validator


class PurgingBffValidationTests(unittest.TestCase):
    def setUp(self):
        network = patch.object(validator.requests.Session, 'request', side_effect=AssertionError('No network in pure tests'))
        network.start(); self.addCleanup(network.stop)
        self.row = {
            'id': 'acfda44d-113d-4dd9-9677-aa8ac0b097cb',
            'planningJobId': 'acfda44d-113d-4dd9-9677-aa8ac0b097cb', 'executionJobId': None,
            'state': 'Ready', 'command': {'captureId': '3699984d-73e4-4cf2-a90c-c291cb5621af',
                'captureKind': 'Trash', 'source': {'rootId': validator.ROOT, 'path': 'owned/中文'}, 'scope': 'All'},
            'entryCount': 3, 'totalBytes': 2 * 1024**2 + 7, 'archiveBytes': 2 * 1024**2 + 1024,
            'readyExpiresAtUtc': '2099-01-01T00:05:00Z', 'resultCode': None, 'version': 1,
            'deletionStarted': False, 'archiveRemoved': False, 'originalsRemoved': False,
            'confirmation': {'planHash': 'opaque-test-plan', 'expiresAtUtc': '2099-01-01T00:05:00Z', 'warning': '不可撤销'},
        }

    def reject(self, row):
        with self.assertRaises((AssertionError, ValueError, TypeError)):
            validator.public_response(row)

    def completed(self, scope, kind):
        row = copy.deepcopy(self.row)
        row.update(state='Succeeded', version=3, confirmation=None,
            executionJobId='6237205e-fc42-4cf4-9b58-1b2d67360eef', deletionStarted=True,
            archiveRemoved=scope != 'Originals', originalsRemoved=scope != 'Archive')
        row['command'].update(scope=scope, captureKind=kind)
        return row

    def test_both_capture_kinds_all_scopes_have_exact_selected_facts(self):
        for kind in ('Trash', 'Overwrite'):
            for scope in validator.SCOPES:
                with self.subTest(kind=kind, scope=scope):
                    row = self.completed(scope, kind)
                    self.assertIs(row, validator.public_response(row))
                    for key in ('deletionStarted', 'archiveRemoved', 'originalsRemoved'):
                        wrong = {**row, key: not row[key]}
                        self.reject(wrong)

    def test_opaque_canonical_nonzero_identifiers_only(self):
        for value in ('00000000-0000-0000-0000-000000000000', self.row['id'].upper(), '../other', None):
            with self.subTest(value=value), self.assertRaises(AssertionError): validator.identifier(value)
        self.assertEqual(self.row['id'], validator.identifier(self.row['id']))

    def test_no_unexpected_private_or_missing_public_fields(self):
        for key in validator.PUBLIC_FIELDS:
            row = copy.deepcopy(self.row); row.pop(key)
            self.reject(row)
        for key in ('owner', 'loginBinding', 'receiptJson', 'planJson', 'hostPath'):
            self.reject({**self.row, key: 'private'})
        row = copy.deepcopy(self.row); row['command']['owner'] = 'private'; self.reject(row)

    def test_ready_confirmation_is_complete_bounded_and_unchanged(self):
        self.assertIs(self.row, validator.public_response(self.row))
        for change in (None, {}, {**self.row['confirmation'], 'planHash': ''},
            {**self.row['confirmation'], 'expiresAtUtc': '2099-01-01T00:06:00Z'},
            {**self.row['confirmation'], 'warning': ''}, {**self.row['confirmation'], 'token': 'private'}):
            self.reject({**self.row, 'confirmation': change})
        row = self.completed('All', 'Trash'); row['confirmation'] = self.row['confirmation']; self.reject(row)

    def test_public_paths_cannot_escape_or_rebind_the_root(self):
        for path in ('../escape', '/absolute', 'a//b', 'a/./b', 'a\\b', 'a\0b', 'a\nb', ''):
            row = copy.deepcopy(self.row); row['command']['source']['path'] = path; self.reject(row)
        row = copy.deepcopy(self.row); row['command']['source']['rootId'] = 'another.root'; self.reject(row)

    def test_partial_or_unbounded_plan_is_rejected(self):
        for key, value in (('entryCount', 0), ('entryCount', 10001), ('entryCount', True),
            ('totalBytes', -1), ('totalBytes', 16 * 1024**3 + 1), ('archiveBytes', 0),
            ('archiveBytes', 17 * 1024**3 + 1), ('readyExpiresAtUtc', '2026-10-21T00:00:00')):
            self.reject({**self.row, key: value})
        for key in ('entryCount', 'totalBytes', 'archiveBytes', 'readyExpiresAtUtc'):
            self.reject({**self.row, key: None})

    def test_unknown_null_facts_are_not_misreported_as_absence(self):
        row = self.completed('All', 'Overwrite')
        row.update(state='Unknown', deletionStarted=None, archiveRemoved=None, originalsRemoved=None)
        self.assertIs(row, validator.public_response(row))
        self.reject({**row, 'deletionStarted': False})
        self.reject({**row, 'archiveRemoved': True})

    def test_execution_id_is_separate_from_plan_and_capture(self):
        row = self.completed('All', 'Trash')
        for value in (None, row['id'], row['command']['captureId']):
            self.reject({**row, 'executionJobId': value})
        self.reject({**self.row, 'executionJobId': row['executionJobId']})

    def test_non_owned_cleanup_targets_are_rejected_without_file_mutation(self):
        with patch.object(Path, 'unlink', side_effect=AssertionError('No unlink allowed')) as unlink:
            with patch.object(Path, 'rmdir', side_effect=AssertionError('No rmdir allowed')) as rmdir:
                for path in (validator.DATA, validator.DATA / 'ordinary', validator.DATA / 'wdv-purge-bff-not-canonical'):
                    with self.subTest(path=str(path)), self.assertRaises(AssertionError):
                        validator.cleanup_live_fixture(path, (1, 2), {})
                unlink.assert_not_called(); rmdir.assert_not_called()


if __name__ == '__main__': unittest.main()
