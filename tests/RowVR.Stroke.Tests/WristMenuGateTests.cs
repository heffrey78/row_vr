using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The wrist menu's gesture (#46).</summary>
    [TestFixture]
    public class WristMenuGateTests
    {
        const double Dt = 1d / 72d;

        static void Hold(WristMenuGate gate, ref double t, double seconds, bool tracked, float facing, StrokeState state = default)
        {
            for (var i = 0; i < (int)(seconds * 72); i++) gate.Update(t += Dt, tracked, facing, state);
        }

        [Test]
        public void Turning_the_palm_up_shows_it_and_turning_away_hides_it()
        {
            var gate = new WristMenuGate();
            var t = 0d;
            Hold(gate, ref t, 0.1, true, 0.9f);
            Assert.That(gate.Visible, Is.False, "a glance is not a request");
            Hold(gate, ref t, 0.3, true, 0.9f);
            Assert.That(gate.Visible, Is.True);
            Hold(gate, ref t, 0.5, true, 0.1f);
            Assert.That(gate.Visible, Is.False);
        }

        [Test]
        public void A_wobble_between_the_thresholds_keeps_it_open()
        {
            var gate = new WristMenuGate();
            var t = 0d;
            Hold(gate, ref t, 0.4, true, 0.9f);
            for (var i = 0; i < 72 * 3; i++) gate.Update(t += Dt, true, 0.45f + 0.1f * (float)System.Math.Sin(i * 0.3), default);
            Assert.That(gate.Visible, Is.True);
        }

        [Test]
        public void A_brief_tracking_loss_keeps_it_open_but_a_long_one_closes_it()
        {
            var gate = new WristMenuGate();
            var t = 0d;
            Hold(gate, ref t, 0.4, true, 0.9f);
            Hold(gate, ref t, 0.3, false, 0f);
            Assert.That(gate.Visible, Is.True, "poking with the other hand can occlude this one briefly");
            Hold(gate, ref t, 0.4, false, 0f);
            Assert.That(gate.Visible, Is.False);
        }

        [Test]
        public void It_never_shows_while_rowing()
        {
            var gate = new WristMenuGate();
            var t = 0d;
            gate.Update(t += Dt, true, 0f, new StrokeState(StrokePhase.Catch, 0f, 0f, 1f, default));
            gate.Update(t += Dt, true, 0f, new StrokeState(StrokePhase.Finish, 0f, 0f, 0f, default));
            Hold(gate, ref t, 1.0, true, 0.95f);
            Assert.That(gate.Visible, Is.False, "within 3 s of a stroke");
            Hold(gate, ref t, 2.5, true, 0.95f);
            Assert.That(gate.Visible, Is.True, "once the rower has stopped");
        }

        [Test]
        public void A_withdrawn_catch_is_not_rowing()
        {
            // Letting go raises a catch that is then withdrawn; reaching for the menu right
            // after letting go must work.
            var gate = new WristMenuGate();
            var t = 0d;
            gate.Update(t += Dt, true, 0f, new StrokeState(StrokePhase.Catch, 0f, 0f, 1f, default));
            gate.Update(t += Dt, true, 0f, new StrokeState(StrokePhase.Unknown, 0f, 0f, 0f, default, true));
            gate.Update(t += Dt, true, 0f, new StrokeState(StrokePhase.Finish, 0f, 0f, 0f, default));
            Hold(gate, ref t, 0.4, true, 0.9f);
            Assert.That(gate.Visible, Is.True);
        }

        [Test]
        public void It_eases_in()
        {
            var gate = new WristMenuGate();
            var t = 0d;
            Hold(gate, ref t, 0.3, true, 0.9f);
            Assert.That(gate.Appear, Is.GreaterThan(0f).And.LessThan(1f));
            Hold(gate, ref t, 0.3, true, 0.9f);
            Assert.That(gate.Appear, Is.EqualTo(1f));
        }
    }
}
