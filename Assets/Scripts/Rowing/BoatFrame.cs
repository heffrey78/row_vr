using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Where the boat is, relative to the rower. The one frame the hull, the oars and the
    /// metrics panel all sit in, so they always agree.
    ///
    /// There is no machine to see, so the frame is inferred. Its centre follows a slow average
    /// of the hands, so within a stroke it holds still while the rower slides past, and over a
    /// session it settles to where they sit. Its heading is the machine's axis, learned from
    /// the hands' travel (MachineAxis, engine-free and tested): the handle can only move along
    /// the machine, whichever way the rower looks. It used to follow the head's facing, and
    /// looking to the side turned the boat.
    ///
    /// Runs its LateUpdate before the views (execution order -50), after RowingRig's Update has
    /// taken the frame's pose. Keeps WaterHeight on SceneryLayers matched to the oarlocks.
    ///
    /// The rigging is the scull's by default; the rapids' raft sets its own (higher oarlocks,
    /// so the rower sits up on the raft) when the scene is built.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(RowingRig))]
    public sealed class BoatFrame : MonoBehaviour
    {
        [Header("Rigging (metres)")]
        [Tooltip("Distance from the boat's centreline to each oarlock. Sculls are rigged at ~0.8 m.")]
        [SerializeField] float oarlockSpread = 0.8f;

        [Tooltip("Height of the water below the oarlocks.")]
        [SerializeField] float waterBelowOarlock = 0.25f;

        [Tooltip("How far the oarlocks sit below the average hand position. 0 for a scull, whose " +
                 "handles run level with the oarlocks; an oar raft's rower sits up over them and " +
                 "levers the oars down to the water.")]
        [SerializeField] float oarlockBelowHands = 0f;

        [Tooltip("How far the oarlocks sit behind the average hand position. A sculler's catch " +
                 "reaches further past the oarlock than the finish comes back behind it; 0.1 m " +
                 "centres a 0.88 m inboard on the stroke.")]
        [SerializeField] float oarlockSetBack = 0.1f;

        [Tooltip("Time constant, seconds, for the frame's centre to follow the rower. Long enough " +
                 "that the oarlocks do not move with the seat during a stroke.")]
        [SerializeField] float followSeconds = 4f;

        RowingRig _rig;
        SceneryLayers _scenery;
        readonly MachineAxis _axis = new MachineAxis();

        public bool HasFrame { get; private set; }

        /// <summary>Average hand position: the middle of the stroke.</summary>
        public Vector3 Centre { get; private set; }

        /// <summary>The rower's levelled facing, toward the stern.</summary>
        public Vector3 Forward { get; private set; } = Vector3.forward;

        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public float OarlockHeight => Centre.y - oarlockBelowHands;
        public float WaterHeight => OarlockHeight - waterBelowOarlock;
        public float OarlockSpread => oarlockSpread;
        public float OarlockAboveWater => waterBelowOarlock;
        public float HandsAboveWater => waterBelowOarlock + oarlockBelowHands;
        public float OarlockSetBack => oarlockSetBack;

        /// <summary>The oarlock's position; side -1 is the rower's left, +1 their right. Rocks with the boat.</summary>
        public Vector3 Oarlock(int side) => BoatPoint(-oarlockSetBack, oarlockSpread * side, waterBelowOarlock);

        /// <summary>
        /// A point in the boat's level frame: metres toward the stern, to the right, and above the
        /// water. It does not rock, so a card placed with it stays steady.
        /// </summary>
        public Vector3 Point(float towardStern, float toRight, float aboveWater) =>
            new Vector3(Centre.x, WaterHeight, Centre.z) + Forward * towardStern + Right * toRight + Vector3.up * aboveWater;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);

        /// <summary>
        /// The boat's rocking (#90): pitch and roll about a pivot in the boat, plus heave. Level
        /// on the lake; RaftView sets it in the rapids.
        /// </summary>
        public Quaternion Rock { get; private set; } = Quaternion.identity;

        public float Heave { get; private set; }

        // Boat space: (to the right, above the water, toward the stern).
        Vector3 _rockPivot;

        /// <summary>Sets the rocking: bow (Forward) up and right side up, degrees, and heave, metres.</summary>
        public void SetRock(float pitchDegrees, float rollDegrees, float heave, float pivotTowardStern, float pivotAboveWater)
        {
            Rock = Quaternion.Euler(-pitchDegrees, 0f, rollDegrees);
            Heave = heave;
            _rockPivot = new Vector3(0f, pivotAboveWater, pivotTowardStern);
        }

        /// <summary>A point on the boat itself, which rocks with it: as Point, then pitched, rolled and heaved.</summary>
        public Vector3 BoatPoint(float towardStern, float toRight, float aboveWater)
        {
            var local = new Vector3(toRight, aboveWater, towardStern);
            var rocked = _rockPivot + Rock * (local - _rockPivot) + Vector3.up * Heave;
            return new Vector3(Centre.x, WaterHeight, Centre.z) + Rotation * rocked;
        }

        /// <summary>The boat's own rotation, rocking included.</summary>
        public Quaternion BoatRotation => Rotation * Rock;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
        }

        void LateUpdate()
        {
            if (!_rig.IsTracking) return;
            Follow(_rig.LastSample);
            if (_scenery != null) _scenery.WaterHeight = WaterHeight;
        }

        void Follow(HandSample sample)
        {
            var midpoint = (sample.LeftHand + sample.RightHand) * 0.5f;
            var hands = midpoint.ToVector3();

            if (!HasFrame)
            {
                // The head is only the first guess; the hands take over as soon as they row.
                _axis.Seed(sample.HeadForward);
                Centre = hands;
                Forward = _axis.Forward.ToVector3();
                HasFrame = true;
                return;
            }

            _axis.Update(sample.TimeSeconds, midpoint);
            Forward = _axis.Forward.ToVector3();

            var alpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, followSeconds));
            Centre = Vector3.Lerp(Centre, hands, alpha);
        }
    }
}
