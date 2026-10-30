#!/usr/bin/env bash
set -euo pipefail
# Ejecutar como root. El JSON se descarga desde Monitor > Respaldos de TEST.
config="${1:?Indicar el archivo keryx-backup-agent.json}"
test "$(id -u)" = 0 || { echo 'Ejecutar con sudo.'; exit 1; }
test -s "$config"
command -v mysqldump >/dev/null
command -v openssl >/dev/null
command -v python3 >/dev/null
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
install -d -m 700 /etc/keryx-backup /opt/medios/backups/agente
install -m 600 "$config" /etc/keryx-backup/agent.json
install -m 700 "$source_dir/keryx-backup-agent.py" /usr/local/sbin/keryx-backup-agent.py
cat >/etc/systemd/system/keryx-backup.service <<'UNIT'
[Unit]
Description=Respaldo productivo de Keryx al NAS
After=network-online.target mysql.service docker.service
Wants=network-online.target
[Service]
Type=oneshot
User=root
UMask=0077
ExecStart=/usr/bin/python3 /usr/local/sbin/keryx-backup-agent.py --once
TimeoutStartSec=50min
Nice=15
IOSchedulingClass=idle
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/medios/backups/agente
ReadOnlyPaths=/etc/keryx-backup /opt/medios
UNIT
cat >/etc/systemd/system/keryx-backup.timer <<'UNIT'
[Unit]
Description=Consultar la programación de respaldos de Keryx
[Timer]
OnBootSec=1min
OnUnitInactiveSec=1min
AccuracySec=15s
Persistent=true
[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now keryx-backup.timer
systemctl start keryx-backup.service
systemctl --no-pager status keryx-backup.timer
printf '\nAgente instalado. La hora y las solicitudes se administran desde Monitor en TEST.\n'
