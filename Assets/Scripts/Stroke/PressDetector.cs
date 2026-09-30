namespace RowVR.Stroke
{
    /// <summary>
    /// Presses a button with a fingertip (ADR-0013). Positions are in the button's own frame:
    /// X and Y across its face (metres from its centre), Z its depth, positive in front of
    /// the face and negative pushed into it.
    ///
    /// A press needs the fingertip to arrive from in front of the face, within its outline,
    /// and push in past <see cref="PressDepth"/>. It releases once pulled back out past
    /// <see cref="ReleaseDepth"/>, a smaller depth, so a finger resting at the threshold does
    /// not chatter. A finger sweeping in from the side never counts, since it never came from
    /// the front, and after a press there is a short refractory time. Engine-free and tested
    /// with synthetic finger paths (PressDetectorTests).
    /// </summary>
    public sealed class PressDetector
    {
        public float HalfWidth = 0.03f;
        public float HalfHeight = 0.02f;

        /// <summary>How far in, metres, the fingertip must push to press.</summary>
        public float PressDepth = 0.012f;

        /// <summary>How far out, metres, it must come back to release. Smaller than the press depth.</summary>
        public float ReleaseDepth = 0.004f;

        /// <summary>How close in front, metres, counts as approaching from the front.</summary>
        public float ApproachDepth = 0.04f;

        /// <summary>Seconds after a press before the next can register.</summary>
        public float RefractorySeconds = 0.35f;

        bool _armed;
        bool _down;
        double _lastPress = double.NegativeInfinity;

        public bool Down => _down;

        /// <summary>
        /// Feeds the fingertip's position in button space. Returns true on the one update where
        /// a press happens.
        /// </summary>
        public bool Update(double timeSeconds, Vec3 fingertip)
        {
            var within = System.Math.Abs(fingertip.X) <= HalfWidth && System.Math.Abs(fingertip.Y) <= HalfHeight;

            if (_down)
            {
                if (fingertip.Z > -ReleaseDepth || !within) _down = false;
                return false;
            }

            // Armed only by being in front of the face, inside its outline.
            if (within && fingertip.Z > 0f && fingertip.Z < ApproachDepth) _armed = true;
            else if (!within || fingertip.Z >= ApproachDepth) _armed = false;

            if (_armed && within && fingertip.Z <= -PressDepth && timeSeconds - _lastPress >= RefractorySeconds)
            {
                _down = true;
                _armed = false;
                _lastPress = timeSeconds;
                return true;
            }
            return false;
        }

        /// <summary>Forgets the finger: no hand in view.</summary>
        public void Lost()
        {
            _armed = false;
            _down = false;
        }
    }
}
