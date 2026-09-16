#!/usr/bin/env bash
# Checks everything the dev loop needs, and says how to fix whatever is missing.
# Exits non-zero if any hard requirement fails. Run this first on a new machine.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

problems=0
note() { warn "$*"; problems=$((problems + 1)); }

info "row_vr preflight"
echo

# --- host tools -------------------------------------------------------------
if command -v adb >/dev/null 2>&1; then
  ok "adb            $(adb version 2>/dev/null | head -1)"
else
  note "adb not found.
     Debian/Ubuntu : sudo apt install android-sdk-platform-tools
     Arch          : sudo pacman -S android-tools
     Fedora        : sudo dnf install android-tools"
fi

if command -v scrcpy >/dev/null 2>&1; then
  ok "scrcpy         $(scrcpy --version 2>/dev/null | head -1)"
else
  warn "scrcpy not found (optional, but it is the only way to see the headset view on
     Linux — Meta Quest Developer Hub does not exist here). 'make cast' needs it."
fi

if command -v java >/dev/null 2>&1; then
  ok "java           $(java -version 2>&1 | head -1 | tr -d '"')"
else
  warn "No system JDK. Unity ships its own OpenJDK with the Android module, so this is
     usually fine — only a concern if builds fail looking for JAVA_HOME."
fi

if command -v dotnet >/dev/null 2>&1; then
  ok "dotnet         $(dotnet --version 2>/dev/null)"
else
  note "dotnet SDK not found — 'make test' (tier 1, the fast loop) needs it.
     See https://dotnet.microsoft.com/download or your distro's dotnet-sdk package."
fi

# --- unity ------------------------------------------------------------------
pinned="$(project_unity_version)"
if unity_bin="$(find_unity 2>/dev/null)"; then
  ok "unity          $unity_bin"
  dim "               project is pinned to $pinned"
else
  note "Unity $pinned not found. Install it via Unity Hub with these modules:
       - Android Build Support
       - OpenJDK
       - Android SDK & NDK Tools
     Then set UNITY_PATH in tools/local.env if it lives somewhere unusual."
fi

# --- udev (the classic first-hour blocker) ----------------------------------
# Without a rule, the Quest enumerates but adb sees it as 'no permissions'.
if compgen -G "/etc/udev/rules.d/*android*" >/dev/null 2>&1 \
   || compgen -G "/etc/udev/rules.d/*51-*" >/dev/null 2>&1; then
  ok "udev rule      present"
else
  warn "No Android udev rule found in /etc/udev/rules.d.
     If adb shows your Quest as 'no permissions', run:
       echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"2833\", MODE=\"0666\", GROUP=\"plugdev\"' \\
         | sudo tee /etc/udev/rules.d/51-oculus.rules
       sudo udevadm control --reload-rules && sudo udevadm trigger
     2833 is Meta/Oculus. Make sure you are in the plugdev group."
fi

# --- device -----------------------------------------------------------------
echo
if command -v adb >/dev/null 2>&1; then
  if serial="$(adb_device 2>/dev/null)"; then
    model="$(adb -s "$serial" shell getprop ro.product.model 2>/dev/null | tr -d '\r')"
    release="$(adb -s "$serial" shell getprop ro.build.version.release 2>/dev/null | tr -d '\r')"
    sdk="$(adb -s "$serial" shell getprop ro.build.version.sdk 2>/dev/null | tr -d '\r')"
    ok "device         $serial  ($model, Android $release / API $sdk)"
    if adb -s "$serial" shell pm list packages 2>/dev/null | grep -q "^package:$APP_ID$"; then
      ok "app installed  $APP_ID"
    else
      dim "               $APP_ID not installed yet — run 'make dev'"
    fi
  else
    note "No Quest connected. Plug in over USB and accept the on-headset prompt,
     or run 'make connect' for wireless. Developer mode must be enabled for this
     headset in the Meta Horizon phone app (it requires a developer organisation)."
  fi
fi

echo
if (( problems )); then
  fail "$problems blocking problem(s) above."
  exit 1
fi
ok "Preflight passed."
