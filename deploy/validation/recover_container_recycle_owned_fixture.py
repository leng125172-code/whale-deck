#!/usr/bin/env python3
"""QA-only takeback of one frozen, expired fixture. Never a product restore.

The original native journal/HDD stay byte-exact Trashed. No RPC, restart,
instance rebinding, native record edits, or recursive deletion is performed.
"""
import ctypes
import errno
import fcntl
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import uuid
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from container_recycle_agent_io import STATE, PRIVATE, physical, file_snapshot, read_operation
from recursive_acl_restore_agent_io import frame
from validate_container_recycle_restore_agent import baseline
from validate_container_directory_restart import preflight
from validate_container_file_publishing import common, verify_container

FAILED = 'container-recycle-restore-agent-3ce57573e7da48cd93db3883b0f93014'
FAILED_MANIFEST = '5bf7957d2f244e56c2453ac091b6a9310ea7bb0a0c7f30561491da851f946849'
SOURCES = ('recover_container_recycle_owned_fixture.py', 'verify_container_recycle_owned_fixture.py', 'test_container_recycle_owned_recovery.py')


def moved_proof(before, after):
    assert before is not None and after is not None
    typed_equal(before['identity'][:8], after['identity'][:8]); assert after['identity'][8] >= before['identity'][8]
    for key in ('sha256', 'attributes', 'sizeBytes'): typed_equal(before[key], after[key])


def rename_noreplace(source, name, target, leaf):
    libc = ctypes.CDLL(None, use_errno=True)
    call = libc.renameat2; call.argtypes = (ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint); call.restype = ctypes.c_int
    if call(source, os.fsencode(name), target, os.fsencode(leaf), 1) != 0:
        code = ctypes.get_errno(); raise OSError(code, errno.errorcode.get(code, 'RENAME_FAILED'))


def return_inode(folder, operation, expected):
    assert len(operation) == 32 and all(c in '0123456789abcdef' for c in operation) and int(operation, 16) != 0
    typed_equal(physical(folder, operation), expected); assert expected['source'] is None and expected['retained'] is not None
    parent = os.open(folder, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        assert os.path.samestat(os.fstat(parent), folder.lstat())
        private = os.open(PRIVATE, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
        try:
            i = os.fstat(private); typed_equal([i.st_dev, i.st_ino], expected['privateIdentity'])
            assert (i.st_uid, i.st_gid, stat.S_IMODE(i.st_mode)) == (0, 0, 0o700) and os.listdir(private) == [operation+'.trash']
            fd = os.open(operation+'.trash', os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=private)
            try:
                typed_equal(frame(os.fstat(fd)), expected['retained']['identity'])
                rename_noreplace(private, operation+'.trash', parent, 'recycle-rpc.bin'); os.fsync(private); os.fsync(parent)
            finally: os.close(fd)
        finally: os.close(private)
    finally: os.close(parent)
    after = physical(folder, operation); assert after['retained'] is None; typed_equal(after['privateIdentity'], expected['privateIdentity']); moved_proof(expected['retained'], after['source'])
    return after


def failed_inputs(repo):
    old = repo/'deploy/artifacts'/FAILED; manifest = old/'EVIDENCE_SHA256.json'
    assert hashlib.sha256(manifest.read_bytes()).hexdigest() == FAILED_MANIFEST
    for name, digest in json.loads(manifest.read_bytes()).items(): assert hashlib.sha256((old/name).read_bytes()).hexdigest() == digest
    assert (old/'failure.json').exists() and not (old/'verified.json').exists()
    fixture = json.loads((old/'fixture.json').read_bytes()); steps = json.loads((old/'steps.json').read_bytes())
    assert len(steps) == 2 and [s['action'] for s in steps] == ['source-apply', 'source-inspect'] and all(s['receipt']['state'] == 'Trashed' for s in steps)
    folder = Path(fixture['folder']); assert folder.parent == common.APPS and folder.name == fixture['name'] and folder.name.startswith('wdv-container-publish-')
    uuid.UUID(hex=folder.name.removeprefix('wdv-container-publish-')); assert folder.resolve(strict=True) == folder
    i = folder.lstat(); assert stat.S_ISDIR(i.st_mode) and (i.st_uid, i.st_gid, stat.S_IMODE(i.st_mode)) == (0, 0, 0o755)
    return old, fixture, steps, folder


def stopped(fixture, folder):
    c = json.loads(common.command(['docker', 'inspect', fixture['header']['containerId']]))[0]
    verify_container(c, fixture['header']['containerId'], fixture['name'], folder); s = c['State']
    assert s['Status'] == 'exited' and s['Running'] is False and s['Pid'] == 0 and s['ExitCode'] == 0 and not s['OOMKilled'] and not s['Paused'] and not s['Restarting']
    assert s['StartedAt'] == fixture['header']['instanceId'].split(':', 1)[1]
    return dict(containerId=c['Id'], state='exited', pid=0, exitCode=0, startedAt=s['StartedAt'], finishedAt=s['FinishedAt'])


def main(revision):
    assert os.geteuid() == 0 and len(revision) == 40; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    repo = Path(__file__).resolve().parents[2]; assert not subprocess.check_output(['git', 'status', '--porcelain']).strip()
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(); closure = source_closure(repo, sha, seeds=SOURCES)
    old, fixture, steps, folder = failed_inputs(repo); last = steps[-1]; operation = last['request']['operationId']; preflight()
    assert set(p.name for p in folder.iterdir()) == {'original.bin', PRIVATE}; current = physical(folder, operation); typed_equal(current, last['physical'])
    original = file_snapshot(folder/'original.bin'); assert original is not None
    expected_bytes = hashlib.shake_256(('owned-container-original:'+folder.name).encode()).digest(common.SIZE)
    assert original['sha256'] == hashlib.sha256(expected_bytes).hexdigest().upper() and original['sizeBytes'] == len(expected_bytes) and original['identity'][3:6] == [1000, 1000, 1]
    assert stat.S_IMODE(original['identity'][2]) == 0o640 and original['attributes'] == []
    e = repo/'deploy/artifacts'/('container-recycle-owned-recovery-'+uuid.uuid4().hex); e.mkdir(mode=0o700)
    def save(name, value):
        with (e/name).open('x') as stream:
            stream.write(json.dumps(value, ensure_ascii=False, indent=2)); stream.flush(); os.fsync(stream.fileno())
        fd = os.open(e, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try: os.fsync(fd)
        finally: os.close(fd)
    save('source.json', dict(revision=revision, validationRevision=sha, sourceSha256=closure, failedEvidence=FAILED, failedManifest=FAILED_MANIFEST))
    save('fixture.json', dict(fixture=fixture, sentinel=original, operationId=operation, beforePhysical=current, folderIdentity=frame(folder.lstat())))
    save('before.json', baseline(revision)); save('container-stopped.json', stopped(fixture, folder))
    print(json.dumps(dict(ownedRecovery='started', evidence=e.name)), flush=True)
    lock = os.open(STATE/(operation+'.lock'), os.O_RDWR | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        i = os.fstat(lock); assert stat.S_ISREG(i.st_mode) and (i.st_uid, i.st_gid, stat.S_IMODE(i.st_mode), i.st_nlink, i.st_size) == (0, 0, 0o600, 1, 0)
        assert os.path.samestat(i, (STATE/(operation+'.lock')).lstat()); fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        assert not os.path.lexists(Path('/var/lib/whaledeck-container-recycle-restores/claims')/(operation+'.json'))
        native = read_operation(last['request'], last['receipt'], e/'native-originals', create=True); typed_equal(native, last['records'])
        save('prepared.json', dict(manualQaTakeback=True, productRestore=False, sourceOperationId=operation, nativeState='Trashed', noNativeRecordMutation=True))
        stopped(fixture, folder); returned = return_inode(folder, operation, current); save('returned.json', returned)
        with (e/'independent-returned.log').open('x') as log:
            subprocess.run([sys.executable, str(repo/'deploy/validation/verify_container_recycle_owned_fixture.py'), e.name, '--returned'], stdout=log, stderr=subprocess.STDOUT, check=True, timeout=90)
        # Remove only this exact stopped container. A concurrent start makes
        # non-force docker rm fail, leaving all verified data for investigation.
        stopped(fixture, folder); subprocess.run(['docker', 'rm', fixture['header']['containerId']], capture_output=True, check=True, timeout=20)
        typed_equal(file_snapshot(folder/'recycle-rpc.bin'), returned['source']); typed_equal(file_snapshot(folder/'original.bin'), original)
        assert set(p.name for p in folder.iterdir()) == {'original.bin', 'recycle-rpc.bin', PRIVATE}
        (folder/'recycle-rpc.bin').unlink(); (folder/'original.bin').unlink()
        private = folder/PRIVATE; i = private.lstat(); assert [i.st_dev, i.st_ino] == current['privateIdentity'] and not list(private.iterdir()); private.rmdir(); folder.rmdir()
        typed_equal(read_operation(last['request'], last['receipt'], e/'native-originals'), native)
        save('after.json', baseline(revision)); typed_equal(json.loads((e/'before.json').read_bytes()), json.loads((e/'after.json').read_bytes()))
        save('verified.json', dict(manualQaTakeback=True, productRestore=False, nativeJournalAndHddByteExact=True, fixtureAbsent=True, containerAbsent=True, allOtherBaselineFieldsPreserved=True))
        with (e/'independent-final.log').open('x') as log:
            subprocess.run([sys.executable, str(repo/'deploy/validation/verify_container_recycle_owned_fixture.py'), e.name], stdout=log, stderr=subprocess.STDOUT, check=True, timeout=90)
        print(json.dumps(dict(ownedRecovery='passed', evidence=e.name, productRestore=False)), flush=True)
    except BaseException as error:
        import traceback
        save('failure.json', dict(type=type(error).__name__, frames=[dict(file=Path(f.filename).name, line=f.lineno) for f in traceback.extract_tb(error.__traceback__)])); raise
    finally: os.close(lock)


if __name__ == '__main__':
    try: assert len(sys.argv) == 2; main(sys.argv[1])
    except BaseException as error: print(json.dumps(dict(ownedRecovery='failed', type=type(error).__name__)), flush=True); raise SystemExit(1) from None
