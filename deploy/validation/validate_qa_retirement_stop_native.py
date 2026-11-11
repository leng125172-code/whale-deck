#!/usr/bin/env python3
"""Real frozen cgroup Cancel and alarm-thaw; work itself is inert."""
import json
import os
from pathlib import Path
import select
import shutil
import signal
import sys
import tempfile
import time

import validate_development_archive_execution as native
from validate_qa_retirement_interruption_native import tasks_for
from pause_qa_retirement_execution import events, expired, paused_live, require_stopped


def main():
    assert os.geteuid() == 0 and sys.argv[1:] == ['--run']
    private = Path(tempfile.mkdtemp(prefix='whaledeck-qa-stop-', dir='/run'))
    owned, evidence = [], {}
    try:
        native.pin_sources(private)
        for name in (Path(__file__).name, 'pause_qa_retirement_execution.py',
                'interrupt_qa_retirement_execution.py', 'validate_qa_retirement_interruption_native.py'):
            target = private/'validation'/name
            target.write_bytes(Path(__file__).with_name(name).read_bytes()); target.chmod(0o600)
        def launch():
            value = native.request(); tasks = tasks_for(private, value)
            command = private/(value['taskId']+'.json')
            command.write_bytes(tasks.d.canonical(value)); command.chmod(0o600)
            unit = 'whaledeck-development-retirement-run-'+value['taskId'].replace('-', '')+'.service'
            native.run('/usr/bin/systemd-run', '--quiet', '--collect', '--service-type=exec', '--unit='+unit,
                '--property=Slice=system.slice', '--property=RuntimeMaxSec=45', '--property=TimeoutStopSec=3',
                '--property=NoNewPrivileges=yes', '--property=CapabilityBoundingSet=',
                '--property=MemoryMax=256M', '--property=TasksMax=16', sys.executable, '-B',
                str(private/'validation'/Path(__file__).name), '--child', str(private), str(command))
            path = '/system.slice/'+unit; info = (native.control.CGROUP/path[1:]).stat()
            proof = dict(bootId=tasks.execution.boot(), path=path, device=info.st_dev, inode=info.st_ino); owned.append(proof)
            native.wait(lambda: (private/(value['taskId']+'.entered')).exists())
            assert native.proof(private, value) == proof
            with tasks.entry(value) as directory:
                tasks.persist(directory, 'dispatch.json', dict(schemaVersion=1,
                    requestSha256=tasks.receipt(value, 'Pending')['requestSha256']))
            return value, tasks, proof
        value, tasks, proof = launch()
        before = {p.name: p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        descendant = json.loads((private/(value['taskId']+'.entered')).read_bytes())['descendantPid']
        pidfd = os.pidfd_open(descendant)
        try:
            with paused_live(tasks, value, lambda key, data: evidence.update({key: data})) as frozen:
                assert frozen and native.observe(tasks, value) == 'Running'
                # Actual native Cancel takes the same task lock, persists the
                # cancellation bit and kills the real frozen cgroup/descendant.
                assert tasks.cancel(value)['status'] == 'Stopped'
                stopped = require_stopped(tasks, value); assert stopped['stopped']
                assert select.select([pidfd], [], [], 0)[0]
        finally:
            os.close(pidfd)
        after = {p.name: p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        assert before['cancel'] == b'0' and after.pop('cancel') == b'1'
        before.pop('cancel'); assert before == after
        assert tasks.run(value)['status'] == 'Stopped'
        assert evidence['thawed']['passed'] and not (private/(value['taskId']+'.computed')).exists()
        value, tasks, proof = launch()
        old_handler = signal.signal(signal.SIGALRM, expired)
        try:
            try:
                with paused_live(tasks, value, lambda *_: None) as frozen:
                    assert frozen
                    signal.setitimer(signal.ITIMER_REAL, 0.15)
                    time.sleep(3)
                raise AssertionError('Alarm failed')
            except TimeoutError:
                pass
            finally:
                signal.setitimer(signal.ITIMER_REAL, 0)
            with tasks.d.absolute(native.control.CGROUP/proof['path'][1:]) as group:
                assert events(tasks, group) == dict(populated=1, frozen=0)
            assert native.observe(tasks, value) == 'Running'
        finally:
            signal.signal(signal.SIGALRM, old_handler)
        print(json.dumps(dict(passed=True,realFrozenExecutionCancelled=True,descendantSettled=True,
            onlyProductCancelWritesTombstone=True,originalTaskCannotRestart=True,
            realAlarmThawsStillLiveTask=True,productAcceptance=False,evidence=evidence)), flush=True)
    finally:
        for proof in reversed(owned):
            assert tasks.execution.settled(proof, stop=True), 'Retain unconfirmed tree: '+str(private)
        shutil.rmtree(private)


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--child':
        private = Path(sys.argv[2]); value = json.loads(Path(sys.argv[3]).read_bytes())
        tasks_for(private, value).run(value)
    else:
        main()
