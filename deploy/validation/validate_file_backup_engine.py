#!/usr/bin/env python3
"""Physical HDD copy in an owned root-only sandbox; no production config edits."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import uuid

sys.dont_write_bytecode = True
HDD_PARENT = Path('/data/DockerData/whaledeck')
CONTENT = bytes(range(256)) * 8192 + b'\x00owned\xff'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module; spec.loader.exec_module(module)
    return module


def inside(fixture):
    assert fixture.parent == Path('/var/lib') and fixture.name.startswith('wdv-backup-engine-') and not fixture.is_symlink()
    assert fixture.stat().st_uid == 0 and stat.S_IMODE(fixture.stat().st_mode) == 0o700
    files = load('file_backup_validation_driver', fixture / 'host/whaledeck-files.py')
    backups = load('file_backup_validation_store', fixture / 'host/whaledeck-file-backups.py')
    backups.CONFIGURATION = str(fixture / 'policy.json')
    engine = backups.FileBackups(files); source = fixture / 'source.bin'
    information = source.stat(); old_sha = hashlib.sha256(CONTENT).hexdigest().upper()
    record = {'operationId': (fixture / 'operation').read_text(), 'owner': 'F' * 64, 'rootId': 'validation.files', 'path': source.name,
        'expectedVersion': files.version(information), 'originalIdentity': files.publication_identity(information), 'originalSha256': old_sha}
    parent = os.open(fixture, os.O_RDONLY | os.O_DIRECTORY)
    try:
        result = engine.ensure(record, parent, source.name)
        assert result['differentSourceDevice'] and result['sha256'] == old_sha and result['sizeBytes'] == len(CONTENT)
        assert engine.ensure(record, parent, source.name) == result
        assert source.read_bytes() == CONTENT
        # Change only our disposable policy, never unmount or change the real HDD.
        configuration = json.loads((fixture / 'policy.json').read_bytes())
        configuration['filesystemUuid'] = '00000000-0000-0000-0000-000000000001' if configuration['filesystemUuid'] != '00000000-0000-0000-0000-000000000001' else '00000000-0000-0000-0000-000000000002'
        (fixture / 'policy.json').write_text(json.dumps(configuration))
        try: engine.ensure({**record, 'operationId': uuid.uuid4().hex}, parent, source.name)
        except files.Rejected as error: assert str(error) == 'FILES_BACKUP_DEVICE_MISMATCH'
        else: raise AssertionError('mismatched physical device was accepted')
        assert source.read_bytes() == CONTENT
    finally: os.close(parent)
    print(json.dumps({'physicalHddBackupEngine': 'passed', 'binaryBytes': len(CONTENT), 'sourceDifferentDevice': True,
        'fullChecksumPrivateReplayAndUuidRejection': 'passed', 'actualApiWorkerAndInstalledHelper': 'not-covered'}))


def main():
    assert os.geteuid() == 0 and HDD_PARENT.is_dir() and not HDD_PARENT.is_symlink()
    fixture = Path(tempfile.mkdtemp(prefix='wdv-backup-engine-', dir='/var/lib'))
    target = Path(tempfile.mkdtemp(prefix='wdv-file-backup-', dir=HDD_PARENT))
    original = fixture.stat(); original_target = target.stat()
    operation = uuid.uuid4().hex; source = Path(__file__).resolve().parents[1] / 'host'
    hashes = {}
    try:
        (fixture / 'host').mkdir(mode=0o700)
        for name in ('whaledeck-files.py', 'whaledeck-file-backups.py'):
            shutil.copyfile(source / name, fixture / 'host' / name)
            hashes['host/' + name] = hashlib.sha256((fixture / 'host' / name).read_bytes()).digest()
        shutil.copyfile(__file__, fixture / 'probe.py'); hashes['probe.py'] = hashlib.sha256((fixture / 'probe.py').read_bytes()).digest()
        (fixture / 'source.bin').write_bytes(CONTENT); (fixture / 'operation').write_text(operation)
        block = f'{os.major(original_target.st_dev)}:{os.minor(original_target.st_dev)}'
        properties = (Path('/run/udev/data') / ('b' + block)).read_text().splitlines()
        identifiers = [value.removeprefix('E:ID_FS_UUID=') for value in properties if value.startswith('E:ID_FS_UUID=')]
        assert len(identifiers) == 1
        (fixture / 'policy.json').write_text(json.dumps({'schemaVersion': 1, 'storageId': 'database-platform.backup.hdd', 'mountPoint': '/data', 'filesystemUuid': identifiers[0],
            'targetRelative': str(target.relative_to('/data')), 'reserveBytes': 512 * 1024**2, 'reservePercent': 5, 'maxVersions': 10000}))
        unit = 'wdv-backup-engine-' + operation
        command = ['/usr/bin/systemd-run', '--quiet', '--wait', '--pipe', '--collect', '--service-type=exec', '--unit=' + unit,
            '--property=User=root', '--property=Group=root', '--property=UMask=0077', '--property=ProtectSystem=strict',
            '--property=ProtectHome=yes', '--property=PrivateTmp=yes', '--property=PrivateDevices=yes', '--property=NoNewPrivileges=yes',
            '--property=RestrictSUIDSGID=no', '--property=ProtectKernelTunables=yes', '--property=ProtectKernelModules=yes',
            '--property=ProtectControlGroups=yes', '--property=RestrictNamespaces=yes', '--property=RestrictAddressFamilies=AF_UNIX',
            '--property=RuntimeMaxSec=60', '--property=TimeoutStopSec=5', '--property=KillMode=control-group',
            '--property=TasksMax=8', '--property=MemoryMax=128M', '--property=CPUQuota=50%', '--property=LimitCORE=0',
            '--property=LimitNOFILE=64', '--property=LimitFSIZE=1073741824',
            '--property=CapabilityBoundingSet=CAP_DAC_OVERRIDE CAP_DAC_READ_SEARCH CAP_FOWNER CAP_CHOWN',
            '--property=ReadWritePaths=' + str(fixture) + ' ' + str(target), '/usr/bin/python3', '-I', str(fixture / 'probe.py'), '--inside', str(fixture)]
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        try: output, _ = process.communicate(timeout=70)
        except subprocess.TimeoutExpired:
            # An observation timeout is not termination: stop the exact owned
            # unit and reap its observer before inspecting/cleaning any files.
            subprocess.run(['/usr/bin/systemctl', 'stop', unit + '.service'], check=True, timeout=15)
            output, _ = process.communicate(timeout=10)
            raise RuntimeError('owned-backup-probe-timeout')
        assert process.returncode == 0, 'sandbox-probe-failed'
        result = json.loads(output); assert result['physicalHddBackupEngine'] == 'passed'; print(json.dumps(result))
    finally:
        assert fixture.resolve() == fixture and (fixture.stat().st_dev, fixture.stat().st_ino) == (original.st_dev, original.st_ino)
        assert target.resolve() == target and (target.stat().st_dev, target.stat().st_ino) == (original_target.st_dev, original_target.st_ino)
        for name in (operation + '.data', operation + '.partial'):
            data = target / name
            if data.exists():
                info = data.lstat(); assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and hashlib.sha256(data.read_bytes()).digest() == hashlib.sha256(CONTENT).digest()
                data.unlink()
        metadata = target / (operation + '.json')
        if metadata.exists(): assert metadata.is_file() and not metadata.is_symlink() and json.loads(metadata.read_bytes())['operationId'] == operation; metadata.unlink()
        assert not any(target.iterdir()); target.rmdir()
        for name, expected in hashes.items():
            file = fixture / name
            assert not file.is_symlink() and file.stat().st_nlink == 1 and hashlib.sha256(file.read_bytes()).digest() == expected; file.unlink()
        for name in ('source.bin', 'operation', 'policy.json'):
            file = fixture / name
            if file.exists(): assert not file.is_symlink() and file.stat().st_nlink == 1; file.unlink()
        assert not any((fixture / 'host').iterdir()); (fixture / 'host').rmdir()
        assert not any(fixture.iterdir()); fixture.rmdir()
        print(json.dumps({'ownedPhysicalBackupFixturesRemoved': True}))


if __name__ == '__main__':
    try:
        if len(sys.argv) == 3 and sys.argv[1] == '--inside': inside(Path(sys.argv[2]))
        elif len(sys.argv) == 1: main()
        else: raise RuntimeError('invalid-arguments')
    except Exception:
        print(json.dumps({'physicalHddBackupEngine': 'failed'})); raise SystemExit(1) from None
