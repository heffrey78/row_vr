namespace RowVR.Stroke
{
    /// <summary>
    /// Where the rower is in the stroke cycle.
    ///
    /// <see cref="Drive"/> and <see cref="Recovery"/> are sustained phases. <see cref="Catch"/>
    /// and <see cref="Finish"/> are the instants between them and are reported for exactly one
    /// update each — they exist so a caller can fire audio, haptics or a splash on the frame
    /// the transition happens, without having to diff the phase itself.
    /// </summary>
    public enum StrokePhase
    {
        /// <summary>Not enough motion observed yet to know the rower's range. No stroke is being tracked.</summary>
        Unknown = 0,

        /// <summary>One update only: blades enter the water, the drive begins.</summary>
        Catch = 1,

        /// <summary>Handle travelling toward the body. Legs, then back, then arms.</summary>
        Drive = 2,

        /// <summary>One update only: handle is at the body, the drive ends.</summary>
        Finish = 3,

        /// <summary>Handle travelling away from the body, back to the catch. Should be slower than the drive.</summary>
        Recovery = 4,
    }
}
