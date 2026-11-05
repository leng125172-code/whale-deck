namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecycleRestoreIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recycle_restore_command_valid(c jsonb, cid text, instance text, path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE s jsonb; p jsonb;
    BEGIN
      s:=c->'Source';p:=s->'Plan';
      RETURN COALESCE(jsonb_typeof(c)='object' AND (SELECT count(*) FROM jsonb_object_keys(c))=2 AND c ?& ARRAY['Source','HistoryDigest']
        AND jsonb_typeof(c->'HistoryDigest')='string' AND c->>'HistoryDigest' ~ '^[A-F0-9]{64}$'
        AND jsonb_typeof(s)='object' AND (SELECT count(*) FROM jsonb_object_keys(s))=3 AND s ?& ARRAY['OperationId','Owner','Plan']
        AND jsonb_typeof(s->'OperationId')='string' AND s->>'OperationId' ~ '^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$' AND (s->>'OperationId')::uuid<>'00000000-0000-0000-0000-000000000000'::uuid
        AND jsonb_typeof(s->'Owner')='string' AND s->>'Owner' ~ '^[A-F0-9]{64}$'
        AND whaledeck_recycle_command_valid(p->'Command',cid,instance,path) AND whaledeck_recycle_plan_valid(p,p->'Command'),FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recycle_restore_plan_valid(p jsonb,c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE original jsonb; field text;
    BEGIN
      original:=c->'Source'->'Plan';
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p))<>11 OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','PrivateIdentity','RetainedIdentity','RetainedChangedAtUnixNs','Fingerprint']
        OR p->'Command' IS DISTINCT FROM c OR NOT whaledeck_recycle_uint(p->'SchemaVersion',1) OR p->>'SchemaVersion'<>'1' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentVersion','Fingerprint'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      IF p->'TargetProof' IS DISTINCT FROM original->'TargetProof' OR p->'ParentIdentity' IS DISTINCT FROM original->'ParentIdentity'
        OR p->'OwnerUid' IS DISTINCT FROM original->'OwnerUid' OR p->'OwnerGid' IS DISTINCT FROM original->'OwnerGid'
        OR p->'RetainedIdentity' IS DISTINCT FROM original->'SourceIdentity' OR NOT whaledeck_recycle_sint(p->'RetainedChangedAtUnixNs')
        OR (p->>'RetainedChangedAtUnixNs')::bigint < (original->>'SourceChangedAtUnixNs')::bigint THEN RETURN FALSE; END IF;
      RETURN COALESCE(jsonb_typeof(p->'PrivateIdentity')='object' AND (SELECT count(*) FROM jsonb_object_keys(p->'PrivateIdentity'))=2
        AND whaledeck_recycle_uint(p->'PrivateIdentity'->'Device',18446744073709551615) AND whaledeck_recycle_uint(p->'PrivateIdentity'->'Inode',18446744073709551615)
        AND (p->'PrivateIdentity'->>'Inode')::numeric>0,FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recycle_restore_receipt_valid(r jsonb,p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text;
    BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(r))<>14 OR NOT r ?& ARRAY['Id','SourceOperationId','Fingerprint','TargetProof','State','BindingCurrent','Replayed','HistoryVerified','HistoryDigest','HistoryAvailable','SourceState','SourceRetained','SourceRestored','RestoredByThisOperation'] THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','SourceOperationId','Fingerprint','TargetProof','State','HistoryDigest'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['BindingCurrent','Replayed','HistoryVerified','HistoryAvailable'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'boolean' THEN RETURN FALSE; END IF;
      END LOOP;
      IF jsonb_typeof(r->'SourceState') NOT IN ('string','null') THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['SourceRetained','SourceRestored','RestoredByThisOperation'] LOOP
        IF jsonb_typeof(r->field) NOT IN ('boolean','null') THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recycle_restore_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recycle_restore_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.source_intent_id,NEW.source_proof_hash,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.source_intent_id,OLD.source_proof_hash,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_restore_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_restore_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recycle_restore_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recycle_restore_started_immutable' USING ERRCODE='23514';
      END IF;
      IF (OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.source_state,NEW.source_retained,NEW.source_restored,NEW.restored_by_this_operation,NEW.history_verified,NEW.history_digest)
        IS DISTINCT FROM ROW(OLD.root_state,OLD.source_state,OLD.source_retained,OLD.source_restored,OLD.restored_by_this_operation,OLD.history_verified,OLD.history_digest)) OR
        (OLD.history_verified AND NOT NEW.history_verified) OR
        (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) OR
        (OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR
          (NEW.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable']) IS DISTINCT FROM
          (OLD.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable']))) THEN
        RAISE EXCEPTION 'container_recycle_restore_fact_immutable' USING ERRCODE='23514';
      END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recycle_restore_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Unknown') THEN
        RAISE EXCEPTION 'container_recycle_restore_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recycle_restore_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recycle_restore_guard BEFORE UPDATE OR DELETE ON container_recycle_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_guard();
    CREATE FUNCTION whaledeck_container_recycle_restore_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_directory_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_copy_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_move_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_acl_restore_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recycle_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECYCLE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) OR EXISTS (SELECT 1 FROM file_path_locks) OR
        EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recycle_restore_intents WHERE publish_barrier AND id<>NEW.id)) THEN
        RAISE EXCEPTION 'CONTAINER_RECYCLE_RESTORE_TARGET_BUSY' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER whaledeck_recycle_restore_fence_0 BEFORE INSERT OR UPDATE ON file_path_locks
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_1 BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_2 BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_3 BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_4 BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_5 BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_6 BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_7 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_8 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_9 BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_10 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_11 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_12 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    CREATE TRIGGER whaledeck_recycle_restore_fence_13 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_restore_fence();
    """;
}
