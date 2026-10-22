"""Pure lease contracts; no provisioned users or Docker access in these tests."""
import io
import json
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import container_file_browser_lease as lease
from ephemeral_browser_user import validate_payload, temporary_identity


class BrowserLeaseTests(unittest.TestCase):
    def payload(self):
        return {'username': 'wd-browser-' + 'a'*32, 'password': 'Wd!' + 'b'*40, 'administrator': True}

    def test_identity_request_has_no_extra_roles_paths_or_credentials_fields(self):
        request = self.payload()
        self.assertEqual((request['username'], request['password'], True), validate_payload(request))
        for change in ({'username': 'normal-user'}, {'password': 'too-short'}, {'administrator': 1}, {'path': '/data'}):
            with self.subTest(change=change), self.assertRaises(AssertionError): validate_payload({**request, **change})
        with self.assertRaises(AssertionError): validate_payload([])
        self.assertFalse(validate_payload({**request, 'administrator': False})[2])

    def owned(self):
        return {'identifier': 'a'*64, 'proofs': {'中文.bin': ((), 'B'*64)}}

    def test_evidence_and_close_only_operate_on_owned_target(self):
        stream = io.StringIO('evidence\nclose\n'); emit = Mock()
        with patch.object(lease, 'evidence', return_value={'completeDigestValidated': 1}) as evidence:
            lease.hold(self.owned(), stream=stream, emit=emit, clock=lambda: 0, ready=lambda *args: ([stream], [], []))
        evidence.assert_called_once_with('a'*64, 'B'*64)
        self.assertEqual({'browserFileEvidence': {'completeDigestValidated': 1}}, json.loads(emit.call_args.args[0]))

    def test_eof_timeout_and_signal_end_without_evidence_or_target_mutation(self):
        for stream, ready in ((io.StringIO(''), lambda *args: ([args[0][0]], [], [])),
                (io.StringIO('evidence\n'), lambda *args: ([], [], [])),
                (io.StringIO('evidence\n'), Mock(side_effect=KeyboardInterrupt))):
            with self.subTest(stream=stream), patch.object(lease, 'evidence') as evidence:
                lease.hold(self.owned(), stream=stream, clock=lambda: 0, ready=ready)
            evidence.assert_not_called()

    def test_deadline_is_absolute_not_extended_by_commands(self):
        stream = io.StringIO('evidence\nevidence\n')
        with patch.object(lease, 'evidence', return_value={}) as evidence:
            lease.hold(self.owned(), stream=stream, emit=Mock(), clock=Mock(side_effect=[0, 499, 501]), ready=lambda *args: ([stream], [], []))
        self.assertEqual(1, evidence.call_count)

    def test_unknown_or_oversized_command_is_not_executed(self):
        for command in ('delete /data\n', 'evidence '+ 'x'*80 +'\n', 'evidence'):
            stream = io.StringIO(command)
            with self.subTest(command=command), patch.object(lease, 'evidence') as evidence, self.assertRaises(AssertionError):
                lease.hold(self.owned(), stream=stream, clock=lambda: 0, ready=lambda *args: ([stream], [], []))
            evidence.assert_not_called()

    def test_identity_cleanup_targets_only_created_id_on_success_and_body_failure(self):
        for fail_body in (False, True):
            request = self.payload(); session = Mock(); session.headers = {}
            created = Mock(); created.json.return_value = {'username': request['username'], 'pk': 123}
            session.post.side_effect = [created, Mock()]; session.delete.return_value = Mock(status_code=204)
            modules = {'requests': SimpleNamespace(Session=lambda: session),
                'run_with_ephemeral_admin': SimpleNamespace(AUTHENTIK_API='http://fixture/api/',
                    container_environment=lambda: {}, require=lambda *_: 'synthetic-test-setting')}
            with self.subTest(fail_body=fail_body), patch.dict(sys.modules, modules), patch('builtins.print'):
                if fail_body:
                    with self.assertRaisesRegex(RuntimeError, 'test-body-failed'):
                        with temporary_identity(request): raise RuntimeError('test-body-failed')
                else:
                    with temporary_identity(request) as actor: self.assertEqual('123', actor['id'])
            session.delete.assert_called_once_with('http://fixture/api/core/users/123/', timeout=15)
            session.close.assert_called_once(); self.assertEqual({}, request)

    def test_identity_cleanup_failure_still_closes_http_client(self):
        request = self.payload(); session = Mock(); session.headers = {}
        created = Mock(); created.json.return_value = {'username': request['username'], 'pk': 123}
        session.post.side_effect = [created, Mock()]; session.delete.return_value = Mock(status_code=500)
        modules = {'requests': SimpleNamespace(Session=lambda: session),
            'run_with_ephemeral_admin': SimpleNamespace(AUTHENTIK_API='http://fixture/api/',
                container_environment=lambda: {}, require=lambda *_: 'synthetic-test-setting')}
        with patch.dict(sys.modules, modules), self.assertRaises(AssertionError):
            with temporary_identity(request): pass
        session.close.assert_called_once()


if __name__ == '__main__': unittest.main()
