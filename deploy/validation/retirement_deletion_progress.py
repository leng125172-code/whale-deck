"""Read-only proof of a partially removed, manifest-bound QA source tree.

No storage flock is acquired: the executing helper owns it. A live sample is
only a trigger. The complete, authoritative inventory is taken after the exact
cgroup is empty, before the browser may authorize a recovery.
"""
import contextlib
import hashlib
import json
import os
import stat
import time

DIR = os.O_RDONLY | os.O_DIRECTORY


def manifest_digest(driver, manifest):
    # Tree manifests use UTF-8, whereas storage metadata canonicalizes ASCII.
    def encode(value):
        return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')
    header = encode({k: v for k, v in manifest.items() if k not in ('entries', 'manifestSha256')})
    digest = hashlib.sha256(b'whaledeck-file-tree-manifest-v1\n' + len(header).to_bytes(8, 'big') + header)
    for entry in manifest['entries']:
        raw = encode(entry)
        digest.update(len(raw).to_bytes(8, 'big') + raw)
    return digest.hexdigest().upper()


def bind_prepared(driver, prepared, request):
    assert prepared['request'] == request
    assert prepared['bindingSha256'] == hashlib.sha256(driver.canonical(request)).hexdigest()
    plan, manifest = prepared['plan'], prepared['manifest']
    assert plan['retirementPlanSha256'] == request['retirementPlanSha256']
    assert hashlib.sha256(driver.canonical({k: v for k, v in plan.items() if k != 'retirementPlanSha256'})).hexdigest() == request['retirementPlanSha256']
    assert plan['sourceManifestSha256'] == manifest['manifestSha256'].lower()
    assert manifest['manifestSha256'] == manifest_digest(driver, manifest)
    entries = manifest['entries']
    assert 2 <= len(entries) == manifest['entryCount'] == plan['entryCount'] <= 10000
    assert entries[0]['path'] == '' and entries[0]['kind'] == 'Directory'
    paths = set()
    for row in entries:
        path = row['path']
        assert isinstance(path, str) and path not in paths
        assert path == '' or all(piece not in ('', '.', '..') for piece in path.split('/')) and not path.startswith('/')
        assert row['kind'] in ('Directory', 'File')
        paths.add(path)


def entry_stat(driver, payload, path):
    """Descriptor-relative no-symlink traversal; an absent ancestor is absent."""
    with contextlib.ExitStack() as opened:
        parent = payload
        try:
            for piece in path.split('/')[:-1]:
                parent = opened.enter_context(driver.opened(parent, piece, DIR))
            return os.stat(path.rsplit('/', 1)[-1], dir_fd=parent, follow_symlinks=False)
        except FileNotFoundError:
            return None


def inventory(driver, journal, payload, manifest):
    """Only call after execution has stopped; require every absence justified."""
    removed, remaining = [], []
    for index, row in enumerate(manifest['entries'][1:], 1):
        info = entry_stat(driver, payload, row['path'])
        if info is None:
            intent = dict(index=index, entry=row)
            driver.read_json(journal, f'remove-{index}.json', 32 * 1024**2, expected=intent)
            removed.append(dict(index=index, path=row['path'], kind=row['kind'],
                intentSha256=hashlib.sha256(driver.canonical(intent)).hexdigest()))
        else:
            assert driver.files.publication_identity(info)[:5] == row['sourceIdentity'][:5]
            if row['kind'] == 'File':
                assert stat.S_ISREG(info.st_mode) and driver.files.version(info) == row['sourceVersion']
            else:
                assert stat.S_ISDIR(info.st_mode)
            remaining.append(dict(index=index, path=row['path'], kind=row['kind']))
    removed_files = sum(row['kind'] == 'File' for row in removed)
    remaining_files = sum(row['kind'] == 'File' for row in remaining)
    assert removed_files > 0 and remaining_files > 0, 'QA_PARTIAL_FILE_DELETION_NOT_PROVEN'
    return dict(partialDeletion=True, entryCount=manifest['entryCount'], removedFiles=removed_files,
        remainingFiles=remaining_files, removed=removed, remaining=remaining,
        everyAbsenceHasExactIntent=True)


class InstalledDeletionProgress:
    def __init__(self):
        self.binding = None

    @contextlib.contextmanager
    def opened(self, tasks, value):
        driver = tasks.d
        root, workspace = driver.validate_request(value['prerequisite']['storage'])
        config = driver.configuration(root)
        # namespace attests the approved filesystem without taking its flock.
        with driver.namespace(root, config) as parent:
            with driver.opened(parent, '.retirement-' + workspace, DIR) as journal:
                driver.directory(journal, private=True)
                prepared = driver.read_json(journal, 'prepared.json', 32 * 1024**2)
                bind_prepared(driver, prepared, tasks.engine_request(value))
                with driver.opened(journal, 'source', DIR) as allocation:
                    try:
                        os.stat(workspace, dir_fd=parent, follow_symlinks=False)
                    except FileNotFoundError:
                        pass
                    else:
                        raise AssertionError('QA_SOURCE_NOT_DETACHED')
                    assert driver.files.publication_identity(os.fstat(allocation))[:5] == prepared['allocationIdentity'][:5]
                    with driver.opened(allocation, 'data', DIR) as payload:
                        assert driver.files.publication_identity(os.fstat(payload))[:5] == prepared['manifest']['entries'][0]['sourceIdentity'][:5]
                        binding = dict(preparedSha256=hashlib.sha256(driver.canonical(prepared)).hexdigest(),
                            journalIdentity=driver.files.publication_identity(os.fstat(journal))[:5],
                            payloadIdentity=driver.files.publication_identity(os.fstat(payload))[:5],
                            manifestSha256=prepared['manifest']['manifestSha256'])
                        yield journal, payload, prepared['manifest'], binding
                driver.recheck(parent, root, config)

    def wait(self, tasks, value):
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            try:
                with self.opened(tasks, value) as (journal, payload, manifest, binding):
                    # Reverse-order deletion starts at the final manifest row.
                    index = len(manifest['entries']) - 1
                    row = manifest['entries'][index]
                    if entry_stat(tasks.d, payload, row['path']) is None:
                        tasks.d.read_json(journal, f'remove-{index}.json', 32 * 1024**2,
                            expected=dict(index=index, entry=row))
                        self.binding = binding
                        return dict(**binding, observedRemovedIndex=index, observedRemovedPath=row['path'],
                            deletionObservedBeforeKill=True, liveSampleOnly=True)
            except FileNotFoundError:
                pass
            time.sleep(0.002)
        raise TimeoutError('QA_DELETION_PROGRESS_NOT_OBSERVED')

    def stopped(self, tasks, value):
        assert self.binding is not None
        with self.opened(tasks, value) as (journal, payload, manifest, binding):
            assert binding == self.binding, 'QA_DELETION_TREE_CHANGED'
            return dict(**binding, **inventory(tasks.d, journal, payload, manifest),
                executionAlreadySettled=True, noStorageLockOrWrites=True)
