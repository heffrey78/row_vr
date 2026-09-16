#!/usr/bin/env bash
# Tier 3, step 1: build an IL2CPP/arm64 APK headlessly.
# Every build is a full IL2CPP compile, so expect minutes. That cost is why tiers 1
# and 2 exist; reach for this one deliberately.
#
#   make build            # development build (default: readable stacks, debugger-attachable)
#   make build RELEASE=1  # release build
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

unity_bin="$(find_unity)"
log="$STATE_DIR/build.log"
: > "$log"

build_args=(-output "$APK_PATH")
if [[ -z "${RELEASE:-}" ]]; then
  build_args+=(-devBuild)
  mode="development"
else
  mode="release"
fi

info "Building $mode APK with $(basename "$(dirname "$(dirname "$unity_bin")")")"
dim "  output : $APK_PATH"
dim "  log    : $log"

start=$SECONDS

# -nographics keeps it off the GPU; no -quit because BuildQuest.Apk sets the exit code
# itself (Unity's -quit exits 0 even on a failed build).
set +e
"$unity_bin" \
  -batchmode \
  -nographics \
  -projectPath "$REPO_ROOT" \
  -logFile "$log" \
  -executeMethod RowVR.Editor.BuildQuest.Apk \
  -- "${build_args[@]}"
status=$?
set -e

elapsed=$(( SECONDS - start ))

if (( status != 0 )); then
  fail "Build failed after ${elapsed}s (exit $status). Errors from the log:"
  grep -E '^\[RowVR\]|error CS|Exception|BuildFailedException' "$log" | tail -40 || true
  echo
  dim "Full log: $log"
  exit "$status"
fi

[[ -f "$APK_PATH" ]] || die "Unity reported success but $APK_PATH does not exist. See $log"

size="$(du -h "$APK_PATH" | cut -f1)"
ok "Built $mode APK in ${elapsed}s ($size)"
