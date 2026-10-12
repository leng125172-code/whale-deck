#!/usr/bin/env python3
"""Publish the current dev revision through Whale Deck's fixed update action.

This script schedules an in-place development deployment. A failed deployment
is intentionally left in place for diagnosis; the next tested revision is
expected to overwrite it. It never requests the optional rollback action.
"""

from __future__ import annotations

import os
import sys

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def main() -> int:
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
