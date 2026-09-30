using System;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// The boat's heading, from the hands (developer report 2026-09-30: turning the head turned
    /// the boat). Run on the rowing-machine recordings.
    /// </summary>
    [TestFixture]
    public class MachineAxisTests
    {
        static readonly string[] ErgFixtures =
        {
            "real-erg-steady-12strokes-20260927.rowclip",
            "real-erg-sloppy-12strokes-20260927.rowclip",
            "real-erg-stop-6-6-20260927.rowclip",
        };

        static HandClip Load(string fixture) =>
            HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));

        static Vec3 Hands(HandSample s) => (s.LeftHand + s.RightHand) * 0.5f;

        /// <summary>The machine's line: the principal direction of the hands' level travel.</summary>
        static Vec3 ReferenceAxis(HandClip clip)
        {
            double mx = 0, mz = 0;
            foreach (var s in clip.Samples) { var h = Hands(s); mx += h.X; mz += h.Z; }
            mx /= clip.Samples.Count; mz /= clip.Samples.Count;
            double xx = 0, xz = 0, zz = 0;
            foreach (var s in clip.Samples)
            {
                var h = Hands(s);
                var dx = h.X - mx; var dz = h.Z - mz;
                xx += dx * dx; xz += dx * dz; zz += dz * dz;
            }
            var angle = 0.5 * Math.Atan2(2 * xz, xx - zz);
            return new Vec3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }

        static double DegreesBetweenLines(Vec3 a, Vec3 b) =>
            Math.Acos(Math.Min(1.0, Math.Abs(Vec3.Dot(a.Normalized, b.Normalized)))) * 180.0 / Math.PI;

        static Vec3 Yaw(Vec3 v, double degrees)
        {
            var r = degrees * Math.PI / 180.0;
            return new Vec3((float)(v.X * Math.Cos(r) - v.Z * Math.Sin(r)), v.Y, (float)(v.X * Math.Sin(r) + v.Z * Math.Cos(r)));
        }

        static MachineAxis Row(HandClip clip, Vec3 seed)
        {
            var axis = new MachineAxis();
            axis.Seed(seed);
            foreach (var s in clip.Samples) axis.Update(s.TimeSeconds, Hands(s));
            return axis;
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void Settles_on_the_machine_line_from_the_head_the_rower_started_with(string fixture)
        {
            var clip = Load(fixture);
            var axis = Row(clip, clip.Samples[0].HeadForward);
            Assert.That(DegreesBetweenLines(axis.Forward, ReferenceAxis(clip)), Is.LessThan(5.0));
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void A_head_turned_away_at_the_start_does_not_decide_the_heading(string fixture)
        {
            // The rower was looking 30 degrees off to the side when the app started.
            var clip = Load(fixture);
            var axis = Row(clip, Yaw(clip.Samples[0].HeadForward, 30));
            Assert.That(DegreesBetweenLines(axis.Forward, ReferenceAxis(clip)), Is.LessThan(5.0));
        }

        [Test]
        public void Points_toward_the_stern_the_way_the_rower_faces()
        {
            var clip = Load(ErgFixtures[0]);
            var axis = Row(clip, clip.Samples[0].HeadForward);
            Assert.That(Vec3.Dot(axis.Forward, clip.Samples[0].HeadForward), Is.GreaterThan(0f));
        }

        [Test]
        public void Slow_or_sideways_hand_motion_does_not_steer_it()
        {
            var axis = new MachineAxis();
            axis.Seed(Vec3.Forward);
            var t = 0d;
            var hands = new Vec3(0f, 1f, 0f);
            for (var i = 0; i < 72 * 30; i++)
            {
                t += 1d / 72d;
                // Sideways at 1 m/s (a reach for a water bottle), then a slow drift.
                hands = hands + (i < 72 * 15 ? new Vec3(1f / 72f, 0f, 0f) : new Vec3(0.1f / 72f, 0f, 0.1f / 72f));
                axis.Update(t, hands);
            }
            Assert.That(DegreesBetweenLines(axis.Forward, Vec3.Forward), Is.LessThan(0.5));
        }
    }
}
