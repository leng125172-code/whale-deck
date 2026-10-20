#!/usr/bin/env python3
"""Read-only, bounded boolean diagnosis of this validation fixture, never a retry."""
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tarfile
from validate_tree_overwrite_restoring_agent import DIRECTORY, STATE, fixture_scope, plain_file, proof


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2 and re.fullmatch('wdv-agent-overwrite-[a-f0-9]{16}', sys.argv[1])
    fixture = DIRECTORY / sys.argv[1]
    fixture_scope(fixture)
    policy = next(row for row in json.loads(Path('/etc/whaledeck/file-roots.json').read_bytes()) if row['id'] == 'whaledeck.files.shared')
    configuration = json.loads(Path('/etc/whaledeck/file-backups.json').read_bytes())
    store = Path(configuration['mountPoint']) / configuration['targetRelative'] / 'directory-archives'
    private = fixture / '.whaledeck-files'
    assert private.resolve() == private and private.is_dir()
    children = list(private.iterdir())
    assert len(children) == 4 and all(re.fullmatch('[a-f0-9]{32}\\.(tree-trash|tree-overwrite-restore)', path.name) for path in children)
    reports = []
    for retained in children:
        if not retained.name.endswith('.tree-overwrite-restore'): continue
        operation = retained.name.split('.')[0]
        record_path = STATE / 'tree-overwrite-restoring' / (operation + '.json')
        plain_file(record_path, 32768)
        record = json.loads(record_path.read_bytes())
        command = record['plan']['command']
        assert command['target']['path'].startswith(fixture.name + '/')
        size = command['target']['path'].split('/')[-1].removesuffix('-target')
        assert size in ('small', 'large')
        capture = command['captureId']
        assert re.fullmatch('[a-f0-9]{32}', capture)
        recycle_path = STATE / 'tree-recycling' / (capture + '.json')
        plain_file(recycle_path, 32768)
        recycle = json.loads(recycle_path.read_bytes())
        assert recycle['plan']['source']['path'] == fixture.name + '/' + size + '-source'
        source = private / (capture + '.tree-trash')
        source_data, old_data, output_data = proof(source / '中文.bin'), proof(retained / '中文.bin'), proof(fixture / (size + '-target') / '中文.bin')
        checks = {'rootCompleted': record['state'] == 'Completed' and recycle['state'] == 'Completed',
            'rootFlags': all(record.get(key) for key in ('backupStarted', 'backupVerified', 'swapStarted', 'originalsRetained')),
            'differentOwners': record['owner'] != recycle['owner'], 'inputBinding': command['source'] == recycle['plan']['source'],
            'newIdentity': proof(fixture / (size + '-target'))[:2] == tuple(record['newIdentity']),
            'oldIdentity': proof(retained)[:2] == tuple(record['plan']['targetIdentity']),
            'sourceRetainedIdentity': proof(source)[:2] == tuple(recycle['retainedIdentity']),
            'outputWholeBytes': output_data[6] == source_data[6] and output_data[-1] == source_data[-1],
            'newOutputInode': output_data[:2] != source_data[:2] and output_data[:2] != old_data[:2],
            'outputOwner': output_data[3:5] == (policy['ownerUid'], policy['ownerGid']),
            'outputMode': stat.S_IMODE(output_data[2]) == 0o640, 'outputMtime': output_data[7] == source_data[7]}
        checks['outputShape'] = {str(path.relative_to(fixture / (size + '-target'))) for path in (fixture / (size + '-target')).rglob('*')} == {'空目录', '中文.bin'}
        checks['sourceAbsent'] = not (fixture / (size + '-source')).exists()
        checks['originalSourceMtime'] = source_data[7] == 1712345678123456789
        checks['originalTargetMtime'] = old_data[7] == 1712345678123456789
        scoped_aborts = []
        for candidate in (STATE / 'tree-overwrite-restoring').iterdir():
            if not re.fullmatch('[a-f0-9]{32}\\.json', candidate.name): continue
            plain_file(candidate, 32768)
            value = json.loads(candidate.read_bytes())
            if value.get('plan', {}).get('command', {}).get('target', {}).get('path') == command['target']['path'] and value.get('state') == 'Aborted':
                scoped_aborts.append(value)
        checks['oneAborted'] = len(scoped_aborts) == 1
        if scoped_aborts:
            aborted = scoped_aborts[0]
            checks['abortedPlanOwnerUnswapped'] = aborted['owner'] == record['owner'] and aborted['plan'] == record['plan'] and not aborted.get('swapStarted', False)
            for suffix in ('.json', '.lock'):
                plain_file(STATE / 'tree-overwrite-restoring' / (aborted['operationId'] + suffix), 32768)
        for identifier, folder in ((operation, 'tree-overwrite-restoring'), (capture, 'tree-recycling')):
            for suffix in ('.json', '.lock'): plain_file(STATE / folder / (identifier + suffix), 32768)
        for kind, identifier, original, expected_owner, expected_location in (
            ('source', capture, source_data, recycle['owner'], recycle['plan']['source']),
            ('target', operation, old_data, record['owner'], command['target'])):
            path = store / (identifier + '.json')
            plain_file(path, 32768)
            saved = json.loads(path.read_bytes())
            manifest_path = store / (identifier + '.manifest.json')
            plain_file(manifest_path, 16 * 1024**2)
            manifest = json.loads(manifest_path.read_bytes())
            member = next(item for item in manifest['entries'] if item['path'] == '中文.bin')
            archive = store / (identifier + '.tar.gz')
            plain_file(archive)
            archive_proof = proof(archive)
            prefix = kind + '.'
            if kind == 'source':
                original_root = next(item for item in manifest['entries'] if item['path'] == '')
                checks[prefix + 'privateSkeletonHasNewRootIdentity'] = proof(source)[:2] != tuple(original_root['sourceIdentity'][:2])
            checks[prefix + 'rootPlanManifest'] = saved['manifestSha256'] == (recycle['plan']['manifestSha256'] if kind == 'source' else record['plan']['targetManifestSha256'])
            checks[prefix + 'rootPlanArchive'] = saved['archiveBytes'] == (record['plan']['archiveBytes'] if kind == 'source' else record['backupArchiveBytes']) and saved['archiveSha256'] == (record['plan']['archiveSha256'] if kind == 'source' else record['backupArchiveSha256'])
            checks[prefix + 'retainedShape'] = {str(path.relative_to(source if kind == 'source' else retained)) for path in (source if kind == 'source' else retained).rglob('*')} == {'空目录', '中文.bin'}
            for suffix in ('.json', '.manifest.json', '.tar.gz', '.lock'): plain_file(store / (identifier + suffix), None if suffix == '.tar.gz' else 16 * 1024**2)
            checks.update({prefix + 'state': saved['state'] == 'Verified', prefix + 'binding': saved['owner'] == expected_owner and saved['source'] == expected_location,
                prefix + 'kind': saved.get('captureKind', 'Trash') == ('Trash' if kind == 'source' else 'Overwrite'),
                prefix + 'count': saved['entryCount'] == 3 and saved['totalBytes'] == original[6],
                prefix + 'manifestFileHash': proof(manifest_path)[-1] == saved['manifestFileSha256'],
                prefix + 'manifestBinding': manifest['path'] == expected_location['path'] and manifest['rootVersion'] == expected_location['expectedVersion'],
                prefix + 'manifestPaths': {item['path'] for item in manifest['entries']} == {'', '空目录', '中文.bin'},
                prefix + 'manifestIdentity': tuple(member['sourceIdentity'][:8]) == original[:8], prefix + 'manifestWholeHash': member['sha256'] == original[-1],
                prefix + 'archiveIdentityHashBytes': archive_proof[:2] == tuple(saved['archiveIdentity']) and archive_proof[6] == saved['archiveBytes'] and archive_proof[-1] == saved['archiveSha256'],
                prefix + 'differentDevice': saved['differentSourceDevice'] == (original[0] != archive_proof[0])})
            with tarfile.open(archive, 'r:gz') as reader:
                members = reader.getmembers()
                checks[prefix + 'tarPaths'] = {item.name.rstrip('/') for item in members} == {'.', '空目录', '中文.bin'}
                member = next(item for item in members if item.name == '中文.bin')
                with reader.extractfile(member) as stream:
                    checks[prefix + 'tarWholeHash'] = hashlib.file_digest(stream, 'sha256').hexdigest().upper() == original[-1]
        reports.append({'size': size, 'checks': checks})
    assert len(reports) == 2
    print(json.dumps({'overwriteFixtureReadOnlyDiagnosis': reports, 'mutations': 0, 'acceptance': 'not-claimed'}), flush=True)


if __name__ == '__main__':
    try: main()
    except BaseException as error:
        print(json.dumps({'overwriteFixtureReadOnlyDiagnosis': 'failed', 'failureType': type(error).__name__, 'privateDetailsWithheld': True}), flush=True)
        raise SystemExit(1) from None
