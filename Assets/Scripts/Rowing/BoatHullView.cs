using System.Collections.Generic;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The boat the rower sits in (TASK-0030): a single-scull shell, riggers out to the
    /// oarlocks, slide rails with a seat that slides with the rower, and a footplate. It sits
    /// on the water at <see cref="BoatFrame"/>, which the oars share, so the riggers always
    /// meet the oars' gates.
    ///
    /// The rower faces the stern (ADR-0010): most of the shell runs behind them, and the
    /// stern is in front, past their feet. The shell never pitches or rolls (ADR-0010).
    ///
    /// Built in code, illustrated (ADR-0011) when the scene provides the shader.
    /// </summary>
    [RequireComponent(typeof(RowingRig), typeof(BoatFrame))]
    public sealed class BoatHullView : MonoBehaviour
    {
        [Header("Shell (metres)")]
        [Tooltip("How far the stern reaches in front of the stroke's midpoint.")]
        [SerializeField] float sternReach = 2.6f;

        [Tooltip("How far the bow reaches behind it.")]
        [SerializeField] float bowReach = 5.2f;

        [SerializeField] float beam = 0.29f;
        [SerializeField] float freeboard = 0.06f;
        [SerializeField] float draft = 0.11f;

        [Tooltip("Where the shell is widest, relative to the stroke's midpoint (negative is toward the bow).")]
        [SerializeField] float widestAt = -0.6f;

        const int Stations = 48;
        const int RingPoints = 10;

        RowingRig _rig;
        BoatFrame _frame;
        SceneryLayers _scenery;
        Transform _boat;
        Transform _seat;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _frame = GetComponent<BoatFrame>();
            if (_frame == null) _frame = gameObject.AddComponent<BoatFrame>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            Build();
            _boat.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_boat != null) Destroy(_boat.gameObject);
        }

        void LateUpdate()
        {
            if (!_frame.HasFrame) return;
            _boat.SetPositionAndRotation(_frame.Point(0f, 0f, 0f), _frame.Rotation);
            if (!_boat.gameObject.activeSelf) _boat.gameObject.SetActive(true);

            // The seat rides the rails under the rower's head: their slide, made visible.
            if (_rig.IsTracking)
            {
                var head = _rig.LastSample.HeadPosition.ToVector3();
                var along = Vector3.Dot(head - _frame.Centre, _frame.Forward);
                _seat.localPosition = new Vector3(0f, freeboard + 0.07f, Mathf.Clamp(along, -1.05f, 0.1f));
            }
        }

        void Build()
        {
            _boat = new GameObject("Boat").transform;
            if (_scenery != null) _boat.SetParent(_scenery.Near, false);
            var palette = _scenery != null ? _scenery.Palette : null;
            var hullColour = palette != null ? palette.hull : new Color(0.93f, 0.9f, 0.82f);
            var deckColour = palette != null ? palette.deck : new Color(0.72f, 0.52f, 0.34f);
            var metal = palette != null ? palette.rigger : new Color(0.36f, 0.38f, 0.42f);

            // Boat space: +Z toward the stern (the rower's forward), +X to the rower's right,
            // Y up from the waterline.
            MeshPart("Shell", BuildShell(), hullColour, 0.008f);

            // The open cockpit where the rower sits: a wooden well between the decks.
            Box("Cockpit", new Vector3(0f, freeboard + 0.005f, -0.35f), new Vector3(beam * 0.78f, 0.012f, 1.9f), deckColour, 0.004f);

            // Slide rails and the seat that rides them.
            foreach (var x in new[] { -0.08f, 0.08f })
                Box("Rail", new Vector3(x, freeboard + 0.035f, -0.45f), new Vector3(0.018f, 0.018f, 1.25f), metal, 0.003f);
            _seat = Box("Seat", new Vector3(0f, freeboard + 0.07f, -0.5f), new Vector3(0.26f, 0.035f, 0.24f), deckColour, 0.005f);

            // Footplate, angled toward the rower, with a pair of shoes.
            var foot = Box("Footplate", new Vector3(0f, freeboard + 0.12f, 0.72f), new Vector3(0.26f, 0.2f, 0.015f), metal, 0.004f);
            foot.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            foreach (var x in new[] { -0.065f, 0.065f })
            {
                var shoe = Box("Shoe", new Vector3(x, freeboard + 0.1f, 0.66f), new Vector3(0.09f, 0.07f, 0.24f), deckColour * 0.6f, 0.004f);
                shoe.localRotation = Quaternion.Euler(-38f, 0f, 0f);
            }

            // Riggers: from the gunwale out to each oarlock, a top arm and two stays. The
            // oarlocks are BoatFrame's, the same ones the oars turn in, in boat space.
            foreach (var side in new[] { -1f, 1f })
            {
                var oarlock = new Vector3(side * _frame.OarlockSpread, _frame.OarlockAboveWater, -_frame.OarlockSetBack);
                var gunwale = side * beam * 0.45f;
                Strut("Rigger Arm", new Vector3(gunwale, freeboard, oarlock.z), oarlock, metal);
                Strut("Rigger Stay", new Vector3(gunwale, freeboard - 0.01f, 0.35f), oarlock + Vector3.down * 0.07f, metal);
                Strut("Rigger Stay", new Vector3(gunwale, freeboard - 0.01f, -0.55f), oarlock + Vector3.down * 0.07f, metal);
                // The upright the gate turns on.
                Box("Pin", oarlock + Vector3.down * 0.05f, new Vector3(0.016f, 0.1f, 0.016f), metal, 0.002f);
            }
        }

        /// <summary>
        /// A closed shell: rings from bow to stern, each running from one deck edge down round
        /// the keel to the other, closed over the top by a slightly crowned deck.
        /// </summary>
        Mesh BuildShell()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (var i = 0; i <= Stations; i++)
            {
                var z = Mathf.Lerp(-bowReach, sternReach, (float)i / Stations);
                var s = z < widestAt ? (z - widestAt) / (widestAt + bowReach) : (z - widestAt) / (sternReach - widestAt);
                var fullness = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(s), 2.2f)), 0.7f);
                var halfBeam = Mathf.Max(0.004f, beam * 0.5f * fullness);
                var depth = Mathf.Max(0.004f, draft * Mathf.Sqrt(fullness));
                var deckY = freeboard * (0.6f + 0.4f * fullness);

                for (var r = 0; r < RingPoints; r++)
                {
                    var theta = Mathf.PI * r / (RingPoints - 1);
                    vertices.Add(new Vector3(halfBeam * Mathf.Cos(theta), deckY - (deckY + depth) * Mathf.Sin(theta), z));
                }
                // Deck crown point on the centreline.
                vertices.Add(new Vector3(0f, deckY + 0.012f * fullness, z));
            }

            var stride = RingPoints + 1;
            for (var i = 0; i < Stations; i++)
            {
                var a = i * stride;
                var b = a + stride;
                for (var r = 0; r < RingPoints - 1; r++)
                {
                    triangles.AddRange(new[] { a + r, b + r, a + r + 1 });
                    triangles.AddRange(new[] { a + r + 1, b + r, b + r + 1 });
                }
                // Deck: from each deck edge up to the crown.
                var crownA = a + RingPoints;
                var crownB = b + RingPoints;
                triangles.AddRange(new[] { a, crownA, b, crownA, crownB, b });
                var lastA = a + RingPoints - 1;
                var lastB = b + RingPoints - 1;
                triangles.AddRange(new[] { lastA, lastB, crownA, crownA, lastB, crownB });
            }

            var mesh = new Mesh { name = "Scull Shell" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            IllustratedMaterials.BakeSmoothNormals(mesh);
            return mesh;
        }

        void MeshPart(string name, Mesh mesh, Color colour, float ink)
        {
            var part = new GameObject(name);
            part.transform.SetParent(_boat, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            var material = IllustratedMaterials.Create(_scenery, name, colour, true, ink);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Both sides: the shell is seen from above and below the waterline edge.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        Transform Box(string name, Vector3 position, Vector3 size, Color colour, float ink)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(_boat, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            Paint(box, colour, ink);
            return box.transform;
        }

        void Strut(string name, Vector3 from, Vector3 to, Color colour)
        {
            var strut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            strut.name = name;
            Destroy(strut.GetComponent<Collider>());
            strut.transform.SetParent(_boat, false);
            var direction = to - from;
            strut.transform.localPosition = (from + to) * 0.5f;
            strut.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            strut.transform.localScale = new Vector3(0.022f, direction.magnitude * 0.5f, 0.022f);
            Paint(strut, colour, 0.003f);
        }

        void Paint(GameObject part, Color colour, float ink)
        {
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, part, colour, true, ink);
            else
                part.GetComponent<Renderer>().material.color = colour;
        }
    }
}
