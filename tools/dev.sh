#!/usr/bin/env bash
# Tier 3 in one command: build, install, launch, follow logs.
# 2-8 minutes per pass. Use it for tracking fidelity, performance and comfort —
# not for iterating on stroke logic, which belongs in tier 1 ('make test').
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

# Fail fast on a missing headset rather than after a multi-minute IL2CPP build.
serial="$(adb_device)"
dim "target: $serial"

"$REPO_ROOT/tools/build.sh"

# Unity kills the adb server when a batch-mode build exits ("Killing ADB server" at the end
# of .devloop/build.log). A USB device reappears when the next adb command starts a new
# server, but a wireless connection only ever lived in the old server's memory, so it is
# gone and deploy would fail with "No Quest detected". Reconnect to the same address.
if [[ "$serial" == *:* ]]; then
  IP="${serial%:*}" PORT="${serial##*:}" "$REPO_ROOT/tools/quest-connect.sh"
fi

exec "$REPO_ROOT/tools/deploy.sh"
