using System.IO;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Exercises the committed-fixture path end to end: a clip file on disk, parsed and run
    /// through the detector exactly as `make pull-recordings` + `make promote` will produce.
    ///
    /// The fixture here is synthetic, a stand-in so this path is covered before anyone has
    /// worn the headset. Recording a real stroke and promoting it turns these into genuine
    /// regression tests over real tracking data, which is the whole point of tier 1.
    /// </summary>
    [TestFixture]
    public class FixtureClipTests
    {
        const string FixtureName = "synthetic-22spm.rowclip";

        static string FixturePath =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", FixtureName);

        [Test]
        public void FixtureIsPresentAndLoadable()
        {
            Assert.That(File.Exists(FixturePath), Is.True,
                $"missing fixture {FixturePath} - check the csproj copies fixtures/ to the output directory");

            var clip = HandClip.Load(FixturePath);

            Assert.That(clip.Samples, Is.Not.Empty);
            Assert.That(clip.Note, Is.Not.Null.And.Not.Empty);
            Assert.That(clip.DurationSeconds, Is.GreaterThan(5d));
        }

        [Test]
        public void DetectorFindsTheExpectedStrokesInTheFixture()
        {
            var clip = HandClip.Load(FixturePath);
            var metrics = SyntheticRower.Run(clip).Last().Metrics;

            // The fixture is 5 strokes at 22 spm. The first is unmeasurable (no range
            // established yet), leaving 3-4 fully bracketed by catches.
            Assert.That(metrics.CompletedStrokes, Is.InRange(3, 4),
                $"found {metrics.CompletedStrokes} strokes: {metrics}");
            Assert.That(metrics.StrokesPerMinute, Is.EqualTo(22f).Within(1.5f),
                $"metrics were: {metrics}");
        }

        [Test]
        public void ReplayThroughTheClipSourceMatchesDirectPlayback()
        {
            // ClipHandPoseSource interpolates, so a test may step at any rate. Stepping at
            // the clip's own rate must agree with feeding the samples straight in, or the
            // interpolation is distorting the very data being regression-tested.
            var clip = HandClip.Load(FixturePath);
            var source = new ClipHandPoseSource(clip);

            var direct = new StrokeDetector();
            foreach (var s in clip.Samples)
            {
                direct.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
            }

            var replayed = new StrokeDetector();
            for (var t = source.StartTime; t <= source.EndTime; t += 1d / 72d)
            {
                if (source.TryGetPose(t, out var sample))
                {
                    replayed.Update(sample.TimeSeconds, sample.LeftHand, sample.RightHand,
                                    sample.HeadPosition, sample.HeadForward);
                }
            }

            Assert.That(replayed.Metrics.CompletedStrokes,
                Is.EqualTo(direct.Metrics.CompletedStrokes).Within(1));
            Assert.That(replayed.Metrics.StrokesPerMinute,
                Is.EqualTo(direct.Metrics.StrokesPerMinute).Within(1.0f));
        }
    }
}
