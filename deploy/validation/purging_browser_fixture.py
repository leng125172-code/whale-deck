#!/usr/bin/env python3
"""Independent disk observer for TWO owned Trash captures cleared by real UI.

Reuses the reviewed source/target fixture and full-byte capture observer. Never
calls product APIs or engines. Ready evidence precedes deletion; finish only
removes unchanged, never-submitted targets and empty fixture directories.
Captures, manifests, root journals and SQL are never removed by this observer.
"""
import itertools
import json
import os
import stat
import sys

import overwriting_browser_fixture as original
from validate_file_tree_purging_bff import completed_root_receipt, metadata_components


def save(folder, name, value):
    assert name in ('purge-ready-light.json', 'purge-ready-dark.json', 'purge-result.json')
    assert folder.resolve() == folder and folder.lstat().st_uid == 0
    assert stat.S_IMODE(folder.lstat().st_mode) == 0o700
    raw = original.encode(value); assert len(raw) <= 65536
    descriptor = os.open(folder / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(descriptor, 'wb', closefd=False) as stream:
            stream.write(raw); stream.flush(); os.fsync(descriptor)
    finally: os.close(descriptor)


def ready(marker, theme, capture_id):
    assert theme in original.THEMES
    capture = original.identifier(capture_id)
    folder, evidence, baseline = original.fixture(marker)
    _, store = original.policy_and_store()
    original.inspect_ready(marker, theme, capture_id)
    recycled, saved, _, retained, _ = original.attest_source(folder, baseline, store, theme, capture)
    manifest = store / (capture + '.manifest.json')
    journal = original.STATE / 'tree-recycling' / (capture + '.json')
    record = {'theme': theme, 'captureId': capture_id, 'marker': marker,
        'manifestProof': original.proof(manifest), 'recyclingProof': original.proof(journal),
        'source': recycled['plan']['source'], 'captureOwner': saved['owner'],
        'archiveSha256': saved['archiveSha256'], 'archiveBytes': saved['archiveBytes'],
        'retainedIdentity': original.proof(retained)[:2], 'differentSourceDevice': saved['differentSourceDevice']}
    save(evidence, 'purge-ready-' + theme + '.json', record)
    return {'purgingBrowserReadyDiskProof': 'passed', 'theme': theme,
        'fullBytes': original.SIZE, 'archiveAndOriginalVerifiedBeforeDeletion': True,
        'unsubmittedTargetUnchanged': True}


def completed(marker, operations, finish=False):
    folder, evidence, baseline = original.fixture(marker); _, store = original.policy_and_store()
    assert type(operations) is list and 1 <= len(operations) <= 2
    if finish: assert len(operations) == 2
    themes, ids, reports, persistent = set(), set(), [], {}
    for item in operations:
        assert type(item) is dict and set(item) == {'theme', 'captureId', 'executionJobId'}
        theme = item['theme']; assert theme in original.THEMES and theme not in themes; themes.add(theme)
        capture, execution = original.identifier(item['captureId']), original.identifier(item['executionJobId'])
        assert capture != execution and capture not in ids and execution not in ids; ids.update((capture, execution))
        before = original.private_json(evidence / ('purge-ready-' + theme + '.json'), 65536)
        assert before['theme'] == theme and before['captureId'] == item['captureId'] and before['marker'] == marker
        source = before['source']
        assert source['rootId'] == original.ROOT and source['path'] == marker + '/source-' + theme
        assert not os.path.lexists(folder / ('source-' + theme))
        journal = original.STATE / 'tree-purging' / (execution + '.json')
        receipt = original.private_json(journal, 65536)
        completed_root_receipt(receipt, item['executionJobId'], item['captureId'], 'All')
        command = receipt['plan']['command']
        assert command['captureOwner'] == before['captureOwner'] and command['source'] == source
        saved = original.private_json(store / (capture + '.json'), 32768)
        assert metadata_components(saved) == (True, True)
        assert saved['operationId'] == capture and saved.get('captureKind', 'Trash') == 'Trash'
        assert saved['source'] == source and saved['owner'] == before['captureOwner']
        assert saved['archiveSha256'] == before['archiveSha256'] and saved['archiveBytes'] == before['archiveBytes']
        assert not os.path.lexists(store / (capture + '.tar.gz'))
        assert not os.path.lexists(folder / '.whaledeck-files' / (capture + '.tree-trash'))
        for path, expected in ((store / (capture + '.manifest.json'), before['manifestProof']),
                (original.STATE / 'tree-recycling' / (capture + '.json'), before['recyclingProof'])):
            assert original.proof(path) == tuple(expected); persistent[path] = tuple(expected)
        for path in (journal, store / (capture + '.json')): persistent[path] = original.proof(path)
        reports.append({'theme': theme, 'fullBytesBeforeDeletion': original.SIZE,
            'bothComponentsAbsent': True, 'manifestAndRootReceiptsRetained': True,
            'differentSourceDevice': before['differentSourceDevice']})
    expected, removable = {'.whaledeck-files'}, {}
    for theme in original.THEMES:
        kinds = ('target',) if theme in themes else ('source', 'target')
        for kind in kinds:
            root = folder / (kind + '-' + theme); before = baseline['trees'][root.name]
            for path, field in ((root, 'root'), (root / original.MEMBER, 'file'), (root / '空目录', 'empty')):
                assert original.proof(path) == tuple(before[field])
                expected.add(str(path.relative_to(folder))); removable[path] = tuple(before[field])
            assert (root / original.MEMBER).read_bytes() == original.payload(marker, theme, kind)
    members = list(itertools.islice(folder.rglob('*'), 33)); assert len(members) <= 32
    # Before final finish the other theme might already be recycled but not
    # yet purged. Do not inspect/clean that mixed state as if still untouched.
    assert {str(path.relative_to(folder)) for path in members} == expected
    assert all(original.proof(path) == value for path, value in persistent.items())
    if finish:
        assert themes == set(original.THEMES)
        namespace = folder / '.whaledeck-files'
        assert namespace.resolve() == namespace and namespace.is_dir() and not any(namespace.iterdir())
        assert namespace.lstat().st_uid == 0 and stat.S_IMODE(namespace.lstat().st_mode) == 0o700
        assert all(original.proof(path) == value for path, value in removable.items())
        save(evidence, 'purge-result.json', {'marker': marker, 'operations': operations, 'reports': reports})
        # Explicit original baseline identities only. No recursive deletion,
        # no history-file unlink, and no re-snapshot of arbitrary current data.
        for path, value in removable.items():
            if stat.S_ISREG(value[2]): assert original.proof(path) == value; path.unlink()
        for path, value in sorted(removable.items(), key=lambda pair: len(pair[0].parts), reverse=True):
            if stat.S_ISDIR(value[2]): assert original.proof(path)[:2] == value[:2]; path.rmdir()
        namespace.rmdir(); folder.rmdir(); assert not os.path.lexists(folder)
        assert all(original.proof(path) == value for path, value in persistent.items())
    return {'realBrowserPurgingDiskObserver': 'passed', 'reports': reports,
        'onlyUntouchedOwnedLiveTargetsRemoved': finish, 'permanentHistoryRetained': True}


def main():
    assert os.geteuid() == 0
    raw = sys.stdin.buffer.readline(65537); assert 1 <= len(raw) <= 65536
    request = json.loads(raw); assert type(request) is dict
    if request == {'action': 'create'}: result = original.create_fixture()
    elif set(request) == {'action', 'marker', 'theme', 'captureId'} and request['action'] == 'ready':
        result = ready(request['marker'], request['theme'], request['captureId'])
    else:
        assert set(request) == {'action', 'marker', 'operations'} and request['action'] in ('inspect', 'finish')
        result = completed(request['marker'], request['operations'], request['action'] == 'finish')
    print(json.dumps(result), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'purgingBrowserObserverFailed': True, 'failureType': type(error).__name__,
            'unverifiedObjectsPreserved': True}), file=sys.stderr)
        raise SystemExit(1) from None
