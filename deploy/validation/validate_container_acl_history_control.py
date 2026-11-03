#!/usr/bin/env python3
"""Installed exact InvocationID stop and stdin-EOF, BEFORE any target request.

This tests the real Agent sudo identity and fixed history unit, not cancellation
of an in-flight signed RPC. No container/path or ACL/HDD mutation is requested.
"""
import json
import os
from pathlib import Path
import re
import select
import subprocess
import sys
import time
import uuid

from validate_container_acl_browser import no_active_work
from validate_container_recursive_ownership_agent import host_identity
from validate_container_file_publishing import common
from validate_container_file_write_agent import trusted

WRAPPER = '/usr/local/libexec/whaledeck-privileged'
PREFIX = ['runuser', '-u', 'whaledeck-agent', '--', 'sudo', '-n', WRAPPER]


def properties(unit):
    text = subprocess.check_output(['systemctl', 'show', unit, '-p', 'MainPID', '-p', 'InvocationID', '-p', 'CapabilityBoundingSet',
        '-p', 'ReadWritePaths', '-p', 'ProtectSystem', '-p', 'NoNewPrivileges'], text=True, timeout=5)
    return dict(line.split('=', 1) for line in text.splitlines())


def frame(process):
    data = b''; deadline = time.monotonic() + 6
    while b'\n' not in data:
        assert select.select([process.stdout], [], [], max(0, deadline-time.monotonic()))[0], 'DRIVER_FRAME_DEADLINE'
        chunk = os.read(process.stdout.fileno(), 4096)
        assert chunk, 'DRIVER_FRAME_EOF'
        data += chunk; assert len(data) <= 4096
    assert data.endswith(b'\n') and data.count(b'\n') == 1
    return json.loads(data)


def main():
    assert os.geteuid() == 0 and len(sys.argv) == 2
    revision = sys.argv[1]; assert re.fullmatch('[a-f0-9]{40}', revision)
    os.umask(0o077); os.environ['GIT_OPTIONAL_LOCKS'] = '0'
    repo = Path(__file__).resolve().parents[2]
    for name in ('whaledeck-privileged', 'whaledeck-container-acl-history.py', 'whaledeck-container-acl-history-transport.py'):
        p = Path('/usr/local/libexec') / name; trusted(p)
        assert p.read_bytes() == subprocess.check_output(['git', '-C', str(repo), 'show', revision+':deploy/host/'+name])
    no_active_work(); before = common.core_snapshot(); hosts = host_identity(revision)
    evidence = repo/'deploy/artifacts'/('container-acl-history-control-'+uuid.uuid4().hex)
    evidence.mkdir(mode=0o700)
    rows = []
    for action in ('exact-stop', 'stdin-eof'):
        identifier = uuid.uuid4().hex; unit = 'whaledeck-container-acl-history-'+identifier+'.service'
        invocation = None
        with (evidence/(action+'.stderr')).open('wb') as errors:
            process = subprocess.Popen([*PREFIX, 'container-file-acl-history', 'list', identifier],
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors)
            try:
                driver = frame(process)
                assert set(driver) == {'type', 'invocationId'} and driver['type'] == 'driver'
                invocation = driver['invocationId']; assert re.fullmatch('[a-f0-9]{32}', invocation)
                active = properties(unit)
                assert active['InvocationID'] == invocation and int(active['MainPID']) > 0
                assert set(active['CapabilityBoundingSet'].split()) == {'cap_dac_read_search', 'cap_sys_ptrace'}
                assert active['ReadWritePaths'] == '/var/lib/whaledeck-container-files' and active['ProtectSystem'] == 'strict' and active['NoNewPrivileges'] == 'yes'
                if action == 'exact-stop':
                    wrong = uuid.uuid4().hex; assert wrong != invocation
                    rejected = subprocess.run([*PREFIX, 'container-file-acl-history-stop', identifier, wrong], capture_output=True, timeout=5)
                    assert rejected.returncode == 2 and properties(unit) == active
                    stopped = subprocess.run([*PREFIX, 'container-file-acl-history-stop', identifier, invocation], capture_output=True, timeout=7)
                    assert stopped.returncode == 0
                    process.stdin.close(); process.wait(timeout=8)
                    assert not process.stdout.read(1)
                else:
                    process.stdin.close(); error = frame(process)
                    assert error == {'type': 'error', 'code': 'CONTAINER_ACL_HISTORY_UNAVAILABLE'}
                    process.wait(timeout=8); assert process.returncode != 0 and not process.stdout.read(1)
                assert not properties(unit)['InvocationID'] and properties(unit)['MainPID'] == '0'
                rows.append(dict(action=action, unit=unit, invocationId=invocation, properties=active, passed=True))
                (evidence/'results.json').write_text(json.dumps(rows))
            finally:
                if process.poll() is None and invocation is not None:
                    subprocess.run([*PREFIX, 'container-file-acl-history-stop', identifier, invocation], capture_output=True, timeout=7)
                if not process.stdin.closed: process.stdin.close()
                try: process.wait(timeout=8)
                except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
                process.stdout.close()
    assert common.core_snapshot() == before and host_identity(revision) == hosts
    idle = no_active_work()
    result = dict(installedHistoryControl='passed', cases=2, exactStopAndWrongInvocation=True, eofExited=True,
                  coreAndHostsUnchanged=True, targetRequestSent=False, signedRpcCancellation=False, idle=idle)
    (evidence/'verified.json').write_text(json.dumps(result)); print(json.dumps({**result, 'evidence':evidence.name}))


if __name__ == '__main__': main()
