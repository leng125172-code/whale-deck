#!/usr/bin/env python3
"""Rehearse exact EF SQL in a disposable PostgreSQL database, never production DDL."""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import subprocess
import time
import uuid
from pathlib import Path

CONTAINER = 'database-platform-postgres'


def sql(database: str, statement: str, role: str | None = None, *, failure: str | None = None, transaction: bool = True):
    if not re.fullmatch(r'wdv_metric_[a-f0-9]{12}|postgres|whaledeck', database):
        raise RuntimeError('Unexpected database scope')
    prefix = f'SET ROLE {role};\n' if role else ''
    body = prefix + (f'BEGIN;\n{statement}\nCOMMIT;' if transaction else statement)
    result = subprocess.run(['docker', 'exec', '-i', '-u', 'postgres', CONTAINER, 'psql', '-X', '-t', '-A', '-v', 'ON_ERROR_STOP=1', '-d', database],
                            input=body, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=330)
    if failure is not None:
        if result.returncode == 0 or failure not in result.stderr:
            raise RuntimeError(f'Expected PostgreSQL rejection was not observed: {failure}')
        return None
    if result.returncode != 0:
        raise RuntimeError(f'Isolated PostgreSQL SQL failed: {result.stderr[-1800:]}')
    values = [line for line in result.stdout.splitlines() if line and line not in {'BEGIN', 'COMMIT', 'SET'}]
    return values[-1] if values else None


def scalar(database: str, expression: str, role: str | None = None):
    return json.loads(sql(database, f'SELECT to_json(({expression}));', role))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--previous', type=Path, required=True)
    parser.add_argument('--up', type=Path, required=True)
    parser.add_argument('--down', type=Path, required=True)
    args = parser.parse_args()
    token = uuid.uuid4().hex[:12]
    database, role = f'wdv_metric_{token}', f'wdv_metric_owner_{token}'
    reader = f'wdv_metric_reader_{token}'
    previous, up, down = (path.read_text(encoding='utf-8-sig') for path in (args.previous, args.up, args.down))
    if any(not any(marker in source for marker in ('BEGIN;', 'START TRANSACTION;')) or '__EFMigrationsHistory' not in source for source in (previous, up, down)):
        raise RuntimeError('Rehearsal requires the exact EF-generated transactional scripts')
    now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
    cutoff = now - dt.timedelta(days=14)
    stamp = now.isoformat()
    sql('postgres', f'CREATE ROLE {role} NOLOGIN;', transaction=False)
    created = False
    try:
        sql('postgres', f'CREATE DATABASE {database} OWNER {role};', transaction=False)
        created = True
        sql('postgres', f'CREATE ROLE {reader} NOLOGIN;', transaction=False)
        sql(database, previous, role, transaction=False)
        sql(database, up, failure='must run as the existing table owner', transaction=False)
        if scalar(database, "SELECT count(*) FROM pg_class WHERE oid IN ('metric_samples'::regclass,'metric_rollups'::regclass) AND relkind='r'", role) != 2:
            raise RuntimeError('Rejected migration changed the original tables')
        sql(database, up, role, transaction=False)
        sql(database, down, role, transaction=False)
        # Null/warmup values, exact retention boundary, leap day, month and future routing.
        times = [now, now-dt.timedelta(days=30), cutoff-dt.timedelta(seconds=1), cutoff,
                 cutoff+dt.timedelta(seconds=1), now+dt.timedelta(days=1), dt.datetime(2024, 2, 29, tzinfo=dt.timezone.utc)]
        tuples = ','.join(f"('00000000-0000-0000-0000-000000000011','{value.isoformat()}',{i if i else 'NULL'},'{ 'Good' if i else 'Warmup'}')" for i, value in enumerate(times))
        sql(database, f"INSERT INTO metric_samples VALUES {tuples}; INSERT INTO metric_rollups SELECT series_id, sampled_at_utc, '1m', 1,2,1.5,3,2 FROM metric_samples; CREATE TABLE expected_samples AS TABLE metric_samples; CREATE TABLE expected_rollups AS TABLE metric_rollups;", role)
        sql(database, f'GRANT SELECT ON metric_samples, metric_rollups TO {reader};', role)
        sql(database, up, role, transaction=False)
        for table in ('metric_samples', 'metric_rollups'):
            if not scalar(database, f"has_table_privilege('{reader}', '{table}', 'SELECT')", role) or scalar(database, f"has_table_privilege('{reader}', '{table}', 'INSERT')", role):
                raise RuntimeError('Migration changed least-privilege reader grants')
        for table in ('samples', 'rollups'):
            if scalar(database, f'EXISTS (SELECT * FROM expected_{table} EXCEPT ALL SELECT * FROM metric_{table}) OR (SELECT count(*) FROM expected_{table}) <> (SELECT count(*) FROM metric_{table})', role):
                raise RuntimeError('Migration did not preserve exact historical rows')
        sql(database, down, role, transaction=False)
        sql(database, up, role, transaction=False)
        for table in ('metric_samples', 'metric_rollups'):
            if not scalar(database, f"has_table_privilege('{reader}', '{table}', 'SELECT')", role):
                raise RuntimeError('Downgrade/re-upgrade lost reader grants')
        if scalar(database, "SELECT count(*) FROM pg_partitioned_table WHERE partrelid IN ('metric_samples'::regclass,'metric_rollups'::regclass)", role) != 2:
            raise RuntimeError('Both metric tables are not true declarative range parents')
        sql(database, "INSERT INTO metric_samples SELECT * FROM expected_samples LIMIT 1;", role, failure='duplicate key')
        before = scalar(database, "SELECT count(*) FROM pg_inherits WHERE inhparent IN ('metric_samples'::regclass,'metric_rollups'::regclass)", role)
        dropped = scalar(database, f"public.wd_maintain_metric_partitions('{stamp}'::timestamptz)", role)
        if dropped < 2:
            raise RuntimeError('Expired daily partitions were not dropped')
        for table, column in (('samples', 'sampled_at_utc'), ('rollups', 'window_start_utc')):
            expected = f"SELECT * FROM expected_{table} WHERE {column} >= '{cutoff.isoformat()}'::timestamptz"
            if scalar(database, f'EXISTS (({expected}) EXCEPT ALL SELECT * FROM metric_{table}) OR EXISTS (SELECT * FROM metric_{table} EXCEPT ALL ({expected}))', role):
                raise RuntimeError('Rolling 14-day retention boundary is not exact')
        after = scalar(database, "SELECT count(*) FROM pg_inherits WHERE inhparent IN ('metric_samples'::regclass,'metric_rollups'::regclass)", role)
        if before <= after or after != 34 or scalar(database, f"public.wd_maintain_metric_partitions('{stamp}'::timestamptz)", role) != 0:
            raise RuntimeError('Daily partitions are not bounded/idempotent')
        sql(database, f"SET TIME ZONE 'Asia/Shanghai'; SELECT public.wd_maintain_metric_partitions('{now.astimezone(dt.timezone(dt.timedelta(hours=8))).isoformat()}');", role)
        # A DEFAULT or misleading name cannot turn maintenance into arbitrary table deletion.
        sql(database, 'CREATE TABLE wd_metric_unmanaged PARTITION OF metric_samples DEFAULT;', role)
        sql(database, f"SELECT public.wd_maintain_metric_partitions('{stamp}');", role, failure='Unmanaged metric partition')
        if not scalar(database, "to_regclass('wd_metric_unmanaged') IS NOT NULL", role):
            raise RuntimeError('Unmanaged partition was removed')
        sql(database, 'DROP TABLE wd_metric_unmanaged;', role)
        wrong_day, real_day = (now.date()-dt.timedelta(days=40)), (now.date()+dt.timedelta(days=10))
        wrong_name = f'metric_samples_p{wrong_day:%Y%m%d}'
        sql(database, f"CREATE TABLE {wrong_name} PARTITION OF metric_samples FOR VALUES FROM ('{real_day} 00:00:00+00') TO ('{real_day+dt.timedelta(days=1)} 00:00:00+00');", role)
        sql(database, f"SELECT public.wd_maintain_metric_partitions('{stamp}');", role, failure='name and bounds disagree')
        sql(database, f'DROP TABLE {wrong_name};', role)
        collision_day = now.date()+dt.timedelta(days=3)
        collision = f'metric_rollups_p{collision_day:%Y%m%d}'
        sql(database, f'CREATE TABLE {collision} (sentinel integer); INSERT INTO {collision} VALUES (77);', role)
        later = (now+dt.timedelta(days=1)).isoformat()
        sql(database, f"SELECT public.wd_maintain_metric_partitions('{later}');", role, failure='collides with an unrelated relation')
        if scalar(database, f'SELECT sentinel FROM {collision}', role) != 77:
            raise RuntimeError('Unrelated relation was changed')
        sql(database, f'DROP TABLE {collision};', role)
        # Recovery after a long downtime must prepare future partitions before native writes.
        future = now+dt.timedelta(days=40)
        scalar(database, f"public.wd_maintain_metric_partitions('{future.isoformat()}')", role)
        sql(database, f"INSERT INTO metric_samples VALUES ('00000000-0000-0000-0000-000000000022','{future.isoformat()}',9,'Good');", role)
        if scalar(database, f"SELECT tableoid::regclass::text FROM metric_samples WHERE series_id='00000000-0000-0000-0000-000000000022'", role) != f'metric_samples_p{future:%Y%m%d}':
            raise RuntimeError('Future sample routed to the wrong day')
        if scalar(database, "SELECT count(*) FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a WHERE p.oid='wd_maintain_metric_partitions(timestamptz)'::regprocedure AND a.grantee=0", role) != 0:
            raise RuntimeError('Maintenance function execution leaked to PUBLIC')
        if scalar(database, "SELECT prosecdef FROM pg_proc WHERE oid='wd_maintain_metric_partitions(timestamptz)'::regprocedure", role):
            raise RuntimeError('Maintenance unexpectedly uses definer privileges')
        if scalar(database, f"has_function_privilege('{reader}', 'wd_maintain_metric_partitions(timestamptz)', 'EXECUTE')", role):
            raise RuntimeError('Read-only role can execute maintenance')
        locker = subprocess.Popen(['docker','exec','-i','-u','postgres',CONTAINER,'psql','-X','-t','-A','-d',database],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            locker.stdin.write(f'SET ROLE {role}; BEGIN; SELECT pg_advisory_xact_lock(82592831620704); SELECT pg_sleep(4); COMMIT;')
            locker.stdin.close()
            locker.stdin = None
            deadline = time.monotonic()+3
            while time.monotonic() < deadline:
                if scalar(database, "EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND granted AND database=(SELECT oid FROM pg_database WHERE datname=current_database()))", role):
                    break
            else:
                raise RuntimeError('The concurrent advisory lock was not acquired')
            started = time.monotonic()
            if scalar(database, f"public.wd_maintain_metric_partitions('{future.isoformat()}')", role) != -1 or time.monotonic()-started > 2:
                raise RuntimeError('Concurrent maintenance did not skip without blocking')
        finally:
            locker.communicate(timeout=8)
        print('Isolated PostgreSQL partition rehearsal passed: full empty-schema migration chain, exact seeded upgrade/downgrade, daily routing, unique keys, UTC and strict 14-day retention, real partition drops, idempotence, unmanaged/bounds/collision failure-closed and long-downtime recovery; application-owned role, no production DDL')
        return 0
    finally:
        if created:
            sql('postgres', f'DROP DATABASE {database} WITH (FORCE);', transaction=False)
        sql('postgres', f'DROP ROLE {role};', transaction=False)
        sql('postgres', f'DROP ROLE IF EXISTS {reader};', transaction=False)


if __name__ == '__main__':
    raise SystemExit(main())
