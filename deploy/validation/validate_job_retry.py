#!/usr/bin/env python3
"""Validate retry end-to-end using one temporary container and no data mounts."""

from __future__ import annotations

import os
import subprocess
import time
import uuid

from validate_databases import Api, TERMINAL_STATES
from validate_governance import request
from validate_oidc import authenticate, required_environment


def wait_terminal(api: Api, job_id: str, timeout: int = 90) -> dict[str, object]:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        job = request(api, "GET", f"jobs/{job_id}").json()
        if job.get("state") in TERMINAL_STATES:
            return job
        time.sleep(2)
    raise RuntimeError("Retry acceptance task did not reach a terminal state")


def main() -> int:
    base_url = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base_url, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base_url, session)
    name = f"wdv-retry-{uuid.uuid4().hex[:10]}"
    source = api.post("containers/start", {
        "resourceId": name, "parameters": {}, "confirmed": False,
        "planHash": None, "idempotencyKey": f"validation-source:{uuid.uuid4()}",
    }, 202).json()
    failed = wait_terminal(api, source["id"])
    if failed.get("state") != "Failed":
        raise RuntimeError("The missing-container operation did not fail as expected")
    context = request(api, "GET", f"jobs/{source['id']}/retry-context").json()
    if context.get("resourceId") != name or context.get("requiresInput"):
        raise RuntimeError("Retry context changed the failed operation")

    image = subprocess.run(
        ["docker", "inspect", "--format", "{{.Config.Image}}", "database-platform-valkey"],
        check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20,
    ).stdout.strip()
    try:
        subprocess.run([
            "docker", "create", "--name", name, "--network", "none", "--memory", "32m", "--cpus", "0.1",
            "--label", "io.whaledeck.managed=true", "--label", "io.whaledeck.protected=false",
            "--entrypoint", "/bin/sh", image, "-c", "sleep 180",
        ], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
        preview = api.post(f"jobs/{source['id']}/retry/plan", {"inputTicket": None}, 200).json()
        payload = {
            "idempotencyKey": f"validation-retry:{uuid.uuid4()}",
            "planHash": preview.get("plan", {}).get("planHash") if preview.get("plan") else None,
            "inputTicket": None, "confirmed": False,
        }
        api.post(f"jobs/{source['id']}/retry", payload, 409)
        payload["confirmed"] = True
        retry = api.post(f"jobs/{source['id']}/retry", payload, 202).json()
        duplicate = api.post(f"jobs/{source['id']}/retry", payload, 202).json()
        if retry["id"] == source["id"] or retry["id"] != duplicate["id"]:
            raise RuntimeError("Retry job identity or idempotency is incorrect")
        succeeded = wait_terminal(api, retry["id"])
        if succeeded.get("state") != "Succeeded":
            raise RuntimeError("Retry did not start the repaired temporary container")
        original = request(api, "GET", f"jobs/{source['id']}").json()
        if original.get("state") != "Failed":
            raise RuntimeError("Retry overwrote the failed source task")
        api.post(f"jobs/{retry['id']}/retry/plan", {"inputTicket": None}, 409)
        audit = request(api, "GET", "audit?take=100").json()
        record = next((item for item in audit if item.get("jobId") == retry["id"]), None)
        if record is None:
            raise RuntimeError("Retry is missing its audit record")
        print("Job retry passed: failed source preserved, current-resource retry succeeds, confirmation and idempotency enforced")
    finally:
        subprocess.run(["docker", "rm", "-f", name], check=False, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
