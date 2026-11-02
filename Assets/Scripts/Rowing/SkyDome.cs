using System.Collections.Generic;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The sky (#39 Sky layer; illustrated per #42): a painted dome, a low morning
    /// sun with a warm glow along the horizon beneath it, flat painted clouds, and a few birds
    /// crossing slowly.
    ///
    /// Everything here follows the head's position and never its rotation. A sky that turned
    /// with the head or the stroke would tilt the horizon, which is the fastest way to make a
    /// rower sick. It is geometry rather than the camera's skybox so that hiding it leaves
    /// transparent pixels for passthrough. None of it is fogged: the sky is the colour the
    /// fog fades to.
    /// </summary>
    [RequireComponent(typeof(SceneryLayers))]
    public sealed class SkyDome : MonoBehaviour
    {
        [Tooltip("Dome radius, metres. Inside the camera's far clip, beyond the fog.")]
        [SerializeField] float radius = 900f;

        [Tooltip("Where the sun sits: degrees from the rower's forward (negative is to their left), and above the horizon.")]
        [SerializeField] float sunAzimuth = -35f;
        [SerializeField] float sunElevation = 11f;

        [SerializeField] int clouds = 9;
        [SerializeField] int birds = 5;

        const int Segments = 48;
        const int Rings = 20;

        SceneryLayers _layers;
        Transform _sky;
        readonly List<Bird> _birds = new List<Bird>();

        sealed class Bird
        {
            public Transform Body;
            public Transform LeftWing, RightWing;
            public Vector3 Start, Velocity;
            public float Phase;
            public float Period;
        }

        void Start()
        {
            _layers = GetComponent<SceneryLayers>();
            var material = _layers.CreateMaterial("Sky");
            if (material == null) { enabled = false; return; }
            material.SetFloat("_FogAmount", 0f);
            // Both faces: seen only from inside, and getting a winding wrong would make it vanish.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            var palette = _layers.Palette;
            var zenith = palette != null ? palette.skyZenith : new Color(0.28f, 0.48f, 0.78f);
            var horizon = _layers.HorizonColour;
            var glow = palette != null ? palette.horizonGlow : horizon;
            var sunColour = palette != null ? palette.sun : Color.white;
            var cloudColour = palette != null ? palette.cloud : Color.white;
            var cloudShade = palette != null ? palette.cloudShade : Color.grey;
            var inkColour = palette != null ? palette.ink : Color.black;

            _sky = new GameObject("Sky").transform;
            _sky.SetParent(_layers.Sky, false);
            // Built facing the course, so the sun keeps its place relative to the rower.
            _sky.rotation = Quaternion.LookRotation(_layers.RowerForward, Vector3.up);

            var sunDirection = Quaternion.Euler(-sunElevation, sunAzimuth, 0f) * Vector3.forward;

            AddMesh("Dome", BuildDome(zenith, horizon, glow, sunDirection), material, Vector3.one * radius);
            AddMesh("Sun Halo", Disc(sunDirection * radius * 0.93f, 58f, Color.Lerp(glow, sunColour, 0.4f), 40), material);
            AddMesh("Sun", Disc(sunDirection * radius * 0.92f, 34f, sunColour, 40), material);

            var random = new System.Random(21);
            for (var i = 0; i < clouds; i++)
            {
                var azimuth = -170f + 340f * (i + (float)random.NextDouble() * 0.6f) / clouds;
                var elevation = 6f + (float)random.NextDouble() * 16f;
                var direction = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
                AddMesh("Cloud", Cloud(direction * radius * 0.7f, 40f + (float)random.NextDouble() * 50f,
                                       cloudColour, cloudShade, random), material);
            }

            for (var i = 0; i < birds; i++) _birds.Add(CreateBird(material, inkColour, random));
        }

        void LateUpdate()
        {
            var head = _layers.Head;
            // Position follows the head; rotation follows only the course direction, which is
            // fixed once the rower is in place, so the sun keeps its place and never tilts.
            if (_sky != null && head != null)
                _sky.SetPositionAndRotation(head.position, Quaternion.LookRotation(_layers.RowerForward, Vector3.up));

            // Birds drift across and wrap round; wings beat on a slow sine.
            var t = Time.time;
            foreach (var bird in _birds)
            {
                var along = (t + bird.Phase) % bird.Period - bird.Period * 0.5f;
                bird.Body.localPosition = bird.Start + bird.Velocity * along;
                bird.Body.localRotation = Quaternion.LookRotation(bird.Velocity.normalized, Vector3.up);
                var flap = Mathf.Sin((t + bird.Phase) * 5.5f) * 28f;
                bird.LeftWing.localRotation = Quaternion.Euler(0f, 0f, 12f + flap);
                bird.RightWing.localRotation = Quaternion.Euler(0f, 0f, -12f - flap);
            }
        }

        void AddMesh(string name, Mesh mesh, Material material) => AddMesh(name, mesh, material, Vector3.one);

        void AddMesh(string name, Mesh mesh, Material material, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_sky, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Vertex colours reach the shader as-is, unlike material and texture colours, which
        /// Unity converts. The project renders in Linear space, so convert them here or every
        /// authored colour comes out washed out.
        /// </summary>
        internal static Color ToVertexColour(Color authored) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        /// <summary>
        /// A unit sphere coloured by latitude, zenith to horizon, with a warm glow along the
        /// horizon that is strongest toward the sun.
        /// </summary>
        static Mesh BuildDome(Color zenith, Color horizon, Color glow, Vector3 sun)
        {
            var vertices = new Vector3[(Segments + 1) * (Rings + 1)];
            var colours = new Color[vertices.Length];
            var sunFlat = new Vector3(sun.x, 0f, sun.z).normalized;
            for (var r = 0; r <= Rings; r++)
            {
                var latitude = Mathf.Lerp(90f, -90f, (float)r / Rings) * Mathf.Deg2Rad;
                var y = Mathf.Sin(latitude);
                var ring = Mathf.Cos(latitude);
                for (var s = 0; s <= Segments; s++)
                {
                    var angle = 2f * Mathf.PI * s / Segments;
                    var p = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                    var i = r * (Segments + 1) + s;
                    vertices[i] = p;

                    // Most of the change is low in the sky, as in a real one.
                    var height = y > 0f ? Mathf.Pow(y, 0.45f) : 0f;
                    var colour = Color.Lerp(horizon, zenith, height);
                    // The glow: low in the sky and toward the sun.
                    var flat = new Vector3(p.x, 0f, p.z);
                    var toward = flat.sqrMagnitude > 1e-6f ? Mathf.Max(0f, Vector3.Dot(flat.normalized, sunFlat)) : 0f;
                    var low = Mathf.Clamp01(1f - Mathf.Abs(y) * 4f);
                    colour = Color.Lerp(colour, glow, Mathf.Pow(toward, 3f) * low * 0.85f);
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
                    triangles[k++] = a; triangles[k++] = a + 1; triangles[k++] = b;
                    triangles[k++] = a + 1; triangles[k++] = b + 1; triangles[k++] = b;
                }
            }

            var mesh = new Mesh { name = "Sky Dome", vertices = vertices, colors = colours, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        /// <summary>A flat disc facing the centre of the sky.</summary>
        static Mesh Disc(Vector3 centre, float size, Color colour, int sides)
        {
            var facing = Quaternion.LookRotation(-centre.normalized, Vector3.up);
            var vertices = new List<Vector3> { centre };
            var colours = new List<Color> { ToVertexColour(colour) };
            var triangles = new List<int>();
            for (var s = 0; s <= sides; s++)
            {
                var a = 2f * Mathf.PI * s / sides;
                vertices.Add(centre + facing * new Vector3(Mathf.Cos(a) * size, Mathf.Sin(a) * size, 0f));
                colours.Add(ToVertexColour(colour));
                if (s > 0) triangles.AddRange(new[] { 0, s, s + 1 });
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A painted cloud: a flat-bottomed row of overlapping puffs, lit on top and shaded
        /// underneath, facing the centre of the sky.
        /// </summary>
        static Mesh Cloud(Vector3 centre, float width, Color light, Color shade, System.Random random)
        {
            var facing = Quaternion.LookRotation(-centre.normalized, Vector3.up);
            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            var puffs = 4 + random.Next(4);
            for (var p = 0; p < puffs; p++)
            {
                var x = Mathf.Lerp(-width * 0.5f, width * 0.5f, (p + 0.5f) / puffs) + ((float)random.NextDouble() - 0.5f) * width * 0.1f;
                var r = width * (0.14f + (float)random.NextDouble() * 0.12f) * (1f - Mathf.Abs(x) / width * 0.9f);
                var cy = r * 0.35f;
                var baseIndex = vertices.Count;
                // Later puffs sit slightly nearer, so overlaps draw in a stable order.
                var depth = -p * 0.2f;
                vertices.Add(centre + facing * new Vector3(x, cy, depth));
                colours.Add(ToVertexColour(light));
                const int sides = 20;
                for (var s = 0; s <= sides; s++)
                {
                    var a = 2f * Mathf.PI * s / sides;
                    // Flat bottom: puffs are clipped at the cloud's base line.
                    var py = Mathf.Max(cy + Mathf.Sin(a) * r, 0f);
                    vertices.Add(centre + facing * new Vector3(x + Mathf.Cos(a) * r, py, depth));
                    colours.Add(ToVertexColour(Color.Lerp(shade, light, Mathf.Clamp01(py / (r * 1.2f) + 0.25f))));
                    if (s > 0) triangles.AddRange(new[] { baseIndex, baseIndex + s, baseIndex + s + 1 });
                }
            }
            var mesh = new Mesh { name = "Cloud" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        Bird CreateBird(Material material, Color ink, System.Random random)
        {
            var body = new GameObject("Bird").transform;
            body.SetParent(_sky, false);

            // A thin curved stroke, like a pen tick: the classic drawn bird.
            Transform Wing(float side)
            {
                var pivot = new GameObject(side < 0 ? "Left Wing" : "Right Wing").transform;
                pivot.SetParent(body, false);
                var vertices = new List<Vector3>();
                var colours = new List<Color>();
                var triangles = new List<int>();
                const int n = 6;
                for (var i = 0; i <= n; i++)
                {
                    var t = (float)i / n;
                    var x = side * t * 1.3f;
                    var y = Mathf.Sin(t * Mathf.PI) * 0.25f;
                    var w = 0.07f * (1f - t * 0.8f);
                    vertices.Add(new Vector3(x, y + w, 0f));
                    vertices.Add(new Vector3(x, y - w, 0f));
                    colours.Add(ToVertexColour(ink));
                    colours.Add(ToVertexColour(ink));
                    if (i < n)
                    {
                        var a = i * 2;
                        triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                    }
                }
                var mesh = new Mesh { name = "Wing" };
                mesh.SetVertices(vertices);
                mesh.SetColors(colours);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                var stroke = new GameObject("Stroke");
                stroke.transform.SetParent(pivot, false);
                stroke.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = stroke.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return pivot;
            }

            var azimuth = (float)random.NextDouble() * 360f;
            var distance = 120f + (float)random.NextDouble() * 160f;
            var height = 25f + (float)random.NextDouble() * 35f;
            var start = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward * distance + Vector3.up * height;
            var across = Quaternion.Euler(0f, azimuth + 90f + ((float)random.NextDouble() - 0.5f) * 40f, 0f) * Vector3.forward;
            body.localScale = Vector3.one * (1.2f + (float)random.NextDouble() * 0.8f);
            return new Bird
            {
                Body = body,
                LeftWing = Wing(-1f),
                RightWing = Wing(1f),
                Start = start,
                Velocity = across * (6f + (float)random.NextDouble() * 5f),
                Phase = (float)random.NextDouble() * 60f,
                Period = 70f + (float)random.NextDouble() * 40f,
            };
        }
    }
}
