#!/usr/bin/env python3
"""Read-only image-cleanup preflight; never submits or executes a cleanup job."""

from __future__ import annotations

import json
import os
import re
import subprocess
from decimal import Decimal, ROUND_HALF_UP
from urllib.parse import quote

from validate_databases import Api
from validate_oidc import authenticate, required_environment


def docker_get(path: str) -> object:
    result = subprocess.run(
        ["curl", "--unix-socket", "/var/run/docker.sock", "--noproxy", "*",
         "--silent", "--show-error", "--fail", "--max-time", "10", f"http://localhost{path}"],
        check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15,
    )
    return json.loads(result.stdout)


def inventory() -> tuple[list[str], list[str], list[str]]:
    images = sorted(item["Id"] for item in docker_get("/images/json?all=1"))
    volumes = sorted(item["Name"] for item in docker_get("/volumes").get("Volumes") or [])
    containers = sorted(item["Id"] for item in docker_get("/containers/json?all=1"))
    return images, volumes, containers


def bytes_text(value: int) -> str:
    size = Decimal(value)
    units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"]
    unit = 0
    while size >= 1024 and unit < len(units) - 1:
        size /= 1024
        unit += 1
    text = str(int(size)) if unit == 0 else format(size.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP), "f").removesuffix("0")
    return f"{text} {units[unit]}"


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    before = inventory()
    query = quote(json.dumps({"dangling": {"true": True}}, separators=(",", ":")))
    dangling = docker_get(f"/images/json?all=1&filters={query}")
    total = sum(max(0, item["Size"]) for item in dangling)
    plan = api.post("containers/prune/plan", {
        "resourceId": None, "parameters": {"images": "true", "volumes": "false"},
    }, 200).json()
    expected = f"清理未使用的悬空镜像，预计最多回收 {bytes_text(total)}"
    if plan.get("changes") != [expected] or not plan.get("planHash"):
        raise RuntimeError("Image-cleanup preflight did not match native dangling-only capacity")
    if not re.fullmatch(r".*\d+(?:\.\d+)? (?:B|KiB|MiB|GiB|TiB|PiB|EiB)", expected):
        raise RuntimeError("Cleanup capacity has an invalid unit")
    if inventory() != before:
        raise RuntimeError("Read-only preflight unexpectedly changed image, volume or container inventory")
    print(expected)
    print(f"PASS: native dangling-only estimate ({len(dangling)} images); image/volume/container inventories unchanged; no cleanup job submitted")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
