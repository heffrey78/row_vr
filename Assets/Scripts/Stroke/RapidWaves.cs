using System;
using System.Collections.Generic;

namespace RowVR.Stroke
{
    /// <summary>
    /// One rapid's standing waves as a height field (#30, #90): smooth crests across the river
    /// every few metres, each a ridge whose height and position vary gently along it, taller
    /// toward the side a weak raft drifts to and low on the good line.
    ///
    /// The river draws its crests from this, and the raft rocks on it, so what tips the raft
    /// is what the rower sees. Section space: +Z downstream from the rapid's start, X across
    /// (positive to the right looking downstream), heights in metres above the flat water.
    /// The same section and seed always give the same waves.
    /// </summary>
    public sealed class RapidWaves
    {
        /// <summary>How far upstream of its line a crest's face begins, metres.</summary>
        public const float UpstreamFace = 0.6f;

        /// <summary>How far downstream of its line a crest's back runs out, metres.</summary>
        public const float DownstreamFace = 1f;

        /// <summary>Height of a crest's foot: just above the water, so it never flickers into it.</summary>
        public const float Foot = 0.02f;

        const float LowOnTheLine = 0.08f;
        const float HighOnTheDriftSide = 0.34f;
        const float RoughWithinMetres = 6f;

        readonly List<float> _lines = new List<float>();
        readonly List<float> _phases = new List<float>();
        readonly int _driftSide;

        public RapidWaves(RiverSection section, float riverHalfWidth, int seed)
        {
            HalfWidth = riverHalfWidth;
            Span = riverHalfWidth * 0.8f;
            Length = section.LengthMetres;
            _driftSide = section.DriftSide;
            var random = new Random(seed);
            for (var z = 8f; z < Length - 4f; z += 5f + (float)random.NextDouble() * 4f)
            {
                _lines.Add(z);
                _phases.Add((float)random.NextDouble() * 6.28f);
            }
        }

        public float HalfWidth { get; }

        /// <summary>Crests run from -Span to +Span across the river.</summary>
        public float Span { get; }

        public float Length { get; }
        public int CrestCount => _lines.Count;

        /// <summary>Where crest <paramref name="i"/> stands at <paramref name="x"/> across: its line wanders a little.</summary>
        public float CrestZ(int i, float x) => _lines[i] + (float)Math.Sin(x * 0.18f + _phases[i]) * 0.8f;

        /// <summary>Crest <paramref name="i"/>'s height at <paramref name="x"/> across; 0 where it is broken or past its ends.</summary>
        public float CrestHeight(int i, float x) => Math.Abs(x) > Span || IsBroken(i, x) ? 0f : RidgeHeight(i, x);

        /// <summary>The height crest <paramref name="i"/>'s ridge would have at <paramref name="x"/>, ignoring its breaks.</summary>
        public float RidgeHeight(int i, float x)
        {
            var phase = _phases[i];
            // Calm-ish on the line, rough within a few metres of it on the drift side (as far as
            // a weak raft drifts), low on the far side.
            var towardDrift = Clamp01(0.35f + x * _driftSide / RoughWithinMetres);
            return (LowOnTheLine + (HighOnTheDriftSide - LowOnTheLine) * towardDrift)
                   * (0.85f + 0.15f * (float)Math.Sin(x * 0.5f + phase))
                   * Clamp01(1.2f - Math.Abs(x) / Span);
        }

        /// <summary>Whether crest <paramref name="i"/> is broken at <paramref name="x"/>: real standing waves are never one unbroken ridge.</summary>
        public bool IsBroken(int i, float x) => Math.Sin(x * 0.9f + _phases[i] * 3f) > 0.55f;

        /// <summary>The water's height above flat at a point in the rapid; 0 between crests and outside it.</summary>
        public float Height(float x, float z)
        {
            if (z < 0f || z > Length) return 0f;
            var height = 0f;
            for (var i = 0; i < _lines.Count; i++)
            {
                var line = CrestZ(i, x);
                if (z < line - UpstreamFace) break;  // lines run downstream, and wander less than the gap
                if (z > line + DownstreamFace) continue;
                var top = CrestHeight(i, x);
                if (top <= 0f) continue;
                var h = z <= line
                    ? Foot + (top - Foot) * (z - (line - UpstreamFace)) / UpstreamFace
                    : top + (Foot - top) * (z - line) / DownstreamFace;
                if (h > height) height = h;
            }
            return height;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// The standing waves of a whole run: each rapid's <see cref="RapidWaves"/> at its place on
    /// the river, and flat water in the pools.
    /// </summary>
    public sealed class RiverWaves
    {
        readonly List<(double Start, RapidWaves Waves)> _rapids = new List<(double, RapidWaves)>();

        /// <param name="firstSeed">The first rapid's seed; each later rapid takes the next.</param>
        public RiverWaves(IList<RiverSection> sections, float riverHalfWidth, int firstSeed = 901)
        {
            var start = 0d;
            var seed = firstSeed;
            foreach (var section in sections)
            {
                if (section.Kind == SectionKind.Rapid)
                    _rapids.Add((start, new RapidWaves(section, riverHalfWidth, seed++)));
                start += section.LengthMetres;
            }
        }

        /// <summary>The rapids, in order down the river, with the distance each starts at.</summary>
        public IReadOnlyList<(double Start, RapidWaves Waves)> Rapids => _rapids;

        /// <summary>The water's height above flat at <paramref name="x"/> across and <paramref name="distance"/> down the river.</summary>
        public float Height(float x, double distance)
        {
            foreach (var (start, waves) in _rapids)
                if (distance >= start && distance <= start + waves.Length)
                    return waves.Height(x, (float)(distance - start));
            return 0f;
        }
    }
}
