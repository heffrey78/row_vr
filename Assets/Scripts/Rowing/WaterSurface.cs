using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The water (#39 Middle layer, #72): one large surface centred on the rower,
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
        [SerializeField] float tileSize = 7f;

        [Tooltip("Half-width of the surface, metres. Beyond the fog, inside the sky dome.")]
        [SerializeField] float halfSize = 880f;

        [SerializeField] Color deepColour = new Color(0.1f, 0.24f, 0.34f);
        [SerializeField] Color lightColour = new Color(0.32f, 0.47f, 0.56f);


        SceneryLayers _layers;
        Transform _surface;
        Material _material;

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            _material = _layers.CreateMaterial("Water");
            if (_material == null) { enabled = false; return; }
            _material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            var palette = _layers.Palette;
            _material.SetTexture("_BaseMap", palette != null
                ? BuildRipples(palette.waterDeep, palette.waterLight, palette.waterDash)
                : BuildRipples(deepColour, lightColour, Color.white));
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
            var tiles = (float)(_layers.ViewDistance / tileSize % 1d);
            // Drift slides the water sideways with the rest of the scenery.
            var across = _layers.LateralOffset / tileSize % 1f;
            _material.SetTextureOffset("_BaseMap", new Vector2(across, -tiles));
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
        /// Illustrated water (#42): flat colour in two soft bands, with sparse drawn
        /// dashes lying across the course, like a pen sketch of ripples. Tileable: every
        /// dash wraps round the texture edges, and the bands use whole-number waves.
        /// </summary>
        static Texture2D BuildRipples(Color deep, Color light, Color dash)
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Ripples",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                // Low on purpose: high anisotropy keeps the dashes sharp at grazing angles,
                // where they stretch into streaks that read as rain.
                anisoLevel = 2,
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                var v = 2f * Mathf.PI * y / size;
                var band = Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(2f * v + 0.6f * Mathf.Sin(v)));
                var row = Color.Lerp(deep, light, 0.25f + 0.35f * band);
                for (var x = 0; x < size; x++) pixels[y * size + x] = row;
            }

            // Dashes: thin, tapered, lying across the course (along U), a few pixels tall.
            var random = new System.Random(5);
            for (var d = 0; d < 22; d++)
            {
                var cx = (float)random.NextDouble() * size;
                var cy = (float)random.NextDouble() * size;
                var halfLength = 8f + (float)random.NextDouble() * 22f;
                var halfHeight = 1.1f + (float)random.NextDouble() * 0.9f;
                var strength = 0.55f + (float)random.NextDouble() * 0.45f;
                for (var dy = -3; dy <= 3; dy++)
                {
                    for (var dx = -(int)halfLength - 1; dx <= (int)halfLength + 1; dx++)
                    {
                        var u = dx / halfLength;
                        if (Mathf.Abs(u) > 1f) continue;
                        // Tapered at both ends, soft at the top and bottom edges.
                        var thickness = halfHeight * Mathf.Sqrt(1f - u * u);
                        var cover = Mathf.Clamp01(thickness - Mathf.Abs(dy + (cy - Mathf.Floor(cy))) + 0.5f) * strength;
                        if (cover <= 0f) continue;
                        var px = ((int)(cx + dx) % size + size) % size;
                        var py = ((int)cy + dy % size + size) % size;
                        var i = py * size + px;
                        pixels[i] = Color.Lerp(pixels[i], dash, cover);
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
