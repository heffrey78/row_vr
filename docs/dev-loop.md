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
sudo pacman -S android-tools                  # Arch
sudo dnf install android-tools                # Fedora
```

### 1.3 Host: udev rules ← **the usual first-hour blocker**

Without this, `adb devices` shows your headset as `no permissions` and every guide you find
will tell you to do something unrelated.

```sh
echo 'SUBSYSTEM=="usb", ATTR{idVendor}=="2833", MODE="0666", GROUP="plugdev"' \
  | sudo tee /etc/udev/rules.d/51-oculus.rules
sudo udevadm control --reload-rules && sudo udevadm trigger
sudo usermod -aG plugdev "$USER"      # log out and back in for this to take effect
adb kill-server && adb devices
```

`2833` is Meta/Oculus. You should see your serial and the word `device`.

### 1.4 Host: Unity

Install **Unity Hub**, then the Editor version this project is pinned to — read it from
`ProjectSettings/ProjectVersion.txt` rather than assuming, because that is the version the
build scripts look for.

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
sudo apt install scrcpy          # headset casting; the MQDH replacement
sudo apt install dotnet-sdk-8.0  # tier 1 tests. Not optional — this is the fast loop.
git lfs install                  # binary assets go through LFS
```

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

`make bootstrap` applies the Quest configuration from `Assets/Editor/ProjectBootstrap.cs`
(IL2CPP, ARM64, Vulkan, Linear, targetSdk 34, text serialization, 4x MSAA). It prints a
short list of things it cannot set reliably from code — mostly XR Plug-in Management
checkboxes — with the exact UI path for each. Do those once.

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

Press Play with the **XR Interaction Simulator** in the scene (`GameObject > XR > XR
Interaction Simulator`). It simulates *hands*, not just controllers, which is what makes it
a real substitute here given Meta XR Simulator does not run on Linux.

For scene wiring, UI, audio, feedback and boat feel. Good enough to iterate on how the thing
*reads*; not good enough to judge real tracking.

Play Mode is configured to skip domain and scene reload, which is most of the speed. The
cost: statics are not reset between runs, so anything static needs
`[RuntimeInitializeOnLoadMethod]` to reset itself.

### Tier 3 — `make dev` (2–8 min, headset)

```sh
make connect     # once per session, if you want to be free of the cable
make dev         # build → install → launch → tail logs
```

Ground truth: real hand-tracking fidelity, real performance, real comfort. Nothing else
substitutes, and nothing else should be used for anything a lower tier can answer.

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

---

## 4. Casting

```sh
make cast
```

The headset renders both eyes side by side with heavy lens distortion, so an uncropped
mirror looks wrong. Run it once uncropped, read the source resolution scrcpy prints, then
crop to roughly the left half:

```sh
QUEST_CROP=1600:900:0:0 make cast
MAX_SIZE=1024 make cast        # if the stream is choppy over Wi-Fi
```

Put the value in `tools/local.env` once you have one that suits your headset's current
render resolution.

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

Nothing below has been run on real hardware — it was written in a container with no Unity,
no Android SDK and no headset. Work through it in order; each step tells you which layer is
broken if it fails.

- [ ] `make preflight` — all green
- [ ] Unity opens the project without errors
- [ ] `make bootstrap` — settings applied; do the manual XR steps it lists
- [ ] `make cast` — you can see the headset view
- [ ] `make dev` — builds, installs, launches, logs stream. **Note the wall-clock time**; it
      sets your expectations for tier 3 forever
- [ ] Play Mode with the XR Interaction Simulator — simulated hands drive the scene
- [ ] `make test` — 30 tests pass
- [ ] Record a stroke → `make pull-recordings` → `make promote` → `make test` against it

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
