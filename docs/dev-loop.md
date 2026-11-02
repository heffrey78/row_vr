# The dev loop

How to get from a fresh Linux machine to a change you can feel in the headset, and then
how to stop needing the headset for most of the work.

---

## 0. What Linux costs you, up front

Most Quest tutorials assume Windows. These are the pieces that simply do not exist here:

| Tool | What it does | Linux |
|---|---|---|
| Quest Link / Air Link | Run Play Mode *on the headset* | ❌ Windows only |
| Meta XR Simulator | Fake headset on the desktop | ❌ Windows + Mac ARM only |
| Meta Quest Developer Hub (MQDH) | Device manager, casting, logs, perf | ❌ Windows + macOS only |
| Unity Editor + Android build | | ✅ |
| `adb` | | ✅ |
| Unity XR Interaction Simulator | Simulated hands in Play Mode | ✅ pure editor |

So there is **no in-editor play-on-device path**, and no GUI for device management. `adb` and
`scrcpy` replace MQDH. Every device build is a full IL2CPP/arm64 compile — minutes, not
seconds — and that cost is the reason this repo is shaped the way it is.

> If you later want Play Mode rendering to the headset, that is possible on Linux via WiVRn
> or SteamVR+ALVR, but it needs bionic→glibc shims and a patched OpenXR loader, and the
> known recipe covers controllers only — hand tracking through the shim is unproven. Treat
> it as a timeboxed experiment, never as part of the loop. See the bottom of this file.

---

## 1. One-time setup

### 1.1 Headset: developer mode

1. Create a developer organisation at `developers.meta.com` (required even for solo work,
   and it needs a verified account — a card or 2FA).
2. In the **Meta Horizon** phone app: your headset → **Headset settings** → **Developer
   mode** → on.
3. Reboot the headset.
4. Plug it into your machine over USB. Put it on — there is an **Allow USB debugging**
   prompt *inside* the headset. It is easy to miss and nothing works until you accept it.

### 1.2 Host: adb

```sh
sudo apt install android-sdk-platform-tools   # Debian/Ubuntu
sudo pacman -S android-tools                  # Arch / CachyOS
sudo dnf install android-tools                # Fedora
```

### 1.3 Host: udev rules ← **the usual first-hour blocker**

Without this, `adb devices` shows your headset as `no permissions` and every guide you find
will tell you to do something unrelated.

There are two halves to it: a **rule** granting access to a group, and **your account being
in that group**. Getting one without the other looks exactly like getting neither.

**Arch / CachyOS** — use the packaged rules. The `android-udev` package installs them into
`/usr/lib/udev/rules.d` and creates the group they grant access to, which is `adbusers`.
There is no `plugdev` group on Arch, so instructions telling you to join one are for a
different distro:

```sh
sudo pacman -S android-udev
sudo usermod -aG adbusers "$USER"     # log out and back in for this to take effect
sudo udevadm control --reload-rules && sudo udevadm trigger
```

**Debian / Ubuntu** — the packaged rules use `plugdev`:

```sh
sudo apt install android-sdk-platform-tools-common
sudo usermod -aG plugdev "$USER"      # log out and back in for this to take effect
sudo udevadm control --reload-rules && sudo udevadm trigger
```

**Any distro, by hand** — if no package provides rules, write one. `2833` is Meta/Oculus:

```sh
echo 'SUBSYSTEM=="usb", ATTR{idVendor}=="2833", MODE="0666"' \
  | sudo tee /etc/udev/rules.d/51-oculus.rules
sudo udevadm control --reload-rules && sudo udevadm trigger
```

Then check:

```sh
adb kill-server && adb devices
```

You should see your serial and the word `device`. A group change does **not** apply to
existing sessions — log out and back in, not just a new terminal.

### 1.4 Host: Unity

Install **Unity Hub**, then the Editor version this project is pinned to — read it from
`ProjectSettings/ProjectVersion.txt` rather than assuming, because that is the version the
build scripts look for.

**Arch / CachyOS:** `paru -S unityhub`. The AUR package is not a third-party rebuild — it
repackages Unity's own official `.deb` from `hub.unity3d.com` as a native Arch package, so
it is the same binary Ubuntu gets, with dependencies handled by pacman. Prefer it over the
AppImage: no FUSE workarounds, and it installs Editors to `~/Unity/Hub/Editor/<version>/`,
which is the first path `find_unity()` in `tools/common.sh` looks in, so preflight finds it
with no `UNITY_PATH` needed.

If that package is flagged out of date, it usually does not matter — the Hub is only a
launcher and the Editor version is pinned by this project anyway. The one failure mode is
an older Hub not listing the pinned Editor; if that happens, use the Unity download archive
link for that exact version, which hands off to the Hub directly.

**Any other distro:** the AppImage from
`https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x86_64.AppImage`. If it fails
with a FUSE error, run it with `--appimage-extract-and-run`.

Unity officially supports Ubuntu only; RHEL/Fedora are explicitly unsupported and Arch is
not mentioned at all. That applies to the AppImage too — it is more portable, not more
supported.

Required modules (tick them during install; adding them later means a re-download):

- **Android Build Support**
- **OpenJDK**
- **Android SDK & NDK Tools**

If your Editor lives somewhere unusual, create `tools/local.env` (gitignored):

```sh
UNITY_PATH=/opt/unity/editors/6000.3.12f1/Editor/Unity
```

### 1.5 Host: the rest

```sh
# Arch / CachyOS
sudo pacman -S scrcpy dotnet-sdk-8.0 git-lfs

# Debian / Ubuntu
sudo apt install scrcpy dotnet-sdk-8.0 git-lfs

git lfs install                  # binary assets go through LFS
```

`scrcpy` is headset casting, the MQDH replacement. The .NET SDK is **not** optional — it is
the entire fast loop. A system JDK is not needed: Unity ships its own with the Android
module.

Point git at Unity's merge tool so scenes and prefabs merge instead of conflicting:

```sh
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver \
  "'/path/to/Editor/Data/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```

### 1.6 Check it

```sh
make preflight
```

It checks every item above and tells you how to fix whatever is missing. Get it green before
going further.

### 1.7 First Unity open

```sh
# Open the project in Unity once so it generates Library/ and resolves packages, then:
make bootstrap
```

Then build the scene, once, from the Editor menu: **RowVR > Create Rowing Scene**. It
creates `Assets/Scenes/Row.unity` with a floor-relative XR Origin and a Rowing Rig whose
`trackingOrigin` and `head` are already wired, and registers the scene in Build Settings -
`make build` aborts when that list is empty. Commit the scene; from then on it is an
ordinary scene you edit in the Editor.

The wiring is done in code for one reason: hand joints arrive in tracking space, so a rig
with `trackingOrigin` unset reports a handle parked at the world origin, forever, with no
error anywhere. That is the single most confusing failure this project has.

`make bootstrap` applies the Quest configuration from `Assets/Editor/ProjectBootstrap.cs`
(IL2CPP, ARM64, Vulkan, Linear, targetSdk 34, text serialization, 4x MSAA). It also sets up
XR Plug-in Management through `Assets/Editor/XRBootstrap.cs`: it assigns the OpenXR loader
for Android and turns on the hand tracking features and interaction profiles. It ends with
a report of every setting next to the value it should have. There are no manual steps.
Nothing else makes a missing XR loader obvious: without one, the APK builds and runs as a
flat 2D Android app, with no error.

> Settings live in code rather than in checked-in `ProjectSettings/*.asset` YAML on purpose:
> those files are large, version-specific, merge badly, and a malformed one stops the Editor
> opening. As code, every decision is reviewable in a diff, and a CLI build and a GUI build
> cannot drift apart.

---

## 2. The three loops

### Tier 1 — `make test` (~2s, no headset, no Unity)

```sh
make test
```

Stroke detection lives in `Assets/Scripts/Stroke/` as an assembly that **cannot reference
UnityEngine** (its asmdef sets `noEngineReferences`, so a `using UnityEngine` there is a
compile error, not a style note). `src/RowVR.Stroke/RowVR.Stroke.csproj` compiles those same
files, so the identical code builds and tests under plain `dotnet test`.

This is where stroke phase logic, cadence, filtering and thresholds get developed. **Default
to this tier.** Reaching for the headset to answer a question you could have answered in two
seconds is the main way this project could end up slow to work on.

Tests run against two things: synthetic rowing motion of exactly known timing
(`SyntheticRower`), and recorded clips committed under
`tests/RowVR.Stroke.Tests/fixtures/`.

### Tier 2 — Unity Play Mode (~5s, no headset)

Press Play with the **XR Interaction Simulator** in the scene. It simulates *hands*, not
just controllers, which is what makes it a real substitute here given Meta XR Simulator does
not run on Linux.

> The simulator is **not** a `GameObject > XR` menu item. It ships as a package *sample*,
> so it does not exist in the project until you import it: **Window > Package Manager > XR
> Interaction Toolkit > Samples > XR Interaction Simulator > Import**. That drops it under
> `Assets/Samples/`, and the prefab can then go in the scene. `RowVR > Create Rowing Scene`
> adds it automatically if it has already been imported, and warns if it has not.

For scene wiring, UI, audio, feedback and boat feel. Good enough to iterate on how the thing
*reads*; not good enough to judge real tracking.

The simulator starts in **controller mode**, and until you switch it the readout shows
"both hands untracked" from a subsystem called `Hand-Capture-Playback`. That subsystem is
how the simulator delivers hands in XR Hands 1.8+. To switch, press `[` and `]` together
once to select both hands, then again to change to hand mode.

**Replaying a recording is usually the better tier 2.** Keyboard-driven hands are too
jerky to look like a stroke. `ClipPlayback`, on the Rowing Rig, replays a `.rowclip` from
`tests/RowVR.Stroke.Tests/fixtures/` in real time, as if it were live tracking. It starts
again at the end of the clip, and each pass resets the detector and the readout's count.
Set **Clip Path** to choose a take, or untick the component to use the simulator. It only
runs in the Editor and turns itself off in a headset build.

**Headless: `make preview`.** Opens the scene in batch mode, enters Play Mode with the
recording replaying, and after `PREVIEW_SECONDS` (default 16) writes PNG stills to
`.devloop/preview/`: `eye-forward`, `eye-left`, `eye-back` from the recorded head pose, and
`boat` from outside. About 40 s. It needs a display (it renders on the GPU) and the Editor
must not have the project open. Like any batch Unity run it kills the adb server on exit, so
run `make connect` afterwards if the headset was on Wi-Fi.

Play Mode is configured to skip domain and scene reload, which is most of the speed. The
cost: statics are not reset between runs, so anything static needs
`[RuntimeInitializeOnLoadMethod]` to reset itself.

### Tier 3 — `make dev` (1 min warm, ~13 min for the first build, headset)

```sh
make connect     # once per session, if you want to be free of the cable
make dev         # build → install → launch → tail logs
```

Ground truth: real hand-tracking fidelity, real performance, real comfort. Nothing else
substitutes, and nothing else should be used for anything a lower tier can answer.

Measured on this machine (CachyOS, Quest 3, Unity 6000.6.2f1), 2026-09-23:

| what changed | build | APK | `make dev` total |
|---|---|---|---|
| cold, empty `Library/Bee` | 790s | 91 MB | 13:16 |
| settings or scene only, cache warm | 50-65s | 105-150 MB | ~1:00 |
| script edit in an existing assembly | ~122s | 150 MB | ~2:07 |
| new assemblies (imported a package sample) | 579s | 150 MB | 9:46 |

The number that matters is **what changed**, not whether the cache is warm. Editing a
script inside an assembly that already exists is the ~1 minute case. Adding an assembly -
importing a package sample, adding an asmdef - invalidates most of the IL2CPP work and
costs nearly as much as a cold build. Plan sample imports and package changes as
batched, deliberate events rather than sprinkling them through a session.

Tier 1 remains ~25x faster than even the best tier 3 case and needs no headset, so the
ordering advice stands.

#### Frame rate on the headset

MQDH's profiler does not exist on Linux (#34), but the Quest runtime logs a
performance line every second, and adb can read it:

```sh
pid=$(adb shell pidof -s com.heffrey78.rowvr | tr -d '\r')
adb logcat -d --pid="$pid" | grep 'VrApi.*FPS='
```

Filter by the app's pid: the system shell logs the same lines under its own. Each line has
`FPS=` (achieved/target), `Stale=` (frames the compositor had to reuse), `GPU%=` (GPU
utilisation), and `CPU&GPU=` (frame time, ms). At 72 Hz the frame budget is 13.9 ms.

Measured 2026-09-29, scenery (#39) plus oars and sound, 102 s while rowing 23 strokes:

| | |
|---|---|
| FPS | 72 for 99 of 102 s; 67 for 2 s; 1 s at startup |
| GPU utilisation | median 16%, peak 41% |
| CPU&GPU frame time | median 5.5 ms of a 13.9 ms budget |
| APK | 157 MB (150 MB before the scenery) |
| Build after adding the scenery | 10:02, a new shader and scripts make it near-cold |

Draw calls and triangle counts are not in the runtime's line. #39's budget of 100
draw calls and 150k triangles is unmeasured, but the GPU is far from its limit.

Individually:

```sh
make build              # development APK (RELEASE=1 for release)
make deploy             # install, launch, follow logs
make deploy NOLOGS=1    # install and launch only
make logs               # follow logs of whatever is already running
make cast               # mirror the headset to your desktop
```

---

## 3. Wireless adb

Rowing on a cable tethered to your face is unpleasant. After one USB setup pass:

```sh
make connect
```

This reads the headset's Wi-Fi address over USB, switches it to `adb tcpip 5555`, connects,
and caches the address. Later runs reuse the cache and fall back to USB discovery if the
headset has moved networks. Unplug once it reports connected.

```sh
make connect IP=10.0.0.42    # skip discovery
make disconnect
```

The connection drops when the headset sleeps. Re-run `make connect`.

Unity also drops it: at the end of every command-line build it kills the adb server
(`Killing ADB server` at the end of `.devloop/build.log`). A USB device comes back by
itself when the next adb command starts a new server. A Wi-Fi connection existed only in
the old server, so it's gone. `make dev` reconnects to the same address after the build.
If you run `make build` on its own, run `make connect` before `make deploy`.

---

## 4. Casting

```sh
make cast
```

The headset renders both eyes side by side with heavy lens distortion, so an uncropped
mirror looks wrong. `QUEST_CROP` is `width:height:x:y`: the size of the box to keep, then
its top-left corner in the full image. `adb shell wm size` gives the full size.

On a Quest 3 that is 4128x2208, so each eye is 2064x2208. This keeps a 1600x1200 box
centred in the left eye, and gives a clear view *(verified 2026-09-27)*:

```sh
QUEST_CROP=1600:1200:232:504 make cast
MAX_SIZE=1024 make cast        # if the stream is choppy over Wi-Fi
```

Put the value in `tools/local.env` so plain `make cast` uses it. Over home Wi-Fi the
stream was smooth without `MAX_SIZE`.

---

## 5. Recording real strokes — the thing that makes tier 1 real

Synthetic motion is good for pinning down logic, but it cannot tell you what Quest hand
tracking actually does at the catch, when the hands are furthest from the cameras and partly
occluded by the body. Record it once, then iterate against it forever:

```sh
make dev                  # with HandRecorder in the scene, record a piece
make pull-recordings      # pull clips into recordings/raw/ (gitignored)
make promote CLIP=recordings/raw/stroke-20260916-142211.rowclip NAME=steady-24spm
make test                 # now testing against real tracking data
```

`make promote` copies a clip into `tests/RowVR.Stroke.Tests/fixtures/`, where it **is**
committed. A promoted clip is a regression test: it runs in CI, with no Unity, no licence
and no headset.

Clips are tab-separated text (`.rowclip`) rather than JSON or binary, so they diff line by
line in review and parse with nothing outside the BCL.

Worth recording early: a steady piece at your usual rate, a deliberately sloppy piece, a
very slow piece, and one where you stop mid-stroke. The last two are where detectors break.

---

## 5b. Passthrough and the boundary

When you let go of the handle, the room fades in (#28, #45), and it fades out at the
end of your first stroke. It uses the Quest's passthrough through Unity's OpenXR Meta package,
and `make bootstrap` turns on the two features it needs (Meta Quest: Session and Camera). The
menu and the numbers stay drawn over the room.

The room shows after about 0.3 s when your hands clearly leave the handle, and up to about 3 s
when the detector has to wait to rule out a stroke. If you open your fingers but keep your hands
still, nothing changes until tracking is lost: a known gap, noted in `PassthroughPolicy`.

**The boundary grid** is the Quest's, not the app's, and the app cannot choose it. For rowing,
set up a **stationary boundary** in the headset (Settings, then Boundary, then Stationary)
while sitting on the machine. The room-scale grid then stops drawing through the scene as you
row.

## 6. Troubleshooting

**`adb devices` shows `no permissions`** → §1.3 udev rules, and check you are in `plugdev`
(`groups | grep plugdev`); you must log out and back in after `usermod`.

**`adb devices` shows `unauthorized`** → put the headset on and accept the prompt.
`adb kill-server && adb devices` re-triggers it.

**Nothing at all in `adb devices`** → developer mode off, a charge-only USB cable, or the
headset asleep. Try another cable before anything else; this wastes more time than it should.

**`INSTALL_FAILED_UPDATE_INCOMPATIBLE`** → a build signed with a different key is installed.
`adb uninstall com.heffrey78.rowvr`, then `make deploy`.

**Build fails looking for the Android SDK** → the Android modules were not installed with
the Editor. Unity Hub → your version → gear → Add modules.

**Unity build "succeeds" but the APK is stale** → this is exactly why `tools/build.sh` does
not pass `-quit`. With `-quit`, Unity exits 0 even on a failed build. If you edit the build
scripts, keep `EditorApplication.Exit` doing the work.

**Hand tracking does not engage at all** → your controllers are probably awake. Horizon OS
switches input modality automatically, and while a controller is powered on and held, hand
tracking is disabled at the *system* level - the app cannot override it. In logcat this
reads as:

```
TrackingService: [HANDTRACKING] Controller ... in hand: Y
ServiceHandStateProcessor: HANDTRACKING hand 0 status set to Disabled
ModalityTransitionsPolicyManager: Switching to Tracked Remotes only   TRACKED REMOTES
ControllerNotifications: ... mHandTrackingEnabled?: false
```

Put the controllers down, out of reach, and let them sleep. This is very easy to mistake for
a detector bug, because the app looks like it is running fine and simply never sees a hand.

**Hands track but nothing registers as a stroke** → check `XRHandsPoseSource` is being given
the XR Origin transform. Joint poses arrive in tracking space; without that conversion the
handle sits at the world origin and never moves. This is the first thing to check.

**Stroke count is wrong / doubles** → do not tune blind against the headset. Record a clip,
promote it, and work in tier 1 where you can see the numbers. `FilterBeta` is the main
noise-versus-lag dial; `StrokeDetectorSettings` documents the tradeoff.

**Unity wants to upgrade the project on open** → your Editor differs from
`ProjectSettings/ProjectVersion.txt`. Install the pinned version or change the pin
deliberately, in its own commit.

---

## 7. Verification checklist

This file was originally written in a container with no Unity, no Android SDK and no
headset, so none of it had been run. Each ticked item below has been run on the hardware
named here, with the date. An unticked item has never been run, and the docs make no
claim about it.

**Host:** CachyOS, kernel 7.2.6, Unity 6000.6.2f1. **Headset:** Quest 3, Android 14 /
API 34, build UP1A.231005.007.A1.

- [x] `make preflight` — exits 0 with the headset connected over wireless adb and the app
      installed *(2026-09-27)*. First run *(2026-09-22)*: adb 1.0.41, scrcpy 4.1,
      dotnet SDK 8.0.131, udev rules from `android-udev` at
      `/usr/lib/udev/rules.d/51-android.rules`, account in `adbusers`.
- [x] Unity opens the project without errors *(2026-09-22)*. No package-resolution or
      compile errors in `Logs/Editor.log`, and no version-upgrade prompt. Getting there
      took moving the package pins to versions that support 6000.6 (`9b7a351`).
- [x] `make bootstrap` — every setting reports as wanted *(2026-09-27)*. XR Plug-in
      Management is set from code too (`XRBootstrap`): OpenXR loader for Android, init on
      start, hand tracking features and interaction profiles. No manual steps remain.
- [x] `make cast` — a clear single-eye view over wireless adb, smooth without `MAX_SIZE`
      *(2026-09-27)*. See section 4 for the crop.
- [x] `make dev` — builds, installs, launches, logs stream *(2026-09-23)*. The app runs as
      an immersive OpenXR app, and logcat shows real Quest hand tracking reaching the
      detector (`hand source: ok`, `catch #9 54.7 spm`). Wall-clock times are in the
      tier 3 table: 13:16 cold, ~1:00 warm.
- [x] `make dev` over wireless adb, uninterrupted, no cable *(2026-09-27)*. The build took
      103 s. Unity killed the adb server as it exited, `make dev` reconnected to the Wi-Fi
      address by itself, then installed and launched the app. Earlier runs that day had
      failed at exactly that point; see section 3.
- [x] Play Mode replaying a recorded take drives the detector *(2026-09-27)*. With
      `ClipPlayback` on the steady rowing-machine take: 13 catches, 1 withdrawn, 12 net,
      matching the rower's count, and every pass identical. Play Mode starts quickly.
- [x] Play Mode with the XR Interaction Simulator's hands *(2026-09-27)*. In hand mode
      the readout reports the simulated hands as tracking. In the default controller mode
      it shows "both hands untracked", as expected. Keyboard motion was not checked for
      strokes; recordings cover that.
- [x] `make test` — 53 tests pass *(2026-09-27)*, **~2.8s warm** wall clock, with test
      execution itself ~260ms. First run *(2026-09-22)*: 30 tests, 2.65s warm, 8.1s cold
      including .NET first-run setup. That first run compiled clean with Unity not
      installed on the machine at all, which proves the engine-free claim.
- [x] Record a stroke → `make pull-recordings` → `make promote` → `make test` against it
      *(2026-09-23, 2026-09-27)*. Seven real Quest 3 fixtures are committed: three
      standing arms-only takes and four from a rowing machine, each with a stroke count
      the rower made by hand.

---

## 8. The Play-Mode-on-headset experiment (optional, timeboxed)

Two routes exist for streaming Play Mode to the headset from Linux:

- **WiVRn** — native Linux OpenXR streamer, Quest client on the Meta Store, packaged for
  Arch/Fedora/Gentoo/NixOS and on Flathub. Supports hand tracking over the stream.
- **SteamVR + ALVR** — there is a working Unity 6 recipe at
  `Stridemann/Unity-XR-on-Linux-for-Meta-Quest`.

The catch: Unity ships an *Android* `libUnityOpenXR.so` even on desktop Linux, so it needs
bionic→glibc shims, an OpenXR API layer stripping Android extensions, and an `LD_PRELOAD`
JVM stub. The published recipe documents **controller input only**. Hand tracking through
that shim is unproven, and hand tracking is this project's entire input model.

Worth an afternoon *after* tiers 1–3 are working. Never worth blocking on.
