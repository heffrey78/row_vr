#!/usr/bin/env bash
# Follows the app's logs. This is the Linux replacement for Meta Quest Developer Hub's
# log viewer.
#
# Filters to our process where possible, because an unfiltered Quest logcat is a
# torrent of system chatter. Crash output from the Android runtime is kept regardless,
# since a native crash kills the pid before it can be attributed.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

serial="$(adb_device)"
adb -s "$serial" logcat -c 2>/dev/null || true

pid="$(adb -s "$serial" shell pidof -s "$APP_ID" 2>/dev/null | tr -d '\r' || true)"

if [[ -n "$pid" ]]; then
  info "Following $APP_ID (pid $pid) on $serial — Ctrl-C to stop"
  # --pid drops the system noise but would also hide a native crash, so the
  # crash-relevant tags are followed in parallel.
  exec adb -s "$serial" logcat --pid="$pid" \
    Unity:V CRASH:V DEBUG:V AndroidRuntime:E libc:F '*:S'
else
  warn "$APP_ID is not running; following by tag instead."
  info "Following Unity/crash tags on $serial — Ctrl-C to stop"
  exec adb -s "$serial" logcat Unity:V CRASH:V DEBUG:V AndroidRuntime:E libc:F '*:S'
fi
