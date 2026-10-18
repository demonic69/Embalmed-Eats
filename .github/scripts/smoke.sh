#!/usr/bin/env bash
# Boots a real Vintage Story dedicated server with the mod installed, runs the
# mod's built-in self test from the server console, then shuts the server down.
# Passes only if the server starts, the self test passes and no exception
# mentions the mod.
set -uo pipefail

zip=$(ls out/*.zip 2>/dev/null | head -1)
if [ -z "$zip" ]; then
  echo "SKIPPED: no mod zip" | tee ci/smoke-summary.txt
  exit 0
fi

ROOT=$PWD
DATA=$ROOT/vsdata
CMDS=$ROOT/server-commands.txt
LOG=$ROOT/ci/server-stdout.log
mkdir -p "$DATA/Mods" ci/server-logs
cp "$zip" "$DATA/Mods/"
: > "$CMDS"

cd vs
( tail -f "$CMDS" | dotnet VintagestoryServer.dll --dataPath "$DATA" > "$LOG" 2>&1 ) &
cd "$ROOT"

wait_for() { # pattern timeout_seconds
  local pattern=$1 t=$2 i=0
  while [ $i -lt "$t" ]; do
    if grep -qE "$pattern" "$LOG" "$DATA"/Logs/server-main.* 2>/dev/null; then return 0; fi
    if ! pgrep -f VintagestoryServer.dll > /dev/null; then return 2; fi
    sleep 5; i=$((i+5))
  done
  return 1
}

started=no
if wait_for "Dedicated Server now running|Server now running|now running on Port" 1200; then
  started=yes
  sleep 10
  echo "/embalmedeats selftest" >> "$CMDS"
  wait_for "SELFTEST (PASS|FAIL)" 180
  sleep 3
  echo "/embalmedeats help" >> "$CMDS"
  sleep 4
  echo "/embalmedeats" >> "$CMDS"
  sleep 4
fi

echo "/stop" >> "$CMDS"
for _ in $(seq 1 30); do pgrep -f VintagestoryServer.dll > /dev/null || break; sleep 4; done
pkill -f VintagestoryServer.dll 2>/dev/null
pkill -f "tail -f $CMDS" 2>/dev/null

cp -r "$DATA"/Logs/* ci/server-logs/ 2>/dev/null

{
  echo "server started: $started"
  echo "--- self test ---"
  grep -hE "SELFTEST" "$LOG" ci/server-logs/* 2>/dev/null | sort -u
  echo "--- help output ---"
  grep -hE -A9 "Embalmed Eats commands:" "$LOG" 2>/dev/null | head -40
  echo "help shown: $(grep -c "Embalmed Eats commands:" "$LOG" 2>/dev/null) time(s) (expected 2: /embalmedeats help and /embalmedeats)"
  echo "--- mod lines ---"
  grep -hiE "embalmed" ci/server-logs/server-main* ci/server-logs/server-debug* "$LOG" 2>/dev/null | head -300
  echo "--- errors / exceptions (all) ---"
  grep -hE "\[Error\]|\[Fatal\]|Exception" ci/server-logs/server-main* "$LOG" 2>/dev/null | sort -u | head -200
} > ci/smoke-summary.txt
cat ci/smoke-summary.txt

[ "$started" = yes ] || { echo "Server did not start"; exit 1; }
grep -qE "SELFTEST PASS" ci/smoke-summary.txt || { echo "Self test did not pass"; exit 1; }
[ "$(grep -c "Embalmed Eats commands:" "$LOG")" -ge 1 ] || { echo "/embalmedeats help printed nothing"; exit 1; }
if grep -hE "Exception" ci/server-logs/* "$LOG" 2>/dev/null | grep -qi "embalmed"; then
  echo "Exception mentioning the mod"; exit 1
fi
echo "SMOKE TEST PASSED"
