-- Statement transition tables enqueue whole changed minutes in the same transaction as samples.
-- All product sample writes target the partitioned parent, never an individual daily partition.
CREATE FUNCTION public.wd_mark_metric_minutes() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public SET TimeZone = 'UTC' AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        INSERT INTO public.metric_aggregation_pending(window_start_utc, revision)
        SELECT DISTINCT date_bin(INTERVAL '1 minute', sampled_at_utc, TIMESTAMPTZ '1970-01-01'), 1
        FROM (SELECT sampled_at_utc FROM new_samples UNION SELECT sampled_at_utc FROM old_samples) changed
        ORDER BY 1
        ON CONFLICT(window_start_utc) DO UPDATE SET revision = public.metric_aggregation_pending.revision + 1;
        RETURN NULL;
    END IF;
    INSERT INTO public.metric_aggregation_pending(window_start_utc, revision)
    SELECT DISTINCT date_bin(INTERVAL '1 minute', sampled_at_utc, TIMESTAMPTZ '1970-01-01'), 1
    FROM new_samples
    ORDER BY 1
    ON CONFLICT(window_start_utc) DO UPDATE SET revision = public.metric_aggregation_pending.revision + 1;
    RETURN NULL;
END $$;
CREATE TRIGGER wd_metric_insert AFTER INSERT ON public.metric_samples
    REFERENCING NEW TABLE AS new_samples FOR EACH STATEMENT EXECUTE FUNCTION public.wd_mark_metric_minutes();
CREATE TRIGGER wd_metric_update AFTER UPDATE ON public.metric_samples
    REFERENCING OLD TABLE AS old_samples NEW TABLE AS new_samples FOR EACH STATEMENT EXECUTE FUNCTION public.wd_mark_metric_minutes();
REVOKE ALL ON FUNCTION public.wd_mark_metric_minutes() FROM PUBLIC;
