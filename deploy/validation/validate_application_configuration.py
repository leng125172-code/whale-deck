#!/usr/bin/env python3
"""Native temporary application editing, secrecy and data-retention acceptance."""
import json
import os
import re
import subprocess
import time
import uuid

import requests
from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment
from validate_metric_partition_runtime import query


def native(arguments):
    completed = subprocess.run(["docker", *arguments], check=True, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45)
    return completed.stdout


def main():
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    slug = "wdv-config-" + uuid.uuid4().hex[:12]
    resource = "application:" + slug
    container = "whaledeck-app-" + slug
    volume = container + "-cache"
    marker = "SYNTHETIC_" + uuid.uuid4().hex
    literal = marker + " $BASE ${BASE} 'quote' \\\n中文\tend"
    command = ["/bin/sh", "-c", "printf '%s' '$WHALEDECK_LITERAL' >/dev/null; exec nginx -g 'daemon off;' #" + marker]
    installed = False
    ever_installed = False
    proof_written = False
    def snapshot():
        return request(api, "GET", "applications/configuration?resourceId=" + resource).json()
    def stage_plan(input, fingerprint):
        ticket = api.stage_secret(json.dumps(input, ensure_ascii=False).ljust(16))
        preview = api.post("applications/configuration/plan", {"resourceId": resource,
            "expectedFingerprint": fingerprint, "inputTicket": ticket}, 200).json()
        assert marker not in json.dumps(preview)
        return ticket, preview["planHash"]
    def execute(ticket, plan, fingerprint):
        job = api.post("applications/configure", {"resourceId": resource, "parameters": {"slug": slug,
            "inputKind": "environment", "inputTicket": ticket, "expectedFingerprint": fingerprint},
            "idempotencyKey": "configuration-test:" + uuid.uuid4().hex, "planHash": plan, "confirmed": True}, 202).json()
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            observed = request(api, "GET", "jobs/" + job["id"]).json()
            assert marker not in json.dumps(observed)
            if observed["state"] == "Succeeded": return observed
            if observed["state"] in {"Failed", "Canceled", "RolledBack"}:
                raise RuntimeError("Configuration job failed: " + str(observed.get("errorCode")))
            time.sleep(2)
        raise RuntimeError("Configuration job did not reach a terminal state")
    try:
        api.run("install", "", {"slug": slug, "image": "nginxinc/nginx-unprivileged:alpine", "autoupdate": "false"},
            area="applications", confirmed=True, requires_plan=True, timeout=180)
        installed = True
        ever_installed = True
        before = json.loads(native(["inspect", "--type", "container", container]))[0]
        original = snapshot()
        input = {"services": {"app": {"cpus": 0.25, "memoryMb": 64, "command": command,
            "entrypoint": [], "ports": ["127.0.0.1:0:8080/tcp"], "volumes": [
                {"type": "bind", "source": original["ownedDataDirectory"], "target": "/tmp/retained-data", "readOnly": False},
                {"type": "volume", "source": volume, "target": "/tmp/app-cache", "readOnly": False}]}},
            "environment": {"set": {"LITERAL": literal, "EMPTY": ""}, "remove": []}, "apply": False}
        ticket, plan = stage_plan(input, original["fingerprint"])
        # A second preflight with the same owner ticket proves preflight does not consume it.
        api.post("applications/configuration/plan", {"resourceId": resource,
            "expectedFingerprint": original["fingerprint"], "inputTicket": ticket}, 200)
        saved = execute(ticket, plan, original["fingerprint"])
        unchanged = json.loads(native(["inspect", "--type", "container", container]))[0]
        assert before["Id"] == unchanged["Id"] and before["State"]["StartedAt"] == unchanged["State"]["StartedAt"]
        assert json.loads(saved["resultJson"])["applied"] is False
        api.post("secrets/consume", {"token": ticket}, 404)
        stale = session.post(base + "/api/v1/applications/configuration/plan", headers=api.headers,
            json={"resourceId": resource, "expectedFingerprint": original["fingerprint"], "inputTicket": api.stage_secret('{"apply":true}   ')}, timeout=30)
        assert stale.status_code in {400, 409, 422}
        current = snapshot()
        assert marker not in json.dumps(current) and current["services"][0]["commandPresent"] is True
        ticket, plan = stage_plan({"apply": True}, current["fingerprint"])
        execute(ticket, plan, current["fingerprint"])
        actual = json.loads(native(["inspect", "--type", "container", container]))[0]
        assert actual["Id"] != before["Id"] and actual["State"]["Running"]
        assert actual["HostConfig"]["NanoCpus"] == 250_000_000 and actual["HostConfig"]["Memory"] == 64 * 1024 * 1024
        environment = dict(item.split("=", 1) for item in actual["Config"]["Env"])
        assert environment["LITERAL"] == literal and environment["EMPTY"] == ""
        assert actual["Config"]["Cmd"] == command
        assert actual["Config"]["Labels"]["io.whaledeck.runtime-overrides-private"] == "true"
        assert any(mount["Type"] == "bind" and mount["Source"] == current["ownedDataDirectory"] for mount in actual["Mounts"])
        redacted = request(api, "GET", "containers/" + actual["Id"] + "/inspect").json()
        assert marker not in json.dumps(redacted) and redacted["command"] == []
        native(["exec", "--user", "0", container, "/bin/sh", "-c", "printf data-proof > /tmp/retained-data/probe.txt"])
        proof_written = True
        count = query(f"SELECT json_build_object('jobs',(SELECT count(*) FROM operation_jobs WHERE request_json::text LIKE '%{marker}%'), 'audit',(SELECT count(*) FROM audit_events WHERE detail_json::text LIKE '%{marker}%'));")
        assert count == {"jobs": 0, "audit": 0}
        anonymous = requests.Session(); anonymous.trust_env = False
        assert anonymous.get(base + "/api/v1/applications/configuration?resourceId=" + resource, timeout=20).status_code == 401
        print(json.dumps({"applicationConfiguration": "passed", "saveOnly": "same-container-and-start-time",
            "redeploy": "healthy-new-container", "nativeEnvironment": "exact", "nativeResources": "exact",
            "privateCommand": "native-exact-api-redacted", "preflight": "non-consuming", "staleFingerprint": "rejected",
            "nativePostgresMarkerCounts": count, "anonymous": "401"}))
    finally:
        if installed:
            api.run("uninstall", "", {"slug": slug, "deleteVolumes": "true"}, area="applications",
                confirmed=True, requires_plan=True, timeout=120)
            installed = False
        # Exact generated prefix; no application data outside this temporary installation is read or removed.
        prefix = slug + "-"
        reader = "wdv-config-reader-" + uuid.uuid4().hex
        listing = native(["run", "--rm", "--name", reader, "--network", "none", "--read-only", "--cap-drop", "ALL",
            "--user", "997:975", "--memory", "32m", "--cpus", "0.25", "--mount",
            "type=bind,source=/data/WhaleDeck/apps/.retained,target=/retained,readonly", "alpine:3.22",
            "find", "/retained", "-mindepth", "1", "-maxdepth", "1", "-name", prefix + "*"]).strip().splitlines() if ever_installed else []
        for path in listing:
            name = path.rsplit("/", 1)[-1]
            if not re.fullmatch(re.escape(prefix) + "[a-f0-9]{32}", name): raise RuntimeError("Unexpected retained test path")
            proof = native(["run", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL", "--user", "997:975",
                "--memory", "32m", "--cpus", "0.25", "--mount",
                "type=bind,source=/data/WhaleDeck/apps/.retained/" + name + ",target=/app,readonly", "alpine:3.22",
                "/bin/sh", "-c", "stat -c %a /app/.env /app/compose.yml /app/manifest.json" + ("; cat /app/data/probe.txt" if proof_written else "")])
            assert proof == "600\n600\n600\n" + ("data-proof" if proof_written else ""), "Private modes or retained test data differ"
            native(["run", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE",
                "--memory", "32m", "--cpus", "0.25", "--mount", "type=bind,source=/data/WhaleDeck/apps/.retained,target=/retained",
                "alpine:3.22", "rm", "-rf", "--", "/retained/" + name])
        remaining = subprocess.run(["docker", "volume", "inspect", volume], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
        assert remaining.returncode != 0, "Temporary named volume remains"
        print(json.dumps({"temporaryApplicationCleanup": "passed", "retainedDirectoriesRemoved": len(listing), "namedVolumeRemoved": True}))


if __name__ == "__main__": main()
