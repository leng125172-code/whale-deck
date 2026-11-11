"""Fail closed before freezing; always undo an owned freeze on exceptions."""
from contextlib import nullcontext
import errno
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

import pause_qa_retirement_execution as stop

TASK = '11111111-1111-1111-1111-111111111111'
BOOT = '22222222-2222-2222-2222-222222222222'


class StopTests(unittest.TestCase):
    def tasks(self):
        tasks = Mock()
        tasks.entry.return_value = nullcontext(10)
        tasks.d.absolute.return_value = nullcontext(11)
        tasks.d.opened.return_value = nullcontext(12)
        tasks.d.canonical.return_value = b'{}'
        tasks.completed.return_value = None; tasks.cancelled.return_value = False
        tasks.proof.return_value = dict(bootId=BOOT, device=30, inode=40,
            path='/system.slice/whaledeck-development-retirement-run-'+TASK.replace('-', '')+'.service')
        tasks.execution.boot.return_value = BOOT; tasks.execution.settled.return_value = False
        return tasks

    def test_changed_inode_or_preexisting_freeze_is_never_written(self):
        for changed in (True, False):
            with self.subTest(changed=changed):
                tasks = self.tasks()
                with patch.object(stop.os, 'fstat', return_value=SimpleNamespace(st_dev=30, st_ino=99 if changed else 40)), \
                        patch.object(stop, 'events', return_value=dict(populated=1, frozen=1)), \
                        patch.object(stop, 'write_flag') as write:
                    with self.assertRaises(AssertionError), stop.paused_live(tasks, dict(taskId=TASK), Mock()): pass
                    write.assert_not_called()

    def test_before_evidence_failure_prevents_freeze(self):
        tasks = self.tasks()
        with patch.object(stop.os, 'fstat', return_value=SimpleNamespace(st_dev=30, st_ino=40)), \
                patch.object(stop, 'events', return_value=dict(populated=1, frozen=0)), \
                patch.object(stop, 'write_flag') as write:
            with self.assertRaises(OSError), stop.paused_live(tasks, dict(taskId=TASK), Mock(side_effect=OSError('full'))): pass
            write.assert_not_called()

    def test_uncertain_freeze_write_is_followed_by_thaw(self):
        tasks = self.tasks(); saved = Mock()
        with patch.object(stop.os, 'fstat', return_value=SimpleNamespace(st_dev=30, st_ino=40)), \
                patch.object(stop, 'events', return_value=dict(populated=1, frozen=0)), \
                patch.object(stop, 'write_flag', side_effect=[OSError(errno.EIO, 'lost reply'), None]) as write:
            with self.assertRaises(OSError), stop.paused_live(tasks, dict(taskId=TASK), saved): pass
            self.assertEqual([call.args[1] for call in write.call_args_list], [b'1', b'0'])
            self.assertEqual(saved.call_args_list[-1].args[0], 'thawed')

    def test_eof_or_ui_failure_thaws_original_pinned_group(self):
        tasks = self.tasks(); saved = Mock()
        with patch.object(stop.os, 'fstat', return_value=SimpleNamespace(st_dev=30, st_ino=40)), \
                patch.object(stop, 'events', side_effect=[dict(populated=1, frozen=0), dict(populated=1, frozen=1), dict(populated=1, frozen=0)]), \
                patch.object(stop, 'write_flag') as write:
            with self.assertRaises(EOFError):
                with stop.paused_live(tasks, dict(taskId=TASK), saved) as before:
                    self.assertEqual(before['taskId'], TASK)
                    raise EOFError('parent disconnected')
            self.assertEqual([call.args[1] for call in write.call_args_list], [b'1', b'0'])
            self.assertEqual(saved.call_args_list[-1].args[0], 'thawed')

    def test_malformed_event_state_never_passes(self):
        tasks = self.tasks()
        for raw in [b'populated 1\n', b'populated 1\nfrozen 2\n', b'populated 1\nfrozen 1\nfrozen 0\n', b'', b'x'*513]:
            with self.subTest(raw=raw), patch.object(stop.os, 'read', return_value=raw):
                with self.assertRaises(AssertionError): stop.events(tasks, 12)

    def test_only_fixed_freeze_values_can_open_a_write_fd(self):
        with patch.object(stop.os, 'open') as opened:
            with self.assertRaises(AssertionError): stop.write_flag(12, b'other')
            opened.assert_not_called()
