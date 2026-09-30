using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Runs the boat (REQ-0003-FUNC) and the session numbers (REQ-0006-FUNC) from the rig, and
    /// drives the scenery. Both models are engine-free and tested against the rowing-machine
    /// recordings (BoatModelTests, SessionStatsTests); this only feeds them every frame, the
    /// way those tests do: an update with the detector's state when there is a pose, a glide
    /// when there is not.
    ///
    /// The scenery moves at the boat's visible speed, smoothed so there is no per-stroke lurch
    /// (ADR-0010). Distance and pace on the panel come from the true speed.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(RowingRig))]
    public sealed class BoatMotion : MonoBehaviour
    {
        [Tooltip("Your machine's resistance dial, 1-10. Higher reads as more speed for the same stroke.")]
        [Range(1, 10)]
        [SerializeField] int resistance = 5;

        [Tooltip("Set the scenery's speed from the boat. Off in the rapids, where RapidsMotion " +
                 "adds the current and sets it instead.")]
        [SerializeField] bool driveScenery = true;

        RowingRig _rig;
        SceneryLayers _scenery;

        public BoatModel Boat { get; private set; }
        public SessionStats Session { get; private set; }

        /// <summary>The chosen workout's progress; restarts with each new session.</summary>
        public WorkoutProgress Workout { get; private set; }
        bool _wasActive;

        public int Resistance
        {
            get => resistance;
            set => resistance = Mathf.Clamp(value, 1, 10);
        }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            Boat = new BoatModel();
            Session = new SessionStats();
            // The menu's saved choices (REQ-0008); the Inspector value is only the first default.
            if (UnityEngine.PlayerPrefs.HasKey("rowvr.resistance")) resistance = RowerSettings.Resistance;
            Workout = new WorkoutProgress(RowerSettings.Workout);
            if (_scenery != null && driveScenery) _scenery.UseDebugSpeed = false;
        }

        // LateUpdate: RowingRig's Update has produced this frame's state.
        void LateUpdate()
        {
            Boat.Settings.ResistanceLevel = resistance;
            var t = Time.timeAsDouble;
            if (_rig.IsTracking)
            {
                Boat.Update(t, _rig.State);
                Session.Update(t, _rig.State, Boat.Distance);
            }
            else
            {
                Boat.Glide(t);
                Session.Tick(t, Boat.Distance);
            }

            // A new session starts the chosen workout afresh.
            if (Session.Active && !_wasActive) Workout = new WorkoutProgress(RowerSettings.Workout);
            _wasActive = Session.Active;
            if (Session.Active) Workout.Update(Session.ElapsedSeconds, Session.DistanceMetres);

            if (_scenery != null && driveScenery) _scenery.Speed = Boat.VisibleSpeed;
        }
    }
}
