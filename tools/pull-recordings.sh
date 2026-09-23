#!/usr/bin/env bash
# Pulls hand-motion clips recorded on the headset by HandRecorder.
#
# This is the bridge that makes the fast loop possible: record a real stroke once
# with the headset on, then iterate on stroke detection against it forever with the
# headset on the shelf.
#
#   make pull-recordings
#   make promote CLIP=recordings/raw/stroke-20260916-142211.rowclip
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

serial="$(adb_device)"
remote="/sdcard/Android/data/$APP_ID/files/recordings"
local_dir="$REPO_ROOT/recordings/raw"
mkdir -p "$local_dir"

if ! adb -s "$serial" shell "test -d '$remote'" 2>/dev/null; then
  die "No recordings directory on the headset ($remote).
     Record a clip first: with the app running on the headset,
       make record NAME=steady-22spm   then row, then   make record-stop"
fi

count="$(adb -s "$serial" shell "ls -1 '$remote' 2>/dev/null | wc -l" | tr -d '\r')"
if [[ "$count" == "0" ]]; then
  warn "No clips on the headset yet."
  exit 0
fi

info "Pulling $count clip(s) from $serial"
adb -s "$serial" pull "$remote/." "$local_dir" >/dev/null
ok "Pulled to recordings/raw/"
ls -1t "$local_dir" | head -10 | sed 's/^/     /'
echo
dim "These are gitignored. Promote one to a committed test fixture with:"
dim "  make promote CLIP=recordings/raw/<name>.rowclip"
