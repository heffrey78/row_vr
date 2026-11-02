using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Boat speed from the stroke (#26, #41), run on the rowing-machine
    /// recordings through the default detector, as in the headset.
    /// </summary>
    [TestFixture]
    public class BoatModelTests
    {
        const string Steady = "real-erg-steady-12strokes-20260927.rowclip";
        const string Sloppy = "real-erg-sloppy-12strokes-20260927.rowclip";
        const string Stop = "real-erg-stop-6-6-20260927.rowclip";

        struct Run
        {
            public BoatModel Boat;
            public List<double> CatchTimes;
            public List<double> CatchDistances;
            public List<(double Time, float Visible)> Visible;
            public double EndTime;
        }

        /// <summary>
        /// Rows a fixture the way the headset does: 72 updates a second, the detector and the
        /// boat updated when there is a pose, and the boat gliding when tracking is lost.
        /// </summary>
        static Run Row(string fixture, BoatSettings settings = null)
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var run = new Run
            {
                Boat = new BoatModel(settings),
                CatchTimes = new List<double>(),
                CatchDistances = new List<double>(),
                Visible = new List<(double, float)>(),
            };
            for (var t = source.StartTime; t <= source.EndTime; t += 1d / 72d)
            {
                if (source.TryGetPose(t, out var s))
                {
                    var state = detector.Update(t, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                    run.Boat.Update(t, state);
                    if (state.Phase == StrokePhase.Catch)
                    {
                        run.CatchTimes.Add(t);
                        run.CatchDistances.Add(run.Boat.Distance);
                    }
                }
                else
                {
                    run.Boat.Glide(t);
                }
                run.Visible.Add((t, run.Boat.VisibleSpeed));
                run.EndTime = t;
            }
            return run;
        }

        /// <summary>Split of the stroke that starts at catch <paramref name="i"/>.</summary>
        static float StrokeSplit(Run run, int i) =>
            BoatModel.SplitSeconds((float)((run.CatchDistances[i + 1] - run.CatchDistances[i]) /
                                           (run.CatchTimes[i + 1] - run.CatchTimes[i])));

        [Test]
        public void Steady_rowing_settles_to_an_average_rowers_pace()
        {
            // About 21 spm with ~1.2 m strokes, at the default resistance. After the boat is
            // up to speed it should read like a moderate steady-state piece on a monitor.
            var run = Row(Steady);
            for (var i = 6; i < 11; i++)
                Assert.That(StrokeSplit(run, i), Is.InRange(125f, 155f), $"stroke {i + 1} split");
        }

        [Test]
        public void The_boat_gets_up_to_speed_within_a_few_strokes()
        {
            var run = Row(Steady);
            Assert.That(StrokeSplit(run, 0), Is.GreaterThan(StrokeSplit(run, 5)), "starting from rest is slower");
            Assert.That(StrokeSplit(run, 4), Is.LessThan(StrokeSplit(run, 9) * 1.15f), "within 15% of cruising by stroke 5");
        }

        [Test]
        public void Rowing_harder_and_faster_covers_more_ground()
        {
            // The sloppy take was rowed at ~14 spm with slower drives.
            Assert.That(Row(Steady).Boat.Distance, Is.GreaterThan(Row(Sloppy).Boat.Distance));
        }

        [Test]
        public void More_resistance_on_the_dial_means_more_speed_for_the_same_stroke()
        {
            var light = Row(Steady, new BoatSettings { ResistanceLevel = 2 });
            var heavy = Row(Steady, new BoatSettings { ResistanceLevel = 9 });
            Assert.That(heavy.Boat.Distance, Is.GreaterThan(light.Boat.Distance));
        }

        [Test]
        public void With_no_strokes_the_boat_glides_to_rest()
        {
            var run = Row(Steady);
            var t = run.EndTime;
            var speed = run.Boat.Speed;
            Assert.That(speed, Is.GreaterThan(1f));
            for (var i = 0; i < 90 * 72; i++)
            {
                t += 1d / 72d;
                run.Boat.Glide(t);
                Assert.That(run.Boat.Speed, Is.LessThanOrEqualTo(speed + 1e-5f), "gliding never speeds up");
                speed = run.Boat.Speed;
            }
            Assert.That(run.Boat.Speed, Is.LessThan(0.3f), "effectively at rest after 90 s");
        }

        [Test]
        public void Stopping_mid_piece_slows_the_boat_before_it_picks_up_again()
        {
            // erg-stop: 6 strokes, a stop around 20-25 s from the start, 6 more.
            var run = Row(Stop);
            var t0 = run.Visible[0].Time;
            float At(double seconds)
            {
                foreach (var (time, v) in run.Visible) if (time - t0 >= seconds) return v;
                return float.NaN;
            }
            Assert.That(At(24.5), Is.LessThan(At(19.0) * 0.8f), "slower during the stop");
            Assert.That(At(40.0), Is.GreaterThan(At(24.5)), "faster again once rowing resumes");
        }

        [Test]
        public void The_visible_speed_has_no_per_stroke_lurch()
        {
            // #41: within one steady stroke cycle the speed the world moves at varies by
            // under 5%. The true speed surges far more than that.
            var run = Row(Steady);
            for (var i = 6; i < 11; i++)
            {
                float lo = float.MaxValue, hi = 0f;
                foreach (var (time, v) in run.Visible)
                {
                    if (time < run.CatchTimes[i] || time >= run.CatchTimes[i + 1]) continue;
                    lo = System.Math.Min(lo, v);
                    hi = System.Math.Max(hi, v);
                }
                Assert.That((hi - lo) / hi, Is.LessThan(0.05f), $"visible speed swing in stroke {i + 1}");
            }
        }

        [Test]
        public void Split_is_seconds_per_500_metres()
        {
            Assert.That(BoatModel.SplitSeconds(500f / 140f), Is.EqualTo(140f).Within(0.01f));
            Assert.That(float.IsPositiveInfinity(BoatModel.SplitSeconds(0f)), Is.True);
        }
    }
}
