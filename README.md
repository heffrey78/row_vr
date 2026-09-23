# row_vr

A rowing app for Meta Quest 3, driven by **hand tracking** (no controllers).
Unity 6, OpenXR, developed on **Linux**.

Status: **dev loop scaffolding.** No gameplay yet.

## Why this repo looks the way it does

Linux is a first-class constraint here, and it removes most of Meta's tooling:

| Tool | Linux? |
|---|---|
| Quest Link / Air Link | ❌ Windows only |
| Meta XR Simulator | ❌ Windows + Mac ARM only |
| Meta Quest Developer Hub | ❌ Windows + macOS only |
| Unity Editor + Android build | ✅ |
| `adb` (platform-tools) | ✅ |
| Unity XR Interaction Simulator (hands) | ✅ (pure editor) |

There is no in-editor play-on-device path on Linux, so the only ground-truth loop is
`build APK → adb install → run` — an IL2CPP/arm64 build every time, minutes not seconds.
Everything here is organised around not having to pay that cost very often.

## The three loops

| Tier | Command | Cycle | Headset | For |
|---|---|---|---|---|
| 1 | `make test` | ~2s | no | stroke phase logic, cadence, smoothing |
| 2 | Unity Play Mode + XR Interaction Simulator | ~5s | no | scene wiring, feedback, boat feel |
| 3 | `make dev` | ~1 min, or ~10 min if assemblies changed | yes | real tracking fidelity, perf, comfort |

Tier 1 is plain `dotnet test` against the stroke library — netstandard2.1, with **zero
`UnityEngine` dependency**, enforced by the asmdef rather than by convention. Stroke detection is signal processing; it has no business
needing an engine, let alone a headset. Record a real stroke on-device once, and iterate
against it forever with the headset on the shelf.

## Getting started

Read **[`docs/dev-loop.md`](docs/dev-loop.md)** — it covers Quest developer mode, the Linux
`udev` rules for `adb` (the usual first-hour blocker), Unity Hub modules, wireless `adb`,
and troubleshooting.

Then:

```sh
make preflight     # check adb, Unity, JDK, udev, device authorisation
make cast          # mirror the headset to your desktop (scrcpy; the MQDH replacement)
make dev           # build + install + launch + tail logs
make test          # tier 1: stroke detector tests, no Unity, no headset
```

## Layout

```
Assets/
  Editor/              BuildQuest.cs, ProjectBootstrap.cs — headless build, settings-as-code
  Scripts/Stroke/      engine-free stroke detection. Cannot reference UnityEngine.
  Scripts/Rowing/      Unity adapters: XR Hands -> the detector
  Scripts/Recording/   HandRecorder — writes .rowclip files on-device
  Tests/EditMode/      only tests that genuinely need Unity types
src/RowVR.Stroke/      csproj that compiles Assets/Scripts/Stroke with plain dotnet
tests/                 the fast test suite + committed .rowclip fixtures
tools/                 dev loop shell scripts
docs/dev-loop.md       the runbook — start here
```

The stroke sources live under `Assets/` because Unity must own them; `src/RowVR.Stroke/`
links the same files so `dotnet test` can build them with no Unity, no licence and no
headset. One source of truth, two compilers.
