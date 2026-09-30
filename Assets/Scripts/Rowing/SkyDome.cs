using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The sky (ADR-0008 Sky layer, TASK-0025): a dome around the head, graded from the zenith
    /// colour down to the horizon colour, and horizon colour below it, so the fogged water
    /// meets it with no seam.
    ///
    /// It follows the head's position, never its rotation. A sky that moved with the head or
    /// the stroke would tilt the horizon, which is the fastest way to make a rower sick. It is
    /// geometry rather than the camera's skybox so that hiding it leaves transparent pixels
    /// for passthrough.
    /// </summary>
    [RequireComponent(typeof(SceneryLayers))]
    public sealed class SkyDome : MonoBehaviour
    {
        [SerializeField] Color zenithColour = new Color(0.28f, 0.48f, 0.78f);

        [Tooltip("Dome radius, metres. Inside the camera's far clip, beyond the fog.")]
        [SerializeField] float radius = 900f;

        const int Segments = 32;
        const int Rings = 16;

        SceneryLayers _layers;
        Transform _dome;

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            var material = _layers.CreateMaterial("Sky");
            if (material == null) { enabled = false; return; }
            material.SetFloat("_FogAmount", 0f);
            // Both faces: seen only from inside, and drawing both costs nothing that matters
            // for one mesh, while getting the winding wrong would make the sky vanish.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            var dome = new GameObject("Sky Dome");
            dome.transform.SetParent(_layers.Sky, false);
            dome.AddComponent<MeshFilter>().sharedMesh = BuildDome(zenithColour, _layers.HorizonColour);
            var renderer = dome.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            dome.transform.localScale = Vector3.one * radius;
            _dome = dome.transform;
        }

        void LateUpdate()
        {
            var head = _layers.Head;
            if (_dome != null && head != null) _dome.position = head.position;
        }

        /// <summary>
        /// Vertex colours reach the shader as-is, unlike material and texture colours, which
        /// Unity converts. The project renders in Linear space, so convert them here or every
        /// authored colour comes out washed out.
        /// </summary>
        internal static Color ToVertexColour(Color authored) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        /// <summary>
        /// A unit sphere with inward-facing triangles, coloured by latitude: the zenith colour
        /// at the top, easing to the horizon colour at the horizon, and flat below it.
        /// </summary>
        static Mesh BuildDome(Color zenith, Color horizon)
        {
            var vertices = new Vector3[(Segments + 1) * (Rings + 1)];
            var colours = new Color[vertices.Length];
            for (var r = 0; r <= Rings; r++)
            {
                var latitude = Mathf.Lerp(90f, -90f, (float)r / Rings) * Mathf.Deg2Rad;
                var y = Mathf.Sin(latitude);
                var ring = Mathf.Cos(latitude);
                // Most of the change happens low in the sky, as in a real one.
                var t = y > 0f ? Mathf.Pow(y, 0.5f) : 0f;
                var colour = Color.Lerp(horizon, zenith, t);
                for (var s = 0; s <= Segments; s++)
                {
                    var angle = 2f * Mathf.PI * s / Segments;
                    var i = r * (Segments + 1) + s;
                    vertices[i] = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                    colours[i] = ToVertexColour(colour);
                }
            }

            var triangles = new int[Segments * Rings * 6];
            var k = 0;
            for (var r = 0; r < Rings; r++)
            {
                for (var s = 0; s < Segments; s++)
                {
                    var a = r * (Segments + 1) + s;
                    var b = a + Segments + 1;
                    // Wound to face inward: the camera is inside the dome.
                    triangles[k++] = a; triangles[k++] = a + 1; triangles[k++] = b;
                    triangles[k++] = a + 1; triangles[k++] = b + 1; triangles[k++] = b;
                }
            }

            var mesh = new Mesh { name = "Sky Dome", vertices = vertices, colors = colours, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }
    }
}
