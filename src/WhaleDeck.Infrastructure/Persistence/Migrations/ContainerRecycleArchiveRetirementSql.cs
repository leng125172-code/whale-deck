namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecycleArchiveRetirementIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_archive_retirement_plan_valid(p jsonb, days integer) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE c jsonb; source jsonb; old jsonb; field text; BEGIN
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p))<>22 OR NOT p ?& ARRAY[
        'SchemaVersion','Command','ArchiveRecordDigest','SourceRecordDigest','PurgeRecordDigest','PurgeMarkerDigest','ClaimDigest','HistoryMetadataDigest',
        'HistoryCreatedAtUnixMs','NotBeforeUnixMs','RetentionElapsed','BindingCurrent','RetirementEligible','StorageHash','StoreIdentity','ArchiveIdentity',
        'ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256','PublishedIdentity','PublishedChangedAtUnixNs','Fingerprint'] THEN RETURN FALSE; END IF;
      c:=p->'Command'; source:=c->'Archive'; old:=source->'Plan';
      IF NOT COALESCE(p->'SchemaVersion'='1'::jsonb AND jsonb_typeof(c)='object' AND (SELECT count(*) FROM jsonb_object_keys(c))=3
        AND c ?& ARRAY['Archive','RetainDays','Mode'] AND c->'Mode'='"ArchiveData"'::jsonb AND c->'RetainDays'=to_jsonb(days)
        AND jsonb_typeof(source)='object' AND (SELECT count(*) FROM jsonb_object_keys(source))=3 AND source ?& ARRAY['OperationId','Owner','Plan']
        AND jsonb_typeof(source->'OperationId')='string' AND jsonb_typeof(source->'Owner')='string' AND jsonb_typeof(old)='object'
        AND p->'BindingCurrent'='true'::jsonb AND jsonb_typeof(p->'RetentionElapsed')='boolean' AND p->'RetirementEligible'=p->'RetentionElapsed'
        AND whaledeck_recycle_uint(p->'HistoryCreatedAtUnixMs',253402300799999) AND whaledeck_recycle_uint(p->'NotBeforeUnixMs',253402300799999)
        AND p->'HistoryCreatedAtUnixMs'=old->'Command'->'Purge'->'Plan'->'HistoryCreatedAtUnixMs'
        AND (p->>'NotBeforeUnixMs')::numeric=(p->>'HistoryCreatedAtUnixMs')::numeric+days::bigint*86400000
        AND jsonb_typeof(p->'PublishedIdentity')='object' AND whaledeck_recycle_uint(p->'PublishedChangedAtUnixNs',9223372036854775807),FALSE) THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['ArchiveRecordDigest','ClaimDigest','Fingerprint'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['SourceRecordDigest','PurgeRecordDigest','PurgeMarkerDigest','HistoryMetadataDigest','StorageHash','StoreIdentity','ArchiveIdentity','ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256'] LOOP
        IF NOT (old ? field) OR p->field IS DISTINCT FROM old->field THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;

    CREATE FUNCTION whaledeck_archive_retirement_receipt_valid(r jsonb, p jsonb, execution uuid) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE a jsonb; BEGIN
      a:=p->'Command'->'Archive';
      RETURN COALESCE(jsonb_typeof(r)='object' AND (SELECT count(*) FROM jsonb_object_keys(r))=9
        AND r ?& ARRAY['Id','SourceOperationId','ArchiveOperationId','Fingerprint','State','SourceFenced','OwnedFence','RetirementApplied','NotBeforeUnixMs']
        AND execution IS NOT NULL AND r->'Id'=to_jsonb(execution)
        AND r->'SourceOperationId'=a->'Plan'->'Command'->'Purge'->'Plan'->'Command'->'Source'->'OperationId'
        AND r->'ArchiveOperationId'=a->'OperationId' AND r->'Fingerprint'=p->'Fingerprint' AND r->'NotBeforeUnixMs'=p->'NotBeforeUnixMs'
        AND jsonb_typeof(r->'SourceFenced')='boolean' AND jsonb_typeof(r->'OwnedFence')='boolean'
        AND (r->'OwnedFence'='false'::jsonb OR r->'SourceFenced'='true'::jsonb)
        AND CASE r->'State'
          WHEN '"Reserved"'::jsonb THEN r->'OwnedFence'='true'::jsonb AND r->'RetirementApplied'='false'::jsonb
          WHEN '"Aborted"'::jsonb THEN r->'OwnedFence'='false'::jsonb AND r->'RetirementApplied'='false'::jsonb
          WHEN '"Unknown"'::jsonb THEN r->'SourceFenced'='true'::jsonb AND r->'RetirementApplied' IN ('null'::jsonb,'false'::jsonb)
          WHEN '"Completed"'::jsonb THEN r->'OwnedFence'='true'::jsonb AND r->'RetirementApplied'='true'::jsonb
          ELSE FALSE END,FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    """;

    private const string WriterTables = """
    ARRAY['file_path_locks','container_file_upload_operations','container_directory_operations','container_copy_intents',
      'container_move_intents','container_permission_intents','container_ownership_intents','container_recursive_permission_intents',
      'container_recursive_ownership_intents','container_acl_intents','container_recursive_acl_intents','container_recursive_acl_restore_intents',
      'container_recycle_intents','container_recycle_restore_intents','container_recycle_purge_intents','container_recycle_archive_intents']
    """;

    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_archive_retirement_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    DECLARE a container_recycle_archive_intents%ROWTYPE; p jsonb; BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'archive_retirement_permanent_intent' USING ERRCODE='23514'; END IF;
      IF TG_OP='INSERT' THEN
        IF NEW.state<>'Queued' OR NEW.plan_json IS NOT NULL OR NEW.version<>0 THEN
          RAISE EXCEPTION 'archive_retirement_initial_state' USING ERRCODE='23514';
        END IF;
      ELSE
        IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
          RAISE EXCEPTION 'archive_retirement_terminal_immutable' USING ERRCODE='23514';
        END IF;
        IF ROW(NEW.id,NEW.archive_intent_id,NEW.retain_days,NEW.source_proof_hash,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,
          NEW.trace_id,NEW.owner_hash,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
          IS DISTINCT FROM ROW(OLD.id,OLD.archive_intent_id,OLD.retain_days,OLD.source_proof_hash,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,
          OLD.trace_id,OLD.owner_hash,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc)
          OR OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc)
            IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc)
          OR OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
            IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash)
          OR OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc
          OR OLD.apply_started_at_utc IS NOT NULL AND NEW.apply_started_at_utc IS DISTINCT FROM OLD.apply_started_at_utc
          OR OLD.reservation_json IS NOT NULL AND ROW(NEW.reservation_json,NEW.reservation_hash) IS DISTINCT FROM ROW(OLD.reservation_json,OLD.reservation_hash)
          THEN RAISE EXCEPTION 'archive_retirement_binding_immutable' USING ERRCODE='23514';
        END IF;
        IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json)<>(NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash)
          OR OLD.receipt_json IS NOT NULL AND ROW(NEW.receipt_json,NEW.receipt_hash) IS DISTINCT FROM ROW(OLD.receipt_json,OLD.receipt_hash)
          THEN RAISE EXCEPTION 'archive_retirement_fact_immutable' USING ERRCODE='23514';
        END IF;
        IF OLD.plan_json IS NULL AND NEW.plan_json IS NOT NULL AND NOT (OLD.state='Planning' AND NEW.state='Ready')
          OR OLD.execution_job_id IS NULL AND NEW.execution_job_id IS NOT NULL AND NOT (OLD.state='Ready' AND NEW.state='Pending')
          OR OLD.root_started_at_utc IS NULL AND NEW.root_started_at_utc IS NOT NULL AND NOT (OLD.state='Pending' AND NEW.state='Reserving')
          OR OLD.reservation_json IS NULL AND NEW.reservation_json IS NOT NULL AND NOT (OLD.state='Reserving' AND NEW.state='Reserving')
          OR OLD.apply_started_at_utc IS NULL AND NEW.apply_started_at_utc IS NOT NULL AND
            NOT (OLD.state='Reserving' AND NEW.state='Applying' AND OLD.reservation_json IS NOT NULL)
          THEN RAISE EXCEPTION 'archive_retirement_stage_order' USING ERRCODE='23514';
        END IF;
        IF NEW.state<>OLD.state AND NOT (CASE OLD.state
          WHEN 'Queued' THEN NEW.state IN ('Planning','Canceled','Failed','Expired')
          WHEN 'Planning' THEN NEW.state IN ('Ready','Canceled','Failed','Expired')
          WHEN 'Ready' THEN NEW.state IN ('Pending','Canceled','Expired')
          WHEN 'Pending' THEN NEW.state IN ('Reserving','Canceled','Failed','Expired')
          WHEN 'Reserving' THEN NEW.state IN ('Applying','Unknown','Canceled','Failed','Expired')
          WHEN 'Applying' THEN NEW.state IN ('Unknown','Succeeded','Canceled','Failed','Expired')
          WHEN 'Unknown' THEN NEW.state='Acknowledged'
          ELSE FALSE END) THEN
          RAISE EXCEPTION 'archive_retirement_invalid_transition' USING ERRCODE='23514';
        END IF;
      END IF;

      SELECT * INTO a FROM container_recycle_archive_intents WHERE id=NEW.archive_intent_id;
      IF NOT FOUND OR a.state<>'Succeeded' OR a.completed_at_utc IS NULL OR NEW.created_at_utc<a.completed_at_utc OR a.publish_barrier OR a.root_state IS DISTINCT FROM 'Completed'
        OR a.archive_copied IS DISTINCT FROM TRUE OR a.owned_fence IS DISTINCT FROM TRUE OR a.binding_current IS DISTINCT FROM TRUE THEN
        RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_CHANGED' USING ERRCODE='55000';
      END IF;
      IF NEW.plan_json IS NOT NULL THEN
        p:=NEW.plan_json::jsonb;
        IF p->'Command'->'Archive'->'OperationId' IS DISTINCT FROM to_jsonb(a.execution_job_id)
          OR p->'Command'->'Archive'->'Owner' IS DISTINCT FROM to_jsonb(a.owner_hash)
          OR p->'Command'->'Archive'->'Plan' IS DISTINCT FROM a.plan_json::jsonb
          OR p->'PublishedIdentity' IS DISTINCT FROM a.receipt_json::jsonb->'PublishedIdentity'
          OR p->'PublishedChangedAtUnixNs' IS DISTINCT FROM a.receipt_json::jsonb->'PublishedChangedAtUnixNs'
          OR NEW.execution_job_id IS NOT NULL AND to_jsonb(NEW.execution_job_id) IN (
            to_jsonb(a.execution_job_id), a.command_json::jsonb->'Purge'->'OperationId',
            a.command_json::jsonb->'Purge'->'Plan'->'Command'->'Source'->'OperationId') THEN
          RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_CHANGED' USING ERRCODE='55000';
        END IF;
      END IF;
      RETURN NEW;
    END $f$;

    CREATE TRIGGER archive_retirement_guard BEFORE INSERT OR UPDATE OR DELETE ON container_recycle_archive_retirement_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_archive_retirement_guard();

    CREATE FUNCTION whaledeck_archive_retirement_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    DECLARE writer text; blocked boolean; BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS(SELECT 1 FROM container_recycle_archive_retirement_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME<>'container_recycle_archive_retirement_intents' THEN
        IF NEW.publish_barrier AND EXISTS(SELECT 1 FROM container_recycle_archive_retirement_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier THEN
        IF EXISTS(SELECT 1 FROM file_path_locks) THEN
          RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
        FOREACH writer IN ARRAY
    """ + " " + WriterTables + " " + """
        LOOP
          IF writer='file_path_locks' THEN CONTINUE; END IF;
          EXECUTE format('SELECT EXISTS(SELECT 1 FROM %I WHERE publish_barrier)',writer) INTO blocked;
          IF blocked THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TARGET_BUSY' USING ERRCODE='55000'; END IF;
        END LOOP;
      END IF;
      RETURN NEW;
    END $f$;

    CREATE TRIGGER archive_retirement_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_archive_retirement_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_archive_retirement_fence();
    DO $f$ DECLARE writer text; BEGIN FOREACH writer IN ARRAY
    """ + " " + WriterTables + " " + """
      LOOP
        EXECUTE format('CREATE TRIGGER archive_retirement_fence BEFORE INSERT OR UPDATE%s ON %I FOR EACH ROW EXECUTE FUNCTION whaledeck_archive_retirement_fence()',
          CASE WHEN writer='file_path_locks' THEN '' ELSE ' OF publish_barrier' END,writer);
      END LOOP;
    END $f$;
    """;

    private const string DropFences = """
    DO $f$ DECLARE writer text; BEGIN FOREACH writer IN ARRAY
    """ + " " + WriterTables + " " + """
      LOOP EXECUTE format('DROP TRIGGER archive_retirement_fence ON %I',writer); END LOOP;
    END $f$;
    DROP TRIGGER archive_retirement_fence ON container_recycle_archive_retirement_intents;
    DROP TRIGGER archive_retirement_guard ON container_recycle_archive_retirement_intents;
    DROP FUNCTION whaledeck_archive_retirement_fence();
    DROP FUNCTION whaledeck_archive_retirement_guard();
    """;

    private const string DropShapeFunctions = """
    DROP FUNCTION whaledeck_archive_retirement_receipt_valid(jsonb,jsonb,uuid);
    DROP FUNCTION whaledeck_archive_retirement_plan_valid(jsonb,integer);
    """;
}
