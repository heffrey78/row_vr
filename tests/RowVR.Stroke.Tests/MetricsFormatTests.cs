using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    [TestFixture]
    public class MetricsFormatTests
    {
        [TestCase(0d, "0:00")]
        [TestCase(59.9d, "0:59")]
        [TestCase(754d, "12:34")]
        [TestCase(3725d, "1:02:05")]
        [TestCase(-3d, "0:00")]
        public void Time_reads_like_a_monitor(double seconds, string expected) =>
            Assert.That(MetricsFormat.Time(seconds), Is.EqualTo(expected));

        [TestCase(140f, "2:20.0")]
        [TestCase(118.04f, "1:58.0")]
        [TestCase(119.96f, "2:00.0")]
        [TestCase(float.PositiveInfinity, "-:--")]
        [TestCase(700f, "-:--")]
        public void Split_reads_like_a_monitor(float seconds, string expected) =>
            Assert.That(MetricsFormat.Split(seconds), Is.EqualTo(expected));

        [Test]
        public void Distance_and_rate()
        {
            Assert.That(MetricsFormat.Distance(1234.9), Is.EqualTo("1,234"));
            Assert.That(MetricsFormat.Distance(-4), Is.EqualTo("0"));
            Assert.That(MetricsFormat.Rate(21.6f), Is.EqualTo("22"));
            Assert.That(MetricsFormat.Rate(0f), Is.EqualTo("-"));
        }
    }
}
