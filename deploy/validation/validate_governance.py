#!/usr/bin/env python3
"""Exercise governance and safe host operations through the live API."""

from __future__ import annotations

import json
import os
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone

import requests

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def request(
    api: Api,
    method: str,
    path: str,
    *,
    payload: dict[str, object] | None = None,
    expected: int = 200,
) -> requests.Response:
    response = api.session.request(
        method,
        f"{api.base_url}/api/v1/{path.lstrip('/')}",
        json=payload,
        headers=api.headers if method.upper() not in {"GET", "HEAD"} else None,
        allow_redirects=False,
        timeout=30,
    )
    if response.status_code != expected:
        try:
            problem = response.json()
            detail = f" code={problem.get('code')} title={problem.get('title')} traceId={problem.get('traceId')}"
        except (ValueError, AttributeError):
            detail = ""
        raise RuntimeError(f"{method} {path} returned HTTP {response.status_code}, expected {expected}.{detail}")
    return response


def validate_portal(api: Api) -> None:
    suffix = uuid.uuid4().hex[:8]
    created = request(
        api,
        "POST",
        "portals",
        payload={
            "scope": "Personal",
            "name": f"Acceptance portal {suffix}",
            "description": "Temporary Whale Deck acceptance portal",
            "url": "http://precision-7920-tower.local:8080",
            "iconKind": "BuiltIn",
            "iconValue": "Link",
            "color": "#409eff",
            "sortOrder": 9000,
            "isEnabled": True,
            "expectedVersion": None,
        },
        expected=201,
    ).json()
    portal_id = created["id"]
    try:
        listed = request(api, "GET", "portals").json()
        if not any(item.get("id") == portal_id for item in listed):
            raise RuntimeError("The personal portal was not visible to its owner")
        updated = request(
            api,
            "PUT",
            f"portals/{portal_id}",
            payload={
                **created,
                "name": f"Acceptance portal updated {suffix}",
                "expectedVersion": created["version"],
            },
        ).json()
        stale = api.session.put(
            f"{api.base_url}/api/v1/portals/{portal_id}",
            json={**updated, "expectedVersion": created["version"]},
            headers=api.headers,
            timeout=30,
        )
        if stale.status_code not in {409, 422}:
            raise RuntimeError(f"A stale portal update unexpectedly returned HTTP {stale.status_code}")
    finally:
        request(api, "DELETE", f"portals/{portal_id}", expected=204)


def validate_schedule(api: Api) -> None:
    suffix = uuid.uuid4().hex[:8]
    created = request(
        api,
        "POST",
        "schedules",
        payload={
            "taskType": "MetricsRollup",
            "name": f"Acceptance schedule {suffix}",
            "scheduleKind": "Cron",
            "scheduleExpression": "17 3 * * *",
            "timezone": "Asia/Shanghai",
            "parametersJson": "{}",
            "concurrencyPolicy": "Forbid",
            "timeoutSeconds": 300,
            "isEnabled": False,
            "expectedVersion": None,
        },
    ).json()
    schedule_id = created["id"]
    updated = request(
        api,
        "PUT",
        f"schedules/{schedule_id}",
        payload={
            **created,
            "name": f"Acceptance schedule updated {suffix}",
            "parametersJson": "{}",
            "expectedVersion": created["version"],
        },
    ).json()
    stale = api.session.delete(
        f"{api.base_url}/api/v1/schedules/{schedule_id}?version={created['version']}",
        headers=api.headers,
        timeout=30,
    )
    if stale.status_code not in {409, 422}:
        raise RuntimeError(f"A stale schedule delete unexpectedly returned HTTP {stale.status_code}")
    request(api, "DELETE", f"schedules/{schedule_id}?version={updated['version']}", expected=204)


def validate_alert(api: Api) -> None:
    created = request(
        api,
        "POST",
        "alerts/rules",
        payload={
            "ruleType": "DiskUtilization",
            "resourceSelectorJson": "{}",
            "thresholdJson": json.dumps({"value": 0}),
            "evaluationWindowSeconds": 12,
            "severity": "Info",
            "isEnabled": True,
            "expectedVersion": None,
        },
    ).json()
    rule_id = created["id"]
    current = created
    event_id: str | None = None
    try:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            events = request(api, "GET", "alerts?includeRecovered=true").json()
            event = next((item for item in events if item.get("ruleId") == rule_id and item.get("state") != "Recovered"), None)
            if event is not None:
                event_id = event["id"]
                break
            time.sleep(3)
        if event_id is None:
            raise RuntimeError("The disk utilization alert did not trigger within 60 seconds")

        acknowledged = request(api, "POST", f"alerts/{event_id}/acknowledge", payload={}).json()
        if acknowledged.get("state") != "Acknowledged":
            raise RuntimeError("The alert was not acknowledged")
        silenced = request(
            api,
            "POST",
            f"alerts/{event_id}/silence",
            payload={"untilUtc": (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat()},
        ).json()
        if silenced.get("state") != "Silenced":
            raise RuntimeError("The alert was not silenced")

        current = request(
            api,
            "POST",
            "alerts/rules",
            payload={
                **current,
                "thresholdJson": json.dumps({"value": 100}),
                "expectedVersion": current["version"],
            },
        ).json()
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            events = request(api, "GET", "alerts?includeRecovered=true").json()
            event = next((item for item in events if item.get("id") == event_id), None)
            if event is not None and event.get("state") == "Recovered":
                break
            time.sleep(3)
        else:
            raise RuntimeError("The disk utilization alert did not recover within 60 seconds")
    finally:
        request(
            api,
            "POST",
            "alerts/rules",
            payload={**current, "isEnabled": False, "expectedVersion": current["version"]},
        )


def validate_safe_operations(api: Api) -> None:
    status = request(api, "GET", "config-repository/status").json()
    if status.get("state") != "Clean" or not status.get("version"):
        raise RuntimeError("The configuration repository is not clean or has no commit")
    api.run("snapshot", "database-platform.repository", {}, area="config-repository", timeout=120)
    api.run(
        "commit-push",
        "database-platform.repository",
        {"message": "chore: verify Whale Deck configuration sync"},
        area="config-repository",
        confirmed=True,
        requires_plan=True,
        timeout=180,
    )
    api.run("update-check", "whaledeck.host", {}, area="host", timeout=180)
    api.run("diagnose", "whaledeck.platform", {}, area="platform", timeout=180)


def main() -> int:
    base_url = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(
        base_url,
        required_environment("VALIDATION_USERNAME"),
        required_environment("VALIDATION_PASSWORD"),
    )
    api = Api(base_url, session)
    validate_portal(api)
    validate_schedule(api)
    validate_alert(api)
    validate_safe_operations(api)
    audit = request(api, "GET", "audit?take=100").json()
    actions = {item.get("action") for item in audit}
    required = {"config-repository.snapshot", "config-repository.commit-push", "host.update-check", "platform.diagnose"}
    if not required.issubset(actions):
        raise RuntimeError("The audit feed is missing one or more governance operations")
    print("Governance, alerts, portals and safe host operations validation passed")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Governance validation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
