using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The rower's choices from the hands-only menu (#31), kept between launches in
    /// PlayerPrefs: the machine's resistance dial, the workout, and the scene.
    /// </summary>
    public static class RowerSettings
    {
        const string ResistanceKey = "rowvr.resistance";
        const string WorkoutKey = "rowvr.workout";

        public const string LakeScene = "Row";
        public const string RapidsScene = "Rapids";

        public static int Resistance
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ResistanceKey, 5), 1, 10);
            set { PlayerPrefs.SetInt(ResistanceKey, Mathf.Clamp(value, 1, 10)); PlayerPrefs.Save(); }
        }

        /// <summary>Index into Workout.Presets.</summary>
        public static int WorkoutIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(WorkoutKey, 0), 0, Workout.Presets.Count - 1);
            set { PlayerPrefs.SetInt(WorkoutKey, Mathf.Clamp(value, 0, Workout.Presets.Count - 1)); PlayerPrefs.Save(); }
        }

        public static Workout Workout => Workout.Presets[WorkoutIndex];
    }
}
