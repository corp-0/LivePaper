#!/usr/bin/env bash
set -euo pipefail

install_root=${LIVEPAPER_INSTALL_ROOT:-"$HOME/.local/lib/livepaper"}
bin_path=${LIVEPAPER_BIN_DIR:-"$HOME/.local/bin"}
config_root=${XDG_CONFIG_HOME:-"$HOME/.config"}
unit_path="$config_root/systemd/user/livepaper.service"

systemctl --user disable --now livepaper.service 2>/dev/null || true
rm -f -- "$unit_path" "$bin_path/livepaper"
rm -rf -- "$install_root"
systemctl --user daemon-reload

echo "Removed LivePaper binaries and user service."
echo "Preserved config: $config_root/livepaper"
echo "Preserved wallpapers: ${XDG_DATA_HOME:-"$HOME/.local/share"}/livepaper/wallpapers"
