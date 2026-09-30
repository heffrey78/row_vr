using System.Collections.Generic;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Builds materials on RowVR/Illustrated (ADR-0011) and the paper grain they share, and
    /// bakes the smoothed normals its ink pass inflates along.
    ///
    /// The shader itself comes from SceneryLayers, which holds it as a scene reference so a
    /// device build keeps it.
    /// </summary>
    public static class IllustratedMaterials
    {
        static Texture2D _grain;

        /// <summary>
        /// A material in the illustrated style, or null if the scene has no illustrated shader
        /// (callers keep their placeholder look).
        /// </summary>
        public static Material Create(SceneryLayers layers, string name, Color colour, bool ink = true,
                                      float inkWidth = 0.006f, bool vertexColour = false)
        {
            if (layers == null || layers.IllustratedShader == null) return null;
            var palette = layers.Palette;
            var material = new Material(layers.IllustratedShader) { name = name };
            material.SetColor("_BaseColor", colour);
            if (palette != null)
            {
                material.SetColor("_ShadeColor", palette.shadeTint);
                material.SetColor("_InkColor", palette.ink);
            }
            material.SetFloat("_InkWidth", ink ? inkWidth : 0f);
            material.SetFloat("_UseVertexColor", vertexColour ? 1f : 0f);
            material.SetTexture("_GrainTex", Grain);
            // The ink pass still runs at zero width; turn it off entirely when not wanted.
            material.SetShaderPassEnabled("SRPDefaultUnlit", ink);
            return material;
        }

        /// <summary>
        /// Paper grain: soft blotches plus fine fibres, tileable, mipmapped. Generated once with
        /// a fixed seed, so the paper is the same every run.
        /// </summary>
        public static Texture2D Grain
        {
            get
            {
                if (_grain != null) return _grain;
                const int size = 128;
                _grain = new Texture2D(size, size, TextureFormat.R8, true, true)
                {
                    name = "Paper Grain",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                var random = new System.Random(7);
                var coarse = new float[16 * 16];
                for (var i = 0; i < coarse.Length; i++) coarse[i] = (float)random.NextDouble();
                var pixels = new Color[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        // Tileable value noise for blotches, plus per-pixel fibre speckle.
                        var fx = x / 8f; var fy = y / 8f;
                        int x0 = (int)fx % 16, y0 = (int)fy % 16, x1 = (x0 + 1) % 16, y1 = (y0 + 1) % 16;
                        float tx = Smooth(fx - Mathf.Floor(fx)), ty = Smooth(fy - Mathf.Floor(fy));
                        var blotch = Mathf.Lerp(Mathf.Lerp(coarse[y0 * 16 + x0], coarse[y0 * 16 + x1], tx),
                                                Mathf.Lerp(coarse[y1 * 16 + x0], coarse[y1 * 16 + x1], tx), ty);
                        var fibre = (float)random.NextDouble();
                        var v = 0.5f + (blotch - 0.5f) * 0.55f + (fibre - 0.5f) * 0.35f;
                        pixels[y * size + x] = new Color(v, v, v, 1f);
                    }
                }
                _grain.SetPixels(pixels);
                _grain.Apply(true, true);
                return _grain;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// Averages each vertex's normal with every other vertex at the same position and
        /// stores the result in UV channel 3, which the ink pass inflates along. Without it,
        /// flat-shaded shapes split open at their hard edges.
        /// </summary>
        public static void BakeSmoothNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            var sums = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
            for (var i = 0; i < vertices.Length; i++)
            {
                var k = Key(vertices[i]);
                sums[k] = (sums.TryGetValue(k, out var s) ? s : Vector3.zero) + normals[i];
            }
            var smooth = new List<Vector3>(vertices.Length);
            for (var i = 0; i < vertices.Length; i++) smooth.Add(sums[Key(vertices[i])].normalized);
            mesh.SetUVs(3, smooth);
        }

        /// <summary>A copy of a primitive's mesh with smooth normals baked, for an inked primitive.</summary>
        public static Mesh InkedCopy(Mesh shared)
        {
            var copy = Object.Instantiate(shared);
            copy.name = shared.name + " (inked)";
            BakeSmoothNormals(copy);
            return copy;
        }

        /// <summary>
        /// Swaps a primitive's material for an illustrated one, and its mesh for an inked copy.
        /// No-op without an illustrated shader.
        /// </summary>
        public static void Apply(SceneryLayers layers, GameObject primitive, Color colour, bool ink = true, float inkWidth = 0.006f)
        {
            var material = Create(layers, primitive.name, colour, ink, inkWidth);
            if (material == null) return;
            var renderer = primitive.GetComponent<MeshRenderer>();
            var filter = primitive.GetComponent<MeshFilter>();
            if (renderer != null) renderer.sharedMaterial = material;
            if (filter != null && ink) filter.sharedMesh = InkedCopy(filter.sharedMesh);
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
