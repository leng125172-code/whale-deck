#!/usr/bin/env python3
"""Read-only acceptance: compare selected relationship fields with native Docker inspect."""
from __future__ import annotations

import json
import os
import re
import subprocess

import requests

from validate_databases import Api
from validate_governance import request
from validate_oidc import authenticate, required_environment
from run_with_ephemeral_admin import container_environment


def native(container_id: str) -> dict:
    if not re.fullmatch(r"[a-f0-9]{64}", container_id):
        raise RuntimeError("Only exact native Docker container IDs are accepted")
    completed = subprocess.run(["docker", "inspect", "--type", "container", container_id],
        check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
    return json.loads(completed.stdout)[0]


def main() -> int:
    base = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    session, _ = authenticate(base, required_environment("VALIDATION_USERNAME"), required_environment("VALIDATION_PASSWORD"))
    api = Api(base, session)
    capabilities = request(api, "GET", "agents").json()["capabilities"]
    assert capabilities["protocolMajor"] == 1 and capabilities["protocolMinor"] >= 11
    assert "docker.relationships" in capabilities["capabilities"]
    containers = request(api, "GET", "containers?includeStopped=true").json()
    resources = {kind: {item["id"] for item in request(api, "GET", kind).json()} for kind in ("images", "networks", "volumes")}
    projects = {item["attributes"].get("composeProject") for item in request(api, "GET", "compose-projects").json()}
    checked = endpoints = mounts = mappings = 0
    for container in containers:
        metadata = native(container["id"])
        inspect = request(api, "GET", f"containers/{container['id']}/inspect").json()
        relation = inspect["relationships"]
        assert relation and not relation["isTruncated"]
        assert relation["imageId"] == metadata["Image"] == container["imageId"]
        assert "image:" + relation["imageId"] in resources["images"]
        labels = metadata.get("Config", {}).get("Labels") or {}
        assert relation["composeProject"] == labels.get("com.docker.compose.project", "")
        assert relation["composeService"] == labels.get("com.docker.compose.service", "")
        if relation["composeProject"]:
            assert relation["composeProject"] in projects
        networks = metadata.get("NetworkSettings", {}).get("Networks") or {}
        assert len(relation["endpoints"]) == len(networks)
        assert set(container["networkIds"]) == {item["NetworkID"] for item in networks.values() if item.get("NetworkID")}
        for endpoint in relation["endpoints"]:
            expected = networks[endpoint["name"]]
            for public, original in {"networkId": "NetworkID", "ipv4Address": "IPAddress", "ipv4PrefixLength": "IPPrefixLen",
                "ipv4Gateway": "Gateway", "ipv6Address": "GlobalIPv6Address", "ipv6PrefixLength": "GlobalIPv6PrefixLen",
                "ipv6Gateway": "IPv6Gateway", "macAddress": "MacAddress"}.items():
                assert endpoint[public] == (expected.get(original) or (0 if "Length" in public else ""))
            if endpoint["networkId"]:
                assert "network:" + endpoint["networkId"] in resources["networks"]
            endpoints += 1
        expected_mounts = sorted(metadata.get("Mounts") or [], key=lambda item: item["Destination"])
        assert relation["mounts"] == [{"type": item.get("Type", ""), "name": item.get("Name", ""),
            "source": item.get("Source", ""), "destination": item.get("Destination", ""), "readWrite": item["RW"]} for item in expected_mounts]
        assert set(container["volumeNames"]) == {item["Name"] for item in expected_mounts if item["Type"] == "volume" and item.get("Name")}
        for mount in relation["mounts"]:
            if mount["type"] == "volume" and mount["name"]:
                assert "volume:" + mount["name"] in resources["volumes"]
            mounts += 1
        bindings = metadata.get("NetworkSettings", {}).get("Ports") or metadata.get("HostConfig", {}).get("PortBindings") or {}
        ports = sorted(set((metadata.get("Config", {}).get("ExposedPorts") or {}).keys()) | set(bindings.keys()))
        expected_ports = []
        for port in ports:
            values = bindings.get(port)
            if not values:
                expected_ports.append({"containerPort": port, "hostAddress": "", "hostPort": "", "published": False})
            else:
                expected_ports.extend({"containerPort": port, "hostAddress": item.get("HostIp", ""), "hostPort": item.get("HostPort", ""), "published": True} for item in values)
        assert relation["portBindings"] == expected_ports
        assert not {"environment", "env", "config", "driverOptions", "options"}.intersection(relation)
        mappings += len(expected_ports)
        checked += 1
    anonymous = requests.Session()
    anonymous.trust_env = False
    for endpoint in ["containers", "images", "networks", "volumes", f"containers/{containers[0]['id']}/inspect"]:
        assert anonymous.get(f"{base}/api/v1/{endpoint}", timeout=20).status_code == 401
    # This second account is owned by the outer validation wrapper, which removes it in finally.
    identity = requests.Session()
    identity.trust_env = False
    identity.headers["Authorization"] = "Bearer " + container_environment()["Authentication__Authentik__ApiToken"]
    identity_base = "http://127.0.0.1:8081/api/v3/"
    created = identity.post(identity_base + "core/users/", json={"username": required_environment("VALIDATION_MANAGED_USERNAME"),
        "name": "Whale Deck relationship readonly acceptance", "is_active": True, "groups": []}, timeout=20)
    created.raise_for_status()
    changed = identity.post(identity_base + f"core/users/{created.json()['pk']}/set_password/",
        json={"password": required_environment("VALIDATION_MANAGED_PASSWORD")}, timeout=20)
    changed.raise_for_status()
    ordinary, ordinary_profile = authenticate(base, required_environment("VALIDATION_MANAGED_USERNAME"),
        required_environment("VALIDATION_MANAGED_PASSWORD"), require_administrator=False)
    assert ordinary_profile["isAdministrator"] is False
    for endpoint in ["containers", "images", "networks", "volumes", f"containers/{containers[0]['id']}/inspect"]:
        assert ordinary.get(f"{base}/api/v1/{endpoint}", timeout=20).status_code == 403
    print(json.dumps({"containerRelationships": "passed", "containers": checked, "networkEndpoints": endpoints,
        "mounts": mounts, "portEntries": mappings, "anonymous": "401", "ordinary": "403", "mode": "read-only-native-comparison"}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
