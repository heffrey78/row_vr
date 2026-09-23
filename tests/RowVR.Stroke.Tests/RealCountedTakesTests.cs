using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Single-set takes with a stroke count the rower is confident of: 12 each, recorded on
    /// 2026-09-23 on a Quest 3 (firmware UP1A.231005.007.A1). Each take starts and ends at
    /// rest, so there is no cold-start artefact from slicing.
    ///
    /// Where the detector falls short of the truth, the tests are split in two. A passing
    /// test pins the current accuracy as a floor, so a regression fails `make test`. An
    /// ignored test states the target, to be switched on when tuning reaches it. The ranges
    /// run from "what the detector does now" to "what is true", so a fix never breaks the
    /// floor test.
    /// </summary>
    [TestFixture]
    public class RealCountedTakesTests
    {
        const int RowedStrokes = 12;

        // Seconds from the first sample. Steady: still until ~3s, rowing to ~28s, then still.
        const string Steady = "real-steady-12strokes-20260923.rowclip";
        const double SteadyRowingEnd = 28.5, SteadyAfterStart = 29.0;

        // Sloppy: rowing to ~32.5s. Hands then drop to the lap (tracking drops out for
        // 1.4s at ~35s), rest completely still until ~41s, and come up at ~42s - reaching
        // for the headset. That movement is counted as a stroke today.
        const string Sloppy = "real-sloppy-12strokes-20260923.rowclip";
        const double SloppyRowingEnd = 32.8, SloppyAfterStart = 33.5;

        struct Catch
        {
            public double Time;
            public float Rate;
        }

        static readonly Dictionary<string, List<Catch>> s_runs = new Dictionary<string, List<Catch>>();

        static List<Catch> Run(string fixture)
        {
            if (s_runs.TryGetValue(fixture, out var cached)) return cached;

            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture);
            Assert.That(File.Exists(path), Is.True, $"missing fixture {path}");

            var clip = HandClip.Load(path);
            var detector = new StrokeDetector();
            var t0 = clip.Samples[0].TimeSeconds;
            var catches = new List<Catch>();

            foreach (var s in clip.Samples)
            {
                var state = detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                if (state.Phase == StrokePhase.Catch)
                    catches.Add(new Catch { Time = s.TimeSeconds - t0, Rate = state.Metrics.StrokesPerMinute });
            }

            s_runs[fixture] = catches;
            return catches;
        }

        static List<Catch> Window(string fixture, double from, double to) =>
            Run(fixture).Where(c => c.Time >= from && c.Time < to).ToList();

        static string Describe(IEnumerable<Catch> catches) =>
            string.Join(", ", catches.Select(c => $"{c.Time:F1}s@{c.Rate:F1}"));

        // ---- floors: current accuracy, must not regress --------------------------------

        [TestCase(Steady, SteadyRowingEnd)]
        [TestCase(Sloppy, SloppyRowingEnd)]
        public void CountsAtLeastElevenOfTwelveStrokes(string fixture, double rowingEnd)
        {
            var rowing = Window(fixture, 0, rowingEnd);
            Assert.That(rowing.Count, Is.InRange(RowedStrokes - 1, RowedStrokes), Describe(rowing));
        }

        [Test]
        public void SteadyTakeHasNoStrokesAfterTheRowerStops()
        {
            var after = Window(Steady, SteadyAfterStart, double.MaxValue);
            Assert.That(after, Is.Empty, Describe(after));
        }

        [Test]
        public void SteadyTakeReadsAsASteadyRate()
        {
            // The first catch has no interval to measure yet.
            var rates = Window(Steady, 0, SteadyRowingEnd).Skip(1).Select(c => c.Rate).ToList();
            var median = rates.OrderBy(r => r).ElementAt(rates.Count / 2);

            Assert.That(median, Is.InRange(26f, 31f), string.Join(", ", rates));
            foreach (var rate in rates)
                Assert.That(rate, Is.EqualTo(median).Within(12).Percent, string.Join(", ", rates));
        }

        // ---- targets: known weaknesses, for tuning -----------------------------------

        [Test]
        [Ignore("Known weakness, a tuning target: the first stroke from rest is never counted. The " +
                "detector needs to observe the rower's range before it calls a catch, so 12 rowed " +
                "strokes read as 11. Also seen when a session is sliced mid-set.")]
        public void CountsTheFirstStrokeFromRest()
        {
            var rowing = Window(Steady, 0, SteadyRowingEnd);
            Assert.That(rowing.Count, Is.EqualTo(RowedStrokes), Describe(rowing));
        }

        [Test]
        [Ignore("Known weakness, a tuning target: after ~6s completely at rest, the rower's hands " +
                "rising from the lap (~42s) are counted as a stroke - 0.59m long, 5.2s recovery, " +
                "nothing like the strokes before it.")]
        public void HandsComingUpAfterStoppingAreNotAStroke()
        {
            var after = Window(Sloppy, SloppyAfterStart, double.MaxValue);
            Assert.That(after, Is.Empty, Describe(after));
        }
    }
}
