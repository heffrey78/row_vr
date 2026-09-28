using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Two sculling oars that sweep and dip with the stroke (REQ-0002-FUNC). The pose comes
    /// from <see cref="OarKinematics"/>, which is tested against the rowing-machine
    /// recordings; this only places and draws it.
    ///
    /// The oarlocks need a boat to sit on, and there is no boat yet: the rower's machine is
    /// the only fixed thing, and the app cannot see it. So the boat frame is inferred the way
    /// the detector infers its seat reference. Its centre follows a slow average of the
    /// handle position, and its forward direction a slow average of the head's levelled
    /// forward. Within a stroke the oarlocks stay put, while the rower slides past them;
    /// over a session they settle to wherever the rower actually sits. A real boat model can
    /// replace this frame later.
    ///
    /// Sign convention, from <see cref="OarSettings"/>: positive sweep is toward the catch,
    /// with the handle forward and the blade reaching back toward the bow.
    ///
    /// Placeholder geometry, built in code. Includes a water plane, just enough to judge
    /// blade depth until the scenery requirement adds real water.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class OarsView : MonoBehaviour
    {
        [Header("Rigging (metres)")]
        [Tooltip("Distance from the boat's centreline to each oarlock. Sculls are rigged at ~0.8 m.")]
        [SerializeField] float oarlockSpread = 0.8f;

        [Tooltip("Oar length from the oarlock to the handle end.")]
        [SerializeField] float inboard = 0.88f;

        [Tooltip("Oar length from the oarlock to the blade tip.")]
        [SerializeField] float outboard = 2.0f;

        [Tooltip("Height of the water below the oarlocks.")]
        [SerializeField] float waterBelowOarlock = 0.25f;

        [Tooltip("How far the blade's centre sits below the surface when fully buried.")]
        [SerializeField] float buriedDepth = 0.08f;

        [Tooltip("How far the blade's centre clears the surface when out of the water.")]
        [SerializeField] float clearance = 0.12f;

        [Header("Boat frame")]
        [Tooltip("Time constant, seconds, for the boat frame to follow the rower. Long enough " +
                 "that the oarlocks do not move with the seat during a stroke.")]
        [SerializeField] float frameFollowSeconds = 4f;

        [SerializeField] bool showWater = true;

        const float BladeLength = 0.45f;
        const float BladeHeight = 0.2f;
        const float ShaftDiameter = 0.035f;

        RowingRig _rig;
        readonly OarKinematics _kinematics = new OarKinematics();
        Transform _port, _starboard, _water;
        bool _hasFrame;
        Vector3 _centre;
        Vector3 _forward;

        /// <summary>The oar pose drawn this frame.</summary>
        public OarPose Pose { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _port = CreateOar("Port Oar");
            _starboard = CreateOar("Starboard Oar");

            if (showWater)
            {
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                water.name = "Water (placeholder)";
                Destroy(water.GetComponent<Collider>());
                water.GetComponent<Renderer>().material.color = new Color(0.15f, 0.3f, 0.45f);
                // A unit plane is 10 m across.
                water.transform.localScale = Vector3.one * 0.8f;
                _water = water.transform;
            }

            SetVisible(false);
        }

        void OnDestroy()
        {
            if (_port != null) Destroy(_port.gameObject);
            if (_starboard != null) Destroy(_starboard.gameObject);
            if (_water != null) Destroy(_water.gameObject);
        }

        // LateUpdate so the rig has already taken this frame's pose in its Update.
        void LateUpdate()
        {
            // No pose, no new state: hold the oars where they are, as the handle does.
            if (!_rig.IsTracking) return;

            UpdateFrame(_rig.LastSample);
            Pose = _kinematics.Update(Time.timeAsDouble, _rig.State);

            var right = Vector3.Cross(Vector3.up, _forward);
            var oarlockY = _centre.y;
            var waterY = oarlockY - waterBelowOarlock;
            var bladeY = Mathf.Lerp(waterY + clearance, waterY - buriedDepth, Pose.BladeDepth);

            PlaceOar(_port, -right, oarlockY, bladeY);
            PlaceOar(_starboard, right, oarlockY, bladeY);

            if (_water != null) _water.position = new Vector3(_centre.x, waterY, _centre.z);
            SetVisible(true);
        }

        void UpdateFrame(HandSample sample)
        {
            var handle = ((sample.LeftHand + sample.RightHand) * 0.5f).ToVector3();
            var forward = sample.HeadForward.ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = _hasFrame ? _forward : Vector3.forward;
            forward.Normalize();

            if (!_hasFrame)
            {
                _centre = handle;
                _forward = forward;
                _hasFrame = true;
                return;
            }

            var alpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, frameFollowSeconds));
            _centre = Vector3.Lerp(_centre, handle, alpha);
            _forward = Vector3.Slerp(_forward, forward, alpha).normalized;
        }

        /// <summary>
        /// Puts one oar's pivot at its oarlock and points its shaft so the blade's centre is
        /// at <paramref name="bladeY"/>. Levering the blade down lifts the handle end, as it
        /// does on a real oar.
        /// </summary>
        void PlaceOar(Transform oar, Vector3 outward, float oarlockY, float bladeY)
        {
            // The frame centre is the average handle position. An oar's handle end sits
            // inboard * sin(sweep) forward of its oarlock, so over a stroke it averages
            // halfway between the catch and finish values; shift the oarlocks back by that, so
            // the oar handles travel with the rower's hands along the boat.
            var s = _kinematics.Settings;
            var handleAhead = inboard * 0.5f *
                (Mathf.Sin(s.CatchAngleDegrees * Mathf.Deg2Rad) + Mathf.Sin(s.FinishAngleDegrees * Mathf.Deg2Rad));
            var oarlock = new Vector3(_centre.x, oarlockY, _centre.z)
                          - _forward * handleAhead
                          + outward * oarlockSpread;

            // Horizontal direction from the oarlock to the blade. At the catch (positive
            // sweep) the blade reaches back, away from the rower's forward.
            var sweep = Pose.SweepDegrees * Mathf.Deg2Rad;
            var flat = outward * Mathf.Cos(sweep) - _forward * Mathf.Sin(sweep);

            var reach = outboard - BladeLength * 0.5f;
            var drop = Mathf.Clamp(bladeY - oarlockY, -reach * 0.9f, reach * 0.9f);
            var along = flat * Mathf.Sqrt(reach * reach - drop * drop) + Vector3.up * drop;

            oar.SetPositionAndRotation(oarlock, Quaternion.LookRotation(along.normalized, Vector3.up));
        }

        /// <summary>
        /// An oar whose local +Z runs from the oarlock toward the blade, so placing it is one
        /// position and one rotation.
        /// </summary>
        Transform CreateOar(string name)
        {
            var oar = new GameObject(name).transform;

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(oar, false);
            var total = inboard + outboard;
            // A unit cylinder is 2 units long along Y; lay it along Z.
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localPosition = new Vector3(0f, 0f, (outboard - inboard) * 0.5f);
            shaft.transform.localScale = new Vector3(ShaftDiameter, total * 0.5f, ShaftDiameter);
            shaft.GetComponent<Renderer>().material.color = new Color(0.9f, 0.9f, 0.85f);

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(oar, false);
            blade.transform.localPosition = new Vector3(0f, 0f, outboard - BladeLength * 0.5f);
            // Squared: the broad face is upright and faces along the boat.
            blade.transform.localScale = new Vector3(0.02f, BladeHeight, BladeLength);
            blade.GetComponent<Renderer>().material.color = new Color(0.8f, 0.2f, 0.15f);

            return oar;
        }

        void SetVisible(bool visible)
        {
            foreach (var t in new[] { _port, _starboard, _water })
            {
                if (t == null || t.gameObject.activeSelf == visible) continue;
                t.gameObject.SetActive(visible);
            }
        }
    }
}
