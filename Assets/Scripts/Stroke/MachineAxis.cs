using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// Which way the rowing machine points, learned from the hands (level, unit vector toward
    /// the stern: the way the rower faces on the machine).
    ///
    /// The handle can only travel along the machine, so the direction of the hands' travel
    /// during strokes is the machine's axis, whichever way the rower looks. The head is only
    /// used for the very first guess and to choose which end of the axis is the stern. Before
    /// this, the boat's heading followed a slow average of the head's facing, so looking to one
    /// side turned the whole boat that way (developer report, 2026-09-30).
    ///
    /// Updates are ignored unless the hands are moving briskly along the current estimate, so
    /// hand wobble, reaching for a bottle, or sideways drift do not steer it.
    /// </summary>
    public sealed class MachineAxis
    {
        /// <summary>Hand speed, m/s, below which motion says nothing about the axis.</summary>
        public float MinSpeed = 0.3f;

        /// <summary>Travel must be within this many degrees of the current axis to count.</summary>
        public float MaxDeviationDegrees = 40f;

        /// <summary>Time constant, seconds of qualifying motion, for the estimate to settle.</summary>
        public float SettleSeconds = 6f;

        Vec3 _previousHands;
        double _previousTime = double.NaN;

        public bool HasAxis { get; private set; }

        /// <summary>Level unit vector along the machine, toward the stern (the rower's front).</summary>
        public Vec3 Forward { get; private set; } = Vec3.Forward;

        /// <summary>First guess, from where the rower is looking. Only used until motion is seen.</summary>
        public void Seed(Vec3 headForward)
        {
            var level = Level(headForward);
            if (level.SqrMagnitude < 1e-6f) return;
            Forward = level.Normalized;
            HasAxis = true;
        }

        /// <summary>Feeds the midpoint of the two hands at a time.</summary>
        public void Update(double timeSeconds, Vec3 hands)
        {
            if (double.IsNaN(_previousTime) || !HasAxis)
            {
                _previousTime = timeSeconds;
                _previousHands = hands;
                return;
            }

            var dt = (float)(timeSeconds - _previousTime);
            if (dt <= 0f) return;
            // A long gap is not motion: start again from here.
            if (dt > 0.25f)
            {
                _previousTime = timeSeconds;
                _previousHands = hands;
                return;
            }

            var travel = Level(hands - _previousHands);
            _previousTime = timeSeconds;
            _previousHands = hands;

            var speed = travel.Magnitude / dt;
            if (speed < MinSpeed) return;

            // The axis has two ends; take the one nearer the current estimate. Drive and
            // recovery both count, since they travel the same line in opposite directions.
            var direction = travel.Normalized;
            var dot = Vec3.Dot(direction, Forward);
            if (dot < 0f) { direction = -direction; dot = -dot; }
            if (dot < (float)Math.Cos(MaxDeviationDegrees * Math.PI / 180d)) return;

            var alpha = 1f - (float)Math.Exp(-dt / Math.Max(0.01f, SettleSeconds));
            var blended = Forward + (direction - Forward) * alpha;
            if (blended.SqrMagnitude > 1e-6f) Forward = blended.Normalized;
        }

        static Vec3 Level(Vec3 v) => new Vec3(v.X, 0f, v.Z);
    }
}
