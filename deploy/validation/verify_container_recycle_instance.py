#!/usr/bin/env python3
"""Separate read-only reread of Unknown, declined decision, ACK and retained originals."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

from validate_container_recycle_instance import SOURCES, browser_proof
from container_recycle_instance_proof import prove, require_ack, verify_instance, acknowledgement_audit, APPS
from validate_container_recycle_browser import sql
from validate_container_recycle_agent import baseline, preserved
from container_recycle_agent_io import read_operation
from acl_history_browser_proof import typed_equal
from acl_source_apply_missing_proof import source_closure
from verify_container_recursive_acl import private_json


def verify(name, live=False):
    assert re.fullmatch('container-recycle-instance-[a-f0-9]{32}', name)
    repo = Path(__file__).resolve().parents[2]; e = repo/'deploy/artifacts'/name
    read = lambda n: private_json(e/n)
    source, fixture, browser, initial, final = [read(n) for n in ('source.json','fixture.json','browser.json','unknown-proof.json','live-proof.json')]
    assert source['validationSourceSha256'] == source_closure(repo, source['validationRevision'], seeds=SOURCES)
    for n,h in source['validationSourceSha256'].items(): assert hashlib.sha256((repo/'deploy/validation'/n).read_bytes()).hexdigest() == h
    assert Path(fixture['folder']).parent == APPS and re.fullmatch('wdv-container-publish-[a-f0-9]{32}', Path(fixture['folder']).name)
    browser_proof(browser, fixture); require_ack(initial, final, browser['recoverySubject']); typed_equal(read('canceled-decision-proof.json'), initial)
    cut, record, exited = [read(n) for n in ('cut-proof.json','cut-record.json','helper-exit.json')]
    assert cut['twoKernelFdProofs'] and cut['sourceNotRemoved'] and type(cut['pid']) is int and cut['pid'] > 1
    assert re.fullmatch('[a-f0-9]{32}', cut['invocationId']) and re.fullmatch('whaledeck-container-recycling-[a-f0-9]{32}.service', cut['cgroup'])
    assert record['state'] in ('Preparing','Retaining') and record['historyVerified'] and record['operationId'] == initial['request']['operationId'] and record['owner'] == initial['request']['owner'] and record['plan'] == initial['request']['plan']
    typed_equal(cut['sourceIdentity'], fixture['original']['identity']); assert cut['sourceSha256'] == fixture['original']['sha256'] and cut['historyDigest'] == initial['receipt']['historyDigest']
    verify_instance(fixture,e,final['sql']['row'])
    assert exited['observedWithOriginalPidfd'] and exited['measurementBeginsBeforeOwnedInstanceRestart'] and 0 < exited['elapsedSeconds'] < 20
    if live: typed_equal(prove(browser['result'], fixture, e), final)
    else:
        typed_equal(sql(browser['result']['id']), final['sql'])
        typed_equal(read_operation(final['request'], final['receipt'], e/'originals'), final['records'])
        typed_equal(acknowledgement_audit(final['sql']['row']['execution_job_id']), final['acknowledgementAudit'])
        assert read('independent-live.json')['sourceReread'] and read('verified.json')['fixtureAndUsersRemoved'] and not os.path.lexists(fixture['folder'])
        assert subprocess.run(['docker','inspect',fixture['containerId']], capture_output=True).returncode != 0
    preserved(read('before.json'), baseline(source['revision']), [final['request']['operationId']])
    return dict(independent=True, sourceReread=live, originalInstanceChangedWithoutSourceMutation=True, declinedDecisionUnchanged=True, onlyExplicitDifferentAdminAck=True, rootAndHddReread=True)


if __name__ == '__main__':
    try:
        assert os.geteuid() == 0 and (len(sys.argv) == 2 or sys.argv[2:] == ['--live'])
        os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'; live = sys.argv[2:] == ['--live']
        value = verify(sys.argv[1], live)
        p = Path(__file__).resolve().parents[2]/'deploy/artifacts'/sys.argv[1]/('independent-live.json' if live else 'independent-final.json')
        with p.open('x') as out: json.dump(value, out)
        print(json.dumps(dict(recycleInstanceIndependent='passed', **value)))
    except BaseException as error:
        import traceback
        t=traceback.extract_tb(error.__traceback__)[-1]
        print(json.dumps(dict(recycleInstanceIndependent='failed',type=type(error).__name__,file=Path(t.filename).name,line=t.lineno)))
        raise SystemExit(1) from None
