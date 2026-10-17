using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// When the room fades in (#28, #45), on the rowing-machine recordings, stepped at
    /// 72 Hz as on the headset. Times are seconds from the start of each take.
    /// </summary>
    [TestFixture]
    public class PassthroughPolicyTests
    {
        static List<(double Time, bool Wanted, float Fade)> Run(string fixture)
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var policy = new PassthroughPolicy();
            var trace = new List<(double, bool, float)>();
            var state = default(StrokeState);
            for (var t = source.StartTime; t <= source.EndTime; t += 1d / 72d)
            {
                var tracking = source.TryGetPose(t, out var s);
                if (tracking) state = detector.Update(t, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                policy.Update(t, tracking, tracking ? state : default, detector.HandsOff);
                trace.Add((t - source.StartTime, policy.Wanted, policy.Fade));
            }
            return trace;
        }

        static bool AnyWanted(List<(double Time, bool Wanted, float Fade)> trace, double from, double to)
        {
            foreach (var f in trace) if (f.Time >= from && f.Time < to && f.Wanted) return true;
            return false;
        }

        static bool WantedAt(List<(double Time, bool Wanted, float Fade)> trace, double at)
        {
            foreach (var f in trace) if (f.Time >= at) return f.Wanted;
            return false;
        }

        [TestCase("real-erg-steady-12strokes-20260927.rowclip", 2.0, 39.5)]
        [TestCase("real-erg-sloppy-12strokes-20260927.rowclip", 8.0, 53.5)]
        [TestCase("real-erg-release-types-12strokes-20260927.rowclip", 2.0, 13.0)]
        [TestCase("real-erg-release-types-12strokes-20260927.rowclip", 21.5, 48.5)]
        [TestCase("real-erg-stop-6-6-20260927.rowclip", 2.0, 20.5)]
        [TestCase("real-erg-stop-6-6-20260927.rowclip", 28.5, 44.5)]
        public void The_room_never_appears_while_rowing(string fixture, double from, double to)
        {
            // Including through the single-frame hands-off blips of grip changes mid-row
            // (release-types 43.3 and 45.8 s, sloppy 42.9 s), and sub-second tracking gaps.
            Assert.That(AnyWanted(Run(fixture), from, to), Is.False);
        }

        [TestCase("real-erg-steady-12strokes-20260927.rowclip", 41.0)]
        [TestCase("real-erg-sloppy-12strokes-20260927.rowclip", 54.5)]
        [TestCase("real-erg-release-types-12strokes-20260927.rowclip", 16.5)]
        [TestCase("real-erg-release-types-12strokes-20260927.rowclip", 50.0)]
        public void Letting_go_brings_the_room_in(string fixture, double by)
        {
            Assert.That(WantedAt(Run(fixture), by), Is.True);
        }

        [Test]
        public void It_stays_after_letting_go_until_rowing_resumes()
        {
            // After the final release the drifting handle can make the detector adopt a drive
            // with no catch; its finish must not bring the scene back.
            var steady = Run("real-erg-steady-12strokes-20260927.rowclip");
            Assert.That(WantedAt(steady, 43.5), Is.True);

            // Release A: in at the withdrawal, out at the first finish once rowing resumes.
            var releases = Run("real-erg-release-types-12strokes-20260927.rowclip");
            Assert.That(WantedAt(releases, 19.0), Is.True, "still off the handle");
            Assert.That(WantedAt(releases, 21.5), Is.False, "rowing again");
        }

        [Test]
        public void A_long_tracking_loss_while_stopped_brings_the_room_in()
        {
            // erg-stop: stopped at the finish, hands out of view 21.1-24.8 s.
            var trace = Run("real-erg-stop-6-6-20260927.rowclip");
            Assert.That(WantedAt(trace, 23.0), Is.True);
            Assert.That(WantedAt(trace, 28.5), Is.False, "back after the first stroke's finish");
        }

        [Test]
        public void Known_gap_fingers_opened_with_the_hands_still()
        {
            // Release B (~29-35 s): nothing moves, so nothing tells the detector. Documented in
            // PassthroughPolicy; if this starts failing, the gap has closed.
            Assert.That(AnyWanted(Run("real-erg-release-types-12strokes-20260927.rowclip"), 28.5, 35.5), Is.False);
        }

        [Test]
        public void The_fade_eases_and_never_jumps()
        {
            var trace = Run("real-erg-release-types-12strokes-20260927.rowclip");
            var policy = new PassthroughPolicy();
            var maxStep = 1f / 72f / System.Math.Min(policy.FadeInSeconds, policy.FadeOutSeconds) + 1e-4f;
            for (var i = 1; i < trace.Count; i++)
                Assert.That(System.Math.Abs(trace[i].Fade - trace[i - 1].Fade), Is.LessThanOrEqualTo(maxStep));
        }

        [Test]
        public void Dismissing_brings_the_scene_back_until_the_next_stroke()
        {
            var policy = new PassthroughPolicy();
            var t = 0d;
            var withdrawn = new StrokeState(StrokePhase.Unknown, 0f, 0f, 0f, default, true);
            policy.Update(t, true, withdrawn, false);
            for (var i = 0; i < 72; i++) policy.Update(t += 1d / 72d, true, default, false);
            Assert.That(policy.Wanted, Is.True, "let go: the room");

            policy.Dismiss();
            policy.Update(t += 1d / 72d, true, default, false);
            Assert.That(policy.Wanted, Is.False, "back to the scene");

            // Stays dismissed through more let-go signals and lost tracking while resting.
            policy.Update(t += 1d / 72d, true, withdrawn, true);
            for (var i = 0; i < 72 * 3; i++) policy.Update(t += 1d / 72d, false, default, false);
            Assert.That(policy.Wanted, Is.False);

            // A stroke re-arms it: the next let-go brings the room back.
            policy.Update(t += 1d / 72d, true, new StrokeState(StrokePhase.Catch, 0f, 0f, 1f, default), false);
            policy.Update(t += 1d / 72d, true, new StrokeState(StrokePhase.Finish, 0f, 0f, 0f, default), false);
            Assert.That(policy.Dismissed, Is.False);
            policy.Update(t += 1d / 72d, true, withdrawn, false);
            Assert.That(policy.Wanted, Is.True);
        }
    }
}
