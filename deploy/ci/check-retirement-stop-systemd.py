#!/usr/bin/env python3
"""Exercise the fixed helper against an inert transient unit on a hosted CI VM."""

import os
from pathlib import Path
import select
import subprocess
import tempfile
import time
import uuid


def run(*args, check=True):
    return subprocess.run(args, check=check, capture_output=True, text=True, timeout=12)


def lookup(unit):
    result = run('/usr/bin/systemctl', 'show', unit, '--property=InvocationID', '--property=MainPID', '--property=ControlGroup')
    return dict(line.split('=', 1) for line in result.stdout.splitlines())


def main():
    if os.geteuid() != 0 or os.environ.get('GITHUB_ACTIONS') != 'true' or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted':
        raise SystemExit('Requires the disposable GitHub-hosted CI VM and root.')

    driver, wrong_invocation = uuid.uuid4().hex, uuid.uuid4().hex
    unit = f'whaledeck-container-recycle-archive-retirements-{driver}.service'
    host = Path(__file__).resolve().parents[1] / 'host'
    stop_name = 'whaledeck-container-recycle-archive-retirement-stop.py'
    with tempfile.TemporaryDirectory(prefix='whaledeck-retirement-ci-', dir='/run') as directory:
        private = Path(directory)
        stop = private / stop_name
        stop.write_bytes((host / stop_name).read_bytes())
        stop.chmod(0o700)
        helper = private / 'helper'
        helper.write_text((host / 'whaledeck-privileged').read_text().replace(f'/usr/local/libexec/{stop_name}', str(stop)))
        helper.chmod(0o700)

        initial = lookup(unit)
        assert initial['MainPID'] == '0' and not initial['InvocationID']
        # The target has both of the production driver's capabilities. Its
        # only behavior is to sleep and exit on signal or the runtime limit.
        run('/usr/bin/systemd-run', '--quiet', '--collect', '--service-type=exec',
            f'--unit={unit}', '--property=Slice=system.slice', '--property=RuntimeMaxSec=30',
            '--property=NoNewPrivileges=yes',
            '--property=CapabilityBoundingSet=CAP_DAC_READ_SEARCH CAP_SYS_PTRACE',
            '/usr/bin/sleep', '30')
        actual = lookup(unit)
        assert actual['ControlGroup'] == f'/system.slice/{unit}'
        assert int(actual['MainPID']) > 1 and len(actual['InvocationID']) == 32
        with os.fdopen(os.pidfd_open(int(actual['MainPID'])), 'rb') as pinned:
            assert not select.select([pinned], [], [], 0)[0]
            # Start the helper without CAP_SYS_PTRACE to match the Agent's
            # capability boundary. PID 1 provisions it only for the stop unit.
            action = ('/usr/bin/setpriv', '--bounding-set=-sys_ptrace',
                      str(helper), 'container-file-recycle-archive-retirements-stop', driver)
            refused = run(*action, wrong_invocation, check=False)
            assert refused.returncode != 0 and lookup(unit) == actual
            assert not select.select([pinned], [], [], 0)[0]
            started = time.monotonic()
            stopped = run(*action, actual['InvocationID'], check=False)
            assert stopped.returncode == 0, stopped.stderr
            assert select.select([pinned], [], [], 0)[0]
            assert time.monotonic() - started < 8
        print('Hosted systemd test passed for invocation mismatch and real pidfd cancellation.')


if __name__ == '__main__':
    main()
