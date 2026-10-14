#!/usr/bin/env python3
"""Isolated PostgreSQL reproduction of the legacy sequence-only read-all predicate.

Two minimal tables, not production data or an assertion of full C# acceptance.
No credentials; exact generated database is dropped even after a failure.
"""
import json
import re
import select
import subprocess
import uuid


def main():
    name = 'wdv_snapshot_repro_' + uuid.uuid4().hex[:16]
    assert re.fullmatch(r'wdv_snapshot_repro_[a-f0-9]{16}', name)
    base = ['docker', 'exec', '-i', '-u', 'postgres', 'database-platform-postgres',
        'psql', '-X', '-q', '-t', '-A', '-v', 'ON_ERROR_STOP=1']
    def sql(text, database=name):
        return subprocess.run(base + ['-d', database], input=text, check=True, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20).stdout.strip()
    created = False
    delayed = None
    try:
        sql(f'CREATE DATABASE "{name}" OWNER whaledeck;', 'postgres'); created = True
        sql('''CREATE TABLE notification_events (
            id uuid PRIMARY KEY, sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
            audience text NOT NULL, recipient_subject text, expires_at_utc timestamptz NOT NULL);
          CREATE TABLE notification_read_states (notification_id uuid REFERENCES notification_events(id),
            subject text NOT NULL, read_at_utc timestamptz NOT NULL, PRIMARY KEY(subject, notification_id));''')
        late_id, early_id = str(uuid.uuid4()), str(uuid.uuid4())
        delayed = subprocess.Popen(base + ['-d', name], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True)
        delayed.stdin.write(f"BEGIN; INSERT INTO notification_events (id,audience,recipient_subject,expires_at_utc) VALUES ('{late_id}','Subject','snapshot-owner',now()+interval '1 day') RETURNING sequence;\n")
        delayed.stdin.flush()
        if not select.select([delayed.stdout], [], [], 20)[0]:
            raise TimeoutError('Delayed isolated transaction did not produce its sequence')
        low = int(delayed.stdout.readline().strip())
        high = int(sql(f"INSERT INTO notification_events (id,audience,recipient_subject,expires_at_utc) VALUES ('{early_id}','Subject','snapshot-owner',now()+interval '1 day') RETURNING sequence;"))
        assert low < high
        legacy = f'''WITH changed AS (INSERT INTO notification_read_states (notification_id,subject,read_at_utc)
          SELECT id,'snapshot-owner',now() FROM notification_events
          WHERE expires_at_utc>now() AND sequence<={high}
            AND (audience='Subject' AND recipient_subject='snapshot-owner')
            AND NOT EXISTS (SELECT 1 FROM notification_read_states r WHERE r.notification_id=notification_events.id AND r.subject='snapshot-owner')
          ORDER BY sequence DESC LIMIT 1000 FOR KEY SHARE
          ON CONFLICT(subject,notification_id) DO NOTHING RETURNING notification_id)
          SELECT count(*) FROM changed;'''
        first = int(sql(legacy))
        delayed.communicate('COMMIT;\n', timeout=20)
        assert delayed.returncode == 0; delayed = None
        second = int(sql(legacy))
        late_read = sql(f"SELECT count(*) FROM notification_read_states WHERE notification_id='{late_id}';") == '1'
        assert first == 1 and second == 1 and late_read
        print(json.dumps({'legacyReadAllLateCommitDefect': 'reproduced', 'scope': 'minimal-isolated-sql-not-full-csharp',
            'lowerSequence': low, 'snapshotCutoff': high, 'firstBatchChanged': first,
            'laterBatchChanged': second, 'lateMessageIncorrectlyRead': late_read}))
    finally:
        if delayed is not None:
            delayed.kill(); delayed.communicate(timeout=10)
        if created:
            sql(f'DROP DATABASE "{name}" WITH (FORCE);', 'postgres')
            print(json.dumps({'snapshotSqlReproductionCleanup': 'passed'}))


if __name__ == '__main__':
    main()
