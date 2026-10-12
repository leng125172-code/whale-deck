#!/usr/bin/env python3
"""Reject plaintext job inputs and exercise bounded, one-use staging. Synthetic only."""
import json
import os
import subprocess
import uuid

from validate_databases import Api
from validate_oidc import authenticate, required_environment
from validate_metric_partition_runtime import query


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    suffix = uuid.uuid4().hex
    for key in ("environment", "ENVIRONMENT"):
        response = api.post("applications/install", {
            "resourceId": "whaledeck.apps", "idempotencyKey": "queue-guard:" + key + ":" + suffix,
            "parameters": {key: json.dumps({"PUBLIC_VALIDATION_LITERAL": suffix})},
            "planHash": "0" * 64, "confirmed": True,
        }, 400)
        assert suffix not in response.text, "Rejected secret input appeared in the response"
    # Larger configuration tickets remain five-minute, owner-bound, consumption-only values.
    value = "PUBLIC_SYNTHETIC_CONFIGURATION_" + suffix + "x" * 10_000
    ticket = api.post("secrets/stage", {"value": value, "generate": False}, 200)
    assert ticket.headers.get("Cache-Control") == "no-store"
    assert value not in ticket.text
    token = ticket.json()["token"]
    consumed = api.post("secrets/consume", {"token": token}, 200)
    assert consumed.json()["value"] == value
    assert consumed.headers.get("Cache-Control") == "no-store"
    api.post("secrets/consume", {"token": token}, 404)
    api.post("secrets/stage", {"value": "x" * 65_537, "generate": False}, 400)
    # Only a generated hex marker is interpolated; no credential or arbitrary input reaches SQL.
    evidence = query(f"""SELECT json_build_object(
        'jobs', (SELECT count(*) FROM operation_jobs WHERE request_json::text LIKE '%{suffix}%' OR idempotency_key LIKE '%{suffix}%'),
        'audit', (SELECT count(*) FROM audit_events WHERE detail_json::text LIKE '%{suffix}%'));
        """)
    assert evidence == {"jobs": 0, "audit": 0}, "Synthetic secret marker reached persistent business records"
    for container in ("whaledeck-api", "whaledeck-worker"):
        logs = subprocess.run(["docker", "logs", "--since", "2m", "--tail", "1000", container],
            check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
        assert suffix not in logs.stdout + logs.stderr, "Synthetic secret marker reached bounded container logs"
    print(json.dumps({"secretQueueGuard": "passed", "plaintextEnvironment": "rejected-before-enqueue",
        "largeSyntheticTicket": "consumed-once", "oversizeTicket": "rejected", "applicationJobsCreated": 0,
        "nativePostgresMarkerCounts": evidence, "boundedApiWorkerLogScan": "clean"}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
