#!/usr/bin/env bash
# Mirrors the headset view to your desktop. Meta Quest Developer Hub is Windows/macOS
# only, so on Linux scrcpy is how you see what the wearer sees.
#
#   make cast
#   QUEST_CROP=1600:1200:232:504 make cast     # left eye on a Quest 3 (4128x2208)
#
# The headset renders both eyes side by side with heavy lens distortion, so an
# uncropped mirror looks wrong. Crop to one eye for anything you intend to watch for
# more than a moment. The right numbers depend on the current render resolution:
# run 'make cast' once uncropped, read the source size scrcpy prints, then crop to
# roughly the left half.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

command -v scrcpy >/dev/null 2>&1 || die "scrcpy not found.
     Debian/Ubuntu : sudo apt install scrcpy
     Arch          : sudo pacman -S scrcpy
     Fedora        : sudo dnf install scrcpy"

serial="$(adb_device)"

args=(--serial "$serial" --no-audio --stay-awake --window-title "row_vr @ $serial")
[[ -n "$QUEST_CROP" ]] && args+=(--crop "$QUEST_CROP")
[[ -n "${MAX_SIZE:-}" ]] && args+=(--max-size "$MAX_SIZE")

info "Casting from $serial${QUEST_CROP:+ (crop $QUEST_CROP)}"
exec scrcpy "${args[@]}"
