#!/usr/bin/env python3
"""Acceptance-only SIGKILL capsule: exact temporary job/process; never exposed via API."""
from __future__ import annotations

import json
import os
import re
import signal
import sys
from pathlib import Path


def matches_process(cgroup: str, command: bytes, start: str, expected_start: str,
                    slug: str, job: str) -> bool:
    if not re.fullmatch(r'wdv-backup-[a-f0-9]{12}', slug) or not re.fullmatch(r'[a-f0-9]{32}', job):
        return False
    unit = 'whaledeck-application-backup-' + job + '.service'
    fields = command.rstrip(b'\0').split(b'\0')
    return (start.isdecimal() and start == expected_start and
        any(line.split(':', 2)[-1].rstrip('/').split('/')[-1] == unit for line in cgroup.splitlines()) and
        len(fields) == 8 and fields[:6] == [b'/usr/bin/python3',
            b'/usr/local/libexec/whaledeck-application-backup.py', b'run', slug.encode(), job.encode(), b'quiesced'] and
        re.fullmatch(rb'[a-f0-9]{64}', fields[6]) is not None and
        fields[7].isdigit() and 51 <= int(fields[7]) <= 99)


def kill_bound_process(pid: int, start: str, slug: str, job: str) -> None:
    if pid <= 1 or os.geteuid() != 0:
        raise RuntimeError('Fault capsule requires an exact non-init PID and root UID')
    # Open first; a reused numeric PID cannot redirect the signal after validation.
    descriptor = os.pidfd_open(pid, 0)
    try:
        process = Path('/proc') / str(pid)
        actual_start = process.joinpath('stat').read_text().rsplit(')', 1)[1].split()[19]
        if not matches_process(process.joinpath('cgroup').read_text(), process.joinpath('cmdline').read_bytes(),
                               actual_start, start, slug, job):
            raise RuntimeError('Exact backup service/process identity was not proved; no signal sent')
        signal.pidfd_send_signal(descriptor, signal.SIGKILL, None, 0)
    finally:
        os.close(descriptor)


if __name__ == '__main__':
    if len(sys.argv) != 5:
        raise SystemExit('Usage: capsule PID START_TICKS TEMP_SLUG JOB_HEX')
    kill_bound_process(int(sys.argv[1]), *sys.argv[2:])
    print(json.dumps({'boundBackupSignal': 'SIGKILL'}))
