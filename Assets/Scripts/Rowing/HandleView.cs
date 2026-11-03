using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// A rowing handle between the rower's hands (#25). In a fully virtual scene
    /// the real handle is invisible, so this is how the rower sees that the app has their
    /// grip, and how they find the handle again after letting go.
    ///
    /// It sits at the midpoint of the two palms, the same point the stroke detector measures,
    /// so what the rower sees is exactly what is being counted. It is aligned along the line
    /// between the palms, but keeps a fixed length: a real handle does not stretch when the
    /// hands move apart.
    ///
    /// Through a tracking gap it holds the last good pose rather than following an absent
    /// hand, and after <see cref="staleSeconds"/> it dims to say the pose is old. It never
    /// shows before the first pose, so it cannot appear parked at the world origin.
    ///
    /// Placeholder geometry, built in code so there is no asset to import yet. One of two
    /// grip displays; <see cref="GripDisplay"/> chooses between this and the oars.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class HandleView : MonoBehaviour
    {
        [Tooltip("Handle length, metres. A rowing-machine handle is around 0.5 m.")]
        [SerializeField] float length = 0.5f;

        [Tooltip("Handle diameter, metres.")]
        [SerializeField] float diameter = 0.035f;

        [Tooltip("How long without a pose before the handle dims, seconds.")]
        [SerializeField] float staleSeconds = 0.5f;

        [SerializeField] Color liveColor = new Color(0.85f, 0.85f, 0.8f);
        [SerializeField] Color staleColor = new Color(0.3f, 0.3f, 0.3f);

        /// <summary>
        /// Below this palm separation, metres, the direction between the palms is mostly
        /// tracking noise, so the previous orientation is kept.
        /// </summary>
        const float MinPalmSeparation = 0.05f;

        RowingRig _rig;
        Transform _handle;
        Renderer _renderer;
        float _lastPoseTime = float.NegativeInfinity;
        bool _stale;

        /// <summary>World position of the handle's centre, for anything placed relative to it.</summary>
        public Vector3 Position => _handle != null ? _handle.position : Vector3.zero;

        /// <summary>False until the first pose, and while hidden.</summary>
        public bool Visible => _renderer != null && _renderer.enabled;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();

            var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "Handle";
            Destroy(handle.GetComponent<Collider>());
            _handle = handle.transform;
            // A unit cylinder is 2 units tall along its Y axis, hence the half length.
            _handle.localScale = new Vector3(diameter, length * 0.5f, diameter);

            // With scenery present, the handle belongs to its Near layer (#39).
            var scenery = FindFirstObjectByType<SceneryLayers>();
            if (scenery != null) _handle.SetParent(scenery.Near, true);

            _renderer = handle.GetComponent<Renderer>();
            _renderer.material.color = liveColor;
            _renderer.enabled = false;
        }

        // Hidden while switched off, e.g. by GripDisplay in oars mode. It reappears on the
        // next pose once enabled again.
        void OnDisable()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (_handle != null) Destroy(_handle.gameObject);
        }

        // LateUpdate so the rig has already taken this frame's pose in its Update.
        void LateUpdate()
        {
            if (_rig.IsTracking)
            {
                var sample = _rig.LastSample;
                var left = sample.LeftHand.ToVector3();
                var right = sample.RightHand.ToVector3();

                _handle.position = (left + right) * 0.5f;
                var across = right - left;
                if (across.magnitude >= MinPalmSeparation)
                    _handle.rotation = Quaternion.FromToRotation(Vector3.up, across.normalized);

                _lastPoseTime = Time.time;
                _renderer.enabled = true;
                SetStale(false);
                return;
            }

            // No pose this frame: hold where the grip was last seen.
            if (_renderer.enabled && Time.time - _lastPoseTime > staleSeconds) SetStale(true);
        }

        void SetStale(bool stale)
        {
            if (stale == _stale) return;
            _stale = stale;
            _renderer.material.color = stale ? staleColor : liveColor;
        }
    }
}
