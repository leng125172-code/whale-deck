#!/usr/bin/env python3
"""Capture/read only metric migration evidence; never performs live DDL or cleanup."""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
from pathlib import Path


def query(statement: str):
    result = subprocess.run(['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-t', '-A', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck'],
                            input="BEGIN READ ONLY; SET LOCAL TIME ZONE 'UTC'; SET LOCAL statement_timeout='60s';\n"+statement+'\nCOMMIT;',
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=75, check=True)
    values = [line for line in result.stdout.splitlines() if line and line not in {'BEGIN', 'COMMIT', 'SET'}]
    return json.loads('\n'.join(values))


def digest(table: str, column: str, start: str, end: str):
    # Both table identifiers and windows come from this fixed script, not HTTP/user input.
    if table not in {'metric_samples', 'metric_rollups'} or column not in {'sampled_at_utc', 'window_start_utc'}:
        raise RuntimeError('Unexpected relation scope')
    begin, finish = dt.datetime.fromisoformat(start), dt.datetime.fromisoformat(end)
    return query(f"SELECT json_build_object('count',count(*),'digest',coalesce(sum(hashtextextended(row(t.*)::text,0)::numeric),0)::text) FROM {table} t WHERE {column}>='{begin.isoformat()}'::timestamptz AND {column}<'{finish.isoformat()}'::timestamptz;")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['capture', 'verify'])
    parser.add_argument('evidence', type=Path)
    args = parser.parse_args()
    if args.mode == 'capture':
        now = dt.datetime.now(dt.timezone.utc)
        samples_end, rollups_end = now-dt.timedelta(minutes=2), now-dt.timedelta(hours=3)
        start = now-dt.timedelta(days=13)
        summary = {}
        for table, column, end in (('metric_samples','sampled_at_utc',samples_end),('metric_rollups','window_start_utc',rollups_end)):
            summary[table] = {'start':start.isoformat(),'end':end.isoformat(),'expected':digest(table,column,start.isoformat(),end.isoformat())}
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        descriptor = os.open(args.evidence, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'w') as file:
            json.dump(summary, file)
        sizes = query("SELECT json_agg(json_build_object('table',relname,'owner',pg_get_userbyid(relowner),'estimatedRows',reltuples::bigint,'bytes',pg_total_relation_size(oid))) FROM pg_class WHERE oid IN ('metric_samples'::regclass,'metric_rollups'::regclass);")
        print(f'Pre-migration read-only evidence captured: {sizes}; immutable sample/rollup windows are stored privately; row contents and secrets are not emitted')
        return 0
    before = json.loads(args.evidence.read_text())
    for table, column in (('metric_samples','sampled_at_utc'),('metric_rollups','window_start_utc')):
        window = before[table]
        if digest(table,column,window['start'],window['end']) != window['expected']:
            raise RuntimeError(f'Immutable metric window changed during migration: {table}')
    parents = query("SELECT json_agg(json_build_object('table',c.relname,'kind',c.relkind,'owner',pg_get_userbyid(c.relowner),'key',pg_get_partkeydef(c.oid),'children',(SELECT count(*) FROM pg_inherits WHERE inhparent=c.oid))) FROM pg_class c WHERE c.oid IN ('metric_samples'::regclass,'metric_rollups'::regclass);")
    if len(parents) != 2 or any(row['kind'] != 'p' or row['owner'] != 'whaledeck' or not 34//2 <= row['children'] <= 18 for row in parents):
        raise RuntimeError('Live daily partitions, owner or bounded horizon are incorrect')
    capability = query("SELECT json_build_object('definer',prosecdef,'owner',pg_get_userbyid(proowner),'publicExecute',coalesce((SELECT bool_or(grantee=0 AND privilege_type='EXECUTE') FROM aclexplode(proacl)),false)) FROM pg_proc WHERE oid='public.wd_maintain_metric_partitions(timestamptz)'::regprocedure;")
    if capability['definer'] or capability['owner'] != 'whaledeck' or capability['publicExecute']:
        raise RuntimeError('Live maintenance permissions are incorrect')
    index = query("SELECT json_build_object('method',a.amname,'valid',i.indisvalid,'children',(SELECT count(*) FROM pg_inherits WHERE inhparent=c.oid)) FROM pg_class c JOIN pg_index i ON i.indexrelid=c.oid JOIN pg_am a ON a.oid=c.relam WHERE c.oid='public.\"IX_metric_samples_sampled_at_utc_brin\"'::regclass;")
    if index['method']!='brin' or not index['valid'] or index['children']!=17:
        raise RuntimeError('Live inherited time BRIN index is invalid or incomplete')
    plan_end = dt.datetime.now(dt.timezone.utc)
    plan_start = plan_end-dt.timedelta(hours=2)
    plan = query(f"EXPLAIN (FORMAT JSON) SELECT value_double FROM metric_samples WHERE sampled_at_utc >= '{plan_start.isoformat()}'::timestamptz AND sampled_at_utc < '{plan_end.isoformat()}'::timestamptz ORDER BY sampled_at_utc DESC LIMIT 10;")
    relations = set()
    def visit(node):
        if isinstance(node, dict):
            if 'Relation Name' in node: relations.add(node['Relation Name'])
            for value in node.values(): visit(value)
        elif isinstance(node, list):
            for value in node: visit(value)
    visit(plan)
    today = dt.datetime.now(dt.timezone.utc).date()
    allowed = {f'metric_samples_p{day:%Y%m%d}' for day in (today,today-dt.timedelta(days=1))}
    if not relations or not relations.issubset(allowed):
        raise RuntimeError(f'Live time-range partition pruning failed: {relations}')
    print(f'Live read-only partition acceptance passed: unchanged immutable row counts/digests, two UTC range parents {parents}, invoker-only maintenance, bounded daily horizon, query partition pruning {sorted(relations)}; exact full-row copy verification also executes inside the migration transaction')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
