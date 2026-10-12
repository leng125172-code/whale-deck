#!/usr/bin/env python3
"""Reject plaintext job inputs and exercise bounded, one-use staging. Synthetic only."""
import json
import os
import uuid

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    suffix = uuid.uuid4().hex
    before = {item["id"] for item in request(api, "GET", "jobs?take=200").json()}
    for key in ("environment", "ENVIRONMENT"):
        response = api.post("applications/install", {
            "resourceId": "whaledeck.apps", "idempotencyKey": "queue-guard:" + key + ":" + suffix,
            "parameters": {key: json.dumps({"PUBLIC_VALIDATION_LITERAL": suffix})},
            "planHash": "0" * 64, "confirmed": True,
        }, 400)
        assert suffix not in response.text, "Rejected secret input appeared in the response"
    after = request(api, "GET", "jobs?take=200").json()
    assert not any(item["id"] not in before and item["jobType"] == "applications.install" for item in after), "Rejected input created an application job"
    # Larger configuration tickets remain five-minute, owner-bound, consumption-only values.
    value = "PUBLIC_SYNTHETIC_CONFIGURATION_" + "x" * 10_000
    ticket = api.post("secrets/stage", {"value": value, "generate": False}, 200)
    assert ticket.headers.get("Cache-Control") == "no-store"
    assert value not in ticket.text
    token = ticket.json()["token"]
    consumed = api.post("secrets/consume", {"token": token}, 200)
    assert consumed.json()["value"] == value
    assert consumed.headers.get("Cache-Control") == "no-store"
    api.post("secrets/consume", {"token": token}, 404)
    api.post("secrets/stage", {"value": "x" * 65_537, "generate": False}, 400)
    print(json.dumps({"secretQueueGuard": "passed", "plaintextEnvironment": "rejected-before-enqueue",
        "largeSyntheticTicket": "consumed-once", "oversizeTicket": "rejected", "applicationJobsCreated": 0}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
