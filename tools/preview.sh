#!/usr/bin/env bash
# Tier 2, headless: render stills of the rowing scene in Play Mode, with no Editor window
# and no headset. ClipPlayback replays its recording, the rower rows for a while, and the
# shots land in .devloop/preview/ as PNGs.
#
#   make preview                 # capture 16 s into Play Mode
#   make preview PREVIEW_SECONDS=30   # later in the take
#   make preview GIF=1                 # also a ~6 s GIF from the rower's eye (needs ffmpeg)
#   make preview SCENE=Assets/Scenes/Rapids.unity
#   make preview WRIST=1               # pin the wrist menu in front of the head to see its layout
#   make preview GIF=1 VIEW=menu       # the GIF looks down at the menu (or VIEW=level, straight ahead)
#
# Needs a display (it renders on the GPU, so no -nographics), and the Editor must not
# already have the project open. Like any batch Unity run, it kills the adb server when it
# exits, so a wireless headset needs 'make connect' afterwards.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

unity_bin="$(find_unity)"
log="$STATE_DIR/preview.log"
out="$STATE_DIR/preview"
: > "$log"
mkdir -p "$out"
rm -f "$out"/*.png "$out"/*.gif

[[ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]] || die "No display. Preview renders on the GPU and needs one."
[[ ! -f "$REPO_ROOT/Temp/UnityLockfile" ]] || warn "Temp/UnityLockfile exists; if the Editor has the project open, close it first."

info "Rendering preview stills"
start=$SECONDS
"$unity_bin" \
  -batchmode \
  -projectPath "$REPO_ROOT" \
  -logFile "$log" \
  -executeMethod RowVR.Editor.Preview.CaptureBatch \
  -previewSeconds "${PREVIEW_SECONDS:-16}" \
  -previewOut "$out" \
  -previewFrames "$([[ -n "${GIF:-}" ]] && echo 90 || echo 0)" \
  -previewFps 15 \
  -previewScene "${SCENE:-Assets/Scenes/Row.unity}" \
  -previewView "${VIEW:-ahead}" \
  $([[ -n "${WRIST:-}" ]] && echo -previewWristMenu) &
unity_pid=$!

# Unity can take minutes to shut down after a batch run, and on this machine it has hung in
# the 'adb kill-server' it runs on exit. The stills are what matter: once the log says they
# are written (or the run fails), give Unity a short grace period, then stop it.
deadline=$(( SECONDS + 300 ))
while kill -0 "$unity_pid" 2>/dev/null; do
  if grep -qE '^\[RowVR\] preview: (captured|timed out)|error CS' "$log" 2>/dev/null; then
    for _ in $(seq 20); do kill -0 "$unity_pid" 2>/dev/null || break; sleep 1; done
    break
  fi
  (( SECONDS < deadline )) || { warn "No capture after 300 s; stopping Unity."; break; }
  sleep 2
done
if kill -0 "$unity_pid" 2>/dev/null; then
  dim "Unity still shutting down; stopping it."
  pkill -9 -f "AndroidPlayer/SDK/platform-tools/adb kill-server" 2>/dev/null || true
  kill "$unity_pid" 2>/dev/null; sleep 3; kill -9 "$unity_pid" 2>/dev/null || true
fi
# Unity is often killed above, so its exit status says nothing; the log and stills decide.
wait "$unity_pid" 2>/dev/null || true
status=0
grep -qE '^\[RowVR\] preview: captured' "$log" || status=1
ls "$out"/*.png >/dev/null 2>&1 || status=1
elapsed=$(( SECONDS - start ))

if (( status != 0 )); then
  fail "Preview failed after ${elapsed}s (exit $status). From the log:"
  grep -E '^\[RowVR\]|error CS|Exception|Shader error' "$log" | tail -30 || true
  dim "Full log: $log"
  exit "$status"
fi

grep -E '^\[RowVR\] preview' "$log" | tail -1 || true

if [[ -n "${GIF:-}" ]] && ls "$out"/frame_*.png >/dev/null 2>&1; then
  command -v ffmpeg >/dev/null 2>&1 || die "GIF=1 needs ffmpeg."
  # A palette built from the clip itself keeps the illustrated colours clean in 256 colours.
  ffmpeg -loglevel error -y -framerate 15 -i "$out/frame_%03d.png" \
    -vf "scale=480:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=192:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4" \
    "$out/row.gif"
  rm -f "$out"/frame_*.png
fi
ok "Preview in ${elapsed}s:"
ls -1 "$out"/*.png | sed 's/^/     /'
