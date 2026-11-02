using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;
using UnityEngine.XR;

namespace RowVR.Rowing
{
    /// <summary>
    /// Where the boat is, relative to the rower. The one frame the hull, the oars and the
    /// metrics panel all sit in, so they always agree.
    ///
    /// There is no machine to see, so the frame is inferred, by Seat (engine-free and tested,
    /// #96). While rowing, its centre follows a slow average of the hands, so within a stroke it
    /// holds still while the rower slides past, and its heading is the machine's axis, learned
    /// from the hands' travel. While resting it holds still. A headset recenter, or the wrist
    /// menu's re-seat, puts it back round the rower at once.
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

        [Tooltip("Time constant, seconds of rowing, for the frame's centre to follow the rower. Long " +
                 "enough that the oarlocks do not move with the seat during a stroke.")]
        [SerializeField] float followSeconds = 4f;

        RowingRig _rig;
        SceneryLayers _scenery;
        readonly Seat _seat = new Seat();
        static readonly List<XRInputSubsystem> InputSubsystems = new List<XRInputSubsystem>();
        XRInputSubsystem _input;

        public bool HasFrame => _seat.HasSeat;

        /// <summary>Average hand position: the middle of the stroke, at oarlock height.</summary>
        public Vector3 Centre => _seat.Centre.ToVector3();

        /// <summary>The rower's levelled facing, toward the stern.</summary>
        public Vector3 Forward => _seat.Forward.ToVector3();

        /// <summary>Counts seatings. Anything placed once around the boat places itself again when it changes.</summary>
        public int SeatVersion => _seat.Version;

        /// <summary>Puts the boat back round the rower, keeping the heading (the wrist menu's re-seat).</summary>
        public void Reseat() => _seat.Reseat(Time.timeAsDouble);

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
            _seat.FollowSeconds = followSeconds;
        }

        void OnDisable() => Listen(null);

        void LateUpdate()
        {
            ListenForRecenter();
            if (!_rig.IsTracking) return;

            // The frame's clock, not the sample's: a replayed clip carries its own timestamps,
            // and recenters and re-seats are timed by the frame.
            var sample = _rig.LastSample;
            var rowing = _rig.State.Phase != StrokePhase.Unknown;
            _seat.Update(Time.timeAsDouble, sample.HeadPosition, sample.HeadForward,
                         (sample.LeftHand + sample.RightHand) * 0.5f, rowing);
            if (_scenery != null) _scenery.WaterHeight = WaterHeight;
        }

        // The headset's recenter moves the tracking space; OpenXR reports it as
        // trackingOriginUpdated once Recentering has turned it on. The subsystem can start after
        // this component, so keep looking until one is running.
        void ListenForRecenter()
        {
            if (_input != null && _input.running) return;
            SubsystemManager.GetSubsystems(InputSubsystems);
            XRInputSubsystem running = null;
            foreach (var s in InputSubsystems)
                if (s.running) { running = s; break; }
            Listen(running);
        }

        void Listen(XRInputSubsystem input)
        {
            if (input == _input) return;
            if (_input != null) _input.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _input = input;
            if (_input != null) _input.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        void OnTrackingOriginUpdated(XRInputSubsystem input)
        {
            Debug.Log("[RowVR] Tracking space moved (recenter): re-seating.");
            _seat.SpaceMoved(Time.timeAsDouble);
        }
    }
}
