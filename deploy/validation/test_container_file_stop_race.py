"""Actual helper branch with deterministic systemctl collection races."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires Linux root helper guard')
class ContainerWriterStopRaceTests(unittest.TestCase):
    action = 'container-file-write-stop'
    unit_prefix = 'whaledeck-container-file-write-'
    def run_helper(self, first, after, stop_exit, expected, expected_calls, show_exit=0):
        with tempfile.TemporaryDirectory() as temporary:
            directory=Path(temporary); calls=directory/'calls.json'
            fake=directory/'systemctl'; helper=directory/'helper'
            fake.write_text('#!/usr/bin/python3\nimport json,sys\nfrom pathlib import Path\n' +
                'path=Path('+repr(str(calls))+')\nrows=json.loads(path.read_text()) if path.exists() else []\n' +
                'rows.append(sys.argv[1:]);path.write_text(json.dumps(rows))\n' +
                'if sys.argv[1]=="show":\n print('+repr(first)+' if len(rows)==1 else '+repr(after)+')\n raise SystemExit('+repr(show_exit)+' if len(rows)>1 or '+repr(first=="")+' else 0)\n' +
                'if sys.argv[1]=="stop": raise SystemExit('+str(stop_exit)+')\nraise SystemExit(99)\n')
            fake.chmod(0o700)
            source=(Path(__file__).resolve().parents[1]/'host/whaledeck-privileged').read_text()
            helper.write_text(source.replace('/usr/bin/systemctl',str(fake)));helper.chmod(0o700)
            result=subprocess.run(['bash',str(helper),self.action,'a'*32,'b'*32],capture_output=True,timeout=10)
            self.assertEqual(expected,result.returncode)
            rows=json.loads(calls.read_text())
            self.assertEqual(expected_calls,[row[0] for row in rows])
            self.assertTrue(all(row[1]==self.unit_prefix+('a'*32)+'.service' for row in rows))

    def test_collected_after_inspect_is_idempotent_success(self):
        self.run_helper('b'*32,'',5,0,['show','stop','show'])

    def test_live_bound_invocation_stop_is_success(self):
        self.run_helper('b'*32,'b'*32,0,0,['show','stop'])

    def test_absent_before_stop_never_calls_stop(self):
        self.run_helper('','',5,0,['show'])

    def test_wrong_invocation_never_calls_stop(self):
        self.run_helper('c'*32,'',0,2,['show'])

    def test_failed_stop_with_surviving_invocation_remains_failure(self):
        self.run_helper('b'*32,'b'*32,5,2,['show','stop','show'])

    def test_replaced_invocation_is_not_claimed_cleaned_or_stopped_again(self):
        self.run_helper('b'*32,'c'*32,5,2,['show','stop','show'])

    def test_bus_failure_before_stop_is_not_absence(self):
        self.run_helper('','',5,2,['show'],show_exit=1)

    def test_bus_failure_after_stop_is_not_collection(self):
        self.run_helper('b'*32,'',5,2,['show','stop','show'],show_exit=1)


class ContainerReaderStopRaceTests(ContainerWriterStopRaceTests):
    action = 'container-file-transport-stop'
    unit_prefix = 'whaledeck-container-files-'


class HostFileStopRaceTests(ContainerWriterStopRaceTests):
    action = 'file-transport-stop'
    unit_prefix = 'whaledeck-files-'


class ContainerHistoryStopRaceTests(ContainerWriterStopRaceTests):
    action = 'container-file-history-stop'
    unit_prefix = 'whaledeck-container-history-'


if __name__=='__main__': unittest.main()
