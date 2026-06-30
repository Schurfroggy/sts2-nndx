#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

export_sheet() {
  local video="$1"
  local sheet="$2"
  local tile="$3"

  ffmpeg -y -i "$video" \
    -vf "fps=24,scale=270:-1,format=rgba,tile=${tile}" \
    -frames:v 1 \
    -update 1 \
    "$sheet"

  echo "Exported RGBA sprite sheet to $sheet"
}

export_sheet "$ROOT/res/nndx_alpha.mp4" "$ROOT/res/nndx_sheet.png" "19x25"
