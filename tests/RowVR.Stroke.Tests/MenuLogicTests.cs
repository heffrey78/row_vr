using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The hands-only menu's logic (REQ-0008, ADR-0013): pressing, and workouts.</summary>
    [TestFixture]
    public class PressDetectorTests
    {
        const double Dt = 1d / 72d;

        /// <summary>Moves a fingertip in a straight line at a speed, reporting whether it pressed.</summary>
        static int Presses(PressDetector button, Vec3 from, Vec3 to, float speed, ref double t)
        {
            var steps = System.Math.Max(1, (int)((to - from).Magnitude / speed / Dt));
            var presses = 0;
            for (var i = 0; i <= steps; i++)
            {
                t += Dt;
                if (button.Update(t, Vec3.Lerp(from, to, (float)i / steps))) presses++;
            }
            return presses;
        }

        [Test]
        public void A_push_from_the_front_presses_once_and_releases()
        {
            var button = new PressDetector();
            var t = 0d;
            Assert.That(Presses(button, new Vec3(0, 0, 0.1f), new Vec3(0, 0, -0.02f), 0.3f, ref t), Is.EqualTo(1));
            Assert.That(button.Down, Is.True);
            Assert.That(Presses(button, new Vec3(0, 0, -0.02f), new Vec3(0, 0, 0.1f), 0.3f, ref t), Is.EqualTo(0));
            Assert.That(button.Down, Is.False);
        }

        [Test]
        public void Hovering_in_front_does_not_press()
        {
            var button = new PressDetector();
            var t = 0d;
            Assert.That(Presses(button, new Vec3(-0.02f, 0, 0.02f), new Vec3(0.02f, 0, 0.005f), 0.1f, ref t), Is.EqualTo(0));
        }

        [Test]
        public void Sweeping_in_from_the_side_does_not_press()
        {
            // A hand swinging past, already deeper than the face: never came from the front.
            var button = new PressDetector();
            var t = 0d;
            Assert.That(Presses(button, new Vec3(-0.2f, 0, -0.02f), new Vec3(0.2f, 0, -0.02f), 1.5f, ref t), Is.EqualTo(0));
        }

        [Test]
        public void Wobbling_at_the_threshold_does_not_chatter()
        {
            var button = new PressDetector();
            var t = 0d;
            Presses(button, new Vec3(0, 0, 0.05f), new Vec3(0, 0, -0.013f), 0.3f, ref t);
            var extra = 0;
            for (var i = 0; i < 200; i++)
            {
                t += Dt;
                // Between the press and release depths: stays down, never presses again.
                if (button.Update(t, new Vec3(0, 0, -0.008f + 0.003f * (float)System.Math.Sin(i)))) extra++;
            }
            Assert.That(extra, Is.EqualTo(0));
        }

        [Test]
        public void A_second_poke_after_a_moment_presses_again()
        {
            var button = new PressDetector();
            var t = 0d;
            Presses(button, new Vec3(0, 0, 0.06f), new Vec3(0, 0, -0.02f), 0.3f, ref t);
            Presses(button, new Vec3(0, 0, -0.02f), new Vec3(0, 0, 0.06f), 0.3f, ref t);
            t += 0.5;
            Assert.That(Presses(button, new Vec3(0, 0, 0.06f), new Vec3(0, 0, -0.02f), 0.3f, ref t), Is.EqualTo(1));
        }
    }

    [TestFixture]
    public class WorkoutTests
    {
        [Test]
        public void A_timed_piece_counts_down_and_finishes()
        {
            var p = new WorkoutProgress(Workout.Timed(1200));
            p.Update(300, 1200);
            Assert.That(p.SecondsLeft, Is.EqualTo(900).Within(1e-6));
            Assert.That(p.Finished, Is.False);
            p.Update(1200, 4800);
            Assert.That(p.Finished, Is.True);
        }

        [Test]
        public void A_distance_piece_counts_down_metres()
        {
            var p = new WorkoutProgress(Workout.Distance(2000));
            p.Update(100, 450);
            Assert.That(p.MetresLeft, Is.EqualTo(1550).Within(1e-6));
            p.Update(500, 2001);
            Assert.That(p.Finished, Is.True);
        }

        [Test]
        public void Intervals_alternate_work_and_rest_and_record_each_work_distance()
        {
            // 4 x 3:00 on, 1:00 off, rowed at a steady 3.5 m/s on and 1 m/s off.
            var p = new WorkoutProgress(Workout.Intervals(4, 180, 60));
            var distance = 0d;
            for (var t = 0d; t <= 1000 && !p.Finished; t += 0.1)
            {
                p.Update(t, distance);
                distance += (p.Resting ? 1.0 : 3.5) * 0.1;
                if (t > 100 && t < 101) { Assert.That(p.Interval, Is.EqualTo(1)); Assert.That(p.Resting, Is.False); }
                if (t > 200 && t < 201) { Assert.That(p.Interval, Is.EqualTo(1)); Assert.That(p.Resting, Is.True); }
                if (t > 250 && t < 251) { Assert.That(p.Interval, Is.EqualTo(2)); Assert.That(p.Resting, Is.False); }
            }
            Assert.That(p.Finished, Is.True);
            Assert.That(p.IntervalMetres.Count, Is.EqualTo(4));
            foreach (var m in p.IntervalMetres) Assert.That(m, Is.EqualTo(630).Within(1.0), "3:00 at 3.5 m/s");
        }

        [Test]
        public void Just_row_never_finishes()
        {
            var p = new WorkoutProgress(Workout.JustRow());
            p.Update(100000, 50000);
            Assert.That(p.Finished, Is.False);
        }

        [Test]
        public void The_menu_offers_just_row_first()
        {
            Assert.That(Workout.Presets[0].Kind, Is.EqualTo(WorkoutKind.JustRow));
            Assert.That(Workout.Presets.Count, Is.EqualTo(4));
        }
    }
}
