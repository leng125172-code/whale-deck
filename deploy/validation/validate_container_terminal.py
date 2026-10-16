"""Real BFF Exec acceptance in one owned non-root/no-network temporary container.

Uses existing immutable image ID, no production env, mounts, ports or data.
Authentication is read by validate_host_terminal in memory; never printed.
"""
import json
import os
import re
import subprocess
import uuid

import validate_host_terminal as bff


def docker(*args):
    return subprocess.run(['docker', *args], check=True, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, text=True, timeout=30).stdout.strip()


def main():
    image = docker('inspect', '--format', '{{.Image}}', 'whaledeck-worker')
    assert re.fullmatch(r'sha256:[a-f0-9]{64}', image)
    name = 'wd-bff-terminal-validation-' + uuid.uuid4().hex
    label = 'io.whaledeck.validation-owner=' + name
    identifier = None
    try:
        identifier = docker('run', '--detach', '--name', name, '--label', label,
                            '--network', 'none', '--read-only', '--cap-drop', 'ALL',
                            '--security-opt', 'no-new-privileges', '--pids-limit', '32',
                            '--memory', '128m', '--cpus', '0.25', '--user', '1000:1000',
                            '--entrypoint', '/bin/sh', image, '-c', 'exec sleep 600')
        assert re.fullmatch(r'[a-f0-9]{64}', identifier)
        started = docker('inspect', '--format', '{{.State.StartedAt}}', identifier)
        os.environ['VALIDATION_CONTAINER_ID'] = identifier
        bff.main()
        assert docker('inspect', '--format', '{{.State.Running}}', identifier) == 'true'
        assert docker('inspect', '--format', '{{.State.StartedAt}}', identifier) == started
        print(json.dumps({'sameContainerStartupAfterExecClose': 'passed', 'productionVolumesMounted': False}))
    finally:
        os.environ.pop('VALIDATION_CONTAINER_ID', None)
        # Resolve exact immutable target and ownership before removing anything.
        # A creation failure after Docker allocated the object is also recovered.
        lookup = subprocess.run(['docker', 'inspect', '--format', '{{.Id}} {{.Name}} {{index .Config.Labels "io.whaledeck.validation-owner"}}', identifier or name],
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, timeout=15)
        if lookup.returncode == 0:
            parts = lookup.stdout.strip().split()
            assert len(parts) == 3 and re.fullmatch(r'[a-f0-9]{64}', parts[0]) and parts[1:] == ['/' + name, name]
            if identifier: assert parts[0] == identifier
            docker('rm', '--force', parts[0])
        else:
            assert identifier is None, 'TERMINAL_TEST_CONTAINER_MISSING'
        print(json.dumps({'ownedValidationContainerCleanup': 'passed', 'immutableAuditRetained': True}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'containerTerminalAcceptance': 'failed', 'phase': bff.PHASE, 'failureType': type(error).__name__}))
        raise SystemExit(1)
