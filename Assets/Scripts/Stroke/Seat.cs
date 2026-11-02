using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// Where the rower sits in the boat (#96): the point the boat is built around (the middle of
    /// the stroke, where the hands average out) and the way it points (the machine's axis).
    ///
    /// While the rower rows, the centre follows a slow average of the hands and the heading
    /// learns from their travel (MachineAxis). While they rest it holds still. It used to follow
    /// the hands all the time, so the boat and the water sank with them into the lap and rose
    /// with them to the menu.
    ///
    /// Rowing also teaches it where the hands sit relative to the eyes. Seating uses that, not
    /// wherever the hands happen to be, so a seat taken at rest lands where the handle would be.
    ///
    /// The rower is re-seated:
    /// - when the headset's recenter moves the tracking space: <see cref="SpaceMoved"/> from the
    ///   runtime's event, or a head jump no person can make in one frame if the event never
    ///   comes. The heading is then taken from the head, since the recenter asks the rower to
    ///   look ahead and the learned heading points the wrong way in the moved space;
    /// - when they ask (<see cref="Reseat"/>, the wrist menu). The learned heading is kept: it
    ///   came from the machine, and the rower is looking at their wrist.
    /// </summary>
    public sealed class Seat
    {
        /// <summary>Time constant, seconds of rowing, for the centre to follow the hands.</summary>
        public float FollowSeconds = 4f;

        /// <summary>
        /// Time constant, seconds of rowing, for learning where the hands sit from the eyes. Longer
        /// than the stroke's own lean, so it settles on the middle of it.
        /// </summary>
        public float LearnSeconds = 15f;

        /// <summary>Wait after a recenter event before seating; one recenter can send several.</summary>
        public float SettleSeconds = 0.3f;

        /// <summary>A head move bigger than this between close frames is the space moving.</summary>
        public float JumpMetres = 0.3f;

        /// <summary>A turn of the level heading bigger than this between close frames is the space moving.</summary>
        public float JumpDegrees = 45f;

        /// <summary>Frames further apart than this are a tracking gap, not a jump.</summary>
        public double JumpWithinSeconds = 0.05;

        readonly MachineAxis _axis = new MachineAxis();

        double _pendingAt = double.NaN;
        bool _pendingHeadingFromHead;
        bool _hasPrevious;
        double _previousTime;
        Vec3 _previousHead;
        Vec3 _previousHeadForward;

        public bool HasSeat { get; private set; }

        /// <summary>The middle of the stroke: the average hand position while rowing.</summary>
        public Vec3 Centre { get; private set; }

        /// <summary>Level unit vector along the machine, toward the stern (the rower's front).</summary>
        public Vec3 Forward => _axis.Forward;

        /// <summary>How far the hands sit below the eyes while rowing, learned. Default: the recordings' median.</summary>
        public float HandsBelowEyes { get; private set; } = 0.37f;

        /// <summary>How far the hands sit ahead of the eyes along the heading while rowing, learned.</summary>
        public float HandsAhead { get; private set; } = 0.40f;

        /// <summary>
        /// Counts seatings, the first included. Anything placed once around the seat (the course)
        /// compares it to know when to place itself again.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>The tracking space moved (a recenter). Re-seats once the events settle, heading from the head.</summary>
        public void SpaceMoved(double timeSeconds) => Request(timeSeconds + SettleSeconds, headingFromHead: true);

        /// <summary>The rower asked to be re-seated. Keeps the learned heading.</summary>
        public void Reseat(double timeSeconds) => Request(timeSeconds, headingFromHead: false);

        void Request(double at, bool headingFromHead)
        {
            if (double.IsNaN(_pendingAt) || at > _pendingAt) _pendingAt = at;
            _pendingHeadingFromHead |= headingFromHead;
        }

        /// <summary>Feeds a tracked frame. <paramref name="rowing"/>: a stroke is being tracked.</summary>
        public void Update(double timeSeconds, Vec3 head, Vec3 headForward, Vec3 hands, bool rowing)
        {
            var dt = _hasPrevious ? timeSeconds - _previousTime : 0d;
            if (_hasPrevious && dt > 0d && dt <= JumpWithinSeconds && Jumped(head, headForward))
                SpaceMoved(timeSeconds);
            _hasPrevious = true;
            _previousTime = timeSeconds;
            _previousHead = head;
            _previousHeadForward = headForward;

            if (!HasSeat)
            {
                SitDown(head, headForward, headingFromHead: true);
                return;
            }

            if (!double.IsNaN(_pendingAt))
            {
                // Hold until it settles: the space may still be moving.
                if (timeSeconds < _pendingAt) return;
                SitDown(head, headForward, _pendingHeadingFromHead);
                _pendingAt = double.NaN;
                _pendingHeadingFromHead = false;
                return;
            }

            if (!rowing) return;

            _axis.Update(timeSeconds, hands);

            // A tracking gap is not seconds of rowing.
            var step = (float)Math.Min(dt, 0.1d);
            var alpha = 1f - (float)Math.Exp(-step / Math.Max(0.01f, FollowSeconds));
            Centre = Vec3.Lerp(Centre, hands, alpha);

            var learn = 1f - (float)Math.Exp(-step / Math.Max(0.01f, LearnSeconds));
            var offset = hands - head;
            HandsBelowEyes += (-offset.Y - HandsBelowEyes) * learn;
            HandsAhead += (Vec3.Dot(Level(offset), Forward) - HandsAhead) * learn;
        }

        void SitDown(Vec3 head, Vec3 headForward, bool headingFromHead)
        {
            if (headingFromHead || !_axis.HasAxis) _axis.Seed(headForward);
            Centre = head + Forward * HandsAhead - Vec3.Up * HandsBelowEyes;
            HasSeat = true;
            Version++;
        }

        bool Jumped(Vec3 head, Vec3 headForward)
        {
            if (Vec3.Distance(head, _previousHead) > JumpMetres) return true;

            // Looking steeply up or down, the level heading swings with a tiny nod: ignore it.
            var before = Level(_previousHeadForward);
            var after = Level(headForward);
            if (before.SqrMagnitude < 0.25f || after.SqrMagnitude < 0.25f) return false;
            var cos = Vec3.Dot(before.Normalized, after.Normalized);
            return cos < (float)Math.Cos(JumpDegrees * Math.PI / 180d);
        }

        static Vec3 Level(Vec3 v) => new Vec3(v.X, 0f, v.Z);
    }
}
