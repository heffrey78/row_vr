# row_vr

A rowing app for Meta Quest 3, driven by **hand tracking** (no controllers).
Unity 6, OpenXR, developed on **Linux**.

![Rowing on the illustrated lake](docs/media/row.gif)

*Six seconds from the rower's eye, rendered headless with `make preview GIF=1` while replaying a
real rowing-machine recording (`real-erg-steady-12strokes-20260927`). It is not a capture
from the headset.*

Status: **a rowing app for the developer's own machine.** Verified on a Quest 3 on the machine:
hand tracking counts strokes, two oars sweep in the rower's hands, sounds mark the catch and
finish, and (seen by the rower on 2026-09-30) the illustrated lake, the scull and the
hand-lettered card with time, pace and distance. Speed comes from a boat model calibrated on
real recordings. The ambient soundscape is built but not yet judged in the headset. **Built but not yet tried
in the headset:** a forward-facing rapids scene (REQ-0007) and a hands-only menu for workouts,
resistance and scene (REQ-0008), both checked in headless renders and an Android build.

## Rowing with it

Put the headset on, sit at the machine, take the handle, and row. The first stroke starts the
session; the card to your right shows time, pace per 500 m, distance, rate and strokes.

**The menu.** Let go of the handle and rest for a few seconds: a card appears low in front of
you. Poke its buttons with a fingertip to choose:

- **workout**: just row, a 20:00 piece, 2,000 m, or 4 x 3:00 intervals with 1:00 rest;
- **resistance**: match your machine's dial (1-10), so the pace means something;
- **scene**: the lake, or the rapids.

The card disappears at your first stroke and cannot be pressed while you row. Choices are
remembered.

**The rapids.** Facing downstream, three rapids with pools between. In a rapid, keep your
power at or above the target on the card to hold the good line; fall short and you drift
into the rocks and rough water, which costs speed. Each rapid scores the share of it you
spent on the line.

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
