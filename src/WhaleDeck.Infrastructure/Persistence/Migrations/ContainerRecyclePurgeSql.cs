namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecyclePurgeIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recycle_purge_command_valid(c jsonb,cid text,instance text,path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    BEGIN
      RETURN COALESCE(jsonb_typeof(c)='object' AND (SELECT count(*) FROM jsonb_object_keys(c))=3 AND c ?& ARRAY['Source','HistoryDigest','Scope'] AND c->>'Scope'='Originals' AND whaledeck_recycle_restore_command_valid(c-'Scope',cid,instance,path),FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    CREATE FUNCTION whaledeck_recycle_purge_plan_valid(p jsonb,c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; a jsonb; s jsonb; view jsonb;
    BEGIN
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p))<>20 OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','PrivateIdentity','RetainedIdentity','RetainedChangedAtUnixNs','SourceRecordDigest','HistoryMetadataDigest','HistoryCreatedAtUnixMs','StorageHash','StoreIdentity','ArchiveIdentity','ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256','Fingerprint'] OR p->'Command' IS DISTINCT FROM c THEN RETURN FALSE; END IF;
      view:=(p-ARRAY['SourceRecordDigest','HistoryMetadataDigest','HistoryCreatedAtUnixMs','StorageHash','StoreIdentity','ArchiveIdentity','ArchiveChangedAtUnixNs','ArchiveBytes','ArchiveSha256'])||jsonb_build_object('Command',c-'Scope');
      IF NOT whaledeck_recycle_restore_plan_valid(view,c-'Scope') THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['SourceRecordDigest','HistoryMetadataDigest','StorageHash','ArchiveSha256'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      a:=p->'ArchiveIdentity';s:=p->'StoreIdentity';
      RETURN COALESCE(whaledeck_recycle_uint(p->'HistoryCreatedAtUnixMs',253402300799999) AND whaledeck_recycle_uint(p->'ArchiveChangedAtUnixNs',9223372036854775807) AND whaledeck_recycle_uint(p->'ArchiveBytes',1073741824)
        AND jsonb_typeof(s)='object' AND (SELECT count(*) FROM jsonb_object_keys(s))=2 AND s ?& ARRAY['Device','Inode'] AND whaledeck_recycle_uint(s->'Device',18446744073709551615) AND whaledeck_recycle_uint(s->'Inode',18446744073709551615) AND (s->>'Inode')::numeric>0
        AND jsonb_typeof(a)='object' AND (SELECT count(*) FROM jsonb_object_keys(a))=8 AND a ?& ARRAY['Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs']
        AND whaledeck_recycle_uint(a->'Device',18446744073709551615) AND whaledeck_recycle_uint(a->'Inode',18446744073709551615) AND (a->>'Inode')::numeric>0
        AND a->'Mode'='33152'::jsonb AND a->'OwnerUid'='0'::jsonb AND a->'OwnerGid'='0'::jsonb AND a->'LinkCount'='1'::jsonb
        AND a->'SizeBytes'=p->'ArchiveBytes' AND a->'SizeBytes'=c->'Source'->'Plan'->'SourceIdentity'->'SizeBytes' AND whaledeck_recycle_sint(a->'ModifiedAtUnixNs') AND p->'ArchiveSha256'=c->'Source'->'Plan'->'Sha256',FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    CREATE FUNCTION whaledeck_recycle_purge_receipt_valid(r jsonb,p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(r))<>7 OR NOT r ?& ARRAY['Id','SourceOperationId','Fingerprint','State','SourceFenced','OwnedFence','PurgeApplied'] THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','SourceOperationId','Fingerprint','State'] LOOP IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF; END LOOP;
      RETURN COALESCE(jsonb_typeof(r->'SourceFenced')='boolean' AND jsonb_typeof(r->'OwnedFence')='boolean' AND (NOT (r->>'OwnedFence')::boolean OR (r->>'SourceFenced')::boolean)
        AND r->'SourceOperationId'=p->'Command'->'Source'->'OperationId' AND r->'Fingerprint'=p->'Fingerprint' AND r->>'Id'<>r->>'SourceOperationId'
        AND CASE r->>'State' WHEN 'Reserved' THEN r->'PurgeApplied'='false'::jsonb AND (r->>'SourceFenced')::boolean AND (r->>'OwnedFence')::boolean WHEN 'Completed' THEN r->'PurgeApplied'='true'::jsonb AND (r->>'SourceFenced')::boolean AND (r->>'OwnedFence')::boolean WHEN 'Aborted' THEN r->'PurgeApplied'='false'::jsonb AND NOT (r->>'OwnedFence')::boolean WHEN 'Unknown' THEN r->'PurgeApplied'='null'::jsonb AND (r->>'SourceFenced')::boolean ELSE FALSE END,FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE; END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recycle_purge_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recycle_purge_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.source_intent_id,NEW.source_proof_hash,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.source_intent_id,OLD.source_proof_hash,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_purge_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_purge_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recycle_purge_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recycle_purge_started_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.apply_started_at_utc IS NOT NULL AND NEW.apply_started_at_utc IS DISTINCT FROM OLD.apply_started_at_utc OR OLD.reservation_json IS NOT NULL AND ROW(NEW.reservation_json,NEW.reservation_hash) IS DISTINCT FROM ROW(OLD.reservation_json,OLD.reservation_hash) THEN RAISE EXCEPTION 'container_recycle_purge_reservation_immutable' USING ERRCODE='23514'; END IF;
      IF OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.purge_applied) IS DISTINCT FROM ROW(OLD.root_state,OLD.purge_applied) OR OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR (NEW.receipt_json::jsonb-ARRAY['SourceFenced','OwnedFence']) IS DISTINCT FROM (OLD.receipt_json::jsonb-ARRAY['SourceFenced','OwnedFence'])) THEN RAISE EXCEPTION 'container_recycle_purge_fact_immutable' USING ERRCODE='23514'; END IF;
      IF (NEW.reservation_json IS DISTINCT FROM OLD.reservation_json)<>(NEW.reservation_hash IS DISTINCT FROM OLD.reservation_hash) THEN RAISE EXCEPTION 'container_recycle_purge_reservation_authority_required' USING ERRCODE='23514'; END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recycle_purge_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Unknown') THEN
        RAISE EXCEPTION 'container_recycle_purge_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recycle_purge_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recycle_purge_guard BEFORE UPDATE OR DELETE ON container_recycle_purge_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_guard();
    CREATE FUNCTION whaledeck_container_recycle_purge_fence() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME<>'container_recycle_purge_intents' THEN
        IF TG_TABLE_NAME='file_path_locks' THEN
          IF EXISTS(SELECT 1 FROM container_recycle_purge_intents WHERE publish_barrier) THEN RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000'; END IF;
        ELSIF NEW.publish_barrier AND EXISTS(SELECT 1 FROM container_recycle_purge_intents WHERE publish_barrier) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_PURGE_TARGET_BUSY' USING ERRCODE='55000'; END IF;
      ELSIF NEW.publish_barrier AND (EXISTS(SELECT 1 FROM file_path_locks) OR EXISTS(SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_move_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_acl_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) OR EXISTS(SELECT 1 FROM container_recycle_purge_intents WHERE publish_barrier AND id<>NEW.id)) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_PURGE_TARGET_BUSY' USING ERRCODE='55000'; END IF;
      RETURN NEW; END $f$;
    CREATE TRIGGER whaledeck_recycle_purge_fence_0 BEFORE INSERT OR UPDATE ON file_path_locks FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_1 BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_2 BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_3 BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_4 BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_5 BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_6 BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_7 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_8 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_9 BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_10 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_11 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_restore_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_12 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_13 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_restore_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE TRIGGER whaledeck_recycle_purge_fence_14 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_purge_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_purge_fence();
    CREATE FUNCTION whaledeck_recycle_purge_source_fence() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='container_recycle_purge_intents' THEN
        IF EXISTS(SELECT 1 FROM container_recycle_restore_intents WHERE source_intent_id=NEW.source_intent_id AND (completed_at_utc IS NULL OR root_state='Unknown' OR restored_by_this_operation IS TRUE)) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_PURGE_SOURCE_CHANGED' USING ERRCODE='55000'; END IF;
      ELSIF EXISTS(SELECT 1 FROM container_recycle_purge_intents WHERE source_intent_id=NEW.source_intent_id AND (completed_at_utc IS NULL OR root_state='Unknown' OR purge_applied IS TRUE)) THEN RAISE EXCEPTION 'CONTAINER_RECYCLE_RESTORE_SOURCE_CHANGED' USING ERRCODE='55000'; END IF;
      RETURN NEW; END $f$;
    CREATE TRIGGER container_recycle_purge_source_fence BEFORE INSERT OR UPDATE ON container_recycle_purge_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_recycle_purge_source_fence();
    CREATE TRIGGER container_recycle_restore_purge_source_fence BEFORE INSERT OR UPDATE ON container_recycle_restore_intents FOR EACH ROW EXECUTE FUNCTION whaledeck_recycle_purge_source_fence();
    """;
}
