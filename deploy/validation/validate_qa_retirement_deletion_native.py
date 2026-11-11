#!/usr/bin/env python3
"""Private /run source tree, production unlink engine and real cgroup death.

No installed workspace, quota, identity or HDD is opened. This verifies the
progress-triggered control and durable deletion prefix, not product acceptance.
"""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time

import validate_development_archive_execution as native
from interrupt_qa_retirement_execution import interrupt_locked
from retirement_deletion_progress import InstalledDeletionProgress, bind_prepared

engine_module = native.load('whaledeck-development-retirement-execution.py')
tree_module = native.load('whaledeck-file-tree.py')


class DeletingWork:
    durable_json = native.archives.DevelopmentArchives.durable_json

    def __init__(self, driver, private):
        self.d, self.private = driver, private

    def plan(self, _storage, _epoch):
        (self.private/'entered').touch(mode=0o600)
        deadline = time.monotonic() + 20
        while not (self.private/'release').exists():
            assert time.monotonic() < deadline
            time.sleep(0.002)
        remove(self.d, self.private)
        return dict(privateDeletionFinished=True)


def remove(driver, private):
    engine = engine_module.DevelopmentRetirementExecution(type('Archive', (), dict(d=driver))(), native.archives.PayloadFiles)
    with driver.absolute(private/'source') as allocation, driver.absolute(private/'tree-journal') as journal:
        manifest = driver.read_json(journal, 'prepared.json', 32 * 1024**2)['manifest']
        engine.remove_tree(allocation, journal, manifest)


def tasks_for(private):
    native.operations.BASE = private/'journal'
    driver = native.storage.WorkspaceStorage(native.files)
    return native.operations.ArchiveOperations(driver, DeletingWork(driver, private),
        native.control.Execution(driver, kind='retirement'))


class PrivateProgress(InstalledDeletionProgress):
    def __init__(self, private):
        super().__init__(); self.private = private

    @contextlib.contextmanager
    def opened(self, tasks, value):
        driver = tasks.d
        with driver.absolute(self.private/'tree-journal') as journal, driver.absolute(self.private/'source/data') as payload:
            prepared = driver.read_json(journal, 'prepared.json', 32 * 1024**2)
            bind_prepared(driver, prepared, prepared['request'])
            binding = dict(preparedSha256=hashlib.sha256(driver.canonical(prepared)).hexdigest(),
                journalIdentity=driver.files.publication_identity(os.fstat(journal))[:5],
                payloadIdentity=driver.files.publication_identity(os.fstat(payload))[:5],
                manifestSha256=prepared['manifest']['manifestSha256'])
            yield journal, payload, prepared['manifest'], binding

    def wait(self, tasks, value):
        # Only this isolated test's start gate; production observer only reads.
        (self.private/'release').touch(mode=0o600)
        return super().wait(tasks, value)


def main():
    assert os.geteuid() == 0 and sys.argv[1:] == ['--run']
    os.umask(0o077)
    private = Path(tempfile.mkdtemp(prefix='whaledeck-qa-partial-delete-', dir='/run'))
    owned = None
    try:
        native.pin_sources(private)
        for name in ('whaledeck-development-retirement-execution.py', 'whaledeck-file-tree.py'):
            target = private/'host'/name
            target.write_bytes((Path(__file__).resolve().parents[1]/'host'/name).read_bytes()); target.chmod(0o600)
        for name in (Path(__file__).name, 'interrupt_qa_retirement_execution.py', 'retirement_deletion_progress.py'):
            target = private/'validation'/name
            target.write_bytes(Path(__file__).with_name(name).read_bytes()); target.chmod(0o600)
        source = private/'source'; source.mkdir(mode=0o700)
        payload = source/'data'; payload.mkdir(mode=0o700)
        journal = private/'tree-journal'; journal.mkdir(mode=0o700)
        for index in range(800):
            (payload/f'源码-{index:04}.bin').write_bytes(bytes(range(256))*4)
        value = native.request(); tasks = tasks_for(private); driver = tasks.d
        with driver.absolute(source) as allocation:
            command = dict(rootId='private-native', path='data', expectedVersion=driver.files.version(payload.stat()))
            adapter = native.archives.PayloadFiles(driver.files, allocation, command,
                dict(allowedActions=['files-list','files-download']))
            manifest = tree_module.FileTreeManifest(adapter).scan(command)
        plan = dict(sourceManifestSha256=manifest['manifestSha256'].lower(), entryCount=manifest['entryCount'])
        plan['retirementPlanSha256'] = hashlib.sha256(driver.canonical(plan)).hexdigest()
        request = dict(retirementPlanSha256=plan['retirementPlanSha256'])
        prepared = dict(request=request, bindingSha256=hashlib.sha256(driver.canonical(request)).hexdigest(), plan=plan, manifest=manifest)
        (journal/'prepared.json').write_bytes(driver.canonical(prepared))
        command_file = private/'request.json'; command_file.write_bytes(driver.canonical(value))
        unit = 'whaledeck-development-retirement-run-'+value['taskId'].replace('-', '')+'.service'
        native.run('/usr/bin/systemd-run','--quiet','--collect','--service-type=exec','--unit='+unit,
            '--property=Slice=system.slice','--property=RuntimeMaxSec=60','--property=TimeoutStopSec=3',
            '--property=NoNewPrivileges=yes','--property=CapabilityBoundingSet=',
            '--property=MemoryMax=256M','--property=TasksMax=16',sys.executable,'-B',
            str(private/'validation'/Path(__file__).name),'--child',str(private),str(command_file))
        group_path = '/system.slice/'+unit
        group_info = (native.control.CGROUP/group_path[1:]).stat()
        owned = dict(bootId=tasks.execution.boot(),path=group_path,device=group_info.st_dev,inode=group_info.st_ino)
        native.wait(lambda: (private/'entered').exists())
        assert native.proof(private, value) == owned
        with tasks.entry(value) as directory:
            tasks.persist(directory,'dispatch.json',dict(schemaVersion=1,requestSha256=tasks.receipt(value,'Pending')['requestSha256']))
        before = {p.name:p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        evidence = {}
        result = interrupt_locked(tasks,value,lambda key,data:evidence.update({key:data}),progress=PrivateProgress(private))
        progress = result['deletionProgress']
        assert progress['removedFiles'] > 0 and progress['remainingFiles'] > 0
        assert before == {p.name:p.read_bytes() for p in (private/'journal'/value['taskId']).iterdir()}
        assert native.observe(tasks,value) == tasks.run(value)['status'] == 'Interrupted'
        old_intents = {p.name:p.read_bytes() for p in journal.glob('remove-*.json')}
        remove(driver,private)
        assert not payload.exists()
        assert all((journal/name).read_bytes() == raw for name,raw in old_intents.items())
        print(json.dumps(dict(passed=True,realCgroupDeath=True,productionUnlinkEngine=True,
            removedFiles=progress['removedFiles'],remainingFiles=progress['remainingFiles'],
            exactIntentForEveryAbsence=True,originalTaskCannotRestart=True,taskJournalBytesUnchanged=True,
            sameManifestResumed=True,oldIntentBytesPreserved=True,productAcceptance=False)),flush=True)
    finally:
        if owned is not None:
            assert tasks.execution.settled(owned,stop=True), 'Retain unconfirmed native tree: '+str(private)
        shutil.rmtree(private)


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--child':
        private = Path(sys.argv[2]); value = json.loads(Path(sys.argv[3]).read_bytes())
        tasks_for(private).run(value)
    else:
        main()
