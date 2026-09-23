#!/usr/bin/env bash
# Starts and stops a HandRecorder take on the headset, from the laptop.
#
# The app is hands-only, so there is no button to press in the headset. Instead the
# recorder watches for a trigger file in its data directory: this script writes it to
# start a take and deletes it to stop. The file's contents become the clip's note, so
# every take is labelled at the moment it is recorded - including the headset firmware,
# which ADR-0005 wants alongside every real-hardware fixture.
#
#   make record NAME=steady-22spm     # start; row
#   make record-stop                  # stop; the clip is written on the headset
#   make pull-recordings              # bring clips back to recordings/raw/
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

action="${1:-}"
serial="$(adb_device)"
trigger="/sdcard/Android/data/$APP_ID/files/record.cmd"

# Latest [RowVR] line matching a pattern, so the script can tell the app reacted rather
# than assuming it did.
last_line() {
  # `|| true`: no match yet is the normal case before the first take, and under
  # set -e + pipefail a failed grep would end the script without a word.
  { adb -s "$serial" logcat -d -s Unity 2>/dev/null | grep -F "$1" | tail -1 | tr -d '\r'; } || true
}

# Waits up to ~6s for a line matching $1 that differs from $2 (the line before we acted).
await_line() {
  local pattern="$1" before="$2" now i
  for i in $(seq 1 24); do
    now="$(last_line "$pattern")"
    if [[ -n "$now" && "$now" != "$before" ]]; then
      printf '%s\n' "$now"
      return 0
    fi
    sleep 0.25
  done
  return 1
}

case "$action" in
  start)
    name="${NAME:-}"
    [[ -n "$name" ]] || die "Set NAME to say what is being rowed, e.g. make record NAME=steady-22spm"

    adb -s "$serial" shell pidof "$APP_ID" >/dev/null 2>&1 \
      || die "$APP_ID is not running. Put the headset on and launch it first."

    firmware="$(adb -s "$serial" shell getprop ro.build.display.id | tr -d '\r')"
    note="$name | firmware $firmware"
    before="$(last_line '[RowVR] Recording started')"

    # Single-quoted on the device side; take names are simple words, so reject anything
    # that would need escaping rather than trying to escape it.
    [[ "$name" =~ ^[A-Za-z0-9._-]+$ ]] || die "NAME may only contain letters, digits, '.', '_' and '-'."
    adb -s "$serial" shell "echo '$note' > '$trigger'"

    if line="$(await_line '[RowVR] Recording started' "$before")"; then
      ok "Recording: $name"
      dim "  ${line#*\[RowVR\] }"
      dim "  Row, then: make record-stop"
    else
      warn "Trigger written, but the app did not confirm within 6s.
     Is the app in focus with the headset on? The recorder only polls while the app runs."
      exit 1
    fi
    ;;

  stop)
    before="$(last_line '[RowVR] Wrote')"
    before_empty="$(last_line 'Recording stopped with no samples')"
    adb -s "$serial" shell "rm -f '$trigger'"

    if line="$(await_line '[RowVR] Wrote' "$before")"; then
      ok "${line#*\[RowVR\] }"
      dim "  Fetch it with: make pull-recordings"
    elif line="$(await_line 'Recording stopped with no samples' "$before_empty")"; then
      fail "Take ended with no samples - the hands were never tracked while recording."
      exit 1
    elif { adb -s "$serial" logcat -d -s Unity 2>/dev/null \
             | grep -E '\[RowVR\] (Recording started|Reached the .* limit)' | tail -1 \
             | grep -q 'limit'; } 2>/dev/null; then
      # The take hit the recorder's duration cap and was already written; removing the
      # trigger just re-arms the next 'make record'.
      ok "The take had already stopped at the duration cap and been written:"
      dim "  ${before#*\[RowVR\] }"
    else
      warn "Trigger removed, but the app did not confirm a clip within 6s. Check 'make logs'."
      exit 1
    fi
    ;;

  *)
    die "Usage: tools/record.sh start|stop   (or: make record NAME=... / make record-stop)"
    ;;
esac
