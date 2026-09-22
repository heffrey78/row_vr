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
     Linux — Meta Quest Developer Hub does not exist here). 'make cast' needs it.
     Arch/CachyOS  : sudo pacman -S scrcpy
     Debian/Ubuntu : sudo apt install scrcpy"
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
     Arch/CachyOS  : sudo pacman -S dotnet-sdk-8.0
     Debian/Ubuntu : sudo apt install dotnet-sdk-8.0
     Fedora        : sudo dnf install dotnet-sdk-8.0
     Or see https://dotnet.microsoft.com/download"
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
#
# Distro packages (Arch's android-udev, Debian's android-sdk-platform-tools-common)
# install their rules under /usr/lib/udev/rules.d, NOT /etc/udev/rules.d. Checking only
# /etc reports "no rule" on a machine that is correctly set up, which sends you fixing
# something that is not broken. Check every directory udev actually reads.
# Packaged rules are named for the vendor, so match on that. The bare '51-' pattern is
# only honoured under /etc, where this project's hand-written rule goes: matching it in
# the system directories would false-positive on unrelated distro rules that happen to
# sort at 51.
udev_rule_file=""
for pattern in /etc/udev/rules.d/*android* /etc/udev/rules.d/*oculus* \
               /etc/udev/rules.d/*51-* \
               /usr/lib/udev/rules.d/*android* /usr/lib/udev/rules.d/*oculus* \
               /lib/udev/rules.d/*android* /lib/udev/rules.d/*oculus*; do
  if [[ -e "$pattern" ]]; then udev_rule_file="$pattern"; break; fi
done

if [[ -n "$udev_rule_file" ]]; then
  ok "udev rule      $udev_rule_file"
  # A rule is only half of it: the rule grants access to a group, and you have to be in
  # it. The group name differs by distro - Arch's android-udev uses 'adbusers', Debian
  # and Ubuntu use 'plugdev'. Check whichever one exists on this machine.
  for g in adbusers plugdev android; do
    if getent group "$g" >/dev/null 2>&1; then
      if id -nG | tr ' ' '\n' | grep -qx "$g"; then
        ok "device group   $g"
      else
        note "You are not in the '$g' group, which the udev rules grant device access to.
     Run:  sudo usermod -aG $g \"\$USER\"
     then log out and back in (a new shell is not enough)."
      fi
      break
    fi
  done
else
  warn "No Android udev rule found in /etc/udev/rules.d or /usr/lib/udev/rules.d.
     If adb shows your Quest as 'no permissions', install your distro's rules:
       Arch/CachyOS  : sudo pacman -S android-udev
                       sudo usermod -aG adbusers \"\$USER\"
       Debian/Ubuntu : sudo apt install android-sdk-platform-tools-common
                       sudo usermod -aG plugdev \"\$USER\"
     Or write one by hand (2833 is Meta/Oculus):
       echo 'SUBSYSTEM==\"usb\", ATTR{idVendor}==\"2833\", MODE=\"0666\"' \\
         | sudo tee /etc/udev/rules.d/51-oculus.rules
       sudo udevadm control --reload-rules && sudo udevadm trigger
     Log out and back in after changing groups."
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
