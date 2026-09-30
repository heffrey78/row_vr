using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Where the boat is, relative to the rower. The one frame the hull, the oars and the
    /// metrics panel all sit in, so they always agree.
    ///
    /// There is no machine to see, so the frame is inferred, the way the detector infers its
    /// seat reference. Its centre follows a slow average of the hands, and its forward a slow
    /// average of the head's levelled forward (the rower faces the stern, ADR-0010). Within a
    /// stroke it holds still while the rower slides past; over a session it settles to where
    /// the rower actually sits.
    ///
    /// Runs its LateUpdate before the views (execution order -50), after RowingRig's Update has
    /// taken the frame's pose. Keeps WaterHeight on SceneryLayers matched to the oarlocks.
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

        [Tooltip("How far the oarlocks sit behind the average hand position. A sculler's catch " +
                 "reaches further past the oarlock than the finish comes back behind it; 0.1 m " +
                 "centres a 0.88 m inboard on the stroke.")]
        [SerializeField] float oarlockSetBack = 0.1f;

        [Tooltip("Time constant, seconds, for the frame to follow the rower. Long enough that the " +
                 "oarlocks do not move with the seat during a stroke.")]
        [SerializeField] float followSeconds = 4f;

        RowingRig _rig;
        SceneryLayers _scenery;

        public bool HasFrame { get; private set; }

        /// <summary>Average hand position: the middle of the stroke, at oarlock height.</summary>
        public Vector3 Centre { get; private set; }

        /// <summary>The rower's levelled facing, toward the stern.</summary>
        public Vector3 Forward { get; private set; } = Vector3.forward;

        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        public float OarlockHeight => Centre.y;
        public float WaterHeight => Centre.y - waterBelowOarlock;
        public float OarlockSpread => oarlockSpread;
        public float OarlockAboveWater => waterBelowOarlock;
        public float OarlockSetBack => oarlockSetBack;

        /// <summary>The oarlock's position; side -1 is the rower's left, +1 their right.</summary>
        public Vector3 Oarlock(int side) => Centre - Forward * oarlockSetBack + Right * (oarlockSpread * side);

        /// <summary>A point in the boat: metres toward the stern, to the right, and above the water.</summary>
        public Vector3 Point(float towardStern, float toRight, float aboveWater) =>
            new Vector3(Centre.x, WaterHeight, Centre.z) + Forward * towardStern + Right * toRight + Vector3.up * aboveWater;

        public Quaternion Rotation => Quaternion.LookRotation(Forward, Vector3.up);

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
            var hands = ((sample.LeftHand + sample.RightHand) * 0.5f).ToVector3();
            var forward = sample.HeadForward.ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Forward;
            forward.Normalize();

            if (!HasFrame)
            {
                Centre = hands;
                Forward = forward;
                HasFrame = true;
                return;
            }

            var alpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, followSeconds));
            Centre = Vector3.Lerp(Centre, hands, alpha);
            Forward = Vector3.Slerp(Forward, forward, alpha).normalized;
        }
    }
}
