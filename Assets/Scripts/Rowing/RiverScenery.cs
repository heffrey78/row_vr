using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The river for the rapids (#30, illustrated per #42): close, steep banks with
    /// rocky cliffs and conifers, wrapping endlessly like the lake's; and, for each rapid in
    /// the run, its boulders, standing waves and foam, placed where that rapid lies on the
    /// river.
    ///
    /// The rough water sits mostly on the side a weak raft drifts toward (the section's
    /// DriftSide), so drifting off the line visibly carries the raft into it. The raft never
    /// hits anything: rough water costs speed in RapidsRun, and nothing jolts the view
    /// (#43).
    ///
    /// Scenery comes toward the rower, who faces downstream (SceneryLayers.facingTravel), and
    /// slides sideways with the raft's drift. Each rapid is one mesh, so the whole river is a
    /// few dozen draw calls.
    /// </summary>
    [RequireComponent(typeof(SceneryLayers))]
    public sealed class RiverScenery : MonoBehaviour
    {
        [Tooltip("Distance from the raft's line to the water's edge on each side, metres.")]
        [SerializeField] float riverHalfWidth = 14f;

        // Banks wrap within this window, metres behind and ahead: beyond the rapids scene's fog.
        const float Window = 420f;
        const float TileLength = 70f;
        const int TileCount = (int)(Window * 2f / TileLength);

        SceneryLayers _layers;
        ScenePalette _palette;
        RapidsMotion _rapids;
        readonly List<Transform> _banks = new List<Transform>();
        readonly List<(Transform Tile, double Start, float Length)> _rapidMeshes = new List<(Transform, double, float)>();

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            _palette = _layers.Palette != null ? _layers.Palette : gameObject.AddComponent<ScenePalette>();
            _rapids = FindFirstObjectByType<RapidsMotion>();

            var banksMaterial = MakeMaterial("River Banks", 0.06f);
            var rapidsMaterial = MakeMaterial("Rapids", 0.02f);
            if (banksMaterial == null || rapidsMaterial == null) { enabled = false; return; }

            for (var i = 0; i < TileCount; i++)
                _banks.Add(CreateTile("River Banks " + i, _layers.Far, BuildBankTile(i), banksMaterial));

            if (_rapids == null) return;
            var start = 0d;
            var index = 0;
            foreach (var section in _rapids.Run.Sections)
            {
                if (section.Kind == SectionKind.Rapid)
                {
                    var tile = CreateTile("Rapid " + (++index), _layers.Middle, BuildRapid(section, index), rapidsMaterial);
                    _rapidMeshes.Add((tile, start, section.LengthMetres));
                }
                start += section.LengthMetres;
            }
        }

        Material MakeMaterial(string name, float ink)
        {
            var material = IllustratedMaterials.Create(_layers, name, Color.white, ink > 0f, ink, vertexColour: true)
                           ?? _layers.CreateMaterial(name);
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
                var along = CourseWrap.Position(i * TileLength - Window, _layers.ViewDistance, Window, Window);
                _banks[i].SetPositionAndRotation(origin + forward * along, rotation);
            }

            if (_rapids == null) return;
            var travelled = _rapids.Run.Distance;
            foreach (var (tile, start, length) in _rapidMeshes)
            {
                // A rapid lies at a fixed place on the river; it comes toward the rower as the
                // raft travels, and is only drawn while it is anywhere near.
                var along = (float)(start - travelled);
                var near = along < Window && along + length > -60f;
                if (tile.gameObject.activeSelf != near) tile.gameObject.SetActive(near);
                if (near) tile.SetPositionAndRotation(origin + forward * along, rotation);
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
        /// Both banks for one stretch, in tile space (+Z downstream along the river, X across,
        /// Y up from the water): a stony edge, a steep face up to a ledge, stepped cliffs, and
        /// conifers on top.
        /// </summary>
        Mesh BuildBankTile(int seed)
        {
            var mesh = new ShapeBuilder();
            var random = new System.Random(500 + seed);
            foreach (var side in new[] { -1f, 1f })
            {
                var edge = side * riverHalfWidth;
                var ledge = side * (riverHalfWidth + 6f);
                var back = side * (riverHalfWidth + 60f);
                const float ledgeHeight = 5f;

                mesh.Quad(new Vector3(edge, -0.4f, 0f), new Vector3(edge, -0.4f, TileLength),
                          new Vector3(ledge, ledgeHeight, TileLength), new Vector3(ledge, ledgeHeight, 0f),
                          _palette.rockShade, _palette.rock);
                mesh.Quad(new Vector3(ledge, ledgeHeight, 0f), new Vector3(ledge, ledgeHeight, TileLength),
                          new Vector3(back, ledgeHeight + 4f, TileLength), new Vector3(back, ledgeHeight + 4f, 0f),
                          _palette.bank, _palette.treeLight);

                // Stones along the waterline.
                for (var r = 0; r < 10; r++)
                {
                    var p = new Vector3(side * (riverHalfWidth - 0.5f - (float)random.NextDouble() * 1.5f), 0f,
                                        (float)random.NextDouble() * TileLength);
                    mesh.Blob(p, 0.5f + (float)random.NextDouble() * 1.2f,
                              Color.Lerp(_palette.rockShade, _palette.rock, (float)random.NextDouble()), random);
                }

                // Stepped cliffs: big angular blocks on the face.
                for (var b = 0; b < 3; b++)
                {
                    var z = (float)random.NextDouble() * TileLength;
                    var size = 3f + (float)random.NextDouble() * 4f;
                    mesh.Blob(new Vector3(side * (riverHalfWidth + 4f + (float)random.NextDouble() * 4f), ledgeHeight * 0.6f, z),
                              size, Color.Lerp(_palette.rockShade, _palette.rock, 0.5f), random);
                }

                // Conifers above.
                for (var t = 0; t < 12; t++)
                {
                    var basePoint = new Vector3(side * (riverHalfWidth + 8f + (float)random.NextDouble() * 45f), ledgeHeight + 1f,
                                                (float)random.NextDouble() * TileLength);
                    var shade = Color.Lerp(_palette.treeDark, _palette.treeLight, (float)random.NextDouble() * 0.6f);
                    var r = 1.8f + (float)random.NextDouble() * 1.4f;
                    var tall = 8f + (float)random.NextDouble() * 9f;
                    for (var tier = 0; tier < 3; tier++)
                    {
                        var f = tier / 3f;
                        mesh.Cone(basePoint + Vector3.up * (tall * f * 0.6f), r * (1f - f * 0.3f), tall * (0.55f - f * 0.1f),
                                  Color.Lerp(shade, _palette.treeLight, f * 0.3f), 7);
                    }
                }
            }
            return mesh.Build("River Banks");
        }

        /// <summary>
        /// One rapid, in section space (+Z downstream from its start): boulders and standing
        /// waves, heaviest on the drift side, with foam lying on the water between them. The
        /// good line (X near 0) is kept clear.
        /// </summary>
        Mesh BuildRapid(RiverSection section, int seed)
        {
            var mesh = new ShapeBuilder();
            var random = new System.Random(900 + seed);
            var side = section.DriftSide;
            var length = section.LengthMetres;

            // Standing waves: smooth crests across the river, every few metres, each a
            // continuous ridge with its height and position varying gently along it.
            var crest = 0;
            for (var z = 8f; z < length - 4f; z += 5f + (float)random.NextDouble() * 4f, crest++)
            {
                const int segments = 28;
                var span = riverHalfWidth * 0.8f;
                var phase = (float)random.NextDouble() * 6.28f;
                float X(int k) => Mathf.Lerp(-span, span, (float)k / segments);
                // Taller toward the drift side; low but present on the line.
                float H(int k) => Mathf.Lerp(0.08f, 0.34f, Mathf.Clamp01(0.5f + 0.5f * X(k) * side / riverHalfWidth))
                                  * (0.85f + 0.15f * Mathf.Sin(X(k) * 0.5f + phase))
                                  * Mathf.Clamp01(1.2f - Mathf.Abs(X(k)) / span);
                float Z(int k) => z + Mathf.Sin(X(k) * 0.18f + phase) * 0.8f;
                for (var k = 0; k < segments; k++)
                {
                    // Broken crests: real standing waves are never one unbroken ridge.
                    if (Mathf.Sin(X(k) * 0.9f + phase * 3f) > 0.55f) continue;
                    mesh.Quad(new Vector3(X(k), 0.02f, Z(k) - 0.6f), new Vector3(X(k + 1), 0.02f, Z(k + 1) - 0.6f),
                              new Vector3(X(k + 1), H(k + 1), Z(k + 1)), new Vector3(X(k), H(k), Z(k)),
                              _palette.waterLight, _palette.foam);
                    mesh.Quad(new Vector3(X(k), H(k), Z(k)), new Vector3(X(k + 1), H(k + 1), Z(k + 1)),
                              new Vector3(X(k + 1), 0.02f, Z(k + 1) + 1f), new Vector3(X(k), 0.02f, Z(k) + 1f),
                              _palette.foam, _palette.waterLight);
                }
            }

            // Boulders: mostly on the drift side beyond the line, a few on the far side.
            for (var b = 0; b < (int)(length / 7f); b++)
            {
                var onDriftSide = random.NextDouble() < 0.75;
                var across = (onDriftSide ? side : -side) * (3.5f + (float)random.NextDouble() * (riverHalfWidth - 5f));
                var z = (float)random.NextDouble() * length;
                var r = 0.5f + (float)random.NextDouble() * 1.1f;
                mesh.Blob(new Vector3(across, r * 0.25f, z), r, Color.Lerp(_palette.rockShade, _palette.rock, (float)random.NextDouble()), random);
                // Foam piling up on the upstream face of each rock.
                mesh.Quad(new Vector3(across - r * 1.4f, 0.03f, z - r * 1.6f), new Vector3(across + r * 1.4f, 0.03f, z - r * 1.6f),
                          new Vector3(across + r * 1.1f, 0.03f, z - r * 0.3f), new Vector3(across - r * 1.1f, 0.03f, z - r * 0.3f),
                          _palette.foam);
            }

            // Foam streaks lying along the current.
            for (var f = 0; f < (int)(length / 4f); f++)
            {
                var across = ((float)random.NextDouble() * 2f - 1f) * riverHalfWidth * 0.85f;
                var z = (float)random.NextDouble() * length;
                var w = 0.3f + (float)random.NextDouble() * 0.6f;
                var l = 2f + (float)random.NextDouble() * 4f;
                mesh.Quad(new Vector3(across - w, 0.025f, z), new Vector3(across + w, 0.025f, z),
                          new Vector3(across + w * 0.4f, 0.025f, z + l), new Vector3(across - w * 0.4f, 0.025f, z + l),
                          Color.Lerp(_palette.foam, _palette.waterLight, 0.35f));
            }

            return mesh.Build("Rapid " + seed);
        }
    }
}
