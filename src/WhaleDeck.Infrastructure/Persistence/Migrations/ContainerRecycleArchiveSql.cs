namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecycleArchiveIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recycle_archive_command_valid(c jsonb,cid text,instance text,path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE q jsonb; BEGIN
      q:=c->'Purge';
      RETURN COALESCE(jsonb_typeof(c)='object' AND (SELECT count(*) FROM jsonb_object_keys(c))=3 AND c ?& ARRAY['Purge','HistoryDigest','Mode'] AND c->>'Mode'='ArchiveCopy'
        AND jsonb_typeof(q)='object' AND (SELECT count(*) FROM jsonb_object_keys(q))=3 AND q ?& ARRAY['OperationId','Owner','Plan']
        AND q->>'OperationId' ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' AND q->>'OperationId'<>'00000000-0000-0000-0000-000000000000'
        AND q->>'Owner' ~ '^[A-F0-9]{64}$' AND whaledeck_recycle_purge_command_valid(q->'Plan'->'Command',cid,instance,path) AND whaledeck_recycle_purge_plan_valid(q->'Plan',q->'Plan'->'Command')
        AND q->'OperationId'<>q->'Plan'->'Command'->'Source'->'OperationId' AND c->'HistoryDigest'=q->'Plan'->'Command'->'HistoryDigest',FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    CREATE FUNCTION whaledeck_recycle_archive_plan_valid(p jsonb,c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; old jsonb; BEGIN
      old:=c->'Purge'->'Plan';
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p))<>21 OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','PrivateIdentity','DeletedIdentity','DeletedChangedAtUnixNs','SourceRecordDigest','PurgeRecordDigest','PurgeMarkerDigest','HistoryMetadataDigest','StorageHash','StoreIdentity','ArchiveIdentity','ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256','Fingerprint'] OR p->'Command' IS DISTINCT FROM c OR p->'SchemaVersion' IS DISTINCT FROM '1'::jsonb THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentVersion','SourceRecordDigest','PurgeRecordDigest','PurgeMarkerDigest','HistoryMetadataDigest','Fingerprint'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentIdentity','OwnerUid','OwnerGid','StorageHash','StoreIdentity','ArchiveIdentity','ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256'] LOOP
        IF p->field IS DISTINCT FROM old->field THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN COALESCE((p->'PrivateIdentity'='null'::jsonb OR p->'PrivateIdentity'=old->'PrivateIdentity') AND p->'DeletedIdentity'=old->'RetainedIdentity' AND p->'DeletedChangedAtUnixNs'=old->'RetainedChangedAtUnixNs',FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    CREATE FUNCTION whaledeck_recycle_archive_receipt_valid(r jsonb,p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; i jsonb; original jsonb; archive jsonb; BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(r))<>11 OR NOT r ?& ARRAY['Id','SourceOperationId','PurgeOperationId','Fingerprint','State','SourceFenced','OwnedFence','ArchiveCopied','BindingCurrent','PublishedIdentity','PublishedChangedAtUnixNs'] THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','SourceOperationId','PurgeOperationId','Fingerprint','State'] LOOP IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF; END LOOP;
      IF NOT COALESCE(jsonb_typeof(r->'SourceFenced')='boolean' AND jsonb_typeof(r->'OwnedFence')='boolean' AND jsonb_typeof(r->'BindingCurrent') IN ('boolean','null') AND (NOT (r->>'OwnedFence')::boolean OR (r->>'SourceFenced')::boolean)
        AND r->'SourceOperationId'=p->'Command'->'Purge'->'Plan'->'Command'->'Source'->'OperationId' AND r->'PurgeOperationId'=p->'Command'->'Purge'->'OperationId' AND r->'Fingerprint'=p->'Fingerprint' AND r->>'Id'<>r->>'SourceOperationId' AND r->>'Id'<>r->>'PurgeOperationId'
        AND (r->'BindingCurrent'<>'true'::jsonb OR r->>'State'='Completed' AND (r->>'OwnedFence')::boolean)
        AND CASE r->>'State' WHEN 'Reserved' THEN r->'ArchiveCopied'='false'::jsonb AND (r->>'SourceFenced')::boolean AND (r->>'OwnedFence')::boolean WHEN 'Completed' THEN r->'ArchiveCopied'='true'::jsonb AND (r->>'SourceFenced')::boolean WHEN 'Aborted' THEN r->'ArchiveCopied'='false'::jsonb AND NOT (r->>'OwnedFence')::boolean WHEN 'Unknown' THEN r->'ArchiveCopied'='null'::jsonb AND (r->>'SourceFenced')::boolean ELSE FALSE END,FALSE) THEN RETURN FALSE; END IF;
      IF r->>'State'<>'Completed' THEN RETURN r->'PublishedIdentity'='null'::jsonb AND r->'PublishedChangedAtUnixNs'='null'::jsonb; END IF;
      i:=r->'PublishedIdentity';original:=p->'Command'->'Purge'->'Plan'->'Command'->'Source'->'Plan'->'SourceIdentity';archive:=p->'ArchiveIdentity';
      IF jsonb_typeof(i) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(i))<>8 OR NOT i ?& ARRAY['Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs'] THEN RETURN FALSE; END IF;
      RETURN COALESCE(whaledeck_recycle_uint(i->'Device',18446744073709551615) AND whaledeck_recycle_uint(i->'Inode',18446744073709551615) AND (i->>'Inode')::numeric>0 AND i->'Device'=p->'ParentIdentity'->'Device'
        AND NOT(i->'Device'=original->'Device' AND i->'Inode'=original->'Inode') AND NOT(i->'Device'=archive->'Device' AND i->'Inode'=archive->'Inode')
        AND i->'Mode'=original->'Mode' AND i->'OwnerUid'=original->'OwnerUid' AND i->'OwnerGid'=original->'OwnerGid' AND i->'SizeBytes'=original->'SizeBytes' AND i->'ModifiedAtUnixNs'=original->'ModifiedAtUnixNs' AND i->'LinkCount'='1'::jsonb AND whaledeck_recycle_uint(r->'PublishedChangedAtUnixNs',9223372036854775807),FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recycle_archive_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recycle_archive_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.source_intent_id,NEW.source_proof_hash,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.source_intent_id,OLD.source_proof_hash,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_archive_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_archive_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recycle_archive_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recycle_archive_started_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.apply_started_at_utc IS NOT NULL AND NEW.apply_started_at_utc IS DISTINCT FROM OLD.apply_started_at_utc OR OLD.reservation_json IS NOT NULL AND ROW(NEW.reservation_json,NEW.reservation_hash) IS DISTINCT FROM ROW(OLD.reservation_json,OLD.reservation_hash) THEN RAISE EXCEPTION 'container_recycle_archive_reservation_immutable' USING ERRCODE='23514'; END IF;
      IF OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.archive_copied) IS DISTINCT FROM ROW(OLD.root_state,OLD.archive_copied) OR OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR (NEW.receipt_json::jsonb-ARRAY['SourceFenced','OwnedFence','BindingCurrent']) IS DISTINCT FROM (OLD.receipt_json::jsonb-ARRAY['SourceFenced','OwnedFence','BindingCurrent'])) THEN RAISE EXCEPTION 'container_recycle_archive_fact_immutable' USING ERRCODE='23514'; END IF;
      IF (NEW.reservation_json IS DISTINCT FROM OLD.reservation_json)<>(NEW.reservation_hash IS DISTINCT FROM OLD.reservation_hash) THEN RAISE EXCEPTION 'container_recycle_archive_reservation_authority_required' USING ERRCODE='23514'; END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recycle_archive_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Unknown') THEN
        RAISE EXCEPTION 'container_recycle_archive_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recycle_archive_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recycle_archive_guard BEFORE UPDATE OR DELETE ON container_recycle_archive_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_guard();
    CREATE FUNCTION whaledeck_container_recycle_archive_fence() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME<>'container_recycle_archive_intents' THEN
        IF TG_TABLE_NAME='file_path_locks' THEN
          IF EXISTS(SELECT 1 FROM container_recycle_archive_intents WHERE publish_barrier) THEN RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000'; END IF;
        ELSIF NEW.publish_barrier AND EXISTS(SELECT 1 FROM container_recycle_archive_intents WHERE publish_barrier) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_TARGET_BUSY' USING ERRCODE='55000'; END IF;
      ELSIF NEW.publish_barrier AND (EXISTS(SELECT 1 FROM file_path_locks) OR EXISTS(SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_move_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_acl_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_purge_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_archive_intents WHERE publish_barrier AND id<>NEW.id)) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_TARGET_BUSY' USING ERRCODE='55000'; END IF;
      RETURN NEW; END $f$;
    CREATE TRIGGER whaledeck_recycle_archive_fence_0 BEFORE INSERT OR UPDATE ON file_path_locks FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_1 BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_2 BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_3 BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_4 BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_5 BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_6 BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_7 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_8 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_9 BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_10 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_11 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_restore_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_12 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_13 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_restore_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_14 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_archive_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE TRIGGER whaledeck_recycle_archive_fence_15 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_purge_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_archive_fence();
    CREATE FUNCTION whaledeck_recycle_archive_source_fence() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF NOT EXISTS(SELECT 1 FROM container_recycle_purge_intents p WHERE p.id=NEW.source_intent_id AND p.state='Succeeded' AND p.completed_at_utc IS NOT NULL AND NOT p.publish_barrier AND p.root_state='Completed' AND p.purge_applied IS TRUE) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED' USING ERRCODE='55000'; END IF;
      RETURN NEW; END $f$;
    CREATE TRIGGER container_recycle_archive_source_fence BEFORE INSERT OR UPDATE ON container_recycle_archive_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_recycle_archive_source_fence();
    """;
}
