using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// When the wrist menu shows (ADR-0015): the rower turns their left hand over, palm toward
    /// their face. That is never a rowing pose, since on the handle the palms face down and
    /// back, so the gesture itself is the signal. The first menu (ADR-0013) appeared on rest
    /// and hid on anything that looked like rowing, which included reaching for it.
    ///
    /// Shows after the palm has faced the head (the cosine at or above
    /// <see cref="ShowFacing"/>) for <see cref="ShowSeconds"/>. Hides once the palm has
    /// turned away (below <see cref="HideFacing"/>, a lower bar, so a wobble does not flicker
    /// it) for <see cref="HideSeconds"/>, or once tracking has been gone
    /// <see cref="LostSeconds"/>. Never shows while rowing, meaning within
    /// <see cref="RowingSeconds"/> of the finish of a real stroke. Engine-free and tested
    /// (WristMenuGateTests).
    /// </summary>
    public sealed class WristMenuGate
    {
        public float ShowFacing = 0.6f;
        public float HideFacing = 0.35f;
        public float ShowSeconds = 0.25f;
        public float HideSeconds = 0.3f;
        public float LostSeconds = 0.5f;
        public float RowingSeconds = 3f;
        public float FadeSeconds = 0.15f;

        double _lastTime = double.NaN;
        double _facingSince = double.NaN;
        double _awaySince = double.NaN;
        double _lostSince = double.NaN;
        double _lastStroke = double.NegativeInfinity;
        bool _strokeFromCatch;

        public bool Visible { get; private set; }

        /// <summary>0 hidden, 1 fully shown; eases so the menu grows in rather than popping.</summary>
        public float Appear { get; private set; }

        /// <param name="palmTracked">Whether the left hand is tracked this update.</param>
        /// <param name="palmFacing">Cosine between the palm's normal and the direction to the head.</param>
        /// <param name="state">The stroke detector's state, to keep the menu away while rowing.</param>
        public void Update(double timeSeconds, bool palmTracked, float palmFacing, StrokeState state)
        {
            var dt = double.IsNaN(_lastTime) ? 0f : (float)Math.Max(0d, Math.Min(0.25d, timeSeconds - _lastTime));
            _lastTime = timeSeconds;

            if (state.Phase == StrokePhase.Catch) _strokeFromCatch = true;
            if (state.CatchWithdrawn) _strokeFromCatch = false;
            if (state.Phase == StrokePhase.Finish)
            {
                if (_strokeFromCatch) _lastStroke = timeSeconds;
                _strokeFromCatch = false;
            }
            var rowing = timeSeconds - _lastStroke < RowingSeconds;

            if (!palmTracked)
            {
                if (double.IsNaN(_lostSince)) _lostSince = timeSeconds;
                _facingSince = double.NaN;
                if (timeSeconds - _lostSince >= LostSeconds) Visible = false;
            }
            else
            {
                _lostSince = double.NaN;
                if (palmFacing >= ShowFacing)
                {
                    if (double.IsNaN(_facingSince)) _facingSince = timeSeconds;
                }
                else
                {
                    _facingSince = double.NaN;
                }

                if (palmFacing < HideFacing)
                {
                    if (double.IsNaN(_awaySince)) _awaySince = timeSeconds;
                }
                else
                {
                    _awaySince = double.NaN;
                }

                if (!Visible && !rowing && !double.IsNaN(_facingSince) && timeSeconds - _facingSince >= ShowSeconds) Visible = true;
                if (Visible && !double.IsNaN(_awaySince) && timeSeconds - _awaySince >= HideSeconds) Visible = false;
            }
            if (rowing) Visible = false;

            var step = FadeSeconds > 1e-4f ? dt / FadeSeconds : 1f;
            Appear = Visible ? Math.Min(1f, Appear + step) : Math.Max(0f, Appear - step);
        }
    }
}
