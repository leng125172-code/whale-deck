#!/usr/bin/env python3
"""Read-only process acceptance, with disposable OIDC users and no host mutations."""

import math
import os
import subprocess
import time

import requests

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    endpoint = f"{base}/api/v1/host/processes"
    documentation = session.get(f"{base}/openapi/v1.json", timeout=20)
    documentation.raise_for_status()
    debug_page = session.get(f"{base}/swagger/index.html", timeout=20)
    debug_page.raise_for_status()
    if "Whale Deck API" not in debug_page.text:
        raise RuntimeError("The administrator did not receive the actual Swagger UI")
    paths = documentation.json().get("paths", {})
    for path in ("/api/v1/host/processes", "/api/v1/host/processes/{pid}"):
        if set(paths.get(path, {})) != {"get"}:
            raise RuntimeError("OpenAPI does not expose the expected read-only process contract")
    page = request(api, "GET", "host/processes?page=1&pageSize=100&sort=memory").json()
    if not page.get("items") or page.get("total", 0) < 1 or page.get("isStale"):
        raise RuntimeError("The host process snapshot was unavailable or stale")
    keys = {"pid", "parentPid", "userId", "instanceId", "name", "state", "threadCount", "startedAtUtc", "cpuTimeSeconds", "cpuPercent", "memoryBytes", "virtualMemoryBytes"}
    if any(set(item) != keys for item in page["items"]):
        raise RuntimeError("Process fields differ from the bounded non-secret contract")
    memory = [item["memoryBytes"] for item in page["items"]]
    if memory != sorted(memory, reverse=True):
        raise RuntimeError("Process memory sorting failed")
    invalid = session.get(endpoint, params={"sort": "command"}, timeout=20)
    if invalid.status_code != 400:
        raise RuntimeError("The API accepted an unsupported process selector")
    host_pid = int(subprocess.run(["systemctl", "show", "whaledeck-agent", "--property=MainPID", "--value"], check=True, text=True, stdout=subprocess.PIPE).stdout.strip())
    own = request(api, "GET", f"host/processes?filter={host_pid}&sort=pid").json()
    agent = next((item for item in own["items"] if item["pid"] == host_pid), None)
    if agent is None:
        raise RuntimeError("The process list does not contain the real systemd Agent PID")
    detail = session.get(f"{endpoint}/{host_pid}", params={"instanceId": agent["instanceId"]}, timeout=20)
    detail.raise_for_status()
    if detail.json()["pid"] != host_pid or set(detail.json()) != keys:
        raise RuntimeError("The process detail did not match its pinned identity")
    changed = session.get(f"{endpoint}/{host_pid}", params={"instanceId": f"{host_pid}:1"}, timeout=20)
    if changed.status_code != 404:
        raise RuntimeError("A mismatched start identity was not rejected")
    time.sleep(7)
    refreshed = request(api, "GET", "host/processes?page=1&pageSize=100&sort=cpu").json()
    cpus = [item["cpuPercent"] for item in refreshed["items"] if item["cpuPercent"] is not None]
    if not cpus or any(not math.isfinite(value) or value < 0 for value in cpus) or cpus != sorted(cpus, reverse=True):
        raise RuntimeError("The six-second CPU sampling or sorting failed")
    if refreshed["sampledAtUtc"] == page["sampledAtUtc"]:
        raise RuntimeError("The Agent process sampler did not advance")
    anonymous = requests.Session()
    anonymous.trust_env = False
    if anonymous.get(endpoint, timeout=20, allow_redirects=False).status_code != 401:
        raise RuntimeError("Anonymous process access was not denied")
    ticket = api.stage_secret(required_environment("VALIDATION_MANAGED_PASSWORD"))
    api.run("create-user", "", {"username": required_environment("VALIDATION_MANAGED_USERNAME"), "name": "Temporary ordinary process-validation user", "email": "", "inputTicket": ticket, "inputKind": "password"}, area="identity", timeout=120)
    ordinary, profile = authenticate(base, required_environment("VALIDATION_MANAGED_USERNAME"), required_environment("VALIDATION_MANAGED_PASSWORD"), require_administrator=False)
    if profile.get("isAdministrator") is not False:
        raise RuntimeError("The disposable ordinary user unexpectedly has management rights")
    if ordinary.get(endpoint, timeout=20, allow_redirects=False).status_code != 403:
        raise RuntimeError("Ordinary users can read process statistics")
    for kind, client in (("anonymous", anonymous), ("ordinary", ordinary)):
        for path in ("/openapi/v1.json", "/swagger/index.html"):
            denied = client.get(f"{base}{path}", timeout=20, allow_redirects=False)
            expected_status = 401 if kind == "anonymous" else 403
            if denied.status_code != expected_status or "Location" in denied.headers:
                raise RuntimeError(f"Unexpected debug access result: {kind} {path} HTTP {denied.status_code}")
            problem = denied.json()
            if problem.get("status") != expected_status or not problem.get("traceId") or problem.get("code") != ("AUTH_REQUIRED" if kind == "anonymous" else "AUTH_FORBIDDEN"):
                raise RuntimeError("Debug access denial did not contain the expected Problem Details")
    print(f"Host process validation passed: {page['total']} processes, real Agent PID, CPU/memory ordering, pinned identity, administrator-only OpenAPI/Swagger and anonymous/ordinary-user denial")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
