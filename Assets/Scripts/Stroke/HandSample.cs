namespace RowVR.Stroke
{
    /// <summary>
    /// One frame of tracked pose: both hands plus the head. This is the entire input the
    /// stroke detector needs, and therefore the entire thing worth recording.
    ///
    /// Head orientation is carried as a forward vector rather than a quaternion. The
    /// detector only ever needs a direction, and a vector keeps this assembly free of
    /// quaternion maths it would otherwise have to reimplement engine-free.
    /// </summary>
    public readonly struct HandSample
    {
        public readonly double TimeSeconds;
        public readonly Vec3 LeftHand;
        public readonly Vec3 RightHand;
        public readonly Vec3 HeadPosition;
        public readonly Vec3 HeadForward;

        public HandSample(
            double timeSeconds,
            Vec3 leftHand,
            Vec3 rightHand,
            Vec3 headPosition,
            Vec3 headForward)
        {
            TimeSeconds = timeSeconds;
            LeftHand = leftHand;
            RightHand = rightHand;
            HeadPosition = headPosition;
            HeadForward = headForward;
        }
    }

    /// <summary>
    /// Where pose samples come from. The live headset and a recorded clip both implement
    /// this, so the detector — and every test around it — runs against one code path.
    /// </summary>
    public interface IHandPoseSource
    {
        /// <summary>
        /// Returns false when no pose is available: hands out of view, tracking lost, or a
        /// clip that has run out. Callers should hold the last known state rather than
        /// treating a gap as motion.
        /// </summary>
        bool TryGetPose(double timeSeconds, out HandSample sample);
    }
}
