using System.Globalization;

namespace RowVR.Stroke
{
    /// <summary>
    /// Coaching metrics for the most recently completed stroke. All values are zero until at
    /// least one full catch-to-catch cycle has been observed.
    /// </summary>
    public readonly struct StrokeMetrics
    {
        /// <summary>Strokes per minute, from the last catch-to-catch interval. Recreational rowing sits around 18-24.</summary>
        public readonly float StrokesPerMinute;

        /// <summary>Seconds spent in the drive.</summary>
        public readonly float DriveSeconds;

        /// <summary>Seconds spent in the recovery.</summary>
        public readonly float RecoverySeconds;

        /// <summary>
        /// Recovery time divided by drive time. The number coaches actually care about:
        /// a controlled stroke sits near 2.0, and rushing the slide drives it toward 1.0.
        /// </summary>
        public readonly float RecoveryToDriveRatio;

        /// <summary>Handle travel along the drive axis, in metres.</summary>
        public readonly float StrokeLengthMetres;

        /// <summary>How many full strokes have been completed since the detector was reset.</summary>
        public readonly int CompletedStrokes;

        public StrokeMetrics(
            float strokesPerMinute,
            float driveSeconds,
            float recoverySeconds,
            float strokeLengthMetres,
            int completedStrokes)
        {
            StrokesPerMinute = strokesPerMinute;
            DriveSeconds = driveSeconds;
            RecoverySeconds = recoverySeconds;
            RecoveryToDriveRatio = driveSeconds > 1e-4f ? recoverySeconds / driveSeconds : 0f;
            StrokeLengthMetres = strokeLengthMetres;
            CompletedStrokes = completedStrokes;
        }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0:F1} spm, drive {1:F2}s, recovery {2:F2}s, ratio 1:{3:F2}, length {4:F2}m, n={5}",
            StrokesPerMinute, DriveSeconds, RecoverySeconds, RecoveryToDriveRatio,
            StrokeLengthMetres, CompletedStrokes);
    }

    /// <summary>The detector's output for a single update.</summary>
    public readonly struct StrokeState
    {
        public readonly StrokePhase Phase;

        /// <summary>Filtered handle position along the drive axis, in metres from the seat reference.</summary>
        public readonly float HandlePosition;

        /// <summary>Filtered handle speed along the drive axis, m/s. Negative during the drive.</summary>
        public readonly float HandleVelocity;

        /// <summary>Handle position rescaled to the rower's own observed range: 0 at the finish, 1 at the catch.</summary>
        public readonly float NormalizedPosition;

        public readonly StrokeMetrics Metrics;

        public StrokeState(
            StrokePhase phase,
            float handlePosition,
            float handleVelocity,
            float normalizedPosition,
            StrokeMetrics metrics)
        {
            Phase = phase;
            HandlePosition = handlePosition;
            HandleVelocity = handleVelocity;
            NormalizedPosition = normalizedPosition;
            Metrics = metrics;
        }
    }
}
