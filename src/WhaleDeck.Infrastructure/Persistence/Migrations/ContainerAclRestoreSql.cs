namespace WhaleDeck.Infrastructure.Persistence.Migrations;

internal static class ContainerAclRestoreSql
{
    internal const string Create = """
    CREATE FUNCTION whaledeck_acl_restore_source_valid(snapshot_text text,command_text text,target_cid text,target_instance text,target_path text)
      RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $f$
    DECLARE s jsonb; e jsonb; c jsonb;
    BEGIN
      IF snapshot_text IS NULL THEN RETURN TRUE; END IF;
      IF octet_length(snapshot_text) NOT BETWEEN 1 AND 32768 THEN RETURN FALSE; END IF;
      s:=snapshot_text::jsonb; c:=command_text::jsonb;
      IF jsonb_typeof(s)<>'object' OR NOT s ?& ARRAY['Location','Entry','AccessAcl','DefaultAcl'] OR
        (SELECT count(*) FROM jsonb_object_keys(s))<>4 THEN RETURN FALSE; END IF;
      e:=s->'Entry';
      IF jsonb_typeof(e)<>'object' OR NOT e ?& ARRAY['CaptureId','HistoryDigest','CreatedAtUnixMs','SourceInstanceId','ChangedAclType','Kind','OriginalMode','AccessEntryCount','DefaultEntryCount'] OR
        (SELECT count(*) FROM jsonb_object_keys(e))<>9 THEN RETURN FALSE; END IF;
      RETURN ((s->'Location'=c->'Location' AND s->'Location'->>'ContainerId'=target_cid AND s->'Location'->>'InstanceId'=target_instance AND s->'Location'->>'Path'=target_path
        AND e->>'CaptureId' ~ '^[a-f0-9]{32}$' AND e->>'CaptureId'<>repeat('0',32) AND e->>'HistoryDigest' ~ '^[A-F0-9]{64}$'
        AND e->>'ChangedAclType'=c->>'AclType' AND e->>'Kind' IN ('File','Directory') AND length(e->>'SourceInstanceId') BETWEEN 22 AND 64
        AND jsonb_typeof(e->'CreatedAtUnixMs')='number' AND e->>'CreatedAtUnixMs' ~ '^(0|[1-9][0-9]*)$' AND (e->>'CreatedAtUnixMs')::numeric BETWEEN 0 AND 253402300799999
        AND jsonb_typeof(e->'OriginalMode')='number' AND e->>'OriginalMode' ~ '^(0|[1-9][0-9]*)$' AND (e->>'OriginalMode')::integer BETWEEN 0 AND 511
        AND jsonb_typeof(e->'AccessEntryCount')='number' AND e->>'AccessEntryCount' ~ '^(0|[1-9][0-9]*)$'
        AND jsonb_typeof(e->'DefaultEntryCount')='number' AND e->>'DefaultEntryCount' ~ '^(0|[1-9][0-9]*)$'
        AND whaledeck_acl_entries_valid(s->'AccessAcl',FALSE) AND whaledeck_acl_entries_valid(s->'DefaultAcl',TRUE)
        AND (e->>'AccessEntryCount')::integer=jsonb_array_length(s->'AccessAcl') AND (e->>'DefaultEntryCount')::integer=jsonb_array_length(s->'DefaultAcl')
        AND (e->>'OriginalMode')::integer=whaledeck_acl_mode(s->'AccessAcl')
        AND c->'Entries'=CASE WHEN c->>'AclType'='access' THEN s->'AccessAcl' ELSE s->'DefaultAcl' END
        AND (e->>'Kind'<>'File' OR c->>'AclType'='access' AND s->'DefaultAcl'='[]'::jsonb)) IS TRUE);
    EXCEPTION WHEN OTHERS THEN RETURN FALSE;
    END $f$;
    CREATE FUNCTION whaledeck_container_acl_restore_guard() RETURNS trigger LANGUAGE plpgsql AS $f$
    BEGIN
      IF NEW.restore_source_json IS DISTINCT FROM OLD.restore_source_json THEN
        RAISE EXCEPTION 'container_acl_restore_source_immutable' USING ERRCODE='23514';
      END IF;
      RETURN NEW;
    END $f$;
    CREATE TRIGGER container_acl_restore_guard BEFORE UPDATE ON container_acl_intents
      FOR EACH ROW EXECUTE FUNCTION whaledeck_container_acl_restore_guard();
    """;
    internal const string RequireSafeDown = """
    DO $f$ BEGIN
      IF EXISTS(SELECT 1 FROM container_acl_intents WHERE restore_source_json IS NOT NULL) THEN
        RAISE EXCEPTION 'container_acl_restore_downgrade_requires_no_restore_intents';
      END IF;
    END $f$;
    DROP TRIGGER container_acl_restore_guard ON container_acl_intents;
    DROP FUNCTION whaledeck_container_acl_restore_guard();
    """;
    internal const string DropValidation = "DROP FUNCTION whaledeck_acl_restore_source_valid(text,text,text,text,text);";
}
