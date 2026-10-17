using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Runs the rapids (#30): feeds RapidsRun from the boat model every frame, and moves
    /// the scenery by the raft's ground speed (its own speed plus the current) and sideways by
    /// its drift off the line. The rules and comfort limits live in RapidsRun (engine-free,
    /// tested); this only connects it.
    ///
    /// BoatMotion still runs the boat model and the session numbers, with its own scenery
    /// driving switched off.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    [RequireComponent(typeof(BoatMotion))]
    public sealed class RapidsMotion : MonoBehaviour
    {
        [Tooltip("Power, W, that holds the line in the middle rapid. 110 is steady work for the " +
                 "developer's recorded rowing (134 W).")]
        [SerializeField] float targetWatts = 110f;

        BoatMotion _motion;
        SceneryLayers _scenery;

        public RapidsRun Run { get; private set; }

        /// <summary>The rapids' standing waves: RiverScenery draws them and RaftView rides them (#90).</summary>
        public RiverWaves Waves { get; private set; }

        public float TargetWatts
        {
            get => targetWatts;
            set => targetWatts = Mathf.Max(20f, value);
        }

        void Awake()
        {
            _motion = GetComponent<BoatMotion>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (_scenery != null) _scenery.UseDebugSpeed = false;
            Restart();
        }

        /// <summary>Starts the run again from the top of the river.</summary>
        public void Restart()
        {
            var sections = RapidsRun.Standard(targetWatts);
            Run = new RapidsRun(sections);
            var river = FindFirstObjectByType<RiverScenery>();
            Waves = new RiverWaves(sections, river != null ? river.RiverHalfWidth : 14f);
        }

        // After BoatMotion (-40) has advanced the boat this frame.
        void LateUpdate()
        {
            var boat = _motion.Boat;
            if (boat == null) return;
            Run.Update(Time.timeAsDouble, boat.VisibleSpeed, boat.Power);

            if (_scenery == null) return;
            _scenery.Speed = Run.Finished ? 0f : Run.GroundSpeed;
            _scenery.LateralOffset = Run.Offset;
        }
    }
}
