namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerAclIntents
{
    private const string EntryFunctions = """
    
    CREATE FUNCTION whaledeck_acl_entries_valid(entries jsonb, allow_empty boolean) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE item jsonb; rank integer; previous_rank integer := 0; numeric_id bigint; previous_id bigint := -1;
      have_owner boolean := FALSE; have_group boolean := FALSE; have_other boolean := FALSE; have_mask boolean := FALSE; named boolean := FALSE; key_count integer;
    BEGIN
      IF jsonb_typeof(entries) IS DISTINCT FROM 'array' OR allow_empty IS NULL THEN RETURN FALSE; END IF;
      IF jsonb_array_length(entries)=0 THEN RETURN allow_empty; END IF;
      IF jsonb_array_length(entries)>64 THEN RETURN FALSE; END IF;
      FOR item IN SELECT value FROM jsonb_array_elements(entries) LOOP
        IF jsonb_typeof(item) IS DISTINCT FROM 'object' OR NOT (item ?& ARRAY['Tag','Id','Permissions']) THEN RETURN FALSE; END IF;
        SELECT count(*) INTO key_count FROM jsonb_object_keys(item);
        IF key_count<>3 OR jsonb_typeof(item->'Tag') IS DISTINCT FROM 'string' OR jsonb_typeof(item->'Permissions') IS DISTINCT FROM 'number' OR item->>'Permissions' !~ '^[0-7]$' THEN RETURN FALSE; END IF;
        rank := CASE item->>'Tag' WHEN 'user_obj' THEN 1 WHEN 'user' THEN 2 WHEN 'group_obj' THEN 4 WHEN 'group' THEN 8 WHEN 'mask' THEN 16 WHEN 'other' THEN 32 ELSE 0 END;
        IF rank=0 THEN RETURN FALSE; END IF;
        numeric_id := -1;
        IF rank IN (2,8) THEN
          IF jsonb_typeof(item->'Id') IS DISTINCT FROM 'number' OR item->>'Id' !~ '^(0|[1-9][0-9]{0,9})$' THEN RETURN FALSE; END IF;
          IF (item->>'Id')::numeric>4294967294 THEN RETURN FALSE; END IF;
          numeric_id := (item->>'Id')::bigint; named := TRUE;
        ELSIF item->'Id' IS DISTINCT FROM 'null'::jsonb THEN RETURN FALSE;
        END IF;
        IF rank<previous_rank OR rank=previous_rank AND numeric_id<=previous_id THEN RETURN FALSE; END IF;
        previous_rank := rank; previous_id := numeric_id;
        have_owner := have_owner OR rank=1; have_group := have_group OR rank=4; have_mask := have_mask OR rank=16; have_other := have_other OR rank=32;
      END LOOP;
      RETURN have_owner AND have_group AND have_other AND (NOT named OR have_mask);
    END $f$;
    CREATE FUNCTION whaledeck_acl_mode(entries jsonb) RETURNS integer LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE item jsonb; owner_bits integer; group_bits integer; mask_bits integer; other_bits integer;
    BEGIN
      IF NOT whaledeck_acl_entries_valid(entries,FALSE) THEN RETURN NULL; END IF;
      FOR item IN SELECT value FROM jsonb_array_elements(entries) LOOP
        CASE item->>'Tag' WHEN 'user_obj' THEN owner_bits := (item->>'Permissions')::integer;
          WHEN 'group_obj' THEN group_bits := (item->>'Permissions')::integer;
          WHEN 'mask' THEN mask_bits := (item->>'Permissions')::integer;
          WHEN 'other' THEN other_bits := (item->>'Permissions')::integer; ELSE NULL;
        END CASE;
      END LOOP;
      RETURN (owner_bits << 6) | (COALESCE(mask_bits,group_bits) << 3) | other_bits;
    END $f$;
    """;
    private const string GuardAndFences = """
        CREATE FUNCTION whaledeck_container_acl_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
        BEGIN
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_acl_permanent_intent' USING ERRCODE='23514'; END IF;
          IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
            IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
            RAISE EXCEPTION 'container_acl_request_immutable' USING ERRCODE='23514';
          END IF;
          IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
            RAISE EXCEPTION 'container_acl_plan_immutable' USING ERRCODE='23514';
          END IF;
          IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
            IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
            RAISE EXCEPTION 'container_acl_execution_immutable' USING ERRCODE='23514';
          END IF;
          IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
            RAISE EXCEPTION 'container_acl_started_immutable' USING ERRCODE='23514';
          END IF;
          IF (OLD.receipt_json IS NOT NULL AND ROW(NEW.receipt_json,NEW.receipt_hash,NEW.acl_confirmed,NEW.published_version,NEW.history_verified,NEW.history_digest)
            IS DISTINCT FROM ROW(OLD.receipt_json,OLD.receipt_hash,OLD.acl_confirmed,OLD.published_version,OLD.history_verified,OLD.history_digest)) OR
            (OLD.history_may_exist AND NOT NEW.history_may_exist) OR (OLD.history_verified AND NOT NEW.history_verified) OR
            (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) THEN
            RAISE EXCEPTION 'container_acl_fact_immutable' USING ERRCODE='23514';
          END IF;
          IF OLD.state='Unknown' AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
            NEW.state='Acknowledged' AND OLD.state<>'Unknown' THEN
            RAISE EXCEPTION 'container_acl_resolution_required' USING ERRCODE='23514';
          END IF;
          IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
            RAISE EXCEPTION 'container_acl_terminal_immutable' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $f$;
        CREATE TRIGGER container_acl_guard BEFORE UPDATE OR DELETE ON container_acl_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_guard();
        CREATE FUNCTION whaledeck_container_acl_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
        BEGIN
          PERFORM pg_advisory_xact_lock(41307341);
          IF TG_TABLE_NAME='file_path_locks' THEN
            IF EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_directory_operations' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_copy_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_move_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_permission_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_recursive_ownership_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
            IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) THEN
              RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
            END IF;
          ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
            EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR
            EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier AND id<>NEW.id)) THEN
            RAISE EXCEPTION 'CONTAINER_ACL_TARGET_BUSY' USING ERRCODE='55000';
          END IF;
          RETURN NEW;
        END $f$;
        CREATE TRIGGER whaledeck_acl_host_path_fence BEFORE INSERT OR UPDATE ON file_path_locks
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_directory_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_copy_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_move_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_recursive_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_recursive_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
        CREATE TRIGGER whaledeck_acl_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents
          FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_fence();
    """;
    private const string RemoveFencesIfEmpty = """
    DO $f$ BEGIN
      IF EXISTS (SELECT 1 FROM container_acl_intents) THEN RAISE EXCEPTION 'container_acl_downgrade_requires_empty_table'; END IF;
    END $f$;
    DROP TRIGGER whaledeck_acl_host_path_fence ON file_path_locks;
    DROP TRIGGER whaledeck_acl_upload_fence ON container_file_upload_operations;
    DROP TRIGGER whaledeck_acl_directory_fence ON container_directory_operations;
    DROP TRIGGER whaledeck_acl_copy_fence ON container_copy_intents;
    DROP TRIGGER whaledeck_acl_move_fence ON container_move_intents;
    DROP TRIGGER whaledeck_acl_permission_fence ON container_permission_intents;
    DROP TRIGGER whaledeck_acl_ownership_fence ON container_ownership_intents;
    DROP TRIGGER whaledeck_acl_recursive_permission_fence ON container_recursive_permission_intents;
    DROP TRIGGER whaledeck_acl_recursive_ownership_fence ON container_recursive_ownership_intents;
    """;
}
