using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// When to show the room (REQ-0005, ADR-0014). The rower is strapped into a machine in a
    /// fully virtual scene, and should never be blind once they are off the handle.
    ///
    /// The room fades in on evidence that the rower let go:
    ///  - the detector withdrew a catch because the hands left the handle (ADR-0007);
    ///  - the hands-off signal held for <see cref="HandsOffSeconds"/>. A single-frame blip is a
    ///    grip adjustment mid-row; the recordings have several;
    ///  - or hand tracking was lost for <see cref="TrackingLostSeconds"/>.
    ///
    /// It fades back out at the finish of the next drive that began with a catch. Not at a catch: the handle
    /// drifting back on its cord after a release can raise a phantom catch, which the detector
    /// later withdraws, and fading on it made the room flicker. A rower who pauses still
    /// holding the handle stays in the scene.
    ///
    /// <see cref="Dismiss"/> (the wrist menu's "back to the scene") returns to the scene until
    /// the next stroke, without finding the handle. A pinch did this at first, but the pinch is
    /// the Quest's own system gesture and did not work reliably on top of it (developer,
    /// 2026-09-30), so it is a button now (ADR-0015).
    ///
    /// Known gap: opening the fingers without moving the hands (release B in the release-types
    /// recording) gives the detector nothing, so the room does not fade in until tracking is
    /// lost or the hands move. Engine-free and tested on the recordings (PassthroughPolicyTests).
    /// </summary>
    public sealed class PassthroughPolicy
    {
        public float HandsOffSeconds = 0.3f;
        public float TrackingLostSeconds = 1.5f;
        public float FadeInSeconds = 0.6f;
        public float FadeOutSeconds = 0.4f;

        double _lastTime = double.NaN;
        double _lostSince = double.NaN;
        double _offSince = double.NaN;
        bool _wanted;
        bool _driveFromCatch;
        bool _dismissed;

        /// <summary>0 the scene, 1 the room.</summary>
        public float Fade { get; private set; }

        /// <summary>Whether the room is wanted now (Fade is easing toward it).</summary>
        public bool Wanted => _wanted;

        /// <summary>True from <see cref="Dismiss"/> until the next stroke: the room will not come back.</summary>
        public bool Dismissed => _dismissed;

        /// <summary>Back to the scene until the next stroke.</summary>
        public void Dismiss()
        {
            _dismissed = true;
            _wanted = false;
        }

        public void Update(double timeSeconds, bool tracking, StrokeState state, bool handsOff)
        {
            var dt = double.IsNaN(_lastTime) ? 0f : (float)Math.Max(0d, Math.Min(0.25d, timeSeconds - _lastTime));
            _lastTime = timeSeconds;

            if (tracking)
            {
                _lostSince = double.NaN;
                // Only a drive that began with a catch, not taken back, is rowing. After a
                // release the detector can adopt a drive from the drifting handle with no catch,
                // and its finish must not bring the scene back while the hands are off.
                if (state.Phase == StrokePhase.Catch) _driveFromCatch = true;
                if (state.CatchWithdrawn)
                {
                    _driveFromCatch = false;
                    _wanted = true;
                }
                if (state.Phase == StrokePhase.Finish)
                {
                    if (_driveFromCatch)
                    {
                        _wanted = false;
                        _dismissed = false;
                    }
                    _driveFromCatch = false;
                }

                if (handsOff)
                {
                    if (double.IsNaN(_offSince)) _offSince = timeSeconds;
                    if (timeSeconds - _offSince >= HandsOffSeconds) _wanted = true;
                }
                else
                {
                    _offSince = double.NaN;
                }
            }
            else
            {
                if (double.IsNaN(_lostSince)) _lostSince = timeSeconds;
                if (timeSeconds - _lostSince >= TrackingLostSeconds) _wanted = true;
            }

            if (_dismissed) _wanted = false;

            var seconds = _wanted ? FadeInSeconds : FadeOutSeconds;
            var step = seconds > 1e-4f ? dt / seconds : 1f;
            Fade = _wanted ? Math.Min(1f, Fade + step) : Math.Max(0f, Fade - step);
        }
    }
}
