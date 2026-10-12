#!/usr/bin/env python3
"""Read-only native PostgreSQL and Agent telemetry acceptance; no credentials printed."""

from __future__ import annotations

import datetime as dt
import json
import math
import os
import subprocess

from validate_oidc import authenticate, required_environment

KINDS = ["network.receive", "network.send", "disk.read", "disk.write", "disk.read.iops", "disk.write.iops", "disk.read.latency", "disk.write.latency"]


def query(sql: str, **parameters: str):
    command = ["docker", "exec", "-i", "database-platform-postgres", "sh", "-c",
               'exec psql -X -U "$POSTGRES_USER" -d whaledeck -t -A -v ON_ERROR_STOP=1 "$@"', "sh"]
    for key, value in parameters.items():
        command.extend(["-v", f"{key}={value}"])
    result = subprocess.run(command, input=sql, check=True, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=60)
    return json.loads(result.stdout.strip())


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    checked = 0
    observed_disk_kinds: set[str] = set()
    for window, resolution, count in [("live", 6, None), ("1h", 30, 121), ("24h", 300, 289), ("7d", 1800, 337)]:
        response = session.get(f"{base}/api/v1/metrics", params=[*(('kinds', kind) for kind in KINDS), ('window', window), ('take', '60')], timeout=60)
        response.raise_for_status()
        series = response.json()
        if not series:
            raise RuntimeError("No telemetry series returned")
        for item in series:
            device = item["deviceId"]
            kind = item["kind"]
            if device.startswith(("veth", "docker", "br-", "loop", "dm-")):
                raise RuntimeError("Virtual/partition telemetry leaked into physical-device totals")
            if item["resolutionSeconds"] != resolution:
                raise RuntimeError("Metric resolution does not match the requested window")
            points = item["points"]
            if count is None:
                if len(points) > 60 or any(point["quality"] != "Good" and point["value"] is not None for point in points):
                    raise RuntimeError("Live point bound or quality handling is incorrect")
                observed_disk_kinds.update([kind] if kind.startswith("disk.") else [])
                continue
            if len(points) != count:
                raise RuntimeError("Historical window was truncated")
            timestamps = [dt.datetime.fromisoformat(point["sampledAtUtc"].replace("Z", "+00:00")) for point in points]
            if any((right - left).total_seconds() != resolution for left, right in zip(timestamps, timestamps[1:])):
                raise RuntimeError("History buckets are not densely aligned")
            valid = [point for point in points if point["value"] is not None]
            if not valid:
                if kind.endswith(".latency") and any(point["quality"] == "Idle" for point in points):
                    continue
                raise RuntimeError("History unexpectedly lacks all real observations")
            # Pick a completed populated bucket so concurrent sampling cannot alter its contents.
            selected = next((point for point in reversed(valid) if point != points[-1]), valid[0])
            expected = query("""
                SELECT to_json(CASE WHEN :'kind' IN ('disk.read.latency','disk.write.latency') THEN
                    SUM(sample.value_double * weight.value_double) / NULLIF(SUM(weight.value_double),0)
                    ELSE AVG(sample.value_double) END)
                FROM metric_samples sample JOIN metric_series source ON source.id=sample.series_id
                LEFT JOIN metric_series weights ON weights.resource_id=source.resource_id AND weights.dimensions_json=source.dimensions_json
                    AND weights.metric_kind=CASE source.metric_kind WHEN 'disk.read.latency' THEN 'disk.read.iops' WHEN 'disk.write.latency' THEN 'disk.write.iops' ELSE NULL END
                LEFT JOIN metric_samples weight ON weight.series_id=weights.id AND weight.sampled_at_utc=sample.sampled_at_utc AND weight.quality='Good'
                WHERE source.metric_kind=:'kind' AND source.dimensions_json->>'DeviceId'=:'device'
                    AND sample.sampled_at_utc>=:'start'::timestamptz AND sample.sampled_at_utc<:'start'::timestamptz+make_interval(secs=>:'resolution'::int)
                    AND sample.sampled_at_utc<=:'end'::timestamptz AND sample.quality='Good';
                """, kind=kind, device=device, start=selected["sampledAtUtc"], end=item["windowEndUtc"], resolution=str(resolution))
            if expected is None or not math.isclose(selected["value"], expected, rel_tol=1e-9, abs_tol=1e-9):
                raise RuntimeError("API history does not match native PostgreSQL aggregation")
            checked += 1
        if window == "7d" and not any(point["quality"] == "Missing" and point["value"] is None for item in series for point in item["points"]):
            raise RuntimeError("Uncollected historical periods were not left empty")
        print(f"Telemetry window {window}: {len(series)} real series, bounded/aligned buckets verified")
    required = {"disk.read", "disk.write", "disk.read.iops", "disk.write.iops", "disk.read.latency", "disk.write.latency"}
    if not required.issubset(observed_disk_kinds):
        raise RuntimeError("Agent disk IOPS/latency capability did not produce persisted series")
    for parameters in [dict(kinds='disk.read', window='14d'), dict(kinds='unapproved.metric')]:
        response = session.get(f"{base}/api/v1/metrics", params=parameters, timeout=30)
        if response.status_code != 400 or not response.json().get("traceId"):
            raise RuntimeError("Invalid history queries did not return bounded Problem Details")
    print(f"Historical telemetry acceptance passed: {checked} buckets independently compared with PostgreSQL; throughput, IOPS, latency, gaps and invalid-query rejection verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
