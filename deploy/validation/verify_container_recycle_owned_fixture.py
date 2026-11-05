#!/usr/bin/env python3
"""Read-only QA takeback/cleanup proof; original product state is historical."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl import private_json
from recover_container_recycle_owned_fixture import FAILED, FAILED_MANIFEST, SOURCES, moved_proof, stopped
from container_recycle_agent_io import physical, file_snapshot, read_operation
from recursive_acl_restore_agent_io import frame
from validate_container_recycle_restore_agent import baseline
from validate_container_file_publishing import common


def verify(name, returned):
    assert re.fullmatch('container-recycle-owned-recovery-[a-f0-9]{32}', name) and type(returned) is bool
    repo = Path(__file__).resolve().parents[2]; e = repo/'deploy/artifacts'/name
    source = private_json(e/'source.json'); fixture = private_json(e/'fixture.json'); prepared = private_json(e/'prepared.json')
    assert source['failedEvidence'] == FAILED and source['failedManifest'] == FAILED_MANIFEST
    old = repo/'deploy/artifacts'/FAILED; assert hashlib.sha256((old/'EVIDENCE_SHA256.json').read_bytes()).hexdigest() == FAILED_MANIFEST
    for path, digest in private_json(old/'EVIDENCE_SHA256.json').items(): assert hashlib.sha256((old/path).read_bytes()).hexdigest() == digest
    assert source['sourceSha256'] == source_closure(repo, source['validationRevision'], seeds=SOURCES)
    for path, digest in source['sourceSha256'].items(): assert hashlib.sha256((repo/'deploy/validation'/path).read_bytes()).hexdigest() == digest
    original_fixture = private_json(old/'fixture.json'); steps = private_json(old/'steps.json'); last = steps[-1]
    typed_equal(fixture['fixture'], original_fixture); typed_equal(fixture['beforePhysical'], last['physical'])
    assert prepared == dict(manualQaTakeback=True, productRestore=False, sourceOperationId=fixture['operationId'], nativeState='Trashed', noNativeRecordMutation=True)
    assert fixture['operationId'] == last['request']['operationId']
    native = read_operation(last['request'], last['receipt'], e/'native-originals'); typed_equal(native, last['records'])
    assert native['root']['state'] == native['history']['captureState'] == 'Trashed'
    # Native receipt is the archived pre-takeback observation. It does not
    # describe the namespace after this external QA action, nor current binding.
    typed_equal(private_json(e/'native-originals/root.json'), native['root']); typed_equal(private_json(e/'native-originals/hdd.json'), native['history'])
    restored = private_json(e/'returned.json'); assert restored['retained'] is None
    moved_proof(fixture['beforePhysical']['retained'], restored['source']); typed_equal(restored['privateIdentity'], fixture['beforePhysical']['privateIdentity'])
    folder = Path(original_fixture['folder']); assert folder.parent == common.APPS and folder.name == original_fixture['name']
    current = baseline(source['revision']); typed_equal(private_json(e/'before.json'), current)
    if returned:
        typed_equal(physical(folder, fixture['operationId']), restored); typed_equal(file_snapshot(folder/'original.bin'), fixture['sentinel'])
        typed_equal(frame(folder.lstat())[:6], fixture['folderIdentity'][:6]); stopped(original_fixture, folder)
        assert set(p.name for p in folder.iterdir()) == {'original.bin', 'recycle-rpc.bin', '.whaledeck-container-recycling-v1'}
    else:
        assert not os.path.lexists(folder)
        result = subprocess.run(['docker', 'inspect', original_fixture['header']['containerId']], capture_output=True, timeout=10)
        assert result.returncode != 0 and b'no such' in result.stderr.lower()
        typed_equal(private_json(e/'after.json'), current)
        assert private_json(e/'returned-independent.json')['physicalReread'] is True
        assert private_json(e/'verified.json') == dict(manualQaTakeback=True, productRestore=False, nativeJournalAndHddByteExact=True, fixtureAbsent=True, containerAbsent=True, allOtherBaselineFieldsPreserved=True)
    return dict(manualQaTakeback=True, productRestore=False, physicalReread=returned, nativeJournalAndHddByteExact=True, fixtureAbsent=not returned, oldFailureStillFailed=True)


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 3 and sys.argv[2] == '--returned'); os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
        returned = sys.argv[2:] == ['--returned']; result = verify(sys.argv[1], returned)
        e = Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]
        with (e/('returned-independent.json' if returned else 'final-independent.json')).open('x') as f: json.dump(result, f)
        print(json.dumps(dict(ownedRecoveryIndependent='passed', **result)))
    except BaseException as error:
        import traceback
        f = traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(ownedRecoveryIndependent='failed', type=type(error).__name__, file=Path(f.filename).name, line=f.lineno))); raise SystemExit(1) from None
