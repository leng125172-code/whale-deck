#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

installer_version="0.1.0"
install_root="${WHALEDECK_INSTALL_ROOT:-/data/GitRepos}"
database_repository="${WHALEDECK_DATABASE_REPOSITORY:-https://github.com/leng125172-code/database-platform.git}"
database_branch="${WHALEDECK_DATABASE_BRANCH:-main}"
source_repository="${WHALEDECK_SOURCE_REPOSITORY:-https://github.com/leng125172-code/whale-deck.git}"
source_branch="${WHALEDECK_SOURCE_BRANCH:-master}"
configure_mirror="${WHALEDECK_CONFIGURE_MIRROR:-}"
registry_mirror="${WHALEDECK_REGISTRY_MIRROR:-}"
non_interactive=false
prepare_only=false
dependencies_only=false
dry_run=false
install_owner="${SUDO_USER:-root}"
log_file="/var/log/whaledeck-install.log"

usage() {
  cat <<'EOF'
Whale Deck staged installer

Usage: sudo ./install.sh [options]
  --non-interactive        Never prompt; required values come from environment.
  --prepare-only           Stop after package and Docker preparation.
  --dependencies-only      Stop after database-platform is healthy.
  --dry-run                Print stages without network, root access or changes.
  --install-root PATH      Repository parent directory (default /data/GitRepos).
  --database-branch NAME   database-platform branch (default main).
  --source-branch NAME     Whale Deck branch (default master).
  --configure-mirror y|n  Merge a registry mirror into daemon.json.
  --mirror-url URL         Registry mirror, for example a Xuanyuan tenant URL.
  -h, --help               Show this help.
EOF
}

while (($#)); do
  case "$1" in
    --non-interactive) non_interactive=true ;;
    --prepare-only) prepare_only=true ;;
    --dependencies-only) dependencies_only=true ;;
    --dry-run) dry_run=true ;;
    --install-root) install_root=${2:?missing value}; shift ;;
    --database-branch) database_branch=${2:?missing value}; shift ;;
    --source-branch) source_branch=${2:?missing value}; shift ;;
    --configure-mirror) configure_mirror=${2:?missing value}; shift ;;
    --mirror-url) registry_mirror=${2:?missing value}; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
  shift
done

[[ $install_root == /* && $install_root != / && $install_root != /root && $install_root != /home ]] || { echo 'Use an absolute, dedicated repository parent directory.' >&2; exit 2; }
[[ -z $configure_mirror || $configure_mirror =~ ^[yYnN]$ ]] || { echo 'Mirror choice must be y or n.' >&2; exit 2; }
if [[ $dry_run == true ]]; then
  printf '%s\n' '1. Inspect Linux, systemd, storage and existing tools.' '2. Check package sources; measure HTTP response time.' '3. Install missing CLI tools and Docker plugins.' '4. Optionally merge registry mirrors with validation and rollback.'
  [[ $prepare_only == true ]] && exit 0
  printf '5. Clone/update database-platform (%s) under %s.\n' "$database_branch" "$install_root"
  printf '%s\n' '6. Prepare private settings, storage and database-platform-* dependencies.'
  [[ $dependencies_only == true ]] && exit 0
  printf '7. Clone/update Whale Deck (%s), validate settings, build host services/images, migrate and deploy.\n' "$source_branch"
  exit 0
fi

log() { printf '[Whale Deck] %s\n' "$*"; if [[ -f $log_file && -w $log_file ]]; then printf '[Whale Deck] %s\n' "$*" >> "$log_file"; fi; }
fail() { log "ERROR: $*"; exit 1; }
on_error() { log "Installation stopped at line $1. Correct the issue and rerun; completed stages are reusable."; }
trap 'on_error "$LINENO"' ERR

[[ $EUID -eq 0 ]] || fail 'Run the installer with sudo.'
[[ -r /etc/os-release ]] || fail '/etc/os-release is missing.'
# shellcheck disable=SC1091
source /etc/os-release
case "$ID" in ubuntu|debian) ;; *) fail "Unsupported distribution: $ID" ;; esac
command -v systemctl >/dev/null 2>&1 || fail 'systemd is required.'
install -d -m 0755 "$(dirname "$log_file")"
touch "$log_file"
chmod 0600 "$log_file"
if (( $(stat -c %s "$log_file") > 1048576 )); then mv -f "$log_file" "$log_file.1"; touch "$log_file"; chmod 0600 "$log_file"; fi
install -d -m 0755 /run/lock
exec 9>/run/lock/whaledeck-install.lock
flock -n 9 || fail 'Another Whale Deck installation is running.'
log "Installer V$installer_version on $PRETTY_NAME"

ask_yes_no() {
  local prompt=$1 default=${2:-n} answer
  if [[ $non_interactive == true ]]; then
    [[ $default =~ ^[yY]$ ]]
    return
  fi
  local choices='y/N'
  [[ $default == y ]] && choices='Y/n'
  read -r -p "$prompt [$choices] " answer
  answer=${answer:-$default}
  [[ $answer =~ ^[yY]$ ]]
}

probe_url() {
  local name=$1 url=$2 result
  result=$(curl -LIsS -o /dev/null --connect-timeout 4 --max-time 8 -w '%{http_code} %{time_total}' "$url" 2>/dev/null || true)
  [[ -n $result ]] && log "Source probe $name: $result" || log "Source probe $name: unavailable"
}

install_base_tools() {
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  local missing=() package
  for package in ca-certificates curl git gnupg jq openssl python3 rsync tar xz-utils sudo; do
    if ! dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -qx 'install ok installed'; then missing+=("$package"); fi
  done
  ((${#missing[@]} == 0)) || apt-get install -y --no-install-recommends "${missing[@]}"
}

ensure_docker_source() {
  if grep -RqsE 'download.docker.com|docker-ce/linux' /etc/apt/sources.list.d /etc/apt/sources.list 2>/dev/null; then return; fi
  log 'Adding the official Docker apt source.'
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
  chmod 0644 /etc/apt/keyrings/docker.asc
  local codename=${VERSION_CODENAME:?distribution codename is missing}
  printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' \
    "$(dpkg --print-architecture)" "$ID" "$codename" > /etc/apt/sources.list.d/docker.list
}

ensure_toolchain() {
  ensure_docker_source
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  if ! command -v docker >/dev/null 2>&1; then
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  fi
  if ! docker compose version >/dev/null 2>&1; then apt-get install -y docker-compose-plugin; fi
  if ! docker buildx version >/dev/null 2>&1; then apt-get install -y docker-buildx-plugin; fi
  systemctl enable --now docker
  docker version >/dev/null
  docker compose version >/dev/null
  git --version >/dev/null
}

configure_registry_mirror() {
  local daemon=/etc/docker/daemon.json current="" answer_default=n
  if [[ -r $daemon ]]; then
    current=$(jq -r '.["registry-mirrors"][0] // empty' "$daemon")
  fi
  if [[ -n $current && -z $registry_mirror ]]; then registry_mirror=$current; fi
  if [[ -z $configure_mirror ]]; then
    [[ $non_interactive == false ]] && answer_default=y
    if ask_yes_no 'Configure or preserve a Docker registry accelerator?' "$answer_default"; then configure_mirror=y; else configure_mirror=n; fi
  fi
  [[ $configure_mirror =~ ^[yY]$ ]] || { log 'Docker mirror configuration skipped.'; return; }
  if [[ -z $registry_mirror && $non_interactive == false ]]; then
    read -r -p 'Registry mirror URL (Xuanyuan tenant URL recommended): ' registry_mirror
  fi
  [[ $registry_mirror =~ ^https?://[^[:space:]]+$ ]] || fail 'A valid HTTP(S) registry mirror URL is required.'

  install -d -m 0755 /etc/docker
  local backup temporary
  backup="/etc/docker/daemon.json.whaledeck.$(date +%Y%m%d%H%M%S).bak"
  temporary=$(mktemp /etc/docker/daemon.json.whaledeck.XXXXXX)
  if [[ -r $daemon ]]; then
    cp -a "$daemon" "$backup"
    jq --arg mirror "${registry_mirror%/}" '.["registry-mirrors"] = ((.["registry-mirrors"] // []) + [$mirror] | unique)' "$daemon" > "$temporary"
  else
    backup=""
    jq -n --arg mirror "${registry_mirror%/}" '{"registry-mirrors":[$mirror]}' > "$temporary"
  fi
  dockerd --validate --config-file "$temporary" >/dev/null
  if [[ -r $daemon ]] && diff -q <(jq -S . "$daemon") <(jq -S . "$temporary") >/dev/null; then
    rm -f "$temporary"
    log 'Requested accelerator is already configured; no restart needed.'
    return
  fi
  install -o root -g root -m 0644 "$temporary" "$daemon"
  rm -f "$temporary"
  if ! systemctl restart docker || ! wait_for_docker; then
    if [[ -n $backup ]]; then install -o root -g root -m 0644 "$backup" "$daemon"; else rm -f /etc/docker/daemon.json; fi
    systemctl restart docker || true
    fail 'Docker rejected the accelerator configuration; the previous file was restored.'
  fi
  log 'Docker registry accelerator configured and validated.'
}

wait_for_docker() {
  local attempt
  for attempt in {1..30}; do
    if docker info >/dev/null 2>&1; then return 0; fi
    sleep 2
  done
  return 1
}

as_owner() {
  if [[ $install_owner == root ]]; then "$@"; else runuser -u "$install_owner" -- "$@"; fi
}

canonical_repository() {
  local value=${1%.git}
  value=${value#git@github.com:}
  value=${value#ssh://git@github.com/}
  value=${value#https://github.com/}
  value=${value#http://github.com/}
  printf '%s\n' "${value,,}"
}

clone_or_update() {
  local repository=$1 branch=$2 destination=$3
  if [[ -d $destination/.git ]]; then
    local actual_origin
    actual_origin=$(as_owner git -C "$destination" remote get-url origin)
    [[ $(canonical_repository "$actual_origin") == $(canonical_repository "$repository") ]] || fail "Repository origin does not match the configured source: $destination"
    [[ -z $(as_owner git -C "$destination" status --porcelain) ]] || fail "Repository has local changes: $destination"
    if as_owner timeout 30 git -C "$destination" fetch origin "$branch"; then
      as_owner git -C "$destination" checkout "$branch"
      as_owner git -C "$destination" merge --ff-only FETCH_HEAD
    else
      [[ $(as_owner git -C "$destination" branch --show-current) == "$branch" ]] || fail "Remote is unavailable and the existing checkout is not on $branch: $destination"
      as_owner git -C "$destination" rev-parse --verify HEAD >/dev/null
      log "Remote update unavailable; using the existing clean $branch checkout at $destination."
    fi
  elif [[ -e $destination ]]; then
    fail "Destination exists but is not a Git repository: $destination"
  else
    as_owner git clone --branch "$branch" --single-branch "$repository" "$destination"
  fi
}

deploy_dependencies() {
  local repository=$1
  "$repository/bootstrap/install-dependencies.sh"
  "$repository/bootstrap/install-workstation-update-systemd.sh"
}

read_env_value() {
  local key=$1 file=$2
  awk -v key="$key" 'index($0, key "=") == 1 { print substr($0, length(key) + 2); exit }' "$file"
}

write_secret_setting() {
  local key=$1 value=$2
  # Compose supports single-quoted literal values (no $ interpolation).
  [[ $value != *$'\n'* && $value != *$'\r'* && $value != *"'"* ]] || fail "Unsupported characters in $key; use a private Compose .env file."
  printf "%s='%s'\n" "$key" "$value"
}

write_runtime_environment() {
  local source_dir=$1 database_dir=$2
  local target="$source_dir/.env"
  if [[ -f $target ]]; then
    chmod 0600 "$target"
    log 'Using the existing Whale Deck .env without overwriting credentials.'
    return
  fi
  local database_password valkey_password agent_gid
  database_password=$(read_env_value WHALEDECK_POSTGRES_PASSWORD "$database_dir/.env")
  valkey_password=$(read_env_value WHALEDECK_VALKEY_PASSWORD "$database_dir/.env")
  agent_gid=$(getent group whaledeck | cut -d: -f3)
  : "${database_password:?database-platform Whale Deck PostgreSQL credential is missing}"
  : "${valkey_password:?database-platform Whale Deck Valkey credential is missing}"
  : "${agent_gid:?whaledeck group is missing}"
  : "${WHALEDECK_OIDC_INTERNAL_AUTHORITY:?WHALEDECK_OIDC_INTERNAL_AUTHORITY is required}"
  : "${WHALEDECK_OIDC_INTERNAL_CLIENT_ID:?WHALEDECK_OIDC_INTERNAL_CLIENT_ID is required}"
  : "${WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET:?WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET is required}"
  : "${WHALEDECK_OIDC_EXTERNAL_AUTHORITY:?WHALEDECK_OIDC_EXTERNAL_AUTHORITY is required}"
  : "${WHALEDECK_OIDC_EXTERNAL_CLIENT_ID:?WHALEDECK_OIDC_EXTERNAL_CLIENT_ID is required}"
  : "${WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET:?WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET is required}"
  : "${WHALEDECK_AUTHENTIK_API_TOKEN:?WHALEDECK_AUTHENTIK_API_TOKEN is required}"
  : "${WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID:?WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID is required}"

  local temporary
  temporary=$(mktemp "$source_dir/.env.install.XXXXXX")
  {
    printf 'WHALEDECK_GATEWAY_UPSTREAM_PORT=18080\nAUTHENTIK_UPSTREAM_PORT=18081\n'
    printf 'WHALEDECK_AGENT_GID=%s\n' "$agent_gid"
    printf 'WHALEDECK_RUNTIME_UID=%s\n' "${WHALEDECK_RUNTIME_UID:-1654}"
    printf 'WHALEDECK_DB_NAME=whaledeck\nWHALEDECK_DB_USER=whaledeck\nWHALEDECK_VALKEY_USER=whaledeck\n'
    write_secret_setting WHALEDECK_DB_PASSWORD "$database_password"
    write_secret_setting WHALEDECK_VALKEY_PASSWORD "$valkey_password"
    write_secret_setting WHALEDECK_OIDC_INTERNAL_AUTHORITY "$WHALEDECK_OIDC_INTERNAL_AUTHORITY"
    write_secret_setting WHALEDECK_OIDC_INTERNAL_CLIENT_ID "$WHALEDECK_OIDC_INTERNAL_CLIENT_ID"
    write_secret_setting WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET "$WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET"
    write_secret_setting WHALEDECK_OIDC_EXTERNAL_AUTHORITY "$WHALEDECK_OIDC_EXTERNAL_AUTHORITY"
    write_secret_setting WHALEDECK_OIDC_EXTERNAL_CLIENT_ID "$WHALEDECK_OIDC_EXTERNAL_CLIENT_ID"
    write_secret_setting WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET "$WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET"
    write_secret_setting WHALEDECK_AUTHENTIK_API_TOKEN "$WHALEDECK_AUTHENTIK_API_TOKEN"
    write_secret_setting WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID "$WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID"
    printf 'WHALEDECK_TITLE=Whale Deck\nWHALEDECK_BACKEND_VERSION=0.1.0\nWHALEDECK_AUTHENTIK_URL=http://192.168.22.19:8081\n'
  } > "$temporary"
  chown "$install_owner:$(id -gn "$install_owner")" "$temporary"
  chmod 0600 "$temporary"
  mv -T "$temporary" "$target"
}

log 'Stage 1/7: system preflight.'
for mount in /data /dataNvme; do
  [[ -d $mount ]] || fail "Required storage path is missing: $mount"
  used=$(df -P "$mount" | awk 'NR==2 {gsub(/%/, "", $5); print $5}')
  ((used < 85)) || fail "$mount usage is ${used}%; installation requires less than 85%."
done

log 'Stage 2/7: package-source checks and latency probes.'
install_base_tools
probe_url GitHub https://github.com/
probe_url Docker https://download.docker.com/
probe_url Microsoft https://packages.microsoft.com/
[[ -n $registry_mirror ]] && probe_url RegistryMirror "$registry_mirror"

log 'Stage 3/7: required CLI and runtime installation.'
ensure_toolchain

log 'Stage 4/7: optional Docker accelerator configuration.'
configure_registry_mirror
[[ $prepare_only == true ]] && { log 'Preparation completed.'; exit 0; }

install -d -o "$install_owner" -g "$(id -gn "$install_owner")" -m 0755 "$install_root"
database_dir="$install_root/database-platform"
source_dir="$install_root/whale-deck"

log 'Stage 5/7: database-platform repository checkout.'
clone_or_update "$database_repository" "$database_branch" "$database_dir"

log 'Stage 6/7: database-platform dependency deployment.'
deploy_dependencies "$database_dir"
[[ $dependencies_only == true ]] && { log 'All database-platform-* dependencies are healthy.'; exit 0; }

log 'Stage 7/7: Whale Deck source build and deployment.'
clone_or_update "$source_repository" "$source_branch" "$source_dir"
# Tags in the example are for development. A release must supply immutable images.
[[ -r $source_dir/.images.env ]] || fail 'Create Whale Deck .images.env from .images.env.example and pin all base-image digests before deployment.'
for key in DOTNET_SDK_IMAGE ASPNET_IMAGE DOTNET_RUNTIME_IMAGE NODE_IMAGE NGINX_IMAGE; do
  image_reference=$(read_env_value "$key" "$source_dir/.images.env")
  [[ $image_reference =~ @sha256:[a-f0-9]{64}$ ]] || fail "$key must use an immutable sha256 digest in .images.env."
done
# Validate prerequisites before installing or restarting host services.
if [[ ! -r $source_dir/.env ]]; then
  provisioned_environment="$source_dir/.authentik-provision.env"
  if [[ -z ${WHALEDECK_OIDC_INTERNAL_CLIENT_ID:-} || -z ${WHALEDECK_OIDC_EXTERNAL_CLIENT_ID:-} || -z ${WHALEDECK_AUTHENTIK_API_TOKEN:-} ]]; then
    "$source_dir/deploy/install/provision-authentik.sh" "$provisioned_environment"
    set -a
    # shellcheck disable=SC1090
    source "$provisioned_environment"
    set +a
  fi
  for key in WHALEDECK_OIDC_INTERNAL_AUTHORITY WHALEDECK_OIDC_INTERNAL_CLIENT_ID WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET WHALEDECK_OIDC_EXTERNAL_AUTHORITY WHALEDECK_OIDC_EXTERNAL_CLIENT_ID WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET WHALEDECK_AUTHENTIK_API_TOKEN WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID; do
    if [[ -z ${!key:-} && $non_interactive == false ]]; then
      read -r -s -p "$key: " "$key"
      printf '\n'
    fi
    [[ -n ${!key:-} ]] || fail "$key is required for the final stage. Use --dependencies-only to stop before Whale Deck deployment."
  done
fi
export WHALEDECK_BUILD_REVISION=$(git -c "safe.directory=$source_dir" -C "$source_dir" rev-parse HEAD)
"$source_dir/deploy/host/publish-and-install.sh"
write_runtime_environment "$source_dir" "$database_dir"
rm -f -- "${provisioned_environment:-}"
compose=(docker compose --project-directory "$source_dir" --env-file "$source_dir/.env" --env-file "$source_dir/.images.env" -f "$source_dir/compose.yml")
"${compose[@]}" --profile tools config --quiet
bash "$source_dir/deploy/build-images.sh"
"${compose[@]}" --profile tools run --rm --no-deps migrator
"${compose[@]}" up -d --wait --wait-timeout 180
log 'Whale Deck deployment completed. Host access remains HTTP/IP-only through the MaintenanceHost entry points.'
