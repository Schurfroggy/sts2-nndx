#!/usr/bin/env bash
# Copy to: SlayTheSpire2.app/Contents/MacOS/launch_mp_test.sh
# Usage: ./launch_mp_test.sh [player_count]
set -euo pipefail

MACOS_DIR="$(cd "$(dirname "$0")" && pwd)"
STS2="$MACOS_DIR/Slay the Spire 2"
PLAYERS="${1:-2}"

if [[ ! -x "$STS2" ]]; then
  echo "Executable not found: $STS2" >&2
  echo "Expected Mac binary name: Slay the Spire 2 (with spaces)" >&2
  exit 1
fi

echo -n 2868840 > "$MACOS_DIR/steam_appid.txt"

"$STS2" --fastmp host_standard &
sleep 3

for ((i=1; i<PLAYERS; i++)); do
  cid=$((1000 + i))
  "$STS2" --fastmp join --clientId "$cid" &
  sleep 2
done

wait
