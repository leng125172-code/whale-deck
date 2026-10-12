SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';
SET LOCAL TIME ZONE 'UTC';
SELECT pg_advisory_xact_lock(82592831620704);
LOCK TABLE public.metric_samples, public.metric_rollups IN ACCESS EXCLUSIVE MODE;
DO $owner_check$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_class WHERE oid IN ('public.metric_samples'::regclass, 'public.metric_rollups'::regclass)
        AND relowner <> (SELECT oid FROM pg_roles WHERE rolname = CURRENT_USER)) THEN
        RAISE EXCEPTION 'Metric migration must run as the existing table owner';
    END IF;
END
$owner_check$;

ALTER TABLE public.metric_samples RENAME TO wd_metric_samples_legacy;
ALTER TABLE public.wd_metric_samples_legacy RENAME CONSTRAINT "PK_metric_samples" TO wd_metric_samples_legacy_pk;
ALTER INDEX public."IX_metric_samples_series_id_sampled_at_utc" RENAME TO wd_metric_samples_legacy_idx;
ALTER TABLE public.metric_rollups RENAME TO wd_metric_rollups_legacy;
ALTER TABLE public.wd_metric_rollups_legacy RENAME CONSTRAINT "PK_metric_rollups" TO wd_metric_rollups_legacy_pk;

CREATE TABLE public.metric_samples (LIKE public.wd_metric_samples_legacy INCLUDING DEFAULTS INCLUDING CONSTRAINTS)
    PARTITION BY RANGE (sampled_at_utc);
ALTER TABLE public.metric_samples ADD CONSTRAINT "PK_metric_samples" PRIMARY KEY (series_id, sampled_at_utc);
CREATE INDEX "IX_metric_samples_series_id_sampled_at_utc" ON public.metric_samples (series_id, sampled_at_utc);
CREATE INDEX wd_metric_samples_time_idx ON public.metric_samples (sampled_at_utc);
CREATE TABLE public.metric_rollups (LIKE public.wd_metric_rollups_legacy INCLUDING DEFAULTS INCLUDING CONSTRAINTS)
    PARTITION BY RANGE (window_start_utc);
ALTER TABLE public.metric_rollups ADD CONSTRAINT "PK_metric_rollups" PRIMARY KEY (series_id, window_start_utc, resolution);
CREATE INDEX wd_metric_rollups_time_idx ON public.metric_rollups (window_start_utc);

-- Preserve ALL existing data, even older than retention. Cleanup is a separate Worker action.
DO $migration$
DECLARE
    day date;
    spec record;
    days date[];
    mismatch boolean;
    permission record;
BEGIN
    FOR spec IN SELECT * FROM (VALUES
        ('metric_samples', 'sampled_at_utc', 'wd_metric_samples_legacy'),
        ('metric_rollups', 'window_start_utc', 'wd_metric_rollups_legacy')) AS parents(parent_name, time_column, legacy_name)
    LOOP
        EXECUTE format('SELECT array_agg(day ORDER BY day) FROM (SELECT DISTINCT (%I AT TIME ZONE ''UTC'')::date AS day FROM public.%I UNION SELECT generate_series((CURRENT_TIMESTAMP AT TIME ZONE ''UTC'')::date - 14, (CURRENT_TIMESTAMP AT TIME ZONE ''UTC'')::date + 2, INTERVAL ''1 day'')::date) days', spec.time_column, spec.legacy_name) INTO days;
        IF cardinality(days) > 4096 THEN RAISE EXCEPTION 'Metric migration exceeds its bounded partition budget'; END IF;
        FOREACH day IN ARRAY days LOOP
            EXECUTE format('CREATE TABLE public.%I PARTITION OF public.%I FOR VALUES FROM (%L) TO (%L)',
                spec.parent_name || '_p' || to_char(day, 'YYYYMMDD'), spec.parent_name,
                day::timestamp AT TIME ZONE 'UTC', (day + 1)::timestamp AT TIME ZONE 'UTC');
        END LOOP;
        EXECUTE format('INSERT INTO public.%I SELECT * FROM public.%I', spec.parent_name, spec.legacy_name);
        -- Exact set comparison plus count, before the original heap can be removed.
        EXECUTE format('SELECT (SELECT count(*) FROM public.%I) <> (SELECT count(*) FROM public.%I) OR EXISTS (SELECT * FROM public.%I EXCEPT ALL SELECT * FROM public.%I)',
            spec.legacy_name, spec.parent_name, spec.legacy_name, spec.parent_name) INTO STRICT mismatch;
        IF mismatch THEN RAISE EXCEPTION 'Metric migration data verification failed'; END IF;
        FOR permission IN SELECT acl.privilege_type, acl.is_grantable,
            CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(r.rolname) END AS grantee
            FROM pg_class c CROSS JOIN LATERAL aclexplode(c.relacl) acl LEFT JOIN pg_roles r ON r.oid = acl.grantee
            WHERE c.oid = to_regclass('public.' || spec.legacy_name) AND acl.grantee <> c.relowner
        LOOP
            EXECUTE format('GRANT %s ON TABLE public.%I TO %s%s', permission.privilege_type, spec.parent_name,
                permission.grantee, CASE WHEN permission.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END);
        END LOOP;
    END LOOP;
END
$migration$;

DROP TABLE public.wd_metric_samples_legacy;
DROP TABLE public.wd_metric_rollups_legacy;

-- Invoker permissions only; no arbitrary table/path/DDL input, and no SECURITY DEFINER.
CREATE FUNCTION public.wd_maintain_metric_partitions(p_now timestamptz DEFAULT CURRENT_TIMESTAMP)
RETURNS integer LANGUAGE plpgsql SECURITY INVOKER
SET search_path TO pg_catalog, public SET TimeZone TO 'UTC' SET lock_timeout TO '3s'
AS $maintenance$
DECLARE
    spec record;
    child record;
    day date;
    cutoff timestamptz := p_now - INTERVAL '14 days';
    today date := (p_now AT TIME ZONE 'UTC')::date;
    boundary date := (cutoff AT TIME ZONE 'UTC')::date;
    expected text;
    removed integer := 0;
BEGIN
    IF p_now IS NULL OR NOT isfinite(p_now) THEN RAISE EXCEPTION 'Invalid metric maintenance clock'; END IF;
    IF NOT pg_try_advisory_xact_lock(82592831620704) THEN RETURN -1; END IF;
    FOR spec IN SELECT * FROM (VALUES ('metric_samples', 'sampled_at_utc'), ('metric_rollups', 'window_start_utc')) AS parents(parent_name, time_column)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_partitioned_table p WHERE p.partrelid = to_regclass('public.' || spec.parent_name)
            AND pg_get_partkeydef(p.partrelid) = 'RANGE (' || spec.time_column || ')') THEN
            RAISE EXCEPTION 'Expected metric range parent is unavailable';
        END IF;
        FOR child IN SELECT c.relname, c.relkind, n.nspname, pg_get_expr(c.relpartbound, c.oid) AS bounds
            FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE i.inhparent = to_regclass('public.' || spec.parent_name)
        LOOP
            IF child.nspname <> 'public' OR child.relkind <> 'r' OR child.relname !~ ('^' || spec.parent_name || '_p[0-9]{8}$') THEN
                RAISE EXCEPTION 'Unmanaged metric partition blocks maintenance';
            END IF;
            day := to_date(right(child.relname, 8), 'YYYYMMDD');
            expected := format('FOR VALUES FROM (%L) TO (%L)', day::timestamp AT TIME ZONE 'UTC', (day + 1)::timestamp AT TIME ZONE 'UTC');
            IF right(child.relname, 8) <> to_char(day, 'YYYYMMDD') OR child.bounds <> expected THEN
                RAISE EXCEPTION 'Metric partition name and bounds disagree';
            END IF;
            IF (day + 1)::timestamp AT TIME ZONE 'UTC' <= cutoff THEN
                EXECUTE format('DROP TABLE public.%I', child.relname); -- deliberately no CASCADE
                removed := removed + 1;
            END IF;
        END LOOP;
        FOR day IN SELECT generate_series(boundary, today + 2, INTERVAL '1 day')::date LOOP
            IF to_regclass('public.' || spec.parent_name || '_p' || to_char(day, 'YYYYMMDD')) IS NULL THEN
                EXECUTE format('CREATE TABLE public.%I PARTITION OF public.%I FOR VALUES FROM (%L) TO (%L)',
                    spec.parent_name || '_p' || to_char(day, 'YYYYMMDD'), spec.parent_name,
                    day::timestamp AT TIME ZONE 'UTC', (day + 1)::timestamp AT TIME ZONE 'UTC');
            ELSIF NOT EXISTS (SELECT 1 FROM pg_inherits WHERE inhparent = to_regclass('public.' || spec.parent_name)
                AND inhrelid = to_regclass('public.' || spec.parent_name || '_p' || to_char(day, 'YYYYMMDD'))) THEN
                RAISE EXCEPTION 'Metric partition name collides with an unrelated relation';
            END IF;
        END LOOP;
        -- Only the one partial boundary day requires row cleanup, not the entire parent/history.
        EXECUTE format('DELETE FROM public.%I WHERE %I < $1', spec.parent_name || '_p' || to_char(boundary, 'YYYYMMDD'), spec.time_column) USING cutoff;
    END LOOP;
    RETURN removed;
END
$maintenance$;
REVOKE ALL ON FUNCTION public.wd_maintain_metric_partitions(timestamptz) FROM PUBLIC;
