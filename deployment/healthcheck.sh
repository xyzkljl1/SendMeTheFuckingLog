#!/usr/bin/env bash
set -euo pipefail
port="${Tls__HttpPort:-8080}"
exec 3<>/dev/tcp/127.0.0.1/"$port"
printf 'GET /healthz HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3
IFS= read -r status <&3
[[ "$status" == *" 200 "* ]]
