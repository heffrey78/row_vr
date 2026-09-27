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
    /// STANDING, ARMS ONLY - not rowing-machine data. The rower stood and rowed with their
    /// arms: ~0.4-0.5m of handle travel, head roughly still, open hands, ~28 spm, drive and
    /// recovery roughly 1:1. The app's primary use is a rowing machine (REQ-0001-FUNC), where
    /// the stroke is a full leg-back-arm movement with the head sliding with the seat and the
    /// hands gripping a handle. These fixtures remain valid as regression tests of the
    /// detector's logic against real Quest 3 tracking noise; they are not evidence that it is
    /// tuned for machine rowing.
    ///
    /// Before the detector understood pauses, it counted 11 of 12 in both takes (the first
    /// stroke from rest was lost) and scored hands coming up off the lap after the sloppy
    /// take as a stroke - so the sloppy take's "12" was 11 real strokes plus a phantom.
    /// These tests hold the exact truth now that it is met.
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
        // for the headset.
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
            // Standing, arms only: no handle, so the hands' spacing wanders freely and the
            // grip check would read ordinary rowing as letting go. See HandleGripToleranceMetres.
            var detector = new StrokeDetector(new StrokeDetectorSettings { HandleGripToleranceMetres = 0f });
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

        [TestCase(Steady, SteadyRowingEnd)]
        [TestCase(Sloppy, SloppyRowingEnd)]
        public void CountsAllTwelveStrokesIncludingTheFirstFromRest(string fixture, double rowingEnd)
        {
            var rowing = Window(fixture, 0, rowingEnd);
            Assert.That(rowing.Count, Is.EqualTo(RowedStrokes), Describe(rowing));
        }

        [TestCase(Steady, SteadyAfterStart)]
        [TestCase(Sloppy, SloppyAfterStart)]
        public void NothingIsCountedAfterTheRowerStops(string fixture, double afterStart)
        {
            // The sloppy take is the hard one: hands drop to the lap, rest ~6s, then come up
            // to reach for the headset. The steady take ends with a reach for the headset too.
            var after = Window(fixture, afterStart, double.MaxValue);
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
    }
}
