using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Rowing-machine recordings - the app's primary use (REQ-0001-FUNC). Three takes from
    /// 2026-09-27 on a Quest 3 (firmware UP1A.231005.007.A1), hands gripping the handle, each
    /// starting and ending off the handle. Counts are the rower's own.
    ///
    /// The detector runs with default settings, which is the machine profile: the grip check
    /// is on. A catch the detector later withdraws (<see cref="StrokeState.CatchWithdrawn"/>)
    /// does not count, exactly as the in-headset readout treats it.
    ///
    /// Before the grip and seat-slide logic these read 13 / 12 / 13: letting go of the handle
    /// and drawing it in to stop were counted as strokes, and a tracking dropout during the
    /// stop merged the next stroke into the last at 6.5 spm.
    /// </summary>
    [TestFixture]
    public class RealErgTests
    {
        const string Steady = "real-erg-steady-12strokes-20260927.rowclip";
        const string Sloppy = "real-erg-sloppy-12strokes-20260927.rowclip";
        const string Stop = "real-erg-stop-6-6-20260927.rowclip";

        // erg-stop: 6 strokes, a stop at the finish from ~20s (tracking lost 21.1-24.9s), 6 more.
        const double StopBoundary = 22.0;

        struct Event
        {
            public double Time;
            public bool Withdrawn;
            public float Rate;
        }

        static readonly Dictionary<string, List<Event>> s_runs = new Dictionary<string, List<Event>>();

        static List<Event> Run(string fixture)
        {
            if (s_runs.TryGetValue(fixture, out var cached)) return cached;

            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture);
            Assert.That(File.Exists(path), Is.True, $"missing fixture {path}");

            var clip = HandClip.Load(path);
            var detector = new StrokeDetector();
            var t0 = clip.Samples[0].TimeSeconds;
            var events = new List<Event>();
            var lastCatch = double.NaN;

            foreach (var s in clip.Samples)
            {
                var state = detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                var t = s.TimeSeconds - t0;
                if (state.Phase == StrokePhase.Catch)
                {
                    events.Add(new Event { Time = t, Rate = state.Metrics.StrokesPerMinute });
                    lastCatch = t;
                }

                // Credited to the catch it takes back, not to when the decision landed: a
                // withdrawal can arrive seconds later (after a tracking gap, say), and windowing
                // by decision time would split a catch from its own withdrawal.
                if (state.CatchWithdrawn)
                    events.Add(new Event { Time = lastCatch, Withdrawn = true });
            }

            s_runs[fixture] = events;
            return events;
        }

        /// <summary>Catches minus withdrawals - what the rower's count should equal.</summary>
        static int Net(IEnumerable<Event> events) => events.Sum(e => e.Withdrawn ? -1 : 1);

        static IEnumerable<Event> Window(string fixture, double from, double to) =>
            Run(fixture).Where(e => e.Time >= from && e.Time < to);

        static string Describe(IEnumerable<Event> events) =>
            string.Join(", ", events.Select(e => e.Withdrawn ? $"[withdrawn {e.Time:F1}s]" : $"{e.Time:F1}s@{e.Rate:F1}"));

        [TestCase(Steady)]
        [TestCase(Sloppy)]
        [TestCase(Stop)]
        public void CountsTwelveStrokes(string fixture)
        {
            Assert.That(Net(Run(fixture)), Is.EqualTo(12), Describe(Run(fixture)));
        }

        [Test]
        public void LettingGoOfTheHandleIsWithdrawn()
        {
            // Both takes end with a catch fired by the cord pulling the handle back as the
            // rower let go - and both must take it back.
            Assert.That(Run(Steady).Count(e => e.Withdrawn), Is.EqualTo(1), Describe(Run(Steady)));
            Assert.That(Window(Stop, StopBoundary, double.MaxValue).Count(e => e.Withdrawn), Is.EqualTo(1),
                Describe(Run(Stop)));
        }

        [Test]
        public void AGripChangeDuringARealStrokeIsNotWithdrawn()
        {
            // The sloppy take's last stroke was rowed with the hands drawn close together -
            // the grip broke right after the catch, but the seat slid back as in any real drive.
            Assert.That(Run(Sloppy).Count(e => e.Withdrawn), Is.EqualTo(0), Describe(Run(Sloppy)));
        }

        [Test]
        public void StopTakeCountsSixEitherSideOfTheStop()
        {
            var before = Window(Stop, 0, StopBoundary).ToList();
            var after = Window(Stop, StopBoundary, double.MaxValue).ToList();
            Assert.That(Net(before), Is.EqualTo(6), "before: " + Describe(before));
            Assert.That(Net(after), Is.EqualTo(6), "after: " + Describe(after));
        }

        [Test]
        public void RateAfterTheStopIsNotStretchedByIt()
        {
            // Tracking was lost for 3.8s while stopped at the finish. Treated as a pause, the
            // first stroke after it keeps a rowing rate instead of ~6.5 spm.
            var first = Window(Stop, StopBoundary, double.MaxValue).First(e => !e.Withdrawn);
            Assert.That(first.Rate, Is.GreaterThan(15f), Describe(Run(Stop)));
        }

        [Test]
        public void SteadyTakeReadsAsASteadyRate()
        {
            // The catches that stood: every catch except those later withdrawn.
            var withdrawnAt = new HashSet<double>(Run(Steady).Where(e => e.Withdrawn).Select(e => e.Time));
            var rates = Run(Steady).Where(e => !e.Withdrawn && !withdrawnAt.Contains(e.Time))
                                   .Select(e => e.Rate).ToList();

            // Skip the first catch (no interval yet) and two warm-up strokes: the rower built
            // from 17 to ~21 spm before settling, which is rowing, not detector noise.
            var settled = rates.Skip(3).ToList();
            var median = settled.OrderBy(r => r).ElementAt(settled.Count / 2);

            Assert.That(median, Is.InRange(17f, 23f), string.Join(", ", rates));
            foreach (var rate in settled)
                Assert.That(rate, Is.EqualTo(median).Within(10).Percent, string.Join(", ", rates));
        }
    }
}
