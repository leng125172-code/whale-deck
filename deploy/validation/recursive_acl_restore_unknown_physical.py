"""One fixed leaf rename in a proved, new QA-owned tree; exact reread/cleanup.

Not a product endpoint. No arbitrary name, recursive removal or rename-back.
"""
import ctypes
import os
import re
import stat
from pathlib import Path
from acl_history_browser_proof import typed_equal
from recursive_acl_history_paging_proof import FILES, inventory as original_inventory
from recursive_acl_restore_agent_io import frame
from recursive_acl_restore_unknown_record_proof import renamed_leaf

APPS = Path('/data/WhaleDeck/apps')
OLD = 'item-01.bin'
NEW = 'renamed-item-01.bin'
RENAMED_FILES = tuple(NEW if name == OLD else name for name in FILES)


def _rename_no_replace(fd):
    libc = ctypes.CDLL(None, use_errno=True)
    libc.renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    libc.renameat2.restype = ctypes.c_int
    if libc.renameat2(fd, os.fsencode(OLD), fd, os.fsencode(NEW), 1) != 0:
        raise OSError(ctypes.get_errno(), 'OWNED_UNKNOWN_RENAME_REFUSED')


def owned_root(root):
    root = Path(root)
    assert root.name == 'recursive-tree' and root.parent.parent == APPS
    assert re.fullmatch('wdv-container-publish-[a-f0-9]{32}', root.parent.name)
    assert root.resolve(strict=True) == root and not root.is_symlink() and not root.parent.is_symlink()
    return root


def _rename_at(root, before):
    # Fixed names and pinned directory FD, even in explicitly isolated tests.
    assert set(before) == {*FILES, ''}
    assert {p.name for p in root.iterdir()} == set(FILES)
    fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        typed_equal(frame(os.fstat(fd)), before['']['identity'])
        info = os.stat(OLD, dir_fd=fd, follow_symlinks=False)
        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1
        typed_equal(frame(info), before[OLD]['identity'])
        assert not os.path.lexists(root / NEW)
        _rename_no_replace(fd)
        assert frame(os.fstat(fd))[:6] == before['']['identity'][:6]
        assert frame(root.lstat())[:6] == before['']['identity'][:6]
    finally:
        os.close(fd)


def inventory(root, reader, data, acl):
    assert not root.is_symlink() and stat.S_ISDIR(root.lstat().st_mode)
    assert {p.name for p in root.iterdir()} == set(RENAMED_FILES)
    out = {}
    for relative in (*RENAMED_FILES, ''):
        path = root / relative
        info = path.lstat()
        assert not path.is_symlink()
        if relative:
            assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and path.read_bytes() == data
        else:
            assert stat.S_ISDIR(info.st_mode)
        assert os.getxattr(path, 'user.whaledeck-recursive') == b'preserve ordinary attribute'
        assert set(os.listxattr(path)) <= {'user.whaledeck-recursive', *acl.XATTRS.values()}
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
        try:
            assert frame(os.fstat(fd)) == frame(info)
            out[relative] = dict(identity=list(reader.fingerprint(info)),
                access=acl.read_acl(fd, 'access', info.st_mode), default=acl.read_acl(fd, 'default', info.st_mode))
        finally:
            os.close(fd)
    return out


def rename_owned(root, reader, data, acl, before):
    root = owned_root(root)
    typed_equal(original_inventory(root, reader, data, acl), before)
    _rename_at(root, before)
    after = inventory(root, reader, data, acl)
    return after, renamed_leaf(before, after, OLD, NEW)


def remove_proved(root, reader, data, acl, expected):
    root = owned_root(root)
    typed_equal(inventory(root, reader, data, acl), expected)
    for name in RENAMED_FILES:
        path = root / name
        typed_equal(frame(path.lstat()), expected[name]['identity'])
        path.unlink()
    assert not list(root.iterdir()) and frame(root.lstat())[:5] == expected['']['identity'][:5]
    root.rmdir()
