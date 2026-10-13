#!/usr/bin/env python3
"""Read real load/systemd/Docker observations; only create a temporary Info rule.

No host load generation, Agent/service stop, production metric rewrite or container lifecycle action.
Failure/offline transitions are proven separately in the isolated native database test.
"""
from __future__ import annotations

import json
import math
import os
import re
import subprocess
import time

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    capabilities = request(api, "GET", "agents").json()["capabilities"]
    assert capabilities["protocolMajor"] == 1 and capabilities["protocolMinor"] >= 16
    assert "host.load-average" in capabilities["capabilities"]
    snapshot = request(api, "GET", "overview").json()
    kinds = {"load.average.1", "load.average.5", "load.average.15", "load.normalized"}
    metrics = {item["kind"]: item for item in snapshot["metrics"] if item["kind"] in kinds}
    assert set(metrics) == kinds and len({item["sampledAtUtc"] for item in metrics.values()}) == 1
    assert all(item["quality"] == "Good" and math.isfinite(item["value"]) and item["value"] >= 0 for item in metrics.values())
    cores = snapshot["host"]["logicalProcessorCount"]
    assert cores > 0 and math.isclose(metrics["load.normalized"]["value"], metrics["load.average.1"]["value"] / cores * 100, abs_tol=1e-9)
    native_load = [float(value) for value in open("/proc/loadavg", encoding="utf-8").read().split()[:3]]
    assert len(native_load) == 3 and all(math.isfinite(value) for value in native_load)
    history = request(api, "GET", "metrics?window=live&take=10&" + "&".join(f"kinds={kind}" for kind in sorted(kinds))).json()
    assert {item["kind"] for item in history} == kinds and all(item["points"] for item in history)
    units = request(api, "GET", "systemd").json()
    assert units
    for unit in units:
        name = unit["name"]
        assert re.fullmatch(r"[a-zA-Z0-9_.@-]+\.(?:service|timer)", name)
        result = subprocess.run(["systemctl", "show", name, "--property=ActiveState", "--property=SubState", "--property=LoadState"],
            check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)
        native = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
        assert unit["state"] == native["ActiveState"] and unit["attributes"]["subState"] == native["SubState"]
        assert unit["attributes"]["loadState"] == native["LoadState"]
    containers = request(api, "GET", "containers?includeStopped=true").json()
    native = subprocess.run(["docker", "ps", "--all", "--format", "{{.Names}}|{{.Status}}"],
        check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)
    status = dict(line.split("|", 1) for line in native.stdout.splitlines())
    for container in containers:
        # Durations may advance between reads; compare only the authoritative health suffix.
        for marker in ("(healthy)", "(unhealthy)", "(health: starting)"):
            assert container["status"].endswith(marker) == status[container["name"]].endswith(marker)
    payload = {"ruleType": "LoadAverage", "resourceSelectorJson": '{"resourceIds":["local"]}',
        "thresholdJson": '{"value":0,"recoveryValue":0}', "evaluationWindowSeconds": 12,
        "severity": "Info", "isEnabled": True, "expectedVersion": None}
    rule = request(api, "POST", "alerts/rules", payload=payload).json()
    def wait_event(state: str):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            matches = [item for item in request(api, "GET", "alerts?includeRecovered=true").json() if item["ruleId"] == rule["id"]]
            if len(matches) == 1 and matches[0]["state"] == state: return matches[0]
            time.sleep(2)
        raise RuntimeError("Temporary load rule did not reach the expected state")
    try:
        event = wait_event("Active")
        assert event["monitoring"]["triggered"] is True and event["monitoring"]["coveragePercent"] >= 80
        assert math.isfinite(event["monitoring"]["value"])
    finally:
        rule = request(api, "POST", "alerts/rules", payload=dict(rule, isEnabled=False, expectedVersion=rule["version"])).json()
    recovered = wait_event("Recovered")
    assert recovered["monitoring"]["reason"] == "RuleDisabled"
    print(json.dumps({"healthAlertAcceptance": "passed", "logicalProcessors": cores, "loadKinds": len(kinds),
        "systemdUnits": len(units), "containers": len(containers), "temporaryRule": "disabled/event-recovered",
        "productionServiceLifecycleMutations": 0, "productionMetricRewrites": 0}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
