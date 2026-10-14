"""Real OIDC -> API -> Valkey -> Worker -> Agent acceptance, no external send.

Only a disabled random channel and example.invalid binding are used. Removes
its binding via the normal workflow, then deletes only its synthetic DB objects.
Immutable audit receipts are retained. No production destination is touched.
"""
import json
import os
import subprocess
import time
import uuid

from validate_oidc import authenticate, required_environment


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session, _ = authenticate(base, required_environment('VALIDATION_USERNAME'), required_environment('VALIDATION_PASSWORD'))
    csrf = session.get(base + '/api/v1/auth/csrf', timeout=15).json()
    headers = {csrf['headerName']: csrf['token'], 'Origin': base}
    endpoint = base + '/api/v1/notifications/manage/'
    marker = 'private-acceptance-' + uuid.uuid4().hex[:12]
    channel_id = None
    jobs = []
    def post(path, body, key=None):
        return session.post(endpoint + path, json=body, headers={**headers, 'Idempotency-Key': key or str(uuid.uuid4())}, timeout=20, allow_redirects=False)
    def channels():
        response = session.get(endpoint + 'channels', params={'take': 100}, timeout=15); response.raise_for_status()
        return next(item for item in response.json()['items'] if item['id'] == channel_id)
    def wait(job_id):
        for _ in range(45):
            response = session.get(base + '/api/v1/jobs/' + job_id, timeout=15); response.raise_for_status()
            state = response.json()['state']
            if state in ('Succeeded', 'Failed', 'Canceled'):
                assert state == 'Succeeded', 'Private workflow did not succeed'
                return
            time.sleep(2)
        raise AssertionError('Private workflow observation budget expired; inspect the original job')
    def change(action, ticket=None):
        channel = channels()
        response = post('channels/' + channel_id + '/private/plan', {'expectedChannelVersion': channel['version'], 'action': action, 'inputTicket': ticket})
        assert response.status_code == 200, 'Private plan failed'
        plan = response.json()
        assert 'example.invalid' not in response.text and 'target' not in plan, 'Private values were returned'
        body = {'planHash': plan['planHash'], 'inputTicket': ticket, 'confirmed': True}
        key = str(uuid.uuid4())
        response = post('channels/' + channel_id + '/private', body, key)
        assert response.status_code == 202, 'Private job was not accepted'
        job_id = str(uuid.UUID(response.json()['id'])); jobs.append(job_id)
        replay = post('channels/' + channel_id + '/private', body, key)
        assert replay.status_code == 202 and replay.json()['id'] == job_id, 'Private request replay created another job'
        wait(job_id)
        assert not channels()['isEnabled'], 'Private configuration unexpectedly enabled sending'
        status = session.get(endpoint + 'channels/' + channel_id + '/private/status', timeout=15)
        assert status.status_code == 200 and status.json()['state'] == 'Succeeded'
    try:
        created = post('channels', {'name': marker, 'provider': 'Webhook', 'destinationReference': 'unused-private-validation', 'isEnabled': False, 'confirmed': True})
        assert created.status_code == 200
        channel_id = str(uuid.UUID(created.json()['id']))
        rejected = post('channels/' + channel_id + '/private/plan', {'expectedChannelVersion': -1, 'action': 'apply'})
        assert rejected.status_code == 400
        raw = json.dumps({'target': {'provider': 'Webhook', 'url': 'https://example.invalid/' + marker, 'allowedAddresses': []}, 'credentialMode': 'remove'})
        staged = session.post(base + '/api/v1/secrets/stage', json={'value': raw, 'generate': False}, headers=headers, timeout=15)
        assert staged.status_code == 200
        change('apply', staged.json()['token'])
        snapshot = session.get(endpoint + 'channels/' + channel_id + '/private', timeout=15)
        assert snapshot.status_code == 200 and snapshot.json()['hasTarget'] and not snapshot.json()['hasCredential']
        assert 'example.invalid' not in snapshot.text
        change('remove')
        snapshot = session.get(endpoint + 'channels/' + channel_id + '/private', timeout=15)
        assert snapshot.status_code == 200 and not snapshot.json()['hasTarget']
        doc = session.get(base + '/openapi/v1.json', timeout=15); doc.raise_for_status()
        assert '/api/v1/notifications/manage/channels/{id}/private/plan' in doc.json()['paths']
        print(json.dumps({'privateConfigurationRealWorkflow': 'passed', 'oidcApiValkeyWorkerAgent': 'passed', 'asynchronousPermanentReplay': 'passed', 'channelNeverEnabled': True, 'externalSends': 0}))
    finally:
        if channel_id is not None:
            # Remove an accepted binding using its own confirmed workflow if an
            # earlier assertion failed; never delete a possibly running job.
            running = []
            for job_id in jobs:
                response = session.get(base + '/api/v1/jobs/' + job_id, timeout=15)
                response.raise_for_status()
                if response.json()['state'] not in ('Succeeded', 'Failed', 'Canceled'):
                    running.append(job_id)
            assert not running, 'Original private job still running; cleanup intentionally withheld'
            snapshot = session.get(endpoint + 'channels/' + channel_id + '/private', timeout=15)
            if snapshot.status_code == 200 and snapshot.json()['hasTarget']:
                change('remove')
            # UUIDs are validated above; guard by the unique synthetic name.
            sql = f"""
                BEGIN;
                DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM notification_channels WHERE id='{channel_id}' AND name='{marker}' AND NOT is_enabled) THEN RAISE EXCEPTION 'Synthetic ownership guard failed'; END IF; END $$;
                DELETE FROM notification_events WHERE source_kind='Job' AND source_id IN (SELECT id FROM notification_private_operations WHERE channel_id='{channel_id}');
                DELETE FROM outbox_messages WHERE message_type IN ('NotificationPrivateRequested','NotificationPrivateCancellationRequested') AND payload_json->>'jobId' IN (SELECT id::text FROM notification_private_operations WHERE channel_id='{channel_id}');
                DELETE FROM operation_job_events WHERE job_id IN (SELECT id FROM notification_private_operations WHERE channel_id='{channel_id}');
                DELETE FROM notification_private_operations WHERE channel_id='{channel_id}' AND state IN ('Succeeded','Failed','Canceled','Superseded');
                DELETE FROM operation_jobs WHERE id IN ({','.join("'" + value + "'" for value in jobs) or 'NULL'}) AND job_type='notifications.private-configuration' AND completed_at_utc IS NOT NULL;
                DELETE FROM notification_channels WHERE id='{channel_id}' AND name='{marker}' AND NOT is_enabled;
                COMMIT;
                """
            completed = subprocess.run(['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres', 'psql', '-X', '-v', 'ON_ERROR_STOP=1', '-d', 'whaledeck'], input=sql, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
            assert completed.returncode == 0, 'Synthetic cleanup failed; no broader deletion attempted'
            print(json.dumps({'privateWorkflowTemporaryCleanup': 'passed', 'immutableAuditRetained': True}))


if __name__ == '__main__':
    main()
