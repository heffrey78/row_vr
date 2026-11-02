using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// The sky board (#99): its block lettering, and what it shows when. The owner found the
    /// card small and in the way (2026-11-01) and asked for big block numbers in the sky.
    /// </summary>
    [TestFixture]
    public class SkyNumbersTests
    {
        const string Digits = "0123456789";

        static string Pattern(char c)
        {
            var cells = "";
            for (var row = 0; row < BlockFont.Rows; row++)
                for (var column = 0; column < BlockFont.Width(c); column++)
                    cells += BlockFont.Filled(c, column, row) ? '#' : '.';
            return cells;
        }

        [Test]
        public void Every_digit_has_its_own_shape()
        {
            var seen = new HashSet<string>();
            foreach (var c in Digits)
            {
                Assert.That(BlockFont.Has(c), c.ToString());
                Assert.That(seen.Add(Pattern(c)), Is.True, $"{c} looks like another digit");
            }
        }

        [Test]
        public void Draws_everything_the_numbers_can_contain()
        {
            // Split, time, distance and rate, including the blanks shown at rest.
            var samples = new[]
            {
                MetricsFormat.Split(125.3f), MetricsFormat.Split(float.PositiveInfinity),
                MetricsFormat.Time(3725), MetricsFormat.Distance(12345.6), MetricsFormat.Rate(0f),
            };
            foreach (var text in samples)
                foreach (var c in text)
                    Assert.That(BlockFont.Has(c), Is.True, $"'{c}' in \"{text}\"");
        }

        [Test]
        public void A_thousands_comma_is_a_gap_not_a_point()
        {
            Assert.That(Pattern(','), Does.Not.Contain("#"));
            Assert.That(Pattern('.'), Does.Contain("#"));
        }

        [Test]
        public void Unknown_characters_are_spaces()
        {
            Assert.That(BlockFont.Has('m'), Is.False);
            Assert.That(Pattern('m'), Is.EqualTo(Pattern(' ')));
        }

        [Test]
        public void Measures_glyphs_and_the_gaps_between_them()
        {
            Assert.That(BlockFont.Measure(""), Is.EqualTo(0));
            Assert.That(BlockFont.Measure("8"), Is.EqualTo(3));
            // 3 + 1 + 1 (colon) + 1 + 3 + 1 + 3
            Assert.That(BlockFont.Measure("2:05"), Is.EqualTo(13));
        }

        static (SessionStats Stats, WorkoutProgress Workout, double End) Row(string fixture, Workout workout, double restAfter = 0d)
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var boat = new BoatModel();
            var stats = new SessionStats();
            var progress = new WorkoutProgress(workout);
            var t = source.StartTime;
            for (; t <= source.EndTime + restAfter; t += 1d / 72d)
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
                if (stats.Active) progress.Update(stats.ElapsedSeconds, stats.DistanceMetres);
            }
            return (stats, progress, t);
        }

        const string Steady = "real-erg-steady-12strokes-20260927.rowclip";

        [Test]
        public void Before_the_first_stroke_it_invites_one()
        {
            var readout = new SkyReadout();
            readout.Update(new SessionStats(), null, "just row");
            Assert.That(readout.Headline.Value, Is.EqualTo("-:--"));
            Assert.That(readout.Left.Value, Is.EqualTo("0:00"));
            Assert.That(readout.Footer, Does.Contain("just row").And.Contain("take a stroke"));
        }

        [Test]
        public void While_rowing_pace_is_the_headline_with_time_and_distance_beside_it()
        {
            var (stats, progress, _) = Row(Steady, Workout.JustRow());
            var readout = new SkyReadout();
            readout.Update(stats, progress, "just row");
            Assert.That(readout.Headline.Label, Is.EqualTo("/500m"));
            Assert.That(readout.Left.Label, Is.EqualTo("time"));
            Assert.That(readout.Right.Label, Is.EqualTo("metres"));
            Assert.That(readout.Left.Value, Is.EqualTo(MetricsFormat.Time(stats.ElapsedSeconds)));
            Assert.That(readout.Footer, Does.StartWith("rate"));
        }

        [Test]
        public void Live_distance_counts_in_tens_so_the_cloud_can_settle()
        {
            var (stats, progress, _) = Row(Steady, Workout.JustRow());
            var readout = new SkyReadout();
            readout.Update(stats, progress, "just row");
            var tens = Math.Floor(stats.DistanceMetres / 10d) * 10d;
            Assert.That(readout.Right.Value, Is.EqualTo(MetricsFormat.Distance(tens)));
            Assert.That(readout.Right.Value, Does.EndWith("0"));
        }

        [Test]
        public void A_timed_workout_counts_down_on_the_left()
        {
            var (stats, progress, _) = Row(Steady, Workout.Timed(20 * 60));
            var readout = new SkyReadout();
            readout.Update(stats, progress, "20:00");
            Assert.That(readout.Left.Label, Is.EqualTo("time left"));
            Assert.That(readout.Left.Value, Is.EqualTo(MetricsFormat.Time(progress.SecondsLeft)));
        }

        [Test]
        public void A_distance_workout_counts_down_on_the_right()
        {
            var (stats, progress, _) = Row(Steady, Workout.Distance(2000));
            var readout = new SkyReadout();
            readout.Update(stats, progress, "2,000 m");
            Assert.That(readout.Right.Label, Is.EqualTo("metres left"));
        }

        [Test]
        public void When_the_session_ends_it_shows_the_summary()
        {
            // A minute and more of rest ends the session.
            var (stats, progress, _) = Row(Steady, Workout.JustRow(), restAfter: 90d);
            Assume.That(stats.LastSummary.HasValue, "the session ended");
            var readout = new SkyReadout();
            readout.Update(stats, progress, "just row");
            Assert.That(readout.Headline.Label, Is.EqualTo("avg /500m"));
            Assert.That(readout.Left.Label, Is.EqualTo("session"));
            Assert.That(readout.Footer, Does.Contain("12 strokes"));
        }
    }
}
