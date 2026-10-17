"""Explicit synthetic device metadata for ordinary transport regression tests.

Only the mount identity inputs are simulated. File copy/rename/fsync, root
ownership, limits and source preservation run on the actual Linux filesystem.
"""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
from unittest.mock import patch


class FileBackupFixture:
    def __init__(self, driver):
        spec = importlib.util.spec_from_file_location('file_backups_transport_fixture', Path(__file__).resolve().parents[1] / 'host/whaledeck-file-backups.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        self.temporary = tempfile.TemporaryDirectory(prefix='wd-transport-backups-')
        disk = Path(self.temporary.name); self.target = disk / 'versions'; self.target.mkdir(mode=0o700)
        device = f'{os.major(self.target.stat().st_dev)}:{os.minor(self.target.stat().st_dev)}'
        uid = '00000000-0000-0000-0000-000000000001'
        policy = disk / 'policy.json'
        policy.write_text(json.dumps({'schemaVersion': 1, 'storageId': module.STORAGE, 'mountPoint': str(disk), 'filesystemUuid': uid,
            'targetRelative': 'versions', 'reserveBytes': 512 * 1024**2, 'reservePercent': 5, 'maxVersions': 10000}))
        udev = disk / 'udev'; udev.mkdir(); (udev / ('b' + device)).write_text('E:ID_FS_UUID=' + uid + '\nE:ID_FS_USAGE=filesystem\n')
        sysfs = disk / 'sys'; queue = sysfs / device / 'queue'; queue.mkdir(parents=True); (queue / 'rotational').write_bytes(b'1\n')
        mountinfo = disk / 'mountinfo'; mountinfo.write_text(f'1 0 {device} / {disk} rw - ext4 /dev/fixture-only rw\n')
        self.engine = module.FileBackups(driver)
        self.patches = [patch.object(module, 'CONFIGURATION', str(policy)), patch.object(module, 'UDEV', str(udev)),
            patch.object(module, 'SYS_DEVICES', str(sysfs)), patch.object(module, 'MOUNTINFO', str(mountinfo)),
            patch.object(driver, 'backup_engine', return_value=self.engine)]
        for item in self.patches: item.start()

    def close(self):
        for item in reversed(self.patches): item.stop()
        self.temporary.cleanup()
