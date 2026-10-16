"""Linux protocol tests only; no PTY, sudo, registry edits, or host sessions."""
import base64
import importlib.util
import json
from pathlib import Path
import unittest

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


if __name__ == '__main__':
    unittest.main()
