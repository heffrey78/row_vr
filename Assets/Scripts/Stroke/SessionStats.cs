namespace RowVR.Stroke
{
    /// <summary>A finished session's totals, for the end-of-session summary (#29).</summary>
    public readonly struct SessionSummary
    {
        public readonly double ElapsedSeconds;
        public readonly double DistanceMetres;
        public readonly int Strokes;
        public readonly float AverageSplitSeconds;
        public readonly float AverageRate;

        public SessionSummary(double elapsed, double distance, int strokes, float averageSplit, float averageRate)
        {
            ElapsedSeconds = elapsed;
            DistanceMetres = distance;
            Strokes = strokes;
            AverageSplitSeconds = averageSplit;
            AverageRate = averageRate;
        }
    }

    /// <summary>
    /// What the rower sees on the panel (#29): elapsed time, distance, strokes,
    /// stroke rate, pace per 500 m for the last stroke and on average. It reads like a rowing
    /// monitor: the clock starts at the first catch, pace is per stroke, and a withdrawn catch
    /// (#38) is taken back off the count.
    ///
    /// A session ends after <see cref="IdleEndSeconds"/> without a stroke. Its totals are kept
    /// as <see cref="LastSummary"/> and the next catch starts a new session. The clock keeps
    /// running through shorter rests, as a monitor's does.
    ///
    /// Feed it every update: <see cref="Update"/> with the detector's state when there is a
    /// pose, <see cref="Tick"/> when there is not.
    /// </summary>
    public sealed class SessionStats
    {
        /// <summary>Seconds without a stroke that end the session.</summary>
        public double IdleEndSeconds = 60d;

        /// <summary>Seconds without a stroke after which the rower counts as resting (rate and pace blank).</summary>
        public double RestSeconds = 6d;

        double _start;
        double _startDistance;
        double _lastCatch = double.NaN;
        double _lastCatchDistance;
        double _now;
        double _rateSum;
        int _rateCount;

        public bool Active { get; private set; }
        public double ElapsedSeconds => Active ? _now - _start : 0d;
        public double DistanceMetres { get; private set; }
        public int Strokes { get; private set; }

        /// <summary>Strokes per minute of the last stroke; 0 when resting.</summary>
        public float Rate { get; private set; }

        /// <summary>Pace of the last complete stroke, seconds per 500 m; infinity when resting.</summary>
        public float StrokeSplitSeconds { get; private set; } = float.PositiveInfinity;

        /// <summary>Pace over the whole session, seconds per 500 m.</summary>
        public float AverageSplitSeconds =>
            DistanceMetres > 1d ? (float)(ElapsedSeconds / DistanceMetres * 500d) : float.PositiveInfinity;

        /// <summary>Recovery time over drive time for the last stroke; about 2 is a good steady rhythm.</summary>
        public float RecoveryToDriveRatio { get; private set; }

        public bool Resting => !Active || double.IsNaN(_lastCatch) || _now - _lastCatch > RestSeconds;

        /// <summary>The last session that ended, if any.</summary>
        public SessionSummary? LastSummary { get; private set; }

        public void Update(double timeSeconds, StrokeState state, double boatDistance)
        {
            Tick(timeSeconds, boatDistance);

            if (state.CatchWithdrawn && Active && Strokes > 0) Strokes--;

            if (state.Phase == StrokePhase.Catch)
            {
                if (!Active) Begin(timeSeconds, boatDistance);
                if (!double.IsNaN(_lastCatch) && timeSeconds - _lastCatch <= RestSeconds)
                {
                    var speed = (boatDistance - _lastCatchDistance) / (timeSeconds - _lastCatch);
                    StrokeSplitSeconds = BoatModel.SplitSeconds((float)speed);
                }
                Strokes++;
                _lastCatch = timeSeconds;
                _lastCatchDistance = boatDistance;

                var rate = state.Metrics.StrokesPerMinute;
                if (rate > 0f)
                {
                    Rate = rate;
                    _rateSum += rate;
                    _rateCount++;
                }
            }

            if (state.Phase == StrokePhase.Finish && state.Metrics.RecoveryToDriveRatio > 0f)
                RecoveryToDriveRatio = state.Metrics.RecoveryToDriveRatio;
        }

        public void Tick(double timeSeconds, double boatDistance)
        {
            _now = timeSeconds;
            if (!Active) return;

            DistanceMetres = boatDistance - _startDistance;
            if (Resting)
            {
                Rate = 0f;
                StrokeSplitSeconds = float.PositiveInfinity;
            }
            if (!double.IsNaN(_lastCatch) && timeSeconds - _lastCatch > IdleEndSeconds) End();
        }

        void Begin(double time, double distance)
        {
            Active = true;
            _start = time;
            _startDistance = distance;
            _lastCatch = double.NaN;
            _rateSum = 0d;
            _rateCount = 0;
            Strokes = 0;
            DistanceMetres = 0d;
            Rate = 0f;
            RecoveryToDriveRatio = 0f;
            StrokeSplitSeconds = float.PositiveInfinity;
        }

        void End()
        {
            // The rest before the session timed out is not part of the row.
            var elapsed = _lastCatch - _start;
            var average = DistanceMetres > 1d ? (float)(elapsed / DistanceMetres * 500d) : float.PositiveInfinity;
            LastSummary = new SessionSummary(elapsed, DistanceMetres, Strokes, average,
                _rateCount > 0 ? (float)(_rateSum / _rateCount) : 0f);
            Active = false;
            _lastCatch = double.NaN;
        }
    }
}
