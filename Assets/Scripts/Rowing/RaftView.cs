using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The raft for the rapids (REQ-0007): an inflatable tube round the rower, a floor and a
    /// thwart under them, facing downstream. Sits on the water at BoatFrame, and never rolls,
    /// pitches or bobs (ADR-0012): rough water is shown by spray bursting round the bow when
    /// the raft is off the line in a rapid, not by moving the rower's view.
    ///
    /// Built in code, illustrated (ADR-0011).
    /// </summary>
    [RequireComponent(typeof(BoatFrame))]
    public sealed class RaftView : MonoBehaviour
    {
        [SerializeField] float length = 4.2f;
        [SerializeField] float width = 1.9f;
        [SerializeField] float tubeRadius = 0.2f;

        [Tooltip("How far the raft's centre sits behind the average hand position, metres.")]
        [SerializeField] float setBack = 0.9f;

        BoatFrame _frame;
        RapidsMotion _rapids;
        SceneryLayers _scenery;
        Transform _raft;
        readonly List<Transform> _spray = new List<Transform>();
        float _sprayLevel;

        void Awake()
        {
            _frame = GetComponent<BoatFrame>();
            _rapids = GetComponent<RapidsMotion>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            Build();
            _raft.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_raft != null) Destroy(_raft.gameObject);
        }

        void LateUpdate()
        {
            if (!_frame.HasFrame) return;
            _raft.SetPositionAndRotation(_frame.Point(-setBack, 0f, 0f), _frame.Rotation);
            if (!_raft.gameObject.activeSelf) _raft.gameObject.SetActive(true);

            // Spray: grows in over a second when off the line in a rapid, dies away after.
            var rough = _rapids != null && _rapids.Run.InRapid && !_rapids.Run.OnLine;
            var inRapid = _rapids != null && _rapids.Run.InRapid;
            var target = rough ? 1f : inRapid ? 0.35f : 0f;
            _sprayLevel = Mathf.MoveTowards(_sprayLevel, target, Time.deltaTime);
            var t = Time.time;
            for (var i = 0; i < _spray.Count; i++)
            {
                var burst = Mathf.Max(0f, Mathf.Sin(t * (2.1f + i * 0.37f) + i * 1.9f));
                var size = _sprayLevel * burst * (0.25f + 0.1f * (i % 3));
                _spray[i].localScale = new Vector3(size, size * 1.3f, size);
                _spray[i].gameObject.SetActive(size > 0.01f);
            }
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var tubeColour = palette != null ? palette.raft : new Color(0.96f, 0.62f, 0.2f);
            var floorColour = palette != null ? palette.raftFloor : new Color(0.3f, 0.33f, 0.38f);
            var foam = palette != null ? palette.foam : Color.white;

            _raft = new GameObject("Raft").transform;
            if (_scenery != null) _raft.SetParent(_scenery.Near, false);

            Part("Tube", BuildTube(), tubeColour, 0.01f);

            var floor = new ShapeBuilder();
            var hl = length * 0.5f - tubeRadius;
            var hw = width * 0.5f - tubeRadius;
            floor.Quad(new Vector3(-hw, 0.04f, -hl), new Vector3(-hw, 0.04f, hl), new Vector3(hw, 0.04f, hl), new Vector3(hw, 0.04f, -hl), floorColour);
            Part("Floor", floor.Build("Raft Floor"), floorColour, 0f);

            // The thwart: a cross-tube under the rower.
            var thwart = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            thwart.name = "Thwart";
            Destroy(thwart.GetComponent<Collider>());
            thwart.transform.SetParent(_raft, false);
            thwart.transform.localPosition = new Vector3(0f, tubeRadius * 0.8f, -0.2f);
            thwart.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            thwart.transform.localScale = new Vector3(tubeRadius * 1.5f, width * 0.5f - tubeRadius, tubeRadius * 1.5f);
            if (_scenery != null) IllustratedMaterials.Apply(_scenery, thwart, tubeColour, true, 0.008f);

            // Spray blobs round the bow, shown only in rough water.
            var random = new System.Random(17);
            for (var i = 0; i < 9; i++)
            {
                var blob = new ShapeBuilder();
                blob.Blob(Vector3.zero, 1f, foam, random);
                var part = Part("Spray", blob.Build("Spray"), foam, 0.01f);
                var angle = Mathf.Lerp(-80f, 80f, i / 8f) * Mathf.Deg2Rad;
                part.localPosition = new Vector3(Mathf.Sin(angle) * width * 0.6f, 0.35f, length * 0.5f * Mathf.Cos(angle) + 0.1f);
                part.gameObject.SetActive(false);
                _spray.Add(part);
            }
        }

        /// <summary>
        /// A tube swept round a stadium (a rectangle with rounded ends), like an inflatable
        /// raft's buoyancy tube. Raft space: +Z downstream, X across, Y up from the water.
        /// </summary>
        Mesh BuildTube()
        {
            const int around = 12;
            const int pathSteps = 40;
            var path = new List<Vector3>();
            var r = width * 0.5f - tubeRadius;
            var straight = length * 0.5f - width * 0.5f;
            for (var i = 0; i < pathSteps; i++)
            {
                var a = 2f * Mathf.PI * i / pathSteps;
                var x = Mathf.Cos(a) * r;
                var z = Mathf.Sin(a) * r + (Mathf.Sin(a) >= 0f ? straight : -straight);
                path.Add(new Vector3(x, tubeRadius * 0.8f, z));
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < pathSteps; i++)
            {
                var p = path[i];
                var tangent = (path[(i + 1) % pathSteps] - path[(i + pathSteps - 1) % pathSteps]).normalized;
                var outward = Vector3.Cross(Vector3.up, tangent).normalized;
                if (Vector3.Dot(outward, new Vector3(p.x, 0f, p.z)) < 0f) outward = -outward;
                for (var j = 0; j < around; j++)
                {
                    var b = 2f * Mathf.PI * j / around;
                    vertices.Add(p + (outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * tubeRadius);
                }
            }
            for (var i = 0; i < pathSteps; i++)
            {
                var ni = (i + 1) % pathSteps;
                for (var j = 0; j < around; j++)
                {
                    var nj = (j + 1) % around;
                    int a = i * around + j, b = ni * around + j, c = ni * around + nj, d = i * around + nj;
                    triangles.AddRange(new[] { a, b, c, a, c, d });
                }
            }
            var mesh = new Mesh { name = "Raft Tube" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            IllustratedMaterials.BakeSmoothNormals(mesh);
            return mesh;
        }

        Transform Part(string name, Mesh mesh, Color colour, float ink)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_raft, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            // Built shapes carry their colour in the vertices; tint those white, not twice.
            var vertexColoured = name == "Spray" || name == "Floor";
            var material = IllustratedMaterials.Create(_scenery, name, vertexColoured ? Color.white : colour, ink > 0f, ink, vertexColoured)
                           ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
