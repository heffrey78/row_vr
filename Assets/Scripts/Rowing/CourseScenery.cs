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
            var origin = _layers.CourseOrigin + Vector3.up * _layers.WaterHeight + _layers.DriftShift;

            for (var i = 0; i < TileCount; i++)
            {
                // Each tile's place is the start of its stretch; tiles fill the window exactly.
                var along = CourseWrap.Position(i * TileLength - Behind, _layers.ViewDistance, Behind, Ahead);
                var position = origin + forward * along;
                _banks[i].SetPositionAndRotation(position, rotation);
                _buoys[i].SetPositionAndRotation(position, rotation);
            }
            for (var i = 0; i < HillTileCount; i++)
            {
                var along = CourseWrap.Position(i * HillTileLength - HillWindow, _layers.ViewDistance, HillWindow, HillWindow);
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
            var mesh = new ShapeBuilder();
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
            var mesh = new ShapeBuilder();
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
            var mesh = new ShapeBuilder();
            var count = Mathf.RoundToInt(TileLength / buoySpacing);
            foreach (var side in new[] { -1f, 1f })
            {
                for (var b = 0; b < count; b++)
                    mesh.Blob(new Vector3(side * buoyLine, 0.04f, b * buoySpacing), 0.13f,
                              b % 2 == 0 ? _palette.buoyA : _palette.buoyB, null);
            }
            return mesh.Build("Buoys");
        }
    }
}
