"""Durable, public-only native validation status; never persist process output."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import tempfile
import uuid


class NativeValidationStatus:
    allowed = frozenset(('state', 'stage', 'sdkImageId', 'containerName', 'executedTests',
        'passedTests', 'cleanupConfirmed', 'failureType', 'finishedAtUtc'))

    def __init__(self, root: Path, suite: str, source_sha: str):
        if suite not in ('capacity', 'metrics', 'alerts', 'notifications'):
            raise ValueError('Unknown native suite')
        if not re.fullmatch(r'[a-f0-9]{40}', source_sha):
            raise ValueError('Invalid public source revision')
        root.mkdir(parents=True, exist_ok=True)
        self.path = root / (suite + '-' + uuid.uuid4().hex + '.json')
        self.data = {'suite': suite, 'sourceSha': source_sha, 'runnerPid': os.getpid(),
            'startedAtUtc': self.now(), 'state': 'Preparing', 'cleanupConfirmed': False}
        self.update()

    @staticmethod
    def now():
        return datetime.now(timezone.utc).isoformat()

    def update(self, **fields):
        if fields.keys() - self.allowed:
            raise ValueError('Non-public native status field')
        for key, value in fields.items():
            if not isinstance(value, (str, bool, int)) or isinstance(value, str) and (
                    len(value) > 160 or any(ord(char) < 32 for char in value)):
                raise ValueError('Invalid native status value')
            if key == 'state' and value not in ('Preparing', 'Running', 'Passed', 'Failed'):
                raise ValueError('Invalid native state')
        next_data = {**self.data, **fields}
        if next_data['state'] == 'Passed' and not (
                next_data['cleanupConfirmed'] and next_data.get('executedTests') == 1
                and next_data.get('passedTests') == 1):
            raise ValueError('Cannot report acceptance before execution and cleanup')
        descriptor, temporary = tempfile.mkstemp(prefix='.native-status-', dir=self.path.parent)
        try:
            with os.fdopen(descriptor, 'w', encoding='utf-8') as stream:
                json.dump(next_data, stream, sort_keys=True)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.path)
            self.data = next_data
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
