using System;

namespace RowVR.Stroke
{
    /// <summary>One number on the sky board, with its label.</summary>
    public readonly struct ReadoutLine
    {
        public readonly string Label;
        public readonly string Value;

        public ReadoutLine(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// What the sky board shows (#99), and where: pace per 500 m as the headline, time on the
    /// left, distance on the right, and a footer line. While rowing it reads like a monitor; a
    /// workout (#31) takes over the lines it counts down; when a session ends it becomes the
    /// session's summary until the next stroke. The same numbers the card showed (#29),
    /// arranged for the sky.
    /// </summary>
    public sealed class SkyReadout
    {
        public ReadoutLine Headline { get; private set; }
        public ReadoutLine Left { get; private set; }
        public ReadoutLine Right { get; private set; }
        public string Footer { get; private set; } = "";

        /// <param name="workout">The workout in progress, or null.</param>
        /// <param name="workoutName">The chosen workout's name, shown before the first stroke.</param>
        public void Update(SessionStats session, WorkoutProgress workout, string workoutName)
        {
            if (!session.Active && session.LastSummary.HasValue)
            {
                var s = session.LastSummary.Value;
                Headline = new ReadoutLine("avg /500m", MetricsFormat.Split(s.AverageSplitSeconds));
                Left = new ReadoutLine("session", MetricsFormat.Time(s.ElapsedSeconds));
                Right = new ReadoutLine("metres", MetricsFormat.Distance(s.DistanceMetres));
                Footer = $"{s.Strokes} strokes  ·  avg rate {MetricsFormat.Rate(s.AverageRate)}";
                return;
            }

            Headline = new ReadoutLine("/500m", MetricsFormat.Split(session.StrokeSplitSeconds));
            Left = new ReadoutLine("time", MetricsFormat.Time(session.ElapsedSeconds));
            Right = new ReadoutLine("metres", MetricsFormat.Distance(Steps(session.DistanceMetres, down: true)));

            if (session.Active && workout != null && workout.Workout.Kind != WorkoutKind.JustRow)
            {
                if (workout.Finished)
                {
                    Footer = "workout complete" + IntervalSummary(workout);
                    return;
                }
                switch (workout.Workout.Kind)
                {
                    case WorkoutKind.Timed:
                        Left = new ReadoutLine("time left", MetricsFormat.Time(workout.SecondsLeft));
                        break;
                    case WorkoutKind.Distance:
                        Right = new ReadoutLine("metres left", MetricsFormat.Distance(Steps(workout.MetresLeft, down: false)));
                        break;
                    case WorkoutKind.Intervals:
                        Left = new ReadoutLine($"{(workout.Resting ? "rest" : "work")} {workout.Interval} of {workout.Workout.Repeats}",
                                               MetricsFormat.Time(workout.SecondsLeft));
                        break;
                }
            }

            Footer = session.Active
                ? $"rate {MetricsFormat.Rate(session.Rate)}  ·  {session.Strokes} strokes  ·  avg {MetricsFormat.Split(session.AverageSplitSeconds)}"
                : $"{workoutName}  ·  take a stroke to begin";
        }

        /// <summary>
        /// Live distance in whole tens. At rowing speed the metres' last digit changes several
        /// times a second, faster than a cloud can re-form, so it never read. Counting up rounds
        /// down and counting down rounds up, so neither claims metres not yet rowed. The summary
        /// keeps exact metres.
        /// </summary>
        public const double DistanceStep = 10d;

        static double Steps(double metres, bool down) =>
            (down ? Math.Floor(metres / DistanceStep) : Math.Ceiling(metres / DistanceStep)) * DistanceStep;

        static string IntervalSummary(WorkoutProgress workout)
        {
            if (workout.IntervalMetres.Count == 0) return "";
            var parts = "";
            foreach (var m in workout.IntervalMetres) parts += (parts.Length > 0 ? " " : "") + MetricsFormat.Distance(m);
            return "  ·  " + parts + " m";
        }
    }
}
