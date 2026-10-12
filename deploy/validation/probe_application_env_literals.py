#!/usr/bin/env python3
"""Disposable native Compose parser probe. Synthetic values only; no containers are started."""
import json
import subprocess
import tempfile
import uuid
from pathlib import Path

values = {
    "QUOTE": "a'b", "SLASH": "ends\\", "SLASH_QUOTE": "a\\'b", "DOLLAR": "$BASE ${BASE} $$",
    "MULTILINE": "line1\nline2", "LITERAL_ESCAPE": "a\\nb", "EMPTY": "", "UNICODE": "中文值",
    "DOUBLE_QUOTE": 'a"b', "TAB": "left\tright",
}
results = {}
image = "nginxinc/nginx-unprivileged:alpine"
image_metadata = json.loads(subprocess.run(["docker", "image", "inspect", image], check=True, text=True, stdout=subprocess.PIPE, timeout=20).stdout)[0]
assert not image_metadata.get("Config", {}).get("Volumes"), "Probe image must not create volumes"
owned_name = "wdv-env-probe-" + uuid.uuid4().hex
with tempfile.TemporaryDirectory(prefix="whaledeck-env-probe-") as directory:
    root = Path(directory)
    model = {"name": owned_name, "services": {"app": {"image": image, "container_name": owned_name,
        "network_mode": "none", "restart": "no", "env_file": [".env"]}}}
    (root / "compose.json").write_text(json.dumps(model), encoding="utf-8")
    for name, encoder in {
        "single": lambda value: "'" + value.replace("'", "\\'") + "'",
        "double": lambda value: '"' + value.replace("\\", "\\\\").replace('"', '\\"').replace("$", "\\$").replace("\n", "\\n").replace("\t", "\\t") + '"',
        "hybrid": lambda value: ('"' + value.replace("\\", "\\\\").replace('"', '\\"').replace("$", "\\$").replace("\n", "\\n") + '"') if "\\" in value else "'" + value.replace("'", "\\'") + "'",
    }.items():
        checks = {}
        runtime_checks = {}
        for key, expected in values.items():
            (root / ".env").write_text("BASE=must-not-expand\n" + key + "=" + encoder(expected) + "\n", encoding="utf-8")
            completed = subprocess.run(["docker", "compose", "--project-directory", directory, "--file", str(root / "compose.json"),
                "--env-file", str(root / ".env"), "config", "--format", "json"], text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
            checks[key] = completed.returncode == 0 and json.loads(completed.stdout)["services"]["app"]["environment"].get(key) == expected
            try:
                created = subprocess.run(["docker", "compose", "--project-directory", directory, "--file", str(root / "compose.json"),
                    "--env-file", str(root / ".env"), "create", "--pull", "never", "--no-build"], text=True,
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
                if created.returncode != 0:
                    runtime_checks[key] = False
                    continue
                container = json.loads(subprocess.run(["docker", "inspect", owned_name], check=True, text=True, stdout=subprocess.PIPE, timeout=20).stdout)[0]
                assert container["Name"] == "/" + owned_name and container["State"]["Status"] == "created"
                assert not container.get("Mounts") and not container["HostConfig"].get("PortBindings")
                environment = dict(item.split("=", 1) for item in container["Config"]["Env"])
                runtime_checks[key] = environment.get(key) == expected
            finally:
                removed = subprocess.run(["docker", "rm", owned_name], text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
                if created.returncode == 0:
                    assert removed.returncode == 0, "Owned inert container cleanup failed"
        results[name] = checks
        results[name + "_container_metadata"] = runtime_checks
assert all(results["double_container_metadata"].values()), "Approved encoder changed native environment semantics"
assert not root.exists(), "Owned temporary directory was not removed"
print(json.dumps({"nativeComposeLiteralProbe": results, "containersStarted": 0, "inertContainersRemoved": True, "temporaryDirectoryRemoved": True}))
