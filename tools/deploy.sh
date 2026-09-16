#!/usr/bin/env bash
# Tier 3, step 2: install the APK, launch it, and tail its logs.
#
#   make deploy           # install + launch + follow logs
#   make deploy NOLOGS=1  # install + launch, then return
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

[[ -f "$APK_PATH" ]] || die "No APK at $APK_PATH. Run 'make build' first."

serial="$(adb_device)"
info "Installing to $serial"

# -r reinstalls keeping data; -d allows a version-code downgrade, which happens
# whenever you build from an older branch. Without -d that fails confusingly.
if ! install_out="$(adb -s "$serial" install -r -d "$APK_PATH" 2>&1)"; then
  fail "Install failed:"
  printf '%s\n' "$install_out" >&2
  if grep -q 'INSTALL_FAILED_UPDATE_INCOMPATIBLE\|signatures do not match' <<<"$install_out"; then
    echo
    warn "The installed build was signed with a different key. Uninstall and retry:"
    warn "  adb -s $serial uninstall $APP_ID && make deploy"
  fi
  exit 1
fi
ok "Installed $APP_ID"

activity="$(launcher_activity "$serial")"
if [[ -n "$activity" && "$activity" == *'/'* ]]; then
  adb -s "$serial" shell am start -n "$activity" >/dev/null
else
  # Fall back to an implicit launcher intent if the activity could not be resolved.
  warn "Could not resolve the launcher activity; starting via monkey."
  adb -s "$serial" shell monkey -p "$APP_ID" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
fi
ok "Launched — put the headset on"

[[ -n "${NOLOGS:-}" ]] && exit 0
exec "$REPO_ROOT/tools/logs.sh"
