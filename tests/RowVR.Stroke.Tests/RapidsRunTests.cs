using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The rapids game (#30) and its comfort limits (#90).</summary>
    [TestFixture]
    public class RapidsRunTests
    {
        const double Dt = 1d / 72d;

        /// <summary>Speed and power, frame by frame, from rowing the steady rowing-machine take on loop.</summary>
        static List<(float Speed, float Power)> SteadyRowing()
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures",
                "real-erg-steady-12strokes-20260927.rowclip"));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var boat = new BoatModel();
            var frames = new List<(float, float)>();
            // The middle of the take, once up to speed: the stretch that loops cleanly.
            for (var t = source.StartTime; t <= source.EndTime; t += Dt)
            {
                if (source.TryGetPose(t, out var s)) boat.Update(t, detector.Update(t, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward));
                else boat.Glide(t);
                if (t - source.StartTime > 15 && t - source.StartTime < 40) frames.Add((boat.VisibleSpeed, boat.Power));
            }
            return frames;
        }

        static RapidsRun Run(Func<int, (float Speed, float Power)> input, float target = 110f, int maxFrames = 72 * 900)
        {
            var run = new RapidsRun(RapidsRun.Standard(target));
            var t = 0d;
            for (var i = 0; i < maxFrames && !run.Finished; i++)
            {
                t += Dt;
                var (speed, power) = input(i);
                run.Update(t, speed, power);
            }
            return run;
        }

        [Test]
        public void Steady_rowing_holds_the_line_through_every_rapid()
        {
            // 134 W average while rowing, against a 110 W run.
            var rowing = SteadyRowing();
            var run = Run(i => rowing[i % rowing.Count]);
            Assert.That(run.Finished, Is.True);
            Assert.That(run.Results.Count, Is.EqualTo(3));
            foreach (var r in run.Results) Assert.That(r.Clean, Is.GreaterThan(0.85f), "clean share of a rapid");
        }

        [Test]
        public void Easy_rowing_drifts_off_the_line_and_is_slower()
        {
            var rowing = SteadyRowing();
            var steady = Run(i => rowing[i % rowing.Count]);
            // The same strokes at a third of the power: well short of every target.
            var easy = Run(i => (rowing[i % rowing.Count].Speed * 0.7f, rowing[i % rowing.Count].Power / 3f));
            Assert.That(easy.Finished, Is.True);
            Assert.That(easy.Results[2].Clean, Is.LessThan(0.5f));
            Assert.That(easy.Elapsed, Is.GreaterThan(steady.Elapsed));
        }

        [Test]
        public void Sideways_drift_never_exceeds_the_comfort_limits()
        {
            // No power at all in the rapids: the worst case for drift.
            var run = new RapidsRun(RapidsRun.Standard());
            var t = 0d;
            var previous = 0f;
            var previousSpeed = 0f;
            for (var i = 0; i < 72 * 900 && !run.Finished; i++)
            {
                t += Dt;
                run.Update(t, 2.5f, 0f);
                var lateral = (run.Offset - previous) / (float)Dt;
                Assert.That(Math.Abs(lateral), Is.LessThanOrEqualTo(run.Settings.MaxDriftSpeed + 1e-3f), "drift speed");
                Assert.That(Math.Abs(lateral - previousSpeed), Is.LessThanOrEqualTo(run.Settings.MaxDriftAcceleration * (float)Dt + 1e-3f), "drift eases");
                Assert.That(Math.Abs(run.Offset), Is.LessThanOrEqualTo(run.Settings.MaxOffsetMetres + 1e-4f), "offset");
                previous = run.Offset;
                previousSpeed = lateral;
            }
            Assert.That(run.Finished, Is.True);
        }

        [Test]
        public void The_current_eases_between_sections()
        {
            var run = new RapidsRun(RapidsRun.Standard());
            var t = 0d;
            var previous = run.Current;
            for (var i = 0; i < 72 * 900 && !run.Finished; i++)
            {
                t += Dt;
                run.Update(t, 3f, 200f);
                Assert.That(Math.Abs(run.Current - previous), Is.LessThan(0.01f), "a step in the current");
                previous = run.Current;
            }
        }

        [Test]
        public void Pools_never_push_the_raft_off_the_line()
        {
            var run = new RapidsRun(new List<RiverSection> { new RiverSection(SectionKind.Pool, 300f, 0f, 0.3f, 1) });
            var t = 0d;
            for (var i = 0; i < 72 * 200 && !run.Finished; i++)
            {
                t += Dt;
                run.Update(t, 2f, 0f);
                Assert.That(run.Offset, Is.EqualTo(0f));
            }
            Assert.That(run.Finished, Is.True);
        }

        [Test]
        public void Nothing_starts_until_the_raft_moves_and_sections_come_in_order()
        {
            var run = new RapidsRun(RapidsRun.Standard());
            run.Update(0d, 0f, 0f);
            run.Update(1d, 0f, 0f);
            Assert.That(run.Started, Is.False);
            Assert.That(run.Elapsed, Is.EqualTo(0d));
            Assert.That(run.RapidCount, Is.EqualTo(3));

            var seen = new List<int>();
            var t = 1d;
            while (!run.Finished && t < 2000d)
            {
                t += Dt;
                run.Update(t, 3f, 200f);
                if (seen.Count == 0 || seen[seen.Count - 1] != run.SectionIndex) seen.Add(run.SectionIndex);
            }
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6 }));
            Assert.That(run.Distance, Is.GreaterThanOrEqualTo(run.TotalMetres));
        }
    }
}
