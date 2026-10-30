#!/usr/bin/env python3
"""Frozen internal candidate on owned HDD/NVMe mounts and real HDD history.

Not an installed helper/RPC/business/API/UI gate. Only exact synthetic mount
objects are mutated; permanent candidate receipts/HDD metadata are retained.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import select
import signal
import stat
import subprocess
import sys
import tempfile
import uuid

import validate_container_file_publishing as owned
from validate_container_file_staging import load
from validate_container_directory_restart import preflight

PHASE = 'startup'


def run(*args): return subprocess.check_output(args, text=True, timeout=30).strip()


def inventory(root, reader, content):
    expected = {'': 'Directory', '权限.bin': 'File', '权限目录': 'Directory', '权限目录/代码.bin': 'File', '权限目录/空目录': 'Directory'}
    proof = {}
    for relative, kind in expected.items():
        path = root / relative; info = path.lstat(); assert not path.is_symlink()
        assert stat.S_ISDIR(info.st_mode) if kind == 'Directory' else stat.S_ISREG(info.st_mode) and info.st_nlink == 1
        if kind == 'File': assert path.read_bytes() == content
        else: assert {p.name for p in path.iterdir()} == {name.rsplit('/', 1)[-1] for name in expected if name and name.rpartition('/')[0] == relative}
        proof[relative] = list(reader.fingerprint(info))
    return proof


def remove_proved(root, expected, reader, content):
    assert inventory(root, reader, content) == expected
    for relative in ('权限目录/代码.bin', '权限目录/空目录', '权限目录', '权限.bin'):
        path = root / relative; info = list(reader.fingerprint(path.lstat()))
        if stat.S_ISREG(info[2]): assert info == expected[relative]; path.unlink()
        else: assert info[:5] == expected[relative][:5] and not list(path.iterdir()); path.rmdir()
    assert not list(root.iterdir()) and list(reader.fingerprint(root.stat()))[:5] == expected[''][:5]


def kill_at(engine, invoke, phase):
    child = os.fork()
    if child == 0:
        engine.checkpoint = lambda point: os.kill(os.getpid(), signal.SIGKILL) if point == phase else None
        try: invoke()
        except BaseException: os._exit(2)
        os._exit(3)
    descriptor = os.pidfd_open(child)
    try:
        ready = select.select([descriptor], [], [], 30)[0]
        if not ready: signal.pidfd_send_signal(descriptor, signal.SIGKILL)
        _, status = os.waitpid(child, 0)
        assert ready and os.WIFSIGNALED(status) and os.WTERMSIG(status) == signal.SIGKILL, 'OWNED_CANDIDATE_CUT_NOT_REACHED'
    finally: os.close(descriptor)


def main():
    global PHASE
    assert os.geteuid() == 0 and len(sys.argv) == 2; os.umask(0o077)
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    repo = Path(__file__).resolve().parents[2]
    names = ('whaledeck-files.py', 'whaledeck-container-files.py', 'whaledeck-container-terminal.py', 'whaledeck-container-upload.py',
        'whaledeck-container-publish.py', 'whaledeck-file-backups.py', 'whaledeck-container-directories.py', 'whaledeck-container-permissions.py')
    frozen = {name: subprocess.check_output(['git', '-C', str(repo), 'show', revision + ':deploy/host/' + name], timeout=10) for name in names}
    assert all((repo / 'deploy/host' / name).read_bytes() == value for name, value in frozen.items())
    assert Path(__file__).read_bytes() == subprocess.check_output(['git', '-C', str(repo), 'show', revision + ':deploy/validation/' + Path(__file__).name], timeout=10)
    preflight(); before = owned.common.core_snapshot(); assert len(before) == 11
    owned.common.verify_apps_directory(owned.common.APPS, __import__('pwd').getpwnam('whaledeck-agent').pw_uid)
    evidence = repo / 'deploy/artifacts' / ('container-permissions-' + uuid.uuid4().hex); evidence.mkdir(mode=0o700)
    state = evidence / 'root-state'; state.mkdir(mode=0o700)
    name = 'wdv-container-permissions-' + uuid.uuid4().hex; volume_name = name + '-nvme'
    folder = owned.common.APPS / name; folder.mkdir(mode=0o755); folder.chmod(0o755)
    identifier = volume = fast = None; proved = False; results = []
    content = hashlib.shake_256(('owned-container-permissions:' + name).encode()).digest(2 * 1024**2 + 7)
    with tempfile.TemporaryDirectory(prefix='wd-native-permissions-') as temporary:
        bundle = Path(temporary)
        for module, raw in frozen.items(): (bundle / module).write_bytes(raw); (bundle / module).chmod(0o750)
        reader = load(bundle / names[1], 'native_permission_reader')
        files = reader.trusted_module(names[0], 'native_permission_files'); docker = reader.trusted_module(names[2], 'native_permission_docker')
        upload_type = load(bundle / names[3], 'native_permission_upload').ContainerUploads
        unused = bundle / 'unused-upload'; unused.mkdir(mode=0o700); upload = upload_type(reader, files, unused)
        publisher = load(bundle / names[4], 'native_permission_publish').ContainerPublisher(upload, type('UnusedBackup', (), {'u': upload})())
        storage = load(bundle / names[5], 'native_permission_hdd').FileBackups(files)
        base_type = load(bundle / names[6], 'native_permission_receipts').ContainerDirectories
        module = load(bundle / names[7], 'native_permission_engine'); history = module.ContainerPermissionHistory(storage)
        engine = module.container_permissions_type(base_type)(upload, publisher, state, history)
        try:
            PHASE = 'owned-two-mount-container'
            run('docker', 'volume', 'create', '--label', 'io.whaledeck.validation=' + name, volume_name)
            volume = json.loads(run('docker', 'volume', 'inspect', volume_name))[0]
            assert volume['Name'] == volume_name and volume['Driver'] == 'local' and not volume.get('Options') and volume['Labels']['io.whaledeck.validation'] == name
            fast = Path(volume['Mountpoint']); assert fast.resolve(strict=True) == fast and not list(fast.iterdir())
            os.chown(fast, 1000, 1000); fast.chmod(0o755)
            assert folder.stat().st_dev != fast.stat().st_dev
            fast_device = fast.stat().st_dev; block = Path('/sys/dev/block') / (str(os.major(fast_device)) + ':' + str(os.minor(fast_device)))
            assert block.resolve(strict=True).name.startswith('nvme'), 'ACTUAL_NVME_VOLUME_NOT_PROVED'
            for root in (folder, fast):
                (root / '权限.bin').write_bytes(content); (root / '权限目录').mkdir(mode=0o750); (root / '权限目录/代码.bin').write_bytes(content); (root / '权限目录/空目录').mkdir(mode=0o700)
                for relative in ('权限.bin', '权限目录', '权限目录/代码.bin', '权限目录/空目录'):
                    path = root / relative; os.chown(path, 2100, 3100)
                    path.chmod(0o640 if path.is_file() else 0o750 if relative == '权限目录' else 0o700)
                    os.utime(path, ns=(1700000000123456789, 1700000000123456789))
            original = {'work': inventory(folder, reader, content), 'fast': inventory(fast, reader, content)}
            identifier = run('docker', 'run', '--detach', '--name', name, '--network', 'none', '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--user', '1000:1000', '--pids-limit', '16', '--memory', '32m', '--cpus', '0.25',
                '--log-driver', 'local', '--log-opt', 'max-size=1m', '--log-opt', 'max-file=2', '--label', 'io.whaledeck.validation=' + name,
                '--mount', 'type=bind,src=' + str(folder) + ',dst=/work', '--mount', 'type=volume,src=' + volume_name + ',dst=/fast',
                '--entrypoint', '/bin/sleep', owned.common.IMAGE, '600')
            inspected = json.loads(run('docker', 'inspect', identifier))[0]
            assert inspected['Image'] == owned.common.IMAGE and inspected['Config']['User'] == '1000:1000'
            assert inspected['HostConfig']['ReadonlyRootfs'] and inspected['HostConfig']['NetworkMode'] == 'none'
            assert {(m['Type'], m['Destination'], m['Source'], m['RW']) for m in inspected['Mounts']} == {('bind', '/work', str(folder), True), ('volume', '/fast', str(fast), True)}
            target = docker.target(inspected, identifier, docker.registry()); location = {'containerId': identifier, 'instanceId': target['instanceId']}
            (evidence / 'baseline.json').write_text(json.dumps({'revision': revision, **location, 'name': name, 'source': str(folder), 'volume': volume,
                'sourceDevice': folder.stat().st_dev, 'nvmeDevice': fast.stat().st_dev, 'original': original, 'sha256': hashlib.sha256(content).hexdigest().upper(),
                'bytesPerFile': len(content), 'coreBefore': before, 'candidateOnly': True}))
            with reader.ContainerRoot(location, files, docker) as session:
                def apply(prefix, relative, mode, phase=None):
                    root = folder if prefix == 'work' else fast; leaf = root / relative; old = list(reader.fingerprint(leaf.stat()))
                    command = {**location, 'path': prefix + '/' + relative, 'expectedVersion': reader.version(location, prefix + '/' + relative, leaf.stat()), 'mode': mode}
                    request = {'operationId': uuid.uuid4().hex, 'owner': 'B'*64, 'plan': engine.plan(command, session)}
                    if phase is None: result = engine.apply(request, session)
                    else: kill_at(engine, lambda: engine.apply(request, session), phase); result = engine.inspect(request, session)
                    expected = 'Aborted' if phase == 'Prepared' else 'Completed'
                    assert result['state'] == expected and result['modeConfirmed'] is (expected == 'Completed')
                    current = list(reader.fingerprint(leaf.stat()))
                    wanted = list(old); wanted[2] = wanted[2] & ~0o777 | int(mode, 8) if expected == 'Completed' else wanted[2]
                    assert current[:8] == wanted[:8]
                    if relative == '权限.bin': assert leaf.read_bytes() == content
                    else:
                        assert (leaf / '代码.bin').read_bytes() == content
                        assert list(reader.fingerprint((leaf / '代码.bin').stat())) == original[prefix]['权限目录/代码.bin']
                        assert list(reader.fingerprint((leaf / '空目录').stat())) == original[prefix]['权限目录/空目录']
                    engine.history.verify(request, result['historyDigest']); assert result['historyVerified']
                    metadata = state / (request['operationId'] + '.json'); raw = metadata.read_bytes(); record = json.loads(raw)
                    assert record['plan'] == request['plan'] and record['state'] == expected and record['owner'] == 'B'*64
                    # An installed inspector must not need to repeat chmod;
                    # here prove the internal replay directly, without hooks.
                    again = engine.apply(request, session)
                    assert again['replayed'] and {k: v for k, v in again.items() if k != 'replayed'} == {k: v for k, v in result.items() if k != 'replayed'}
                    assert metadata.read_bytes() == raw and list(reader.fingerprint(leaf.stat())) == current
                    with history.target(False) as (_, history_fd):
                        saved = storage.read_json(history_fd, request['operationId'] + '.json')
                        assert saved['plan'] == request['plan'] and saved['historyDigest'] == result['historyDigest']
                        assert os.fstat(history_fd).st_dev == folder.stat().st_dev
                        (evidence / (request['operationId'] + '.hdd.json')).write_text(json.dumps(saved))
                    result.update(prefix=prefix, relative=relative, crashPhase=phase, before=old, after=current, sourceDevice=root.stat().st_dev)
                    results.append(result); (evidence / 'results.json').write_text(json.dumps(results))
                    return result
                for prefix in ('work', 'fast'):
                    for relative, desired, initial in (('权限.bin', '0600', '0640'), ('权限目录', '0700', '0750')):
                        PHASE = 'actual-normal-mode-' + prefix
                        apply(prefix, relative, desired); apply(prefix, relative, initial)
                        for phase in ('Prepared', 'ModeChanged'):
                            PHASE = 'actual-owned-candidate-crash-' + prefix + '-' + phase
                            outcome = apply(prefix, relative, desired, phase)
                            if outcome['state'] == 'Completed': apply(prefix, relative, initial)
                PHASE = 'proved-final-objects'
                final = {'work': inventory(folder, reader, content), 'fast': inventory(fast, reader, content)}
                for prefix in ('work', 'fast'):
                    for relative in original[prefix]:
                        if relative: assert original[prefix][relative][:8] == final[prefix][relative][:8]
                assert owned.common.core_snapshot() == before
                (evidence / 'physical-final.json').write_text(json.dumps(final)); proved = True
        finally:
            if identifier is not None:
                current = json.loads(run('docker', 'inspect', identifier))[0]
                assert current['Id'] == identifier and current['Name'] == '/' + name and current['Image'] == owned.common.IMAGE
                assert current['Config']['Labels']['io.whaledeck.validation'] == name
                assert {(m['Type'], m['Destination'], m['Source']) for m in current['Mounts']} == {('bind', '/work', str(folder)), ('volume', '/fast', str(fast))}
                run('docker', 'rm', '--force', identifier)
            if proved:
                remove_proved(folder, final['work'], reader, content); folder.rmdir()
                remove_proved(fast, final['fast'], reader, content)
                assert not run('docker', 'ps', '-aq', '--filter', 'volume=' + volume_name)
                assert json.loads(run('docker', 'volume', 'inspect', volume_name))[0] == volume
                run('docker', 'volume', 'rm', volume_name)
            else: (evidence / 'preserved.json').write_text(json.dumps({'source': str(folder), 'volume': volume_name, 'unprovedDataPreserved': True}))
    assert proved and owned.common.core_snapshot() == before
    assert not folder.exists() and subprocess.run(['docker', 'volume', 'inspect', volume_name], capture_output=True).returncode != 0
    preflight(); (evidence / 'verified.json').write_text(json.dumps({'revision': revision, 'operations': len(results),
        'actualCandidateCrashCuts': sum(r['crashPhase'] is not None for r in results), 'normalHddAndNvmeFilesAndDirectories': True,
        'fullBytesAndOriginalInodesAndChildrenVerified': True, 'actualAttestedHddMetadata': True, 'coreUnchanged': True,
        'exactContainerAndSourceAndVolumeCleaned': True, 'candidateRootReceiptsAndHddHistoryRetained': True,
        'installedHelper': False, 'signedRpc': False, 'humanBffOrUi': False}))
    print(json.dumps({'containerPermissionCandidate': 'passed', 'evidence': str(evidence), 'operations': len(results), 'actualCrashCuts': 8}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        import traceback
        print(json.dumps({'containerPermissionCandidate': 'failed', 'phase': PHASE, 'failureType': type(error).__name__,
            'locations': [{'file': Path(f.filename).name, 'line': f.lineno} for f in traceback.extract_tb(error.__traceback__)], 'unprovedDataPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
