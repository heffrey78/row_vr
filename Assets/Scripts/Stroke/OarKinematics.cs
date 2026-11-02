using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// Tuning for <see cref="OarKinematics"/>. Angles are the oar shaft's sweep in the
    /// horizontal plane, in degrees from square (perpendicular to the boat): positive toward
    /// the catch, where the blade is reaching toward the stern, negative toward the finish.
    /// </summary>
    public sealed class OarSettings
    {
        /// <summary>Sweep at the catch. A sculler's catch angle is typically 55-65 degrees.</summary>
        public float CatchAngleDegrees = 60f;

        /// <summary>Sweep at the finish. Typically 35-45 degrees past square.</summary>
        public float FinishAngleDegrees = -40f;

        /// <summary>Sweep with no stroke in progress: square to the boat.</summary>
        public float RestAngleDegrees = 0f;

        /// <summary>
        /// Time for the blade to go fully in or fully out, seconds. Short enough to read as
        /// the blade dropping in at the catch, long enough that it does not snap.
        /// </summary>
        public float BladeTransitionSeconds = 0.12f;

        /// <summary>
        /// Time for the sweep to ease back to rest once no stroke is tracked and the blade is
        /// clear of the water, seconds. Full settle is this plus <see cref="BladeTransitionSeconds"/>.
        /// </summary>
        public float RestSeconds = 0.6f;

        /// <summary>
        /// Handle speed, m/s, that counts as clearly moving one way. During a drive, the
        /// handle moving toward the catch faster than this lifts the blade, and moving toward
        /// the finish faster than this puts it back in. Matches the detector's own
        /// <see cref="StrokeDetectorSettings.VelocityThreshold"/>.
        /// </summary>
        public float BladeVelocityThreshold = 0.15f;
    }

    /// <summary>One oar's pose. Sculling uses two mirrored oars with the same pose.</summary>
    public readonly struct OarPose
    {
        /// <summary>Sweep in degrees from square; see <see cref="OarSettings"/> for the sign.</summary>
        public readonly float SweepDegrees;

        /// <summary>0 with the blade clear of the water, 1 fully buried.</summary>
        public readonly float BladeDepth;

        /// <summary>True when no stroke is being tracked and the oars are heading to, or at, rest.</summary>
        public readonly bool Resting;

        public OarPose(float sweepDegrees, float bladeDepth, bool resting)
        {
            SweepDegrees = sweepDegrees;
            BladeDepth = bladeDepth;
            Resting = resting;
        }

        public override string ToString() =>
            $"sweep {SweepDegrees:F1} deg, depth {BladeDepth:F2}{(Resting ? ", resting" : "")}";
    }

    /// <summary>
    /// Turns the detector's output into an oar pose, so the oars follow the stroke the
    /// detector sees and nothing else.
    ///
    /// Sweep follows <see cref="StrokeState.NormalizedPosition"/>: 1 at the catch maps to the
    /// catch angle, 0 at the finish to the finish angle. The blade is buried from the catch
    /// through the drive and lifted from the finish through the recovery, easing over
    /// <see cref="OarSettings.BladeTransitionSeconds"/>.
    ///
    /// The blade also follows the handle's direction, not only the phase. When a catch is
    /// called prematurely, with the hands low and hidden at the catch, the detector stays in
    /// <see cref="StrokePhase.Drive"/> while the hands carry on toward the catch until the
    /// real drive (the erg-sloppy fixture does this at ~187-190 s, #67). Its stroke
    /// count is unaffected, but a blade that believed it would sit buried while the handle
    /// still travels toward the catch. So during a drive the blade lifts when the
    /// handle moves clearly toward the catch, and goes back in when it moves clearly toward
    /// the finish. A blade in the water moving toward the catch is never right.
    ///
    /// <see cref="StrokePhase.Unknown"/> means no stroke is being tracked: before the first
    /// stroke, after a stop, and after a withdrawn catch (#38), which the detector
    /// reports as Unknown. In all three the blade lifts and the sweep eases back to square,
    /// rather than freezing mid-stroke.
    ///
    /// Stateful only for the easing. Feed it every update, in time order.
    /// </summary>
    public sealed class OarKinematics
    {
        readonly OarSettings _settings;
        double _lastTime = double.NaN;
        float _sweep;
        float _depth;
        bool _bladeIn;

        public OarKinematics(OarSettings settings = null)
        {
            _settings = settings ?? new OarSettings();
            _sweep = _settings.RestAngleDegrees;
        }

        public OarSettings Settings => _settings;

        public void Reset()
        {
            _lastTime = double.NaN;
            _sweep = _settings.RestAngleDegrees;
            _depth = 0f;
            _bladeIn = false;
        }

        public OarPose Update(double timeSeconds, StrokeState state)
        {
            var dt = double.IsNaN(_lastTime) ? 0f : (float)Math.Max(0d, timeSeconds - _lastTime);
            _lastTime = timeSeconds;

            var resting = state.Phase == StrokePhase.Unknown;
            _bladeIn = BladeIn(state, _bladeIn);
            _depth = MoveTowards(_depth, _bladeIn ? 1f : 0f, Rate(_settings.BladeTransitionSeconds, dt));

            if (resting)
            {
                // Eased, not snapped: a withdrawn catch lands mid-drive, and the oars jumping
                // to square in one frame would be the most visible glitch in the scene. The
                // blade clears the water first; squaring a stopped oar near the finish means
                // moving toward the catch, which a buried blade must never do.
                if (_depth > 0f) return new OarPose(_sweep, _depth, true);
                var span = Math.Abs(_settings.CatchAngleDegrees - _settings.FinishAngleDegrees);
                _sweep = MoveTowards(_sweep, _settings.RestAngleDegrees, span * Rate(_settings.RestSeconds, dt));
            }
            else
            {
                var t = Clamp01(state.NormalizedPosition);
                _sweep = _settings.FinishAngleDegrees + (_settings.CatchAngleDegrees - _settings.FinishAngleDegrees) * t;
            }

            return new OarPose(_sweep, _depth, resting);
        }

        /// <summary>
        /// In at the catch, out at the finish. Within a drive, hysteresis on the handle's
        /// direction (velocity is negative toward the finish): between the two thresholds the
        /// blade keeps its current state, so hand-tracking wobble cannot flicker it.
        /// </summary>
        bool BladeIn(StrokeState state, bool wasIn)
        {
            switch (state.Phase)
            {
                case StrokePhase.Catch:
                    return true;
                case StrokePhase.Drive:
                    var v = _settings.BladeVelocityThreshold;
                    if (state.HandleVelocity > v) return false;
                    if (state.HandleVelocity < -v) return true;
                    return wasIn;
                default:
                    return false;
            }
        }

        /// <summary>Fraction of a full move allowed in <paramref name="dt"/> for a move that takes <paramref name="seconds"/>.</summary>
        static float Rate(float seconds, float dt) => seconds > 1e-6f ? dt / seconds : 1f;

        static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
