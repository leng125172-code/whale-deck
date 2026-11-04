namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class ContainerRecursiveAclRestoreIntents
{
    private const string ShapeFunctions = """
    CREATE FUNCTION whaledeck_recursive_acl_restore_uint(v jsonb, maximum numeric) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
      SELECT CASE WHEN jsonb_typeof(v)='number' AND v::text ~ '^(0|[1-9][0-9]{0,19})$' THEN (v::text)::numeric BETWEEN 0 AND maximum ELSE FALSE END
    $f$;
    CREATE FUNCTION whaledeck_recursive_acl_restore_sint(v jsonb) RETURNS boolean LANGUAGE sql IMMUTABLE AS $f$
      SELECT CASE WHEN jsonb_typeof(v)='number' AND v::text ~ '^-?(0|[1-9][0-9]{0,18})$' THEN (v::text)::numeric BETWEEN -9223372036854775808 AND 9223372036854775807 ELSE FALSE END
    $f$;
    CREATE FUNCTION whaledeck_recursive_acl_restore_instance_valid(v text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE stamp text;
    BEGIN
      IF v IS NULL OR v !~ '^[1-9][0-9]{0,9}:[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,9})?Z$'
        OR split_part(v,':',1)::bigint NOT BETWEEN 2 AND 2147483647 THEN RETURN FALSE; END IF;
      stamp:=substring(v FROM position(':' IN v)+1);
      RETURN substring(stamp,1,4)::integer BETWEEN 1 AND 9999 AND make_date(substring(stamp,1,4)::integer,substring(stamp,6,2)::integer,substring(stamp,9,2)::integer) IS NOT NULL
        AND substring(stamp,12,2)::integer BETWEEN 0 AND 23 AND substring(stamp,15,2)::integer BETWEEN 0 AND 59 AND substring(stamp,18,2)::integer BETWEEN 0 AND 59;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recursive_acl_restore_command_valid(c jsonb, cid text, instance text, path text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    BEGIN
      RETURN COALESCE(jsonb_typeof(c)='object' AND c ?& ARRAY['Location','CaptureId','ExpectedHistoryDigest','ExpectedVersion']
        AND (SELECT count(*) FROM jsonb_object_keys(c))=4 AND jsonb_typeof(c->'Location')='object'
        AND (SELECT count(*) FROM jsonb_object_keys(c->'Location'))=3
        AND jsonb_typeof(c->'Location'->'ContainerId')='string' AND jsonb_typeof(c->'Location'->'InstanceId')='string' AND jsonb_typeof(c->'Location'->'Path')='string'
        AND c->'Location'->>'ContainerId'=cid AND c->'Location'->>'InstanceId'=instance AND c->'Location'->>'Path'=path AND whaledeck_recursive_acl_restore_instance_valid(instance)
        AND jsonb_typeof(c->'CaptureId')='string' AND c->>'CaptureId' ~ '^[a-f0-9]{32}$' AND c->>'CaptureId'<>'00000000000000000000000000000000'
        AND jsonb_typeof(c->'ExpectedHistoryDigest')='string' AND c->>'ExpectedHistoryDigest' ~ '^[A-F0-9]{64}$'
        AND jsonb_typeof(c->'ExpectedVersion')='string' AND c->>'ExpectedVersion' ~ '^[A-F0-9]{64}$',FALSE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recursive_acl_restore_plan_valid(p jsonb, c jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text; s jsonb; count_nodes integer; count_files integer; count_dirs integer; changes integer; h jsonb; f jsonb;
    BEGIN
      IF jsonb_typeof(p) IS DISTINCT FROM 'object' OR NOT p ?& ARRAY['SchemaVersion','Command','TargetProof','ParentIdentity','ParentVersion','OwnerUid','OwnerGid','Fingerprint','RootIdentity','RootChangedAtUnixNs','ManifestDigest','EntryCount','FileCount','DirectoryCount','ChangeCount','RestoreSource','RestoreSourceFrame','RestoreTargetsDigest']
        OR (SELECT count(*) FROM jsonb_object_keys(p))<>18 OR p->'Command' IS DISTINCT FROM c OR NOT whaledeck_recursive_acl_restore_uint(p->'SchemaVersion',1) OR p->>'SchemaVersion'<>'1' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['TargetProof','ParentVersion','Fingerprint','ManifestDigest','RestoreTargetsDigest'] LOOP
        IF jsonb_typeof(p->field) IS DISTINCT FROM 'string' OR p->>field !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['OwnerUid','OwnerGid'] LOOP
        IF NOT whaledeck_recursive_acl_restore_uint(p->field,4294967294) THEN RETURN FALSE; END IF;
      END LOOP;
      IF jsonb_typeof(p->'ParentIdentity') IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(p->'ParentIdentity'))<>2
        OR NOT whaledeck_recursive_acl_restore_uint(p->'ParentIdentity'->'Device',18446744073709551615)
        OR NOT whaledeck_recursive_acl_restore_uint(p->'ParentIdentity'->'Inode',18446744073709551615) OR (p->'ParentIdentity'->>'Inode')::numeric=0 THEN RETURN FALSE; END IF;
      s:=p->'RootIdentity';
      IF jsonb_typeof(s) IS DISTINCT FROM 'object' OR (SELECT count(*) FROM jsonb_object_keys(s))<>8
        OR NOT whaledeck_recursive_acl_restore_uint(s->'Device',18446744073709551615)
        OR NOT whaledeck_recursive_acl_restore_uint(s->'Inode',18446744073709551615) OR (s->>'Inode')::numeric=0
        OR NOT whaledeck_recursive_acl_restore_uint(s->'Mode',65535) OR ((s->>'Mode')::integer & 61440)<>16384 OR ((s->>'Mode')::integer & 3584)<>0
        OR NOT whaledeck_recursive_acl_restore_uint(s->'OwnerUid',4294967294) OR NOT whaledeck_recursive_acl_restore_uint(s->'OwnerGid',4294967294)
        OR NOT whaledeck_recursive_acl_restore_uint(s->'LinkCount',18446744073709551615) OR (s->>'LinkCount')::numeric=0
        OR NOT whaledeck_recursive_acl_restore_uint(s->'SizeBytes',9223372036854775807) THEN RETURN FALSE; END IF;
      IF jsonb_typeof(s->'ModifiedAtUnixNs') IS DISTINCT FROM 'number' OR s->>'ModifiedAtUnixNs' !~ '^-?(0|[1-9][0-9]{0,18})$'
        OR (s->>'ModifiedAtUnixNs')::numeric NOT BETWEEN -9223372036854775808 AND 9223372036854775807
        OR jsonb_typeof(p->'RootChangedAtUnixNs') IS DISTINCT FROM 'number' OR p->>'RootChangedAtUnixNs' !~ '^-?(0|[1-9][0-9]{0,18})$'
        OR (p->>'RootChangedAtUnixNs')::numeric NOT BETWEEN -9223372036854775808 AND 9223372036854775807 THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['EntryCount','FileCount','DirectoryCount','ChangeCount'] LOOP
        IF NOT whaledeck_recursive_acl_restore_uint(p->field,10000) THEN RETURN FALSE; END IF;
      END LOOP;
      count_nodes:=(p->>'EntryCount')::integer;count_files:=(p->>'FileCount')::integer;count_dirs:=(p->>'DirectoryCount')::integer;changes:=(p->>'ChangeCount')::integer;
      h:=p->'RestoreSource'; f:=p->'RestoreSourceFrame';
      IF jsonb_typeof(h) IS DISTINCT FROM 'object' OR NOT h ?& ARRAY['CaptureId','HistoryDigest','CreatedAtUnixMs','SourceInstanceId','ChangedAclType','ManifestDigest','EntryCount','FileCount','DirectoryCount','ChangeCount']
        OR (SELECT count(*) FROM jsonb_object_keys(h))<>10 OR h->'CaptureId' IS DISTINCT FROM c->'CaptureId' OR h->'HistoryDigest' IS DISTINCT FROM c->'ExpectedHistoryDigest'
        OR NOT whaledeck_recursive_acl_restore_uint(h->'CreatedAtUnixMs',9223372036854775807)
        OR jsonb_typeof(h->'SourceInstanceId') IS DISTINCT FROM 'string' OR NOT whaledeck_recursive_acl_restore_instance_valid(h->>'SourceInstanceId')
        OR jsonb_typeof(h->'ChangedAclType') IS DISTINCT FROM 'string' OR h->>'ChangedAclType' NOT IN ('access','default')
        OR jsonb_typeof(h->'ManifestDigest') IS DISTINCT FROM 'string' OR h->>'ManifestDigest' !~ '^[A-F0-9]{64}$' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['EntryCount','FileCount','DirectoryCount','ChangeCount'] LOOP
        IF NOT whaledeck_recursive_acl_restore_uint(h->field,10000) THEN RETURN FALSE; END IF;
      END LOOP;
      IF h->'EntryCount' IS DISTINCT FROM p->'EntryCount' OR h->'FileCount' IS DISTINCT FROM p->'FileCount' OR h->'DirectoryCount' IS DISTINCT FROM p->'DirectoryCount'
        OR (h->>'ChangeCount')::integer>count_nodes OR h->>'ChangedAclType'='default' AND (h->>'ChangeCount')::integer>count_dirs THEN RETURN FALSE; END IF;
      IF jsonb_typeof(f) IS DISTINCT FROM 'object' OR NOT f ?& ARRAY['Device','Inode','Mode','OwnerUid','OwnerGid','LinkCount','SizeBytes','ModifiedAtUnixNs','ChangedAtUnixNs']
        OR (SELECT count(*) FROM jsonb_object_keys(f))<>9 OR NOT whaledeck_recursive_acl_restore_uint(f->'Device',18446744073709551615)
        OR NOT whaledeck_recursive_acl_restore_uint(f->'Inode',18446744073709551615) OR (f->>'Inode')::numeric=0
        OR NOT whaledeck_recursive_acl_restore_uint(f->'Mode',33152) OR f->>'Mode'<>'33152'
        OR NOT whaledeck_recursive_acl_restore_uint(f->'OwnerUid',0) OR NOT whaledeck_recursive_acl_restore_uint(f->'OwnerGid',0)
        OR NOT whaledeck_recursive_acl_restore_uint(f->'LinkCount',1) OR f->>'LinkCount'<>'1'
        OR NOT whaledeck_recursive_acl_restore_uint(f->'SizeBytes',16777216) OR (f->>'SizeBytes')::numeric=0
        OR NOT whaledeck_recursive_acl_restore_sint(f->'ModifiedAtUnixNs') OR NOT whaledeck_recursive_acl_restore_sint(f->'ChangedAtUnixNs') THEN RETURN FALSE; END IF;
      RETURN count_nodes>0 AND count_dirs>0 AND count_nodes=count_files+count_dirs AND changes<=count_nodes AND (h->>'ChangedAclType'<>'default' OR changes<=count_dirs);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_recursive_acl_restore_receipt_valid(r jsonb, p jsonb) RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE field text;
    BEGIN
      IF jsonb_typeof(r) IS DISTINCT FROM 'object' OR NOT r ?& ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','BindingCurrent','Replayed','CompletedCount','ChangedCount','AbortedCount','NotStartedCount','UnknownCount','HistoryMayExist','HistoryVerified','HistoryDigest','HistoryAvailable','OutcomeDigest','AclType']
        OR (SELECT count(*) FROM jsonb_object_keys(r))<>19 OR r->'AclType' IS DISTINCT FROM p->'RestoreSource'->'ChangedAclType' THEN RETURN FALSE; END IF;
      FOREACH field IN ARRAY ARRAY['Id','ContainerId','InstanceId','Fingerprint','TargetProof','State','HistoryDigest','OutcomeDigest','AclType'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'string' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['BindingCurrent','Replayed','HistoryMayExist','HistoryVerified','HistoryAvailable'] LOOP
        IF jsonb_typeof(r->field) IS DISTINCT FROM 'boolean' THEN RETURN FALSE; END IF;
      END LOOP;
      FOREACH field IN ARRAY ARRAY['CompletedCount','ChangedCount','AbortedCount','NotStartedCount','UnknownCount'] LOOP
        IF NOT whaledeck_recursive_acl_restore_uint(r->field,10000) THEN RETURN FALSE; END IF;
      END LOOP;
      RETURN TRUE;
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    """;
    private const string GuardAndFences = """
    CREATE FUNCTION whaledeck_container_recursive_acl_restore_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF TG_OP='DELETE' THEN RAISE EXCEPTION 'container_recursive_acl_restore_permanent_intent' USING ERRCODE='23514'; END IF;
      IF ROW(NEW.id,NEW.idempotency_key,NEW.actor_subject,NEW.login_binding,NEW.trace_id,NEW.owner_hash,NEW.container_id,NEW.instance_id,NEW.relative_path,NEW.command_json,NEW.request_hash,NEW.created_at_utc,NEW.planning_expires_at_utc)
        IS DISTINCT FROM ROW(OLD.id,OLD.idempotency_key,OLD.actor_subject,OLD.login_binding,OLD.trace_id,OLD.owner_hash,OLD.container_id,OLD.instance_id,OLD.relative_path,OLD.command_json,OLD.request_hash,OLD.created_at_utc,OLD.planning_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_request_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.plan_json IS NOT NULL AND ROW(NEW.plan_json,NEW.plan_hash,NEW.ready_expires_at_utc) IS DISTINCT FROM ROW(OLD.plan_json,OLD.plan_hash,OLD.ready_expires_at_utc) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_plan_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.execution_job_id IS NOT NULL AND ROW(NEW.execution_job_id,NEW.confirmed_at_utc,NEW.execution_expires_at_utc,NEW.execution_hash)
        IS DISTINCT FROM ROW(OLD.execution_job_id,OLD.confirmed_at_utc,OLD.execution_expires_at_utc,OLD.execution_hash) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_execution_immutable' USING ERRCODE='23514';
      END IF;
      IF OLD.root_started_at_utc IS NOT NULL AND NEW.root_started_at_utc IS DISTINCT FROM OLD.root_started_at_utc THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_started_immutable' USING ERRCODE='23514';
      END IF;
      IF (OLD.root_state IS NOT NULL AND ROW(NEW.root_state,NEW.completed_count,NEW.changed_count,NEW.aborted_count,NEW.not_started_count,NEW.unknown_count,NEW.outcome_digest,NEW.history_verified,NEW.history_digest)
        IS DISTINCT FROM ROW(OLD.root_state,OLD.completed_count,OLD.changed_count,OLD.aborted_count,OLD.not_started_count,OLD.unknown_count,OLD.outcome_digest,OLD.history_verified,OLD.history_digest)) OR
        (OLD.history_may_exist AND NOT NEW.history_may_exist) OR (OLD.history_verified AND NOT NEW.history_verified) OR
        (OLD.history_digest IS NOT NULL AND NEW.history_digest IS DISTINCT FROM OLD.history_digest) OR
        (OLD.receipt_json IS NOT NULL AND (NEW.receipt_json IS NULL OR
          (NEW.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) IS DISTINCT FROM
          (OLD.receipt_json::jsonb - ARRAY['Replayed','BindingCurrent','HistoryAvailable','HistoryMayExist']) OR
          (OLD.receipt_json::jsonb->>'HistoryMayExist')::boolean AND NOT (NEW.receipt_json::jsonb->>'HistoryMayExist')::boolean)) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_fact_immutable' USING ERRCODE='23514';
      END IF;
      IF (NEW.receipt_json IS DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS NOT DISTINCT FROM OLD.receipt_hash) OR
        (NEW.receipt_json IS NOT DISTINCT FROM OLD.receipt_json AND NEW.receipt_hash IS DISTINCT FROM OLD.receipt_hash) THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_receipt_authority_required' USING ERRCODE='23514';
      END IF;
      IF OLD.state IN ('Partial','Unknown') AND NEW.state NOT IN (OLD.state,'Acknowledged') OR
        NEW.state='Acknowledged' AND OLD.state NOT IN ('Partial','Unknown') THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_resolution_required' USING ERRCODE='23514';
      END IF;
      IF OLD.completed_at_utc IS NOT NULL AND NEW IS DISTINCT FROM OLD THEN
        RAISE EXCEPTION 'container_recursive_acl_restore_terminal_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_recursive_acl_restore_guard BEFORE UPDATE OR DELETE ON container_recursive_acl_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_guard();
    CREATE FUNCTION whaledeck_container_recursive_acl_restore_fence() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      PERFORM pg_advisory_xact_lock(41307341);
      IF TG_TABLE_NAME='file_path_locks' THEN
        IF EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'FILES_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_file_upload_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_UPLOAD_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_directory_operations' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_DIRECTORY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_copy_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_COPY_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_move_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_MOVE_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_permission_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_PERMISSION_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_ownership_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_OWNERSHIP_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF TG_TABLE_NAME='container_recursive_acl_intents' THEN
        IF NEW.publish_barrier AND EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier) THEN
          RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_TARGET_BUSY' USING ERRCODE='55000';
        END IF;
      ELSIF NEW.publish_barrier AND (EXISTS (SELECT 1 FROM file_path_locks) OR
        EXISTS (SELECT 1 FROM container_acl_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_acl_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_file_upload_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_directory_operations WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_copy_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_move_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_ownership_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_permission_intents WHERE publish_barrier) OR
        EXISTS (SELECT 1 FROM container_recursive_acl_restore_intents WHERE publish_barrier AND id<>NEW.id)) THEN
        RAISE EXCEPTION 'CONTAINER_RECURSIVE_ACL_RESTORE_TARGET_BUSY' USING ERRCODE='55000';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER whaledeck_recursive_acl_restore_original_acl_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_host_path_fence BEFORE INSERT OR UPDATE ON file_path_locks
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_upload_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_file_upload_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_directory_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_directory_operations
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_copy_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_copy_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_move_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_move_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_recursive_permission_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_permission_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_publication_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_acl_restore_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_acl_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    CREATE TRIGGER whaledeck_recursive_acl_restore_recursive_ownership_fence BEFORE INSERT OR UPDATE OF publish_barrier ON container_recursive_ownership_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_recursive_acl_restore_fence();
    """;
}
