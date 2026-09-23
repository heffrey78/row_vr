namespace RowVR.Stroke
{
    /// <summary>
    /// Tuning for <see cref="StrokeDetector"/>. The defaults are a starting point for a
    /// seated adult on a Quest 3; retune them against a recorded clip rather than by
    /// putting the headset back on (see docs/dev-loop.md, tier 1).
    /// </summary>
    public sealed class StrokeDetectorSettings
    {
        /// <summary>One Euro filter minimum cutoff, Hz. Raise if the handle jitters when nearly still.</summary>
        public float FilterMinCutoff = 1.0f;

        /// <summary>
        /// One Euro filter speed coefficient. This is the main noise-versus-lag dial, and
        /// the two costs pull in opposite directions: too low and filter lag stretches the
        /// measured drive (the handle leaves the finish slowly, so the filtered minimum
        /// lingers there); too high and hand-tracking jitter at the catch gets counted as
        /// extra strokes. 0.5 was chosen by sweeping against synthetic motion of known
        /// timing - it keeps the stroke count exact under 8 mm of jitter while holding the
        /// residual ratio bias to about 7%.
        /// </summary>
        public float FilterBeta = 1.0f;

        /// <summary>
        /// Handle speed, m/s, needed to call a phase change. Below this the handle is
        /// treated as hovering rather than driving, which is what keeps tracker noise at
        /// the catch from registering as a stroke.
        /// </summary>
        public float VelocityThreshold = 0.15f;

        /// <summary>
        /// Shortest believable phase, seconds. A phase change inside this window of the
        /// previous one is rejected. 24 spm is a 2.5s cycle, so even a very sharp drive
        /// stays well above this; anything faster is noise, not rowing.
        /// </summary>
        public float MinPhaseSeconds = 0.15f;

        /// <summary>Normalized position above which a catch may be called (1 = fullest reach seen).</summary>
        public float CatchZone = 0.55f;

        /// <summary>Normalized position below which a finish may be called (0 = closest to the body seen).</summary>
        public float FinishZone = 0.45f;

        /// <summary>
        /// Handle travel, metres, that must be observed before the detector will report any
        /// phase at all. Stops idle hand-waving from being scored as rowing.
        /// </summary>
        public float MinStrokeLengthMetres = 0.25f;

        /// <summary>
        /// Seconds the handle must stay below <see cref="VelocityThreshold"/> for the rower
        /// to count as having stopped. A pause does three things: the stroke rate stops
        /// measuring across it (a 3s freeze otherwise reads as ~11 spm), small movements
        /// after it must build up a fresh <see cref="MinStrokeLengthMetres"/> of travel before
        /// anything counts (hands coming up off the lap were being scored as a stroke), and a
        /// drive away from a pause held at the catch position is counted as that stroke's
        /// catch (the first stroke from rest was never counted).
        ///
        /// Swept against the real Quest 3 recordings in tests/RowVR.Stroke.Tests/fixtures:
        /// every count comes out right for 1.5s to 3.0s. At 1.0s and below, hands coming up
        /// off the lap after a rest are scored as a stroke again. At 4.0s, a ~3s freeze and
        /// a ~3s rest before rowing are too short to register, so the first stroke from rest
        /// is lost and the rate after a freeze reads ~11 spm. 2s sits mid-band. Re-sweep if
        /// you change it - the band is set by those recordings, not by theory.
        /// </summary>
        public float PauseSeconds = 2.0f;

        /// <summary>
        /// Half-life in seconds for the observed range (min/max handle position). The range
        /// has to decay or one freak reach would raise the ceiling for the rest of the
        /// session; decaying too fast makes the normalization drift mid-piece.
        /// </summary>
        public float RangeHalfLifeSeconds = 12f;

        /// <summary>
        /// Time constant, seconds, for the seat reference frame. Deliberately long: the
        /// frame must follow the rower turning or shifting over a session, but must not
        /// follow the body swing inside a single stroke — if it did, it would cancel the
        /// very handle travel being measured.
        /// </summary>
        public float ReferenceFrameTimeConstant = 6f;
    }
}
