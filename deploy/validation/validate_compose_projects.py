#!/usr/bin/env python3
"""Accept Compose discovery and planned lifecycle on one disposable application."""
from __future__ import annotations

import os
import secrets
import sys
import time

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def inventory(api: Api) -> list[dict[str, object]]:
    response = api.session.get(f"{api.base_url}/api/v1/compose-projects", timeout=30)
    response.raise_for_status()
    return response.json()


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    initial = inventory(api)
    if not any(item["isProtected"] and item["type"] == "ComposeProject" for item in initial):
        raise RuntimeError("Core Compose projects were not discovered as protected.")
    for resource in ["compose:whaledeck", "application:../database-platform"]:
        rejected = session.post(f"{base}/api/v1/compose-projects/redeploy/plan",
                                json={"resourceId": resource, "parameters": {}}, headers=api.headers, timeout=30)
        if rejected.status_code not in {400, 403, 409, 422}:
            raise RuntimeError("An unregistered or traversal project was not rejected.")
    slug = f"wdv-compose-{secrets.token_hex(5)}"
    resource = f"application:{slug}"
    installed = False
    try:
        api.run("install", "", {"slug": slug, "image": "nginx:alpine", "autoupdate": "false"},
                area="applications", confirmed=True, requires_plan=True, timeout=600)
        installed = True
        items = [item for item in inventory(api) if item["id"] == resource]
        if len(items) != 1 or items[0]["attributes"].get("configurationSource") != "compose.yml":
            raise RuntimeError("Managed Compose source metadata is missing.")
        preview = api.post("compose-projects/redeploy/plan", {"resourceId": resource, "parameters": {}}, 200).json()
        if not any("Unchanged" in line for line in preview.get("changes", [])):
            raise RuntimeError("Compose preflight did not compare desired and running service hashes.")
        for action in ["pull", "stop", "start", "restart", "redeploy"]:
            api.run(action, resource, {}, area="compose-projects", confirmed=True, requires_plan=True, timeout=600)
            observed = next(item for item in inventory(api) if item["id"] == resource)
            expected = "Stopped" if action == "stop" else "Running"
            if observed["state"] != expected:
                raise RuntimeError(f"Compose {action} state={observed['state']}, expected={expected}.")
        # A consumed plan cannot be used for a second independently keyed job.
        plan = api.plan("restart", resource, {}, area="compose-projects")
        first = api.post("compose-projects/restart", {
            "resourceId": resource, "parameters": {}, "planHash": plan, "confirmed": True,
            "idempotencyKey": "compose-first-" + secrets.token_hex(8),
        }, 202).json()
        api.wait_job(first["id"], 120)
        duplicate = api.post("compose-projects/restart", {
            "resourceId": resource, "parameters": {}, "planHash": plan, "confirmed": True,
            "idempotencyKey": "compose-second-" + secrets.token_hex(8),
        }, 202).json()
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            result = session.get(f"{base}/api/v1/jobs/{duplicate['id']}", timeout=20)
            result.raise_for_status()
            if result.json()["state"] == "Failed":
                break
            if result.json()["state"] == "Succeeded":
                raise RuntimeError("A consumed Compose plan was reused.")
            time.sleep(2)
        else:
            raise RuntimeError("Rejected Compose plan did not reach a terminal job state.")
        print("Compose acceptance passed: core discovery/protection, source/services, desired/running diff, pull/stop/start/restart/redeploy and single-use plans.")
    finally:
        # Restrict cleanup to the exact disposable slug created by this run.
        if installed or any(item["id"] == resource for item in inventory(api)):
            api.run("uninstall", "", {"slug": slug, "deleteVolumes": "false"},
                    area="applications", confirmed=True, requires_plan=True, timeout=180)
        if any(item["id"] == resource for item in inventory(api)):
            raise RuntimeError("Disposable Compose project cleanup failed.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Compose acceptance failed: {error}", file=sys.stderr)
        raise SystemExit(1)
