namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecycleIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recycle_uint(v jsonb, maximum numeric) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
      SELECT CASE WHEN jsonb_typeof(v)='number' AND v::text ~ '^(0|[1-9][0-9]{0,19})$' THEN (v::text)::numeric BETWEEN 0 AND maximum ELSE FALSE END
    $f$;
    CREATE FUNCTION whaledeck_recycle_sint(v jsonb) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
      SELECT CASE WHEN jsonb_typeof(v)='number' AND v::text ~ '^-?(0|[1-9][0-9]{0,18})$' THEN (v::text)::numeric BETWEEN -9223372036854775808 AND 9223372036854775807 ELSE FALSE END
    $f$;
    CREATE FUNCTION whaledeck_recycle_instance_valid(v text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE stamp text;
    BEGIN
      IF v IS NULL OR v !~ '^[1-9][0-9]{0,9}:[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,9})?Z$'
        OR split_part(v,':',1)::bigint NOT BETWEEN 2 AND 2147483647 THEN RETURN FALSE; END IF;
      stamp:=substring(v FROM position(':' IN v)+1);
      RETURN substring(stamp,1,4)::integer BETWEEN 1 AND 9999 AND make_date(substring(stamp,1,4)::integer,substring(stamp,6,2)::integer,substring(stamp,9,2)::integer) IS NOT NULL
        AND substring(stamp,12,2)::integer BETWEEN 0 AND 23 AND substring(stamp,15,2)::integer BETWEEN 0 AND 59 AND substring(stamp,18,2)::integer BETWEEN 0 AND 59;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recycle_command_valid(c jsonb, cid text, instance text, path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    BEGIN
      RETURN COALESCE(jsonb_typeof(c)='object' AND c ?& ARRAY['Location','ExpectedVersion']
        AND (SELECT count(*) FROM jsonb_object_keys(c))=2 AND jsonb_typeof(c->'Location')='object'
        AND (SELECT count(*) FROM jsonb_object_keys(c->'Location'))=3
        AND jsonb_typeof(c->'Location'->'ContainerId')='string' AND jsonb_typeof(c->'Location'->'InstanceId')='string' AND jsonb_typeof(c->'Location'->'Path')='string'
        AND c->'Location'->>'ContainerId'=cid AND c->'Location'->>'InstanceId'=instance AND c->'Location'->>'Path'=path AND whaledeck_recycle_instance_valid(instance)
        AND path !~ '[[:cntrl:]]' AND position(chr(92) in path)=0 AND octet_length(path) BETWEEN 1 AND 4096
        AND NOT EXISTS (SELECT 1 FROM unnest(string_to_array(path,'/')) segment WHERE segment IN ('','.','..') OR octet_length(segment)>255)
        AND jsonb_typeof(c->'ExpectedVersion')='string' AND c->>'ExpectedVersion' ~ '^[A-F0-9]{64}$',FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recycle_plan_valid(p jsonb, c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; s jsonb; attr jsonb; name_bytes bytea; previous_name bytea; data bytea; total_bytes integer:=0;
    BEGIN
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','Fingerprint','SourceIdentity','SourceChangedAtUnixNs','Sha256','Attributes']
        OR (SELECT count(*) FROM jsonb_object_keys(p))<>12 OR p->'Command' IS DISTINCT FROM c OR NOT whaledeck_recycle_uint(p->'SchemaVersion',1) OR p->>'SchemaVersion'<>'1' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentVersion','Fingerprint','Sha256'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['OwnerUid','OwnerGid'] LOOP
        IF NOT whaledeck_recycle_uint(p->field,4294967294) THEN RETURN FALSE; END IF;
      END LOOP;
      IF jsonb_typeof(p->'ParentIdentity') IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p->'ParentIdentity'))<>2
        OR NOT whaledeck_recycle_uint(p->'ParentIdentity'->'Device',18446744073709551615)
        OR NOT whaledeck_recycle_uint(p->'ParentIdentity'->'Inode',18446744073709551615) OR (p->'ParentIdentity'->>'Inode')::numeric=0 THEN RETURN FALSE; END IF;
      s:=p->'SourceIdentity';
      IF jsonb_typeof(s) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(s))<>8
        OR NOT whaledeck_recycle_uint(s->'Device',18446744073709551615)
        OR NOT whaledeck_recycle_uint(s->'Inode',18446744073709551615) OR (s->>'Inode')::numeric=0
        OR NOT whaledeck_recycle_uint(s->'Mode',65535) OR ((s->>'Mode')::integer & 61440)<>32768 OR ((s->>'Mode')::integer & 3584)<>0
        OR NOT whaledeck_recycle_uint(s->'OwnerUid',4294967294) OR NOT whaledeck_recycle_uint(s->'OwnerGid',4294967294)
        OR NOT whaledeck_recycle_uint(s->'LinkCount',1) OR (s->>'LinkCount')::numeric<>1
        OR NOT whaledeck_recycle_uint(s->'SizeBytes',1073741824)
        OR NOT whaledeck_recycle_sint(s->'ModifiedAtUnixNs') OR NOT whaledeck_recycle_sint(p->'SourceChangedAtUnixNs') THEN RETURN FALSE; END IF;
      IF jsonb_typeof(p->'Attributes') IS DISTINCT FROM 'array' OR jsonb_array_length(p->'Attributes')>32 THEN RETURN FALSE; END IF;
      FOR attr IN SELECT value FROM jsonb_array_elements(p->'Attributes') LOOP
        IF jsonb_typeof(attr) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(attr))<>2
          OR jsonb_typeof(attr->'Name') IS DISTINCT FROM 'string' OR jsonb_typeof(attr->'Value') IS DISTINCT FROM 'string'
          OR left(attr->>'Name',5)<>'user.' THEN RETURN FALSE; END IF;
        name_bytes:=convert_to(attr->>'Name','UTF8');data:=decode(attr->>'Value','base64');
        IF octet_length(name_bytes)>255 OR (previous_name IS NOT NULL AND name_bytes<=previous_name)
          OR replace(encode(data,'base64'),chr(10),'')<>attr->>'Value' THEN RETURN FALSE; END IF;
        total_bytes:=total_bytes+octet_length(name_bytes)+octet_length(data);previous_name:=name_bytes;
        IF total_bytes>4096 THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recycle_receipt_valid(r jsonb, p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text;
    BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR NOT r ?& ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','BindingCurrent','Replayed','HistoryMayExist','HistoryVerified','HistoryDigest','HistoryAvailable','SourceRetained','SourceRestored']
        OR (SELECT count(*) FROM jsonb_object_keys(r))<>14 THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','HistoryDigest'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['BindingCurrent','Replayed','HistoryMayExist','HistoryVerified','HistoryAvailable'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'boolean' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['SourceRetained','SourceRestored'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM (CASE WHEN r->>'State'='Unknown' THEN 'null' ELSE 'boolean' END) THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recycle_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recycle_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recycle_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recycle_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recycle_started_immutable' USING ERRCODE='23514';
      END IF;
      IF (OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.source_retained,NEW.source_restored,NEW.history_verified,NEW.history_digest)
        IS DISTINCT FROM ROW(OLD.root_state,OLD.source_retained,OLD.source_restored,OLD.history_verified,OLD.history_digest)) OR
        (OLD.history_may_exist AND NOT NEW.history_may_exist) OR (OLD.history_verified AND NOT NEW.history_verified) OR
        (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) OR
        (OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR
          (NEW.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) IS DISTINCT FROM
          (OLD.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) OR
          (OLD.receipt_json::jsonb->>'HistoryMayExist')::boolean AND NOT (NEW.receipt_json::jsonb->>'HistoryMayExist')::boolean)) THEN
        RAISE EXCEPTION 'container_recycle_fact_immutable' USING ERRCODE='23514';
      END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recycle_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Unknown') THEN
        RAISE EXCEPTION 'container_recycle_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recycle_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recycle_guard BEFORE UPDATE OR DELETE ON container_recycle_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_guard();
    CREATE FUNCTION whaledeck_container_recycle_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_directory_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_copy_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_move_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_acl_restore_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
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
        EXISTS (SELECT 1 FROM container_recycle_intents WHERE publish_barrier AND id<>NEW.id)) THEN
        RAISE EXCEPTION 'CONTAINER_RECYCLE_TARGET_BUSY' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER whaledeck_recycle_fence_0 BEFORE INSERT OR UPDATE ON file_path_locks
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_1 BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_2 BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_3 BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_4 BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_5 BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_6 BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_7 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_8 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_9 BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_10 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_11 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    CREATE TRIGGER whaledeck_recycle_fence_12 BEFORE INSERT OR UPDATE OF publish_barrier ON container_recycle_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recycle_fence();
    """;
}
