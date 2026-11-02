using System;
using System.Collections.Generic;

namespace RowVR.Stroke
{
    public enum WorkoutKind
    {
        JustRow,
        Timed,
        Distance,
        Intervals,
    }

    /// <summary>A workout the rower can choose on the menu (#31).</summary>
    public readonly struct Workout
    {
        public readonly WorkoutKind Kind;
        public readonly string Name;
        public readonly double TargetSeconds;
        public readonly double TargetMetres;
        public readonly double WorkSeconds;
        public readonly double RestSeconds;
        public readonly int Repeats;

        Workout(WorkoutKind kind, string name, double seconds = 0, double metres = 0, double work = 0, double rest = 0, int repeats = 0)
        {
            Kind = kind;
            Name = name;
            TargetSeconds = seconds;
            TargetMetres = metres;
            WorkSeconds = work;
            RestSeconds = rest;
            Repeats = repeats;
        }

        public static Workout JustRow() => new Workout(WorkoutKind.JustRow, "just row");
        public static Workout Timed(double seconds) => new Workout(WorkoutKind.Timed, MetricsFormat.Time(seconds), seconds);
        public static Workout Distance(double metres) => new Workout(WorkoutKind.Distance, MetricsFormat.Distance(metres) + " m", metres: metres);

        public static Workout Intervals(int repeats, double work, double rest) =>
            new Workout(WorkoutKind.Intervals, $"{repeats} x {MetricsFormat.Time(work)}", work: work, rest: rest, repeats: repeats);

        /// <summary>The choices on the menu, in order.</summary>
        public static IReadOnlyList<Workout> Presets { get; } = new[]
        {
            JustRow(),
            Timed(20 * 60),
            Distance(2000),
            Intervals(4, 180, 60),
        };
    }

    /// <summary>
    /// Where the rower is in their workout, from the session's time and distance: time or
    /// metres left, which interval and whether it is work or rest, and each interval's
    /// distance. Engine-free and tested (WorkoutTests).
    /// </summary>
    public sealed class WorkoutProgress
    {
        readonly List<double> _intervalMetres = new List<double>();
        double _workStartMetres;
        int _currentWork = -1;
        int _recordedWork = -1;

        public WorkoutProgress(Workout workout) => Workout = workout;

        public Workout Workout { get; }
        public bool Finished { get; private set; }

        /// <summary>Seconds left in a timed piece, or in the current interval phase.</summary>
        public double SecondsLeft { get; private set; }

        /// <summary>Metres left in a distance piece.</summary>
        public double MetresLeft { get; private set; }

        /// <summary>1-based interval number; 0 outside intervals.</summary>
        public int Interval { get; private set; }

        public bool Resting { get; private set; }

        /// <summary>Metres covered in each finished work interval.</summary>
        public IReadOnlyList<double> IntervalMetres => _intervalMetres;

        public void Update(double elapsedSeconds, double distanceMetres)
        {
            if (Finished) return;
            switch (Workout.Kind)
            {
                case WorkoutKind.Timed:
                    SecondsLeft = Math.Max(0, Workout.TargetSeconds - elapsedSeconds);
                    Finished = SecondsLeft <= 0;
                    break;

                case WorkoutKind.Distance:
                    MetresLeft = Math.Max(0, Workout.TargetMetres - distanceMetres);
                    Finished = MetresLeft <= 0;
                    break;

                case WorkoutKind.Intervals:
                    UpdateIntervals(elapsedSeconds, distanceMetres);
                    break;
            }
        }

        void UpdateIntervals(double elapsed, double distance)
        {
            var cycle = Workout.WorkSeconds + Workout.RestSeconds;
            var index = (int)Math.Floor(elapsed / cycle);
            var into = elapsed - index * cycle;

            // The last rest is not rowed: the workout ends with the last work interval.
            if (index >= Workout.Repeats || (index == Workout.Repeats - 1 && into >= Workout.WorkSeconds))
            {
                CloseWork(Workout.Repeats - 1, distance);
                Finished = true;
                Interval = Workout.Repeats;
                Resting = false;
                SecondsLeft = 0;
                return;
            }

            Interval = index + 1;
            Resting = into >= Workout.WorkSeconds;
            if (Resting)
            {
                CloseWork(index, distance);
                SecondsLeft = cycle - into;
            }
            else
            {
                // Entering a work interval: its metres count from here.
                if (_currentWork != index)
                {
                    _currentWork = index;
                    _workStartMetres = distance;
                }
                SecondsLeft = Workout.WorkSeconds - into;
            }
        }

        void CloseWork(int index, double distance)
        {
            if (_recordedWork >= index || _currentWork != index) return;
            _intervalMetres.Add(distance - _workStartMetres);
            _recordedWork = index;
        }
    }
}
