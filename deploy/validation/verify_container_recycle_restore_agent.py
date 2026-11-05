#!/usr/bin/env python3
"""Separate read-only installed transcript, archived originals and live namespace proof."""
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
from recursive_acl_restore_agent_io import read_at
from container_recycle_restore_workflow import transcript_proof
import container_recycle_restore_rpc_proof as proof
import container_recycle_restore_agent_io as restore_io
import container_recycle_agent_io as source_io
from validate_container_recycle_restore_agent import SOURCES, HOSTS, PROBE_SOURCES, baseline, preserved
from validate_container_file_publishing import common, verify_container


def verify(name, live=False):
    assert type(live) is bool and re.fullmatch('container-recycle-restore-agent-[a-f0-9]{32}', name)
    repo = Path(__file__).resolve().parents[2]; e = repo/'deploy/artifacts'/name
    source = private_json(e/'source.json'); before = private_json(e/'baseline.json'); fixture = private_json(e/'fixture.json')
    assert source['validationSourceSha256'] == source_closure(repo, source['validationRevision'], seeds=SOURCES)
    for n, h in source['validationSourceSha256'].items(): assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest() == h
    assert set(source['probeSourceSha256']) == set(PROBE_SOURCES)
    for n, h in source['probeSourceSha256'].items():
        raw = subprocess.check_output(['git', '-C', str(repo), 'show', source['probeRevision']+':'+n]); assert hashlib.sha256(raw).hexdigest() == h and (repo/n).read_bytes() == raw
    for n in HOSTS: assert (Path('/usr/local/libexec')/n).read_bytes() == subprocess.check_output(['git', '-C', str(repo), 'show', source['revision']+':deploy/host/'+n])
    assert fixture['header']['revision'] == source['revision'] and fixture['header']['probeRevision'] == source['probeRevision']
    binary = Path(source['binary']); assert binary.parent.parent == repo/'deploy/artifacts' and re.fullmatch(r'terminal-probe\.[A-Za-z0-9]+', binary.parent.name) and binary.name == 'TerminalProbe'
    assert hashlib.sha256(binary.read_bytes()).hexdigest() == source['binarySha256']
    subprocess.run(['sha256sum', '--quiet', '-c', 'SHA256SUMS'], cwd=binary.parent, check=True, capture_output=True)
    transcript = [proof.unique_json(line) for line in (e/'rpc.jsonl').read_text().splitlines()]; steps = private_json(e/'steps.json')
    summary = transcript_proof(transcript, steps, fixture['header'])
    assert (e/'rpc.stderr').read_bytes() == b'' and (e/'capability/rpc.stderr').read_bytes() == b''
    pre = [proof.unique_json(line) for line in (e/'capability/rpc.jsonl').read_text().splitlines()]
    assert len(pre) == 2 and pre[-1] == dict(kind='done'); proof.ready_proof(pre[0], fixture['header'])
    typed_equal(transcript[1], private_json(e/'source-plan-frame.json')); typed_equal(transcript[4], private_json(e/'plan-frame.json')); typed_equal(transcript[5], private_json(e/'refusals.json'))
    final = {}
    for index, step in enumerate(steps):
        folder = e/('records-'+str(index)); records = step['records']; request = step['request']; receipt = step['receipt']; data = records['data']
        if step['phase'] == 'source':
            _, root, _ = read_at(None, str(folder/'root.json')); _, history, _ = read_at(None, str(folder/'hdd.json'))
            typed_equal(root, records['root']); typed_equal(history, records['history'])
            proof.source_proof.record_proof(request, receipt, root, history, data_sha256=data['sha256'], data_size=data['sizeBytes'])
            source_io.physical_proof(step['physical'], fixture['original'], root, receipt)
        else:
            archived = {}
            for key, name in (('root', 'restore-root.json'), ('claim', 'source-claim.json'), ('sourceRoot', 'source-root.json'), ('history', 'source-hdd.json')):
                _, archived[key], _ = read_at(None, str(folder/name)); typed_equal(archived[key], records[key])
            proof.record_proof(request, receipt, archived['root'], archived['claim'], archived['sourceRoot'], archived['history'], data_sha256=data['sha256'], data_size=data['sizeBytes'])
            restore_io.physical_proof(step['physical'], fixture['original'], records, receipt)
        final[(step['phase'], request['operationId'])] = (index, step)
    assert len(final) == 3
    # The two restore records and the original source are reread independently;
    # historical pre-restore snapshots are never compared to later mutable roots.
    for index, step in final.values():
        reader = source_io.read_operation if step['phase'] == 'source' else restore_io.read_operation
        typed_equal(reader(step['request'], step['receipt'], e/('records-'+str(index))), step['records'])
    current = baseline(source['revision']); preserved(before, current, summary['sourceOperationId'], (summary['cancelOperationId'], summary['restoreOperationId']))
    folder = Path(fixture['folder']); assert folder.parent == common.APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', folder.name) and fixture['name'] == folder.name
    if live:
        typed_equal(source_io.physical(folder, summary['sourceOperationId']), steps[-1]['physical'])
        c = json.loads(common.command(['docker', 'inspect', fixture['header']['containerId']]))[0]
        verify_container(c, fixture['header']['containerId'], fixture['name'], folder)
        assert str(c['State']['Pid'])+':'+c['State']['StartedAt'] == fixture['header']['instanceId']
    else:
        assert not os.path.lexists(folder) and subprocess.run(['docker', 'inspect', fixture['header']['containerId']], capture_output=True).returncode != 0
        typed_equal(private_json(e/'after.json'), current); assert private_json(e/'live-independent.json')['physicalReread'] is True
        accepted = private_json(e/'verified.json'); assert accepted['signedRpc'] is True and accepted['sourceOperations'] == 1 and accepted['restoreOperations'] == 2 and accepted['newReceiptFrames'] == 11 and accepted['sourceReceiptFrames'] == 3 and accepted['sourceAndContainerAbsent'] is True
    return dict(independent=True, physicalReread=live, sourceBytesRereadAfterCleanup=False, signedTranscriptBound=True, historiesRetained=True, humanUi=False, **summary)


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and (len(sys.argv) == 2 or len(sys.argv) == 3 and sys.argv[2] == '--live'); os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
        live = sys.argv[2:] == ['--live']; result = verify(sys.argv[1], live)
        p = Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('live-independent.json' if live else 'final-independent.json')
        with p.open('x') as out: json.dump(result, out)
        print(json.dumps(dict(installedRecycleRestoreIndependent='passed', **result)))
    except BaseException as error:
        import traceback
        f = traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(installedRecycleRestoreIndependent='failed', type=type(error).__name__, file=Path(f.filename).name, line=f.lineno))); raise SystemExit(1) from None
