using System;
using System.Collections.Generic;

namespace RowVR.Stroke
{
    public enum SectionKind
    {
        /// <summary>Calm water between rapids: rest, no line to hold.</summary>
        Pool,

        /// <summary>White water: faster current, and a line held by power.</summary>
        Rapid,
    }

    /// <summary>One stretch of the river.</summary>
    public readonly struct RiverSection
    {
        public readonly SectionKind Kind;
        public readonly float LengthMetres;

        /// <summary>Average power, W, that holds the good line through a rapid. Unused in a pool.</summary>
        public readonly float TargetWatts;

        /// <summary>Speed of the water, m/s, added to the raft's own.</summary>
        public readonly float CurrentMetresPerSecond;

        /// <summary>Which bank the rapid pushes a weak raft toward: -1 left, +1 right.</summary>
        public readonly int DriftSide;

        public RiverSection(SectionKind kind, float length, float targetWatts, float current, int driftSide)
        {
            Kind = kind;
            LengthMetres = length;
            TargetWatts = targetWatts;
            CurrentMetresPerSecond = current;
            DriftSide = driftSide < 0 ? -1 : 1;
        }
    }

    /// <summary>A rapid's result, for the run summary.</summary>
    public readonly struct RapidResult
    {
        public readonly double Seconds;

        /// <summary>Fraction of the rapid spent on the good line, 0-1.</summary>
        public readonly float Clean;

        public RapidResult(double seconds, float clean)
        {
            Seconds = seconds;
            Clean = clean;
        }
    }

    /// <summary>
    /// A rapids run (REQ-0007): the river as a list of sections, and the rules of the game.
    ///
    /// The raft moves at the boat's speed plus the current, which is faster in rapids. In a
    /// rapid, the rower's average power (over a few seconds) against the section's target
    /// decides the line: short of the target, the raft drifts toward one bank; at or above it,
    /// it comes back to the good line. Well off the line is rough water, which costs speed.
    /// Each rapid scores the fraction of its time spent on the line.
    ///
    /// Comfort limits from ADR-0012 are built in, not left to the scene: sideways drift is at
    /// most <see cref="RapidsSettings.MaxDriftSpeed"/> and eased, never beyond
    /// <see cref="RapidsSettings.MaxOffsetMetres"/>, and the current changes smoothly between
    /// sections. Engine-free and tested (RapidsRunTests).
    /// </summary>
    public sealed class RapidsRun
    {
        readonly RapidsSettings _settings;
        readonly List<RiverSection> _sections;
        readonly List<RapidResult> _results = new List<RapidResult>();
        double _lastTime = double.NaN;
        float _lateralSpeed;
        double _rapidSeconds;
        double _rapidCleanSeconds;
        int _rapidIndex = -1;

        public RapidsRun(IList<RiverSection> sections, RapidsSettings settings = null)
        {
            if (sections == null || sections.Count == 0) throw new ArgumentException("a run needs at least one section");
            _sections = new List<RiverSection>(sections);
            _settings = settings ?? new RapidsSettings();
            RapidCount = 0;
            foreach (var s in _sections)
            {
                TotalMetres += s.LengthMetres;
                if (s.Kind == SectionKind.Rapid) RapidCount++;
            }
            Current = _sections[0].CurrentMetresPerSecond;
        }

        public RapidsSettings Settings => _settings;
        public IReadOnlyList<RiverSection> Sections => _sections;
        public double TotalMetres { get; }

        /// <summary>Metres down the river.</summary>
        public double Distance { get; private set; }

        /// <summary>Seconds since the raft first moved.</summary>
        public double Elapsed { get; private set; }

        public bool Started { get; private set; }
        public bool Finished { get; private set; }

        /// <summary>Sideways distance from the good line, metres; positive toward the right bank.</summary>
        public float Offset { get; private set; }

        /// <summary>The current as eased toward the section's, m/s.</summary>
        public float Current { get; private set; }

        /// <summary>Speed down the river, m/s: the raft's own plus the current, less rough water.</summary>
        public float GroundSpeed { get; private set; }

        /// <summary>The rower's power averaged over the last few seconds, W.</summary>
        public float AveragePower { get; private set; }

        public int SectionIndex { get; private set; }
        public RiverSection Section => _sections[SectionIndex];
        public bool InRapid => !Finished && Section.Kind == SectionKind.Rapid;
        public bool OnLine => Math.Abs(Offset) <= _settings.CleanOffsetMetres;

        /// <summary>Metres into the current section.</summary>
        public double SectionDistance { get; private set; }

        /// <summary>How many rapids there are, and which one this is (1-based, 0 before the first).</summary>
        public int RapidCount { get; }
        public int RapidNumber => _rapidIndex + 1;

        /// <summary>Fraction of the current (or last) rapid spent on the line.</summary>
        public float CurrentClean => _rapidSeconds > 0d ? (float)(_rapidCleanSeconds / _rapidSeconds) : 1f;

        public IReadOnlyList<RapidResult> Results => _results;

        /// <summary>
        /// Advances the run. <paramref name="raftSpeed"/> is the boat model's visible speed
        /// (ADR-0010), <paramref name="power"/> its instantaneous power, W.
        /// </summary>
        public void Update(double timeSeconds, float raftSpeed, float power)
        {
            var dt = double.IsNaN(_lastTime) ? 0f : (float)Math.Min(0.25d, Math.Max(0d, timeSeconds - _lastTime));
            _lastTime = timeSeconds;
            if (dt <= 0f || Finished) return;

            if (!Started && raftSpeed > 0.1f) Started = true;
            if (!Started) return;

            Elapsed += dt;
            AveragePower += (power - AveragePower) * Alpha(dt, _settings.PowerWindowSeconds);

            var section = Section;
            Current += (section.CurrentMetresPerSecond - Current) * Alpha(dt, _settings.CurrentEaseSeconds);

            // The line. Short of the target in a rapid, drift toward the section's bank, faster
            // the further short; otherwise come back to the line.
            float targetLateral;
            if (section.Kind == SectionKind.Rapid && AveragePower < section.TargetWatts)
            {
                var shortfall = Clamp01((section.TargetWatts - AveragePower) / section.TargetWatts);
                targetLateral = section.DriftSide * _settings.MaxDriftSpeed * (0.35f + 0.65f * shortfall);
            }
            else
            {
                targetLateral = -Math.Sign(Offset) * Math.Min(_settings.MaxDriftSpeed, Math.Abs(Offset) * 0.8f);
            }
            // Brake before the edge: no faster than it can still stop from in the room left,
            // so it arrives at the limit at rest instead of being stopped dead there.
            if (targetLateral != 0f)
            {
                // Aim to stop a little short of the limit, so the hard clamp below is only a
                // safety net and never the thing that stops the drift.
                var room = Math.Max(0f, _settings.MaxOffsetMetres - 0.05f - Offset * Math.Sign(targetLateral));
                var stoppable = (float)Math.Sqrt(2f * _settings.MaxDriftAcceleration * room);
                targetLateral = Math.Sign(targetLateral) * Math.Min(Math.Abs(targetLateral), stoppable);
            }

            // Eased: sideways speed changes at a limited rate, never a jolt.
            var change = _settings.MaxDriftAcceleration * dt;
            _lateralSpeed += Math.Max(-change, Math.Min(change, targetLateral - _lateralSpeed));
            _lateralSpeed = Math.Max(-_settings.MaxDriftSpeed, Math.Min(_settings.MaxDriftSpeed, _lateralSpeed));
            var offset = Offset + _lateralSpeed * dt;
            if (Math.Abs(offset) >= _settings.MaxOffsetMetres)
            {
                offset = Math.Sign(offset) * _settings.MaxOffsetMetres;
                _lateralSpeed = 0f;
            }
            Offset = offset;

            var rough = section.Kind == SectionKind.Rapid && !OnLine;
            GroundSpeed = Math.Max(0f, raftSpeed + Current) * (rough ? _settings.RoughWaterSpeed : 1f);
            Distance += GroundSpeed * dt;

            if (section.Kind == SectionKind.Rapid)
            {
                _rapidSeconds += dt;
                if (OnLine) _rapidCleanSeconds += dt;
            }

            Advance();
        }

        void Advance()
        {
            var start = 0d;
            for (var i = 0; i < _sections.Count; i++)
            {
                var end = start + _sections[i].LengthMetres;
                if (Distance < end || i == _sections.Count - 1)
                {
                    if (i != SectionIndex) EnterSection(i);
                    SectionDistance = Math.Min(Distance, end) - start;
                    if (Distance >= end && i == _sections.Count - 1) Finish();
                    return;
                }
                start = end;
            }
        }

        void EnterSection(int index)
        {
            if (_sections[SectionIndex].Kind == SectionKind.Rapid) CloseRapid();
            SectionIndex = index;
            if (_sections[index].Kind == SectionKind.Rapid)
            {
                _rapidIndex++;
                _rapidSeconds = 0d;
                _rapidCleanSeconds = 0d;
            }
        }

        void CloseRapid() => _results.Add(new RapidResult(_rapidSeconds, CurrentClean));

        void Finish()
        {
            if (Section.Kind == SectionKind.Rapid) CloseRapid();
            Finished = true;
            GroundSpeed = 0f;
        }

        /// <summary>A standard run: three rapids, each longer and harder, with pools between.</summary>
        public static List<RiverSection> Standard(float targetWatts = 110f)
        {
            return new List<RiverSection>
            {
                new RiverSection(SectionKind.Pool, 150f, 0f, 0.3f, 1),
                new RiverSection(SectionKind.Rapid, 180f, targetWatts * 0.85f, 1.1f, 1),
                new RiverSection(SectionKind.Pool, 200f, 0f, 0.3f, 1),
                new RiverSection(SectionKind.Rapid, 240f, targetWatts, 1.3f, -1),
                new RiverSection(SectionKind.Pool, 200f, 0f, 0.3f, 1),
                new RiverSection(SectionKind.Rapid, 300f, targetWatts * 1.1f, 1.5f, 1),
                new RiverSection(SectionKind.Pool, 150f, 0f, 0.2f, 1),
            };
        }

        static float Alpha(float dt, float seconds) => 1f - (float)Math.Exp(-dt / Math.Max(0.01f, seconds));
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>Tuning for <see cref="RapidsRun"/>. The comfort limits come from ADR-0012.</summary>
    public sealed class RapidsSettings
    {
        /// <summary>Fastest sideways drift, m/s (ADR-0012: at most 0.4).</summary>
        public float MaxDriftSpeed = 0.4f;

        /// <summary>How fast sideways speed may change, m/s^2: drift eases in and out.</summary>
        public float MaxDriftAcceleration = 0.25f;

        /// <summary>Furthest off the line the raft goes, metres (ADR-0012: at most 3).</summary>
        public float MaxOffsetMetres = 3f;

        /// <summary>Within this far of the line counts as on it.</summary>
        public float CleanOffsetMetres = 1f;

        /// <summary>Seconds over which power is averaged for the line: a few strokes.</summary>
        public float PowerWindowSeconds = 4f;

        /// <summary>Time constant, seconds, of the current easing between sections.</summary>
        public float CurrentEaseSeconds = 4f;

        /// <summary>Speed multiplier in rough water, off the line.</summary>
        public float RoughWaterSpeed = 0.75f;
    }
}
