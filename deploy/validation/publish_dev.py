#!/usr/bin/env python3
"""Publish the current dev revision through Whale Deck's fixed update action.

This script schedules an in-place development deployment. A failed deployment
is intentionally left in place for diagnosis; the next tested revision is
expected to overwrite it. It never requests the optional rollback action.
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def update_checkout() -> str:
    repository = Path(__file__).resolve().parents[2]
    status = subprocess.run(
        ["git", "-C", str(repository), "status", "--porcelain"],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        timeout=30,
    )
    if status.stdout.strip():
        raise RuntimeError("The Whale Deck checkout has local changes")

    branch = subprocess.run(
        ["git", "-C", str(repository), "branch", "--show-current"],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        timeout=30,
    ).stdout.strip()
    if branch != "dev":
        raise RuntimeError(f"Development deployment requires the dev branch, found {branch or 'detached HEAD'}")

    subprocess.run(
        ["git", "-C", str(repository), "pull", "--ff-only", "origin", "dev"],
        check=True,
        timeout=180,
    )
    revision = subprocess.run(
        ["git", "-C", str(repository), "rev-parse", "--short=12", "HEAD"],
        check=True,
        text=True,
        stdout=subprocess.PIPE,
        timeout=30,
    ).stdout.strip()
    print(f"Publishing dev revision {revision}")
    return revision


def main() -> int:
    update_checkout()
    base_url = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    username = required_environment("VALIDATION_USERNAME")
    password = required_environment("VALIDATION_PASSWORD")
    session, _ = authenticate(base_url, username, password)
    api = Api(base_url, session)
    job = api.run(
        "apply-update",
        "whaledeck.platform",
        {},
        area="platform",
        confirmed=True,
        requires_plan=True,
        timeout=120,
    )
    print(f"Development deployment scheduled by job {job['id']}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Development deployment failed: {error}", file=sys.stderr)
        raise SystemExit(1)
