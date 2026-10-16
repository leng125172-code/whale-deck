"""Hold one owned disposable UI acceptance container until stdin closes (max 600s)."""
import json
import re
import select
import signal
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'validation'))
from validate_container_terminal import docker


def interrupted(_number, _frame):
    raise KeyboardInterrupt()


def main():
    for number in (signal.SIGHUP, signal.SIGINT, signal.SIGTERM):
        signal.signal(number, interrupted)
    image = docker('inspect', '--format', '{{.Image}}', 'whaledeck-worker')
    assert re.fullmatch(r'sha256:[a-f0-9]{64}', image)
    name = 'wd-browser-terminal-' + uuid.uuid4().hex
    identifier = None
    try:
        identifier = docker('run', '--detach', '--name', name, '--label', 'io.whaledeck.validation-owner=' + name,
                            '--network', 'none', '--read-only', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
                            '--pids-limit', '32', '--memory', '128m', '--cpus', '0.25', '--user', '1000:1000',
                            '--entrypoint', '/bin/sh', image, '-c', 'exec sleep 600')
        assert re.fullmatch(r'[a-f0-9]{64}', identifier)
        print(json.dumps({'containerId': identifier, 'name': name, 'lifetimeSeconds': 600}), flush=True)
        if select.select([sys.stdin], [], [], 600)[0]:
            sys.stdin.readline(64)
    finally:
        # Use the generated name only to recover an interrupted create; removal
        # always uses the exact inspected immutable ID after ownership checks.
        inspected = docker('inspect', '--format', '{{.Id}} {{.Name}} {{index .Config.Labels "io.whaledeck.validation-owner"}}', identifier or name).split()
        assert len(inspected) == 3 and re.fullmatch(r'[a-f0-9]{64}', inspected[0]) and inspected[1:] == ['/' + name, name]
        if identifier: assert inspected[0] == identifier
        docker('rm', '--force', inspected[0])
        print(json.dumps({'ownedTerminalContainerRemoved': True}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except BaseException as error:
        print(json.dumps({'failureType': type(error).__name__}), file=sys.stderr)
        raise SystemExit(1) from None
