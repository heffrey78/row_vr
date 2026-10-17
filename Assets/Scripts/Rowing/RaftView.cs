using System;
using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The raft for the rapids (#30, #90): an oar raft. Inflatable tubes round a floor, and an
    /// aluminium frame on the tubes with a seat under the rower and an oar tower each side,
    /// which the oars (OarsView) turn on. The rower sits up at the tubes' top, as on a real
    /// oar rig: BoatFrame's oarlocks are rigged high for it when the scene is built.
    ///
    /// The raft rides the river's standing waves (RaftRocking, engine-free and tested): it
    /// heaves, pitches and rolls about the rower's seat, through BoatFrame, so the oarlocks
    /// move with it. The rower's view never moves. A depth-only lid over the opening keeps
    /// the crests passing under the hull from showing through the floor.
    ///
    /// Spray bursts round the bow when the raft is off the line in a rapid.
    ///
    /// Built in code, illustrated (#42).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(BoatFrame))]
    public sealed class RaftView : MonoBehaviour
    {
        [SerializeField] float length = 4.3f;
        [SerializeField] float width = 2f;
        [SerializeField] float tubeRadius = 0.25f;

        [Tooltip("How far the raft's centre, and the seat, sit behind the average hand position, metres.")]
        [SerializeField] float setBack = 0.5f;

        [Tooltip("How far the rower's hands ride above the seat, metres: 0.3 in the rowing-machine recordings.")]
        [SerializeField] float handsAboveSeat = 0.3f;

        [Tooltip("How much of the water's motion the raft shows, 0 (still) to 1 (#90).")]
        [Range(0f, 1f)]
        [SerializeField] float rocking = 1f;

        [Tooltip("RowVR/DepthMask, which keeps the river out of the raft. Set by RowVR > Create Rapids Scene.")]
        [SerializeField] Shader depthMaskShader;

        // Drawn before the lid, which is drawn before the river (Geometry).
        const int RaftQueue = 1998;

        BoatFrame _frame;
        RapidsMotion _rapids;
        SceneryLayers _scenery;
        Transform _raft;
        readonly List<Transform> _spray = new List<Transform>();
        float _sprayLevel;
        RaftRocking _rocking;
        Func<float, float, float> _heightUnderRaft;

        float TubeTop => tubeRadius * 1.8f;
        float SeatTop => _frame.HandsAboveWater - handsAboveSeat;

        void Awake()
        {
            _frame = GetComponent<BoatFrame>();
            _rapids = GetComponent<RapidsMotion>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            _rocking = new RaftRocking(new RaftRockingSettings { LengthMetres = length, WidthMetres = width });
            _heightUnderRaft = HeightUnderRaft;
            Build();
            _raft.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_raft != null) Destroy(_raft.gameObject);
        }

        // After BoatFrame (-50) has followed the rower and RapidsMotion (-30) has advanced the
        // run; before OarsView reads the oarlocks.
        void LateUpdate()
        {
            if (!_frame.HasFrame) return;

            _rocking.Settings.Amount = rocking;
            if (_rapids != null && _rapids.Waves != null) _rocking.Update(Time.timeAsDouble, _heightUnderRaft);
            _frame.SetRock(_rocking.Pitch, _rocking.Roll, _rocking.Heave, -setBack, SeatTop);

            _raft.SetPositionAndRotation(_frame.BoatPoint(-setBack, 0f, 0f), _frame.BoatRotation);
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

        /// <summary>
        /// The water under the raft: metres to the right of its centre and toward its bow. The
        /// raft is Run.Offset to the right of the line, and its centre setBack behind the rower,
        /// where the run measures distance.
        /// </summary>
        float HeightUnderRaft(float across, float along) =>
            _rapids.Waves.Height(_rapids.Run.Offset + across, _rapids.Run.Distance - setBack + along);

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var tubeColour = palette != null ? palette.raft : new Color(0.96f, 0.62f, 0.2f);
            var floorColour = palette != null ? palette.raftFloor : new Color(0.3f, 0.33f, 0.38f);
            var metal = palette != null ? palette.rigger : new Color(0.36f, 0.38f, 0.42f);
            var seatColour = palette != null ? palette.deck : new Color(0.72f, 0.52f, 0.34f);
            var foam = palette != null ? palette.foam : Color.white;

            _raft = new GameObject("Raft").transform;
            if (_scenery != null) _raft.SetParent(_scenery.Near, false);

            var path = TubePath();
            Part("Tube", BuildTube(path), tubeColour, 0.01f, RaftQueue);

            var floor = new ShapeBuilder();
            var hl = length * 0.5f - tubeRadius;
            var hw = width * 0.5f - tubeRadius;
            floor.Quad(new Vector3(-hw, 0.06f, -hl), new Vector3(-hw, 0.06f, hl), new Vector3(hw, 0.06f, hl), new Vector3(hw, 0.06f, -hl), floorColour);
            Part("Floor", floor.Build("Raft Floor"), floorColour, 0f, RaftQueue);

            // The frame: a rail along the top of each tube, cross bars fore and aft, a seat box
            // under the rower, and an oar tower each side up to BoatFrame's oarlocks.
            const float bar = 0.05f;
            const float frameHalf = 1.05f;
            var railY = TubeTop + bar * 0.5f;
            foreach (var side in new[] { -1f, 1f })
                Box("Rail", new Vector3(side * hw, railY, 0f), new Vector3(bar, bar, frameHalf * 2f + bar), metal);
            foreach (var end in new[] { -1f, 1f })
                Box("Cross Bar", new Vector3(0f, railY, end * frameHalf), new Vector3(hw * 2f, bar, bar), metal);
            var seatTop = SeatTop;
            Box("Seat", new Vector3(0f, (seatTop + 0.06f) * 0.5f, 0f), new Vector3(0.62f, seatTop - 0.06f, 0.4f), seatColour);

            // Raft space: the oarlocks are setBack - OarlockSetBack ahead of the seat.
            var towerZ = setBack - _frame.OarlockSetBack;
            var gateBottom = _frame.OarlockAboveWater - 0.08f;
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * _frame.OarlockSpread;
                Box("Tower Bracket", new Vector3((x + side * hw) * 0.5f, railY, towerZ), new Vector3(Mathf.Abs(x - side * hw) + bar, bar, bar), metal);
                Box("Tower", new Vector3(x, (railY + gateBottom) * 0.5f, towerZ), new Vector3(0.04f, Mathf.Max(0.01f, gateBottom - railY), 0.04f), metal);
            }

            BuildLid(path);

            // Spray blobs round the bow, shown only in rough water.
            var random = new System.Random(17);
            for (var i = 0; i < 9; i++)
            {
                var blob = new ShapeBuilder();
                blob.Blob(Vector3.zero, 1f, foam, random);
                var part = Part("Spray", blob.Build("Spray"), foam, 0.01f, -1);
                var angle = Mathf.Lerp(-80f, 80f, i / 8f) * Mathf.Deg2Rad;
                part.localPosition = new Vector3(Mathf.Sin(angle) * width * 0.6f, 0.35f, length * 0.5f * Mathf.Cos(angle) + 0.1f);
                part.gameObject.SetActive(false);
                _spray.Add(part);
            }
        }

        /// <summary>
        /// The tubes' centreline: a stadium (a rectangle with rounded ends), like an inflatable
        /// raft's buoyancy tube. Raft space: +Z toward the bow, X across, Y up from the water.
        /// </summary>
        List<Vector3> TubePath()
        {
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
            return path;
        }

        Mesh BuildTube(List<Vector3> path)
        {
            const int around = 12;
            var steps = path.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < steps; i++)
            {
                var p = path[i];
                var tangent = (path[(i + 1) % steps] - path[(i + steps - 1) % steps]).normalized;
                var outward = Vector3.Cross(Vector3.up, tangent).normalized;
                if (Vector3.Dot(outward, new Vector3(p.x, 0f, p.z)) < 0f) outward = -outward;
                for (var j = 0; j < around; j++)
                {
                    var b = 2f * Mathf.PI * j / around;
                    vertices.Add(p + (outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * tubeRadius);
                }
            }
            for (var i = 0; i < steps; i++)
            {
                var ni = (i + 1) % steps;
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

        /// <summary>The depth-only lid across the opening, out to the tubes' centreline at their top.</summary>
        void BuildLid(List<Vector3> path)
        {
            var shader = depthMaskShader != null ? depthMaskShader : Shader.Find("RowVR/DepthMask");
            if (shader == null)
            {
                Debug.LogWarning("[RowVR] RaftView: no RowVR/DepthMask shader; waves will show through the raft's floor.");
                return;
            }
            var vertices = new List<Vector3> { new Vector3(0f, TubeTop, 0f) };
            var triangles = new List<int>();
            for (var i = 0; i < path.Count; i++)
            {
                vertices.Add(new Vector3(path[i].x, TubeTop, path[i].z));
                triangles.AddRange(new[] { 0, 1 + i, 1 + (i + 1) % path.Count });
            }
            var mesh = new Mesh { name = "Raft Lid" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var lid = new GameObject("Lid");
            lid.transform.SetParent(_raft, false);
            lid.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = lid.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader) { name = "Raft Lid" };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void Box(string name, Vector3 centre, Vector3 size, Color colour)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(_raft, false);
            box.transform.localPosition = centre;
            box.transform.localScale = size;
            if (_scenery != null) IllustratedMaterials.Apply(_scenery, box, colour, true, 0.004f);
            // .material, not .sharedMaterial: without the illustrated shader the box still has the
            // primitives' shared default, which must not be moved to another queue.
            var material = box.GetComponent<Renderer>().material;
            if (_scenery == null || _scenery.IllustratedShader == null) material.color = colour;
            material.renderQueue = RaftQueue;
        }

        /// <param name="queue">The material's render queue, or -1 to keep the shader's.</param>
        Transform Part(string name, Mesh mesh, Color colour, float ink, int queue)
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
            if (queue >= 0) material.renderQueue = queue;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
