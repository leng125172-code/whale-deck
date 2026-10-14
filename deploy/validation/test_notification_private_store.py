"""Pure validation locally; Linux filesystem assertions use only a new scratch dir."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import uuid

path = Path(__file__).resolve().parents[1] / 'host/whaledeck-notification-config.py'
spec = importlib.util.spec_from_file_location('notification_private', path)
private = importlib.util.module_from_spec(spec)
spec.loader.exec_module(private)


def target():
    return {'provider': 'Webhook', 'url': 'https://example.test/events', 'allowedAddresses': [], 'minimumIntervalSeconds': 5}


class Validation(unittest.TestCase):
    def test_supported_fixed_provider_shapes(self):
        private.validate(target(), None)
        private.validate({'provider': 'Email', 'smtpHost': 'mail.example.test', 'from': 'sender@example.test', 'to': ['recipient@example.test'], 'allowedAddresses': []}, {'username': 'fixture-only', 'password': 'fixture-only-password'})
        private.validate({'provider': 'DingTalk', 'url': 'https://oapi.dingtalk.com/robot/send?access_token=fixture', 'allowedAddresses': []}, {'signingSecret': 'fixture-only'})
        private.validate({'provider': 'Feishu', 'url': 'https://open.feishu.cn/open-apis/bot/v2/hook/fixture', 'allowedAddresses': []}, {'signingSecret': 'fixture-only'})
        private.validate({'provider': 'WeCom', 'url': 'https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=fixture', 'allowedAddresses': []}, None)

    def test_unsafe_addresses_cannot_be_approved(self):
        for value in ['127.0.0.1', '::1', '::ffff:127.0.0.1', '169.254.169.254', '0.0.0.0', '::', '224.0.0.1', 'fe80::1', 'ff02::1', '64:ff9b::1', '2002:a9fe::1', '2001::1']:
            with self.subTest(address=value):
                self.assertFalse(private.allowed_ip(value, True))
                with self.assertRaises(private.Rejected):
                    private.validate({**target(), 'allowedAddresses': [value], 'allowPrivateNetwork': True}, None)

    def test_private_requires_explicit_address_and_transport_permission(self):
        for payload in [{**target(), 'allowedAddresses': ['192.168.1.20']}, {**target(), 'allowPrivateNetwork': True}, {**target(), 'url': 'http://192.168.1.20/events', 'allowedAddresses': ['192.168.1.20'], 'allowPrivateNetwork': True}]:
            with self.assertRaises(private.Rejected):
                private.validate(payload, None)
        private.validate({**target(), 'url': 'http://192.168.1.20/events', 'allowedAddresses': ['192.168.1.20'], 'allowPrivateNetwork': True, 'allowHttp': True}, None)

    def test_duplicate_unknown_deep_and_oversized_json_rejected(self):
        for raw in [b'{"version":1,"version":1}', b'{"number":NaN}', b'x' * 65537, b'{"a":' * 10 + b'0' + b'}' * 10]:
            with self.assertRaises(private.Rejected):
                private.decode(raw)
        with self.assertRaises(private.Rejected):
            private.validate({**target(), 'arbitraryPath': '/tmp'}, None)

    def test_cross_provider_and_control_injection_rejected(self):
        for payload, credential in [({**target(), 'url': 'https://user:password@example.test'}, None), ({**target(), 'url': 'https://localhost/'}, None), (target(), {'password': 'fixture'}), (target(), {'bearerToken': 'fixture\u0085value'}), ({**target(), 'minimumIntervalSeconds': True}, None), ({**target(), 'smtpPort': True}, None)]:
            with self.assertRaises(private.Rejected):
                private.validate(payload, credential)

    def test_bot_host_path_query_port_and_credentials_are_strict(self):
        for url in ['https://evil.example.test/robot/send?access_token=fixture', 'https://oapi.dingtalk.com:8443/robot/send?access_token=fixture', 'https://oapi.dingtalk.com/robot/send?access_token=fixture&other=x']:
            with self.assertRaises(private.Rejected):
                private.validate({**target(), 'provider': 'DingTalk', 'url': url}, {'signingSecret': 'fixture'})


@unittest.skipUnless(os.name == 'posix', 'Real Linux permissions/rename/locking are required')
class Filesystem(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix='wd-private-test-')
        os.chmod(self.scratch.name, 0o700)
        self.store = private.Store(self.scratch.name, os.getuid())
        self.channel = str(uuid.uuid4())
        self.command = {'channelId': self.channel, 'operationId': str(uuid.uuid4()), 'expectedRevision': 'EMPTY', 'target': target(), 'credentialMode': 'replace', 'credential': {'bearerToken': 'fixture-only-not-a-real-token'}}

    def tearDown(self):
        self.scratch.cleanup()

    def read(self):
        return self.store.execute('inspect', {'channelId': self.channel})

    def test_atomic_private_write_and_exact_response_loss_replay(self):
        first = self.store.execute('apply', self.command)
        replay = self.store.execute('apply', self.command)
        self.assertEqual({**first, 'replayed': True}, replay)
        self.assertTrue(self.read()['hasCredential'])
        self.assertEqual(first['revision'], self.read()['revision'])
        self.assertEqual(0o600, (Path(self.scratch.name) / 'registry.json').stat().st_mode & 0o777)
        self.assertNotIn('fixture-only', json.dumps(first))
        self.assertFalse(any(p.name.startswith('.registry-') for p in Path(self.scratch.name).iterdir()))

    def test_retry_after_commit_then_directory_fsync_failure(self):
        fsync = os.fsync
        calls = []
        def failing(fd):
            calls.append(fd)
            if len(calls) == 2:
                raise OSError('synthetic-after-rename')
            return fsync(fd)
        with patch.object(private.os, 'fsync', side_effect=failing):
            with self.assertRaises(OSError):
                self.store.execute('apply', self.command)
        replay = self.store.execute('apply', self.command)
        self.assertTrue(replay['replayed'])
        self.assertEqual(self.read()['revision'], replay['revision'])

    def test_old_receipt_returns_original_revision_after_later_updates(self):
        first = self.store.execute('apply', self.command)
        next_command = {**self.command, 'operationId': str(uuid.uuid4()), 'expectedRevision': first['revision'], 'target': {**target(), 'url': 'https://other.example.test/events'}}
        later = self.store.execute('apply', next_command)
        self.assertNotEqual(first['revision'], later['revision'])
        self.assertEqual(first['revision'], self.store.execute('apply', self.command)['revision'])

    def test_stale_revision_or_key_reuse_cannot_overwrite(self):
        first = self.store.execute('apply', self.command)
        for command in [{**self.command, 'operationId': str(uuid.uuid4())}, {**self.command, 'target': {**target(), 'url': 'https://other.example.test/'}}]:
            with self.assertRaises(private.Rejected):
                self.store.execute('apply', command)
        self.assertEqual(first['revision'], self.read()['revision'])

    def test_credential_keep_replace_remove_and_scoped_binding_delete(self):
        first = self.store.execute('apply', self.command)
        second = {**self.command, 'operationId': str(uuid.uuid4()), 'expectedRevision': first['revision'], 'credentialMode': 'keep'}
        second.pop('credential')
        kept = self.store.execute('apply', second)
        self.assertTrue(kept['hasCredential'])
        third = {**second, 'operationId': str(uuid.uuid4()), 'expectedRevision': kept['revision'], 'credentialMode': 'remove'}
        removed = self.store.execute('apply', third)
        self.assertFalse(removed['hasCredential'])
        deletion = {'channelId': self.channel, 'operationId': str(uuid.uuid4()), 'expectedRevision': removed['revision']}
        result = self.store.execute('remove', deletion)
        self.assertFalse(result['hasTarget'])
        self.assertEqual({**result, 'replayed': True}, self.store.execute('remove', deletion))

    def test_symbolic_and_hard_links_or_open_permissions_rejected(self):
        registry = Path(self.scratch.name) / 'registry.json'
        other = Path(self.scratch.name) / 'other.json'
        other.write_text('{"version":1,"targets":{},"credentials":{}}')
        other.chmod(0o600)
        registry.symlink_to(other)
        with self.assertRaises((private.Rejected, OSError)):
            self.read()
        registry.unlink()
        os.link(other, registry)
        with self.assertRaises(private.Rejected):
            self.read()
        registry.unlink()
        self.store.execute('apply', self.command)
        registry.chmod(0o640)
        with self.assertRaises(private.Rejected):
            self.read()

    def test_budgets_do_not_modify_file_on_failure(self):
        self.store.execute('apply', self.command)
        registry = Path(self.scratch.name) / 'registry.json'
        value = json.loads(registry.read_text())
        for i in range(31):
            value['targets']['legacy-' + str(i)] = target()
        registry.write_bytes(private.canonical(value))
        before = registry.read_bytes()
        with self.assertRaises(private.Rejected):
            self.store.execute('apply', {**self.command, 'channelId': str(uuid.uuid4()), 'operationId': str(uuid.uuid4()), 'expectedRevision': self.read()['revision']})
        self.assertEqual(before, registry.read_bytes())

    def test_lock_is_nonblocking_and_unknown_path_fields_rejected(self):
        with self.store.locked():
            with self.assertRaises(private.Rejected):
                self.read()
        with self.assertRaises(private.Rejected):
            self.store.execute('inspect', {'channelId': self.channel, 'path': '/etc/passwd'})

    def test_manual_file_change_invalidates_opaque_version(self):
        first = self.store.execute('apply', self.command)
        registry = Path(self.scratch.name) / 'registry.json'
        value = json.loads(registry.read_text())
        value['targets']['wd-' + uuid.UUID(self.channel).hex]['url'] = 'https://manual.example.test/events'
        registry.write_bytes(private.canonical(value))
        self.assertNotEqual(first['revision'], self.read()['revision'])
        with self.assertRaises(private.Rejected):
            self.store.execute('apply', {**self.command, 'operationId': str(uuid.uuid4()), 'expectedRevision': first['revision']})


if __name__ == '__main__':
    unittest.main()
