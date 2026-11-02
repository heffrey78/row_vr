using System;

namespace RowVR.Stroke
{
    /// <summary>
    /// Where a piece of scenery sits on an endless course, relative to the rower.
    ///
    /// The rower never moves (#39); the course moves past. Each piece has a fixed place
    /// on the course, and as the boat travels it slides past the rower. Once it is further
    /// ahead than the window allows it wraps round to behind, so a small set of pieces makes
    /// an endless course. Both ends of the window sit beyond the fog, so the wrap is never
    /// seen.
    ///
    /// Positions are along the rower's facing direction: positive is in front of the rower.
    /// A sculler faces the stern, so as the boat travels, fixed scenery moves forward in the
    /// rower's view: it comes from behind their back, passes beside them and recedes ahead.
    ///
    /// Distance is a double and is reduced modulo the window before anything becomes a float,
    /// so the result keeps its precision after any length of session.
    /// </summary>
    public static class CourseWrap
    {
        /// <summary>
        /// Position of a piece whose place on the course is <paramref name="place"/>, after
        /// <paramref name="distance"/> metres of travel, wrapped into [-behind, ahead).
        /// </summary>
        public static float Position(double place, double distance, float behind, float ahead)
        {
            if (behind < 0f || ahead <= -behind) throw new ArgumentException("window must have positive length");
            double length = ahead + (double)behind;
            var shifted = (place + distance + behind) % length;
            if (shifted < 0d) shifted += length;
            return (float)(shifted - behind);
        }
    }
}
