"""Observer tests only; these do not replace real deployed BFF acceptance."""
import hashlib
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock, patch
from urllib.parse import urlencode

spec = importlib.util.spec_from_file_location('container_bff_extended', Path(__file__).with_name('validate_container_file_bff_extended.py'))
probe = importlib.util.module_from_spec(spec); spec.loader.exec_module(probe)


class BffObserverTests(unittest.TestCase):
    def test_effective_mount_and_immutable_identity_are_required(self):
        folder = Path('/data/WhaleDeck/apps/wdv-container-bff-large-' + 'c'*16)
        identifier = 'a'*64; name = folder.name
        value = {'Id': identifier, 'Name': '/' + name, 'Image': probe.IMAGE,
            'Config': {'Labels': {'io.whaledeck.validation': name}},
            'HostConfig': {'NetworkMode': 'none', 'ReadonlyRootfs': True, 'Binds': None,
                'Mounts': [{'Type': 'bind', 'Source': str(folder), 'Target': '/work', 'ReadOnly': True}]},
            'Mounts': [{'Type': 'bind', 'Source': str(folder), 'Destination': '/work', 'RW': False}]}
        probe.verify_mount_container(value, identifier, name, folder)
        for field, wrong in (('Id', 'b'*64), ('Name', '/other'), ('Image', 'sha256:'+'b'*64)):
            with self.subTest(field=field), self.assertRaises(AssertionError):
                probe.verify_mount_container({**value, field: wrong}, identifier, name, folder)
        for mount in ({'Source': '/data/other'}, {'RW': True}, {'Destination': '/etc'}, {'Type': 'volume'}):
            with self.subTest(mount=mount), self.assertRaises(AssertionError):
                probe.verify_mount_container({**value, 'Mounts': [{**value['Mounts'][0], **mount}]}, identifier, name, folder)
        with self.assertRaises(AssertionError):
            probe.verify_mount_container({**value, 'HostConfig': {**value['HostConfig'], 'Binds': ['/unexpected:/work:ro']}}, identifier, name, folder)

    def test_completed_download_checks_full_stream_tail_and_budget(self):
        payload = b'first-and-last-byte'
        def response(parts):
            value = Mock(status_code=200, headers={'Content-Length': str(len(payload)), 'Cache-Control': 'no-store',
                'X-Content-Type-Options': 'nosniff', 'Content-Disposition': 'attachment; filename=test'})
            value.iter_content.return_value = parts
            value.__enter__ = Mock(return_value=value); value.__exit__ = Mock(return_value=False)
            return value
        digest = hashlib.sha256(payload).hexdigest().upper()
        for parts, succeeds in (([payload[:5], payload[5:]], True), ([payload[:-1]], False),
                ([payload[:-1]+b'!'], False), ([payload, b'x'], False)):
            session = Mock(); session.get.return_value = response(parts)
            with self.subTest(parts=parts):
                if succeeds: probe.download_verified(session, 'http://127.0.0.1/read', {}, len(payload), digest)
                else:
                    with self.assertRaises(AssertionError):
                        probe.download_verified(session, 'http://127.0.0.1/read', {}, len(payload), digest)

    def test_raw_disconnect_uses_bounded_window_and_closes_its_only_socket(self):
        wire = Mock(); wire.__enter__ = Mock(return_value=wire); wire.__exit__ = Mock(return_value=False)
        wire.recv.side_effect = [b'HTTP/1.1 200 OK\r\nContent-Length: 67108871\r\n\r\n', b'x']
        url = 'http://127.0.0.1:8080/read?' + urlencode({'path': 'work/中文.bin'})
        with patch.object(probe.socket, 'socket', return_value=wire):
            probe.interrupted_download(url, {}, probe.CANCEL_SIZE)
        wire.setsockopt.assert_called_once_with(probe.socket.SOL_SOCKET, probe.socket.SO_RCVBUF, 1024)
        wire.shutdown.assert_called_once_with(probe.socket.SHUT_RDWR)
        self.assertIn(b'%E4%B8%AD%E6%96%87.bin', wire.sendall.call_args.args[0])
        self.assertEqual(1, wire.__exit__.call_count)

    def test_disconnect_refuses_unapproved_endpoint_before_socket_creation(self):
        for url in ('https://127.0.0.1/read', 'http://example.com/read', 'http://user@127.0.0.1/read'):
            with self.subTest(url=url), patch.object(probe.socket, 'socket') as socket, self.assertRaises(AssertionError):
                probe.interrupted_download(url, {}, probe.CANCEL_SIZE)
            socket.assert_not_called()

    def test_disconnect_does_not_accept_error_empty_or_inconsistent_response(self):
        for response in (b'HTTP/1.1 403 Forbidden\r\nContent-Length: 1\r\n\r\nx',
                b'HTTP/1.1 200 OK\r\nContent-Length: 1\r\n\r\nx', b''):
            wire = Mock(); wire.__enter__ = Mock(return_value=wire); wire.__exit__ = Mock(return_value=False)
            wire.recv.return_value = response
            with self.subTest(response=response), patch.object(probe.socket, 'socket', return_value=wire), self.assertRaises(AssertionError):
                probe.interrupted_download('http://127.0.0.1/read', {}, probe.CANCEL_SIZE)

    def test_incomplete_audit_is_required_and_success_is_never_tolerated(self):
        version = 'A'*64
        row = lambda result: {'result': result, 'detailJson': '{"version":"' + version + '"}'}
        with patch.object(probe, 'audits', return_value=[row('Incomplete')]):
            probe.wait_incomplete(None, '', '', '', version)
        with patch.object(probe, 'audits', return_value=[row('Incomplete'), row('Validated')]), self.assertRaisesRegex(AssertionError, 'recorded-success'):
            probe.wait_incomplete(None, '', '', '', version)
        with patch.object(probe, 'audits', return_value=[]), patch.object(probe.time, 'monotonic', side_effect=[0, 21]), self.assertRaisesRegex(AssertionError, 'not-finalized'):
            probe.wait_incomplete(None, '', '', '', version)

    def test_public_audit_without_detail_requires_independent_matching_sql_event(self):
        import validate_metric_continuity as sql
        row = {'id': 'owned-event', 'targetId': 'a'*64, 'action': 'containers.files.download', 'result': 'Validated'}
        details = [{'id': 'owned-event', 'result': 'Validated', 'detail': {'version': 'B'*64, 'sizeBytes': 17}}]
        session = Mock(); session.get.return_value = Mock(status_code=200, json=Mock(return_value=[row]))
        with patch.object(sql, 'query', return_value=details) as query:
            result = probe.audits(session, 'http://127.0.0.1', 'owned-subject', 'a'*64)
        self.assertEqual(17, __import__('json').loads(result[0]['detailJson'])['sizeBytes'])
        self.assertEqual({'subject': 'owned-subject', 'target': 'a'*64}, query.call_args.kwargs)
        for invalid in ([], [{**details[0], 'id': 'other-event'}], [{**details[0], 'result': 'Incomplete'}]):
            with self.subTest(invalid=invalid), patch.object(sql, 'query', return_value=invalid), self.assertRaises(AssertionError):
                probe.audits(session, 'http://127.0.0.1', 'owned-subject', 'a'*64)


@unittest.skipUnless(os.name == 'posix' and getattr(os, 'geteuid', lambda: -1)() == 0, 'real root metadata required')
class PayloadObserverTests(unittest.TestCase):
    def test_failed_body_cleans_only_unchanged_proven_payload_and_preserves_changed_bytes(self):
        for changed in (False, True):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); name = 'wdv-container-bff-large-' + 'c'*16; folder = root/name
                identifier = 'a'*64
                container = {'Id': identifier, 'Name': '/'+name, 'Image': probe.IMAGE,
                    'Config': {'Labels': {'io.whaledeck.validation': name}},
                    'HostConfig': {'NetworkMode': 'none', 'ReadonlyRootfs': True, 'Binds': None,
                        'Mounts': [{'Type': 'bind', 'Source': str(folder), 'Target': '/work', 'ReadOnly': True}]},
                    'Mounts': [{'Type': 'bind', 'Source': str(folder), 'Destination': '/work', 'RW': False}]}
                def command(args):
                    if args[:3] == ['docker', 'image', 'inspect']: return json.dumps([{'Id': probe.IMAGE, 'Config': {}}]).encode()
                    if args[:2] == ['docker', 'run']: return identifier.encode()
                    if args == ['docker', 'inspect', identifier]: return json.dumps([container]).encode()
                    if args == ['docker', 'rm', '--force', identifier]: return b''
                    raise AssertionError('unexpected-fake-docker-command')
                output = io.StringIO()
                with patch.object(probe, 'APPS', root), patch.object(probe, 'SIZE', 1031), patch.object(probe, 'CANCEL_SIZE', 2055), \
                        patch.object(probe, 'verify_apps_directory'), patch.object(probe.pwd, 'getpwnam', return_value=SimpleNamespace(pw_uid=0)), \
                        patch.object(probe.uuid, 'uuid4', return_value=SimpleNamespace(hex='c'*32)), \
                        patch.object(probe, 'core_snapshot', return_value={'fixed-core': 'unchanged'}), patch.object(probe, 'command', side_effect=command), \
                        patch.object(probe.subprocess, 'run', side_effect=[SimpleNamespace(returncode=0, stdout=json.dumps([container]).encode()), SimpleNamespace(returncode=1)]), \
                        contextlib.redirect_stdout(output), self.assertRaisesRegex(RuntimeError, 'observer-body-failed'):
                    with probe.fixture() as owned:
                        if changed: (owned['folder']/'中文.bin').write_bytes(b'changed-evidence')
                        raise RuntimeError('observer-body-failed')
                result = json.loads(output.getvalue())
                self.assertFalse(result['fixtureBodyCompleted'])
                self.assertEqual(not changed, result['verifiedPayloadRemoved'])
                self.assertEqual(changed, result['unverifiedPayloadPreserved'])
                self.assertEqual(changed, folder.exists())

    def test_full_payload_identity_tail_and_unexpected_children_refuse_cleanup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            folder = root / ('wdv-container-bff-large-' + 'c'*16); folder.mkdir(mode=0o750)
            leaf = folder / '中文.bin'; leaf.write_bytes(b'full-content-and-tail'); leaf.chmod(0o640)
            proofs = {leaf.name: (probe.fingerprint(leaf), probe.file_digest(leaf))}
            identity = folder.stat().st_dev, folder.stat().st_ino
            with patch.object(probe, 'APPS', root):
                probe.verify_payload(folder, identity, proofs)
                with self.assertRaises(AssertionError): probe.verify_payload(folder, (identity[0], identity[1]+1), proofs)
                unexpected = folder / 'extra'; unexpected.write_bytes(b'x')
                with self.assertRaises(AssertionError): probe.verify_payload(folder, identity, proofs)
                unexpected.unlink()
                wrong_digest = {leaf.name: (probe.fingerprint(leaf), '0'*64)}
                with self.assertRaises(AssertionError): probe.verify_payload(folder, identity, wrong_digest)
                leaf.write_bytes(b'full-content-and-tai!')
                with self.assertRaises(AssertionError): probe.verify_payload(folder, identity, proofs)
                # Verifier only checks and must never destroy evidence itself.
                self.assertTrue(leaf.is_file())


if __name__ == '__main__': unittest.main()
