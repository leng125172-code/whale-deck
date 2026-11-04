#!/usr/bin/env python3
"""Independent read-only installed restore proof. Live bytes precede cleanup.

Final mode proves source absence and retained archives, never rereads deleted
source bytes or executes Plan/Apply/Discard. No local mutation engine fallback.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

from validate_container_recursive_acl_restore_agent import fixture_bytes, attrs_digest, keep_old, core_health, idle_helpers, HOST_NAMES, PROBE_SOURCES, SEEDS
from validate_container_recursive_acl_agent import host_identity
from validate_container_recursive_acl_browser import no_active_work
from validate_container_recursive_acl_faults import preservation_snapshot
from validate_container_recursive_acl import inventory
from verify_container_recursive_acl import private_json
from verify_container_recursive_acl_agent import proto_plan as source_plan
from recursive_acl_restore_rpc_proof import proto_plan, ready_proof, fields
from recursive_acl_restore_record_proof import records_proof, receipt_proof
from recursive_acl_restore_agent_io import installed, read_bundle, snapshot, hdd_snapshot, unique
from acl_source_apply_missing_proof import source_closure
import validate_container_file_publishing as owned


def transcript_proof(source_rx, restore_rx, sources, rows, late, header, bundles):
    assert len(source_rx) == 8 and len(restore_rx) == 24
    ready_proof(source_rx[0], header); ready_proof(restore_rx[0], header)
    assert source_rx[-1] == restore_rx[-1] == dict(kind='done')
    assert [s['label'] for s in sources] == ['source-access', 'source-default', 'source-newer-access']
    assert [r['label'] for r in rows] == ['access-complete', 'access-noop', 'default-complete', 'discard']
    for index, source in enumerate(sources):
        plan = source_rx[1+index*2]; response = source_rx[2+index*2]
        fields(plan, ('kind', 'plan', 'boundaryRefusals')); assert plan['kind'] == 'plan' and plan['boundaryRefusals'] == 0
        assert source_plan(json.loads(plan['plan'], object_pairs_hook=unique)) == source['request']['plan']
        fields(response, ('kind', 'operationId', 'result')); assert response['kind'] == 'receipt' and response['operationId'] == source['request']['operationId']
        assert json.loads(response['result'], object_pairs_hook=unique) == source['result']
    offset = 1
    for index, row in enumerate(rows):
        plan = restore_rx[offset]; offset += 1
        fields(plan, ('kind', 'plan', 'boundaryRefusals')); assert plan['kind'] == 'plan' and plan['boundaryRefusals'] == (22 if index == 0 else 0)
        assert proto_plan(json.loads(plan['plan'], object_pairs_hook=unique)) == row['request']['plan']
        if index == 0:
            assert restore_rx[offset] == dict(kind='rejected', operationId=row['request']['operationId'], refusals=23); offset += 1
        for action in range(4):
            response = restore_rx[offset]; offset += 1
            fields(response, ('kind', 'operationId', 'result')); assert response['kind'] == 'receipt' and response['operationId'] == row['request']['operationId']
            value = json.loads(response['result'], object_pairs_hook=unique)
            if action == 0: assert value == row['result']
            receipt_proof(value, row['request'], bundles[row['request']['operationId']]['root'], restore=True, binding=index != 3, replayed=action in (1, 2))
    assert restore_rx[offset] == late['response'] and offset+1 == len(restore_rx)-1
    response = late['response']; fields(response, ('kind', 'operationId', 'result')); assert response['kind'] == 'receipt' and response['operationId'] == rows[0]['request']['operationId']
    receipt_proof(json.loads(response['result'], object_pairs_hook=unique), rows[0]['request'], bundles[rows[0]['request']['operationId']]['root'], restore=True, binding=False, replayed=True)
    assert late['before'] == late['after'] == sources[-1]['after'] and sources[-1]['before'] != sources[-1]['after']
    assert sources[0]['after'] == rows[0]['before']
    assert rows[0]['after'] == rows[1]['before'] == rows[1]['after']
    assert sources[1]['before'] == rows[1]['after'] and sources[1]['after'] == rows[2]['before']
    assert rows[2]['after'] == sources[2]['before'] and rows[3]['before'] == rows[3]['after'] == sources[2]['after']
    return dict(normalSourceCaptures=3, distinctRestoreOperations=4, restoreReceipts=17, planRefusals=22, operationRefusals=23)


def verify(name, live=False):
    assert re.fullmatch('container-recursive-acl-restore-agent-[a-f0-9]{32}', name)
    repo = Path(__file__).resolve().parents[2]; evidence = repo/'deploy/artifacts'/name
    from validate_container_upload_recovery_agent import trusted
    trusted(evidence, True); b = private_json(evidence/'baseline.json')
    for key in ('revision', 'probeRevision', 'validationSourceRevision'): assert re.fullmatch('[a-f0-9]{40}', b[key])
    assert b['validationSourceSha256'] == source_closure(repo, b['validationSourceRevision'], seeds=SEEDS)
    for filename, sha in b['validationSourceSha256'].items(): assert hashlib.sha256((repo/'deploy/validation'/filename).read_bytes()).hexdigest() == sha
    assert set(b['sourceHashes']) == set(HOST_NAMES) and set(b['probeSourceSha256']) == set(PROBE_SOURCES)
    for name, sha in b['sourceHashes'].items():
        path = Path('/usr/local/libexec')/name; trusted(path)
        frozen = subprocess.check_output(['git', '-C', str(repo), 'show', b['revision']+':deploy/host/'+name]); assert path.read_bytes() == frozen and hashlib.sha256(frozen).hexdigest() == sha
    for name, sha in b['probeSourceSha256'].items():
        raw = subprocess.check_output(['git', '-C', str(repo), 'show', b['probeRevision']+':'+name]); assert (repo/name).read_bytes() == raw and hashlib.sha256(raw).hexdigest() == sha
    assert host_identity(b['revision']) == b['hosts'] and owned.common.core_snapshot() == {k:tuple(v) for k,v in b['core'].items()}; no_active_work()
    assert core_health(b['core']) == b['coreHealth'] and idle_helpers() == 0
    io = installed(); sources = private_json(evidence/'sources.json'); rows = private_json(evidence/'results.json'); late = private_json(evidence/'late.json'); accepted = private_json(evidence/'accepted.json')
    assert len(sources) == 3 and len(rows) == 4
    ids = [r['request']['operationId'] for r in sources+rows]; assert len(set(ids)) == 7
    bundles = {}; source_rows = {}; children = 0
    for source in sources:
        bundle, frame = read_bundle(io, source['request'], False, evidence/'records'/source['request']['operationId'])
        assert source['metadata'] == bundle['metadata'] and source['sourceFrame'] == frame
        assert source['request']['plan']['command']['containerId'] == b['containerId'] and source['request']['plan']['command']['instanceId'] == b['instanceId']
        children += records_proof(source, bundle, restore=False, attribute_digest=attrs_digest()); source_rows[source['request']['operationId']] = source
    for row in rows:
        bundle, _ = read_bundle(io, row['request'], True, evidence/'records'/row['request']['operationId'])
        children += records_proof(row, bundle, restore=True, source=source_rows[row['sourceId']], frame=source_rows[row['sourceId']]['sourceFrame'], attribute_digest=attrs_digest())
        bundles[row['request']['operationId']] = bundle
        assert row['request']['plan']['command']['containerId'] == b['containerId'] and row['request']['plan']['command']['instanceId'] == b['instanceId']
    assert children == 24 and rows[1]['request']['plan']['changeCount'] == 0 and rows[3]['request']['plan']['changeCount'] > 0
    header = {k:b[k] for k in ('revision', 'probeRevision')}
    def transcript(folder):
        assert (evidence/folder/'rpc.stderr').read_bytes() == b''
        return [json.loads(line, object_pairs_hook=unique) for line in (evidence/folder/'rpc.jsonl').read_bytes().splitlines()]
    proof = transcript_proof(transcript('source-rpc'), transcript('restore-rpc'), sources, rows, late, header, bundles)
    assert accepted == dict(**proof, completedRestores=3, discardedRestores=1, humanOrUi=False)
    current = preservation_snapshot(io.storage); keep_old(current, b['oldProtected']); assert current == private_json(evidence/'protected-final.json')
    for current, previous, filename in [(snapshot(io.sourceState), b['oldSourceRoot'], 'source-root-final.json'), (snapshot(io.restoreState), b['oldRestoreRoot'], 'restore-root-final.json'), (hdd_snapshot(io, True), b['oldRestoreHdd'], 'restore-hdd-final.json')]:
        keep_old(current, previous); assert current == private_json(evidence/filename)
    assert snapshot(Path('/var/lib/whaledeck-container-directories')) == b['directoryRoot']
    content = fixture_bytes(b['containerId']); assert len(content) == b['bytesPerFile'] == 2*1024**2+7 and hashlib.sha256(content).hexdigest() == b['sha256']
    prewrite = private_json(evidence/'independent-prewrite.json'); assert prewrite['sourceBytesReadBeforeFirstCapture'] is True and prewrite['normalSourceCaptures'] == prewrite['restoreOperations'] == 0
    final = private_json(evidence/'physical-final.json'); assert final == late['after'] and b['original'] == sources[0]['before']
    if live:
        observed = io.docker.api('GET', '/containers/'+b['containerId']+'/json'); owned.verify_container(observed, b['containerId'], b['name'], Path(b['source']))
        target = io.docker.target(observed, b['containerId'], io.docker.registry()); assert target['instanceId'] == b['instanceId']
        verify_pinned_source_restore(b, final, io, content)
    else:
        original = private_json(evidence/'independent-live.json'); assert original['sourceBytesReadBeforeCleanup'] is True and original['humanOrUi'] is False
        assert not os.path.lexists(b['source']) and not owned.common.command(['docker', 'ps', '-aq', '--no-trunc', '--filter', 'id='+b['containerId']])
    result = dict(**proof, childHddMetadata=24, activeTransientUnits=0, coreHealthy=True, livePinnedSource=live, sourceBytesReadBeforeCleanup=live, sourceBytesRereadAfterCleanup=False, humanOrUi=False)
    (evidence/('independent-live.json' if live else 'independent-final.json')).open('x').write(json.dumps(result)); return result


def verify_prewrite(name):
    assert re.fullmatch('container-recursive-acl-restore-agent-[a-f0-9]{32}', name)
    repo = Path(__file__).resolve().parents[2]; evidence = repo/'deploy/artifacts'/name
    from validate_container_upload_recovery_agent import trusted
    trusted(evidence, True); b = private_json(evidence/'baseline.json'); io = installed()
    assert b['validationSourceSha256'] == source_closure(repo, b['validationSourceRevision'], seeds=SEEDS)
    for filename, sha in b['validationSourceSha256'].items(): assert hashlib.sha256((repo/'deploy/validation'/filename).read_bytes()).hexdigest() == sha
    assert host_identity(b['revision']) == b['hosts'] and owned.common.core_snapshot() == {k:tuple(v) for k,v in b['core'].items()}
    assert core_health(b['core']) == b['coreHealth'] and idle_helpers() == 0; no_active_work()
    assert preservation_snapshot(io.storage) == b['oldProtected'] and snapshot(io.sourceState) == b['oldSourceRoot']
    assert snapshot(io.restoreState) == b['oldRestoreRoot'] and hdd_snapshot(io, True) == b['oldRestoreHdd']
    assert snapshot(Path('/var/lib/whaledeck-container-directories')) == b['directoryRoot']
    assert not (evidence/'sources.json').exists() and not (evidence/'results.json').exists()
    content = fixture_bytes(b['containerId']); assert len(content) == b['bytesPerFile'] and hashlib.sha256(content).hexdigest() == b['sha256']
    observed = io.docker.api('GET', '/containers/'+b['containerId']+'/json'); owned.verify_container(observed, b['containerId'], b['name'], Path(b['source']))
    assert io.docker.target(observed, b['containerId'], io.docker.registry())['instanceId'] == b['instanceId']
    verify_pinned_source_restore(b, b['original'], io, content)
    result = dict(sourceBytesReadBeforeFirstCapture=True, normalSourceCaptures=0, restoreOperations=0, coreHealthy=True, humanOrUi=False)
    (evidence/'independent-prewrite.json').open('x').write(json.dumps(result)); return result


def verify_pinned_source_restore(b, final, io, content):
    # Reopen the exact owned container. Do not call the older reader's hard-coded
    # fixture recipe and do not rewrite a baseline/hash to make that recipe fit.
    from validate_container_file_staging import load
    import stat
    from validate_container_recursive_acl import PATHS
    bridge = load('/usr/local/libexec/whaledeck-container-acl-transport.py', 'restore_independent_mapping')
    with io.reader.ContainerRoot({k:b[k] for k in ('containerId', 'instanceId')}, io.files, io.docker) as session:
        bridge.namespace_current(io.reader, session)
        root = io.files.open_at(session.root, 'work/recursive-tree', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, cross_mount=True)
        try:
            for relative, kind in PATHS.items():
                fd = io.files.open_at(root, relative, os.O_RDONLY | os.O_NOFOLLOW) if relative else os.dup(root)
                try:
                    bridge.require_unmapped(fd); info = os.fstat(fd); expected = final[relative]
                    assert list(io.reader.fingerprint(info)) == expected['identity']
                    assert io.acl.read_acl(fd, 'access', info.st_mode) == expected['access'] and io.acl.read_acl(fd, 'default', info.st_mode) == expected['default']
                    assert set(os.listxattr(fd)) <= {'user.whaledeck-recursive', *io.acl.XATTRS.values()} and os.getxattr(fd, 'user.whaledeck-recursive') == b'preserve ordinary attribute'
                    if kind == 'File':
                        assert stat.S_ISREG(info.st_mode) and info.st_nlink == 1; offset = 0
                        while True:
                            data = os.read(fd, 65536)
                            if not data: break
                            assert data == content[offset:offset+len(data)]; offset += len(data)
                        assert offset == len(content)
                    else:
                        assert stat.S_ISDIR(info.st_mode) and set(os.listdir(fd)) == {n.rsplit('/', 1)[-1] for n in PATHS if n and n.rpartition('/')[0] == relative}
                    assert list(io.reader.fingerprint(os.fstat(fd))) == expected['identity']
                finally: os.close(fd)
            bridge.namespace_current(io.reader, session); session.current()
        finally: os.close(root)
    assert inventory(Path(b['root']), io.reader, content, io.acl) == final


if __name__ == '__main__':
    assert os.geteuid() == 0; os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    assert len(sys.argv) in (2, 3)
    if len(sys.argv) == 3 and sys.argv[1] == '--prewrite':
        print(json.dumps(verify_prewrite(sys.argv[-1])))
    else:
        live = len(sys.argv) == 3
        if live: assert sys.argv[1] == '--live'
        print(json.dumps(verify(sys.argv[-1], live=live)))
