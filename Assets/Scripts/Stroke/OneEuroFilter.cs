using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// One Euro filter (Casiez, Roussel and Vogel, CHI 2012).
    ///
    /// Chosen over a fixed low-pass or moving average because hand-tracking jitter and
    /// rowing motion need opposite treatment: at the finish and the catch the handle is
    /// nearly still and the tracker's jitter dominates, so heavy smoothing is wanted; mid
    /// drive the handle moves fast and any lag reads directly as the boat feeling
    /// disconnected from the body. A fixed filter has to trade one against the other. This
    /// one adapts its cutoff to the signal's own speed, so it smooths hard when slow and
    /// gets out of the way when fast.
    ///
    /// Tuning: raise <c>beta</c> if fast motion feels laggy; raise <c>minCutoff</c> if slow
    /// motion still jitters.
    ///
    /// The defaults below are not the paper's illustrative values (1.0 / 0.007, tuned for a
    /// mouse pointer). They come from sweeping this filter against synthetic rowing motion
    /// of known timing: the paper's beta lags a 1.4 m/s drive enough to skew the measured
    /// drive/recovery ratio by roughly 20%, while beta much above 0.5 stops rejecting
    /// hand-tracking noise and starts inventing strokes. See StrokeDetectorTests.
    /// </summary>
    public sealed class OneEuroFilter
    {
        readonly float _minCutoff;
        readonly float _beta;
        readonly float _dCutoff;

        bool _hasPrevious;
        float _xPrev;
        float _dxPrev;
        double _tPrev;

        public OneEuroFilter(float minCutoff = 1.0f, float beta = 1.0f, float dCutoff = 1.0f)
        {
            if (minCutoff <= 0f) throw new ArgumentOutOfRangeException(nameof(minCutoff));
            if (dCutoff <= 0f) throw new ArgumentOutOfRangeException(nameof(dCutoff));

            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        public void Reset()
        {
            _hasPrevious = false;
            _xPrev = 0f;
            _dxPrev = 0f;
            _tPrev = 0d;
        }

        public float Filter(float x, double timeSeconds)
        {
            if (!_hasPrevious)
            {
                _hasPrevious = true;
                _xPrev = x;
                _dxPrev = 0f;
                _tPrev = timeSeconds;
                return x;
            }

            var dt = (float)(timeSeconds - _tPrev);
            // Guard against a stalled or rewound clock: a non-positive dt would divide by
            // zero below. Replaying a recorded clip can produce duplicate timestamps.
            if (dt <= 0f) return _xPrev;

            _tPrev = timeSeconds;

            var dx = (x - _xPrev) / dt;
            var dxHat = Lowpass(dx, _dxPrev, Alpha(_dCutoff, dt));
            _dxPrev = dxHat;

            // The adaptive step: cutoff rises with speed, so fast motion is barely filtered.
            var cutoff = _minCutoff + _beta * Math.Abs(dxHat);
            var xHat = Lowpass(x, _xPrev, Alpha(cutoff, dt));
            _xPrev = xHat;

            return xHat;
        }

        static float Alpha(float cutoff, float dt)
        {
            var tau = 1f / (2f * (float)Math.PI * cutoff);
            return 1f / (1f + tau / dt);
        }

        static float Lowpass(float x, float prev, float alpha) => alpha * x + (1f - alpha) * prev;
    }
}
