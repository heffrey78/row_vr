# row_vr — conventions

VR rowing app for Meta Quest 3. Hand tracking only, no controllers. Unity 6, OpenXR.
**Developed on Linux** — this is load-bearing, see below.

## Hard constraints — do not plan around tooling that does not exist here

Quest Link, Air Link, Meta XR Simulator and Meta Quest Developer Hub are **Windows/macOS
only**. None of them run on the dev machine. Never propose a workflow that depends on them.
Device interaction is raw `adb`; casting is `scrcpy`.

Apps created after 2026-03-01 must ship `targetSdkVersion 34`.

## Stack

Unity OpenXR + `com.unity.xr.hands` + XR Interaction Toolkit is the **primary** stack.
Rendering is **URP**, configured for Quest from code in `Assets/Editor/RenderPipelineBootstrap.cs`
(decision #40): no post-processing, no HDR, no depth texture. New shaders are URP-only and must
carry the single-pass-instanced stereo macros, or they render in one eye.
Meta XR Core SDK is layered in as OpenXR *features* (passthrough, wide-motion-mode hand
tracking, FFR) — **not** via `OVRCameraRig` / Building Blocks.

This is deliberate. The headset-free loop depends on the XR Interaction Simulator's Hands
Simulation, which is a Unity XRI feature. Meta's rig does not get it, and its substitute
(Meta XR Simulator) does not exist on Linux. Switching to `OVRCameraRig` would leave Linux
with no fast loop at all. Do not "simplify" toward `OVRCameraRig`.

## The rule that matters most

**Stroke detection stays engine-free.** `src/RowVR.Stroke/` targets netstandard2.1 and must
never reference `UnityEngine` — it has its own `Vec3`/`Pose` structs. Unity consumes it via
asmdef; Unity-specific code lives in `Assets/Scripts/Rowing/` as thin adapters.

This is what makes Tier 1 possible: tests run under plain `dotnet test`, with no Unity
install, no licence, and no headset, so they work in CI and in a cloud container.
If you find yourself wanting `UnityEngine.Vector3` in `src/`, add a conversion in the
adapter layer instead.

## Loops

- Tier 1 `make test` — engine-free, ~2s. Default here.
- Tier 2 Unity Play Mode, replaying a recorded take (ClipPlayback) or the XR Interaction
  Simulator — ~5s, no headset. `make preview` runs it headless and writes PNG stills
  (rower's eye and outside the boat) to `.devloop/preview/`, so the scene can be checked
  without anyone at the Editor. Needs a display; the Editor must not have the project open.
- Tier 3 `make dev` — IL2CPP arm64 APK, 2–8 min, needs the headset. Ground truth.

Prefer Tier 1. Reach for Tier 3 only for tracking fidelity, perf and comfort.

## Project tracking

Requirements, architecture decisions and tasks are GitHub issues in `heffrey78/row_vr`, kept through
the `gh-project` MCP server (`.mcp.json`), all in the `row_vr` project (a milestone). Start with
`get_status`. A `#N` in a code comment or doc is one of those issues.

- Record a design choice as a decision before building on it. Once Accepted it is not rewritten:
  amend it, or supersede it with a new one.
- Approving and validating a requirement are the owner's decisions. Ask before moving one to
  Approved or Validated.
- Finish a task with `set_status`, passing `commit` and `evidence`: what was actually run
  (`make test`, `make preview`, the headset), per the verification rule below.
- Records from the earlier lifecycle tracker keep their old ID (`REQ-…`, `ADR-…`, `TASK-…`) in a
  "Migrated from lifecycle" section, so older commit messages can still be traced.

## Unity project hygiene

- Serialization is **Force Text**, meta files **Visible**. Both are required for review and
  merge; do not change them.
- `.unity`/`.prefab`/`.asset` merge through UnityYAMLMerge (`.gitattributes`); binary assets
  go through Git LFS.
- Version pins live in `Packages/manifest.json` and `ProjectSettings/ProjectVersion.txt`.
  Bump them deliberately, in their own commit.
- `Assets/Editor/BuildQuest.cs` sets every Android/XR build setting in code, so a CLI build
  and a GUI build produce the same APK. Change build settings **there**, not only in the
  Editor UI, or the two will drift.

## Verification honesty

A cloud session has no Unity, no .NET and no Quest. Scripts can be `bash -n`'d and JSON
validated; C# under `Assets/` is reviewed, not compiled. Say which of those you actually did
— never report the loop as "working" from a container that cannot run it.
