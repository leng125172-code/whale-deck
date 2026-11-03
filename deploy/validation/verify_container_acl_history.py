#!/usr/bin/env python3
"""Independent post-cleanup read of actual HDD preimages and original receipts."""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

import validate_container_acl_history as native
from validate_container_file_staging import load
from validate_container_recursive_ownership_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2
    os.umask(0o077)
    os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    repo = Path(__file__).resolve().parents[2]
    evidence = Path(sys.argv[1])
    assert evidence.parent == repo / 'deploy/artifacts' and re.fullmatch('container-acl-history-[a-f0-9]{32}', evidence.name)
    info = evidence.lstat()
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    accepted = json.loads((evidence / 'accepted.json').read_bytes())
    source = json.loads((evidence / 'source.json').read_bytes())
    baseline = json.loads((evidence / 'baseline.json').read_bytes())
    rows = json.loads((evidence / 'results.json').read_bytes())
    assert accepted['passed'] and accepted['candidateOnly'] and not accepted['restorationExecuted'] and not accepted['signedRpcOrUi']
    assert accepted['captures'] == accepted['readonlyReads'] == len(rows) == 5
    assert source['sourceRevision'] == accepted['sourceRevision'] and re.fullmatch('[a-f0-9]{40}', source['sourceRevision'])
    blobs = {name: subprocess.check_output(['git', '-C', str(repo), 'show', source['sourceRevision'] + ':deploy/host/' + name]) for name in native.NAMES}
    assert {name: hashlib.sha256(raw).hexdigest() for name, raw in blobs.items()} == source['modules']
    frozen = evidence / 'frozen'
    frozen.mkdir(mode=0o700, exist_ok=True)
    info = frozen.lstat()
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    with tempfile.TemporaryDirectory(prefix='wd-acl-history-independent-') as temporary:
        bundle = Path(temporary)
        for name, raw in blobs.items():
            p = bundle / name; p.write_bytes(raw); p.chmod(0o750)
            p = frozen / name
            if os.path.lexists(p):
                info = p.lstat()
                assert stat.S_ISREG(info.st_mode) and info.st_uid == info.st_gid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == 0o600
                assert p.read_bytes() == raw
            else:
                with p.open('xb') as stream: stream.write(raw)
                p.chmod(0o600)
        reader = load(bundle / native.NAMES[1], 'history_independent_reader')
        files = reader.trusted_module(native.NAMES[0], 'history_independent_files')
        upload = load(bundle / native.NAMES[3], 'history_independent_upload').ContainerUploads(reader, files, evidence / 'candidate-state')
        publisher = load(bundle / native.NAMES[4], 'history_independent_publish').ContainerPublisher(upload, type('UnusedContent', (), {'u': upload})())
        storage = load(bundle / native.NAMES[5], 'history_independent_storage').FileBackups(files)
        directories = load(bundle / native.NAMES[6], 'history_independent_directories').ContainerDirectories
        permission = load(bundle / native.NAMES[7], 'history_independent_permission')
        acl = load(bundle / native.NAMES[8], 'history_independent_acl')
        history = acl.acl_history_type(permission)(storage)
        engine = acl.container_acl_type(directories)(upload, publisher, evidence / 'candidate-state', history)
        catalog = load(bundle / native.NAMES[9], 'history_independent_catalog').ContainerAclHistoryReader(engine)
        with history.target(False) as (_, fd):
            assert os.fstat(fd).st_dev == baseline['hddDevice']
            for row in rows:
                request, result = row['request'], row['result']
                operation = request['operationId']
                value = catalog.metadata(fd, operation)
                assert value == json.loads((evidence / (operation + '.hdd.json')).read_bytes())
                assert value['plan'] == request['plan'] and value['historyDigest'] == result['historyDigest']
                assert row['read']['accessAcl'] == value['plan']['accessAcl'] and row['read']['defaultAcl'] == value['plan']['defaultAcl']
                raw = (evidence / 'candidate-state' / (operation + '.json')).read_bytes()
                assert hashlib.sha256(raw).hexdigest() == row['rootSha256']
                receipt = json.loads(raw)
                assert receipt['state'] == 'Completed' and receipt['historyVerified'] and receipt['plan'] == request['plan']
                assert receipt['historyDigest'] == value['historyDigest']
    pages = json.loads((evidence / 'pages.json').read_bytes())
    for path in ('work/acl-file.bin', 'work/acl-directory'):
        subset = [p for p in pages if p['path'] == path]
        assert subset and subset[-1]['complete'] and len({p['version'] for p in subset}) == 1
        ids = [v['captureId'] for p in subset for v in p['entries']]
        assert len(ids) == len(set(ids))
        assert set(ids) == {r['request']['operationId'] for r in rows if r['request']['plan']['command']['path'] == path}
    source_path = Path(baseline['source'])
    assert source_path.parent == native.existing.owned.common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', source_path.name)
    assert not source_path.exists()
    assert subprocess.run(['docker', 'inspect', baseline['containerId']], capture_output=True, timeout=10).returncode != 0
    current = json.loads(json.dumps(native.existing.owned.common.core_snapshot()))
    assert current == source['core'] and host_identity(accepted['runtimeRevision']) == source['hosts']
    assert not Path('/usr/local/libexec/whaledeck-container-acl-history.py').exists()
    no_active_work()
    result = dict(independentPassed=True, metadata=5, rootReceiptsUnchanged=5, paginationVerified=True,
        exactCleanup=True, coreAndHostsUnchanged=True, installed=False, restorationExecuted=False)
    native.save(evidence, 'independent.json', result)
    print(json.dumps(result))


if __name__ == '__main__': main()
