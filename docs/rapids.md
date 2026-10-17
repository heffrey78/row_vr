# Rapids: design sketch (#30, roadmap)

A second scene, gamified: a river run seen **facing forward**, powered by the rower's strokes,
through **rapid sections** with **calm pools** between them. It's an interval workout disguised
as a descent. This is a sketch to start from, not a decision; the comfort ADR comes first.

## What stays the same

Everything that is engine-free already fits:

| Piece | Reused as |
|---|---|
| `StrokeDetector` | unchanged: strokes from hand tracking |
| `BoatModel` | the raft's speed through the water, from the same effort proxy |
| `SessionStats`, `MetricsFormat` | per-run time, distance, strokes, pace |
| `CourseWrap` | river banks and pools wrap the same way lake scenery does |
| `SceneryLayers`, `RowVR/Illustrated`, `ScenePalette` | new palette (spray whites, slate rock, deep green) |
| `SoundSynth` | rushing water is `HullRush` louder and brighter; new: rapids roar, spray |

## What is new

- **Facing forward, on an oar raft.** The rower looks where the raft goes, sitting up on a
  frame over the tubes and rowing two oars in oarlocks on that frame, as on a whitewater oar
  rig (#90). An early build showed only the handle marker and sat the rower down inside the
  tubes; the owner asked for oars and a higher seat.
- **The line.** Each rapid has a good line and a rough one. Power *during the rapid* (average
  `BoatModel.Power` against a target) keeps the raft on the good line. Falling short drifts it
  toward rocks and waves: slower, a spray of water, lost points. **Never a crash or a jolt.**
  Steering can't come from the hands, which are on the handle. At most, a later option lets the
  rower's head lean bias the line.
- **Current.** Rapids add speed and pools remove it, so effort matters most where the water
  is flat.
- **Sections.** For example 3-6 rapids, each 45-90 s of hard work, each followed by a 60-90 s
  pool: an interval session. Lengths and targets come from a run definition, so "easy",
  "steady" and "sprint" runs are data, not code.
- **Scoring.** Time for the run, plus a "clean line" percentage per rapid. The summary card
  shows both, and the best run so far.

## Comfort first

#41 excluded lateral motion and bobbing for the lake. Rapids invite both, and forward
motion through narrow banks is strong vection. The first decision (#43) kept the raft
perfectly still; on the headset the rapids then had no effect, and the standing waves came up
through the raft's floor. Its replacement, #90, keeps the view still but lets the **raft ride
the waves**: it heaves, pitches and rolls over the crests the rower can see (RapidWaves and
RaftRocking, engine-free and tested), within 6 degrees of roll, 5 of pitch and 0.15 m of
heave, and settles still in the pools. A depth-only lid keeps the water out of the raft.
Rocking the view itself waits for a headset test of the raft alone.

The starting position, before the headset:

1. **No roll, no pitch, ever.** The horizon stays level, as on the lake. (Still true of the
   view; the raft now rocks, #90.)
2. **Forward speed is smoothed,** as in #41. Rapids add speed smoothly over seconds, not
   in surges.
3. **Drifting off the line is slow and lateral only,** at most about 0.5 m/s sideways,
   eased in and out, and it is the world that slides, not the view rotating.
4. **Water motion is shown, not felt:** waves and spray move around the raft, while the raft's
   own frame (the rower's reference) stays steady.
5. **A comfort vignette** darkens the periphery during rapids, if needed, as an option. Test
   without it first.
6. The first playtest is short (one rapid), on the machine, with the rower's verdict recorded
   before anything longer is built.

## Order of work

1. Comfort ADR (as above), then a tiny prototype: one straight rapid, one pool, forward-facing,
   placeholder art, measured on the headset for comfort.
2. An engine-free run model: sections, targets, line drift from power, score. Tested on the
   rowing-machine recordings, like the boat model.
3. River scenery in the illustrated style: narrow banks, rocks, standing waves, spray.
4. Sound: a rapids roar that rises and falls with the sections, and spray at the catch.
5. A scene switch between the lake and the rapids (a hands-only choice at the start; how to
   make a choice with no controllers is its own small design problem).
