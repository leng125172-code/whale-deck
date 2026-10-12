#!/usr/bin/env python3
"""Read-only acceptance for real container and Compose application summaries."""

from __future__ import annotations

import json
import os
import subprocess

from validate_oidc import authenticate, required_environment


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))

    def get(path: str):
        response = session.get(f"{base}/api/v1/{path}", timeout=30)
        response.raise_for_status()
        return response.json()

    containers = get("containers?includeStopped=true")
    if not containers:
        raise RuntimeError("No real containers were discovered")
    for item in containers:
        if not all(isinstance(item.get(key), list) for key in ("ports", "networks", "addresses")) or not item.get("createdAtUtc"):
            raise RuntimeError("Container summary metadata is incomplete")
        networking = json.loads(subprocess.run(
            ["docker", "inspect", "--format", "{{json .NetworkSettings.Networks}}", item["id"]],
            check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20,
        ).stdout)
        expected = sorted({value["IPAddress"] for value in (networking or {}).values() if value.get("IPAddress")})
        if sorted(item["addresses"]) != expected or sorted(item["networks"]) != sorted(networking or {}):
            raise RuntimeError("Container network metadata does not match Docker")
        if item["name"] in ("whaledeck-api", "whaledeck-worker", "whaledeck-gateway"):
            if not item["isProtected"] or item["protectionLevel"] != "ControlPlane":
                raise RuntimeError("Whale Deck control-plane protection is incorrect")

    applications = get("applications")
    project_members: dict[str, list[dict]] = {}
    for item in containers:
        project = item.get("labels", {}).get("com.docker.compose.project")
        if project:
            project_members.setdefault(project, []).append(item)
    for app in applications:
        attributes = app.get("attributes", {})
        project = attributes.get("composeProject")
        if project not in project_members or int(attributes.get("containerCount", "-1")) != len(project_members[project]):
            raise RuntimeError("Application-to-container relation is incorrect")
        if not app.get("version"):
            raise RuntimeError("Discovered application is missing its real image summary")
        if app["isProtected"] and attributes.get("allowedActions"):
            raise RuntimeError("Protected application advertised normal lifecycle actions")
    events = get("containers/events?take=5")
    if not isinstance(events, list) or any(not isinstance(item.get("action"), str) for item in events):
        raise RuntimeError("Docker event actions have an invalid shape")
    print(f"Management view acceptance passed: {len(containers)} real container summaries, {len(applications)} Compose applications, network projection and control-plane protection verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
