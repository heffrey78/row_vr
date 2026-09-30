using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The rower-facing numbers (REQ-0006-FUNC), on the rowing-machine recordings.</summary>
    [TestFixture]
    public class SessionStatsTests
    {
        const string Steady = "real-erg-steady-12strokes-20260927.rowclip";
        const string Releases = "real-erg-release-types-12strokes-20260927.rowclip";

        static (SessionStats Stats, BoatModel Boat, double End) Row(string fixture)
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var boat = new BoatModel();
            var stats = new SessionStats();
            var t = source.StartTime;
            for (; t <= source.EndTime; t += 1d / 72d)
            {
                if (source.TryGetPose(t, out var s))
                {
                    var state = detector.Update(t, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                    boat.Update(t, state);
                    stats.Update(t, state, boat.Distance);
                }
                else
                {
                    boat.Glide(t);
                    stats.Tick(t, boat.Distance);
                }
            }
            return (stats, boat, t);
        }

        [Test]
        public void Counts_the_rowers_strokes_net_of_letting_go()
        {
            // Both takes were counted as 12 by the rower; each ends with a withdrawn release.
            Assert.That(Row(Steady).Stats.Strokes, Is.EqualTo(12));
            Assert.That(Row(Releases).Stats.Strokes, Is.EqualTo(12));
        }

        [Test]
        public void Reads_like_a_monitor_while_rowing()
        {
            var (stats, boat, _) = Row(Steady);
            Assert.That(stats.Active, Is.True);
            Assert.That(stats.ElapsedSeconds, Is.InRange(30d, 45d), "clock runs from the first catch");
            Assert.That(stats.DistanceMetres, Is.GreaterThan(80d).And.LessThanOrEqualTo(boat.Distance));
            Assert.That(stats.AverageSplitSeconds, Is.InRange(120f, 200f));
        }

        [Test]
        public void Rate_and_split_blank_while_resting_and_the_session_ends_after_a_minute()
        {
            var (stats, boat, t) = Row(Steady);
            for (var i = 0; i < 10 * 72; i++) { t += 1d / 72d; boat.Glide(t); stats.Tick(t, boat.Distance); }
            Assert.That(stats.Resting, Is.True);
            Assert.That(stats.Rate, Is.EqualTo(0f));
            Assert.That(float.IsPositiveInfinity(stats.StrokeSplitSeconds), Is.True);
            Assert.That(stats.Active, Is.True, "a short rest keeps the session going");

            for (var i = 0; i < 60 * 72; i++) { t += 1d / 72d; boat.Glide(t); stats.Tick(t, boat.Distance); }
            Assert.That(stats.Active, Is.False);
            Assert.That(stats.LastSummary.HasValue, Is.True);
            var summary = stats.LastSummary.Value;
            Assert.That(summary.Strokes, Is.EqualTo(12));
            Assert.That(summary.ElapsedSeconds, Is.LessThan(45d), "the rest before timing out is not counted");
            Assert.That(summary.AverageRate, Is.InRange(17f, 24f));
        }

        [Test]
        public void Nothing_counts_before_the_first_catch()
        {
            var stats = new SessionStats();
            stats.Tick(10d, 5d);
            Assert.That(stats.Active, Is.False);
            Assert.That(stats.ElapsedSeconds, Is.EqualTo(0d));
            Assert.That(stats.Strokes, Is.EqualTo(0));
        }
    }
}
