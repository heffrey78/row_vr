using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>The endless-course arithmetic behind the scrolling scenery (#39).</summary>
    [TestFixture]
    public class CourseWrapTests
    {
        const float Behind = 900f;
        const float Ahead = 900f;

        [Test]
        public void With_no_travel_a_piece_is_at_its_place()
        {
            Assert.That(CourseWrap.Position(120d, 0d, Behind, Ahead), Is.EqualTo(120f).Within(1e-4f));
            Assert.That(CourseWrap.Position(-300d, 0d, Behind, Ahead), Is.EqualTo(-300f).Within(1e-4f));
        }

        [Test]
        public void Travel_moves_a_piece_forward_in_the_rowers_view()
        {
            // A sculler faces the stern: fixed scenery passes them and recedes ahead.
            Assert.That(CourseWrap.Position(-50d, 30d, Behind, Ahead), Is.EqualTo(-20f).Within(1e-4f));
        }

        [Test]
        public void A_piece_past_the_front_of_the_window_wraps_to_the_back()
        {
            Assert.That(CourseWrap.Position(850d, 100d, Behind, Ahead), Is.EqualTo(-850f).Within(1e-3f));
        }

        [Test]
        public void An_hour_of_rowing_keeps_every_piece_in_the_window_and_the_spacing_intact()
        {
            // 18 pieces 100 m apart fill the 1800 m window exactly. Step through an hour at
            // 5 m/s, faster than any rower holds, at 72 Hz.
            const int pieces = 18;
            const double spacing = 100d;
            const double dt = 1d / 72d;
            var distance = 0d;
            for (var frame = 0; frame < 3600 * 72; frame += 97)
            {
                distance = frame * dt * 5d;
                for (var i = 0; i < pieces; i++)
                {
                    var p = CourseWrap.Position(i * spacing - Behind, distance, Behind, Ahead);
                    Assert.That(p, Is.GreaterThanOrEqualTo(-Behind).And.LessThan(Ahead));
                }
            }

            // After the hour (18 km), neighbours are still 100 m apart to the millimetre,
            // except across the wrap.
            var a = CourseWrap.Position(300d, distance, Behind, Ahead);
            var b = CourseWrap.Position(400d, distance, Behind, Ahead);
            var gap = b - a;
            if (gap < 0f) gap += Ahead + Behind;
            Assert.That(gap, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(distance, Is.GreaterThan(17000d));
        }

        [Test]
        public void An_empty_window_is_rejected()
        {
            Assert.Throws<System.ArgumentException>(() => CourseWrap.Position(0d, 0d, 0f, 0f));
        }
    }
}
