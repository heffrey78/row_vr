using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The raft riding the rapids' waves (#90), and the waves it rides.</summary>
    [TestFixture]
    public class RaftRockingTests
    {
        const double Dt = 1d / 72d;
        const float HalfWidth = 14f;

        /// <summary>Speed and power, frame by frame, from rowing the steady rowing-machine take on loop.</summary>
        static List<(float Speed, float Power)> SteadyRowing()
        {
            var clip = HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures",
                "real-erg-steady-12strokes-20260927.rowclip"));
            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };
            var detector = new StrokeDetector();
            var boat = new BoatModel();
            var frames = new List<(float, float)>();
            for (var t = source.StartTime; t <= source.EndTime; t += Dt)
            {
                if (source.TryGetPose(t, out var s)) boat.Update(t, detector.Update(t, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward));
                else boat.Glide(t);
                if (t - source.StartTime > 15 && t - source.StartTime < 40) frames.Add((boat.VisibleSpeed, boat.Power));
            }
            return frames;
        }

        sealed class Frame
        {
            public float Heave, Pitch, Roll, Rate;
            public bool InRapid;
            public double SecondsIntoSection;
        }

        /// <summary>A whole standard run, the raft rocking on the river's waves, frame by frame.</summary>
        static List<Frame> Ride(Func<int, (float Speed, float Power)> input, RaftRockingSettings settings = null)
        {
            var sections = RapidsRun.Standard();
            var run = new RapidsRun(sections);
            var waves = new RiverWaves(sections, HalfWidth);
            var raft = new RaftRocking(settings);
            var frames = new List<Frame>();
            var t = 0d;
            var section = -1;
            var sectionStart = 0d;
            for (var i = 0; i < 72 * 900 && !run.Finished; i++)
            {
                t += Dt;
                var (speed, power) = input(i);
                run.Update(t, speed, power);
                raft.Update(t, (across, along) => waves.Height(run.Offset + across, run.Distance + along));
                if (run.SectionIndex != section) { section = run.SectionIndex; sectionStart = t; }
                frames.Add(new Frame
                {
                    Heave = raft.Heave, Pitch = raft.Pitch, Roll = raft.Roll, Rate = raft.AngularRate,
                    InRapid = run.InRapid, SecondsIntoSection = t - sectionStart,
                });
            }
            Assert.That(run.Finished, Is.True, "the run finished");
            return frames;
        }

        static float Motion(IEnumerable<Frame> frames)
        {
            double sum = 0d;
            var n = 0;
            foreach (var f in frames) { sum += f.Pitch * f.Pitch + f.Roll * f.Roll; n++; }
            return n == 0 ? 0f : (float)Math.Sqrt(sum / n);
        }

        [Test]
        public void The_raft_never_exceeds_the_comfort_limits()
        {
            var rowing = SteadyRowing();
            var settings = new RaftRockingSettings();
            foreach (var scale in new[] { 1f, 1f / 3f, 0f })
            {
                // Steady rowing, easy rowing well off the line, and no power at all: the worst case.
                var frames = Ride(i => (Math.Max(1.5f, rowing[i % rowing.Count].Speed * (0.4f + 0.6f * scale)),
                                        rowing[i % rowing.Count].Power * scale), settings);
                foreach (var f in frames)
                {
                    Assert.That(Math.Abs(f.Pitch), Is.LessThanOrEqualTo(settings.MaxPitchDegrees + 1e-4f), "pitch");
                    Assert.That(Math.Abs(f.Roll), Is.LessThanOrEqualTo(settings.MaxRollDegrees + 1e-4f), "roll");
                    Assert.That(Math.Abs(f.Heave), Is.LessThanOrEqualTo(settings.MaxHeaveMetres + 1e-4f), "heave");
                    Assert.That(f.Rate, Is.LessThanOrEqualTo(settings.MaxDegreesPerSecond + 1e-2f), "angular rate");
                }
            }
        }

        [Test]
        public void The_raft_rocks_in_the_rapids_and_is_still_in_the_pools()
        {
            var rowing = SteadyRowing();
            var frames = Ride(i => rowing[i % rowing.Count]);
            var rapids = frames.FindAll(f => f.InRapid && f.SecondsIntoSection > 5d);
            var pools = frames.FindAll(f => !f.InRapid && f.SecondsIntoSection > 8d);
            Assert.That(rapids.Count, Is.GreaterThan(0));
            Assert.That(pools.Count, Is.GreaterThan(0));

            Assert.That(Motion(rapids), Is.GreaterThan(1f), "visible rocking in the rapids, degrees RMS");
            var biggestPitch = 0f;
            foreach (var f in rapids) biggestPitch = Math.Max(biggestPitch, Math.Abs(f.Pitch));
            Assert.That(biggestPitch, Is.GreaterThan(2f), "a crest tips the bow");
            foreach (var f in pools)
            {
                Assert.That(Math.Abs(f.Pitch), Is.LessThan(0.05f), "pitch in a pool");
                Assert.That(Math.Abs(f.Roll), Is.LessThan(0.05f), "roll in a pool");
                Assert.That(Math.Abs(f.Heave), Is.LessThan(0.002f), "heave in a pool");
            }
        }

        [Test]
        public void Drifting_off_the_line_rocks_the_raft_harder()
        {
            var rowing = SteadyRowing();
            var steady = Ride(i => rowing[i % rowing.Count]);
            var easy = Ride(i => (rowing[i % rowing.Count].Speed * 0.7f, rowing[i % rowing.Count].Power / 3f));
            var onLine = Motion(steady.FindAll(f => f.InRapid));
            var offLine = Motion(easy.FindAll(f => f.InRapid));
            TestContext.WriteLine($"rocking in the rapids, degrees RMS: on the line {onLine:F2}, off it {offLine:F2}");
            Assert.That(offLine, Is.GreaterThan(onLine * 1.2f));
        }

        [Test]
        public void A_crest_lifts_the_bow_first_then_the_stern()
        {
            var raft = new RaftRocking();
            var maxPitch = 0f;
            var minPitch = 0f;
            var tAtMax = 0d;
            var tAtMin = 0d;
            // One crest, a metre-and-a-half wide ridge across the river, coming from ahead at 3 m/s.
            for (var t = 0d; t < 6d; t += Dt)
            {
                var crestAlong = 6f - 3f * (float)t;  // where the crest is, metres toward the bow
                raft.Update(t, (across, along) => Math.Max(0f, 0.3f * (1f - Math.Abs(along - crestAlong) / 0.8f)));
                if (raft.Pitch > maxPitch) { maxPitch = raft.Pitch; tAtMax = t; }
                if (raft.Pitch < minPitch) { minPitch = raft.Pitch; tAtMin = t; }
            }
            Assert.That(maxPitch, Is.GreaterThan(1f), "bow up");
            Assert.That(minPitch, Is.LessThan(-1f), "bow down as it passes under the stern");
            Assert.That(tAtMax, Is.LessThan(tAtMin));
            Assert.That(Math.Abs(raft.Roll), Is.LessThan(1e-4f), "a square crest does not roll the raft");
        }

        [Test]
        public void Water_higher_on_the_right_lifts_the_right_side()
        {
            var raft = new RaftRocking();
            for (var t = 0d; t < 5d; t += Dt)
                raft.Update(t, (across, along) => across > 0f ? 0.2f : 0f);
            Assert.That(raft.Roll, Is.GreaterThan(1f));
            Assert.That(raft.Heave, Is.GreaterThan(0f));
        }

        [Test]
        public void At_amount_zero_the_raft_stays_still()
        {
            var rowing = SteadyRowing();
            var frames = Ride(i => rowing[i % rowing.Count], new RaftRockingSettings { Amount = 0f });
            foreach (var f in frames)
            {
                Assert.That(f.Pitch, Is.EqualTo(0f));
                Assert.That(f.Roll, Is.EqualTo(0f));
                Assert.That(f.Heave, Is.EqualTo(0f));
            }
        }

        [Test]
        public void The_same_rapid_always_has_the_same_waves()
        {
            var a = new RiverWaves(RapidsRun.Standard(), HalfWidth);
            var b = new RiverWaves(RapidsRun.Standard(), HalfWidth);
            for (var d = 0d; d < 1400d; d += 0.37d)
                for (var x = -10f; x <= 10f; x += 1.3f)
                    Assert.That(a.Height(x, d), Is.EqualTo(b.Height(x, d)));
        }

        [Test]
        public void Waves_are_low_on_the_line_high_on_the_drift_side_and_flat_in_pools()
        {
            var sections = RapidsRun.Standard();
            var river = new RiverWaves(sections, HalfWidth);
            Assert.That(river.Rapids.Count, Is.EqualTo(3));

            var index = 0;
            foreach (var (start, waves) in river.Rapids)
            {
                Assert.That(waves.CrestCount, Is.GreaterThan(10));
                var section = FindRapid(sections, ++index);
                float onLine = 0f, driftSide = 0f, farSide = 0f, highest = 0f;
                for (var z = 0f; z < waves.Length; z += 0.05f)
                {
                    onLine = Math.Max(onLine, waves.Height(0f, z));
                    driftSide = Math.Max(driftSide, waves.Height(3f * section.DriftSide, z));
                    farSide = Math.Max(farSide, waves.Height(-3f * section.DriftSide, z));
                    for (var x = -HalfWidth; x <= HalfWidth; x += 0.5f) highest = Math.Max(highest, waves.Height(x, z));
                }
                // 3 m is as far as a weak raft drifts (RapidsSettings.MaxOffsetMetres).
                Assert.That(onLine, Is.LessThan(0.2f), "on the line");
                Assert.That(driftSide, Is.GreaterThan(onLine * 1.4f), "where a weak raft drifts to");
                Assert.That(farSide, Is.LessThan(onLine), "the far side is calmer");
                Assert.That(highest, Is.LessThanOrEqualTo(0.34f * 1.0f + 1e-4f), "tallest crest");
            }

            // Pools: the first section is a pool, and so is the gap between the first two rapids.
            for (var d = 0d; d < sections[0].LengthMetres; d += 0.5d)
                Assert.That(river.Height(0f, d), Is.EqualTo(0f));
        }

        static RiverSection FindRapid(List<RiverSection> sections, int number)
        {
            var n = 0;
            foreach (var s in sections)
                if (s.Kind == SectionKind.Rapid && ++n == number) return s;
            throw new ArgumentException("no such rapid");
        }
    }
}
