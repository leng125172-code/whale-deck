namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecursiveAclIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recursive_acl_uint(v jsonb, maximum numeric) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
      SELECT CASE WHEN jsonb_typeof(v)='number' AND v::text ~ '^(0|[1-9][0-9]{0,19})$' THEN (v::text)::numeric BETWEEN 0 AND maximum ELSE FALSE END
    $f$;
    CREATE FUNCTION whaledeck_recursive_acl_command_valid(c jsonb, cid text, instance text, path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    BEGIN
      RETURN COALESCE(jsonb_typeof(c)='object' AND c ?& ARRAY['Location','AclType','FileEntries','DirectoryEntries','ExpectedVersion']
        AND (SELECT count(*) FROM jsonb_object_keys(c))=5 AND jsonb_typeof(c->'Location')='object'
        AND (SELECT count(*) FROM jsonb_object_keys(c->'Location'))=3
        AND jsonb_typeof(c->'Location'->'ContainerId')='string' AND jsonb_typeof(c->'Location'->'InstanceId')='string' AND jsonb_typeof(c->'Location'->'Path')='string'
        AND jsonb_typeof(c->'ExpectedVersion')='string' AND c->'Location'->>'ContainerId'=cid AND c->'Location'->>'InstanceId'=instance AND c->'Location'->>'Path'=path
        AND c->>'ExpectedVersion' ~ '^[A-F0-9]{64}$'
        AND ((c->>'AclType'='access' AND whaledeck_acl_entries_valid(c->'FileEntries',FALSE) AND whaledeck_acl_entries_valid(c->'DirectoryEntries',FALSE))
          OR (c->>'AclType'='default' AND c->'FileEntries'='null'::jsonb AND whaledeck_acl_entries_valid(c->'DirectoryEntries',TRUE))),FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recursive_acl_plan_valid(p jsonb, c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; s jsonb; count_nodes integer; count_files integer; count_dirs integer; changes integer;
    BEGIN
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','Fingerprint','RootIdentity','RootChangedAtUnixNs','ManifestDigest','EntryCount','FileCount','DirectoryCount','ChangeCount']
        OR (SELECT count(*) FROM jsonb_object_keys(p))<>15 OR p->'Command' IS DISTINCT FROM c OR NOT whaledeck_recursive_acl_uint(p->'SchemaVersion',1) OR p->>'SchemaVersion'<>'1' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentVersion','Fingerprint','ManifestDigest'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['OwnerUid','OwnerGid'] LOOP
        IF NOT whaledeck_recursive_acl_uint(p->field,4294967294) THEN RETURN FALSE; END IF;
      END LOOP;
      IF jsonb_typeof(p->'ParentIdentity') IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p->'ParentIdentity'))<>2
        OR NOT whaledeck_recursive_acl_uint(p->'ParentIdentity'->'Device',18446744073709551615)
        OR NOT whaledeck_recursive_acl_uint(p->'ParentIdentity'->'Inode',18446744073709551615) OR (p->'ParentIdentity'->>'Inode')::numeric=0 THEN RETURN FALSE; END IF;
      s:=p->'RootIdentity';
      IF jsonb_typeof(s) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(s))<>8
        OR NOT whaledeck_recursive_acl_uint(s->'Device',18446744073709551615)
        OR NOT whaledeck_recursive_acl_uint(s->'Inode',18446744073709551615) OR (s->>'Inode')::numeric=0
        OR NOT whaledeck_recursive_acl_uint(s->'Mode',65535) OR ((s->>'Mode')::integer & 61440)<>16384 OR ((s->>'Mode')::integer & 3584)<>0
        OR NOT whaledeck_recursive_acl_uint(s->'OwnerUid',4294967294) OR NOT whaledeck_recursive_acl_uint(s->'OwnerGid',4294967294)
        OR NOT whaledeck_recursive_acl_uint(s->'LinkCount',18446744073709551615) OR (s->>'LinkCount')::numeric=0
        OR NOT whaledeck_recursive_acl_uint(s->'SizeBytes',9223372036854775807) THEN RETURN FALSE; END IF;
      IF jsonb_typeof(s->'ModifiedAtUnixNs') IS DISTINCT FROM 'number' OR s->>'ModifiedAtUnixNs' !~ '^-?(0|[1-9][0-9]{0,18})$'
        OR (s->>'ModifiedAtUnixNs')::numeric NOT BETWEEN -9223372036854775808 AND 9223372036854775807
        OR jsonb_typeof(p->'RootChangedAtUnixNs') IS DISTINCT FROM 'number' OR p->>'RootChangedAtUnixNs' !~ '^-?(0|[1-9][0-9]{0,18})$'
        OR (p->>'RootChangedAtUnixNs')::numeric NOT BETWEEN -9223372036854775808 AND 9223372036854775807 THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['EntryCount','FileCount','DirectoryCount','ChangeCount'] LOOP
        IF NOT whaledeck_recursive_acl_uint(p->field,10000) THEN RETURN FALSE; END IF;
      END LOOP;
      count_nodes:=(p->>'EntryCount')::integer;count_files:=(p->>'FileCount')::integer;count_dirs:=(p->>'DirectoryCount')::integer;changes:=(p->>'ChangeCount')::integer;
      RETURN count_nodes>0 AND count_dirs>0 AND count_nodes=count_files+count_dirs AND changes<=count_nodes AND (c->>'AclType'<>'default' OR changes<=count_dirs);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recursive_acl_receipt_valid(r jsonb, p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text;
    BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR NOT r ?& ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','BindingCurrent','Replayed','CompletedCount','ChangedCount','AbortedCount','NotStartedCount','UnknownCount','HistoryMayExist','HistoryVerified','HistoryDigest','HistoryAvailable','OutcomeDigest','AclType']
        OR (SELECT count(*) FROM jsonb_object_keys(r))<>19 OR r->'AclType' IS DISTINCT FROM p->'Command'->'AclType' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','HistoryDigest','OutcomeDigest','AclType'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['BindingCurrent','Replayed','HistoryMayExist','HistoryVerified','HistoryAvailable'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'boolean' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['CompletedCount','ChangedCount','AbortedCount','NotStartedCount','UnknownCount'] LOOP
        IF NOT whaledeck_recursive_acl_uint(r->field,10000) THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recursive_acl_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recursive_acl_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_acl_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_acl_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recursive_acl_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recursive_acl_started_immutable' USING ERRCODE='23514';
      END IF;
      IF (OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.completed_count,NEW.changed_count,NEW.aborted_count,NEW.not_started_count,NEW.unknown_count,NEW.outcome_digest,NEW.history_verified,NEW.history_digest)
        IS DISTINCT FROM ROW(OLD.root_state,OLD.completed_count,OLD.changed_count,OLD.aborted_count,OLD.not_started_count,OLD.unknown_count,OLD.outcome_digest,OLD.history_verified,OLD.history_digest)) OR
        (OLD.history_may_exist AND NOT NEW.history_may_exist) OR (OLD.history_verified AND NOT NEW.history_verified) OR
        (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) OR
        (OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR
          (NEW.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) IS DISTINCT FROM
          (OLD.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) OR
          (OLD.receipt_json::jsonb->>'HistoryMayExist')::boolean AND NOT (NEW.receipt_json::jsonb->>'HistoryMayExist')::boolean)) THEN
        RAISE EXCEPTION 'container_recursive_acl_fact_immutable' USING ERRCODE='23514';
      END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recursive_acl_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Partial','Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Partial','Unknown') THEN
        RAISE EXCEPTION 'container_recursive_acl_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recursive_acl_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recursive_acl_guard BEFORE UPDATE OR DELETE ON container_recursive_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_guard();
    CREATE FUNCTION whaledeck_container_recursive_acl_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_directory_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_copy_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_move_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
        EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier AND id<>NEW.id)) THEN
        RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_TARGET_BUSY' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER whaledeck_recursive_acl_host_path_fence BEFORE INSERT OR UPDATE ON file_path_locks
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_directory_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_copy_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_move_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_recursive_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_acl_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    CREATE TRIGGER whaledeck_recursive_acl_recursive_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_fence();
    """;
}
