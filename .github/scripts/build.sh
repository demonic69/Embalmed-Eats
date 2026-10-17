#!/usr/bin/env bash
# Compiles the mod against the downloaded game DLLs and packages the mod zip into ./out.
set -uo pipefail
tfm="${1:-net8.0}"
mkdir -p ci out

proj=src/EmbalmedEats/EmbalmedEats.csproj
if [ ! -f "$proj" ]; then
  echo "SKIPPED: no project yet" | tee ci/build.log
  exit 0
fi

dotnet build "$proj" -c Release -p:VintageStoryPath="$PWD/vs" -p:TargetFramework="$tfm" -o build-out 2>&1 | tee ci/build.log
status=${PIPESTATUS[0]}
grep -E "warning|error" ci/build.log | grep -v "^\s*$" | sort -u > ci/build-warnings-errors.txt || true
if [ "$status" -ne 0 ]; then
  echo "BUILD FAILED" >&2
  exit "$status"
fi

version=$(python3 -c "import json;print(json.load(open('modinfo.json'))['version'])")
stage=$(mktemp -d)
cp modinfo.json "$stage/"
[ -f modicon.png ] && cp modicon.png "$stage/"
cp -r assets "$stage/"
cp build-out/EmbalmedEats.dll "$stage/"
[ -f build-out/EmbalmedEats.pdb ] && cp build-out/EmbalmedEats.pdb "$stage/"
(cd "$stage" && zip -qr "$OLDPWD/out/EmbalmedEats_v${version}.zip" .)
unzip -l "out/EmbalmedEats_v${version}.zip" > ci/zip-contents.txt
echo "Packaged out/EmbalmedEats_v${version}.zip"
