#!/usr/bin/env python3
"""Read-only raw-data preservation and live aggregation state. No production DDL or mutation."""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
from validate_metric_partition_runtime import query, digest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['capture', 'verify', 'progress'])
    parser.add_argument('evidence', type=Path, nargs='?')
    args = parser.parse_args()
    if args.mode != 'progress' and args.evidence is None:
        raise RuntimeError('Capture/verify requires a private evidence file')
    if args.mode == 'capture':
        now = dt.datetime.now(dt.timezone.utc)
        start, end = now - dt.timedelta(days=13), now - dt.timedelta(minutes=2)
        evidence = {'start': start.isoformat(), 'end': end.isoformat(),
                    'expected': digest('metric_samples', 'sampled_at_utc', start.isoformat(), end.isoformat())}
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        descriptor = os.open(args.evidence, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'w') as stream:
            json.dump(evidence, stream)
        print(json.dumps({'rawMetricEvidence': 'captured', 'count': evidence['expected']['count'], 'rowContents': 'not-emitted'}))
        return
    if args.mode == 'verify':
        evidence = json.loads(args.evidence.read_text())
        if digest('metric_samples', 'sampled_at_utc', evidence['start'], evidence['end']) != evidence['expected']:
            raise RuntimeError('Frozen raw metric identities/values/qualities changed')
        trigger = query("""SELECT json_build_object('count',count(*),'invoker',bool_and(NOT p.prosecdef),
            'owner',bool_and(pg_get_userbyid(p.proowner)='whaledeck'),'statementLevel',bool_and((t.tgtype & 1)=0))
            FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid
            WHERE t.tgrelid='metric_samples'::regclass AND t.tgname IN ('wd_metric_insert','wd_metric_update');""")
        if trigger != {'count': 2, 'invoker': True, 'owner': True, 'statementLevel': True}:
            raise RuntimeError('Metric dirty transition triggers/permissions differ from the approved contract')
        print(json.dumps({'rawMetricPreservation': 'passed', 'count': evidence['expected']['count'], 'transitionTriggers': 'passed'}))
    state = query("""SELECT json_build_object('pendingMinutes',(SELECT count(*) FROM metric_aggregation_pending),
        'completedHours',(SELECT count(*) FROM metric_aggregation_windows WHERE completed_through_utc=window_start_utc+INTERVAL '1 hour'),
        'hourRollups',(SELECT count(*) FROM metric_rollups WHERE resolution='1h'),
        'nextHour',(SELECT next_hour_utc FROM metric_aggregation_state WHERE key='retained-history-v1'),
        'backfillComplete',coalesce((SELECT next_hour_utc>=date_bin(INTERVAL '1 hour',now(),TIMESTAMPTZ '1970-01-01') FROM metric_aggregation_state WHERE key='retained-history-v1'),false));""")
    print(json.dumps({'metricAggregationProgress': state}))


if __name__ == '__main__':
    main()
