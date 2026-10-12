#!/usr/bin/env python3
"""Exercise the managed application lifecycle through API -> Worker -> Agent."""

from __future__ import annotations

import os
import secrets
import sys
import time
from datetime import datetime, timedelta, timezone

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def list_applications(api: Api) -> list[dict[str, object]]:
    response = api.session.get(f"{api.base_url}/api/v1/applications", timeout=30)
    response.raise_for_status()
    payload = response.json()
    if not isinstance(payload, list):
        raise RuntimeError("Application inventory did not return a list")
    return payload


def is_installed(api: Api, slug: str) -> bool:
    expected = f"application:{slug}"
    return any(item.get("id") == expected for item in list_applications(api))


def current_maintenance_window() -> str:
    day = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][
        datetime.now(timezone(timedelta(hours=8))).weekday()
    ]
    return f"{day}@00:00-23:59"


def main() -> int:
    base_url = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(
        base_url,
        required_environment("VALIDATION_USERNAME"),
        required_environment("VALIDATION_PASSWORD"),
    )
    api = Api(base_url, session)
    slug = f"wdv-app-{int(time.time()) % 1_000_000}-{secrets.token_hex(2)}"
    common = {"slug": slug}
    installed = False
    try:
        api.run(
            "install",
            "",
            {
                "slug": slug,
                "image": "alpine:3.22",
                "autoupdate": "true",
                "maintenanceWindow": current_maintenance_window(),
            },
            area="applications",
            confirmed=True,
            requires_plan=True,
            timeout=600,
        )
        installed = True
        if not is_installed(api, slug):
            raise RuntimeError("Installed application was not returned by inventory")

        api.run("stop", "", common, area="applications", confirmed=True)
        api.run("start", "", common, area="applications")
        api.run("restart", "", common, area="applications", confirmed=True)
        api.run(
            "update",
            "",
            {"slug": slug, "automatic": "true"},
            area="applications",
            confirmed=True,
            requires_plan=True,
            timeout=600,
        )
        api.run(
            "reinstall",
            "",
            common,
            area="applications",
            confirmed=True,
            requires_plan=True,
            timeout=600,
        )
        api.run(
            "uninstall",
            "",
            {"slug": slug, "deleteVolumes": "false"},
            area="applications",
            confirmed=True,
            requires_plan=True,
            timeout=300,
        )
        installed = False
        if is_installed(api, slug):
            raise RuntimeError("Uninstalled application remained in inventory")
    finally:
        if installed:
            try:
                api.run(
                    "uninstall",
                    "",
                    {"slug": slug, "deleteVolumes": "false"},
                    area="applications",
                    confirmed=True,
                    requires_plan=True,
                    timeout=300,
                )
            except Exception as cleanup_error:
                print(f"Application cleanup failed: {cleanup_error}", file=sys.stderr)
    print(f"Application lifecycle validation passed: {slug}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Application validation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
