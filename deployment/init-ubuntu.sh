#!/usr/bin/env bash
# One-time setup for Ubuntu 24.04 amd64. Run with sudo.
# Docker installation reference: https://docs.docker.com/engine/install/ubuntu/
set -Eeuo pipefail
trap 'printf "\nERROR: %s:%s (exit %s)\nCommand: %s\n" "${BASH_SOURCE[0]}" "$LINENO" "$?" "$BASH_COMMAND" >&2' ERR
PS4='+ ${BASH_SOURCE##*/}:${LINENO}: '
set -x

printf '\n[1/5] Checking root, Ubuntu 24.04, amd64 and systemd\n'
test "$EUID" -eq 0
source /etc/os-release
test "${ID:-}" = ubuntu
test "${VERSION_ID:-}" = 24.04
architecture=$(dpkg --print-architecture)
test "$architecture" = amd64
test -d /run/systemd/system

# APT must stop if resolving dependencies would remove existing packages.
# List unrelated services needing restart; only Docker is started explicitly below.
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=l

printf '\n[2/5] Installing basic dependencies\n'
apt-get update --error-on=any
apt-get install -y --no-remove --no-install-recommends ca-certificates curl python3 iproute2

printf '\n[3/5] Configuring the official Docker repository\n'
install -m 0755 -d /etc/apt/keyrings
curl --fail --show-error --location https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod 0644 /etc/apt/keyrings/docker.asc
cat > /etc/apt/sources.list.d/docker.sources <<'EOF'
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: noble
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
chmod 0644 /etc/apt/sources.list.d/docker.sources

printf '\n[4/5] Installing Docker Engine and Compose\n'
apt-get update --error-on=any
apt-get install -y --no-remove --no-install-recommends docker-ce docker-ce-cli containerd.io docker-compose-plugin

printf '\n[5/5] Starting Docker and checking dependencies\n'
systemctl enable --now docker
systemctl is-enabled docker
systemctl is-active docker
docker info
docker compose version
python3 --version
ip -V

set +x
printf '\nServer dependencies are ready. Configure DNS, firewall ports and GitHub Actions, then run the deployment workflow.\n'
