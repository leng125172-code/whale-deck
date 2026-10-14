#!/usr/bin/env python3
"""Run an acceptance script with a short-lived Authentik administrator.

The wrapper is intended to run on the Whale Deck workstation. It reads the
already-mounted Authentik API settings from the running API container, keeps
all credentials in memory, and deletes the temporary user on exit.
"""

from __future__ import annotations

import os
import secrets
import subprocess
import sys
from pathlib import Path

import requests


AUTHENTIK_API = "http://127.0.0.1:8081/api/v3/"
API_CONTAINER = "whaledeck-api"


def container_environment() -> dict[str, str]:
    result = subprocess.run(
        ["docker", "inspect", "--format", "{{range .Config.Env}}{{println .}}{{end}}", API_CONTAINER],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=20,
    )
    values: dict[str, str] = {}
    for line in result.stdout.splitlines():
        key, separator, value = line.partition("=")
        if separator:
            values[key] = value
    return values


def require(values: dict[str, str], key: str) -> str:
    value = values.get(key, "").strip()
    if not value:
        raise RuntimeError(f"The running API container is missing {key}")
    return value


def main() -> int:
    if len(sys.argv) < 2:
        raise RuntimeError("Usage: run_with_ephemeral_admin.py <validation-script> [arguments...]")

    script = Path(sys.argv[1]).resolve()
    if not script.is_file():
        raise RuntimeError(f"Validation script does not exist: {script}")

    values = container_environment()
    token = require(values, "Authentication__Authentik__ApiToken")
    administrator_group = require(values, "Authentication__Authentik__AdministratorGroupId")
    session = requests.Session()
    session.trust_env = False
    session.headers.update({"Authorization": f"Bearer {token}", "Accept": "application/json"})

    username = f"whaledeck-validation-{secrets.token_hex(5)}"
    password = f"Wd!{secrets.token_urlsafe(24)}9aA"
    managed_username = f"whaledeck-api-validation-{secrets.token_hex(5)}"
    managed_password = f"Wd!{secrets.token_urlsafe(24)}9aA"
    sso_slug = f"whaledeck-validation-{secrets.token_hex(5)}"
    user_id: str | None = None
    try:
        created = session.post(
            f"{AUTHENTIK_API}core/users/",
            json={
                "username": username,
                "name": "Whale Deck ephemeral acceptance user",
                "email": "",
                "is_active": True,
                "groups": [administrator_group],
            },
            timeout=20,
        )
        created.raise_for_status()
        user_id = str(created.json()["pk"])
        changed = session.post(
            f"{AUTHENTIK_API}core/users/{user_id}/set_password/",
            json={"password": password},
            timeout=20,
        )
        changed.raise_for_status()

        environment = os.environ.copy()
        environment["VALIDATION_USERNAME"] = username
        environment["VALIDATION_PASSWORD"] = password
        environment["VALIDATION_USER_ID"] = user_id
        environment["VALIDATION_MANAGED_USERNAME"] = managed_username
        environment["VALIDATION_MANAGED_PASSWORD"] = managed_password
        environment["VALIDATION_SSO_SLUG"] = sso_slug
        completed = subprocess.run(
            [sys.executable, "-u", str(script), *sys.argv[2:]],
            env=environment,
            check=False,
        )
        return completed.returncode
    finally:
        applications = session.get(
            f"{AUTHENTIK_API}core/applications/",
            params={"search": "whaledeck-validation-", "page_size": 100},
            timeout=20,
        )
        if applications.ok:
            for item in applications.json().get("results", []):
                application_slug = str(item.get("slug") or "")
                # Only this wrapper's generated slug belongs to this run. Other validation
                # sessions may be live; a shared prefix is not proof of ownership.
                if application_slug != sso_slug:
                    continue
                provider_id = item.get("provider")
                session.delete(f"{AUTHENTIK_API}core/applications/{application_slug}/", timeout=20)
                if provider_id:
                    session.delete(f"{AUTHENTIK_API}providers/oauth2/{provider_id}/", timeout=20)
        managed = session.get(
            f"{AUTHENTIK_API}core/users/",
            params={"username": managed_username, "page_size": 20},
            timeout=20,
        )
        if managed.ok:
            for item in managed.json().get("results", []):
                if item.get("username") == managed_username:
                    session.delete(f"{AUTHENTIK_API}core/users/{item['pk']}/", timeout=20)
        if user_id is not None:
            deleted = session.delete(f"{AUTHENTIK_API}core/users/{user_id}/", timeout=20)
            if deleted.status_code not in {204, 404}:
                print(
                    f"Temporary Authentik user cleanup returned HTTP {deleted.status_code}",
                    file=sys.stderr,
                )


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Ephemeral administrator validation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
