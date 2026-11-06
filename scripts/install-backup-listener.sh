#!/usr/bin/env bash
set -euo pipefail
# Instala/actualiza solo la escucha HTTPS. Conserva timer y credenciales existentes.
test "$(id -u)" = 0 || { echo 'Ejecutar con sudo.'; exit 1; }
test -s /etc/keryx-backup/agent.json
command -v curl >/dev/null
command -v python3 >/dev/null
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
python3 -m py_compile "$source_dir/keryx-backup-agent.py"
install -d -m 700 /opt/medios/backups/agente
if test -s /usr/local/sbin/keryx-backup-agent.py; then
  cp -p /usr/local/sbin/keryx-backup-agent.py "/opt/medios/backups/agente/antes_escucha_$(date +%Y%m%d_%H%M%S).py"
fi
install -m 700 "$source_dir/keryx-backup-agent.py" /usr/local/sbin/keryx-backup-agent.py
cat >/etc/systemd/system/keryx-backup-listener.service <<'UNIT'
[Unit]
Description=Escucha HTTPS de solicitudes inmediatas de Keryx
After=network-online.target mysql.service docker.service
Wants=network-online.target
[Service]
Type=simple
User=root
UMask=0077
ExecStart=/usr/bin/python3 /usr/local/sbin/keryx-backup-agent.py --watch
Restart=on-failure
RestartSec=30s
Nice=15
IOSchedulingClass=idle
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/medios/backups/agente
ReadOnlyPaths=/etc/keryx-backup /opt/medios
[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable keryx-backup-listener.service
systemctl restart keryx-backup-listener.service
systemctl is-active --quiet keryx-backup-listener.service
printf 'Escucha inmediata instalada. Se mantiene el temporizador existente.\n'
