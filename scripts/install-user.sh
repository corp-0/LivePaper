#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
install_root=${LIVEPAPER_INSTALL_ROOT:-"$HOME/.local/lib/livepaper"}
bin_path=${LIVEPAPER_BIN_DIR:-"$HOME/.local/bin"}
config_root=${XDG_CONFIG_HOME:-"$HOME/.config"}
config_dir="$config_root/livepaper"
unit_dir="$config_root/systemd/user"
release_id=$(date -u +%Y%m%d%H%M%S)
release_dir="$install_root/releases/$release_id"
staging_dir=$(mktemp -d /tmp/livepaper-install.XXXXXX)

cleanup() {
    rm -rf -- "$staging_dir"
}
trap cleanup EXIT

dotnet publish "$repo_root/src/LivePaper.Daemon/LivePaper.Daemon.csproj" \
    -c Release -r linux-x64 --self-contained true -m:1 \
    -o "$staging_dir/publish"
dotnet publish "$repo_root/src/LivePaper.Renderer/LivePaper.Renderer.csproj" \
    -c Release -r linux-x64 --self-contained true -m:1 \
    -o "$staging_dir/publish"

install -d -- "$release_dir" "$bin_path" "$config_dir" "$unit_dir"
cp -a -- "$staging_dir/publish/." "$release_dir/"
ln -sfn -- "$release_dir" "$install_root/current.new"
mv -Tf -- "$install_root/current.new" "$install_root/current"

install -m 0755 -- "$repo_root/scripts/livepaper" "$bin_path/livepaper"
if [[ ! -e "$config_dir/livepaper.toml" ]]; then
    install -m 0644 -- "$repo_root/config/livepaper.toml" "$config_dir/livepaper.toml"
    echo "Installed default config: $config_dir/livepaper.toml"
else
    echo "Kept existing config: $config_dir/livepaper.toml"
fi

install -m 0644 -- "$repo_root/packaging/livepaper.service" "$unit_dir/livepaper.service"
systemctl --user daemon-reload
systemctl --user enable livepaper.service
systemctl --user restart livepaper.service

echo "Installed LivePaper release: $release_dir"
echo "Launcher: $bin_path/livepaper"
echo "Service: systemctl --user status livepaper.service"
