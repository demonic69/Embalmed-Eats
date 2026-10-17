#!/usr/bin/env bash
# Downloads and extracts the Vintage Story dedicated server into ./vs (skipped when cached).
set -euo pipefail

if [ -f vs/VintagestoryServer.dll ]; then
  echo "Server already present (cache hit)"
  exit 0
fi

mkdir -p vs
urls=(
  "https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_${VS_VERSION}.tar.gz"
  "https://cdn.vintagestory.at/gamefiles/stable/vs_server_${VS_VERSION}.tar.gz"
)
for u in "${urls[@]}"; do
  echo "Trying $u"
  if curl -fsSL --retry 3 -o vs.tar.gz "$u"; then
    tar -xzf vs.tar.gz -C vs
    rm vs.tar.gz
    echo "Extracted server from $u"
    ls vs | head -50
    exit 0
  fi
done
echo "Could not download Vintage Story server ${VS_VERSION}" >&2
exit 1
