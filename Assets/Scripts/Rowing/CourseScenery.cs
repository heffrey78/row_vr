using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The course (ADR-0008, TASK-0027): tree-lined banks either side in the Far layer, and
    /// lines of lane buoys in the Middle layer. Both pass the rower as the boat travels and
    /// wrap round beyond the fog, so a handful of tiles makes an endless course.
    ///
    /// Each tile is one generated mesh covering both sides of a stretch of course, so the
    /// whole course is a few draw calls: <see cref="TileCount"/> for the banks and as many
    /// for the buoys. Placement along the course comes from CourseWrap, which is tested
    /// engine-free (CourseWrapTests).
    ///
    /// Placeholder look: flat-coloured, unlit, fogged into the horizon.
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

        [Header("Colours")]
        [SerializeField] Color grass = new Color(0.33f, 0.45f, 0.24f);
        [SerializeField] Color bankFace = new Color(0.36f, 0.33f, 0.24f);
        [SerializeField] Color trees = new Color(0.14f, 0.27f, 0.15f);
        [SerializeField] Color buoyA = new Color(0.95f, 0.45f, 0.1f);
        [SerializeField] Color buoyB = new Color(0.95f, 0.95f, 0.92f);

        /// <summary>The window scenery wraps within, metres behind and ahead. Beyond the fog.</summary>
        public const float Behind = 900f;
        public const float Ahead = 900f;

        const float TileLength = 200f;
        public const int TileCount = (int)((Behind + Ahead) / TileLength);

        SceneryLayers _layers;
        readonly List<Transform> _banks = new List<Transform>();
        readonly List<Transform> _buoys = new List<Transform>();

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            var material = _layers.CreateMaterial("Course");
            if (material == null) { enabled = false; return; }
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            for (var i = 0; i < TileCount; i++)
            {
                _banks.Add(CreateTile("Banks " + i, _layers.Far, BuildBankTile(i), material));
                _buoys.Add(CreateTile("Buoys " + i, _layers.Middle, BuildBuoyTile(), material));
            }
        }

        void LateUpdate()
        {
            var forward = _layers.RowerForward;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var origin = _layers.CourseOrigin + Vector3.up * _layers.WaterHeight;

            for (var i = 0; i < TileCount; i++)
            {
                // Each tile's place is the start of its stretch; tiles tile the window exactly.
                var along = CourseWrap.Position(i * TileLength - Behind, _layers.Distance, Behind, Ahead);
                var position = origin + forward * along;
                _banks[i].SetPositionAndRotation(position, rotation);
                _buoys[i].SetPositionAndRotation(position, rotation);
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
        /// X across it, Y up from the water. A low earth face, a grass top, and a tree line.
        /// </summary>
        Mesh BuildBankTile(int seed)
        {
            var mesh = new MeshBuilder();
            var random = new System.Random(1000 + seed);

            foreach (var side in new[] { -1f, 1f })
            {
                var edge = side * bankDistance;
                var crest = side * (bankDistance + 8f);
                var back = side * (bankDistance + 160f);
                const float height = 2.5f;

                mesh.Quad(new Vector3(edge, -0.5f, 0f), new Vector3(edge, -0.5f, TileLength),
                          new Vector3(crest, height, TileLength), new Vector3(crest, height, 0f), bankFace);
                mesh.Quad(new Vector3(crest, height, 0f), new Vector3(crest, height, TileLength),
                          new Vector3(back, height, TileLength), new Vector3(back, height, 0f), grass);

                for (var t = 0; t < 26; t++)
                {
                    var along = (float)random.NextDouble() * TileLength;
                    var across = side * (bankDistance + 14f + (float)random.NextDouble() * 120f);
                    var radius = 2.5f + (float)random.NextDouble() * 3f;
                    var tall = 8f + (float)random.NextDouble() * 9f;
                    var shade = Color.Lerp(trees, grass, (float)random.NextDouble() * 0.35f);
                    mesh.Cone(new Vector3(across, height, along), radius, tall, shade);
                }
            }

            return mesh.Build("Banks");
        }

        /// <summary>Both lines of lane buoys for one stretch, alternating colours.</summary>
        Mesh BuildBuoyTile()
        {
            var mesh = new MeshBuilder();
            var count = Mathf.RoundToInt(TileLength / buoySpacing);
            foreach (var side in new[] { -1f, 1f })
            {
                for (var b = 0; b < count; b++)
                    mesh.Diamond(new Vector3(side * buoyLine, 0.05f, b * buoySpacing), 0.14f, b % 2 == 0 ? buoyA : buoyB);
            }
            return mesh.Build("Buoys");
        }

        /// <summary>Accumulates flat-coloured primitives into one mesh.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> _vertices = new List<Vector3>();
            readonly List<Color> _colours = new List<Color>();
            readonly List<int> _triangles = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
            {
                var i = Add(a, colour); Add(b, colour); Add(c, colour); Add(d, colour);
                _triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Cone(Vector3 basePoint, float radius, float height, Color colour)
            {
                const int sides = 6;
                var apex = Add(basePoint + Vector3.up * height, colour);
                var first = _vertices.Count;
                for (var s = 0; s < sides; s++)
                {
                    var angle = 2f * Mathf.PI * s / sides;
                    Add(basePoint + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius), colour * 0.8f);
                }
                for (var s = 0; s < sides; s++)
                    _triangles.AddRange(new[] { apex, first + s, first + (s + 1) % sides });
            }

            public void Diamond(Vector3 centre, float size, Color colour)
            {
                var top = Add(centre + Vector3.up * size * 1.4f, colour);
                var bottom = Add(centre - Vector3.up * size, colour * 0.7f);
                var first = _vertices.Count;
                for (var s = 0; s < 4; s++)
                {
                    var angle = Mathf.PI * 0.5f * s;
                    Add(centre + new Vector3(Mathf.Cos(angle) * size, 0f, Mathf.Sin(angle) * size), colour);
                }
                for (var s = 0; s < 4; s++)
                {
                    var a = first + s;
                    var b = first + (s + 1) % 4;
                    _triangles.AddRange(new[] { top, a, b, bottom, b, a });
                }
            }

            int Add(Vector3 position, Color colour)
            {
                _vertices.Add(position);
                _colours.Add(SkyDome.ToVertexColour(colour));
                return _vertices.Count - 1;
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colours);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
