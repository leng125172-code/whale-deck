#!/usr/bin/env bash
set -Eeuo pipefail

[[ $EUID -eq 0 ]] || { echo 'Run this installer with sudo.' >&2; exit 1; }
source_root=${1:-}
[[ -d "$source_root/agent" && -d "$source_root/maintenance" ]] || {
  echo 'Usage: sudo install.sh <publish-root-containing-agent-and-maintenance>' >&2
  exit 2
}
if [[ -f "$source_root/SHA256SUMS" ]]; then
  (
    cd "$source_root"
    sha256sum --check --strict SHA256SUMS
  )
fi

python3 "$(dirname "$0")/validate-runtime.py" "$source_root"
development_unpacker="$(dirname "$0")/whaledeck-development-unpack.py"
runner_artifact="$source_root/runner/umoci"
install_runner=no
if [[ -e $runner_artifact || -L $runner_artifact ]]; then
  python3 -I "$development_unpacker" verify-artifact "$runner_artifact"
  install_runner=yes
fi

# The full host installation shares the maintenance lock. A publish inherits
# fd9; a direct install acquires it before changing accounts or fixed files.
control_recovery="$(dirname "$0")/whaledeck-platform-control-recovery.py"
if [[ -e /proc/$$/fd/9 ]]; then
  python3 -I "$control_recovery" verify-lock
else
  python3 -I "$control_recovery" init-lock
  exec 9<>/run/lock/whaledeck-platform-maintenance.lock
  flock -n 9 || { echo 'Another platform maintenance operation is active.' >&2; exit 1; }
  python3 -I "$control_recovery" verify-lock
fi

getent group whaledeck >/dev/null || groupadd --system whaledeck
id whaledeck-agent >/dev/null 2>&1 || useradd --system --gid whaledeck --groups docker,systemd-journal --home-dir /var/lib/whaledeck-agent --shell /usr/sbin/nologin whaledeck-agent
id whaledeck-maintenance >/dev/null 2>&1 || useradd --system --gid whaledeck --home-dir /nonexistent --shell /usr/sbin/nologin whaledeck-maintenance
id whaledeck-shell >/dev/null 2>&1 || useradd --system --user-group --home-dir /var/lib/whaledeck-shell --shell /usr/sbin/nologin whaledeck-shell
[[ ! -L /var/lib/whaledeck-agent && ! -L /var/lib/whaledeck-agent/platform-maintenance ]] || exit 1
# Match StateDirectory ownership before its first service start, so systemd does
# not recursively chown the root-private controller checkpoint on first boot.
install -d -o whaledeck-agent -g whaledeck -m 0750 /var/lib/whaledeck-agent
install -d -o root -g root -m 0700 /var/lib/whaledeck-agent/platform-maintenance
python3 -I "$(dirname "$0")/whaledeck-platform-control-recovery.py" install "$(dirname "$0")"
systemd-tmpfiles --create /etc/tmpfiles.d/whaledeck-platform-control.conf
# Never reuse the Docker-capable Agent identity or the temporary sudo user for
# a general host shell. Existing misconfigured identities fail closed.
[[ $(id -u whaledeck-shell) -gt 0 && $(id -nG whaledeck-shell) == whaledeck-shell ]] || { echo 'Unsafe terminal service identity.' >&2; exit 1; }
[[ $(getent passwd whaledeck-shell | cut -d: -f6) == /var/lib/whaledeck-shell ]] || exit 1
[[ ! -L /var/lib/whaledeck-shell && ! -L /var/lib/whaledeck-terminal ]] || exit 1
install -d -o whaledeck-shell -g whaledeck-shell -m 0700 /var/lib/whaledeck-shell
install -d -o root -g root -m 0700 /var/lib/whaledeck-terminal

install -d -o root -g whaledeck -m 0750 /etc/whaledeck
# Attest the HDD before creating data roots or replacing any installed binary.
# A missing/wrong mount must not leave files on the OS-disk fallback path.
python3 "$(dirname "$0")/configure-file-backups.py"
[[ ! -L /etc/whaledeck/notifications ]] || { echo 'Notification private directory must not be a symlink.' >&2; exit 1; }
install -d -o root -g whaledeck -m 0750 /etc/whaledeck/notifications
install -d -o whaledeck-agent -g whaledeck -m 0750 /data/WhaleDeck/apps
for files_root in /data/WhaleDeck/files /data/WhaleDeck/sites /data/WhaleDeck/workspaces /var/lib/whaledeck-files; do
  [[ ! -L $files_root ]] || { echo 'File transport roots must not be symlinks.' >&2; exit 1; }
  if [[ ! -e $files_root ]]; then install -d -o root -g root -m 0755 "$files_root"; fi
done
[[ $(stat -c %u /var/lib/whaledeck-files) == 0 ]] || exit 1
chmod 0700 /var/lib/whaledeck-files
[[ ! -L /etc/whaledeck/file-roots.json ]] || exit 1
if [[ ! -e /etc/whaledeck/file-roots.json ]]; then
  install -o root -g whaledeck -m 0640 "$(dirname "$0")/file-roots.json" /etc/whaledeck/file-roots.json
fi
[[ $(stat -c %u /etc/whaledeck/file-roots.json) == 0 ]] || exit 1
[[ $(stat -c %a /etc/whaledeck/file-roots.json) =~ ^(600|640|644)$ ]] || exit 1
python3 "$(dirname "$0")/upgrade-file-roots.py"
install -d -o root -g root -m 0755 /opt/whaledeck-agent /opt/whaledeck-maintenance /usr/local/libexec
# GNU cp otherwise truncates an existing executable in place and fails with
# ETXTBSY while the old service process is still running. Replacing the
# directory entry creates a new inode; the running process keeps its old one
# until the controlled restart below.
cp -a --remove-destination "$source_root/agent/." /opt/whaledeck-agent/
cp -a --remove-destination "$source_root/maintenance/." /opt/whaledeck-maintenance/
chown -R root:root /opt/whaledeck-agent /opt/whaledeck-maintenance
chmod 0755 /opt/whaledeck-agent/WhaleDeck.Agent /opt/whaledeck-maintenance/WhaleDeck.MaintenanceHost

if [[ ! -f /etc/whaledeck/agent-capability.key ]]; then
  umask 0077
  openssl rand 32 > /etc/whaledeck/agent-capability.key
fi
chown root:whaledeck /etc/whaledeck/agent-capability.key
chmod 0640 /etc/whaledeck/agent-capability.key
install -o root -g whaledeck -m 0640 "$(dirname "$0")/resources.json" /etc/whaledeck/resources.json
printf 'Agent__AllowedPeerGid=%s\n' "$(getent group whaledeck | cut -d: -f3)" > /etc/whaledeck/agent.env
chown root:whaledeck /etc/whaledeck/agent.env
chmod 0640 /etc/whaledeck/agent.env
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-privileged" /usr/local/libexec/whaledeck-privileged
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-application-backup.py" /usr/local/libexec/whaledeck-application-backup.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-notification-config.py" /usr/local/libexec/whaledeck-notification-config.py
install -o root -g root -m 0755 "$(dirname "$0")/whaledeck-host-terminal.py" /usr/local/libexec/whaledeck-host-terminal.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-terminal.py" /usr/local/libexec/whaledeck-container-terminal.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-files.py" /usr/local/libexec/whaledeck-container-files.py
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-files
[[ ! -L /var/lib/whaledeck-container-directories ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-directories
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-directories.py" /usr/local/libexec/whaledeck-container-directories.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-directory-transport.py" /usr/local/libexec/whaledeck-container-directory-transport.py
[[ ! -L /var/lib/whaledeck-container-permissions ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-permissions
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-permissions.py" /usr/local/libexec/whaledeck-container-permissions.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-permission-transport.py" /usr/local/libexec/whaledeck-container-permission-transport.py
[[ ! -L /var/lib/whaledeck-container-recycling ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recycling
for recycle_restore_state in /var/lib/whaledeck-container-recycle-restores /var/lib/whaledeck-container-recycle-restores/claims; do
  [[ ! -L $recycle_restore_state ]] || exit 1
  install -d -o root -g root -m 0700 "$recycle_restore_state"
done
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycling.py" /usr/local/libexec/whaledeck-container-recycling.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-restores.py" /usr/local/libexec/whaledeck-container-recycle-restores.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-transport.py" /usr/local/libexec/whaledeck-container-recycle-transport.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-restore-transport.py" /usr/local/libexec/whaledeck-container-recycle-restore-transport.py
[[ ! -L /var/lib/whaledeck-container-recycle-purges ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recycle-purges
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-purge-plan.py" /usr/local/libexec/whaledeck-container-recycle-purge-plan.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-purge-fences.py" /usr/local/libexec/whaledeck-container-recycle-purge-fences.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-purges.py" /usr/local/libexec/whaledeck-container-recycle-purges.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-purge-transport.py" /usr/local/libexec/whaledeck-container-recycle-purge-transport.py
[[ ! -L /var/lib/whaledeck-container-recycle-archives ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recycle-archives
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-archive-plan.py" /usr/local/libexec/whaledeck-container-recycle-archive-plan.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-archive-fences.py" /usr/local/libexec/whaledeck-container-recycle-archive-fences.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-archive-copies.py" /usr/local/libexec/whaledeck-container-recycle-archive-copies.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recycle-archive-transport.py" /usr/local/libexec/whaledeck-container-recycle-archive-transport.py
# Provision only the independent retirement registry. Never create/remove HDD
# history or alter original journals/claims as part of enabling the code files.
for retirement_state in /var/lib/whaledeck-container-recycle-archive-retirements /var/lib/whaledeck-container-recycle-archive-retirements/claims; do
  [[ ! -L $retirement_state ]] || exit 1
  if [[ -e $retirement_state ]]; then
    [[ -d $retirement_state && $(realpath -e -- "$retirement_state") == "$retirement_state" && $(stat -c '%a:%u:%g' "$retirement_state") == '700:0:0' ]] || exit 1
    for retirement_original in /var/lib/whaledeck-container-recycling /var/lib/whaledeck-container-recycle-restores /var/lib/whaledeck-container-recycle-restores/claims /var/lib/whaledeck-container-recycle-purges /var/lib/whaledeck-container-recycle-archives; do
      [[ ! $retirement_state -ef $retirement_original ]] || exit 1
    done
  fi
  install -d -o root -g root -m 0700 "$retirement_state"
done
[[ ! /var/lib/whaledeck-container-recycle-archive-retirements -ef /var/lib/whaledeck-container-recycle-archive-retirements/claims ]] || exit 1
for retirement_module in whaledeck-container-recycle-archive-retention.py whaledeck-container-recycle-archive-retirement-fences.py whaledeck-container-recycle-archive-retirements.py whaledeck-container-recycle-archive-retirement-transport.py whaledeck-container-recycle-retired-history.py whaledeck-container-recycle-archive-retirement-stop.py; do
  retirement_path="/usr/local/libexec/$retirement_module"
  [[ ! -L $retirement_path ]] || exit 1
  if [[ -e $retirement_path ]]; then
    [[ -f $retirement_path && $(stat -c '%a:%u:%g:%h' "$retirement_path") == '750:0:0:1' ]] || exit 1
  fi
  install -T -o root -g root -m 0750 "$(dirname "$0")/$retirement_module" "$retirement_path"
done
# End independent retirement provisioning.
[[ ! -L /var/lib/whaledeck-container-recursive-permissions ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recursive-permissions
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-permissions.py" /usr/local/libexec/whaledeck-container-recursive-permissions.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-permission-transport.py" /usr/local/libexec/whaledeck-container-recursive-permission-transport.py
[[ ! -L /var/lib/whaledeck-container-recursive-ownership ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recursive-ownership
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-ownership.py" /usr/local/libexec/whaledeck-container-recursive-ownership.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-ownership-transport.py" /usr/local/libexec/whaledeck-container-recursive-ownership-transport.py
[[ ! -L /var/lib/whaledeck-container-recursive-acl ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recursive-acl
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl.py" /usr/local/libexec/whaledeck-container-recursive-acl.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl-transport.py" /usr/local/libexec/whaledeck-container-recursive-acl-transport.py
[[ ! -L /var/lib/whaledeck-container-recursive-acl-restore ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-recursive-acl-restore
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl-restore.py" /usr/local/libexec/whaledeck-container-recursive-acl-restore.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl-restore-transport.py" /usr/local/libexec/whaledeck-container-recursive-acl-restore-transport.py
[[ ! -L /var/lib/whaledeck-container-acl ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-acl
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-acl.py" /usr/local/libexec/whaledeck-container-acl.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-acl-history.py" /usr/local/libexec/whaledeck-container-acl-history.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-acl-history-transport.py" /usr/local/libexec/whaledeck-container-acl-history-transport.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl-history.py" /usr/local/libexec/whaledeck-container-recursive-acl-history.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-recursive-acl-history-transport.py" /usr/local/libexec/whaledeck-container-recursive-acl-history-transport.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-acl-transport.py" /usr/local/libexec/whaledeck-container-acl-transport.py
[[ ! -L /var/lib/whaledeck-container-ownership ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-ownership
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-ownership.py" /usr/local/libexec/whaledeck-container-ownership.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-ownership-transport.py" /usr/local/libexec/whaledeck-container-ownership-transport.py
[[ ! -L /var/lib/whaledeck-container-copies ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-copies
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-copy-scope.py" /usr/local/libexec/whaledeck-container-copy-scope.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-copies.py" /usr/local/libexec/whaledeck-container-copies.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-copy-transport.py" /usr/local/libexec/whaledeck-container-copy-transport.py
[[ ! -L /var/lib/whaledeck-container-moves ]] || exit 1
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-moves
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-move-scope.py" /usr/local/libexec/whaledeck-container-move-scope.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-moves.py" /usr/local/libexec/whaledeck-container-moves.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-move-transport.py" /usr/local/libexec/whaledeck-container-move-transport.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-file-write.py" /usr/local/libexec/whaledeck-container-file-write.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-upload.py" /usr/local/libexec/whaledeck-container-upload.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-backups.py" /usr/local/libexec/whaledeck-container-backups.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-publish.py" /usr/local/libexec/whaledeck-container-publish.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-history.py" /usr/local/libexec/whaledeck-container-history.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-restore.py" /usr/local/libexec/whaledeck-container-restore.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-container-history-transport.py" /usr/local/libexec/whaledeck-container-history-transport.py
install -d -o root -g root -m 0700 /var/lib/whaledeck-container-uploads
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-files.py" /usr/local/libexec/whaledeck-files.py
development_base=/var/lib/whaledeck-development
[[ ! -L "$development_base" ]] || exit 1
if [[ ! -e "$development_base" ]]; then install -d -o root -g root -m 0700 "$development_base"; fi
[[ -d "$development_base" && $(stat -c '%a:%U:%G' -- "$development_base") == '700:root:root' ]] || exit 1
for development_module in whaledeck-development-storage.py whaledeck-development-storage-transport.py whaledeck-development-project-quota.py whaledeck-development-quota-guard.py whaledeck-development-runner-isolation.py whaledeck-development-rootfs.py whaledeck-development-unpack.py whaledeck-development-runner.py whaledeck-development-runner-control.py whaledeck-development-source.py; do
  install -o root -g root -m 0750 "$(dirname "$0")/$development_module" "/usr/local/libexec/$development_module"
done
if [[ $install_runner == yes ]]; then
  python3 -I "$development_unpacker" install-artifact "$runner_artifact"
fi
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-file-operations.py" /usr/local/libexec/whaledeck-file-operations.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-file-backups.py" /usr/local/libexec/whaledeck-file-backups.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-file-archives.py" /usr/local/libexec/whaledeck-file-archives.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-file-tree.py" /usr/local/libexec/whaledeck-file-tree.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-copies.py" /usr/local/libexec/whaledeck-tree-copies.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-moves.py" /usr/local/libexec/whaledeck-tree-moves.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-archive.py" /usr/local/libexec/whaledeck-tree-archive.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-packing.py" /usr/local/libexec/whaledeck-tree-packing.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-unpacking.py" /usr/local/libexec/whaledeck-tree-unpacking.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-backups.py" /usr/local/libexec/whaledeck-tree-backups.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-recycling.py" /usr/local/libexec/whaledeck-tree-recycling.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-restoring.py" /usr/local/libexec/whaledeck-tree-restoring.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-overwrite-restoring.py" /usr/local/libexec/whaledeck-tree-overwrite-restoring.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-tree-purging.py" /usr/local/libexec/whaledeck-tree-purging.py
install -d -o root -g root -m 0700 /var/lib/whaledeck-application-backup /data/DockerData/whaledeck/application-backups
for unit in whaledeck-application-backup-recovery.service whaledeck-application-backup-recovery.timer; do
  install -o root -g root -m 0644 "$(dirname "$0")/$unit" "/etc/systemd/system/$unit"
done
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-platform-revisions.py" /usr/local/libexec/whaledeck-platform-revisions.py
install -o root -g root -m 0750 "$(dirname "$0")/whaledeck-platform-maintenance" /usr/local/libexec/whaledeck-platform-maintenance
printf '%s\n' 'whaledeck-agent ALL=(root) NOPASSWD: /usr/local/libexec/whaledeck-privileged *' > /etc/sudoers.d/whaledeck-agent
chmod 0440 /etc/sudoers.d/whaledeck-agent
visudo -cf /etc/sudoers.d/whaledeck-agent

install -o root -g root -m 0644 "$(dirname "$0")/whaledeck-agent.service" /etc/systemd/system/whaledeck-agent.service
install -o root -g root -m 0644 "$(dirname "$0")/whaledeck-maintenance.service" /etc/systemd/system/whaledeck-maintenance.service
systemctl daemon-reload
systemctl enable whaledeck-agent.service whaledeck-maintenance.service
systemctl enable whaledeck-platform-control-recovery.service
systemctl enable --now whaledeck-platform-control-recovery.timer
systemctl enable --now whaledeck-application-backup-recovery.timer
# `enable --now` does not restart an already active service after an upgrade.
# Explicit restart guarantees the copied binaries and settings become active.
systemctl restart whaledeck-agent.service whaledeck-maintenance.service
systemctl --no-pager --full status whaledeck-agent.service whaledeck-maintenance.service
echo "Whale Deck group id: $(getent group whaledeck | cut -d: -f3)"
