using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    [TestFixture]
    public class Vec3Tests
    {
        [Test]
        public void DotAndCrossFollowTheRightHandRule()
        {
            var x = new Vec3(1f, 0f, 0f);
            var y = new Vec3(0f, 1f, 0f);

            Assert.That(Vec3.Dot(x, y), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Vec3.Cross(x, y).Z, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void NormalizingAZeroVectorYieldsZeroRatherThanNaN()
        {
            // Guards the levelled drive axis: looking straight up leaves no horizontal
            // component, and a NaN axis would silently poison every later projection.
            Assert.That(Vec3.Zero.Normalized, Is.EqualTo(Vec3.Zero));
        }

        [Test]
        public void MagnitudeMatchesPythagoras()
        {
            Assert.That(new Vec3(3f, 4f, 0f).Magnitude, Is.EqualTo(5f).Within(1e-6f));
        }
    }

    [TestFixture]
    public class OneEuroFilterTests
    {
        [Test]
        public void PassesAConstantSignalThroughUnchanged()
        {
            var filter = new OneEuroFilter();
            for (var i = 0; i < 50; i++)
            {
                Assert.That(filter.Filter(2.5f, i / 72d), Is.EqualTo(2.5f).Within(1e-4f));
            }
        }

        [Test]
        public void AttenuatesNoiseOnAStationarySignal()
        {
            var rng = new Random(7);
            var filter = new OneEuroFilter();
            double worstRaw = 0, worstFiltered = 0;

            for (var i = 0; i < 300; i++)
            {
                var noise = (float)(rng.NextDouble() * 2d - 1d) * 0.01f;
                var filtered = filter.Filter(noise, i / 72d);
                if (i > 50)
                {
                    worstRaw = Math.Max(worstRaw, Math.Abs(noise));
                    worstFiltered = Math.Max(worstFiltered, Math.Abs(filtered));
                }
            }

            Assert.That(worstFiltered, Is.LessThan(worstRaw * 0.5d),
                $"raw peak {worstRaw:F4}, filtered peak {worstFiltered:F4}");
        }

        [Test]
        public void TracksFastMotionWithoutStallingOut()
        {
            // The adaptive property that makes this filter worth using: a fast ramp must not
            // be lagged into uselessness the way a fixed low-pass would. Built from the
            // shipped settings rather than bare defaults, so this measures the lag the app
            // actually gets. 3.6 m/s is about twice a hard drive.
            var settings = new StrokeDetectorSettings();
            var filter = new OneEuroFilter(settings.FilterMinCutoff, settings.FilterBeta);
            var value = 0f;
            for (var i = 0; i < 72; i++)
            {
                value = filter.Filter(i * 0.05f, i / 72d);
            }

            // Residual lag at 3.6 m/s, expressed as distance. 0.1 m is about 28 ms.
            Assert.That(value, Is.EqualTo(71 * 0.05f).Within(0.1f), $"ended at {value:F3}");
        }

        [Test]
        public void IgnoresNonAdvancingTime()
        {
            var filter = new OneEuroFilter();
            filter.Filter(1f, 0d);
            var repeated = filter.Filter(99f, 0d);

            Assert.That(repeated, Is.EqualTo(1f), "a duplicate timestamp must not be integrated");
            Assert.That(float.IsNaN(repeated), Is.False);
        }
    }

    [TestFixture]
    public class HandClipTests
    {
        [Test]
        public void RoundTripsThroughTheTextFormat()
        {
            var original = SyntheticRower.Generate(strokes: 2, sampleRateHz: 30f);

            var buffer = new StringWriter();
            original.Write(buffer);
            var parsed = HandClip.Parse(new StringReader(buffer.ToString()));

            Assert.That(parsed.Samples.Count, Is.EqualTo(original.Samples.Count));
            Assert.That(parsed.Note, Is.EqualTo(original.Note));

            for (var i = 0; i < parsed.Samples.Count; i++)
            {
                // The format writes 4 decimal places, so sub-0.1 mm differences are expected.
                Assert.That(parsed.Samples[i].LeftHand.Z,
                    Is.EqualTo(original.Samples[i].LeftHand.Z).Within(1e-3f));
                Assert.That(parsed.Samples[i].TimeSeconds,
                    Is.EqualTo(original.Samples[i].TimeSeconds).Within(1e-3d));
            }
        }

        [Test]
        public void SurvivesTheRoundTripWithIdenticalDetectorOutput()
        {
            // The property that actually matters: a clip written to disk, committed, and
            // parsed back must drive the detector to the same conclusion. If it does not,
            // a fixture is not a regression test.
            var original = SyntheticRower.Generate(strokes: 8);
            var buffer = new StringWriter();
            original.Write(buffer);
            var parsed = HandClip.Parse(new StringReader(buffer.ToString()));

            var before = SyntheticRower.Run(original).Last().Metrics;
            var after = SyntheticRower.Run(parsed).Last().Metrics;

            Assert.That(after.CompletedStrokes, Is.EqualTo(before.CompletedStrokes));
            Assert.That(after.StrokesPerMinute, Is.EqualTo(before.StrokesPerMinute).Within(0.5f));
        }

        [Test]
        public void RejectsAFileThatIsNotARowclip()
        {
            var ex = Assert.Throws<FormatException>(
                () => HandClip.Parse(new StringReader("t\tx\ty\n0\t1\t2\n")));
            Assert.That(ex.Message, Does.Contain("rowclip"));
        }

        [Test]
        public void RejectsAMalformedRow()
        {
            var text = HandClip.FormatHeader + "\n0.0\t1.0\t2.0\n";
            var ex = Assert.Throws<FormatException>(() => HandClip.Parse(new StringReader(text)));
            Assert.That(ex.Message, Does.Contain("columns"));
        }
    }

    [TestFixture]
    public class ClipHandPoseSourceTests
    {
        [Test]
        public void InterpolatesBetweenRecordedSamples()
        {
            var clip = new HandClip();
            clip.Add(new HandSample(0d, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Forward));
            clip.Add(new HandSample(1d, new Vec3(0f, 0f, 2f), Vec3.Zero, Vec3.Zero, Vec3.Forward));

            var source = new ClipHandPoseSource(clip);

            Assert.That(source.TryGetPose(0.5d, out var mid), Is.True);
            Assert.That(mid.LeftHand.Z, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ReportsNoPoseOutsideTheClip()
        {
            var clip = SyntheticRower.Generate(strokes: 1);
            var source = new ClipHandPoseSource(clip);

            Assert.That(source.TryGetPose(source.StartTime - 1d, out _), Is.False);
            Assert.That(source.TryGetPose(source.EndTime + 1d, out _), Is.False);
            Assert.That(source.TryGetPose(source.EndTime, out _), Is.True);
        }

        [Test]
        public void InterpolatesAcrossGapsByDefault()
        {
            var clip = new HandClip();
            clip.Add(new HandSample(0d, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Forward));
            clip.Add(new HandSample(1d, new Vec3(0f, 0f, 2f), Vec3.Zero, Vec3.Zero, Vec3.Forward));

            Assert.That(new ClipHandPoseSource(clip).TryGetPose(0.5d, out _), Is.True);
        }

        [Test]
        public void ReportsNoPoseInsideAGapLongerThanMaxGap()
        {
            // Live tracking has no pose while the hands are lost; a replay set up to match
            // must not invent one by interpolating.
            var clip = new HandClip();
            clip.Add(new HandSample(0.00d, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Forward));
            clip.Add(new HandSample(0.02d, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Forward));
            clip.Add(new HandSample(0.90d, new Vec3(0f, 0f, 2f), Vec3.Zero, Vec3.Zero, Vec3.Forward));
            clip.Add(new HandSample(0.92d, new Vec3(0f, 0f, 2f), Vec3.Zero, Vec3.Zero, Vec3.Forward));

            var source = new ClipHandPoseSource(clip) { MaxGapSeconds = 0.1d };

            Assert.That(source.TryGetPose(0.01d, out _), Is.True, "inside normal sampling");
            Assert.That(source.TryGetPose(0.50d, out _), Is.False, "inside the gap");
            Assert.That(source.TryGetPose(0.91d, out var after), Is.True, "after the gap");
            Assert.That(after.LeftHand.Z, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void SupportsRandomAccessAfterSequentialPlayback()
        {
            // The forward-only cursor is an optimisation for sequential replay; it must not
            // stop a test from seeking backwards.
            var clip = SyntheticRower.Generate(strokes: 4);
            var source = new ClipHandPoseSource(clip);

            for (var t = source.StartTime; t < source.EndTime; t += 0.1d) source.TryGetPose(t, out _);

            Assert.That(source.TryGetPose(source.StartTime + 0.05d, out var rewound), Is.True);
            Assert.That(rewound.TimeSeconds, Is.EqualTo(source.StartTime + 0.05d).Within(1e-6d));
        }
    }
}
