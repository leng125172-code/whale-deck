#!/usr/bin/env python3
"""Physical HDD archive probe in owned directories; never edits production policy.

This tests production Python modules under a transient systemd sandbox, not the
installed Agent, BFF, Worker, SQL or WebUI. No synthetic disk attestation.
"""
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
CONTENT = bytes(range(256)) * 8192 + b'\x00owned-archive\xff'
CHANGED = b'owned-external-content\x00\xff'
MODULES = ('whaledeck-files.py', 'whaledeck-file-backups.py', 'whaledeck-file-archives.py')


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); sys.modules[name] = module; spec.loader.exec_module(module)
    return module


def inside(fixture):
    assert fixture.parent == Path('/var/lib') and fixture.name.startswith('wdv-archive-engine-') and not fixture.is_symlink()
    assert fixture.stat().st_uid == 0 and stat.S_IMODE(fixture.stat().st_mode) == 0o700
    files = load('archive_physical_files', fixture / 'host/whaledeck-files.py')
    backups = load('archive_physical_backups', fixture / 'host/whaledeck-file-backups.py')
    archives = load('archive_physical_archives', fixture / 'host/whaledeck-file-archives.py')
    files.BASE = str(fixture); files.REGISTRY = str(fixture / 'resources.json'); files.STATE = str(fixture / 'state')
    backups.CONFIGURATION = str(fixture / 'policy.json'); store = backups.FileBackups(files); engine = archives.FileArchives(files, store)
    ids = json.loads((fixture / 'ids.json').read_bytes()); source = fixture / 'files/source.bin'
    original = source.stat(); checksum = hashlib.sha256(CONTENT).hexdigest().upper()
    def request(kind, version='', absent=False, key=None):
        command = dict(kind=kind, rootId='validation.files', path='source.bin', versionId=version,
            expectedVersion='ABSENT' if absent or kind == 'purge' else files.version(source.stat()))
        return dict(operationId=ids[key or kind], owner='F' * 64, plan=engine.plan(command))
    trash = request('trash'); result = engine.apply(trash)
    assert result['state'] == 'Completed' and result['hddBackup']['differentSourceDevice'] and not source.exists()
    assert result['hddBackup']['sha256'] == checksum and result['hddBackup']['sizeBytes'] == len(CONTENT)
    assert (source.parent / '.whaledeck-files' / (ids['trash'] + '.archive')).read_bytes() == CONTENT
    with store.target() as (_, target):
        store.verify_data(target, ids['trash'], engine.metadata(target, ids['trash']))
    restored = engine.apply(request('restore', ids['trash'], absent=True))
    assert restored['state'] == 'Completed' and source.read_bytes() == CONTENT
    info = source.stat()
    assert (info.st_uid, info.st_gid, stat.S_IMODE(info.st_mode), info.st_mtime_ns) == (original.st_uid, original.st_gid, stat.S_IMODE(original.st_mode), original.st_mtime_ns)
    source.write_bytes(CHANGED); overwritten = engine.apply(request('restore', ids['trash'], key='overwrite'))
    assert overwritten['state'] == 'Completed' and overwritten['hddBackup']['sha256'] == hashlib.sha256(CHANGED).hexdigest().upper()
    assert source.read_bytes() == CONTENT
    with store.target() as (_, target): store.verify_data(target, ids['overwrite'], engine.metadata(target, ids['overwrite']))
    history = engine.list(dict(rootId='validation.files', path='source.bin', after='', take=50, expectedVersion=''))
    assert {row['versionId'] for row in history['entries']} == {ids['trash'], ids['overwrite']}
    assert all(row['differentSourceDevice'] and row['availability'] == 'Present' for row in history['entries'])
    purge = request('purge', ids['trash']); purged = engine.apply(purge)
    assert purged['state'] == 'Completed' and engine.apply(purge)['state'] == 'Completed'
    with store.target() as (_, target):
        assert ids['trash'] + '.data' not in os.listdir(target) and ids['trash'] + '.json' not in os.listdir(target)
        store.verify_data(target, ids['overwrite'], engine.metadata(target, ids['overwrite']))
    # Deliberately mismatch only the disposable policy, not a live mount.
    policy = json.loads((fixture / 'policy.json').read_bytes()); actual = policy['filesystemUuid']
    policy['filesystemUuid'] = '00000000-0000-0000-0000-000000000001' if actual != '00000000-0000-0000-0000-000000000001' else '00000000-0000-0000-0000-000000000002'
    (fixture / 'policy.json').write_text(json.dumps(policy))
    try: engine.plan(dict(kind='trash', rootId='validation.files', path='source.bin', versionId='', expectedVersion=files.version(source.stat())))
    except files.Rejected as error: assert str(error) == 'FILES_BACKUP_DEVICE_MISMATCH'
    else: raise AssertionError('wrong physical HDD identity accepted')
    assert source.read_bytes() == CONTENT
    print(json.dumps({'physicalHddArchiveEngine': 'passed', 'binaryBytes': len(CONTENT), 'differentSourceDevice': True,
        'trashRestoreOverwriteHistoryPurgeReplayWrongUuid': 'passed', 'installedAgentApiWorkerUi': 'not-covered'}))


def main():
    os.umask(0o077)
    assert os.geteuid() == 0 and HDD_PARENT.is_dir() and not HDD_PARENT.is_symlink()
    fixture = Path(tempfile.mkdtemp(prefix='wdv-archive-engine-', dir='/var/lib'))
    target = Path(tempfile.mkdtemp(prefix='wdv-archive-data-', dir=HDD_PARENT))
    original = fixture.stat(); target_original = target.stat(); ids = {key: uuid.uuid4().hex for key in ('trash', 'restore', 'overwrite', 'purge')}
    hashes = {}; unit = 'wdv-archive-engine-' + ids['trash']; observer = None
    try:
        for name in ('host', 'files', 'state'): (fixture / name).mkdir(mode=0o700)
        for name in MODULES:
            shutil.copyfile(Path(__file__).resolve().parents[1] / 'host' / name, fixture / 'host' / name)
            hashes['host/' + name] = hashlib.sha256((fixture / 'host' / name).read_bytes()).hexdigest()
        shutil.copyfile(__file__, fixture / 'probe.py'); hashes['probe.py'] = hashlib.sha256((fixture / 'probe.py').read_bytes()).hexdigest()
        (fixture / 'ids.json').write_text(json.dumps(ids)); (fixture / 'files/source.bin').write_bytes(CONTENT)
        os.chown(fixture / 'files/source.bin', 1000, 1000); (fixture / 'files/source.bin').chmod(0o640)
        (fixture / 'resources.json').write_text(json.dumps([dict(id='validation.files', type='FileRoot', externalId='Owned physical archive probe', protectionLevel='Managed',
            path=str(fixture / 'files'), ownerUid=1000, ownerGid=1000,
            allowedActions=['files-list', 'files-stat', 'files-download', 'files-upload', 'files-mutate', 'files-history', 'files-archive'])]))
        block = f'{os.major(target_original.st_dev)}:{os.minor(target_original.st_dev)}'
        properties = (Path('/run/udev/data') / ('b' + block)).read_text().splitlines()
        identifiers = [line.removeprefix('E:ID_FS_UUID=') for line in properties if line.startswith('E:ID_FS_UUID=')]; assert len(identifiers) == 1
        (fixture / 'policy.json').write_text(json.dumps(dict(schemaVersion=1, storageId='database-platform.backup.hdd', mountPoint='/data', filesystemUuid=identifiers[0],
            targetRelative=str(target.relative_to('/data')), reserveBytes=512 * 1024**2, reservePercent=5, maxVersions=10000)))
        command = ['/usr/bin/systemd-run', '--quiet', '--wait', '--pipe', '--collect', '--service-type=exec', '--unit=' + unit,
            '--property=User=root', '--property=Group=root', '--property=UMask=0077', '--property=ProtectSystem=strict', '--property=ProtectHome=yes',
            '--property=PrivateTmp=yes', '--property=PrivateDevices=yes', '--property=NoNewPrivileges=yes', '--property=RestrictSUIDSGID=no',
            '--property=ProtectKernelTunables=yes', '--property=ProtectKernelModules=yes', '--property=ProtectControlGroups=yes', '--property=RestrictNamespaces=yes',
            '--property=RestrictAddressFamilies=AF_UNIX', '--property=RuntimeMaxSec=60', '--property=TimeoutStopSec=5', '--property=KillMode=control-group',
            '--property=TasksMax=8', '--property=MemoryMax=128M', '--property=CPUQuota=50%', '--property=LimitCORE=0', '--property=LimitNOFILE=64',
            '--property=LimitFSIZE=1073741824', '--property=CapabilityBoundingSet=CAP_DAC_OVERRIDE CAP_DAC_READ_SEARCH CAP_FOWNER CAP_CHOWN',
            '--property=ReadWritePaths=' + str(fixture) + ' ' + str(target), '/usr/bin/python3', '-I', str(fixture / 'probe.py'), '--inside', str(fixture)]
        observer = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        try: output, _ = observer.communicate(timeout=70)
        except subprocess.TimeoutExpired:
            subprocess.run(['/usr/bin/systemctl', 'stop', unit + '.service'], check=True, timeout=15)
            observer.communicate(timeout=10); raise RuntimeError('owned-archive-probe-timeout') from None
        assert observer.returncode == 0, 'physical-archive-probe-failed'
        result = json.loads(output); assert result['physicalHddArchiveEngine'] == 'passed'
        print(json.dumps({**result, 'moduleSha256': {key: value for key, value in hashes.items() if key.startswith('host/')}}))
    finally:
        if observer is not None and observer.poll() is None:
            subprocess.run(['/usr/bin/systemctl', 'stop', unit + '.service'], check=True, timeout=15); observer.communicate(timeout=10)
        assert fixture.resolve() == fixture and target.resolve() == target
        assert (fixture.stat().st_dev, fixture.stat().st_ino) == (original.st_dev, original.st_ino)
        assert (target.stat().st_dev, target.stat().st_ino) == (target_original.st_dev, target_original.st_ino)
        # Explicit allowlist, identities and payload hashes before any removal.
        allowed = set(hashes) | {'ids.json', 'resources.json', 'policy.json', 'files/source.bin', 'state/namespace.lock'}
        for identifier in ids.values():
            allowed |= {'files/.whaledeck-files/' + identifier + '.archive', 'state/archives/' + identifier + '.json', 'state/archives/' + identifier + '.lock'}
        for root, device, names in ((fixture, original.st_dev, allowed), (target, target_original.st_dev, {identifier + suffix for identifier in ids.values() for suffix in ('.data', '.json', '.partial', '.purging')})):
            entries = list(root.rglob('*')); assert len(entries) < 100
            for file in entries:
                info = file.lstat(); assert not file.is_symlink() and info.st_dev == device
                if stat.S_ISDIR(info.st_mode): assert info.st_uid == 0 and stat.S_IMODE(info.st_mode) == 0o700; continue
                relative = str(file.relative_to(root)); assert relative in names and stat.S_ISREG(info.st_mode) and info.st_nlink == 1
                assert info.st_uid in (0, 1000)
                if relative in hashes: assert hashlib.sha256(file.read_bytes()).hexdigest() == hashes[relative]
                if file.suffix in ('.data', '.partial', '.purging', '.archive') or relative == 'files/source.bin': assert file.read_bytes() in (CONTENT, CHANGED)
            for file in sorted(entries, key=lambda value: len(value.parts), reverse=True):
                file.rmdir() if file.is_dir() else file.unlink()
            root.rmdir()
        print(json.dumps({'ownedPhysicalArchiveFixturesRemoved': True}))


if __name__ == '__main__':
    try:
        if len(sys.argv) == 3 and sys.argv[1] == '--inside': inside(Path(sys.argv[2]))
        elif len(sys.argv) == 1: main()
        else: raise RuntimeError('invalid-arguments')
    except Exception:
        print(json.dumps({'physicalHddArchiveEngine': 'failed'})); raise SystemExit(1) from None
