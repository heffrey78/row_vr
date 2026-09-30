using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The course (ADR-0008, illustrated per ADR-0011): banks either side with rounded trees
    /// and conifers, reeds at the water's edge and their dark reflections on the water; three
    /// ridgelines of hills behind, paler with distance; and lines of lane buoys near the boat.
    /// All of it passes the rower as the boat travels and wraps round beyond the fog, so a
    /// handful of tiles makes an endless course.
    ///
    /// Each tile is one mesh covering both sides of a stretch, so the whole course is a few
    /// dozen draw calls. Placement comes from CourseWrap (engine-free, tested). Every shape is
    /// generated with a fixed seed, so the course looks the same each run. The hills are one
    /// ridge function that repeats exactly over the hill window, so wrapped tiles join
    /// without a seam.
    /// </summary>
    [RequireComponent(typeof(SceneryLayers))]
    public sealed class CourseScenery : MonoBehaviour
    {
        [Header("Layout (metres)")]
        [Tooltip("Distance from the rower's line to the water's edge on each side.")]
        [SerializeField] float bankDistance = 60f;

        [Tooltip("Distance from the rower's line to each line of lane buoys.")]
        [SerializeField] float buoyLine = 7f;

        [SerializeField] float buoySpacing = 12.5f;

        /// <summary>The window banks and buoys wrap within, metres behind and ahead. Beyond the fog.</summary>
        public const float Behind = 900f;
        public const float Ahead = 900f;

        const float TileLength = 200f;
        public const int TileCount = (int)((Behind + Ahead) / TileLength);

        // Hills sit further out, so they wrap in a wider window, still beyond the fog.
        const float HillWindow = 1000f;
        const float HillTileLength = 250f;
        const int HillTileCount = (int)(HillWindow * 2f / HillTileLength);

        SceneryLayers _layers;
        ScenePalette _palette;
        readonly List<Transform> _banks = new List<Transform>();
        readonly List<Transform> _buoys = new List<Transform>();
        readonly List<Transform> _hills = new List<Transform>();

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            _palette = _layers.Palette != null ? _layers.Palette : gameObject.AddComponent<ScenePalette>();

            var banksMaterial = MakeMaterial("Banks", 0.12f);
            var hillsMaterial = MakeMaterial("Hills", 0f);
            var buoysMaterial = MakeMaterial("Buoys", 0.012f);
            if (banksMaterial == null || hillsMaterial == null || buoysMaterial == null) { enabled = false; return; }

            for (var i = 0; i < TileCount; i++)
            {
                _banks.Add(CreateTile("Banks " + i, _layers.Far, BuildBankTile(i), banksMaterial));
                _buoys.Add(CreateTile("Buoys " + i, _layers.Middle, BuildBuoyTile(), buoysMaterial));
            }
            for (var i = 0; i < HillTileCount; i++)
                _hills.Add(CreateTile("Hills " + i, _layers.Far, BuildHillTile(i), hillsMaterial));
        }

        /// <summary>Illustrated, vertex-coloured, and inked at the given width; the unlit shader without one.</summary>
        Material MakeMaterial(string name, float ink)
        {
            var material = IllustratedMaterials.Create(_layers, name, Color.white, ink > 0f, ink, vertexColour: true)
                           ?? _layers.CreateMaterial(name);
            // Both faces: reeds and reflections are single sheets.
            material?.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return material;
        }

        void LateUpdate()
        {
            var forward = _layers.RowerForward;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var origin = _layers.CourseOrigin + Vector3.up * _layers.WaterHeight;

            for (var i = 0; i < TileCount; i++)
            {
                // Each tile's place is the start of its stretch; tiles fill the window exactly.
                var along = CourseWrap.Position(i * TileLength - Behind, _layers.Distance, Behind, Ahead);
                var position = origin + forward * along;
                _banks[i].SetPositionAndRotation(position, rotation);
                _buoys[i].SetPositionAndRotation(position, rotation);
            }
            for (var i = 0; i < HillTileCount; i++)
            {
                var along = CourseWrap.Position(i * HillTileLength - HillWindow, _layers.Distance, HillWindow, HillWindow);
                _hills[i].SetPositionAndRotation(origin + forward * along, rotation);
            }
        }

        static Transform CreateTile(string name, Transform parent, Mesh mesh, Material material)
        {
            var tile = new GameObject(name);
            tile.transform.SetParent(parent, false);
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tile.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return tile.transform;
        }

        /// <summary>
        /// Both banks for one stretch, in tile space: +Z along the course from 0 to TileLength,
        /// X across it, Y up from the water. An earth face, a grass top, trees, reeds at the
        /// edge, and the reflection of all of it as a dark band on the water.
        /// </summary>
        Mesh BuildBankTile(int seed)
        {
            var mesh = new MeshBuilder();
            var random = new System.Random(1000 + seed);
            const float height = 2.2f;

            foreach (var side in new[] { -1f, 1f })
            {
                var edge = side * bankDistance;
                var crest = side * (bankDistance + 7f);
                var back = side * (bankDistance + 170f);

                mesh.Quad(new Vector3(edge, -0.4f, 0f), new Vector3(edge, -0.4f, TileLength),
                          new Vector3(crest, height, TileLength), new Vector3(crest, height, 0f), Color.Lerp(_palette.bank, _palette.reflection, 0.35f));
                mesh.Quad(new Vector3(crest, height, 0f), new Vector3(crest, height, TileLength),
                          new Vector3(back, height, TileLength), new Vector3(back, height, 0f), _palette.bank);

                // The trees and the bank, reflected: a dark band lying on the water.
                var near = side * (bankDistance - 9f);
                mesh.Quad(new Vector3(near, 0.03f, 0f), new Vector3(near, 0.03f, TileLength),
                          new Vector3(edge, 0.03f, TileLength), new Vector3(edge, 0.03f, 0f), _palette.reflection);

                for (var t = 0; t < 22; t++)
                {
                    var along = (float)random.NextDouble() * TileLength;
                    var across = side * (bankDistance + 12f + (float)random.NextDouble() * 110f);
                    var basePoint = new Vector3(across, height, along);
                    var shade = Color.Lerp(_palette.treeDark, _palette.treeLight, (float)random.NextDouble());
                    if (random.NextDouble() < 0.55)
                    {
                        var r = 3f + (float)random.NextDouble() * 3.5f;
                        mesh.Cylinder(basePoint, 0.35f, r * 0.9f, _palette.reflection);
                        mesh.Blob(basePoint + Vector3.up * (r * 1.5f), r, shade, random);
                    }
                    else
                    {
                        var r = 2.4f + (float)random.NextDouble() * 1.8f;
                        var tall = 9f + (float)random.NextDouble() * 8f;
                        for (var tier = 0; tier < 3; tier++)
                        {
                            var f = tier / 3f;
                            mesh.Cone(basePoint + Vector3.up * (tall * f * 0.6f), r * (1f - f * 0.3f), tall * (0.55f - f * 0.1f),
                                      Color.Lerp(shade, _palette.treeLight, f * 0.3f), 7);
                        }
                    }
                }

                for (var c = 0; c < 9; c++)
                {
                    var along = (float)random.NextDouble() * TileLength;
                    var at = side * (bankDistance - (float)random.NextDouble() * 2.5f);
                    for (var b = 0; b < 7; b++)
                    {
                        var p = new Vector3(at + ((float)random.NextDouble() - 0.5f) * 1.6f, 0f, along + ((float)random.NextDouble() - 0.5f) * 2.5f);
                        var lean = new Vector3(((float)random.NextDouble() - 0.5f) * 0.5f, 1f, ((float)random.NextDouble() - 0.5f) * 0.3f);
                        mesh.Blade(p, lean.normalized * (1.1f + (float)random.NextDouble() * 1.2f), 0.09f,
                                   Color.Lerp(_palette.reeds, _palette.bank, (float)random.NextDouble() * 0.4f));
                    }
                }
            }

            return mesh.Build("Banks");
        }

        /// <summary>
        /// Three ridgelines per side for one stretch of the hill window, nearest first. Each is
        /// a sloped face rising from the land toward a wavy crest, paler with distance.
        /// </summary>
        Mesh BuildHillTile(int index)
        {
            var mesh = new MeshBuilder();
            var place = index * HillTileLength - HillWindow;
            var ridges = new[]
            {
                (Distance: 280f, Height: 38f, Colour: _palette.hillNear),
                (Distance: 390f, Height: 62f, Colour: _palette.hillMiddle),
                (Distance: 520f, Height: 95f, Colour: _palette.hillFar),
            };

            foreach (var side in new[] { -1f, 1f })
            {
                for (var r = 0; r < ridges.Length; r++)
                {
                    var ridge = ridges[r];
                    const int steps = 25;
                    for (var s = 0; s < steps; s++)
                    {
                        float Z(int k) => HillTileLength * k / steps;
                        float H(int k) => ridge.Height * RidgeHeight(place + Z(k), side, r);
                        var front = side * (ridge.Distance - 70f);
                        var crest = side * ridge.Distance;
                        var top = Color.Lerp(ridge.Colour, _palette.skyHorizon, 0.1f);
                        mesh.Quad(new Vector3(front, 0f, Z(s)), new Vector3(front, 0f, Z(s + 1)),
                                  new Vector3(crest, H(s + 1), Z(s + 1)), new Vector3(crest, H(s), Z(s)), ridge.Colour, top);
                    }
                }
            }
            return mesh.Build("Hills");
        }

        /// <summary>
        /// Crest height, 0-1, at a place on the course. Built only from whole numbers of waves
        /// across the hill window, so it repeats exactly when tiles wrap.
        /// </summary>
        static float RidgeHeight(float place, float side, int ridge)
        {
            var u = (place + HillWindow) / (HillWindow * 2f) * 2f * Mathf.PI;
            var phase = side * 1.7f + ridge * 2.3f;
            var h = 0.55f
                  + 0.22f * Mathf.Sin(3f * u + phase)
                  + 0.14f * Mathf.Sin(5f * u + phase * 1.9f)
                  + 0.07f * Mathf.Sin(11f * u + phase * 0.7f)
                  + 0.03f * Mathf.Sin(23f * u + phase * 2.9f);
            return Mathf.Clamp01(h);
        }

        /// <summary>Both lines of lane buoys for one stretch, alternating colours.</summary>
        Mesh BuildBuoyTile()
        {
            var mesh = new MeshBuilder();
            var count = Mathf.RoundToInt(TileLength / buoySpacing);
            foreach (var side in new[] { -1f, 1f })
            {
                for (var b = 0; b < count; b++)
                    mesh.Blob(new Vector3(side * buoyLine, 0.04f, b * buoySpacing), 0.13f,
                              b % 2 == 0 ? _palette.buoyA : _palette.buoyB, null);
            }
            return mesh.Build("Buoys");
        }

        /// <summary>
        /// Accumulates flat-coloured shapes into one mesh, then bakes the smoothed normals the
        /// ink outline pass inflates along.
        /// </summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Color> _colours = new List<Color>();
            readonly List<int> _triangles = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour) => Quad(a, b, c, d, colour, colour);

            /// <summary>A quad coloured <paramref name="low"/> along a-b and <paramref name="high"/> along c-d.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color low, Color high)
            {
                var i = Add(a, low); Add(b, low); Add(c, high); Add(d, high);
                _triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Cone(Vector3 basePoint, float radius, float height, Color colour, int sides)
            {
                var apex = Add(basePoint + Vector3.up * height, colour);
                var first = _vertices.Count;
                for (var s = 0; s < sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / sides;
                    Add(basePoint + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius), colour * 0.82f);
                }
                for (var s = 0; s < sides; s++)
                    _triangles.AddRange(new[] { apex, first + s, first + (s + 1) % sides });
            }

            public void Cylinder(Vector3 basePoint, float radius, float height, Color colour)
            {
                const int sides = 5;
                var first = _vertices.Count;
                for (var s = 0; s < sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / sides;
                    var o = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    Add(basePoint + o, colour);
                    Add(basePoint + o * 0.7f + Vector3.up * height, colour);
                }
                for (var s = 0; s < sides; s++)
                {
                    int a = first + s * 2, b = first + (s + 1) % sides * 2;
                    _triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            }

            /// <summary>
            /// A rounded blob with a hand-drawn wobble in its silhouette (a fixed-seed static
            /// wobble, never animated: moving lines read as instability in VR).
            /// </summary>
            public void Blob(Vector3 centre, float radius, Color colour, System.Random random)
            {
                const int rings = 6, segments = 9;
                var first = _vertices.Count;
                var wobble = new float[segments];
                for (var s = 0; s < segments; s++) wobble[s] = random == null ? 1f : 0.85f + (float)random.NextDouble() * 0.3f;
                for (var r = 0; r <= rings; r++)
                {
                    var lat = Mathf.Lerp(-0.5f, 0.5f, (float)r / rings) * Mathf.PI;
                    for (var s = 0; s < segments; s++)
                    {
                        var lon = 2f * Mathf.PI * s / segments;
                        var w = r == 0 || r == rings ? 1f : wobble[s];
                        var p = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * w, Mathf.Sin(lat) * 0.9f, Mathf.Cos(lat) * Mathf.Sin(lon) * w);
                        Add(centre + p * radius, Color.Lerp(colour * 0.85f, colour, (float)r / rings));
                    }
                }
                for (var r = 0; r < rings; r++)
                {
                    for (var s = 0; s < segments; s++)
                    {
                        int a = first + r * segments + s, b = first + r * segments + (s + 1) % segments;
                        _triangles.AddRange(new[] { a, a + segments, b, b, a + segments, b + segments });
                    }
                }
            }

            /// <summary>A reed: one thin tapering blade from a base point along a direction.</summary>
            public void Blade(Vector3 basePoint, Vector3 stalk, float width, Color colour)
            {
                var across = Vector3.Cross(stalk, Vector3.forward).normalized * width * 0.5f;
                if (across.sqrMagnitude < 1e-6f) across = Vector3.right * width * 0.5f;
                var i = Add(basePoint - across, colour * 0.8f);
                Add(basePoint + across, colour * 0.8f);
                Add(basePoint + stalk, colour);
                _triangles.AddRange(new[] { i, i + 1, i + 2 });
            }

            int Add(Vector3 position, Color colour)
            {
                _vertices.Add(position);
                _colours.Add(SkyDome.ToVertexColour(colour));
                return _vertices.Count - 1;
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colours);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                IllustratedMaterials.BakeSmoothNormals(mesh);
                return mesh;
            }
        }
    }
}
