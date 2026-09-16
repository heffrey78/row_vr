#!/usr/bin/env bash
# Tier 3 in one command: build, install, launch, follow logs.
# 2-8 minutes per pass. Use it for tracking fidelity, performance and comfort —
# not for iterating on stroke logic, which belongs in tier 1 ('make test').
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

# Fail fast on a missing headset rather than after a multi-minute IL2CPP build.
serial="$(adb_device)"
dim "target: $serial"

"$REPO_ROOT/tools/build.sh"
exec "$REPO_ROOT/tools/deploy.sh"
