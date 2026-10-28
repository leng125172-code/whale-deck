#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

[[ $EUID -eq 0 ]] || { echo 'Run this validator with sudo.' >&2; exit 1; }
python3 "$(dirname "$0")/validate-runtime.py" --installed /opt

assert_metadata() {
  local path=$1 expected=$2 actual
  actual=$(stat -c '%a:%U:%G' "$path")
  [[ $actual == "$expected" ]] || {
    printf 'Unexpected metadata for %s: expected %s, got %s\n' "$path" "$expected" "$actual" >&2
    exit 1
  }
}

for unit in whaledeck-agent.service whaledeck-maintenance.service; do
  systemctl is-enabled --quiet "$unit"
  systemctl is-active --quiet "$unit"
  [[ $(systemctl show "$unit" -p NRestarts --value) == 0 ]]
done

for attempt in {1..30}; do
  [[ -S /run/whaledeck/agent.sock ]] && break
  sleep 1
done
[[ -S /run/whaledeck/agent.sock ]]
assert_metadata /run/whaledeck '770:whaledeck-agent:whaledeck'
assert_metadata /run/whaledeck/agent.sock '660:whaledeck-agent:whaledeck'
assert_metadata /run/whaledeck/maintenance '770:whaledeck-agent:whaledeck'
assert_metadata /etc/whaledeck '750:root:whaledeck'
assert_metadata /etc/whaledeck/agent-capability.key '640:root:whaledeck'
assert_metadata /etc/whaledeck/resources.json '640:root:whaledeck'
assert_metadata /etc/whaledeck/agent.env '640:root:whaledeck'
assert_metadata /usr/local/libexec/whaledeck-privileged '750:root:root'
assert_metadata /usr/local/libexec/whaledeck-container-files.py '750:root:root'
[[ ! -L /usr/local/libexec/whaledeck-container-files.py && $(stat -c %h /usr/local/libexec/whaledeck-container-files.py) == 1 ]]
assert_metadata /var/lib/whaledeck-container-files '700:root:root'
[[ ! -L /var/lib/whaledeck-container-files ]]
for module in /usr/local/libexec/whaledeck-container-directories.py /usr/local/libexec/whaledeck-container-directory-transport.py /usr/local/libexec/whaledeck-container-file-write.py /usr/local/libexec/whaledeck-container-upload.py /usr/local/libexec/whaledeck-container-backups.py /usr/local/libexec/whaledeck-container-publish.py /usr/local/libexec/whaledeck-container-history.py /usr/local/libexec/whaledeck-container-history-transport.py /usr/local/libexec/whaledeck-container-restore.py; do
  assert_metadata "$module" '750:root:root'
  [[ ! -L $module && $(stat -c %h "$module") == 1 ]]
done
assert_metadata /var/lib/whaledeck-container-uploads '700:root:root'
assert_metadata /var/lib/whaledeck-container-directories '700:root:root'
[[ ! -L /var/lib/whaledeck-container-directories ]]
assert_metadata /var/lib/whaledeck-container-copies '700:root:root'
[[ ! -L /var/lib/whaledeck-container-copies ]]
for module in /usr/local/libexec/whaledeck-container-copy-scope.py /usr/local/libexec/whaledeck-container-copies.py /usr/local/libexec/whaledeck-container-copy-transport.py; do
  assert_metadata "$module" '750:root:root'
  [[ ! -L "$module" && $(stat -c %h "$module") == 1 ]]
done
[[ ! -L /var/lib/whaledeck-container-uploads ]]
assert_metadata /usr/local/libexec/whaledeck-files.py '750:root:root'
assert_metadata /usr/local/libexec/whaledeck-file-operations.py '750:root:root'
assert_metadata /usr/local/libexec/whaledeck-file-backups.py '750:root:root'
assert_metadata /usr/local/libexec/whaledeck-file-archives.py '750:root:root'
for module in /usr/local/libexec/whaledeck-file-tree.py /usr/local/libexec/whaledeck-tree-copies.py /usr/local/libexec/whaledeck-tree-moves.py /usr/local/libexec/whaledeck-tree-archive.py /usr/local/libexec/whaledeck-tree-packing.py /usr/local/libexec/whaledeck-tree-unpacking.py /usr/local/libexec/whaledeck-tree-backups.py /usr/local/libexec/whaledeck-tree-recycling.py /usr/local/libexec/whaledeck-tree-restoring.py /usr/local/libexec/whaledeck-tree-overwrite-restoring.py; do
  assert_metadata "$module" '750:root:root'
  [[ ! -L $module && $(stat -c %h "$module") == 1 ]]
done
[[ ! -L /etc/whaledeck/file-backups.json && $(stat -c %u /etc/whaledeck/file-backups.json) == 0 && $(stat -c %h /etc/whaledeck/file-backups.json) == 1 ]]
assert_metadata /usr/local/libexec/whaledeck-tree-purging.py '750:root:root'
[[ ! -L /usr/local/libexec/whaledeck-tree-purging.py && $(stat -c %h /usr/local/libexec/whaledeck-tree-purging.py) == 1 ]]
[[ $(stat -c %a /etc/whaledeck/file-backups.json) =~ ^(600|640|644)$ ]]
assert_metadata /data/DockerData/whaledeck/file-backups '700:root:root'
python3 "$(dirname "$0")/configure-file-backups.py" --check
assert_metadata /var/lib/whaledeck-files '700:root:root'
[[ ! -L /etc/whaledeck/file-roots.json && $(stat -c %u /etc/whaledeck/file-roots.json) == 0 ]]
[[ $(stat -c %a /etc/whaledeck/file-roots.json) =~ ^(600|640|644)$ ]]
assert_metadata /etc/sudoers.d/whaledeck-agent '440:root:root'
visudo -cf /etc/sudoers.d/whaledeck-agent >/dev/null

[[ $(runuser -u whaledeck-agent -- sudo -n /usr/local/libexec/whaledeck-privileged self-check) == ok ]]
runuser -u whaledeck-agent -- docker version --format '{{.Server.Version}}' >/dev/null
runuser -u whaledeck-agent -- test -r /etc/whaledeck/agent-capability.key
runuser -u whaledeck-agent -- test ! -w /etc/whaledeck/agent-capability.key
runuser -u whaledeck-agent -- test ! -w /etc/whaledeck/file-roots.json

temporary=$(mktemp -d /tmp/whaledeck-validate.XXXXXX)
cleanup() {
  rm -f -- "$temporary/grpc-headers" "$temporary/grpc-body" "$temporary/status.json"
  rmdir -- "$temporary"
}
trap cleanup EXIT

probe_status() {
  local address=$1 port=$2 code attempt
  for attempt in {1..30}; do
    if code=$(curl --noproxy '*' --max-time 5 --silent \
      --output "$temporary/status.json" --write-out '%{http_code}' \
      "http://$address:$port/maintenance/status") && \
      [[ $code == 200 || $code == 503 ]] && \
      jq -e '.state | type == "string"' "$temporary/status.json" >/dev/null; then
      return 0
    fi
    sleep 0.2
  done

  printf 'Maintenance status probe failed for %s:%s (last HTTP status: %s).\n' \
    "$address" "$port" "${code:-unreachable}" >&2
  return 1
}

printf '\0\0\0\0\0' | curl --http2-prior-knowledge --unix-socket /run/whaledeck/agent.sock \
  --silent --show-error --dump-header "$temporary/grpc-headers" --output "$temporary/grpc-body" \
  --header 'Content-Type: application/grpc' --header 'TE: trailers' --data-binary @- \
  http://localhost/whaledeck.agent.v1.AgentService/GetHealth
grep -Eqi '^grpc-status: *0' "$temporary/grpc-headers"
(( $(stat -c '%s' "$temporary/grpc-body") >= 5 ))

# A process that reaches the socket through whaledeck as a supplementary group
# must still be rejected when its effective primary group does not match.
set +e
whaledeck_gid=$(getent group whaledeck | cut -d: -f3)
nobody_uid=$(id -u nobody)
nobody_gid=$(id -g nobody)
setpriv --reuid="$nobody_uid" --regid="$nobody_gid" --groups="$whaledeck_gid" \
  curl --fail --http2-prior-knowledge --unix-socket /run/whaledeck/agent.sock \
  --silent --output /dev/null --header 'Content-Type: application/grpc' \
  --header 'TE: trailers' --data-binary '' \
  http://localhost/whaledeck.agent.v1.AgentService/GetHealth
untrusted_status=$?
set -e
(( untrusted_status != 0 ))

mapfile -t active_entry_addresses < <(
  ip -o -4 address show scope global up |
    awk '$2 !~ /^(docker0|br-|veth|virbr|cni|flannel|tun|tap)/ {sub(/\/.*/, "", $4); print $4}' |
    sort -u
)
(( ${#active_entry_addresses[@]} > 0 )) || {
  echo 'No active non-container IPv4 entry point was discovered.' >&2
  exit 1
}

for address in "${active_entry_addresses[@]}"; do
  for port in 8080 8081; do
    probe_status "$address" "$port"
  done
  printf 'Validated active entry point %s on ports 8080 and 8081.\n' "$address"
done

# Loopback remains available for local health checks, while Docker bridge
# addresses must not expose the maintenance endpoint.
for port in 8080 8081; do
  probe_status 127.0.0.1 "$port"
done

bridge_address=$(ip -o -4 address show | awk '$2 ~ /^(docker0|br-)/ {sub(/\/.*/, "", $4); print $4; exit}')
if [[ -n ${bridge_address:-} ]]; then
  code=$(curl --noproxy '*' --max-time 5 --silent --show-error \
    --output /dev/null --write-out '%{http_code}' \
    "http://$bridge_address:8080/maintenance/status")
  [[ $code == 403 ]]
fi

echo 'Whale Deck host services passed: systemd, permissions, UDS gRPC, restricted helper, Docker access and dynamically discovered entry points.'
