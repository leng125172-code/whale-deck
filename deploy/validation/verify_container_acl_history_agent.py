#!/usr/bin/env python3
"""Independent installed history/RPC evidence and original HDD check, no writes."""
import json

import verify_container_acl_agent as acl
from validate_container_acl_history_agent import entry_proof, snapshot_proof, history_bytes


def verify(evidence, baseline, rows, storage):
    result = json.loads((evidence / 'history-verified.json').read_bytes())
    assert result['installedAclHistoryRpc'] == 'passed' and not result['humanBffOrUi'] and not result['restorationExecuted']
    pages = json.loads((evidence / 'history-pages.json').read_bytes())
    snapshots = json.loads((evidence / 'history-snapshots.json').read_bytes())
    assert len(pages) == result['pages'] and len(snapshots) == result['snapshots'] == 8
    assert sum(p['boundaryRefusals'] for p in pages) == result['listBoundaryRefusals'] == 19
    assert sum(p['boundaryRefusals'] for p in snapshots) == result['readBoundaryRefusals'] == 10
    expected = {r['request']['operationId']: r for r in rows if r['result']['state'] == 'Completed'}
    for path in ('work/original.bin', 'work/acl-file.bin', 'work/acl-directory'):
        sequence = [json.loads(p['page']) for p in pages if json.loads(p['page'])['location']['path'] == path]
        assert sequence and sequence[-1]['complete'] and len({p['version'] for p in sequence}) == 1
        after, seen = '', []
        for index, page in enumerate(sequence):
            assert page['location'] == dict(containerId=baseline['containerId'], instanceId=baseline['instanceId'], path=path)
            assert page['complete'] == (index == len(sequence) - 1)
            if not page['complete']: assert page['nextAfter'] > after
            for entry in page['entries']:
                row = expected[entry['captureId']]; entry_proof(entry, row)
                assert row['request']['plan']['command']['path'] == path and entry['captureId'] > after
                seen.append(entry['captureId'])
            after = page['nextAfter']
        assert len(seen) == len(set(seen))
        assert set(seen) == {k for k, v in expected.items() if v['request']['plan']['command']['path'] == path}
    seen = set()
    for response in snapshots:
        snapshot = json.loads(response['snapshot']); capture = snapshot['entry']['captureId']
        assert capture not in seen; seen.add(capture)
        row = expected[capture]; command = row['request']['plan']['command']
        snapshot_proof(snapshot, row, dict(containerId=baseline['containerId'], instanceId=baseline['instanceId'], path=command['path']))
        hdd = json.loads((evidence / (capture + '.hdd.json')).read_bytes())
        assert hdd['createdAtUnixMs'] == int(snapshot['entry']['createdAtUnixMs']) and hdd['historyDigest'] == snapshot['entry']['historyDigest']
    assert seen == set(expected)
    transcript = [json.loads(line) for line in (evidence / 'history-rpc.jsonl').read_bytes().splitlines()]
    assert transcript[0]['kind'] == 'ready' and transcript[0]['protocolMinor'] >= 52
    assert transcript[1:-1] == pages + snapshots and transcript[-1] == {'kind': 'done'}
    assert not (evidence / 'history-rpc.stderr').read_bytes()
    original = json.loads((evidence / 'history-hdd-before.json').read_bytes())
    assert original == json.loads((evidence / 'history-hdd-after.json').read_bytes())
    assert original == json.loads(json.dumps(history_bytes(storage, rows)))
    return dict(independentInstalledAclHistory='passed', pages=len(pages), snapshots=8, authorityRejections=29,
                immutablePreimages=True, currentSourceDeletedNotReRead=True, humanBffOrUi=False, restorationExecuted=False)


if __name__ == '__main__':
    acl.main(history=True)
