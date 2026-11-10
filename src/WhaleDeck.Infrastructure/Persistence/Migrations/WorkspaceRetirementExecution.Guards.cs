namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class WorkspaceRetirementExecution
{
    private const string ExecutionGuards = """
        CREATE OR REPLACE FUNCTION whaledeck_workspace_retirement_admission() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE w development_workspaces%ROWTYPE;
        BEGIN
            SELECT * INTO w FROM development_workspaces WHERE id=NEW.workspace_id FOR UPDATE;
            IF NOT FOUND OR w.state <> 'StorageAllocated' OR w.lifecycle_state NOT IN ('Stopped','Retiring')
                OR w.lifecycle_version <> NEW.expected_epoch OR NOT EXISTS (
                    SELECT 1 FROM development_workspace_archives a JOIN operation_jobs j ON j.id=a.id
                    WHERE a.id=NEW.archive_id AND a.workspace_id=NEW.workspace_id AND a.expected_epoch=NEW.expected_epoch
                      AND a.finished_at_utc IS NOT NULL AND a.phase='verify' AND a.lease IS NULL
                      AND a.lease_until_utc IS NULL AND NOT a.cancel_requested
                      AND a.capture_json IS NOT NULL AND a.verify_json IS NOT NULL AND j.state='Succeeded'
                      AND j.completed_at_utc IS NOT NULL) THEN
                RAISE EXCEPTION 'workspace_retirement_requires_verified_stopped'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_admission';
            END IF;
            IF EXISTS (SELECT 1 FROM development_workspace_retirements WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_retirement_workflows WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_archives WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_lifecycle WHERE workspace_id=w.id AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_source_imports WHERE workspace_id=w.id AND (finished_at_utc IS NULL OR lease IS NOT NULL))
                OR EXISTS (SELECT 1 FROM development_workspace_runs r JOIN operation_jobs j ON j.id=r.id
                    WHERE r.workspace_id=w.id AND (r.finished_at_utc IS NULL OR r.lease IS NOT NULL OR r.report_stage IN ('Collect','Retire')
                        OR j.phase='ResultUnknown' OR j.started_at_utc IS NOT NULL AND j.result_json IS NULL
                          AND j.phase NOT IN ('CanceledBeforeDispatch','AgentUnavailable'))) THEN
                RAISE EXCEPTION 'workspace_retirement_not_idle'
                    USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_idle';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retire_admission BEFORE INSERT ON development_workspace_retirement_workflows
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_admission();

        -- A tombstone is an application-capacity release, never a replacement
        -- for the original registration, budget, source history or HDD proof.
        CREATE FUNCTION whaledeck_workspace_retirement_completion_valid(wid uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
            SELECT EXISTS (SELECT 1
                FROM development_workspace_retirement_completions c
                JOIN development_workspaces w ON w.id=c.workspace_id
                JOIN development_workspace_retirement_workflows r ON r.id=c.retirement_id AND r.workspace_id=w.id
                JOIN development_workspace_retirement_steps s ON s.id=c.execute_step_id AND s.retirement_id=r.id AND s.phase='execute'
                JOIN development_workspace_retirement_steps p ON p.retirement_id=r.id AND p.phase='plan'
                JOIN operation_jobs j ON j.id=r.id
                CROSS JOIN LATERAL (SELECT (s.proof_json->>'resultJson')::jsonb AS e) proof
                WHERE c.workspace_id=wid AND (
                    w.lifecycle_state='Retired' AND w.state='StorageAllocated' AND w.lifecycle_version=r.expected_epoch
                    AND r.phase='execute' AND r.finished_at_utc=c.completed_at_utc AND r.lease IS NULL AND r.lease_until_utc IS NULL
                    AND j.job_type='development.workspace.retirement' AND j.resource_id=w.id
                    AND j.state='Succeeded' AND j.phase='Retired' AND j.completed_at_utc=c.completed_at_utc
                    AND r.confirmed_at_utc >= p.settled_at_utc AND r.confirmed_at_utc < r.plan_expires_at_utc
                    AND p.dispatched_at_utc <= p.settled_at_utc AND p.proof_json->>'status'='Succeeded'
                    AND p.proof_json->>'taskId'=p.id::text
                    AND p.proof_json->>'requestSha256'=encode(sha256(convert_to(p.request_json,'UTF8')),'hex')
                    AND (p.proof_json->>'resultJson')::jsonb=r.plan_json
                    AND s.dispatched_at_utc >= r.confirmed_at_utc AND s.settled_at_utc >= s.dispatched_at_utc
                    AND s.settled_at_utc=c.completed_at_utc AND s.proof_json->>'status'='Succeeded'
                    AND s.proof_json->>'taskId'=s.id::text AND coalesce(s.proof_json->>'code','')=''
                    AND s.proof_json->>'requestSha256'=encode(sha256(convert_to(s.request_json,'UTF8')),'hex')
                    AND s.request_json::jsonb->>'phase'='execute'
                    AND s.request_json::jsonb->>'taskId'=s.id::text AND s.request_json::jsonb->>'retirementId'=r.id::text
                    AND s.request_json::jsonb->>'retirementPlanSha256'=r.plan_json->>'retirementPlanSha256'
                    AND s.request_json::jsonb#>>'{prerequisite,storage,workspaceId}'=w.id::text
                    AND s.request_json::jsonb#>>'{prerequisite,expectedEpoch}'=r.expected_epoch::text
                    AND r.execution_json=proof.e AND proof.e->>'schemaVersion'='1'
                    AND proof.e->>'purpose'='whaledeck-development-source-retirement-receipt-v1'
                    AND proof.e->>'retirementId'=r.id::text AND proof.e->>'workspaceId'=w.id::text
                    AND proof.e->>'requestSha256'=encode(sha256(convert_to(
                        regexp_replace(replace(s.request_json,'"phase":"execute",',''),',"taskId":"[0-9a-f-]{36}"}$','}'),'UTF8')),'hex')
                    AND proof.e->>'retirementPlanSha256'=r.plan_json->>'retirementPlanSha256'
                    AND proof.e->>'storageProofSha256'=r.plan_json->>'storageProofSha256'
                    AND proof.e->>'sourceManifestSha256'=r.plan_json->>'sourceManifestSha256'
                    AND proof.e->>'captureReceiptSha256'=r.capture_receipt_sha256
                    AND r.plan_json->>'captureReceiptSha256'=r.capture_receipt_sha256
                    AND r.plan_json->>'archivePlanSha256'=r.archive_plan_sha256
                    AND r.plan_json->>'archiveId'=r.archive_id::text
                    AND r.plan_json->>'storageProofSha256' ~ '^[a-f0-9]{64}$'
                    AND r.plan_json->>'sourceManifestSha256' ~ '^[a-f0-9]{64}$'
                    AND proof.e->>'projectId' ~ '^[1-9][0-9]{0,9}$' AND (proof.e->>'projectId')::bigint <= 2147483647
                    AND proof.e->'sourceRemoved'='true'::jsonb AND proof.e->'quotaReleased'='true'::jsonb
                    AND proof.e->'claimReleased'='true'::jsonb AND proof.e->'retainsHdd'='true'::jsonb
                    AND proof.e->'releasesApplicationBudget'='false'::jsonb
                    AND j.result_json->'sourceRetired'='true'::jsonb AND j.result_json->'budgetReleased'='true'::jsonb
                    AND j.result_json->'retainsHdd'='true'::jsonb
                ) IS TRUE);
        $$;

        CREATE OR REPLACE FUNCTION whaledeck_workspace_retirement_pair() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE wid uuid; current_state text; pending boolean; completed boolean; rid uuid;
        BEGIN
            IF TG_TABLE_NAME='development_workspaces' THEN
                IF TG_OP='DELETE' THEN wid=OLD.id; ELSE wid=NEW.id; END IF;
            ELSIF TG_TABLE_NAME='operation_jobs' THEN
                SELECT workspace_id INTO wid FROM development_workspace_retirement_workflows WHERE id=NEW.id;
                IF wid IS NULL THEN RETURN NULL; END IF;
            ELSIF TG_TABLE_NAME='development_workspace_retirement_steps' THEN
                IF TG_OP='DELETE' THEN rid=OLD.retirement_id; ELSE rid=NEW.retirement_id; END IF;
                SELECT workspace_id INTO wid FROM development_workspace_retirement_workflows WHERE id=rid;
            ELSE
                IF TG_OP='DELETE' THEN wid=OLD.workspace_id; ELSE wid=NEW.workspace_id; END IF;
            END IF;
            SELECT lifecycle_state INTO current_state FROM development_workspaces WHERE id=wid;
            SELECT EXISTS (SELECT 1 FROM development_workspace_retirements WHERE workspace_id=wid AND finished_at_utc IS NULL)
                OR EXISTS (SELECT 1 FROM development_workspace_retirement_workflows WHERE workspace_id=wid AND finished_at_utc IS NULL) INTO pending;
            SELECT EXISTS (SELECT 1 FROM development_workspace_retirement_completions WHERE workspace_id=wid) INTO completed;
            IF pending AND current_state IS DISTINCT FROM 'Retiring' THEN
                RAISE EXCEPTION 'workspace_retirement_fence_changed' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
            END IF;
            IF current_state='Retiring' AND NOT pending THEN
                RAISE EXCEPTION 'workspace_retirement_pair_missing' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_pair';
            END IF;
            IF ((current_state='Retired') IS TRUE) IS DISTINCT FROM completed OR completed AND NOT whaledeck_workspace_retirement_completion_valid(wid) THEN
                RAISE EXCEPTION 'workspace_retirement_completion_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_completion';
            END IF;
            IF TG_TABLE_NAME='development_workspace_retirement_workflows' THEN
                IF NEW.finished_at_utc IS NOT NULL AND NEW.phase='execute' AND NOT completed
                    AND EXISTS (SELECT 1 FROM development_workspace_retirement_steps WHERE retirement_id=NEW.id AND phase='execute' AND dispatched_at_utc IS NOT NULL) THEN
                    RAISE EXCEPTION 'workspace_retirement_execution_unresolved' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_completion';
                END IF;
            END IF;
            IF TG_TABLE_NAME='development_workspaces' AND TG_OP='UPDATE' THEN
                IF pending OR OLD.lifecycle_state='Retiring' THEN
                    IF (to_jsonb(NEW)-'lifecycle_state') IS DISTINCT FROM (to_jsonb(OLD)-'lifecycle_state') THEN
                        RAISE EXCEPTION 'workspace_retirement_binding_changed' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
                    END IF;
                END IF;
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER workspace_retire_workflow_pair AFTER INSERT OR UPDATE ON development_workspace_retirement_workflows
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_step_pair AFTER INSERT OR UPDATE ON development_workspace_retirement_steps
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_completion_pair AFTER INSERT ON development_workspace_retirement_completions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_job_pair AFTER UPDATE ON operation_jobs
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retirement_pair();

        CREATE FUNCTION whaledeck_workspace_retired_permanent() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF OLD.lifecycle_state='Retired' THEN
                RAISE EXCEPTION 'workspace_retired_permanent' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retired_permanent';
            END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE TRIGGER workspace_retired_permanent BEFORE UPDATE OR DELETE ON development_workspaces
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retired_permanent();
        CREATE FUNCTION whaledeck_workspace_retire_history() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'workspace_retirement_history_immutable' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_history';
        END $$;
        CREATE TRIGGER workspace_retire_completion_immutable BEFORE UPDATE OR DELETE ON development_workspace_retirement_completions
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_history();
        CREATE TRIGGER workspace_retire_workflow_history BEFORE DELETE ON development_workspace_retirement_workflows
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_history();
        CREATE TRIGGER workspace_retire_step_history BEFORE DELETE ON development_workspace_retirement_steps
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_history();

        CREATE FUNCTION whaledeck_workspace_retire_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_TABLE_NAME='development_workspace_retirement_workflows' THEN
                IF TG_OP='INSERT' THEN
                    IF NEW.phase <> 'plan' OR NEW.plan_json IS NOT NULL OR NEW.confirmed_at_utc IS NOT NULL
                        OR NEW.execution_json IS NOT NULL OR NEW.finished_at_utc IS NOT NULL THEN
                        RAISE EXCEPTION 'workspace_retire_initial_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_immutable';
                    END IF;
                ELSIF (NEW.id,NEW.workspace_id,NEW.archive_id,NEW.expected_epoch,NEW.snapshot_json,NEW.archive_plan_sha256,NEW.capture_receipt_sha256)
                    IS DISTINCT FROM (OLD.id,OLD.workspace_id,OLD.archive_id,OLD.expected_epoch,OLD.snapshot_json,OLD.archive_plan_sha256,OLD.capture_receipt_sha256)
                    OR OLD.plan_json IS NOT NULL AND (NEW.plan_json,NEW.plan_expires_at_utc) IS DISTINCT FROM (OLD.plan_json,OLD.plan_expires_at_utc)
                    OR OLD.confirmed_at_utc IS NOT NULL AND NEW.confirmed_at_utc IS DISTINCT FROM OLD.confirmed_at_utc
                    OR OLD.execution_json IS NOT NULL AND NEW.execution_json IS DISTINCT FROM OLD.execution_json
                    OR OLD.phase='execute' AND NEW.phase <> 'execute'
                    OR OLD.finished_at_utc IS NOT NULL AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD) THEN
                    RAISE EXCEPTION 'workspace_retire_binding_immutable' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_immutable';
                END IF;
            ELSIF TG_OP='UPDATE' THEN
                IF (NEW.id,NEW.retirement_id,NEW.phase,NEW.request_json) IS DISTINCT FROM (OLD.id,OLD.retirement_id,OLD.phase,OLD.request_json)
                    OR OLD.settled_at_utc IS NOT NULL AND to_jsonb(NEW) IS DISTINCT FROM to_jsonb(OLD)
                    OR OLD.dispatched_at_utc IS NOT NULL AND NEW.dispatched_at_utc IS NOT NULL AND NEW.dispatched_at_utc <> OLD.dispatched_at_utc
                    OR NEW.dispatched_at_utc IS NULL AND OLD.dispatched_at_utc IS NOT NULL AND (OLD.proof_json IS NOT NULL OR NEW.proof_json IS NOT NULL) THEN
                    RAISE EXCEPTION 'workspace_retire_step_immutable' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_immutable';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retire_workflow_immutable BEFORE INSERT OR UPDATE ON development_workspace_retirement_workflows
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_immutable();
        CREATE TRIGGER workspace_retire_step_immutable BEFORE UPDATE ON development_workspace_retirement_steps
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_immutable();

        -- Old binaries must not enqueue lifecycle work behind a retirement fence.
        CREATE FUNCTION whaledeck_workspace_retire_lifecycle_admission() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE state text;
        BEGIN
            SELECT lifecycle_state INTO state FROM development_workspaces WHERE id=NEW.workspace_id FOR SHARE;
            IF state IN ('Retiring','Retired') THEN
                RAISE EXCEPTION 'workspace_lifecycle_retirement_fenced' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_lifecycle';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retire_lifecycle_admission BEFORE INSERT ON development_workspace_lifecycle
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_lifecycle_admission();
        """;

    private const string RemoveExecutionGuards = """
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM development_workspace_retirement_workflows)
                OR EXISTS (SELECT 1 FROM development_workspace_retirement_steps)
                OR EXISTS (SELECT 1 FROM development_workspace_retirement_completions)
                OR EXISTS (SELECT 1 FROM development_workspaces WHERE lifecycle_state='Retired') THEN
                RAISE EXCEPTION 'workspace_retirement_execution_history_present';
            END IF;
        END $$;
        DROP TRIGGER workspace_retire_lifecycle_admission ON development_workspace_lifecycle;
        DROP FUNCTION whaledeck_workspace_retire_lifecycle_admission();
        DROP TRIGGER workspace_retire_workflow_immutable ON development_workspace_retirement_workflows;
        DROP TRIGGER workspace_retire_step_immutable ON development_workspace_retirement_steps;
        DROP FUNCTION whaledeck_workspace_retire_immutable();
        DROP TRIGGER workspace_retire_completion_immutable ON development_workspace_retirement_completions;
        DROP TRIGGER workspace_retire_workflow_history ON development_workspace_retirement_workflows;
        DROP TRIGGER workspace_retire_step_history ON development_workspace_retirement_steps;
        DROP FUNCTION whaledeck_workspace_retire_history();
        DROP TRIGGER workspace_retired_permanent ON development_workspaces;
        DROP FUNCTION whaledeck_workspace_retired_permanent();
        DROP TRIGGER workspace_retire_workflow_pair ON development_workspace_retirement_workflows;
        DROP TRIGGER workspace_retire_step_pair ON development_workspace_retirement_steps;
        DROP TRIGGER workspace_retire_completion_pair ON development_workspace_retirement_completions;
        DROP TRIGGER workspace_retire_job_pair ON operation_jobs;
        DROP FUNCTION whaledeck_workspace_retirement_completion_valid(uuid);
        DROP TRIGGER workspace_retire_admission ON development_workspace_retirement_workflows;
        """;

    private const string LegacyPair = """
        CREATE OR REPLACE FUNCTION whaledeck_workspace_retirement_pair() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE wid uuid; current_state text; pending boolean;
        BEGIN
        IF TG_TABLE_NAME='development_workspaces' THEN
        IF TG_OP='DELETE' THEN wid=OLD.id; ELSE wid=NEW.id; END IF;
        ELSE
        IF TG_OP='DELETE' THEN wid=OLD.workspace_id; ELSE wid=NEW.workspace_id; END IF;
        END IF;
        SELECT lifecycle_state INTO current_state FROM development_workspaces WHERE id=wid;
        SELECT EXISTS (SELECT 1 FROM development_workspace_retirements WHERE workspace_id=wid AND finished_at_utc IS NULL) INTO pending;
        IF pending AND current_state IS DISTINCT FROM 'Retiring' THEN
        RAISE EXCEPTION 'workspace_retirement_fence_changed'
        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
        END IF;
        IF current_state='Retiring' AND NOT pending THEN
        RAISE EXCEPTION 'workspace_retirement_pair_missing'
        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_pair';
        END IF;
        IF TG_TABLE_NAME='development_workspaces' AND TG_OP='UPDATE' THEN
        IF pending OR OLD.lifecycle_state='Retiring' THEN
        IF (to_jsonb(NEW)-'lifecycle_state') IS DISTINCT FROM (to_jsonb(OLD)-'lifecycle_state') THEN
        RAISE EXCEPTION 'workspace_retirement_binding_changed'
        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_fence';
        END IF;
        END IF;
        END IF;
        RETURN NULL;
        END $$;
        """;

    private const string LegacyAdmission = """
        CREATE OR REPLACE FUNCTION whaledeck_workspace_retirement_admission() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE w development_workspaces%ROWTYPE;
        BEGIN
        SELECT * INTO w FROM development_workspaces WHERE id=NEW.workspace_id FOR UPDATE;
        IF NOT FOUND OR w.state <> 'StorageAllocated' OR w.lifecycle_state NOT IN ('Stopped','Retiring')
        OR w.lifecycle_version <> NEW.expected_epoch OR NOT EXISTS (
        SELECT 1 FROM development_workspace_archives a JOIN operation_jobs j ON j.id=a.id
        WHERE a.id=NEW.archive_id AND a.workspace_id=NEW.workspace_id AND a.expected_epoch=NEW.expected_epoch
        AND a.finished_at_utc IS NOT NULL AND a.phase='verify' AND a.lease IS NULL
        AND a.lease_until_utc IS NULL AND NOT a.cancel_requested
        AND a.capture_json IS NOT NULL AND a.verify_json IS NOT NULL AND j.state='Succeeded'
        AND j.completed_at_utc IS NOT NULL) THEN
        RAISE EXCEPTION 'workspace_retirement_requires_verified_stopped'
        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_admission';
        END IF;
        IF EXISTS (SELECT 1 FROM development_workspace_archives WHERE workspace_id=w.id AND finished_at_utc IS NULL)
        OR EXISTS (SELECT 1 FROM development_workspace_lifecycle WHERE workspace_id=w.id AND finished_at_utc IS NULL)
        OR EXISTS (SELECT 1 FROM development_workspace_source_imports WHERE workspace_id=w.id AND (finished_at_utc IS NULL OR lease IS NOT NULL))
        OR EXISTS (SELECT 1 FROM development_workspace_runs r JOIN operation_jobs j ON j.id=r.id
        WHERE r.workspace_id=w.id AND (r.finished_at_utc IS NULL OR r.lease IS NOT NULL OR r.report_stage IN ('Collect','Retire')
        OR j.phase='ResultUnknown' OR j.started_at_utc IS NOT NULL AND j.result_json IS NULL
        AND j.phase NOT IN ('CanceledBeforeDispatch','AgentUnavailable'))) THEN
        RAISE EXCEPTION 'workspace_retirement_not_idle'
        USING ERRCODE='23514', CONSTRAINT='ck_workspace_retirement_idle';
        END IF;
        RETURN NEW;
        END $$;
        """;
}
