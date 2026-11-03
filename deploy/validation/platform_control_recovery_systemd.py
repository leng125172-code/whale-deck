#!/usr/bin/env python3
"""Root opt-in systemd tests in bind-isolated paths; never install live tools.

Synthetic ledger/controller bytes are explicit fixtures. This is not application
rollback, a real deployment, physical power-loss or a workstation boot test.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / 'deploy/host'
CONTROLS = ('whaledeck-platform-maintenance', 'whaledeck-platform-revisions.py', 'whaledeck-privileged')
TARGETS = ('/usr/local/libexec', '/var/lib/whaledeck-agent/platform-maintenance', '/run/whaledeck/maintenance', '/run/lock')


def require(value, message):
    if not value:
        raise RuntimeError(message)


def command(*args):
    return subprocess.run(args, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True).stdout


def write(path, raw, mode=0o600):
    path.write_bytes(raw)
    path.chmod(mode)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def runtime():
    units = command('systemctl', 'show', 'whaledeck-agent.service', 'whaledeck-maintenance.service',
        '--property=MainPID,ActiveState,InvocationID').splitlines()
    containers = [json.loads(line) for line in command('docker', 'ps', '--format', '{{json .}}').splitlines()]
    # No environments, mounts, endpoints, tokens or logs are read.
    return dict(units=units, containers=sorted((v['ID'], v['Image'], v['Status']) for v in containers
        if v['Names'].startswith('whaledeck-')))


def protected():
    files = [Path('/usr/local/libexec') / name for name in (*CONTROLS, 'whaledeck-platform-control-recovery.py')]
    files += [Path('/var/lib/whaledeck-agent/platform-maintenance') / name
        for name in ('deployment-revisions.json', 'control-recovery.json', 'previous-revision')]
    return {str(p): None if not os.path.lexists(p) else dict(sha256=digest(p), mode=p.lstat().st_mode,
        inode=p.lstat().st_ino, uid=p.lstat().st_uid) for p in files}


CHILD = '''import fcntl, hashlib, importlib.util, json, os, signal, subprocess, sys
from pathlib import Path
base=Path('/usr/local/libexec')
expected=json.loads((base/'qa-inodes.json').read_text())
for name, identity in expected.items():
    s=os.stat(name)
    assert [s.st_dev,s.st_ino]==identity, 'shadow mount identity mismatch'
if sys.argv[1]=='check':
    raise SystemExit(0)
spec=importlib.util.spec_from_file_location('recovery',base/'whaledeck-platform-control-recovery.py')
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
fd=m.lock_file()
os.dup2(fd,9)
if fd!=9: os.close(fd)
fcntl.flock(9,fcntl.LOCK_EX|fcntl.LOCK_NB)
m.main(['verify-lock'])
# A separately launched installer without inherited fd9 must fail before it
# opens any installation source or destination, even as root.
denied=subprocess.run(['/usr/bin/python3','-I','-B',str(base/'whaledeck-platform-control-recovery.py'),
    'install','/not-an-installation-source'],capture_output=True,text=True)
assert denied.returncode==1
assert json.loads(denied.stderr)['code']=='PLATFORM_CONTROL_RECOVERY_UNPROVEN'
op=json.loads((m.STATE/'deployment-revisions.json').read_text())['pending']
capture=m.Recovery().checkpoint(op)
assert m.install_sources('/not-read-during-rollback')==dict(installationMode='preserve',filesReplaced=0)
for name in m.CONTROLS:
    (base/name).write_bytes(b'synthetic older installer overwrite\\n')
    (base/name).chmod(0o750)
(m.STATE/'qa-ready.json').write_text(json.dumps(dict(capture=capture,pid=os.getpid())))
while True: signal.pause()
'''


def wait_for(predicate, seconds=20):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        if predicate():
            return
        time.sleep(0.1)
    raise RuntimeError('isolated unit observation timeout')


def properties(case):
    props = ['User=root', 'Group=root', 'UMask=0077', 'ProtectSystem=strict', 'ProtectHome=true',
        'PrivateTmp=true', 'NoNewPrivileges=true', 'RestrictAddressFamilies=AF_UNIX',
        'CapabilityBoundingSet=CAP_CHOWN CAP_DAC_OVERRIDE', 'TimeoutStartSec=30s', 'TimeoutStopSec=10s',
        'ReadWritePaths=' + ' '.join(TARGETS)]
    for name, target in zip(('libexec', 'state', 'public', 'locks'), TARGETS):
        props.append('BindPaths=' + str(case / name) + ':' + target)
    props.append('ExecStartPre=/usr/bin/python3 -I -B /usr/local/libexec/qa-child.py check')
    return [arg for prop in props for arg in ('--property', prop)]


def run_case(root, kind):
    case = root / kind
    case.mkdir(mode=0o700)
    for name in ('libexec', 'state', 'public', 'locks'):
        (case / name).mkdir(mode=0o700 if name == 'state' else 0o755)
        (case / name).chmod(0o700 if name == 'state' else 0o755)
    runner = case / 'libexec/whaledeck-platform-control-recovery.py'
    write(runner, (HOST / runner.name).read_bytes(), 0o750)
    require(digest(runner) == digest(HOST / runner.name), 'runner copy differs')
    originals = {name: ('synthetic trusted controller ' + name + '\n').encode() for name in CONTROLS}
    for name, raw in originals.items():
        write(case / 'libexec' / name, raw, 0o750)
    op = dict(action='rollback', revision='b' * 40, token=uuid.uuid4().hex)
    ledger = dict(schemaVersion=1, currentRevision='a' * 40, previousRevision='b' * 40, pending=op,
        lastCompleted=dict(action='update', revision='a' * 40, token=uuid.uuid4().hex))
    write(case / 'state/deployment-revisions.json', (json.dumps(ledger) + '\n').encode())
    ledger_sha = digest(case / 'state/deployment-revisions.json')
    sentinel = case / 'application-sentinel'
    write(sentinel, b'application sentinel: never modify or replay\n')
    sentinel_sha = digest(sentinel)
    write(case / 'libexec/qa-child.py', CHILD.encode(), 0o750)

    def save_inodes():
        values = {}
        for name, target in zip(('libexec', 'state', 'public', 'locks'), TARGETS):
            info = (case / name).stat()
            values[target] = [info.st_dev, info.st_ino]
        write(case / 'libexec/qa-inodes.json', json.dumps(values).encode())

    save_inodes()
    unit = 'whaledeck-control-qa-' + uuid.uuid4().hex + '.service'
    args = ['systemd-run', '--unit', unit, *properties(case)]
    if kind == 'poststop':
        args += ['--property', 'ExecStopPost=/usr/bin/python3 -I -B /usr/local/libexec/whaledeck-platform-control-recovery.py recover']
    args += ['/usr/bin/python3', '-I', '-B', '/usr/local/libexec/qa-child.py', 'pause']
    started = False
    try:
        command(*args)
        started = True
        wait_for(lambda: (case / 'state/qa-ready.json').exists())
        ready = json.loads((case / 'state/qa-ready.json').read_text())
        pid = int(command('systemctl', 'show', unit, '--property=MainPID', '--value').strip())
        require(pid == ready['pid'] and pid > 1, 'test unit PID mismatch')
        require(all((case / 'libexec' / n).read_bytes() != raw for n, raw in originals.items()), 'overwrite not reached')
        busy_unit = 'whaledeck-control-qa-' + uuid.uuid4().hex + '.service'
        command('systemd-run', '--unit', busy_unit, '--wait', '--property=Type=oneshot', *properties(case),
            '/usr/bin/python3', '-I', '-B', '/usr/local/libexec/whaledeck-platform-control-recovery.py', 'recover')
        require((case / 'state/control-recovery.json').exists() and
            not (case / 'state' / ('control-' + ready['capture']) / 'receipt.json').exists(), 'busy recovery wrote state')
        require(all((case / 'libexec' / n).read_bytes() != raw for n, raw in originals.items()), 'busy recovery restored controls')
        command('systemctl', 'kill', '--kill-whom=main', '--signal=SIGKILL', unit)
        wait_for(lambda: command('systemctl', 'show', unit, '--property=ActiveState', '--value').strip() in ('failed', 'inactive'))
        killed = command('systemctl', 'show', unit, '--property=Result,ExecMainCode,ExecMainStatus').splitlines()
        require('ExecMainStatus=9' in killed and 'Result=signal' in killed, 'actual SIGKILL not proved')
        recovery_unit = None
        if kind == 'volatile-loss':
            require((case / 'state/control-recovery.json').exists(), 'checkpoint lost before fresh recovery')
            # Recreate only the two exact shadow /run directories; original
            # entries are retained for forensic inspection, never remove /run.
            for name in ('public', 'locks'):
                (case / name).rename(case / (name + '-before-loss'))
                (case / name).mkdir(mode=0o755)
                (case / name).chmod(0o755)
            save_inodes()
            recovery_unit = 'whaledeck-control-qa-' + uuid.uuid4().hex + '.service'
            command('systemd-run', '--unit', recovery_unit, '--wait', '--property=Type=oneshot', *properties(case),
                '/usr/bin/python3', '-I', '-B', '/usr/local/libexec/whaledeck-platform-control-recovery.py', 'recover')
        capture = ready['capture']
        receipt = json.loads((case / 'state' / ('control-' + capture) / 'receipt.json').read_text())
        require(receipt == dict(schemaVersion=1, capture=capture, operation=op, outcome='interrupted',
            controlsRestored=True, applicationReplayed=False, automaticRollback=False), 'receipt mismatch')
        require(not (case / 'state/control-recovery.json').exists(), 'active checkpoint remains')
        for name, raw in originals.items():
            p = case / 'libexec' / name
            require(p.read_bytes() == raw and p.stat().st_uid == 0 and p.stat().st_mode & 0o777 == 0o750,
                'controller bytes or permissions not restored')
        require(digest(case / 'state/deployment-revisions.json') == ledger_sha and digest(sentinel) == sentinel_sha,
            'canonical ledger or application sentinel changed')
        status = json.loads((case / 'public/status.json').read_text())
        require(status['state'] == 'failed', 'interruption falsely reported healthy')
        result = dict(kind=kind, passed=True, unit=unit, recoveryUnit=recovery_unit, killed=killed,
            sourceSha256=digest(runner), receipt=receipt, ledgerUnchanged=True, sentinelUnchanged=True,
            fixedPathMountIdentityChecked=True, busyRecoverySkipped=True, rootInstallerWithoutLockRejected=True,
            rollbackInstallerPreservedRunner=True)
        write(case / 'result.json', (json.dumps(result, indent=2) + '\n').encode())
        return result
    finally:
        if started:
            state = command('systemctl', 'show', unit, '--property=ActiveState', '--value').strip()
            if state not in ('failed', 'inactive'):
                command('systemctl', 'stop', unit)
            subprocess.run(['systemctl', 'reset-failed', unit], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    require(sys.argv[1:] == ['--run'] and os.geteuid() == 0, 'requires explicit root --run')
    os.umask(0o077)
    require(not Path(TARGETS[1], 'control-recovery.json').exists(), 'live recovery pending; do not test')
    before_files, before_runtime = protected(), runtime()
    root = ROOT / 'deploy/artifacts' / ('platform-control-systemd-' + uuid.uuid4().hex)
    root.mkdir(mode=0o700)
    write(root / 'before.json', json.dumps(dict(files=before_files, runtime=before_runtime), indent=2).encode())
    results = []
    try:
        for kind in ('poststop', 'volatile-loss'):
            results.append(run_case(root, kind))
        after_files, after_runtime = protected(), runtime()
        # Docker human Status includes elapsed uptime; compare only stable IDs,
        # images and health suffix, retaining full output as evidence.
        stable = lambda r: (r['units'], [(i, img, s.endswith('(healthy)')) for i, img, s in r['containers']])
        require(before_files == after_files and stable(before_runtime) == stable(after_runtime), 'production changed during test')
        write(root / 'after.json', json.dumps(dict(files=after_files, runtime=after_runtime), indent=2).encode())
        result = dict(passed=True, syntheticLedger=True, unchangedProduction=True, cases=results,
            workstationRebooted=False, installed=False, applicationRollbackTested=False)
        write(root / 'result.json', (json.dumps(result, indent=2) + '\n').encode())
        print(json.dumps(dict(passed=True, cases=len(results), evidence=str(root), installed=False)))
    except BaseException:
        write(root / 'failed.json', json.dumps(dict(passed=False, casesFinished=len(results))).encode())
        print(json.dumps(dict(passed=False, evidence=str(root))), file=sys.stderr)
        raise


if __name__ == '__main__':
    main()
