"""Linux protocol tests only; no PTY, sudo, registry edits, or host sessions."""
import base64
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import os
import tempfile
import uuid

spec = importlib.util.spec_from_file_location('terminal_driver', Path(__file__).resolve().parents[1] / 'host/whaledeck-host-terminal.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


class FrameTests(unittest.TestCase):
    def test_dimensions(self):
        self.assertEqual(driver.frame(b'{"type":"open","columns":80,"rows":24}')['columns'], 80)
        for value in (True, False, 0, -1, 401, '80', None):
            with self.subTest(value=value), self.assertRaises(ValueError):
                driver.frame(json.dumps({'type': 'resize', 'columns': value, 'rows': 24}).encode())

    def test_input_binary(self):
        self.assertEqual(driver.frame(b'{"type":"input","data":"AAH/"}')['data'], b'\x00\x01\xff')
        raw = json.dumps({'type': 'input', 'data': base64.b64encode(b'x' * 4096).decode()}).encode()
        self.assertEqual(len(driver.frame(raw)['data']), 4096)
        for data in ('', 'not base64!', base64.b64encode(b'x' * 4097).decode()):
            with self.subTest(length=len(data)), self.assertRaises(ValueError):
                driver.frame(json.dumps({'type': 'input', 'data': data}).encode())

    def test_no_command_user_path_or_environment(self):
        for field, value in (('command', 'id'), ('user', 'root'), ('path', '/etc'), ('environment', {})):
            with self.subTest(field=field), self.assertRaises(ValueError):
                driver.frame(json.dumps({'type': 'open', 'columns': 80, 'rows': 24, field: value}).encode())

    def test_duplicates_and_unknown_shapes(self):
        for raw in (b'{"type":"close","type":"open"}', b'[]', b'null', b'{"type":"exec"}', b'{"type":"close","data":""}', b'x' * 8193):
            with self.subTest(size=len(raw)), self.assertRaises(ValueError): driver.frame(raw)

    def test_close(self):
        self.assertEqual(driver.frame(b'{"type":"close"}'), {'type': 'close'})


@unittest.skipUnless(os.geteuid() == 0, 'Root-owned isolated metadata fixture')
class RecordTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='wd-terminal-record-test-')
        self.state = patch.object(driver, 'STATE', Path(self.directory.name))
        self.state.start()
        self.invocation = uuid.uuid4().hex
        self.environment = patch.dict(os.environ, {'INVOCATION_ID': self.invocation})
        self.environment.start()

    def tearDown(self):
        self.environment.stop(); self.state.stop(); self.directory.cleanup()

    def row(self, session):
        return json.loads((driver.STATE / (session + '.json')).read_bytes())

    def test_close_and_permanent_uuid_replay_rejection(self):
        session = uuid.uuid4().hex
        driver.reserve(session)
        self.assertTrue(self.row(session)['active'])
        driver.finish(session)
        self.assertFalse(self.row(session)['active'])
        with self.assertRaises(FileExistsError): driver.reserve(session)

    def test_other_invocation_cannot_close_original(self):
        session = uuid.uuid4().hex
        driver.reserve(session)
        with patch.dict(os.environ, {'INVOCATION_ID': uuid.uuid4().hex}): driver.finish(session)
        self.assertTrue(self.row(session)['active'])
        driver.finish(session)
        self.assertFalse(self.row(session)['active'])

    def test_active_budget_released_by_stop_hook(self):
        ids = [uuid.uuid4().hex for _ in range(5)]
        for session in ids[:4]: driver.reserve(session)
        with self.assertRaises(ValueError): driver.reserve(ids[4])
        driver.finish(ids[0]); driver.reserve(ids[4])
        self.assertTrue(self.row(ids[4])['active'])

    def test_symlink_state_rejects(self):
        with tempfile.TemporaryDirectory(prefix='wd-terminal-symlink-test-') as directory:
            link = Path(directory) / 'link'; link.symlink_to(driver.STATE, target_is_directory=True)
            with patch.object(driver, 'STATE', link), self.assertRaises(PermissionError): driver.reserve(uuid.uuid4().hex)


if __name__ == '__main__':
    unittest.main()
