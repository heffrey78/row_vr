using System.Collections.Generic;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Accumulates flat-coloured shapes into one mesh, then bakes the smoothed normals the
    /// ink outline pass inflates along.
    /// </summary>
    public sealed class ShapeBuilder
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
