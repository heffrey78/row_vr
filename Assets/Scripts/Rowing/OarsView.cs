using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Two sculling oars held in the rower's hands (#25), turning in swivel gates on
    /// the boat's riggers.
    ///
    /// While a stroke is being tracked, each oar's handle end is at the rower's hand: left
    /// hand, left oar. The oar swings about its oarlock along the line from the hand through
    /// the oarlock, so the sweep is the rower's own motion. Blade depth comes from
    /// <see cref="OarKinematics"/>, which is tested against the rowing-machine recordings: on
    /// the machine the hands barely rise or fall, so hand height cannot drive the blade.
    ///
    /// A straight oar cannot pass through a pivot fixed in height, meet the hand and put the
    /// blade at the right depth all at once. So the oar rides up and down in a tall gate, as
    /// a real oar has a little play in its gate. It stays one straight line from the hand to
    /// the blade, and where that line would leave the gate, the blade gives way instead.
    ///
    /// When no stroke is tracked (before the first, after a stop, after letting go) the oars
    /// leave the hands and ease to square with the blades out, so waving the hands about does
    /// not swing the oars.
    ///
    /// The oarlocks, water height and boat direction come from <see cref="BoatFrame"/>, shared
    /// with the hull. "Left" and "right" are from the rower's seat. The geometry is built in
    /// code, illustrated (#42) when the scene provides the shader.
    /// </summary>
    [RequireComponent(typeof(RowingRig), typeof(BoatFrame))]
    public sealed class OarsView : MonoBehaviour
    {
        [Header("Oars (metres)")]
        [Tooltip("Oar length from the oarlock to the handle end, used when the oars are at rest.")]
        [SerializeField] float inboard = 0.88f;

        [Tooltip("Oar length from the oarlock to the blade tip.")]
        [SerializeField] float outboard = 2.0f;

        [Tooltip("How far the blade's centre sits below the surface when fully buried.")]
        [SerializeField] float buriedDepth = 0.08f;

        [Tooltip("How far the blade's centre clears the surface when out of the water.")]
        [SerializeField] float clearance = 0.12f;

        [Tooltip("Half the gate's opening height: how far the oar may ride up or down at the oarlock.")]
        [SerializeField] float gatePlay = 0.06f;

        [Header("Motion")]
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
            public Transform Gate;      // swivels with the oar, fixed at the oarlock
            public float Sweep;         // drawn sweep, degrees, eased
        }

        RowingRig _rig;
        BoatFrame _frame;
        SceneryLayers _scenery;
        readonly OarKinematics _kinematics = new OarKinematics();
        Oar _left, _right;
        Transform _water;

        /// <summary>The kinematic pose this frame; supplies blade depth and the rest state.</summary>
        public OarPose Pose { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            // RequireComponent is not applied to a component already saved in a scene, and
            // scenes from before BoatFrame have OarsView without it.
            _frame = GetComponent<BoatFrame>();
            if (_frame == null) _frame = gameObject.AddComponent<BoatFrame>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            _left = CreateOar("Left Oar");
            _right = CreateOar("Right Oar");

            // With scenery present, the oars belong to its Near layer and the scenery draws the
            // water. Without it, keep a placeholder so blade depth can still be judged.
            if (_scenery != null)
            {
                foreach (var oar in new[] { _left, _right })
                {
                    oar.Inboard.SetParent(_scenery.Near, true);
                    oar.Outboard.SetParent(_scenery.Near, true);
                    oar.Gate.SetParent(_scenery.Near, true);
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
                foreach (var t in new[] { oar.Inboard, oar.Outboard, oar.Gate })
                    if (t != null) Destroy(t.gameObject);
            }
            if (_water != null) Destroy(_water.gameObject);
        }

        // LateUpdate so the rig has taken this frame's pose (Update) and BoatFrame has followed
        // it (LateUpdate, earlier execution order).
        void LateUpdate()
        {
            // No pose, no new state: hold the oars where they are, as the handle does.
            if (!_rig.IsTracking || !_frame.HasFrame) return;

            var sample = _rig.LastSample;
            Pose = _kinematics.Update(Time.timeAsDouble, _rig.State);

            var waterY = _frame.WaterHeight;
            var bladeY = Mathf.Lerp(waterY + clearance, waterY - buriedDepth, Pose.BladeDepth);

            PlaceOar(_left, -1, sample.LeftHand.ToVector3(), bladeY);
            PlaceOar(_right, 1, sample.RightHand.ToVector3(), bladeY);

            if (_water != null) _water.position = new Vector3(_frame.Centre.x, waterY, _frame.Centre.z);
            SetVisible(true);
        }

        void PlaceOar(Oar oar, int side, Vector3 hand, float bladeY)
        {
            var outward = _frame.Right * side;
            var forward = _frame.Forward;
            var oarlock = _frame.Oarlock(side);

            // Held: sweep is the line from the hand through the oarlock. At rest: the kinematic
            // sweep, which eases to square. Either way the drawn sweep is rate limited, which is
            // what smooths picking the oars up and putting them down.
            var held = !Pose.Resting;
            var target = Pose.SweepDegrees;
            if (held)
            {
                var toBlade = oarlock - hand;
                toBlade.y = 0f;
                if (toBlade.sqrMagnitude > 1e-4f)
                    target = Mathf.Atan2(-Vector3.Dot(toBlade, forward), Vector3.Dot(toBlade, outward)) * Mathf.Rad2Deg;
            }
            oar.Sweep = Mathf.MoveTowards(oar.Sweep, target, maxSweepDegreesPerSecond * Time.deltaTime);

            // Horizontal direction from the oarlock to the blade. At the catch (positive sweep)
            // the blade reaches back, away from the rower's forward.
            var sweep = oar.Sweep * Mathf.Deg2Rad;
            var flat = outward * Mathf.Cos(sweep) - forward * Mathf.Sin(sweep);

            var reach = outboard - BladeLength * 0.5f;
            var drop = Mathf.Clamp(bladeY - oarlock.y, -reach * 0.9f, reach * 0.9f);
            var along = (flat * Mathf.Sqrt(reach * reach - drop * drop) + Vector3.up * drop).normalized;
            var pivot = oarlock;

            // The handle end is in the hand only while it is the hand's line that set the
            // sweep; while easing to or from rest it stays on the oar's own line.
            var inHand = held && Mathf.Abs(oar.Sweep - target) < 0.5f;
            if (inHand)
            {
                // One straight line from the hand to where the blade should be, crossing the
                // oarlock's upright where the gate is. Outside the gate's play, keep the pivot
                // at the edge of the gate and let the blade depth give way instead.
                var bladeCentre = oarlock + along * reach;
                var line = bladeCentre - hand;
                var inboardFlat = Vector3.ProjectOnPlane(oarlock - hand, Vector3.up).magnitude;
                var totalFlat = Vector3.ProjectOnPlane(line, Vector3.up).magnitude;
                if (totalFlat > 1e-3f)
                {
                    pivot = hand + line * Mathf.Clamp01(inboardFlat / totalFlat);
                    pivot.y = Mathf.Clamp(pivot.y, oarlock.y - gatePlay, oarlock.y + gatePlay);
                    along = (pivot - hand).normalized;
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

            // The gate stays at the oarlock and swivels to face along the oar.
            oar.Gate.SetPositionAndRotation(oarlock, Quaternion.LookRotation(flat, Vector3.up));
        }

        Oar CreateOar(string name)
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var shaftColour = palette != null ? palette.oarShaft : new Color(0.9f, 0.9f, 0.85f);
            var bladeColour = palette != null ? palette.blade : new Color(0.8f, 0.2f, 0.15f);
            var metal = palette != null ? palette.rigger : new Color(0.35f, 0.37f, 0.4f);

            var inboardShaft = Part(PrimitiveType.Cylinder, name + " Inboard", null, shaftColour, 0.004f);

            // Outboard: a pivot at the oarlock with its local +Z toward the blade.
            var outboardPivot = new GameObject(name).transform;

            var shaft = Part(PrimitiveType.Cylinder, "Shaft", outboardPivot, shaftColour, 0.004f);
            shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.localPosition = new Vector3(0f, 0f, outboard * 0.5f);
            shaft.localScale = new Vector3(ShaftDiameter, outboard * 0.5f, ShaftDiameter);

            var blade = Part(PrimitiveType.Cube, "Blade", outboardPivot, bladeColour, 0.006f);
            blade.localPosition = new Vector3(0f, 0f, outboard - BladeLength * 0.5f);
            // Squared: the broad face is upright and faces along the boat.
            blade.localScale = new Vector3(0.02f, BladeHeight, BladeLength);

            // The gate: a tall U the oar rides in, open at the top, facing along the oar
            // (local +Z). Its height is the play the oar is allowed at the oarlock.
            var gate = new GameObject(name + " Gate").transform;
            var height = gatePlay * 2f + 0.03f;
            foreach (var x in new[] { -0.035f, 0.035f })
            {
                var post = Part(PrimitiveType.Cube, "Post", gate, metal, 0.003f);
                post.localPosition = new Vector3(x, 0f, 0f);
                post.localScale = new Vector3(0.012f, height, 0.04f);
            }
            var sill = Part(PrimitiveType.Cube, "Sill", gate, metal, 0.003f);
            sill.localPosition = new Vector3(0f, -height * 0.5f, 0f);
            sill.localScale = new Vector3(0.082f, 0.012f, 0.04f);
            var keeper = Part(PrimitiveType.Cube, "Keeper", gate, metal, 0.003f);
            keeper.localPosition = new Vector3(0f, height * 0.5f, 0f);
            keeper.localScale = new Vector3(0.082f, 0.012f, 0.04f);

            return new Oar { Inboard = inboardShaft, Outboard = outboardPivot, Gate = gate };
        }

        Transform Part(PrimitiveType type, string name, Transform parent, Color colour, float inkWidth)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            if (parent != null) part.transform.SetParent(parent, false);
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, part, colour, true, inkWidth);
            else
                part.GetComponent<Renderer>().material.color = colour;
            return part.transform;
        }

        void SetVisible(bool visible)
        {
            foreach (var oar in new[] { _left, _right })
            {
                if (oar == null) continue;
                SetActive(oar.Inboard, visible);
                SetActive(oar.Outboard, visible);
                SetActive(oar.Gate, visible);
            }
            SetActive(_water, visible);
        }

        static void SetActive(Transform t, bool active)
        {
            if (t != null && t.gameObject.activeSelf != active) t.gameObject.SetActive(active);
        }
    }
}
