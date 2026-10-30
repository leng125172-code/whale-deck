"""QA-only exact pidfd cut after publication and partial original retention.

No arbitrary PID, product hook, SQL write or candidate engine. Bind only the
installed move apply helper holding the original container root and lock FDs.
"""
import os
from pathlib import Path
import re
import select
import signal
import stat
import time

from bound_container_move_cut import CGROUP, ROOT, HeldMove, bound_fds, identity, receipt_json, record_for


def children(path):
    result = {}
    for entry in path.iterdir():
        info = entry.lstat()
        assert not entry.is_symlink() and (stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode) and info.st_nlink == 1)
        result[entry.name] = 'Directory' if stat.S_ISDIR(info.st_mode) else 'File'
    return result


def retention_proof(record, folder):
    assert record['plan']['kind'] == 'Directory' and record['plan']['operationKind'] == 'Move'
    if record['state'] != 'Running': return None
    plan = record['plan']; command = plan['command']; operation = record['operationId']
    assert list(identity(folder)) == plan['sourceParent'] == plan['targetParent']
    names = [command[k].removeprefix('work/') for k in ('sourcePath', 'targetPath')]
    assert all(name and '/' not in name for name in names)
    assert all(command[k] == 'work/' + name for k, name in zip(('sourcePath', 'targetPath'), names))
    inner = receipt_json(ROOT / 'engines/tree-moves' / (operation + '.json'))
    if inner is None: return None
    assert inner['operationId'] == operation and inner['owner'] == record['owner'] and inner['plan'] == plan['innerPlan']
    if inner['state'] != 'SourceRetaining' or inner.get('destinationPublished') is not True or inner.get('sourceMutationStarted') is not True or 'retainedIdentity' not in inner: return None
    copied = receipt_json(ROOT / 'engines/tree-move-copies' / (operation + '.json'))
    assert copied is not None and copied['state'] == 'Completed' and copied['plan'] == plan['innerPlan']['copyPlan']
    assert copied['operationId'] == operation and copied['owner'] == record['owner']
    source, target = (folder / name for name in names)
    old = source.lstat(); target_info = target.lstat()
    assert stat.S_ISDIR(old.st_mode) and not source.is_symlink() and [old.st_dev, old.st_ino] == plan['sourceIdentity'][:2]
    assert stat.S_ISDIR(target_info.st_mode) and not target.is_symlink() and list(identity(target)) == copied['newIdentity']
    private = folder / '.whaledeck-container-moves-v1'; info = private.lstat()
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    retained = private / (operation + '.tree-move')
    assert {p.name for p in private.iterdir()} == {retained.name}
    info = retained.lstat()
    assert stat.S_ISDIR(info.st_mode) and info.st_uid == info.st_gid == 0 and stat.S_IMODE(info.st_mode) == 0o700
    assert list(identity(retained)) == inner['retainedIdentity']
    origin, kept, published = children(source), children(retained), children(target)
    original_files = {name for name, kind in origin.items() if kind == 'File'}
    retained_files = {name for name, kind in kept.items() if kind == 'File'}
    target_files = {name for name, kind in published.items() if kind == 'File'}
    assert not original_files & retained_files and original_files | retained_files == target_files
    assert set(origin) - original_files == set(kept) - retained_files == set(published) - target_files == {'empty'}
    assert len(published) + 1 == plan['innerPlan']['copyPlan']['entryCount']
    if not retained_files or not original_files: return None
    return {'inner': inner, 'copied': copied, 'sourceIdentity': list(identity(source)), 'targetIdentity': list(identity(target)),
        'privateIdentity': list(identity(private)), 'retainedIdentity': list(identity(retained)),
        'remainingFiles': sorted(original_files), 'retainedFiles': sorted(retained_files), 'publishedFiles': sorted(target_files)}


class HeldTreeMove(HeldMove):
    def kill(self):
        assert self.descriptor is not None
        signal.pidfd_send_signal(self.descriptor, signal.SIGKILL)
        assert select.select([self.descriptor], [], [], 8)[0], 'BOUND_MOVE_DRIVER_DID_NOT_EXIT'
        os.close(self.descriptor); self.descriptor = None


def freeze_retention(row, folder, container_pid):
    assert os.geteuid() == 0 and re.fullmatch('[a-f0-9]{64}', row['container_id'])
    folder = Path(folder)
    assert folder.parent == Path('/data/WhaleDeck/apps') and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', folder.name)
    assert folder.resolve(strict=True) == folder and not folder.is_symlink()
    assert container_pid > 1 and row['instance_id'].startswith(str(container_pid) + ':')
    root_identity = identity(Path(f'/proc/{container_pid}/root'))
    operation = row['execution_job_id'].replace('-', ''); assert re.fullmatch('[a-f0-9]{32}', operation)
    lock = ROOT / (operation + '.lock'); descriptor = None; pid = group = None
    deadline = time.monotonic() + 40
    try:
        while time.monotonic() < deadline:
            if descriptor is None and lock.exists():
                lock_identity = identity(lock)
                for directory in CGROUP.glob('whaledeck-container-moves-*.service'):
                    if not re.fullmatch(r'whaledeck-container-moves-[a-f0-9]{32}\.service', directory.name): continue
                    try: candidates = (directory / 'cgroup.procs').read_text().split()
                    except FileNotFoundError: continue
                    for candidate in candidates:
                        if candidate.isdecimal() and bound_fds(int(candidate), root_identity, lock_identity):
                            pid = int(candidate)
                            try: descriptor = os.pidfd_open(pid)
                            except ProcessLookupError: continue
                            group = directory.name; break
                    if descriptor is not None: break
            try: record = record_for(row)
            except FileNotFoundError: record = None
            if record is not None and record['state'] in ('Completed', 'Aborted', 'Unknown'): return None
            # Detect only a stable journal phase before signalling. Detailed
            # filesystem proof is read after the exact process is stopped.
            journal = receipt_json(ROOT / 'engines/tree-moves' / (operation + '.json'))
            if descriptor is not None and record is not None and journal is not None and journal['state'] == 'SourceRetaining':
                assert journal['operationId'] == operation and journal['owner'] == record['owner'] and journal['plan'] == record['plan']['innerPlan']
                if not bound_fds(pid, root_identity, identity(lock)): return None
                signal.pidfd_send_signal(descriptor, signal.SIGSTOP)
                until = time.monotonic() + 2
                while True:
                    state = Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0]
                    if state in ('T', 't'): break
                    if state == 'Z': return None
                    assert time.monotonic() < until, 'MOVE_RETENTION_STOP_NOT_OBSERVED'
                    time.sleep(0.0005)
                frozen = record_for(row); proof = retention_proof(frozen, folder)
                if proof is not None:
                    held = HeldTreeMove(pid, descriptor, group, frozen, proof); descriptor = None; return held
                signal.pidfd_send_signal(descriptor, signal.SIGCONT)
                time.sleep(0.002); continue
            time.sleep(0.0005)
        return None
    finally:
        if descriptor is not None:
            try: signal.pidfd_send_signal(descriptor, signal.SIGCONT)
            except ProcessLookupError: pass
            finally: os.close(descriptor)
