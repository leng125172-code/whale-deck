"""Read-only acquisition of separate restore journal/claim and original recycle HDD.

Installed paths are fixed. Test fixtures may patch module constants, never CLI
paths. Namespace proof is separate from receipt/record projection.
"""
import os
from pathlib import Path
import stat
from acl_history_browser_proof import typed_equal
from recursive_acl_restore_agent_io import read_at, archive_raw, installed
import container_recycle_agent_io as source_io
from container_recycle_restore_rpc_proof import request_proof, record_proof

STATE = Path('/var/lib/whaledeck-container-recycle-restores')


def private_directory(path):
    assert path.is_absolute() and path.resolve(strict=True) == path
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
    try:
        i = os.fstat(fd)
        assert stat.S_ISDIR(i.st_mode) and (i.st_uid, i.st_gid, stat.S_IMODE(i.st_mode)) == (0, 0, 0o700)
        assert os.path.samestat(i, path.lstat())
        return fd
    except BaseException: os.close(fd); raise


def read_operation(request, receipt, folder, *, create=False):
    request_proof(request); source = request['plan']['command']['source']; op = request['operationId']; source_op = source['operationId']
    if create: folder.mkdir(mode=0o700)
    evidence = private_directory(folder); os.close(evidence)
    directory = private_directory(STATE)
    try:
        raw, root, _ = read_at(directory, op+'.json'); assert len(raw) <= 65536
        archive_raw(folder, 'restore-root.json', raw, create)
        claims = private_directory(STATE/'claims')
        try:
            assert os.path.samestat(os.fstat(directory), STATE.lstat())
            try: claim_raw, claim, _ = read_at(claims, source_op+'.json')
            except FileNotFoundError: claim_raw, claim = b'null', None
            assert len(claim_raw) <= 65536
            archive_raw(folder, 'source-claim.json', claim_raw, create)
        finally: os.close(claims)
    finally: os.close(directory)
    directory = private_directory(source_io.STATE)
    try:
        raw, source_root, _ = read_at(directory, source_op+'.json'); assert len(raw) <= 65536
        archive_raw(folder, 'source-root.json', raw, create)
    finally: os.close(directory)
    io = installed()
    with source_io.namespace(io.storage) as fd:
        raw, history, _ = read_at(fd, source_op+'.json'); archive_raw(folder, 'source-hdd.json', raw, create)
        data = source_io.file_snapshot(Path(os.readlink('/proc/self/fd/'+str(fd)))/(source_op+'.data'))
        assert data is not None and data['identity'][3:6] == [0, 0, 1] and stat.S_IMODE(data['identity'][2]) == 0o600
    record_proof(request, receipt, root, claim, source_root, history, data_sha256=data['sha256'], data_size=data['sizeBytes'])
    return dict(root=root, claim=claim, sourceRoot=source_root, history=history, data=data)


def physical_proof(value, original, records, receipt):
    """Only conclusive original Trash/Restored namespaces; Unknown needs its own gate."""
    root = records['sourceRoot']; state = root['state']
    assert state in ('Trashed', 'Restored')
    if receipt['state'] == 'Completed': assert state == 'Restored'
    assert receipt['state'] in ('Completed', 'Aborted')
    present = value['retained'] if state == 'Trashed' else value['source']
    assert present is not None and (value['source'] if state == 'Trashed' else value['retained']) is None
    typed_equal(present['identity'], root['actualIdentity']); typed_equal(present['identity'][:8], original['identity'][:8])
    assert present['identity'][8] >= original['identity'][8]
    for key in ('attributes', 'sha256', 'sizeBytes'): typed_equal(present[key], original[key])
    typed_equal(value['privateIdentity'], root['privateIdentity'])
    return dict(physicalNamespaceChecked=True, sourceState=state, originalInodeAndBytes=True)
