using RowVR.Stroke;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Services;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace RowVR.Rowing
{
    /// <summary>
    /// Shows the room when the rower lets go (#28, #45). PassthroughPolicy
    /// (engine-free, tested on the recordings) decides when; this does the showing.
    ///
    /// Passthrough itself is com.unity.xr.meta-openxr's OpenXR camera feature, driven by AR
    /// Foundation's ARCameraManager on the XR camera. It is enabled only while the fade is
    /// above zero, so the passthrough cameras are not running while the rower rows. The fade
    /// is a quad just in front of the camera on RowVR/PassthroughFade, which scales the eye
    /// buffer's colour and alpha down together; the compositor fills the gap with the room.
    ///
    /// The cards (metrics, rapids, menu) and the virtual hands draw after the fade (render queue
    /// 3550 and up), so they stay visible over the room: letting go is also how the rower
    /// reaches the menu, and they need to see where their fingers are.
    ///
    /// It never fades to black. The fade only shows once the passthrough composition layer
    /// actually exists. The first headset test found passthrough on but no layer submitted,
    /// because the Composition Layers feature was off, and the rower was left in the dark. If
    /// the layer does not appear within a couple of seconds, it logs why and stays in the scene.
    /// The wrist menu's "back to the scene" returns to the scene until the next stroke
    /// (<see cref="Dismiss"/>, #46).
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class PassthroughFade : MonoBehaviour
    {
        [Tooltip("RowVR/PassthroughFade. Set by scene setup; a scene reference keeps it in the build.")]
        [SerializeField] Shader fadeShader;

        /// <summary>Render queue the fade draws at; anything meant to stay visible goes after it.</summary>
        public const int FadeQueue = 3500;

        /// <summary>Render queue for cards that stay visible over the room.</summary>
        public const int OverlayQueue = 3550;

        RowingRig _rig;
        Camera _camera;
        ARCameraManager _cameraManager;
        Material _material;
        Renderer _quad;
        readonly PassthroughPolicy _policy = new PassthroughPolicy();
        float _shown;
        float _waitingFor;
        bool _warned;

        /// <summary>0 the scene, 1 the room, as actually shown (never without a passthrough layer).</summary>
        public float Fade => _shown;

        /// <summary>Back to the scene until the next stroke.</summary>
        public void Dismiss() => _policy.Dismiss();

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _camera = Camera.main;
            if (fadeShader == null || _camera == null)
            {
                Debug.LogWarning("[RowVR] PassthroughFade has no shader or camera; passthrough stays off.");
                enabled = false;
                return;
            }

            // AR Foundation needs a session running for its camera subsystem.
            if (FindFirstObjectByType<ARSession>() == null) new GameObject("AR Session").AddComponent<ARSession>();

            _cameraManager = _camera.GetComponent<ARCameraManager>();
            if (_cameraManager == null) _cameraManager = _camera.gameObject.AddComponent<ARCameraManager>();
            _cameraManager.enabled = false;

            _material = new Material(fadeShader) { name = "Passthrough Fade", renderQueue = FadeQueue };
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Passthrough Fade";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_camera.transform, false);
            // Close in front of both eyes and wide enough to cover the whole field of view.
            quad.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(_camera.nearClipPlane * 2f, 0.06f));
            quad.transform.localScale = new Vector3(2f, 2f, 1f);
            _quad = quad.GetComponent<Renderer>();
            _quad.sharedMaterial = _material;
            _quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // The shader draws it full screen; huge bounds keep it from ever being culled.
            _quad.localBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            _quad.enabled = false;
        }

        void Start()
        {
            // The virtual hands stay visible over the room, so the rower can see where their
            // fingers are to poke the menu. Instanced materials: these renderers never swap them.
            var hands = GameObject.Find("Hand Visualizer");
            if (hands == null) return;
            foreach (var renderer in hands.GetComponentsInChildren<Renderer>(true))
                foreach (var m in renderer.materials)
                    KeepVisible(m);
        }

        void LateUpdate()
        {
            var tracking = _rig.IsTracking;
            _policy.Update(Time.timeAsDouble, tracking, tracking ? _rig.State : default, _rig.HandsOff);

            // Run passthrough whenever the room is wanted or still showing; show the fade only
            // over a passthrough layer that actually exists.
            var wantCamera = _policy.Wanted || _shown > 0.001f;
            if (_cameraManager.enabled != wantCamera) _cameraManager.enabled = wantCamera;

            var ready = PassthroughLayerExists();
            var target = ready ? _policy.Fade : 0f;
            _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime / 0.4f);

            _waitingFor = _policy.Wanted && !ready ? _waitingFor + Time.deltaTime : 0f;
            if (_waitingFor > 2f && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[RowVR] passthrough wanted, but no passthrough layer after 2 s " +
                                 $"(layer manager {(CompositionLayerManager.Instance != null ? "present" : "missing")}, " +
                                 $"camera subsystem {(_cameraManager.subsystem != null && _cameraManager.subsystem.running ? "running" : "not running")}). " +
                                 "Staying in the scene.");
            }

            var on = _shown > 0.001f;
            if (_quad.enabled != on) _quad.enabled = on;
            _material.SetFloat("_Fade", _shown);
        }

        /// <summary>
        /// Whether a passthrough composition layer is live: the layer manager exists (it needs the
        /// Composition Layers OpenXR feature) and the camera subsystem has created its layer.
        /// </summary>
        bool PassthroughLayerExists()
        {
            if (CompositionLayerManager.Instance == null) return false;
            if (_cameraManager.subsystem == null || !_cameraManager.subsystem.running) return false;
            var layer = GameObject.Find("Passthrough");
            return layer != null && layer.activeInHierarchy && layer.GetComponent<CompositionLayer>() != null;
        }

        /// <summary>
        /// Moves every material under a card to draw after the fade, so it stays visible over
        /// the room. Called by the cards once they are built.
        /// </summary>
        public static void KeepVisible(Transform card)
        {
            // Shared materials on purpose: each card makes its own, and the menu swaps its button
            // materials every frame, which would drop per-renderer copies.
            foreach (var renderer in card.GetComponentsInChildren<Renderer>(true))
                foreach (var m in renderer.sharedMaterials)
                    if (m != null) KeepVisible(m);
        }

        /// <summary>Moves one material to draw after the fade; transparent ones after the opaque.</summary>
        public static void KeepVisible(Material material)
        {
            if (material.renderQueue < OverlayQueue)
                material.renderQueue = material.renderQueue >= 3000 ? OverlayQueue + 50 : OverlayQueue;
        }
    }
}
