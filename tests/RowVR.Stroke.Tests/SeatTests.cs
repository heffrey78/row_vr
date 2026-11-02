using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Where the rower sits (#96). Owner's headset report, 2026-11-01: they had to reorient
    /// themselves, and the headset's recenter did not put them back at the right height.
    /// Run on the rowing-machine recordings, with recenters simulated by moving and turning the
    /// tracking space partway through.
    /// </summary>
    [TestFixture]
    public class SeatTests
    {
        static readonly string[] ErgFixtures =
        {
            "real-erg-steady-12strokes-20260927.rowclip",
            "real-erg-sloppy-12strokes-20260927.rowclip",
            "real-erg-stop-6-6-20260927.rowclip",
            "real-erg-release-types-12strokes-20260927.rowclip",
        };

        static HandClip Load(string fixture) =>
            HandClip.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));

        static Vec3 Hands(HandSample s) => (s.LeftHand + s.RightHand) * 0.5f;

        static Vec3 Yaw(Vec3 v, double degrees)
        {
            var r = degrees * Math.PI / 180.0;
            return new Vec3((float)(v.X * Math.Cos(r) - v.Z * Math.Sin(r)), v.Y, (float)(v.X * Math.Sin(r) + v.Z * Math.Cos(r)));
        }

        /// <summary>A recenter: the same body, seen in a tracking space turned and shifted.</summary>
        static readonly Func<Vec3, Vec3> Moved = p => Yaw(p, 100) + new Vec3(1.3f, 0f, -0.9f);

        static HandSample Through(HandSample s, Func<Vec3, Vec3> point, Func<Vec3, Vec3> direction) =>
            new HandSample(s.TimeSeconds, point(s.LeftHand), point(s.RightHand), point(s.HeadPosition), direction(s.HeadForward));

        /// <summary>The clip, recentered at <paramref name="at"/> seconds in (null: never).</summary>
        static List<HandSample> Recentered(HandClip clip, double? at)
        {
            var start = clip.Samples[0].TimeSeconds;
            var samples = new List<HandSample>();
            foreach (var s in clip.Samples)
                samples.Add(at.HasValue && s.TimeSeconds - start >= at.Value ? Through(s, Moved, d => Yaw(d, 100)) : s);
            return samples;
        }

        /// <summary>Whether a stroke is being tracked, frame by frame, as the detector sees the clip.</summary>
        static bool[] Rowing(IReadOnlyList<HandSample> samples)
        {
            var detector = new StrokeDetector(new StrokeDetectorSettings());
            var rowing = new bool[samples.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                rowing[i] = detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward).Phase != StrokePhase.Unknown;
            }
            return rowing;
        }

        /// <summary>
        /// Rows the samples through a seat; the callback sees each frame after it. Rowing comes
        /// from the detector on <paramref name="rowing"/>'s clip (default: these samples), so a
        /// recentered run and its untouched reference agree on when the rower rows.
        /// </summary>
        static Seat Row(IReadOnlyList<HandSample> samples, Action<Seat, HandSample, int> after = null, bool[] rowing = null)
        {
            rowing ??= Rowing(samples);
            var seat = new Seat();
            for (var i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                seat.Update(s.TimeSeconds, s.HeadPosition, s.HeadForward, Hands(s), rowing[i]);
                after?.Invoke(seat, s, i);
            }
            return seat;
        }

        static double DegreesBetweenLines(Vec3 a, Vec3 b) =>
            Math.Acos(Math.Min(1.0, Math.Abs(Vec3.Dot(a.Normalized, b.Normalized)))) * 180.0 / Math.PI;

        static double Level(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, 0f, a.Z - b.Z).Magnitude;

        [TestCaseSource(nameof(ErgFixtures))]
        public void Rowing_never_re_seats(string fixture)
        {
            // Head and hands as they really move on the machine: no fast lean or turn passes for a recenter.
            var seat = Row(Load(fixture).Samples);
            Assert.That(seat.Version, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void Learns_where_the_hands_sit_below_and_ahead_of_the_eyes(string fixture)
        {
            var seat = Row(Load(fixture).Samples);
            Assert.That(seat.HandsBelowEyes, Is.InRange(0.25f, 0.5f));
            Assert.That(seat.HandsAhead, Is.InRange(0.25f, 0.55f));
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void A_recenter_puts_the_boat_back_round_the_rower(string fixture)
        {
            var clip = Load(fixture);
            const double recenterAt = 15.0;
            var start = clip.Samples[0].TimeSeconds;

            // The same session, untouched: where the boat should be, in the original space.
            var reference = new List<(double T, Vec3 Centre, Vec3 Forward)>();
            Row(clip.Samples, (seat, s, i) => reference.Add((s.TimeSeconds, seat.Centre, seat.Forward)));

            var moved = Recentered(clip, recenterAt);
            var rowing = Rowing(clip.Samples);
            Row(moved, rowing: rowing, after: (seat, s, i) =>
            {
                var t = s.TimeSeconds - start;
                if (t < recenterAt) return;
                if (i > 0 && moved[i - 1].TimeSeconds - start < recenterAt)
                    seat.SpaceMoved(s.TimeSeconds);
                if (t < recenterAt + 0.5) return;

                Assert.That(seat.Version, Is.EqualTo(2), $"re-seated once by {t:F2} s");
                var expected = reference[i];
                // Seated from the eyes, then refined by rowing: near the boat the untouched
                // session had, carried into the moved space. A seat taken mid-stroke is off by
                // the stroke's own lean (the eyes move 0.2 m against the hands) until rowing
                // refines it.
                Assert.That(Math.Abs(seat.Centre.Y - expected.Centre.Y), Is.LessThan(0.12), $"height at {t:F2} s");
                Assert.That(Level(seat.Centre, Moved(expected.Centre)), Is.LessThan(0.35), $"place at {t:F2} s");
                Assert.That(DegreesBetweenLines(seat.Forward, Yaw(expected.Forward, 100)), Is.LessThan(20), $"heading at {t:F2} s");
            });
        }

        [TestCaseSource(nameof(ErgFixtures))]
        public void A_recenter_whose_event_never_arrives_is_seen_in_the_head_jump(string fixture)
        {
            var seat = Row(Recentered(Load(fixture), 15.0));
            Assert.That(seat.Version, Is.EqualTo(2));
        }

        [Test]
        public void Several_events_from_one_recenter_seat_once()
        {
            var seat = new Seat();
            var head = new Vec3(0f, 1f, 0f);
            var hands = new Vec3(0f, 0.6f, 0.4f);
            seat.Update(0, head, Vec3.Forward, hands, false);
            seat.SpaceMoved(1.0);
            seat.SpaceMoved(1.1);
            seat.SpaceMoved(1.2);
            for (var t = 1.0; t < 3.0; t += 1d / 72d) seat.Update(t, head, Vec3.Forward, hands, false);
            Assert.That(seat.Version, Is.EqualTo(2));
        }

        [Test]
        public void Resting_hands_in_the_lap_do_not_sink_the_boat()
        {
            var clip = Load(ErgFixtures[0]);
            var seat = Row(clip.Samples);
            var centre = seat.Centre;
            var last = clip.Samples[clip.Samples.Count - 1];

            // Ten seconds of rest: hands down in the lap, then up at the wrist menu.
            for (var i = 1; i <= 720; i++)
            {
                var hands = Hands(last) + new Vec3(0f, i < 360 ? -0.3f : 0.25f, 0f);
                seat.Update(last.TimeSeconds + i / 72d, last.HeadPosition, last.HeadForward, hands, false);
            }
            Assert.That(Vec3.Distance(seat.Centre, centre), Is.LessThan(1e-4f));
        }

        [Test]
        public void The_menu_button_re_seats_but_keeps_the_heading_from_the_machine()
        {
            var clip = Load(ErgFixtures[0]);
            var seat = Row(clip.Samples);
            var heading = seat.Forward;
            var last = clip.Samples[clip.Samples.Count - 1];

            // Looking down and to the left at the wrist menu when pressing it.
            var atWrist = Yaw(new Vec3(last.HeadForward.X, -0.6f, last.HeadForward.Z), -40).Normalized;
            var head = last.HeadPosition + new Vec3(0.05f, -0.1f, 0f);
            seat.Reseat(last.TimeSeconds + 0.1);
            seat.Update(last.TimeSeconds + 0.1, head, atWrist, Hands(last), false);

            Assert.That(seat.Version, Is.EqualTo(2));
            Assert.That(DegreesBetweenLines(seat.Forward, heading), Is.LessThan(0.01));
            var expected = head + heading * seat.HandsAhead - Vec3.Up * seat.HandsBelowEyes;
            Assert.That(Vec3.Distance(seat.Centre, expected), Is.LessThan(1e-4f));
        }

        [Test]
        public void The_first_seat_is_taken_from_the_eyes_not_wherever_the_hands_are()
        {
            // Hands up at the face, settling the headset, when tracking starts.
            var seat = new Seat();
            var head = new Vec3(0.2f, 1.0f, -0.1f);
            seat.Update(0, head, Vec3.Forward, head + new Vec3(0f, 0.05f, 0.1f), false);
            Assert.That(seat.Centre.Y, Is.EqualTo(head.Y - seat.HandsBelowEyes).Within(1e-5f));
        }
    }
}
