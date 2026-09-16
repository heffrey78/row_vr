#!/usr/bin/env bash
# Runs Unity's EditMode test suite. Slower than 'make test' because it spins up the
# Editor; use it only for tests that genuinely need engine types. Logic tests belong
# in tests/RowVR.Stroke.Tests, which run without Unity at all.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

unity_bin="$(find_unity)"
log="$STATE_DIR/test-unity.log"
results="$STATE_DIR/editmode-results.xml"
: > "$log"

info "Running Unity EditMode tests"
set +e
"$unity_bin" \
  -batchmode -nographics \
  -projectPath "$REPO_ROOT" \
  -logFile "$log" \
  -runTests -testPlatform EditMode \
  -testResults "$results"
status=$?
set -e

if [[ -f "$results" ]] && command -v python3 >/dev/null 2>&1; then
  python3 - "$results" <<'PY'
import sys, xml.etree.ElementTree as ET
try:
    r = ET.parse(sys.argv[1]).getroot()
    print("  total={} passed={} failed={} skipped={}".format(
        r.get("total"), r.get("passed"), r.get("failed"), r.get("skipped")))
    for tc in r.iter("test-case"):
        if tc.get("result") not in ("Passed", "Skipped"):
            print("  FAILED: {}".format(tc.get("fullname")))
except Exception as e:
    print("  (could not parse results: {})".format(e))
PY
fi

if (( status != 0 )); then
  fail "EditMode tests failed (exit $status). Log: $log"
  exit "$status"
fi
ok "EditMode tests passed."
