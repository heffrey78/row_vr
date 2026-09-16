using System;
using System.Collections.Generic;
using RowVR.Stroke;

namespace RowVR.Stroke.Tests
{
    /// <summary>
    /// Generates rowing motion analytically, so the detector can be tested before any real
    /// clip has been recorded and against motion whose true answer is known exactly.
    ///
    /// The handle path is a phase-warped cosine rather than a plain sinusoid, because real
    /// rowing is asymmetric: the drive is roughly half the duration of the recovery. A plain
    /// sinusoid would make the drive/recovery ratio trivially 1.0 and would not exercise the
    /// part of the detector that matters. The cosine's zero derivative at both ends is also
    /// physically right: the handle genuinely does pause at the catch and at the finish.
    /// </summary>
    public static class SyntheticRower
    {
        public const float DefaultAmplitude = 0.75f;   // metres of handle travel
        public const float DefaultDriveFraction = 1f / 3f;

        public static HandClip Generate(
            float strokesPerMinute = 24f,
            int strokes = 8,
            float amplitude = DefaultAmplitude,
            float driveFraction = DefaultDriveFraction,
            float sampleRateHz = 72f,
            float noiseMetres = 0f,
            float headSwingMetres = 0f,
            int seed = 12345)
        {
            var rng = new Random(seed);
            var cycle = 60f / strokesPerMinute;
            var total = cycle * strokes;
            var dt = 1f / sampleRateHz;

            var clip = new HandClip
            {
                Note = $"synthetic {strokesPerMinute:F1} spm, {strokes} strokes, " +
                       $"drive fraction {driveFraction:F2}, noise {noiseMetres:F3} m",
                RecordedAtIso = "1970-01-01T00:00:00Z",
            };

            var samples = (int)Math.Round(total / dt);
            for (var i = 0; i <= samples; i++)
            {
                var t = i * dt;
                var s = HandleOffset(t, cycle, amplitude, driveFraction);

                // The rower faces +Z. Handle travels along that axis; hands sit either side.
                var handle = new Vec3(0f, 0.95f, s);
                var left = handle + new Vec3(-0.22f, 0f, 0f) + Jitter(rng, noiseMetres);
                var right = handle + new Vec3(0.22f, 0f, 0f) + Jitter(rng, noiseMetres);

                // The body swings with the stroke. This is the case that breaks a detector
                // which uses the headset as its origin: the swing cancels handle travel.
                var swing = headSwingMetres * (s / Math.Max(1e-6f, amplitude));
                var head = new Vec3(0f, 1.35f, swing);

                clip.Add(new HandSample(t, left, right, head, Vec3.Forward));
            }

            return clip;
        }

        /// <summary>
        /// Handle offset along the drive axis: <paramref name="amplitude"/> at the catch,
        /// 0 at the finish.
        /// </summary>
        public static float HandleOffset(float t, float cycle, float amplitude, float driveFraction)
        {
            var phase = (t % cycle) / cycle;           // 0 at the catch
            var half = amplitude * 0.5f;

            if (phase < driveFraction)
            {
                var u = phase / driveFraction;          // 0 -> 1 across the drive
                return half + half * (float)Math.Cos(Math.PI * u);
            }

            var r = (phase - driveFraction) / (1f - driveFraction); // 0 -> 1 across the recovery
            return half + half * (float)Math.Cos(Math.PI * (1f + r));
        }

        static Vec3 Jitter(Random rng, float metres)
        {
            if (metres <= 0f) return Vec3.Zero;
            return new Vec3(
                (float)(rng.NextDouble() * 2d - 1d) * metres,
                (float)(rng.NextDouble() * 2d - 1d) * metres,
                (float)(rng.NextDouble() * 2d - 1d) * metres);
        }

        /// <summary>Runs a clip through a detector and returns every state it produced.</summary>
        public static List<StrokeState> Run(HandClip clip, StrokeDetectorSettings settings = null)
        {
            var detector = new StrokeDetector(settings);
            var states = new List<StrokeState>(clip.Samples.Count);
            foreach (var s in clip.Samples)
            {
                states.Add(detector.Update(s.TimeSeconds, s.LeftHand, s.RightHand, s.HeadPosition, s.HeadForward));
            }
            return states;
        }

        public static int CountPhase(IEnumerable<StrokeState> states, StrokePhase phase)
        {
            var n = 0;
            foreach (var s in states) if (s.Phase == phase) n++;
            return n;
        }
    }
}
