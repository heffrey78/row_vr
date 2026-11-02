using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// The oars (#25) run on the detector's output, so these replay the
    /// rowing-machine recordings through the default detector and check the oar pose frame
    /// by frame. Anything the oars do wrong here, the rower would see in the headset.
    /// </summary>
    [TestFixture]
    public class OarKinematicsTests
    {
        const string Releases = "real-erg-release-types-12strokes-20260927.rowclip";

        static readonly string[] ErgFixtures =
        {
            "real-erg-steady-12strokes-20260927.rowclip",
            "real-erg-sloppy-12strokes-20260927.rowclip",
            "real-erg-stop-6-6-20260927.rowclip",
            Releases,
        };

        struct Frame
        {
            public double Time;
            public StrokeState State;
            public OarPose Pose;
        }

        static List<Frame> Replay(string fixture, OarSettings settings)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture);
            Assert.That(File.Exists(path), Is.True, $"missing fixture {path}");

            var clip = HandClip.Load(path);
            var detector = new StrokeDetector();
            var oars = new OarKinematics(settings);
            var frames = new List<Frame>();
            foreach (var s in clip.Samples)
            {
                var state = detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                frames.Add(new Frame { Time = s.TimeSeconds, State = state, Pose = oars.Update(s.TimeSeconds, state) });
            }
            return frames;
        }

        static bool BladeIn(StrokePhase p) => p == StrokePhase.Catch || p == StrokePhase.Drive;

        [TestCaseSource(nameof(ErgFixtures))]
        public void Blade_is_in_the_water_only_during_the_drive(string fixture)
        {
            var settings = new OarSettings();
            var lastBladeIn = double.NegativeInfinity;

            foreach (var f in Replay(fixture, settings))
            {
                if (BladeIn(f.State.Phase)) lastBladeIn = f.Time;
                if (f.Pose.BladeDepth <= 0f) continue;

                // Lifting takes BladeTransitionSeconds, so a blade still partly in the water
                // just after the finish is correct. Anything later is a blade dragging
                // through the recovery.
                Assert.That(f.Time - lastBladeIn, Is.LessThanOrEqualTo(settings.BladeTransitionSeconds + 1e-6),
                    $"{fixture}: blade at depth {f.Pose.BladeDepth:F2} in {f.State.Phase} at t={f.Time:F2}");
            }
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void Blade_is_fully_buried_in_every_drive(string fixture)
        {
            var settings = new OarSettings();
            var drives = 0;
            var driveStart = double.NaN;
            var maxDepth = 0f;

            foreach (var f in Replay(fixture, settings))
            {
                var inDrive = BladeIn(f.State.Phase);
                if (inDrive && double.IsNaN(driveStart)) { driveStart = f.Time; maxDepth = 0f; }
                if (inDrive) { maxDepth = System.Math.Max(maxDepth, f.Pose.BladeDepth); continue; }
                if (double.IsNaN(driveStart)) continue;

                // Only drives long enough for the blade to get all the way in. A withdrawn
                // catch can cut a "drive" short, and that is not a stroke anyway.
                if (f.Time - driveStart > settings.BladeTransitionSeconds * 2)
                {
                    drives++;
                    Assert.That(maxDepth, Is.EqualTo(1f), $"{fixture}: drive at t={driveStart:F2} never buried the blade");
                }
                driveStart = double.NaN;
            }

            Assert.That(drives, Is.GreaterThanOrEqualTo(10), $"{fixture}: expected most of the 12 strokes to be checked");
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void A_buried_blade_never_moves_toward_the_catch(string fixture)
        {
            // A blade in the water travelling toward the catch is the one oar motion that is
            // physically impossible, and the easiest for the rower to spot. The hand-tracking
            // wobble allowance is per frame.
            const float noiseDegrees = 1.5f;
            const double gapSeconds = 0.1;
            Frame? previous = null;
            var checkedFrames = 0;

            foreach (var f in Replay(fixture, new OarSettings()))
            {
                var buried = f.Pose.BladeDepth >= 0.5f;
                if (buried && previous.HasValue && previous.Value.Pose.BladeDepth >= 0.5f
                    && f.Time - previous.Value.Time <= gapSeconds)
                {
                    checkedFrames++;
                    Assert.That(f.Pose.SweepDegrees, Is.LessThanOrEqualTo(previous.Value.Pose.SweepDegrees + noiseDegrees),
                        $"{fixture}: buried blade moved toward the catch, {previous.Value.Pose.SweepDegrees:F1} to " +
                        $"{f.Pose.SweepDegrees:F1} deg at t={f.Time:F2} ({f.State.Phase}, v={f.State.HandleVelocity:F2})");
                }
                previous = f;
            }

            Assert.That(checkedFrames, Is.GreaterThan(100), $"{fixture}: too few buried frames to mean anything");
        }

        [Test]
        public void Blade_stays_out_while_a_premature_catch_leaves_the_detector_in_drive()
        {
            // erg-sloppy: a catch is called at 187.14 s with the hands low at the catch, tracking
            // drops until 188.03 s, and the hands come back still heading for the catch. The
            // detector stays in Drive until the real drive at ~190 s (#67); the blade must
            // stay out until then.
            var frames = Replay("real-erg-sloppy-12strokes-20260927.rowclip", new OarSettings());
            var sawFalseDrive = false;

            foreach (var f in frames)
            {
                if (f.Time < 188.2 || f.Time > 189.8) continue;
                if (f.State.Phase == StrokePhase.Drive) sawFalseDrive = true;
                Assert.That(f.Pose.BladeDepth, Is.EqualTo(0f), $"blade in the water at {f.Time:F2} during the missed-finish recovery");
            }

            Assert.That(sawFalseDrive, Is.True, "fixture no longer shows the premature catch; this test needs a new case");
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void Oars_are_at_rest_once_no_stroke_is_tracked(string fixture)
        {
            var settings = new OarSettings();
            var settle = settings.RestSeconds + settings.BladeTransitionSeconds;
            var unknownSince = double.NaN;
            var checkedFrames = 0;

            foreach (var f in Replay(fixture, settings))
            {
                if (f.State.Phase != StrokePhase.Unknown) { unknownSince = double.NaN; continue; }
                if (double.IsNaN(unknownSince)) unknownSince = f.Time;
                Assert.That(f.Pose.Resting, Is.True);

                if (f.Time - unknownSince < settle + 1e-6) continue;
                checkedFrames++;
                Assert.That(f.Pose.SweepDegrees, Is.EqualTo(settings.RestAngleDegrees),
                    $"{fixture}: oar not square {f.Time - unknownSince:F2}s after the stroke stopped being tracked");
                Assert.That(f.Pose.BladeDepth, Is.EqualTo(0f));
            }

            // Every take starts and ends off the handle, so there is always rest to check.
            Assert.That(checkedFrames, Is.GreaterThan(0), $"{fixture}: no settled rest found");
        }

        [Test]
        public void A_withdrawn_catch_returns_the_oars_to_rest()
        {
            var settings = new OarSettings();
            var settle = settings.RestSeconds + settings.BladeTransitionSeconds;
            var frames = Replay(Releases, settings);
            var withdrawals = 0;
            var settledCount = 0;

            for (var i = 0; i < frames.Count; i++)
            {
                if (!frames[i].State.CatchWithdrawn) continue;
                withdrawals++;

                // Follow the frames after the withdrawal while no stroke is tracked. The oars
                // must head for rest the whole time: the blade only rises, then the sweep only
                // closes on square and never overshoots it. If the rower stays off the handle
                // long enough, they must arrive.
                var t0 = frames[i].Time;
                var prev = frames[i].Pose;
                for (var j = i + 1; j < frames.Count && frames[j].State.Phase == StrokePhase.Unknown; j++)
                {
                    var pose = frames[j].Pose;
                    Assert.That(pose.Resting, Is.True);
                    Assert.That(pose.BladeDepth, Is.LessThanOrEqualTo(prev.BladeDepth), $"blade went back in at t={frames[j].Time:F2}");
                    Assert.That(System.Math.Abs(pose.SweepDegrees - settings.RestAngleDegrees),
                        Is.LessThanOrEqualTo(System.Math.Abs(prev.SweepDegrees - settings.RestAngleDegrees) + 1e-4f),
                        $"oar moved away from square at t={frames[j].Time:F2}");
                    prev = pose;

                    if (frames[j].Time - t0 < settle + 1e-6) continue;
                    Assert.That(pose.SweepDegrees, Is.EqualTo(settings.RestAngleDegrees));
                    Assert.That(pose.BladeDepth, Is.EqualTo(0f));
                    settledCount++;
                    break;
                }
            }

            // Release A and the final release are withdrawn (RealErgTests); release B produces
            // no catch at all, so there is nothing to withdraw. At least one withdrawal is
            // followed by a long enough rest to prove the oars get all the way to square.
            Assert.That(withdrawals, Is.EqualTo(2));
            Assert.That(settledCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Oars_start_square_with_the_blade_out()
        {
            var oars = new OarKinematics();
            var pose = oars.Update(0d, default);
            Assert.That(pose.SweepDegrees, Is.EqualTo(0f));
            Assert.That(pose.BladeDepth, Is.EqualTo(0f));
            Assert.That(pose.Resting, Is.True);
        }

        [Test]
        public void Sweep_maps_catch_and_finish_to_their_angles()
        {
            var settings = new OarSettings();
            var oars = new OarKinematics(settings);
            var metrics = default(StrokeMetrics);

            var atCatch = oars.Update(0d, new StrokeState(StrokePhase.Catch, 0.5f, 0f, 1f, metrics, false));
            var atFinish = oars.Update(1d, new StrokeState(StrokePhase.Finish, 0f, 0f, 0f, metrics, false));

            Assert.That(atCatch.SweepDegrees, Is.EqualTo(settings.CatchAngleDegrees));
            Assert.That(atFinish.SweepDegrees, Is.EqualTo(settings.FinishAngleDegrees));
        }
    }
}
