#!/usr/bin/env python3
"""Run only fixed telemetry maintenance schedules and verify their terminal history."""
from __future__ import annotations

import os
import time
import uuid

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def main() -> int:
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    api = Api(base, session)
    for kind in ('MetricsRollup','RetentionCleanup'):
        created = request(api, 'POST', 'schedules', payload={
            'taskType':kind,'name':f'wdv-metric-maintenance-{uuid.uuid4().hex[:12]}',
            'scheduleKind':'Cron','scheduleExpression':'17 3 * * *','timezone':'UTC',
            'parametersJson':'{}','concurrencyPolicy':'Forbid','timeoutSeconds':300,'isEnabled':False,'expectedVersion':None
        }).json()
        try:
            request(api,'POST',f"schedules/{created['id']}/run",payload={},expected=202)
            deadline = time.monotonic()+90
            while time.monotonic()<deadline:
                runs = request(api,'GET',f"schedules/runs?scheduleId={created['id']}&take=20").json()
                terminal = next((run for run in runs if run['scheduleId']==created['id'] and run.get('result')=='Succeeded'),None)
                if terminal is not None:
                    if not terminal.get('startedAtUtc') or not terminal.get('completedAtUtc') or terminal['completedAtUtc']<terminal['startedAtUtc']:
                        raise RuntimeError('Completed internal maintenance has invalid timestamps')
                    job = request(api,'GET',f"jobs/{terminal['jobId']}").json()
                    if job['state']!='Succeeded' or job['phase']!='Completed':
                        raise RuntimeError('Schedule history disagrees with its task')
                    print(f'{kind}: actual Worker execution, completed job and terminal run history passed')
                    break
                time.sleep(3)
            else:
                raise RuntimeError(f'{kind} did not complete within 90 seconds')
        finally:
            current = next(item for item in request(api,'GET','schedules').json() if item['id']==created['id'])
            request(api,'DELETE',f"schedules/{created['id']}?version={current['version']}",expected=204)
    print('Metric maintenance schedules passed; only the two uniquely named test schedules were deleted')
    return 0


if __name__=='__main__':
    raise SystemExit(main())
