#!/usr/bin/env python3
"""Sync only Whale Deck's two Authentik entries, icons and full logout flow.

Use the running API's private environment in memory, or the freshly provisioned
private fragment during installation. Never print credentials or API responses.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import shlex
import subprocess
from pathlib import Path

import requests


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--env-file", type=Path)
    arguments = parser.parse_args()
    if arguments.env_file:
        values = {}
        for line in arguments.env_file.read_text().splitlines():
            fields = shlex.split(line)
            if len(fields) == 1 and "=" in fields[0]:
                key, value = fields[0].split("=", 1)
                values[key] = value
        token = values["WHALEDECK_AUTHENTIK_API_TOKEN"]
    else:
        result = subprocess.run(
            ["docker", "inspect", "--format", "{{json .Config.Env}}", "whaledeck-api"],
            check=True, capture_output=True, text=True, timeout=20,
        )
        values = dict(line.split("=", 1) for line in json.loads(result.stdout) if "=" in line)
        token = values["Authentication__Authentik__ApiToken"]
    session = requests.Session()
    session.trust_env = False
    session.headers["Authorization"] = f"Bearer {token}"
    api = "http://127.0.0.1:8081/api/v3/"
    flow = session.get(api + "flows/instances/", params={"slug": "default-invalidation-flow"}, timeout=20)
    flow.raise_for_status()
    flows = [item for item in flow.json()["results"] if item["slug"] == "default-invalidation-flow"]
    if len(flows) != 1:
        raise RuntimeError("The full-session invalidation flow is missing.")
    assets = Path(__file__).resolve().parents[2] / "web/public/images"
    mark = base64.b64encode((assets / "whaledeck-mark.png").read_bytes()).decode("ascii")
    for slug, address, title, description in [
        ("internal", "192.168.22.19", "Whale Deck · 内网", "内网入口 · 192.168.22.19"),
        ("external", "192.168.100.13", "Whale Deck · 外网侧局域网", "外网侧局域网入口 · 192.168.100.13（非公网开放）"),
    ]:
        icon = (assets / f"whaledeck-{slug}.svg").read_text().replace(
            'href="whaledeck-mark.png"', f'href="data:image/png;base64,{mark}"',
        ).encode()
        # Content-addressed files make repeated sync safe without overwriting
        # or deleting files that might be referenced by another application.
        filename = f"whaledeck-{slug}-{hashlib.sha256(icon).hexdigest()[:12]}.svg"
        files = session.get(api + "admin/file/", params={"usage": "media", "search": filename}, timeout=20)
        files.raise_for_status()
        if not any(item["name"] == filename for item in files.json()):
            uploaded = session.post(api + "admin/file/", data={"usage": "media", "name": filename},
                                    files={"file": (filename, icon, "image/svg+xml")}, timeout=30)
            uploaded.raise_for_status()
        app_url = api + f"core/applications/whaledeck-{slug}/"
        application = session.get(app_url, timeout=20)
        application.raise_for_status()
        app = application.json()
        provider_id = app["provider"]
        if not provider_id:
            raise RuntimeError("Whale Deck has no OIDC provider.")
        changed = session.patch(api + f"providers/oauth2/{provider_id}/",
                                json={"invalidation_flow": flows[0]["pk"]}, timeout=20)
        changed.raise_for_status()
        changed = session.patch(app_url, json={
            "name": title, "meta_description": description, "meta_icon": filename,
            "meta_launch_url": f"http://{address}:8080/",
        }, timeout=20)
        changed.raise_for_status()
        updated = changed.json()
        icon_url = updated["meta_icon_url"]
        if not icon_url or updated["meta_launch_url"] != f"http://{address}:8080/":
            raise RuntimeError("Application presentation verification failed.")
        # Assert media is reachable without depending on Whale Deck/Gateway.
        verified = session.get(requests.compat.urljoin("http://127.0.0.1:8081/", icon_url), timeout=20)
        verified.raise_for_status()
        if hashlib.sha256(verified.content).digest() != hashlib.sha256(icon).digest():
            raise RuntimeError("Uploaded application icon differs from source.")
        print(f"Configured whaledeck-{slug}: {address}, uploaded icon verified, full logout enabled.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Requests exceptions can include remote response content; keep errors
        # intentionally bounded and free of credentials.
        raise SystemExit("Authentik presentation synchronization failed; check connectivity, API permissions and flow availability.")
