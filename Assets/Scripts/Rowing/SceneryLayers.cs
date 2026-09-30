using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The scenery's four layers and the course they sit on (ADR-0008, REQ-0004-FUNC).
    ///
    ///   Near    the boat, the oars and the handle; travels with the rower
    ///   Middle  the water and what floats on it, within about 50 m
    ///   Far     the shoreline and landmarks, out to the fog
    ///   Sky     the sky dome, effectively at infinity
    ///
    /// Each layer has a mode. Hidden layers are not drawn, and once passthrough is enabled
    /// (REQ-0005-FUNC) the room shows where they were, because the camera clears to
    /// transparent black and the sky is geometry, not the camera's skybox.
    ///
    /// The rower and the XR Origin never move. The boat's travel is <see cref="Distance"/>,
    /// and each scenery component places its own pieces from it with CourseWrap. The layer
    /// roots themselves stay at the world origin: the hand positions the stroke detector
    /// reads never pass through anything that moves.
    ///
    /// Until the boat speed model (REQ-0003-FUNC) exists, a stand-in boat travels at
    /// <see cref="debugSpeed"/> while the rower is rowing and glides to a stop when they are
    /// not. Scenery that moved while the rower sat still would be motion with no effort
    /// behind it, the classic cause of VR sickness.
    /// </summary>
    public sealed class SceneryLayers : MonoBehaviour
    {
        public enum LayerMode
        {
            Virtual,
            Hidden,
        }

        [Header("Layers")]
        [SerializeField] LayerMode near = LayerMode.Virtual;
        [SerializeField] LayerMode middle = LayerMode.Virtual;
        [SerializeField] LayerMode far = LayerMode.Virtual;
        [SerializeField] LayerMode sky = LayerMode.Virtual;

        [Header("Travel")]
        [Tooltip("Stand-in boat speed while rowing, m/s, until the speed model drives it. " +
                 "3.5 m/s is a 2:23 split. 0 keeps the scenery still.")]
        [SerializeField] float debugSpeed = 3.5f;

        [Tooltip("Stand-in boat: speeding up while rowing, and gliding down when not, m/s².")]
        [SerializeField] float debugAcceleration = 1.2f;
        [SerializeField] float debugDeceleration = 0.35f;

        [Tooltip("Off once the speed model sets Speed itself.")]
        [SerializeField] bool useDebugSpeed = true;

        [Header("Rendering")]
        [Tooltip("RowVR/Unlit. Set by RowVR > Add Scenery; a scene reference keeps it in the build.")]
        [SerializeField] Shader unlitShader;

        [Tooltip("RowVR/Illustrated (ADR-0011). Set by RowVR > Add Scenery, for the same reason.")]
        [SerializeField] Shader illustratedShader;

        [Tooltip("Where the water meets the sky; also the fog colour.")]
        [SerializeField] Color horizonColour = new Color(0.72f, 0.8f, 0.86f);

        Transform _near, _middle, _far, _sky;
        RowingRig _rig;
        Camera _camera;
        bool _hasCourse;

        /// <summary>Metres the boat has travelled. Scenery positions itself from this.</summary>
        public double Distance { get; private set; }

        /// <summary>Boat speed, m/s. BoatMotion sets it from the boat model; without it, the stand-in boat.</summary>
        public float Speed { get; set; }

        /// <summary>Whether the stand-in boat drives Speed. BoatMotion turns it off.</summary>
        public bool UseDebugSpeed
        {
            get => useDebugSpeed;
            set => useDebugSpeed = value;
        }

        /// <summary>
        /// Level unit vector the rower faces, toward the stern: the boat's heading from
        /// BoatFrame (the machine's axis, learned from the hands), never the head's facing.
        /// </summary>
        public Vector3 RowerForward { get; private set; } = Vector3.forward;

        /// <summary>Where the course's centreline passes the rower, on the floor.</summary>
        public Vector3 CourseOrigin { get; private set; }

        /// <summary>Height of the water surface. OarsView keeps it matched to its oarlocks.</summary>
        public float WaterHeight { get; set; } = 0.55f;

        public Color HorizonColour => Palette != null ? Palette.skyHorizon : horizonColour;

        /// <summary>The illustrated shader, or null in a scene set up before ADR-0011.</summary>
        public Shader IllustratedShader => illustratedShader;

        ScenePalette _palette;

        /// <summary>The scene's colours, if a ScenePalette sits beside this component.</summary>
        public ScenePalette Palette => _palette != null ? _palette : (_palette = GetComponent<ScenePalette>());

        public Transform Near => Root(ref _near, "Near");
        public Transform Middle => Root(ref _middle, "Middle");
        public Transform Far => Root(ref _far, "Far");
        public Transform Sky => Root(ref _sky, "Sky");

        /// <summary>The camera, for anything that follows the head.</summary>
        public Transform Head => _camera != null ? _camera.transform : null;

        /// <summary>A new material on the scenery shader, or null if it is missing.</summary>
        public Material CreateMaterial(string name)
        {
            if (unlitShader == null) return null;
            return new Material(unlitShader) { name = name };
        }

        void Awake()
        {
            _rig = FindFirstObjectByType<RowingRig>();
            _camera = Camera.main;

            if (unlitShader == null)
                Debug.LogError("[RowVR] SceneryLayers has no shader. Run RowVR > Add Scenery in the Editor.");

            EnsureTransparentClear();
            ApplyModes();
        }

        void Update()
        {
            ApplyModes();
            CaptureCourse();
            if (useDebugSpeed) UpdateDebugSpeed();
            Distance += Speed * Time.deltaTime;
        }

        void UpdateDebugSpeed()
        {
            var rowing = _rig != null && _rig.IsTracking && _rig.State.Phase != RowVR.Stroke.StrokePhase.Unknown;
            var target = rowing ? debugSpeed : 0f;
            var rate = target > Speed ? debugAcceleration : debugDeceleration;
            Speed = Mathf.MoveTowards(Speed, target, rate * Time.deltaTime);
        }

        /// <summary>Takes the rower's current facing as the course direction.</summary>
        public void Recentre()
        {
            _hasCourse = false;
            CaptureCourse();
        }

        BoatFrame _frame;

        void CaptureCourse()
        {
            // The course runs along the boat, and the boat's heading comes from the machine's
            // axis (BoatFrame, learned from the hands), never from where the rower looks.
            if (_frame == null && _rig != null) _frame = _rig.GetComponent<BoatFrame>();
            if (_frame != null && _frame.HasFrame)
            {
                RowerForward = _frame.Forward;
                if (!_hasCourse)
                {
                    var centre = _frame.Centre;
                    CourseOrigin = new Vector3(centre.x, 0f, centre.z);
                    _hasCourse = true;
                }
                return;
            }
            if (_hasCourse) return;

            // No boat frame (a scene without one): take the rower's facing once, when their
            // hands are first tracked, and keep it.
            var ready = _rig != null && _rig.IsTracking;
            var forward = ready ? _rig.LastSample.HeadForward.ToVector3()
                        : _camera != null ? _camera.transform.forward : Vector3.forward;
            var position = ready ? _rig.LastSample.HeadPosition.ToVector3()
                         : _camera != null ? _camera.transform.position : Vector3.zero;

            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-6f) RowerForward = forward.normalized;
            CourseOrigin = new Vector3(position.x, 0f, position.z);
            _hasCourse = ready;
        }

        void ApplyModes()
        {
            SetActive(Near, near);
            SetActive(Middle, middle);
            SetActive(Far, far);
            SetActive(Sky, sky);
        }

        static void SetActive(Transform root, LayerMode mode)
        {
            var visible = mode == LayerMode.Virtual;
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        /// <summary>
        /// A skybox clear paints every pixel opaque, and passthrough only shows where the app
        /// leaves alpha at 0. RowVR > Add Scenery saves the right clear in the scene; this
        /// catches it being changed back.
        /// </summary>
        void EnsureTransparentClear()
        {
            if (_camera == null) return;
            var transparent = _camera.clearFlags == CameraClearFlags.SolidColor && _camera.backgroundColor.a == 0f;
            if (transparent) return;

            Debug.LogWarning("[RowVR] Camera clear was " + _camera.clearFlags + "; scenery needs solid " +
                             "(0,0,0,0) so passthrough can show through (ADR-0008). Fixed for this run.");
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        Transform Root(ref Transform root, string name)
        {
            if (root != null) return root;
            root = new GameObject(name).transform;
            root.SetParent(transform, false);
            return root;
        }
    }
}
