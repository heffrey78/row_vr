using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Two sculling oars held in the rower's hands (REQ-0002-FUNC).
    ///
    /// While a stroke is being tracked, each oar's handle end is at the rower's hand: left
    /// hand, left oar. The oar swings about a fixed oarlock along the line from the hand
    /// through the oarlock, so the sweep is the rower's own motion. Blade depth comes from
    /// <see cref="OarKinematics"/>, which is tested against the rowing-machine recordings:
    /// on the machine the hands barely rise or fall, so hand height cannot drive the blade.
    /// A straight oar through an oarlock fixed in height cannot satisfy both at once, so the
    /// pivot rides up and down to sit on the straight line from the hand to the blade. There
    /// is no boat or oarlock drawn yet, so nothing shows the pivot moving.
    ///
    /// When no stroke is tracked (before the first, after a stop, after letting go) the oars
    /// leave the hands and ease to square with the blades out, so waving the hands about does
    /// not swing the oars.
    ///
    /// The oarlocks need a boat to sit on, and there is no boat yet. So the boat frame is
    /// inferred the way the detector infers its seat reference: its centre follows a slow
    /// average of the hands, and its forward a slow average of the head's levelled forward.
    /// Within a stroke the oarlocks stay put while the rower slides; over a session they
    /// settle to wherever the rower actually sits. A real boat model can replace this later.
    ///
    /// "Left" and "right" are from the rower's seat. Placeholder geometry, built in code.
    /// With SceneryLayers in the scene the oars sit in its Near layer and set its water
    /// height; without it, a placeholder water plane shows blade depth.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class OarsView : MonoBehaviour
    {
        [Header("Rigging (metres)")]
        [Tooltip("Distance from the boat's centreline to each oarlock. Sculls are rigged at ~0.8 m.")]
        [SerializeField] float oarlockSpread = 0.8f;

        [Tooltip("Oar length from the oarlock to the handle end, used when the oars are at rest.")]
        [SerializeField] float inboard = 0.88f;

        [Tooltip("Oar length from the oarlock to the blade tip.")]
        [SerializeField] float outboard = 2.0f;

        [Tooltip("Height of the water below the oarlocks.")]
        [SerializeField] float waterBelowOarlock = 0.25f;

        [Tooltip("How far the blade's centre sits below the surface when fully buried.")]
        [SerializeField] float buriedDepth = 0.08f;

        [Tooltip("How far the blade's centre clears the surface when out of the water.")]
        [SerializeField] float clearance = 0.12f;

        [Header("Motion")]
        [Tooltip("Time constant, seconds, for the boat frame to follow the rower. Long enough " +
                 "that the oarlocks do not move with the seat during a stroke.")]
        [SerializeField] float frameFollowSeconds = 4f;

        [Tooltip("Fastest the drawn sweep may change, degrees per second. Above a rower's " +
                 "fastest drive, so it only smooths picking up and putting down the oars.")]
        [SerializeField] float maxSweepDegreesPerSecond = 600f;

        [SerializeField] bool showWater = true;

        const float BladeLength = 0.45f;
        const float BladeHeight = 0.2f;
        const float ShaftDiameter = 0.035f;

        sealed class Oar
        {
            public Transform Inboard;   // shaft from the oarlock to the handle end
            public Transform Outboard;  // pivot at the oarlock; shaft and blade along +Z
            public float Sweep;         // drawn sweep, degrees, eased
        }

        RowingRig _rig;
        SceneryLayers _scenery;
        readonly OarKinematics _kinematics = new OarKinematics();
        Oar _left, _right;
        Transform _water;
        bool _hasFrame;
        Vector3 _centre;
        Vector3 _forward;

        /// <summary>The kinematic pose this frame; supplies blade depth and the rest state.</summary>
        public OarPose Pose { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _left = CreateOar("Left Oar");
            _right = CreateOar("Right Oar");

            // With scenery present, the oars belong to its Near layer and the scenery draws
            // the water; the oars only tell it how high the water is. Without scenery, keep
            // the placeholder so blade depth can still be judged.
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (_scenery != null)
            {
                foreach (var oar in new[] { _left, _right })
                {
                    oar.Inboard.SetParent(_scenery.Near, true);
                    oar.Outboard.SetParent(_scenery.Near, true);
                }
            }
            else if (showWater)
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

        void OnDisable() => SetVisible(false);

        void OnDestroy()
        {
            foreach (var oar in new[] { _left, _right })
            {
                if (oar == null) continue;
                if (oar.Inboard != null) Destroy(oar.Inboard.gameObject);
                if (oar.Outboard != null) Destroy(oar.Outboard.gameObject);
            }
            if (_water != null) Destroy(_water.gameObject);
        }

        // LateUpdate so the rig has already taken this frame's pose in its Update.
        void LateUpdate()
        {
            // No pose, no new state: hold the oars where they are, as the handle does.
            if (!_rig.IsTracking) return;

            var sample = _rig.LastSample;
            UpdateFrame(sample);
            Pose = _kinematics.Update(Time.timeAsDouble, _rig.State);

            var right = Vector3.Cross(Vector3.up, _forward);
            var oarlockY = _centre.y;
            var waterY = oarlockY - waterBelowOarlock;
            var bladeY = Mathf.Lerp(waterY + clearance, waterY - buriedDepth, Pose.BladeDepth);

            PlaceOar(_left, -right, sample.LeftHand.ToVector3(), oarlockY, bladeY);
            PlaceOar(_right, right, sample.RightHand.ToVector3(), oarlockY, bladeY);

            if (_water != null) _water.position = new Vector3(_centre.x, waterY, _centre.z);
            if (_scenery != null) _scenery.WaterHeight = waterY;
            SetVisible(true);
        }

        void UpdateFrame(HandSample sample)
        {
            var hands = ((sample.LeftHand + sample.RightHand) * 0.5f).ToVector3();
            var forward = sample.HeadForward.ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = _hasFrame ? _forward : Vector3.forward;
            forward.Normalize();

            if (!_hasFrame)
            {
                _centre = hands;
                _forward = forward;
                _hasFrame = true;
                return;
            }

            var alpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, frameFollowSeconds));
            _centre = Vector3.Lerp(_centre, hands, alpha);
            _forward = Vector3.Slerp(_forward, forward, alpha).normalized;
        }

        void PlaceOar(Oar oar, Vector3 outward, Vector3 hand, float oarlockY, float bladeY)
        {
            // The frame centre is the average hand position. A sculler's catch reaches
            // further past the oarlock than the finish comes back behind it, so set the
            // oarlocks back by the average of the two, keeping the sweep close to the
            // kinematic catch and finish angles.
            var s = _kinematics.Settings;
            var handleAhead = inboard * 0.5f *
                (Mathf.Sin(s.CatchAngleDegrees * Mathf.Deg2Rad) + Mathf.Sin(s.FinishAngleDegrees * Mathf.Deg2Rad));
            var oarlock = new Vector3(_centre.x, oarlockY, _centre.z)
                          - _forward * handleAhead
                          + outward * oarlockSpread;

            // Held: sweep is the line from the hand through the oarlock. At rest: the
            // kinematic sweep, which eases to square. Either way the drawn sweep is rate
            // limited, which is what smooths picking the oars up and putting them down.
            var held = !Pose.Resting;
            var target = Pose.SweepDegrees;
            if (held)
            {
                var toBlade = oarlock - hand;
                toBlade.y = 0f;
                if (toBlade.sqrMagnitude > 1e-4f)
                    target = Mathf.Atan2(-Vector3.Dot(toBlade, _forward), Vector3.Dot(toBlade, outward)) * Mathf.Rad2Deg;
            }
            oar.Sweep = Mathf.MoveTowards(oar.Sweep, target, maxSweepDegreesPerSecond * Time.deltaTime);

            // Horizontal direction from the oarlock to the blade. At the catch (positive
            // sweep) the blade reaches back, away from the rower's forward.
            var sweep = oar.Sweep * Mathf.Deg2Rad;
            var flat = outward * Mathf.Cos(sweep) - _forward * Mathf.Sin(sweep);

            var reach = outboard - BladeLength * 0.5f;
            var drop = Mathf.Clamp(bladeY - oarlockY, -reach * 0.9f, reach * 0.9f);
            var along = (flat * Mathf.Sqrt(reach * reach - drop * drop) + Vector3.up * drop).normalized;
            var pivot = oarlock;

            // The handle end is in the hand only while it is the hand's line that set the
            // sweep; while easing to or from rest it stays on the oar's own line.
            var inHand = held && Mathf.Abs(oar.Sweep - target) < 0.5f;
            if (inHand)
            {
                // Keep the oar straight from the hand to the blade. A pivot fixed in height
                // would bend it wherever the hand's height and the blade's depth disagree,
                // which is most of the stroke. There is no boat or oarlock drawn yet, so the
                // pivot rides up and down the few centimetres needed to sit on that line.
                var bladeCentre = oarlock + along * reach;
                var line = bladeCentre - hand;
                var inboardFlat = Vector3.ProjectOnPlane(oarlock - hand, Vector3.up).magnitude;
                var totalFlat = Vector3.ProjectOnPlane(line, Vector3.up).magnitude;
                if (totalFlat > 1e-3f)
                {
                    pivot = hand + line * Mathf.Clamp01(inboardFlat / totalFlat);
                    along = line.normalized;
                }
            }
            oar.Outboard.SetPositionAndRotation(pivot, Quaternion.LookRotation(along, Vector3.up));

            var handleEnd = inHand ? hand : pivot - along * inboard;
            var toHandle = handleEnd - pivot;
            var length = Mathf.Max(0.01f, toHandle.magnitude);
            oar.Inboard.SetPositionAndRotation(
                pivot + toHandle * 0.5f,
                Quaternion.FromToRotation(Vector3.up, toHandle / length));
            // A unit cylinder is 2 units long along Y.
            oar.Inboard.localScale = new Vector3(ShaftDiameter, length * 0.5f, ShaftDiameter);
        }

        Oar CreateOar(string name)
        {
            var shaftColour = new Color(0.9f, 0.9f, 0.85f);

            var inboardShaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inboardShaft.name = name + " Inboard";
            Destroy(inboardShaft.GetComponent<Collider>());
            inboardShaft.GetComponent<Renderer>().material.color = shaftColour;

            // Outboard: a pivot at the oarlock with its local +Z toward the blade.
            var outboardPivot = new GameObject(name).transform;

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(outboardPivot, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localPosition = new Vector3(0f, 0f, outboard * 0.5f);
            shaft.transform.localScale = new Vector3(ShaftDiameter, outboard * 0.5f, ShaftDiameter);
            shaft.GetComponent<Renderer>().material.color = shaftColour;

            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade";
            Destroy(blade.GetComponent<Collider>());
            blade.transform.SetParent(outboardPivot, false);
            blade.transform.localPosition = new Vector3(0f, 0f, outboard - BladeLength * 0.5f);
            // Squared: the broad face is upright and faces along the boat.
            blade.transform.localScale = new Vector3(0.02f, BladeHeight, BladeLength);
            blade.GetComponent<Renderer>().material.color = new Color(0.8f, 0.2f, 0.15f);

            return new Oar { Inboard = inboardShaft.transform, Outboard = outboardPivot };
        }

        void SetVisible(bool visible)
        {
            foreach (var oar in new[] { _left, _right })
            {
                if (oar == null) continue;
                SetActive(oar.Inboard, visible);
                SetActive(oar.Outboard, visible);
            }
            SetActive(_water, visible);
        }

        static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }
    }
}
