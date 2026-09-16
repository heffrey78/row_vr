using System;
using System.Linq;
using NUnit.Framework;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    [TestFixture]
    public class StrokeDetectorTests
    {
        [Test]
        public void CountsOneCatchPerStroke()
        {
            var clip = SyntheticRower.Generate(strokesPerMinute: 24f, strokes: 8);
            var states = SyntheticRower.Run(clip);

            var catches = SyntheticRower.CountPhase(states, StrokePhase.Catch);

            // The first catch cannot be detected: the detector has not yet observed the
            // rower's range, so it has nothing to measure "full reach" against. Everything
            // after that should be found.
            Assert.That(catches, Is.InRange(7, 8), $"expected 7-8 catches from 8 strokes, got {catches}");
        }

        [Test]
        public void EveryCatchIsFollowedByExactlyOneFinish()
        {
            var clip = SyntheticRower.Generate(strokes: 10);
            var phases = SyntheticRower.Run(clip)
                .Select(s => s.Phase)
                .Where(p => p == StrokePhase.Catch || p == StrokePhase.Finish)
                .ToList();

            // Catches and finishes must strictly alternate. Two catches in a row means the
            // detector double-triggered on noise around the catch.
            for (var i = 1; i < phases.Count; i++)
            {
                Assert.That(phases[i], Is.Not.EqualTo(phases[i - 1]),
                    $"two consecutive {phases[i]} events at index {i}");
            }
        }

        [TestCase(18f)]
        [TestCase(24f)]
        [TestCase(32f)]
        public void ReportsStrokeRateWithinOneSpm(float spm)
        {
            var clip = SyntheticRower.Generate(strokesPerMinute: spm, strokes: 10);
            var states = SyntheticRower.Run(clip);

            var reported = states.Last().Metrics.StrokesPerMinute;

            Assert.That(reported, Is.EqualTo(spm).Within(1.0f),
                $"reported {reported:F2} spm for a {spm:F0} spm clip");
        }

        [Test]
        public void ReportsRecoveryToDriveRatio()
        {
            // A drive fraction of 1/3 means the recovery takes twice as long as the drive:
            // the 1:2 ratio a coach would ask for.
            var clip = SyntheticRower.Generate(strokes: 10, driveFraction: 1f / 3f);
            var metrics = SyntheticRower.Run(clip).Last().Metrics;

            // The measured ratio sits a little under the true 2.0 and always will: detection
            // is causal, so a turn can only be confirmed once the handle is clearly moving
            // the other way, and the filtered signal lingers longest at the finish, where
            // the recovery pulls away gently. Timestamping at the turning point removes most
            // of that (it was 1.47 before), and filter tuning most of the rest; roughly 7%
            // is inherent. This test pins the bias down so a regression widens it visibly.
            Assert.That(metrics.RecoveryToDriveRatio, Is.EqualTo(1.86f).Within(0.12f),
                $"ratio was 1:{metrics.RecoveryToDriveRatio:F2}");
        }

        [Test]
        public void ReportsStrokeLengthCloseToActualTravel()
        {
            const float amplitude = 0.75f;
            var clip = SyntheticRower.Generate(strokes: 10, amplitude: amplitude);
            var metrics = SyntheticRower.Run(clip).Last().Metrics;

            // Filtering rounds off the extremes slightly, so the measured length is expected
            // to fall a little short of the true travel rather than match it exactly.
            Assert.That(metrics.StrokeLengthMetres, Is.EqualTo(amplitude).Within(0.12f),
                $"measured {metrics.StrokeLengthMetres:F3} m against {amplitude:F3} m of travel");
        }

        [Test]
        public void StaysUnknownWhenTheRowerIsIdle()
        {
            // Hands present and drifting slightly, but nothing resembling a stroke.
            var clip = SyntheticRower.Generate(strokes: 6, amplitude: 0.05f);
            var states = SyntheticRower.Run(clip);

            Assert.That(states.All(s => s.Phase == StrokePhase.Unknown), Is.True,
                "idle drift was scored as rowing");
            Assert.That(states.Last().Metrics.CompletedStrokes, Is.Zero);
        }

        [Test]
        public void SurvivesBodySwing()
        {
            // The regression that motivates the seat reference frame: if the detector used
            // the headset as its origin, a body swinging with the stroke would cancel most
            // of the handle's travel and the stroke count would collapse.
            var still = SyntheticRower.Generate(strokes: 10, headSwingMetres: 0f);
            var swinging = SyntheticRower.Generate(strokes: 10, headSwingMetres: 0.25f);

            var stillStrokes = SyntheticRower.Run(still).Last().Metrics.CompletedStrokes;
            var swingStrokes = SyntheticRower.Run(swinging).Last().Metrics.CompletedStrokes;

            Assert.That(swingStrokes, Is.EqualTo(stillStrokes).Within(1),
                $"{stillStrokes} strokes sitting still but {swingStrokes} while swinging");
        }

        [Test]
        public void ToleratesHandTrackingNoise()
        {
            // 8 mm of per-axis jitter is a pessimistic stand-in for Quest 3 hand tracking
            // at the extremes of the stroke, where the hands are furthest from the cameras.
            var clean = SyntheticRower.Generate(strokes: 12, noiseMetres: 0f);
            var noisy = SyntheticRower.Generate(strokes: 12, noiseMetres: 0.008f);

            var cleanStrokes = SyntheticRower.Run(clean).Last().Metrics.CompletedStrokes;
            var noisyStrokes = SyntheticRower.Run(noisy).Last().Metrics.CompletedStrokes;

            Assert.That(noisyStrokes, Is.EqualTo(cleanStrokes).Within(1),
                $"{cleanStrokes} strokes clean but {noisyStrokes} with noise");
        }

        [Test]
        public void IgnoresDuplicateAndRewoundTimestamps()
        {
            // Replaying a clip, or a dropped frame on device, can deliver the same timestamp
            // twice. That must not divide by zero or poison the filter with a NaN.
            var detector = new StrokeDetector();
            var clip = SyntheticRower.Generate(strokes: 4);

            foreach (var s in clip.Samples)
            {
                detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
                var back = detector.Update(s.TimeSeconds - 0.5d, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);

                Assert.That(float.IsNaN(back.HandlePosition), Is.False, "handle position went NaN");
                Assert.That(float.IsNaN(back.HandleVelocity), Is.False, "handle velocity went NaN");
            }
        }

        [Test]
        public void VelocityIsNegativeDuringTheDrive()
        {
            // The sign convention the whole state machine rests on: the drive pulls the
            // handle toward the body, so position along the drive axis decreases.
            var clip = SyntheticRower.Generate(strokes: 8);
            var states = SyntheticRower.Run(clip);

            var driveVelocities = states
                .Where(s => s.Phase == StrokePhase.Drive)
                .Select(s => s.HandleVelocity)
                .ToList();

            Assert.That(driveVelocities, Is.Not.Empty);

            // The invariant: the drive is unambiguously a pull, not a wobble around zero.
            Assert.That(driveVelocities.Average(), Is.LessThan(-0.3f),
                $"mean drive velocity was {driveVelocities.Average():F3} m/s");

            // Not every sample, though, and that is not a defect. Detection is causal: the
            // samples at the tail of the Drive label are ones where the handle has already
            // turned but the finish is not yet confirmed. This bounds that tail rather than
            // pretending it away - if it grows, detection latency has regressed.
            var negativeFraction = driveVelocities.Count(v => v < 0f) / (float)driveVelocities.Count;
            Assert.That(negativeFraction, Is.GreaterThan(0.75f),
                $"only {negativeFraction:P0} of drive samples had negative velocity");
        }

        [Test]
        public void ResetClearsAllState()
        {
            var clip = SyntheticRower.Generate(strokes: 6);
            var detector = new StrokeDetector();
            foreach (var s in clip.Samples)
            {
                detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward);
            }

            Assert.That(detector.Metrics.CompletedStrokes, Is.GreaterThan(0));

            detector.Reset();

            Assert.That(detector.Phase, Is.EqualTo(StrokePhase.Unknown));
            Assert.That(detector.Metrics.CompletedStrokes, Is.Zero);
        }
    }
}
