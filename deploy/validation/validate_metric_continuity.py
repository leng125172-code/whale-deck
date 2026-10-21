#!/usr/bin/env python3
"""Read-only deployed host telemetry observation. Never fill gaps or mutate metric history."""
import argparse
import datetime as dt
import json
import math
import subprocess
import time

KINDS = {'network.receive', 'network.send', 'disk.read', 'disk.write'}


def query(sql, **parameters):
    args = ['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres',
            'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck']
    for key, value in parameters.items():
        args += ['-v', key + '=' + value]
    result = subprocess.run(args, input=sql, text=True, capture_output=True, timeout=30, check=False)
    if result.returncode or len(result.stdout) > 65_536:
        raise RuntimeError('METRIC_CONTINUITY_QUERY_FAILED')
    return json.loads(result.stdout)


def evaluate(rows, expected, duration):
    if not expected or {item[0] for item in expected} != KINDS:
        raise ValueError('METRIC_CONTINUITY_BASELINE_MISSING')
    seen = set()
    minimum = math.floor(duration / 6) - 2
    for row in rows:
        key = (row['kind'], row['device'])
        if key not in expected:
            continue
        if key in seen:
            raise ValueError('METRIC_CONTINUITY_DUPLICATE_SERIES')
        seen.add(key)
        if (row['count'] < minimum or row['maxGapSeconds'] is None or row['maxGapSeconds'] > 9
                or row['leadingSeconds'] > 9 or row['trailingSeconds'] > 9 or row['notGood'] != 0):
            raise ValueError('METRIC_CONTINUITY_GAP_OR_QUALITY_FAILURE')
    if seen != expected:
        raise ValueError('METRIC_CONTINUITY_DEVICE_LOST')
    return {'series': len(seen), 'observedSeconds': duration,
            'minimumSamplesPerSeries': min(row['count'] for row in rows if (row['kind'], row['device']) in seen),
            'maximumGapSeconds': max(row['maxGapSeconds'] for row in rows if (row['kind'], row['device']) in seen),
            'gapsOverNineSeconds': 0, 'syntheticSamples': 0}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--seconds', type=int, default=420, choices=range(180, 901))
    args = parser.parse_args()
    baseline = query("""
        SELECT json_build_object('startedAtUtc', statement_timestamp(), 'devices', COALESCE(json_agg(
          json_build_array(s.metric_kind,s.dimensions_json->>'DeviceId')), '[]'::json))
        FROM metric_series s JOIN managed_resources r ON r.id=s.resource_id
        WHERE r.resource_type='Host' AND r.external_id='local'
          AND s.metric_kind IN ('network.receive','network.send','disk.read','disk.write')
          AND s.last_seen_at_utc > statement_timestamp()-INTERVAL '15 seconds';
    """)
    expected = {tuple(item) for item in baseline['devices']}
    if {item[0] for item in expected} != KINDS:
        raise ValueError('METRIC_CONTINUITY_BASELINE_MISSING')
    start = dt.datetime.fromisoformat(baseline['startedAtUtc'])
    end = start + dt.timedelta(seconds=args.seconds)
    print(json.dumps({'metricContinuity': 'observing', 'seconds': args.seconds, 'series': len(expected)}), flush=True)
    deadline = time.monotonic() + args.seconds + 15  # Allow two ingestion ticks to commit the end of the window.
    next_progress = time.monotonic() + 60
    while time.monotonic() < deadline:
        time.sleep(2)
        if time.monotonic() >= next_progress:
            print(json.dumps({'metricContinuity': 'observing', 'remainingSeconds': max(0, round(deadline-time.monotonic()))}), flush=True)
            next_progress += 60
    rows = query("""
        WITH points AS (
          SELECT s.id,s.metric_kind AS kind,s.dimensions_json->>'DeviceId' AS device,m.sampled_at_utc AS stamp,m.quality,
            EXTRACT(EPOCH FROM m.sampled_at_utc-lag(m.sampled_at_utc) OVER(PARTITION BY s.id ORDER BY m.sampled_at_utc)) AS gap
          FROM metric_samples m JOIN metric_series s ON s.id=m.series_id JOIN managed_resources r ON r.id=s.resource_id
          WHERE r.resource_type='Host' AND r.external_id='local'
            AND s.metric_kind IN ('network.receive','network.send','disk.read','disk.write')
            AND m.sampled_at_utc >= :'start'::timestamptz AND m.sampled_at_utc <= :'end'::timestamptz
        ), summary AS (
          SELECT id,kind,device,count(*) AS count,max(gap) AS "maxGapSeconds",
            EXTRACT(EPOCH FROM min(stamp)-:'start'::timestamptz) AS "leadingSeconds",
            EXTRACT(EPOCH FROM :'end'::timestamptz-max(stamp)) AS "trailingSeconds",
            count(*) FILTER(WHERE quality<>'Good') AS "notGood"
          FROM points GROUP BY id,kind,device
        ) SELECT COALESCE(json_agg(summary),'[]'::json) FROM summary;
    """, start=start.isoformat(), end=end.isoformat())
    print(json.dumps({'metricContinuity': 'passed', **evaluate(rows, expected, args.seconds)}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'metricContinuity': 'failed', 'failureType': type(error).__name__}), flush=True)
        raise SystemExit(1) from None
