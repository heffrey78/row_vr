#!/usr/bin/env bash
# Switches the headset to wireless adb so you can row without a cable attached to
# your face. Requires one USB connection to set up; after that it reconnects over
# Wi-Fi using the cached address.
#
#   make connect          # set up from USB, or reuse the cached address
#   make connect IP=1.2.3.4
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
require_adb

PORT="${PORT:-5555}"
cache="$STATE_DIR/device-ip"

# 1. An explicit address always wins.
ip="${IP:-}"

# adb connect with a timeout. Without one, a connection the local adb server still holds
# from before the cable was pulled makes `adb connect` hang indefinitely - the headset's
# port is open, the handshake just never finishes. Restarting the local server clears it,
# so do that once before giving up.
try_connect() {
  local target="$1" attempt
  for attempt in 1 2; do
    adb disconnect "$target" >/dev/null 2>&1 || true
    if timeout 10 adb connect "$target" 2>&1 | grep -qE '^(already )?connected to'; then
      # "connected" is not enough: a stale transport can report connected but stay offline.
      if timeout 5 adb -s "$target" get-state 2>/dev/null | grep -q '^device$'; then
        return 0
      fi
    fi
    if (( attempt == 1 )); then
      warn "No usable connection to $target; restarting the local adb server and retrying."
      adb kill-server >/dev/null 2>&1 || true
      adb start-server >/dev/null 2>&1 || true
    fi
  done
  return 1
}

# 2. Otherwise try the cached one — the common case on the second run onward.
if [[ -z "$ip" && -f "$cache" ]]; then
  ip="$(cat "$cache")"
  info "Trying cached address $ip"
  if try_connect "$ip:$PORT"; then
    ok "Connected to $ip:$PORT"
    exit 0
  fi
  warn "Cached address did not answer. If the headset is asleep, put it on to wake it and
     re-run. Otherwise falling back to USB discovery."
  ip=""
fi

# 3. Otherwise discover it from a USB-attached device.
if [[ -z "$ip" ]]; then
  info "Looking for a USB-attached headset..."
  usb_serial=""
  while read -r line; do
    [[ "$line" =~ ^List\ of\ devices || -z "$line" ]] && continue
    s="$(awk '{print $1}' <<<"$line")"; st="$(awk '{print $2}' <<<"$line")"
    [[ "$st" == "device" && "$s" != *:* ]] && { usb_serial="$s"; break; }
  done < <(adb devices)

  [[ -n "$usb_serial" ]] || die "No USB-attached headset. Plug the Quest in, accept the
     'Allow USB debugging' prompt on the headset, then re-run. (Wireless setup needs
     one cable connection; after that the cable is optional.)"

  ip="$(adb -s "$usb_serial" shell ip -f inet addr show wlan0 2>/dev/null \
        | awk '/inet /{ sub(/\/.*/, "", $2); print $2; exit }' | tr -d '\r')"
  [[ -n "$ip" ]] || die "Could not read the headset's wlan0 address. Is it on Wi-Fi?"

  info "Headset is at $ip; enabling adb over TCP on port $PORT"
  adb -s "$usb_serial" tcpip "$PORT" >/dev/null
  # The daemon needs a moment to rebind before it will accept a connection.
  sleep 2
fi

if try_connect "$ip:$PORT"; then
  printf '%s\n' "$ip" > "$cache"
  ok "Connected to $ip:$PORT (cached for next time)"
  dim "You can unplug the cable now. 'adb disconnect' to drop it."
else
  die "Could not connect to $ip:$PORT. Check both devices are on the same network
     and that the headset has not gone to sleep."
fi
