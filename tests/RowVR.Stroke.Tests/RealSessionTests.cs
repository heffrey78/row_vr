using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Regression tests over real Quest 3 hand tracking: one continuous recording of four
    /// sets rowed back to back on 2026-09-23 (firmware UP1A.231005.007.A1).
    ///
    /// STANDING, ARMS ONLY - not rowing-machine data. The rower stood and rowed with their
    /// arms: ~0.4-0.5m of handle travel, head roughly still, open hands, ~28 spm, drive and
    /// recovery roughly 1:1. The app's primary use is a rowing machine (REQ-0001-FUNC), where
    /// the stroke is a full leg-back-arm movement with the head sliding with the seat and the
    /// hands gripping a handle. These fixtures remain valid as regression tests of the
    /// detector's logic against real Quest 3 tracking noise; they are not evidence that it is
    /// tuned for machine rowing.
    ///
    /// The clip is run through one detector end to end, exactly as it ran on the headset,
    /// and each assertion looks at a time window. It is deliberately NOT split into one
    /// fixture per set: a freshly started detector needs to observe some motion before it
    /// can call a catch, so every slice lost its first stroke - a cold start that never
    /// happens mid-session and would make the tests about slicing rather than detection.
    ///
    /// Ground truth, and how sure we are of it:
    /// - slow (8 strokes) and stop (5, freeze, 5): the rower's planned count and the
    ///   detector agree exactly, so these are asserted exactly.
    /// - steady and sloppy: the rower was unsure of the counts and the recording may have
    ///   started mid-set, so only loose bounds and behaviour (rate stability) are asserted.
    ///   Tightening them needs a recording with a count we trust, not a guess.
    ///
    /// Set boundaries were inferred from motion and catch timing, not marked live.
    /// </summary>
    [TestFixture]
    public class RealSessionTests
    {
        const string FixtureName = "real-session-4sets-20260923.rowclip";

        // Seconds from the first sample. See the clip's note for provenance.
        const double SteadyStart = 0.0, SteadyEnd = 23.8;
        const double SloppyStart = 23.8, SloppyEnd = 49.3;
        const double SlowStart = 49.3, SlowEnd = 77.0;
        const double StopStart = 77.0, FreezeStart = 90.5, FreezeEnd = 94.6, StopEnd = 106.0;

        struct Catch
        {
            public double Time;
            public StrokeMetrics Metrics;
        }

        static List<Catch> s_catches;

        [OneTimeSetUp]
        public void RunDetectorOverTheWholeSession()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", FixtureName);
            Assert.That(File.Exists(path), Is.True, $"missing fixture {path}");

            var clip = HandClip.Load(path);
            var detector = new StrokeDetector();
            var t0 = clip.Samples[0].TimeSeconds;

            s_catches = new List<Catch>();
            foreach (var s in clip.Samples)
            {
                var state = detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                if (state.Phase == StrokePhase.Catch)
                    s_catches.Add(new Catch { Time = s.TimeSeconds - t0, Metrics = state.Metrics });
            }
        }

        static List<Catch> In(double from, double to) =>
            s_catches.Where(c => c.Time >= from && c.Time < to).ToList();

        static string Describe(IEnumerable<Catch> catches) =>
            string.Join(", ", catches.Select(c => $"{c.Time:F1}s@{c.Metrics.StrokesPerMinute:F1}"));

        [Test]
        public void SlowSetCountsEightStrokes()
        {
            var slow = In(SlowStart, SlowEnd);
            Assert.That(slow.Count, Is.EqualTo(8), Describe(slow));
        }

        [Test]
        public void StopSetCountsFiveStrokesEitherSideOfTheFreeze()
        {
            var before = In(StopStart, FreezeStart);
            var after = In(FreezeEnd, StopEnd);

            Assert.That(before.Count, Is.EqualTo(5), "before the freeze: " + Describe(before));
            Assert.That(after.Count, Is.EqualTo(5), "after the freeze: " + Describe(after));
        }

        [Test]
        public void FreezingMidDriveProducesNoPhantomStroke()
        {
            // The point of the stop set. Tracking noise on a stationary handle is exactly
            // what a naive detector turns into a stroke.
            var frozen = In(FreezeStart, FreezeEnd);
            Assert.That(frozen, Is.Empty, "catches during the freeze: " + Describe(frozen));
        }

        [Test]
        public void SteadyRowingReadsAsASteadyRate()
        {
            // Skip the first catch (no interval to measure yet) and the last, which spans
            // the change of pace into the sloppy set.
            var steady = In(SteadyStart, SteadyEnd);
            var measured = steady.Skip(1).Take(steady.Count - 2).Select(c => c.Metrics.StrokesPerMinute).ToList();
            Assert.That(measured.Count, Is.GreaterThanOrEqualTo(6), Describe(steady));

            var median = measured.OrderBy(r => r).ElementAt(measured.Count / 2);
            Assert.That(median, Is.InRange(26f, 31f), Describe(steady));

            foreach (var rate in measured)
            {
                Assert.That(rate, Is.EqualTo(median).Within(12).Percent,
                    $"rate {rate:F1} is more than 12% from the median {median:F1}: {Describe(steady)}");
            }
        }

        [Test]
        public void SteadyAndSloppyCountsAreInAPlausibleRange()
        {
            // Loose on purpose - see the class comment. 15 strokes were planned for each.
            Assert.That(In(SteadyStart, SteadyEnd).Count, Is.InRange(10, 15));
            Assert.That(In(SloppyStart, SloppyEnd).Count, Is.InRange(12, 16));
        }

        [Test]
        public void RateAfterAPauseIgnoresThePause()
        {
            // The interval across the freeze is not measured, so the first stroke after it
            // carries the rate from before the pause (~27) instead of one that includes the
            // stop (~11).
            var first = In(FreezeEnd, StopEnd).First();
            Assert.That(first.Metrics.StrokesPerMinute, Is.GreaterThan(18f), Describe(In(StopStart, StopEnd)));
        }
    }
}
