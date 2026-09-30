using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The water (ADR-0008 Middle layer, TASK-0026): one large surface centred on the rower,
    /// with a ripple texture that scrolls as the boat travels. The geometry never moves, only
    /// the texture, so the surface is endless and its coordinates stay small.
    ///
    /// The ripples are what make speed visible close to the boat, where the shoreline is too
    /// far away to show it. The surface fades into the fog, and so into the horizon.
    ///
    /// Its height follows <see cref="SceneryLayers.WaterHeight"/>, which the oars keep at
    /// their oarlock height less the freeboard, so the blades dip into this surface.
    /// Placeholder look: unlit, one draw call, no reflections.
    /// </summary>
    [RequireComponent(typeof(SceneryLayers))]
    public sealed class WaterSurface : MonoBehaviour
    {
        [Tooltip("Metres covered by one repeat of the ripple texture.")]
        [SerializeField] float tileSize = 4f;

        [Tooltip("Half-width of the surface, metres. Beyond the fog, inside the sky dome.")]
        [SerializeField] float halfSize = 880f;

        [SerializeField] Color deepColour = new Color(0.1f, 0.24f, 0.34f);
        [SerializeField] Color lightColour = new Color(0.32f, 0.47f, 0.56f);

        const int TextureSize = 128;

        SceneryLayers _layers;
        Transform _surface;
        Material _material;

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            _material = _layers.CreateMaterial("Water");
            if (_material == null) { enabled = false; return; }
            _material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            _material.SetTexture("_BaseMap", BuildRipples(deepColour, lightColour));
            _material.SetTextureScale("_BaseMap", Vector2.one);

            var surface = new GameObject("Water");
            surface.transform.SetParent(_layers.Middle, false);
            surface.AddComponent<MeshFilter>().sharedMesh = BuildQuad(halfSize, tileSize);
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _surface = surface.transform;
        }

        void LateUpdate()
        {
            if (_surface == null) return;

            // Local +Z is the rower's facing, so the texture's V runs along the course.
            var origin = _layers.CourseOrigin;
            _surface.SetPositionAndRotation(
                new Vector3(origin.x, _layers.WaterHeight, origin.z),
                Quaternion.LookRotation(_layers.RowerForward, Vector3.up));

            // Fixed water drifts forward in the rower's view as the boat travels, so the
            // texture slides toward +V. The modulo happens in double so an hour's distance
            // does not cost the offset its precision.
            var tiles = (float)(_layers.Distance / tileSize % 1d);
            _material.SetTextureOffset("_BaseMap", new Vector2(0f, -tiles));
        }

        /// <summary>A square in the XZ plane, UVs in tiles so the texture repeats every tileSize.</summary>
        static Mesh BuildQuad(float half, float tile)
        {
            var uv = half / tile;
            return new Mesh
            {
                name = "Water",
                vertices = new[]
                {
                    new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
                    new Vector3(half, 0f, half), new Vector3(half, 0f, -half),
                },
                uv = new[] { new Vector2(-uv, -uv), new Vector2(-uv, uv), new Vector2(uv, uv), new Vector2(uv, -uv) },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                bounds = new Bounds(Vector3.zero, new Vector3(half * 2f, 1f, half * 2f)),
            };
        }

        /// <summary>
        /// A tileable ripple pattern: a few sine waves at whole-number frequencies (so the
        /// edges meet), with longer crests across the course than along it.
        /// </summary>
        static Texture2D BuildRipples(Color deep, Color light)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true)
            {
                name = "Ripples",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };

            var pixels = new Color[TextureSize * TextureSize];
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var u = 2f * Mathf.PI * x / TextureSize;
                    var v = 2f * Mathf.PI * y / TextureSize;
                    var h = 0.45f * Mathf.Sin(3f * v + Mathf.Sin(u))
                          + 0.25f * Mathf.Sin(5f * v + 2f * u)
                          + 0.2f * Mathf.Sin(7f * v - 3f * u + 1.3f)
                          + 0.1f * Mathf.Sin(11f * v + 5f * u);
                    var t = Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * h);
                    pixels[y * TextureSize + x] = Color.Lerp(deep, light, t);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
