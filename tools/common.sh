#!/usr/bin/env bash
# Shared helpers for the row_vr dev loop. Sourced, never executed directly.
#
# Configuration precedence: environment > tools/local.env > defaults here.
# tools/local.env is gitignored; put machine-specific paths and your headset IP there.

set -euo pipefail

# ${BASH_SOURCE[0]:-$0}, not ${BASH_SOURCE[0]}: under `set -u` an unset BASH_SOURCE is a
# hard error, which happens when this file is sourced from a non-bash shell. The scripts
# themselves are #!/usr/bin/env bash so `make` is unaffected, but sourcing common.sh by
# hand from zsh or fish used to resolve REPO_ROOT to / and then fail somewhere unrelated.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/.." && pwd)"
readonly REPO_ROOT

# --- output -----------------------------------------------------------------
if [[ -t 1 ]] && [[ -z "${NO_COLOR:-}" ]]; then
  C_RED=$'\033[31m'; C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'
  C_BLUE=$'\033[34m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
else
  C_RED=''; C_GREEN=''; C_YELLOW=''; C_BLUE=''; C_DIM=''; C_OFF=''
fi

info()  { printf '%s==>%s %s\n' "$C_BLUE"  "$C_OFF" "$*"; }
ok()    { printf '%s ok %s %s\n' "$C_GREEN" "$C_OFF" "$*"; }
warn()  { printf '%swarn%s %s\n' "$C_YELLOW" "$C_OFF" "$*" >&2; }
fail()  { printf '%sFAIL%s %s\n' "$C_RED"   "$C_OFF" "$*" >&2; }
die()   { fail "$*"; exit 1; }
dim()   { printf '%s%s%s\n' "$C_DIM" "$*" "$C_OFF"; }

# --- configuration ----------------------------------------------------------
# shellcheck source=/dev/null
[[ -f "$REPO_ROOT/tools/local.env" ]] && source "$REPO_ROOT/tools/local.env"

APP_ID="${APP_ID:-com.heffrey78.rowvr}"
APK_PATH="${APK_PATH:-$REPO_ROOT/Build/row_vr.apk}"
STATE_DIR="${STATE_DIR:-$REPO_ROOT/.devloop}"
# scrcpy crop for the headset view, as WIDTH:HEIGHT:X:Y. Left empty by default because the
# correct value depends on the headset's current render resolution; see docs/dev-loop.md.
QUEST_CROP="${QUEST_CROP:-}"

mkdir -p "$STATE_DIR"

# --- unity ------------------------------------------------------------------
project_unity_version() {
  local f="$REPO_ROOT/ProjectSettings/ProjectVersion.txt"
  [[ -f "$f" ]] || die "ProjectSettings/ProjectVersion.txt is missing."
  awk '/^m_EditorVersion:/ { print $2; exit }' "$f"
}

# Locates the Unity Editor binary for the version this project is pinned to.
# Honours $UNITY_PATH if set (put it in tools/local.env for a non-standard install).
find_unity() {
  if [[ -n "${UNITY_PATH:-}" ]]; then
    [[ -x "$UNITY_PATH" ]] || die "UNITY_PATH is set but not executable: $UNITY_PATH"
    printf '%s\n' "$UNITY_PATH"
    return 0
  fi

  local version candidate
  version="$(project_unity_version)"

  local -a candidates=(
    "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
    "$HOME/.local/share/Unity/Hub/Editor/$version/Editor/Unity"
    "/opt/unity/editors/$version/Editor/Unity"
    "/opt/Unity/Hub/Editor/$version/Editor/Unity"
    "/usr/bin/unity-editor"
  )

  for candidate in "${candidates[@]}"; do
    [[ -x "$candidate" ]] && { printf '%s\n' "$candidate"; return 0; }
  done

  # Fall back to any installed version, but say so loudly: building against a different
  # Editor than the project is pinned to triggers an asset database upgrade that will
  # show up as a large, confusing diff.
  local other
  other="$(find "$HOME/Unity/Hub/Editor" -maxdepth 3 -name Unity -type f -perm -u+x 2>/dev/null | head -1 || true)"
  if [[ -n "$other" ]]; then
    warn "Unity $version not found. Falling back to: $other"
    warn "This will prompt an asset upgrade. Install $version via Unity Hub, or set UNITY_PATH."
    printf '%s\n' "$other"
    return 0
  fi

  die "No Unity Editor found for $version. Install it with Unity Hub, or set UNITY_PATH in tools/local.env."
}

# --- adb --------------------------------------------------------------------
require_adb() {
  command -v adb >/dev/null 2>&1 \
    || die "adb not found. Install Android platform-tools; see docs/dev-loop.md."
}

# Prints the serial of the single usable device, or exits with guidance.
# Prefers a wireless (host:port) connection when both are present, so unplugging the
# cable mid-session does not break the loop.
adb_device() {
  require_adb
  local -a devices=()
  local line serial state
  while read -r line; do
    [[ "$line" =~ ^List\ of\ devices || -z "$line" ]] && continue
    serial="$(awk '{print $1}' <<<"$line")"
    state="$(awk '{print $2}' <<<"$line")"
    case "$state" in
      device)      devices+=("$serial") ;;
      unauthorized)
        die "Device $serial is unauthorized. Put the headset on and accept the
     'Allow USB debugging' prompt, then re-run. If no prompt appears, run:
       adb kill-server && adb devices" ;;
      offline)     warn "Device $serial is offline; ignoring." ;;
    esac
  done < <(adb devices 2>/dev/null)

  (( ${#devices[@]} )) || die "No Quest detected. Run 'make connect', or see docs/dev-loop.md (udev rules)."

  local d
  for d in "${devices[@]}"; do
    [[ "$d" == *:* ]] && { printf '%s\n' "$d"; return 0; }
  done
  printf '%s\n' "${devices[0]}"
}

# Resolves the launcher activity rather than assuming UnityPlayerActivity: Unity's
# activity name changes with the XR plug-in and Meta's manifest merging.
launcher_activity() {
  local serial="$1"
  adb -s "$serial" shell cmd package resolve-activity --brief "$APP_ID" 2>/dev/null \
    | tail -n 1 | tr -d '\r'
}
