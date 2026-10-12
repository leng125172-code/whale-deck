SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(82592831620704);
LOCK TABLE public.metric_samples, public.metric_rollups IN ACCESS EXCLUSIVE MODE;
DROP FUNCTION public.wd_maintain_metric_partitions(timestamptz);
ALTER TABLE public.metric_samples RENAME TO wd_metric_samples_partitioned;
ALTER TABLE public.wd_metric_samples_partitioned RENAME CONSTRAINT "PK_metric_samples" TO wd_metric_samples_partitioned_pk;
ALTER INDEX public."IX_metric_samples_series_id_sampled_at_utc" RENAME TO wd_metric_samples_partitioned_idx;
ALTER TABLE public.metric_rollups RENAME TO wd_metric_rollups_partitioned;
ALTER TABLE public.wd_metric_rollups_partitioned RENAME CONSTRAINT "PK_metric_rollups" TO wd_metric_rollups_partitioned_pk;

CREATE TABLE public.metric_samples (LIKE public.wd_metric_samples_partitioned INCLUDING DEFAULTS INCLUDING CONSTRAINTS);
ALTER TABLE public.metric_samples ADD CONSTRAINT "PK_metric_samples" PRIMARY KEY (series_id, sampled_at_utc);
CREATE INDEX "IX_metric_samples_series_id_sampled_at_utc" ON public.metric_samples (series_id, sampled_at_utc);
CREATE TABLE public.metric_rollups (LIKE public.wd_metric_rollups_partitioned INCLUDING DEFAULTS INCLUDING CONSTRAINTS);
ALTER TABLE public.metric_rollups ADD CONSTRAINT "PK_metric_rollups" PRIMARY KEY (series_id, window_start_utc, resolution);
INSERT INTO public.metric_samples SELECT * FROM public.wd_metric_samples_partitioned;
INSERT INTO public.metric_rollups SELECT * FROM public.wd_metric_rollups_partitioned;
DO $verification$
DECLARE
    spec record;
    permission record;
BEGIN
    IF EXISTS (SELECT * FROM public.wd_metric_samples_partitioned EXCEPT ALL SELECT * FROM public.metric_samples)
       OR (SELECT count(*) FROM public.wd_metric_samples_partitioned) <> (SELECT count(*) FROM public.metric_samples)
       OR EXISTS (SELECT * FROM public.wd_metric_rollups_partitioned EXCEPT ALL SELECT * FROM public.metric_rollups)
       OR (SELECT count(*) FROM public.wd_metric_rollups_partitioned) <> (SELECT count(*) FROM public.metric_rollups) THEN
        RAISE EXCEPTION 'Metric downgrade data verification failed';
    END IF;
    FOR spec IN SELECT * FROM (VALUES ('metric_samples', 'wd_metric_samples_partitioned'),
        ('metric_rollups', 'wd_metric_rollups_partitioned')) AS parents(parent_name, old_name)
    LOOP
        FOR permission IN SELECT acl.privilege_type, acl.is_grantable,
            CASE WHEN acl.grantee = 0 THEN 'PUBLIC' ELSE quote_ident(r.rolname) END AS grantee
            FROM pg_class c CROSS JOIN LATERAL aclexplode(c.relacl) acl LEFT JOIN pg_roles r ON r.oid = acl.grantee
            WHERE c.oid = to_regclass('public.' || spec.old_name) AND acl.grantee <> c.relowner
        LOOP
            EXECUTE format('GRANT %s ON TABLE public.%I TO %s%s', permission.privilege_type, spec.parent_name,
                permission.grantee, CASE WHEN permission.is_grantable THEN ' WITH GRANT OPTION' ELSE '' END);
        END LOOP;
    END LOOP;
END
$verification$;
DROP TABLE public.wd_metric_samples_partitioned;
DROP TABLE public.wd_metric_rollups_partitioned;
