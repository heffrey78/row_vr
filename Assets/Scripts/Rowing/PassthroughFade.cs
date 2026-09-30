using RowVR.Stroke;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace RowVR.Rowing
{
    /// <summary>
    /// Shows the room when the rower lets go (REQ-0005, ADR-0014). PassthroughPolicy
    /// (engine-free, tested on the recordings) decides when; this does the showing.
    ///
    /// Passthrough itself is com.unity.xr.meta-openxr's OpenXR camera feature, driven by AR
    /// Foundation's ARCameraManager on the XR camera. It is enabled only while the fade is
    /// above zero, so the passthrough cameras are not running while the rower rows. The fade
    /// is a quad just in front of the camera on RowVR/PassthroughFade, which scales the eye
    /// buffer's colour and alpha down together; the compositor fills the gap with the room.
    ///
    /// The cards (metrics, rapids, menu) draw after the fade (render queue 3550 and up), so
    /// they stay readable over the room: letting go is also how the rower reaches the menu.
    /// Headless previews have no passthrough, so there the fade reads as black.
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

        /// <summary>0 the scene, 1 the room.</summary>
        public float Fade => _policy.Fade;

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

        void LateUpdate()
        {
            var tracking = _rig.IsTracking;
            _policy.Update(Time.timeAsDouble, tracking, tracking ? _rig.State : default, _rig.HandsOff);

            var fade = _policy.Fade;
            var on = fade > 0.001f;
            if (_cameraManager.enabled != on) _cameraManager.enabled = on;
            if (_quad.enabled != on) _quad.enabled = on;
            _material.SetFloat("_Fade", fade);
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
