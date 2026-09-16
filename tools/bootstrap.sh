#!/usr/bin/env bash
# Applies the Quest project configuration from code (see Assets/Editor/ProjectBootstrap.cs).
# Run once after first opening the project in Unity, and again after any package upgrade.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

unity_bin="$(find_unity)"
log="$STATE_DIR/bootstrap.log"
: > "$log"

info "Applying project settings"
set +e
"$unity_bin" \
  -batchmode -nographics -quit \
  -projectPath "$REPO_ROOT" \
  -logFile "$log" \
  -executeMethod RowVR.Editor.ProjectBootstrap.Apply
status=$?
set -e

# Echo the bootstrap report out of the Unity log so the manual steps are visible here
# rather than buried in a log file.
grep -A40 '\[RowVR\] Project settings report' "$log" | sed 's/^/  /' || true

if (( status != 0 )); then
  fail "Bootstrap failed (exit $status). Recent errors:"
  grep -E 'error CS|Exception|\[RowVR\]' "$log" | tail -30 || true
  dim "Full log: $log"
  exit "$status"
fi
ok "Project settings applied."
