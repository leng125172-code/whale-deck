namespace WhaleDeck.Infrastructure.Persistence.Migrations;

public partial class WorkspaceRetirementRecovery
{
    private const string BackfillCurrentStep = """
        -- The migration transaction holds the table DDL lock. Only the derived
        -- pointer is backfilled; no old plan, proof, timestamp or task changes.
        ALTER TABLE development_workspace_retirement_workflows DISABLE TRIGGER workspace_retire_workflow_immutable;
        UPDATE development_workspace_retirement_workflows r SET execute_step_id=s.id
            FROM development_workspace_retirement_steps s WHERE s.retirement_id=r.id AND s.phase='execute';
        SET CONSTRAINTS ALL IMMEDIATE;
        ALTER TABLE development_workspace_retirement_workflows ENABLE TRIGGER workspace_retire_workflow_immutable;
        SET CONSTRAINTS ALL DEFERRED;
        """;

    private const string RecoveryGuards = """
        CREATE FUNCTION whaledeck_workspace_retire_recovery_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_TABLE_NAME='development_workspace_retirement_steps' THEN
                IF NEW.attempt IS DISTINCT FROM OLD.attempt THEN
                    RAISE EXCEPTION 'workspace_retirement_attempt_immutable' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
                END IF;
            ELSIF TG_OP='INSERT' THEN
                IF NEW.execute_step_id IS NOT NULL OR NEW.execution_attempt <> 0 THEN
                    RAISE EXCEPTION 'workspace_retirement_recovery_initial_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
                END IF;
            ELSE
                IF (NEW.execute_step_id,NEW.execution_attempt) IS DISTINCT FROM (OLD.execute_step_id,OLD.execution_attempt) THEN
                    IF NOT ((OLD.phase='plan' AND NEW.phase='execute' AND OLD.execute_step_id IS NULL AND NEW.execute_step_id IS NOT NULL
                        AND NEW.execution_attempt=0) OR
                        (OLD.phase='execute' AND NEW.phase='execute' AND OLD.attention_required AND OLD.finished_at_utc IS NULL
                        AND OLD.lease IS NULL AND OLD.lease_until_utc IS NULL AND NEW.lease IS NULL AND NEW.lease_until_utc IS NULL
                        AND NOT NEW.attention_required AND NOT NEW.cancel_requested AND NOT NEW.authorization_required
                        AND NEW.execution_attempt=OLD.execution_attempt+1 AND NEW.execute_step_id IS DISTINCT FROM OLD.execute_step_id
                        AND NEW.execute_step_id IS NOT NULL)) THEN
                        RAISE EXCEPTION 'workspace_retirement_recovery_transition_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
                    END IF;
                END IF;
                IF (OLD.attention_required AND NOT NEW.attention_required OR OLD.cancel_requested AND NOT NEW.cancel_requested)
                    AND NEW.execution_attempt <> OLD.execution_attempt+1 THEN
                    RAISE EXCEPTION 'workspace_retirement_recovery_confirmation_missing' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_retire_recovery_transition BEFORE INSERT OR UPDATE ON development_workspace_retirement_workflows
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_transition();
        CREATE TRIGGER workspace_retire_attempt_immutable BEFORE UPDATE ON development_workspace_retirement_steps
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_transition();
        CREATE TRIGGER workspace_retire_recovery_immutable BEFORE UPDATE OR DELETE ON development_workspace_retirement_recoveries
            FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_history();

        CREATE FUNCTION whaledeck_workspace_retire_recovery_pair() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rid uuid; r development_workspace_retirement_workflows%ROWTYPE;
        BEGIN
            IF TG_TABLE_NAME='development_workspace_retirement_workflows' THEN rid=NEW.id; ELSE rid=NEW.retirement_id; END IF;
            SELECT * INTO r FROM development_workspace_retirement_workflows WHERE id=rid;
            IF NOT FOUND THEN RETURN NULL; END IF;
            IF (r.phase='plan' AND (r.execute_step_id IS NOT NULL OR r.execution_attempt <> 0)) OR
                (r.phase='execute' AND (NOT EXISTS (SELECT 1 FROM development_workspace_retirement_steps s
                    WHERE s.id=r.execute_step_id AND s.retirement_id=r.id AND s.phase='execute' AND s.attempt=r.execution_attempt)
                    OR (SELECT count(*) FROM development_workspace_retirement_steps WHERE retirement_id=r.id AND phase='execute') <> r.execution_attempt+1)) THEN
                RAISE EXCEPTION 'workspace_retirement_current_attempt_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
            END IF;
            IF EXISTS (SELECT 1 FROM development_workspace_retirement_steps s
                LEFT JOIN development_workspace_retirement_recoveries g ON g.id=s.id
                LEFT JOIN development_workspace_retirement_steps p ON p.id=g.previous_step_id
                WHERE s.retirement_id=r.id AND s.phase='execute' AND s.attempt>0 AND (
                    g.retirement_id=r.id AND g.attempt=s.attempt AND p.retirement_id=r.id AND p.phase='execute'
                    AND p.attempt=s.attempt-1 AND p.settled_at_utc IS NOT NULL
                    AND p.proof_json->>'status' IN ('Interrupted','Stopped','Rejected')
                    AND p.proof_json->>'taskId'=p.id::text
                    AND p.proof_json->>'requestSha256'=encode(sha256(convert_to(p.request_json,'UTF8')),'hex')
                    AND p.proof_json->'resultJson'='null'::jsonb
                    AND g.previous_proof_sha256=encode(sha256(convert_to(p.proof_json::text,'UTF8')),'hex')
                    AND g.plan_sha256=r.plan_json->>'retirementPlanSha256'
                    AND s.request_json::jsonb->>'taskId'=s.id::text
                    AND (s.request_json::jsonb-'taskId')=(p.request_json::jsonb-'taskId')
                    AND g.confirmed_at_utc >= p.settled_at_utc AND g.confirmed_at_utc >= r.confirmed_at_utc
                    AND (s.dispatched_at_utc IS NULL OR s.dispatched_at_utc >= g.confirmed_at_utc)
                    AND (s.settled_at_utc IS NULL OR s.settled_at_utc >= g.confirmed_at_utc)
                ) IS NOT TRUE) OR EXISTS (SELECT 1 FROM development_workspace_retirement_recoveries g
                    JOIN development_workspace_retirement_steps s ON s.id=g.id
                    WHERE g.retirement_id=r.id AND (s.retirement_id<>r.id OR s.phase<>'execute' OR s.attempt<>g.attempt
                        OR s.attempt>r.execution_attempt)) THEN
                RAISE EXCEPTION 'workspace_retirement_recovery_binding_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
            END IF;
            IF EXISTS (SELECT 1 FROM development_workspace_retirement_completions c WHERE c.retirement_id=r.id
                AND c.execute_step_id IS DISTINCT FROM r.execute_step_id) THEN
                RAISE EXCEPTION 'workspace_retirement_completion_attempt_invalid' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
            END IF;
            IF TG_TABLE_NAME='development_workspace_retirement_workflows' AND TG_OP='UPDATE' THEN
                IF NEW.execution_attempt>OLD.execution_attempt AND NOT EXISTS (SELECT 1 FROM development_workspace_retirement_recoveries g
                    WHERE g.id=NEW.execute_step_id AND g.previous_step_id=OLD.execute_step_id AND g.attempt=NEW.execution_attempt
                        AND g.actor_subject=NEW.execution_subject AND g.login_binding=NEW.login_binding) THEN
                    RAISE EXCEPTION 'workspace_retirement_recovery_authorization_missing' USING ERRCODE='23514', CONSTRAINT='ck_workspace_retire_recovery';
                END IF;
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER workspace_retire_recovery_workflow_pair AFTER INSERT OR UPDATE ON development_workspace_retirement_workflows
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_recovery_step_pair AFTER INSERT OR UPDATE ON development_workspace_retirement_steps
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_recovery_pair AFTER INSERT ON development_workspace_retirement_recoveries
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_pair();
        CREATE CONSTRAINT TRIGGER workspace_retire_recovery_completion_pair AFTER INSERT ON development_workspace_retirement_completions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION whaledeck_workspace_retire_recovery_pair();
        -- Validate every upgraded row, including already completed histories.
        UPDATE development_workspace_retirement_workflows SET execution_attempt=execution_attempt;
        """;

    private const string RemoveRecoveryGuards = """
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM development_workspace_retirement_recoveries)
                OR EXISTS (SELECT 1 FROM development_workspace_retirement_steps WHERE attempt>0) THEN
                RAISE EXCEPTION 'workspace_retirement_recovery_history_present';
            END IF;
        END $$;
        DROP TRIGGER workspace_retire_recovery_completion_pair ON development_workspace_retirement_completions;
        DROP TRIGGER workspace_retire_recovery_pair ON development_workspace_retirement_recoveries;
        DROP TRIGGER workspace_retire_recovery_step_pair ON development_workspace_retirement_steps;
        DROP TRIGGER workspace_retire_recovery_workflow_pair ON development_workspace_retirement_workflows;
        DROP FUNCTION whaledeck_workspace_retire_recovery_pair();
        DROP TRIGGER workspace_retire_recovery_immutable ON development_workspace_retirement_recoveries;
        DROP TRIGGER workspace_retire_attempt_immutable ON development_workspace_retirement_steps;
        DROP TRIGGER workspace_retire_recovery_transition ON development_workspace_retirement_workflows;
        DROP FUNCTION whaledeck_workspace_retire_recovery_transition();
        """;
}
