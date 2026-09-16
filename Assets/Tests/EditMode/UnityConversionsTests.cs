using NUnit.Framework;
using RowVR.Rowing;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Tests.EditMode
{
    /// <summary>
    /// Almost every test in this project lives in tests/RowVR.Stroke.Tests and runs under
    /// plain `dotnet test` in about two seconds. This suite exists only for the handful of
    /// things that genuinely need Unity types - starting with the boundary between
    /// UnityEngine.Vector3 and the engine-free Vec3.
    ///
    /// If you find yourself adding a test here that does not need UnityEngine, it belongs in
    /// the fast suite instead.
    /// </summary>
    [TestFixture]
    public class UnityConversionsTests
    {
        [Test]
        public void Vector3RoundTripsThroughVec3()
        {
            var original = new Vector3(1.5f, -2.25f, 0.125f);
            var round = original.ToVec3().ToVector3();

            Assert.That(round.x, Is.EqualTo(original.x));
            Assert.That(round.y, Is.EqualTo(original.y));
            Assert.That(round.z, Is.EqualTo(original.z));
        }

        [Test]
        public void AxisOrderIsPreserved()
        {
            // A silent x/z swap here would show up on device as a stroke that only registers
            // when rowing sideways, and would be maddening to track down from the headset.
            Assert.That(Vector3.forward.ToVec3().Z, Is.EqualTo(1f));
            Assert.That(Vector3.right.ToVec3().X, Is.EqualTo(1f));
            Assert.That(Vector3.up.ToVec3().Y, Is.EqualTo(1f));
        }

        [Test]
        public void MagnitudeAgreesWithUnity()
        {
            var v = new Vector3(3f, 4f, 12f);
            Assert.That(v.ToVec3().Magnitude, Is.EqualTo(v.magnitude).Within(1e-5f));
        }
    }
}
