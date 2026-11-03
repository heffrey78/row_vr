using System;
using System.Collections.Generic;

namespace RowVR.Stroke
{
    /// <summary>One cloud puff as drawn: centre and radius in block units, line-local.</summary>
    public readonly struct CloudPuff
    {
        public readonly Vec3 Position;
        public readonly float Radius;

        public CloudPuff(Vec3 position, float radius)
        {
            Position = position;
            Radius = radius;
        }
    }

    /// <summary>
    /// A number written in cloud (#102): the puffs that draw a string in BlockFont's shapes, and
    /// how they move when it changes.
    ///
    /// A puff sits in each filled cell, and smaller ones join neighbouring cells so the strokes
    /// run together. When the text changes, existing puffs drift to the nearest new places
    /// (closest pairs first, so an unchanged digit keeps its own puffs), spare ones rise and
    /// evaporate, and new ones bloom out of the nearest puff. Every puff billows slowly on its
    /// own phase. Nothing jumps.
    ///
    /// Units are blocks: one cell of the font is 1. The line is centred on x = 0 and y = 0, and
    /// puffs sit at small depths (z, toward the viewer is negative) so overlaps draw in a fixed
    /// order. The renderer scales by the block size.
    /// </summary>
    public sealed class CloudNumerals
    {
        /// <summary>Time constant, seconds, for a puff to drift to its new place.</summary>
        public float MoveSeconds = 0.3f;

        /// <summary>Time constant, seconds, for a puff to grow or evaporate.</summary>
        public float GrowSeconds = 0.2f;

        /// <summary>How far an evaporating puff rises, blocks.</summary>
        public float RiseBlocks = 1.2f;

        /// <summary>Billow: share of a puff's radius it swells and shrinks by.</summary>
        public float Billow = 0.07f;

        /// <summary>Radius of a cell's puff and of a joining puff, blocks.</summary>
        public const float CellRadius = 0.74f;

        /// <summary>Space between digits, blocks: wider than the font's, since puffs spill over their cells.</summary>
        public const float LetterGap = 1.7f;
        public const float JoinRadius = 0.62f;

        const float Gone = 0.01f;
        const int DepthLayers = 12;

        sealed class Puff
        {
            public Vec3 Position;
            public float Radius;
            public Vec3 Target;
            public float TargetRadius;
            public float Phase;
            public float Depth;
            public bool Leaving;
        }

        readonly List<Puff> _puffs = new List<Puff>();
        readonly List<CloudPuff> _drawn = new List<CloudPuff>();
        int _created;
        double _time;
        string _text;

        /// <summary>The puffs as they should be drawn now, billow included.</summary>
        public IReadOnlyList<CloudPuff> Puffs => _drawn;

        /// <summary>Puffs still forming the number (not evaporating).</summary>
        public int Settled
        {
            get
            {
                var n = 0;
                foreach (var p in _puffs) if (!p.Leaving) n++;
                return n;
            }
        }

        /// <summary>Where the puffs of a string sit once settled: centre and radius, blocks.</summary>
        public static List<CloudPuff> Layout(string text)
        {
            var puffs = new List<CloudPuff>();
            if (string.IsNullOrEmpty(text)) return puffs;

            var left = -Width(text) * 0.5f;
            var x0 = left;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                for (var row = 0; row < BlockFont.Rows; row++)
                for (var column = 0; column < BlockFont.Width(c); column++)
                {
                    if (!BlockFont.Filled(c, column, row)) continue;
                    var centre = Cell(x0, column, row);
                    var seed = Hash(i, column, row, c);
                    puffs.Add(new CloudPuff(centre + Jitter(seed, 0.05f), CellRadius + Signed(seed >> 8) * 0.04f));

                    // Join to the right and downward neighbours, so strokes read as one cloud.
                    if (BlockFont.Filled(c, column + 1, row))
                        puffs.Add(new CloudPuff((centre + Cell(x0, column + 1, row)) * 0.5f + Jitter(seed >> 4, 0.03f), JoinRadius));
                    if (BlockFont.Filled(c, column, row + 1))
                        puffs.Add(new CloudPuff((centre + Cell(x0, column, row + 1)) * 0.5f + Jitter(seed >> 12, 0.03f), JoinRadius));
                }
                x0 += BlockFont.Width(c) + LetterGap;
            }
            return puffs;
        }

        /// <summary>Width of a string's lettering, blocks.</summary>
        public static float Width(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var columns = 0f;
            foreach (var c in text) columns += BlockFont.Width(c);
            return columns + LetterGap * (text.Length - 1);
        }

        /// <summary>Changes the number. The same text again changes nothing.</summary>
        public void SetText(string text)
        {
            if (text == _text) return;
            _text = text;
            var targets = Layout(text);

            // Closest pairs first: puffs already in place keep their place.
            var live = new List<Puff>();
            foreach (var p in _puffs) if (!p.Leaving) live.Add(p);
            var pairs = new List<(float Distance, int Puff, int Target)>(live.Count * targets.Count);
            for (var p = 0; p < live.Count; p++)
                for (var t = 0; t < targets.Count; t++)
                    pairs.Add((Flat(live[p].Target, targets[t].Position), p, t));
            pairs.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var puffTaken = new bool[live.Count];
            var targetTaken = new bool[targets.Count];
            foreach (var (_, p, t) in pairs)
            {
                if (puffTaken[p] || targetTaken[t]) continue;
                puffTaken[p] = targetTaken[t] = true;
                live[p].Target = WithDepth(targets[t].Position, live[p].Depth);
                live[p].TargetRadius = targets[t].Radius;
            }

            // Spares evaporate upward.
            for (var p = 0; p < live.Count; p++)
            {
                if (puffTaken[p]) continue;
                live[p].Leaving = true;
                live[p].TargetRadius = 0f;
                live[p].Target = live[p].Position + new Vec3(0f, RiseBlocks, 0f);
            }

            // New puffs bloom from the nearest puff already there.
            for (var t = 0; t < targets.Count; t++)
            {
                if (targetTaken[t]) continue;
                var from = targets[t].Position;
                var best = float.MaxValue;
                foreach (var p in live)
                {
                    var d = Flat(p.Position, targets[t].Position);
                    if (d < best) { best = d; from = p.Position; }
                }
                var depth = -(_created % DepthLayers) * 0.06f;
                _puffs.Add(new Puff
                {
                    Position = WithDepth(from, depth),
                    Radius = 0f,
                    Target = WithDepth(targets[t].Position, depth),
                    TargetRadius = targets[t].Radius,
                    Phase = (_created * 2.399963f) % 6.2831853f,
                    Depth = depth,
                });
                _created++;
            }
        }

        public void Update(double timeSeconds, float deltaSeconds)
        {
            _time = timeSeconds;
            var move = 1f - (float)Math.Exp(-Math.Max(0f, deltaSeconds) / Math.Max(0.01f, MoveSeconds));
            var grow = 1f - (float)Math.Exp(-Math.Max(0f, deltaSeconds) / Math.Max(0.01f, GrowSeconds));

            for (var i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.Position = Vec3.Lerp(p.Position, p.Target, move);
                p.Radius += (p.TargetRadius - p.Radius) * grow;
                if (p.Leaving && p.Radius < Gone) _puffs.RemoveAt(i);
            }

            _drawn.Clear();
            foreach (var p in _puffs)
            {
                var swell = 1f + Billow * (float)Math.Sin(_time * 0.9 + p.Phase);
                var drift = new Vec3((float)Math.Sin(_time * 0.37 + p.Phase * 1.7) * 0.04f,
                                     (float)Math.Sin(_time * 0.29 + p.Phase * 2.3) * 0.04f, 0f);
                _drawn.Add(new CloudPuff(p.Position + drift, p.Radius * swell));
            }
        }

        static Vec3 Cell(float x0, int column, int row) =>
            new Vec3(x0 + column + 0.5f, BlockFont.Rows * 0.5f - row - 0.5f, 0f);

        // Squared distance across the sky, ignoring the puffs' draw-order depth.
        static float Flat(Vec3 a, Vec3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

        static Vec3 WithDepth(Vec3 v, float depth) => new Vec3(v.X, v.Y, depth);

        static int Hash(int index, int column, int row, char c)
        {
            unchecked
            {
                var h = (int)2166136261;
                h = (h ^ index) * 16777619;
                h = (h ^ column) * 16777619;
                h = (h ^ row) * 16777619;
                h = (h ^ c) * 16777619;
                return h & 0x7fffffff;
            }
        }

        static float Signed(int bits) => (bits & 0xff) / 127.5f - 1f;

        static Vec3 Jitter(int seed, float amount) =>
            new Vec3(Signed(seed) * amount, Signed(seed >> 16) * amount, 0f);
    }
}
