#!/usr/bin/env python3
"""Delete one exact browser-owned fixture, never a production file or receipt.

The parent sends name/operation/SHA metadata over stdin, not credentials.
Root receipt, original inode/device, regular single link and full SHA must agree.
"""
import hashlib
import json
import os
import re
import stat
import sys
import uuid


def main():
    assert os.geteuid() == 0
    request = json.loads(sys.stdin.buffer.read(4097))
    assert set(request) == {'name', 'operationId', 'sha256'}
    name = request['name']; checksum = request['sha256']
    assert re.fullmatch(r'wdv-web-file-[a-f0-9]{16}\.txt', name)
    assert re.fullmatch(r'[A-F0-9]{64}', checksum)
    operation = uuid.UUID(request['operationId']).hex
    root = os.open('/data/WhaleDeck/files', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    receipts = os.open('/var/lib/whaledeck-files', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        with os.fdopen(os.open(operation + '.json', os.O_RDONLY | os.O_NOFOLLOW, dir_fd=receipts), 'rb') as source:
            receipt = json.loads(source.read(65537))
        assert receipt['state'] == 'Completed' and receipt['rootId'] == 'whaledeck.files.shared'
        assert receipt['path'] == name and receipt['sha256'] == checksum
        try: descriptor = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=root)
        except FileNotFoundError:
            print(json.dumps({'ownedBrowserFileRemoved': True, 'alreadyAbsent': True})); return
        with os.fdopen(descriptor, 'rb') as source:
            original = os.fstat(source.fileno())
            assert stat.S_ISREG(original.st_mode) and original.st_nlink == 1
            assert original.st_ino == receipt['newInode'] and original.st_dev == receipt['newDevice']
            assert original.st_size == receipt['sizeBytes'] <= 1024 * 1024
            assert hashlib.file_digest(source, 'sha256').hexdigest().upper() == checksum
            current = os.stat(name, dir_fd=root, follow_symlinks=False)
            assert (current.st_dev, current.st_ino, current.st_mtime_ns, current.st_ctime_ns) == (original.st_dev, original.st_ino, original.st_mtime_ns, original.st_ctime_ns)
            os.unlink(name, dir_fd=root); os.fsync(root)
        print(json.dumps({'ownedBrowserFileRemoved': True, 'receiptAuditRetained': True}))
    finally:
        os.close(receipts); os.close(root)


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print(json.dumps({'ownedBrowserFileCleanup': 'failed', 'failureType': type(error).__name__}))
        raise SystemExit(1) from None
