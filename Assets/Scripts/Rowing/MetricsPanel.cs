using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The rower's numbers (#29), written in cloud high in the sky (#102). Pace per 500 m is the
    /// big cloud, with time and distance as smaller clouds lower down, each with a small
    /// hand-lettered caption. Rate, strokes and average pace are on the wrist menu (Footer).
    ///
    /// The puffs billow, and when a number changes they drift into the new digits while spares
    /// evaporate: CloudNumerals, engine-free and tested, in BlockFont's shapes. What goes on
    /// which line is SkyReadout, also tested. It replaced a small card in the boat (in the line
    /// of sight) and then a board of block digits (#99, "I don't want a board").
    ///
    /// The clouds sit far out along the boat's heading, above the horizon. Like the sky dome,
    /// they follow the head's position and only the boat's heading, so they hold a steady place
    /// in the sky. Every puff is a painted disc over a larger shaded one, all in one mesh that is
    /// refilled each frame (about 15k vertices; one draw call). It was drawn instanced at first,
    /// which rendered in the Editor but not on the Quest: plain meshes are what every other
    /// part of the scene already draws with there.
    ///
    /// They belong to the virtual scene: when passthrough fades the room in, they fade with it.
    /// The class keeps its old name so the scenes' wiring holds without regenerating them.
    /// </summary>
    [RequireComponent(typeof(BoatMotion), typeof(BoatFrame))]
    public sealed class MetricsPanel : MonoBehaviour
    {
        [Tooltip("Patrick Hand, for the captions. Set by RowVR > Add Scenery.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by RowVR > Add Scenery; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [Header("Placement")]
        [Tooltip("Distance from the eyes to the clouds, metres.")]
        [SerializeField] float distance = 200f;

        [Tooltip("Degrees above the horizon of the pace cloud's middle.")]
        [SerializeField] float elevation = 20f;

        [Tooltip("Size of one cell of the pace cloud's lettering, metres. Five cells make a digit's height.")]
        [SerializeField] float headlineBlock = 5f;

        const int MaxPuffs = 1024;
        const int Sides = 24;
        // A disc: its middle and a closed ring. Each puff is two: the shaded one behind, then the body.
        const int DiscVertices = Sides + 2;
        const int DiscIndices = Sides * 3;

        BoatMotion _motion;
        BoatFrame _frame;
        SceneryLayers _scenery;
        Material _cloudMaterial;
        Material _textMaterial;
        Transform _captions;
        Mesh _clouds;
        Vector3[] _disc;
        Vector3[] _vertices;
        Color[] _colours;
        int[] _triangles;
        int _drawn = -1;
        readonly SkyReadout _readout = new SkyReadout();
        CloudLine _headline, _left, _right;
        TextMesh _headlineLabel, _leftLabel, _rightLabel;

        /// <summary>Rate, strokes and average pace (or the summary's), for the wrist menu.</summary>
        public string Footer => _readout.Footer;

        sealed class CloudLine
        {
            public readonly CloudNumerals Cloud = new CloudNumerals();
            public Vector3 Offset;
            public float Block;
        }

        void Awake()
        {
            _motion = GetComponent<BoatMotion>();
            _frame = GetComponent<BoatFrame>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            _cloudMaterial = _scenery != null ? _scenery.CreateMaterial("Cloud Numerals") : null;
            if (font == null || textShader == null || _cloudMaterial == null)
            {
                Debug.LogWarning("[RowVR] MetricsPanel has no font, text shader or scenery shader. Run RowVR > Add Scenery.");
                enabled = false;
                return;
            }
            _cloudMaterial.SetFloat("_FogAmount", 0f);
            _cloudMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            font.RequestCharactersInTexture("0123456789:.,-/ abcdefghijklmnopqrstuvwxyz", 96);
            _textMaterial = new Material(textShader) { name = "Sky Captions", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_captions != null) Destroy(_captions.gameObject);
            if (_clouds != null) Destroy(_clouds);
        }

        // A dynamic font re-packs its atlas when it needs new glyphs; point at the new one.
        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == font && _textMaterial != null) _textMaterial.mainTexture = font.material.mainTexture;
        }

        void LateUpdate()
        {
            var head = _scenery.Head;
            var shown = _frame.HasFrame && head != null && _scenery.Near.gameObject.activeInHierarchy;
            if (_captions.gameObject.activeSelf != shown) _captions.gameObject.SetActive(shown);
            if (!shown) return;

            // Out along the boat's heading and up; facing the eyes, never rolled.
            var forward = _frame.Forward;
            var direction = Quaternion.AngleAxis(-elevation, Vector3.Cross(Vector3.up, forward)) * forward;
            var position = head.position + direction * distance;
            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            _captions.SetPositionAndRotation(position, rotation);

            _readout.Update(_motion.Session, _motion.Workout, RowerSettings.Workout.Name);
            Caption(_headlineLabel, _readout.Headline.Label);
            Caption(_leftLabel, _readout.Left.Label);
            Caption(_rightLabel, _readout.Right.Label);

            var count = 0;
            count = Fill(_headline, _readout.Headline.Value, count);
            count = Fill(_left, _readout.Left.Value, count);
            count = Fill(_right, _readout.Right.Value, count);
            Upload(count);
        }

        // Writes a line's puffs into the mesh, in the captions' space, and returns the new count.
        int Fill(CloudLine line, string text, int count)
        {
            line.Cloud.SetText(text);
            line.Cloud.Update(Time.timeAsDouble, Time.deltaTime);
            foreach (var puff in line.Cloud.Puffs)
            {
                if (count >= MaxPuffs) break;
                var centre = line.Offset + puff.Position.ToVector3() * line.Block;
                var radius = puff.Radius * line.Block;
                // Behind every body puff, a larger shaded one, a little low: together they draw
                // one shaded silhouette round the whole number, so it reads as a single cloud.
                Disc(count * 2, centre + new Vector3(0f, -0.1f * radius, line.Block), radius * 1.14f);
                Disc(count * 2 + 1, centre, radius);
                count++;
            }
            return count;
        }

        void Disc(int disc, Vector3 centre, float radius)
        {
            var first = disc * DiscVertices;
            for (var v = 0; v < DiscVertices; v++) _vertices[first + v] = centre + _disc[v] * radius;
        }

        void Upload(int count)
        {
            var vertices = count * 2 * DiscVertices;
            const UnityEngine.Rendering.MeshUpdateFlags quiet = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds;
            if (count != _drawn)
            {
                // The size changed: indices must never point past the vertices, so start clean.
                _clouds.Clear();
                _clouds.SetVertices(_vertices, 0, vertices, quiet);
                _clouds.SetColors(_colours, 0, vertices, quiet);
                _clouds.SetTriangles(_triangles, 0, count * 2 * DiscIndices, 0, false);
                _clouds.bounds = new Bounds(Vector3.zero, Vector3.one * headlineBlock * 60f);
                _drawn = count;
            }
            else if (count > 0)
            {
                _clouds.SetVertices(_vertices, 0, vertices, quiet);
            }
        }

        static void Caption(TextMesh label, string text)
        {
            if (label.text != text) label.text = text;
        }

        void Build()
        {
            var palette = _scenery.Palette;
            var light = palette != null ? palette.cloud : Color.white;
            var shade = palette != null ? palette.cloudShade : new Color(0.86f, 0.84f, 0.88f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);
            var zenith = palette != null ? palette.skyZenith : new Color(0.55f, 0.7f, 0.84f);

            // Line space, metres: +x to the rower's right, +y up, the pace cloud's middle at 0.
            var side = headlineBlock * 0.52f;
            var paceBottom = -BlockFont.Rows * headlineBlock * 0.5f;
            var sideY = paceBottom - headlineBlock * 2.2f - BlockFont.Rows * side * 0.5f;
            var sideX = headlineBlock * 8f;
            _headline = new CloudLine { Offset = Vector3.zero, Block = headlineBlock };
            _left = new CloudLine { Offset = new Vector3(-sideX, sideY, 0f), Block = side };
            _right = new CloudLine { Offset = new Vector3(sideX, sideY, 0f), Block = side };

            _captions = new GameObject("Sky Number Captions").transform;
            _captions.SetParent(_scenery.Near, false);
            // Soft ink, half lost in the sky: a pencilled note, not a sign.
            var caption = Color.Lerp(ink, zenith, 0.35f);
            var gap = headlineBlock * 0.55f;
            _headlineLabel = Text("Pace Caption", new Vector3(0f, paceBottom - gap, 0f), headlineBlock * 0.13f, caption);
            var sideBottom = sideY - BlockFont.Rows * side * 0.5f - gap;
            _leftLabel = Text("Left Caption", new Vector3(-sideX, sideBottom, 0f), headlineBlock * 0.1f, caption);
            _rightLabel = Text("Right Caption", new Vector3(sideX, sideBottom, 0f), headlineBlock * 0.1f, caption);

            BuildClouds(light, shade);
        }

        /// <summary>
        /// The mesh every puff is written into, under the captions so it moves with them. Colours
        /// and triangles repeat per puff, so they are laid out once for the most puffs there can
        /// be; each frame only the vertices change.
        /// </summary>
        void BuildClouds(Color light, Color shade)
        {
            _disc = new Vector3[DiscVertices];
            var body = new Color[DiscVertices];
            var shadow = new Color[DiscVertices];
            // The middle sits a little high, so the light gathers toward the top.
            _disc[0] = new Vector3(0f, 0.15f, 0f);
            body[0] = SkyDome.ToVertexColour(light);
            shadow[0] = SkyDome.ToVertexColour(shade);
            var underneath = Color.Lerp(light, shade, 0.45f);
            for (var s = 0; s <= Sides; s++)
            {
                var a = 2f * Mathf.PI * s / Sides;
                var y = Mathf.Sin(a);
                _disc[s + 1] = new Vector3(Mathf.Cos(a), y, 0f);
                body[s + 1] = SkyDome.ToVertexColour(Color.Lerp(underneath, light, Mathf.Clamp01(y * 0.6f + 0.6f)));
                shadow[s + 1] = shadow[0];
            }

            var discs = MaxPuffs * 2;
            _vertices = new Vector3[discs * DiscVertices];
            _colours = new Color[discs * DiscVertices];
            _triangles = new int[discs * DiscIndices];
            for (var d = 0; d < discs; d++)
            {
                var first = d * DiscVertices;
                var colours = d % 2 == 0 ? shadow : body;
                for (var v = 0; v < DiscVertices; v++) _colours[first + v] = colours[v];
                for (var s = 0; s < Sides; s++)
                {
                    var i = d * DiscIndices + s * 3;
                    _triangles[i] = first;
                    _triangles[i + 1] = first + s + 1;
                    _triangles[i + 2] = first + s + 2;
                }
            }

            _clouds = new Mesh { name = "Cloud Numerals", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _clouds.MarkDynamic();
            var go = new GameObject("Cloud Numerals");
            go.transform.SetParent(_captions, false);
            go.AddComponent<MeshFilter>().sharedMesh = _clouds;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _cloudMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        TextMesh Text(string name, Vector3 position, float size, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_captions, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.color = colour;
            go.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            return text;
        }
    }
}
