using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// Turns a stream of tracked hand and head poses into a rowing stroke phase plus
    /// coaching metrics.
    ///
    /// How it works: both hands are reduced to a single handle point (their midpoint) and
    /// projected onto the rower's drive axis, giving one scalar per update. Stroke phase is
    /// then a sign-of-velocity problem on that scalar, with hysteresis. Everything hard
    /// about it is in three places:
    ///
    ///   1. The reference frame. The origin cannot be the headset, because the body swings
    ///      through the stroke and would cancel most of the handle's travel. A slowly
    ///      adapting seat frame is used instead.
    ///   2. Filtering. Hand tracking is noisiest exactly where precision matters most — at
    ///      the catch and finish, where the handle is nearly still. See OneEuroFilter.
    ///   3. Thresholds. They are relative to the rower's own observed range, not absolute
    ///      metres, so the detector fits the person rather than the person fitting it.
    ///
    /// Deliberately free of any engine dependency: feed it live poses in the headset or a
    /// recorded clip in a unit test and it cannot tell the difference.
    /// </summary>
    public sealed class StrokeDetector
    {
        readonly StrokeDetectorSettings _settings;
        readonly OneEuroFilter _positionFilter;

        // Seat reference frame, adapted slowly. See ReferenceFrameTimeConstant.
        bool _hasFrame;
        Vec3 _seatOrigin;
        Vec3 _driveAxis;

        // Observed handle range, used to normalize thresholds to this rower.
        bool _hasRange;
        float _rangeMin;
        float _rangeMax;

        bool _hasPrevious;
        double _previousTime;
        float _previousPosition;

        StrokePhase _phase = StrokePhase.Unknown;
        double _phaseEnteredAt;

        // The turning point seen so far in the current phase. A transition can only be
        // confirmed once the handle has picked up enough speed to rule out noise, which is
        // necessarily after the turn itself; these carry the moment the turn actually
        // happened so the metrics are not biased by the confirmation delay.
        double _extremumTime;
        float _extremumValue;
        double _lastCatchTime = double.NaN;
        double _lastFinishTime = double.NaN;
        float _lastDriveSeconds;
        float _lastRecoverySeconds;
        float _strokeMin = float.MaxValue;
        float _strokeMax = float.MinValue;
        StrokeMetrics _metrics;

        // Pause tracking. See StrokeDetectorSettings.PauseSeconds.
        bool _still;
        double _stillSince;
        double _restEndTime;
        bool _pauseHandled;
        // After a pause: travel must be rebuilt before any phase is reported.
        bool _needTravel;
        float _sinceRestMin;
        float _sinceRestMax;
        // After a pause held at the catch position, the first drive away is that stroke's catch.
        bool _restWasAtCatch;
        float _restPosition;
        // Extent of the last complete stroke. Unlike the session range this does not decay,
        // so "was the rest at the catch?" can still be answered long after rowing stopped.
        bool _hasStrokeExtent;
        float _lastStrokeMin;
        float _lastStrokeMax;

        public StrokeDetector(StrokeDetectorSettings settings = null)
        {
            _settings = settings ?? new StrokeDetectorSettings();
            _positionFilter = new OneEuroFilter(_settings.FilterMinCutoff, _settings.FilterBeta);
        }

        public StrokeMetrics Metrics => _metrics;
        public StrokePhase Phase => _phase;

        public void Reset()
        {
            _positionFilter.Reset();
            _hasFrame = false;
            _hasRange = false;
            _hasPrevious = false;
            _phase = StrokePhase.Unknown;
            _lastCatchTime = double.NaN;
            _lastFinishTime = double.NaN;
            _lastDriveSeconds = 0f;
            _lastRecoverySeconds = 0f;
            _strokeMin = float.MaxValue;
            _strokeMax = float.MinValue;
            _extremumTime = 0d;
            _extremumValue = 0f;
            _metrics = default;
            _still = false;
            _pauseHandled = false;
            _needTravel = false;
            _restWasAtCatch = false;
            _hasStrokeExtent = false;
        }

        /// <summary>
        /// Advances the detector by one sample.
        /// </summary>
        /// <param name="timeSeconds">Monotonic timestamp. Duplicate or rewound values are ignored.</param>
        /// <param name="leftHand">Left hand position, world space (metres).</param>
        /// <param name="rightHand">Right hand position, world space (metres).</param>
        /// <param name="headPosition">Head position, world space.</param>
        /// <param name="headForward">Head forward direction; need not be normalized or level.</param>
        public StrokeState Update(
            double timeSeconds,
            Vec3 leftHand,
            Vec3 rightHand,
            Vec3 headPosition,
            Vec3 headForward)
        {
            var handle = (leftHand + rightHand) * 0.5f;

            // dt is computed once, up front, and threaded through every time-dependent
            // step. Deriving it separately inside each helper is how the reference frame
            // and the range decay silently stop decaying.
            var dt = _hasPrevious ? (float)(timeSeconds - _previousTime) : 0f;
            if (_hasPrevious && dt <= 0f)
            {
                // A stalled or rewound clock: duplicate timestamps show up when replaying a
                // recorded clip. Report the current state rather than dividing by zero.
                return CurrentState(_previousPosition, 0f);
            }

            UpdateReferenceFrame(dt, headPosition, headForward);

            var raw = Vec3.Dot(handle - _seatOrigin, _driveAxis);
            var position = _positionFilter.Filter(raw, timeSeconds);

            var velocity = _hasPrevious ? (position - _previousPosition) / dt : 0f;

            if (!_hasPrevious)
            {
                _phaseEnteredAt = timeSeconds;
            }

            _previousTime = timeSeconds;
            _previousPosition = position;
            _hasPrevious = true;

            UpdateRange(dt, position);
            _strokeMin = Math.Min(_strokeMin, position);
            _strokeMax = Math.Max(_strokeMax, position);

            var span = _rangeMax - _rangeMin;
            TrackPause(timeSeconds, velocity, position);

            if (!_hasRange || span < _settings.MinStrokeLengthMetres)
            {
                // Not enough travel to be rowing. Stay Unknown rather than guessing.
                _phase = StrokePhase.Unknown;
                return CurrentState(position, velocity);
            }

            if (_needTravel)
            {
                // Rebuilding after a pause. The decayed session range is still wide, so on its
                // own it would let a small movement - hands coming up off the lap - normalize to
                // most of a stroke. Require real travel since the pause first.
                _sinceRestMin = Math.Min(_sinceRestMin, position);
                _sinceRestMax = Math.Max(_sinceRestMax, position);
                if (_sinceRestMax - _sinceRestMin < _settings.MinStrokeLengthMetres)
                {
                    return CurrentState(position, velocity);
                }
                _needTravel = false;
            }

            var normalized = Clamp01((position - _rangeMin) / span);

            if (_phase == StrokePhase.Unknown && _restWasAtCatch && velocity < -_settings.VelocityThreshold)
            {
                // The rower held still at full reach, then drove. That departure is a catch we
                // did witness - it is how a stroke from rest begins - so count it, timed at the
                // moment the handle left rest rather than at the moment we became sure.
                _restWasAtCatch = false;
                _extremumValue = _restPosition;
                _extremumTime = _restEndTime;
                OnCatch(timeSeconds);
                return new StrokeState(_phase, position, velocity, normalized, _metrics);
            }

            if (_phase != StrokePhase.Unknown) _restWasAtCatch = false;
            AdvancePhase(timeSeconds, velocity, normalized);

            return new StrokeState(_phase, position, velocity, normalized, _metrics);
        }

        /// <summary>
        /// Notices the rower stopping: the handle below the velocity threshold for
        /// <see cref="StrokeDetectorSettings.PauseSeconds"/>. Handled once per pause.
        /// </summary>
        void TrackPause(double timeSeconds, float velocity, float position)
        {
            if (Math.Abs(velocity) < _settings.VelocityThreshold)
            {
                if (!_still)
                {
                    _still = true;
                    _stillSince = timeSeconds;
                }
                _restEndTime = timeSeconds;

                if (!_pauseHandled && timeSeconds - _stillSince >= _settings.PauseSeconds)
                {
                    _pauseHandled = true;
                    OnPause(position);
                }
            }
            else
            {
                _still = false;
                _pauseHandled = false;
            }
        }

        void OnPause(float position)
        {
            // Where in the rower's reach did they stop? At the catch, the next drive is a
            // real stroke from rest. Anywhere else - a freeze mid-drive, hands in the lap -
            // it is not. Judged against the last complete stroke, not the session range: the
            // session range decays while resting, and a decayed range made hands in the lap
            // look like full reach, so reaching for the headset was scored as a stroke.
            // Before any stroke has been rowed, rest is treated as the catch, because that is
            // how people start rowing.
            if (_hasStrokeExtent)
            {
                var extent = _lastStrokeMax - _lastStrokeMin;
                _restWasAtCatch = extent > 1e-3f &&
                                  (position - _lastStrokeMin) / extent > _settings.CatchZone;
            }
            else
            {
                _restWasAtCatch = true;
            }
            _restPosition = position;

            // Stop measuring rate across the pause. The next catch starts a new interval
            // instead of producing one that includes the stop.
            _lastCatchTime = double.NaN;
            _lastFinishTime = double.NaN;

            _phase = StrokePhase.Unknown;
            _needTravel = true;
            _sinceRestMin = position;
            _sinceRestMax = position;
        }

        /// <summary>
        /// Tracks a seat origin and drive axis that follow the rower over a session but not
        /// within a stroke. The axis is levelled to the horizontal plane: leaning the head
        /// at the catch must not tilt the axis the handle is measured against.
        /// </summary>
        void UpdateReferenceFrame(float dt, Vec3 headPosition, Vec3 headForward)
        {
            var levelled = new Vec3(headForward.X, 0f, headForward.Z);
            // Looking straight up or down leaves no horizontal component; keep the last
            // good axis rather than collapsing to zero.
            var axis = levelled.SqrMagnitude > 1e-6f
                ? levelled.Normalized
                : (_hasFrame ? _driveAxis : Vec3.Forward);

            if (!_hasFrame)
            {
                _seatOrigin = headPosition;
                _driveAxis = axis;
                _hasFrame = true;
                return;
            }

            // Exponential approach with the configured time constant.
            var alpha = 1f - (float)Math.Exp(-dt / Math.Max(1e-3f, _settings.ReferenceFrameTimeConstant));

            _seatOrigin = Vec3.Lerp(_seatOrigin, headPosition, alpha);
            _driveAxis = Vec3.Lerp(_driveAxis, axis, alpha).Normalized;
        }

        /// <summary>
        /// Maintains the observed handle range, decaying toward the current position so a
        /// single freak reach does not skew normalization for the rest of the session.
        /// </summary>
        void UpdateRange(float dt, float position)
        {
            if (!_hasRange)
            {
                _rangeMin = position;
                _rangeMax = position;
                _hasRange = true;
                return;
            }

            var decay = (float)Math.Pow(0.5d, dt / Math.Max(1e-3f, _settings.RangeHalfLifeSeconds));

            // Shrink the envelope toward the current sample, then re-expand to include it.
            var mid = (_rangeMin + _rangeMax) * 0.5f;
            _rangeMin = mid + (_rangeMin - mid) * decay;
            _rangeMax = mid + (_rangeMax - mid) * decay;

            _rangeMin = Math.Min(_rangeMin, position);
            _rangeMax = Math.Max(_rangeMax, position);
        }

        void AdvancePhase(double timeSeconds, float velocity, float normalized)
        {
            // Catch and Finish are single-update events; having reported one, settle into
            // the sustained phase that follows it.
            if (_phase == StrokePhase.Catch) _phase = StrokePhase.Drive;
            else if (_phase == StrokePhase.Finish) _phase = StrokePhase.Recovery;

            TrackExtremum(timeSeconds);

            var sincePhaseChange = timeSeconds - _phaseEnteredAt;
            if (sincePhaseChange < _settings.MinPhaseSeconds) return;

            var v = _settings.VelocityThreshold;

            switch (_phase)
            {
                case StrokePhase.Unknown:
                    // Adopt whichever phase the current motion matches, without claiming a
                    // catch or finish we did not actually witness.
                    if (velocity < -v) EnterPhase(StrokePhase.Drive, timeSeconds);
                    else if (velocity > v) EnterPhase(StrokePhase.Recovery, timeSeconds);
                    break;

                case StrokePhase.Recovery:
                    // Catch: handle is out near full reach and has started back toward the body.
                    if (velocity < -v && normalized > _settings.CatchZone)
                    {
                        OnCatch(timeSeconds);
                    }
                    break;

                case StrokePhase.Drive:
                    // Finish: handle is in at the body and has started back out.
                    if (velocity > v && normalized < _settings.FinishZone)
                    {
                        OnFinish(timeSeconds);
                    }
                    break;
            }
        }

        /// <summary>
        /// Remembers the furthest point reached in the current phase: the handle's maximum
        /// during the recovery (the catch) and its minimum during the drive (the finish).
        ///
        /// This is what keeps the timing honest. A transition is only confirmed once the
        /// handle is clearly moving the other way, which happens measurably after the turn
        /// - and most so at the finish, where the recovery accelerates gently. Timestamping
        /// at confirmation stretches the drive and shortens the recovery, which shows up
        /// directly in the ratio a rower is coached on.
        /// </summary>
        void TrackExtremum(double timeSeconds)
        {
            switch (_phase)
            {
                case StrokePhase.Recovery:
                    if (_previousPosition > _extremumValue)
                    {
                        _extremumValue = _previousPosition;
                        _extremumTime = timeSeconds;
                    }
                    break;

                case StrokePhase.Drive:
                    if (_previousPosition < _extremumValue)
                    {
                        _extremumValue = _previousPosition;
                        _extremumTime = timeSeconds;
                    }
                    break;
            }
        }

        void OnCatch(double timeSeconds)
        {
            // Report the turn, not the moment we became sure of it.
            var catchTime = _extremumTime > 0d ? _extremumTime : timeSeconds;

            if (!double.IsNaN(_lastFinishTime))
            {
                _lastRecoverySeconds = (float)(catchTime - _lastFinishTime);
            }

            if (!double.IsNaN(_lastCatchTime))
            {
                var cycle = (float)(catchTime - _lastCatchTime);
                var spm = cycle > 1e-3f ? 60f / cycle : 0f;
                var length = _strokeMax > _strokeMin ? _strokeMax - _strokeMin : 0f;

                _metrics = new StrokeMetrics(
                    spm,
                    _lastDriveSeconds,
                    _lastRecoverySeconds,
                    length,
                    _metrics.CompletedStrokes + 1);
            }

            _lastCatchTime = catchTime;
            if (_strokeMax - _strokeMin >= _settings.MinStrokeLengthMetres)
            {
                _hasStrokeExtent = true;
                _lastStrokeMin = _strokeMin;
                _lastStrokeMax = _strokeMax;
            }
            // Range for the stroke that starts here.
            _strokeMin = float.MaxValue;
            _strokeMax = float.MinValue;

            EnterPhase(StrokePhase.Catch, timeSeconds);
        }

        void OnFinish(double timeSeconds)
        {
            var finishTime = _extremumTime > 0d ? _extremumTime : timeSeconds;

            if (!double.IsNaN(_lastCatchTime))
            {
                _lastDriveSeconds = (float)(finishTime - _lastCatchTime);
            }
            _lastFinishTime = finishTime;
            EnterPhase(StrokePhase.Finish, timeSeconds);
        }

        void EnterPhase(StrokePhase phase, double timeSeconds)
        {
            _phase = phase;
            _phaseEnteredAt = timeSeconds;

            // Seed the new phase's extremum with the current position, so the search for the
            // next turning point starts from where the handle actually is.
            _extremumValue = _previousPosition;
            _extremumTime = timeSeconds;
        }

        StrokeState CurrentState(float position, float velocity)
        {
            var span = _rangeMax - _rangeMin;
            var normalized = _hasRange && span > 1e-4f ? Clamp01((position - _rangeMin) / span) : 0f;
            return new StrokeState(_phase, position, velocity, normalized, _metrics);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
