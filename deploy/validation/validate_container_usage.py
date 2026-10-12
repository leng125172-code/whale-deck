#!/usr/bin/env python3
"""Native usage acceptance with one CPU-limited, isolated, disposable container."""

from __future__ import annotations

import json
import math
import os
import re
import subprocess
import time
import uuid

import requests

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def native_stats(container_id: str) -> dict:
    if not re.fullmatch(r"[a-f0-9]{64}", container_id):
        raise RuntimeError("Native stats require an exact Docker container ID")
    completed = subprocess.run(["curl", "--unix-socket", "/var/run/docker.sock", "--noproxy", "*",
        "--silent", "--show-error", "--fail", "--max-time", "5",
        f"http://localhost/containers/{container_id}/stats?stream=false&one-shot=true"],
        check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
    return json.loads(completed.stdout)


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    capabilities = request(api, "GET", "agents").json()["capabilities"]
    if capabilities["protocolMajor"] != 1 or capabilities["protocolMinor"] < 10 or "docker.usage" not in capabilities["capabilities"]:
        raise RuntimeError("Installed Agent does not provide native container usage")
    samples = [request(api, "GET", "containers/usage").json() for _ in range(3)]
    for batch in samples:
        if batch["isStale"] or batch["intervalSeconds"] != 6 or not batch["items"] or len(batch["items"]) > 256:
            raise RuntimeError("Native usage cache is unavailable, stale or unbounded")
        if len({item["containerId"] for item in batch["items"]}) != len(batch["items"]):
            raise RuntimeError("Usage batch has duplicate identities")
        for item in batch["items"]:
            if item["quality"] not in {"Good", "Warmup", "Stopped", "Unavailable"}:
                raise RuntimeError("Unknown usage quality")
            if item["cpuPercent"] is not None and (not math.isfinite(item["cpuPercent"]) or item["cpuPercent"] < 0):
                raise RuntimeError("Invalid CPU usage")
    for before, after in zip(samples, samples[1:]):
        if before["sampledAtUtc"] == after["sampledAtUtc"] and before["items"] != after["items"]:
            raise RuntimeError("Reading statistics changed canonical sample values")
    name = f"wdv-usage-{uuid.uuid4().hex[:12]}"
    try:
        api.run("create", "", {"name": name, "image": "nginxinc/nginx-unprivileged:alpine", "cpus": "0.25",
            "memoryMb": "32", "restartPolicy": "no", "networks": "none",
            "command": json.dumps(["sh", "-c", "while :; do :; done"])}, area="containers", requires_plan=True, confirmed=True)
        containers = request(api, "GET", "containers?includeStopped=true").json()
        owned = next(item for item in containers if item["name"] == name)
        container_id = owned["id"]
        if owned["isProtected"] or owned.get("ports"):
            raise RuntimeError("Disposable test container escaped its ordinary isolated scope")
        # Wait for the canonical first two batches, without triggering per-row sampling from the API.
        first = native_stats(container_id)
        time.sleep(13)
        second = native_stats(container_id)
        batch = request(api, "GET", "containers/usage").json()
        measured = next(item for item in batch["items"] if item["containerId"] == container_id)
        if batch["isStale"] or measured["quality"] != "Good" or measured["cpuPercent"] is None:
            raise RuntimeError("CPU sampler failed to leave warmup on a real busy container")
        system_delta = second["cpu_stats"]["system_cpu_usage"] - first["cpu_stats"]["system_cpu_usage"]
        cpu_delta = second["cpu_stats"]["cpu_usage"]["total_usage"] - first["cpu_stats"]["cpu_usage"]["total_usage"]
        native_cpu = cpu_delta / system_delta * second["cpu_stats"]["online_cpus"] * 100
        if not 10 < measured["cpuPercent"] < 45 or abs(measured["cpuPercent"] - native_cpu) > 15:
            raise RuntimeError(f"Canonical/native CPU comparison failed: api={measured['cpuPercent']:.2f}, native={native_cpu:.2f}")
        memory = second["memory_stats"]
        counters = memory.get("stats", {})
        cache = counters.get("total_inactive_file", counters.get("inactive_file", 0))
        expected_memory = memory["usage"] - cache if cache < memory["usage"] else memory["usage"]
        if measured["memoryLimitBytes"] != memory["limit"] or abs(measured["memoryUsedBytes"] - expected_memory) > 1024 * 1024:
            raise RuntimeError("Memory does not match native cgroup accounting")
        history = session.get(f"{base}/api/v1/metrics", params=[('kinds', 'container.cpu.utilization'), ('kinds', 'container.memory.used'), ('window', 'live')], timeout=30)
        history.raise_for_status()
        persisted = [item for item in history.json() if item["deviceId"] == container_id]
        if len(persisted) != 2 or any(not any(point["quality"] == "Good" and point["value"] is not None for point in item["points"]) for item in persisted):
            raise RuntimeError("Worker did not persist real container CPU/memory samples")
        api.run("stop", container_id, {}, area="containers", confirmed=True)
        time.sleep(7)
        stopped = next(item for item in request(api, "GET", "containers/usage").json()["items"] if item["containerId"] == container_id)
        if stopped["quality"] != "Stopped" or stopped["cpuPercent"] is not None or stopped["memoryUsedBytes"] is not None:
            raise RuntimeError("Stopped containers were presented as measured zero usage")
        api.run("start", container_id, {}, area="containers")
        time.sleep(13)
        restarted = next(item for item in request(api, "GET", "containers/usage").json()["items"] if item["containerId"] == container_id)
        if restarted["quality"] not in {"Warmup", "Good"}:
            raise RuntimeError("Native sampling did not recover after restart")
        print(f"Container usage native acceptance passed: {len(samples[0]['items'])} real cached rows, six-second sampling, isolated 0.25-core CPU/native comparison, memory accounting, stopped state and restart recovery")
    finally:
        if any(item["name"] == name for item in request(api, "GET", "containers?includeStopped=true").json()):
            api.run("delete", name, {"volumes": "false"}, area="containers", requires_plan=True, confirmed=True)
            if any(item["name"] == name for item in request(api, "GET", "containers?includeStopped=true").json()):
                raise RuntimeError("Disposable usage container was not removed")

    anonymous = requests.Session()
    anonymous.trust_env = False
    endpoint = f"{base}/api/v1/containers/usage"
    if anonymous.get(endpoint, timeout=20, allow_redirects=False).status_code != 401:
        raise RuntimeError("Anonymous usage access was not denied")
    ticket = api.stage_secret(required_environment("VALIDATION_MANAGED_PASSWORD"))
    api.run("create-user", "", {"username": required_environment("VALIDATION_MANAGED_USERNAME"),
        "name": "Temporary ordinary usage-validation user", "email": "", "inputTicket": ticket, "inputKind": "password"}, area="identity")
    ordinary, profile = authenticate(base, required_environment("VALIDATION_MANAGED_USERNAME"), required_environment("VALIDATION_MANAGED_PASSWORD"), require_administrator=False)
    if profile.get("isAdministrator") is not False or ordinary.get(endpoint, timeout=20, allow_redirects=False).status_code != 403:
        raise RuntimeError("Ordinary users can access native container statistics")
    print("Container usage permissions passed: anonymous 401, ordinary user 403; temporary users are cleaned by the wrapper")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
