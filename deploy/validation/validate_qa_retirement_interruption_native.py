#!/usr/bin/env python3
"""Real private systemd execution test for the QA interruption controller.

Work is inert: this verifies actual process death, descendants, task flocks,
proof binding and immutable journal state; it is not product acceptance.
"""
import json
import os
from pathlib import Path
import select
import shutil
import subprocess
import sys
import tempfile
import uuid

import validate_development_archive_execution as native
from interrupt_qa_retirement_execution import interrupt_locked


def tasks_for(private, value):
    native.operations.BASE = private/'journal'
    driver = native.storage.WorkspaceStorage(native.files)
    return native.operations.ArchiveOperations(driver,
        native.InertArchiveWorkDouble(driver, private, value['taskId']),
        native.control.Execution(driver, kind='retirement'))


def main():
    assert os.geteuid() == 0 and sys.argv[1:] == ['--run']
    private = Path(tempfile.mkdtemp(prefix='whaledeck-qa-interruption-', dir='/run'))
    owned = None
    try:
        native.pin_sources(private)
        for name in (Path(__file__).name, 'interrupt_qa_retirement_execution.py'):
            target = private/'validation'/name
            target.write_bytes(Path(__file__).with_name(name).read_bytes()); target.chmod(0o600)
        value = native.request(); tasks = tasks_for(private, value)
        command = private/'request.json'; command.write_bytes(tasks.d.canonical(value)); command.chmod(0o600)
        unit = 'whaledeck-development-retirement-run-'+value['taskId'].replace('-', '')+'.service'
        native.run('/usr/bin/systemd-run', '--quiet', '--collect', '--service-type=exec', '--unit='+unit,
            '--property=Slice=system.slice', '--property=RuntimeMaxSec=45', '--property=TimeoutStopSec=3',
            '--property=NoNewPrivileges=yes', '--property=CapabilityBoundingSet=',
            '--property=MemoryMax=256M', '--property=TasksMax=16', sys.executable, '-B',
            str(private/'validation'/Path(__file__).name), '--child', str(private), str(command))
        path = '/system.slice/'+unit
        info = (native.control.CGROUP/path[1:]).stat()
        owned = dict(bootId=tasks.execution.boot(), path=path, device=info.st_dev, inode=info.st_ino)
        native.wait(lambda: (private/(value['taskId']+'.entered')).exists())
        proof = native.proof(private, value); assert proof == owned
        # Real dispatch fence; the inert worker normally runs directly.
        with tasks.entry(value) as directory:
            tasks.persist(directory, 'dispatch.json', dict(schemaVersion=1,
                requestSha256=tasks.receipt(value, 'Pending')['requestSha256']))
        descendant = json.loads((private/(value['taskId']+'.entered')).read_bytes())['descendantPid']
        pidfd = os.pidfd_open(descendant)
        before = {p.name: p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        evidence = {}
        try:
            result = interrupt_locked(tasks, value, lambda key, data: evidence.update({key: data}))
            assert result['interrupted'] and select.select([pidfd], [], [], 0)[0]
        finally:
            os.close(pidfd)
        after = {p.name: p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        assert before == after and native.observe(tasks, value) == 'Interrupted'
        assert tasks.run(value)['status'] == 'Interrupted'
        assert not (private/(value['taskId']+'.computed')).exists()
        print(json.dumps(dict(passed=True, realCgroupAndDescendantStopped=True,
            journalBytesUnchanged=True, noCancellationOrResultWritten=True,
            originalTaskCannotRestart=True, productAcceptance=False, evidence=evidence)), flush=True)
    finally:
        if owned is not None:
            assert tasks.execution.settled(owned, stop=True), 'Retain unconfirmed native tree: '+str(private)
        shutil.rmtree(private)


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--child':
        private = Path(sys.argv[2]); value = json.loads(Path(sys.argv[3]).read_bytes())
        tasks_for(private, value).run(value)
    else:
        main()
