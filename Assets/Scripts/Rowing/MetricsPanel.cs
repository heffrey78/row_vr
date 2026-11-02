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
    /// in the sky. Every puff is one instance of a single painted disc, drawn with
    /// RenderMeshInstanced over a shaded silhouette of the same puffs: two draw calls, nothing
    /// rebuilt per frame.
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

        BoatMotion _motion;
        BoatFrame _frame;
        SceneryLayers _scenery;
        Material _cloudMaterial;
        Material _textMaterial;
        Mesh _puff, _shadow;
        Transform _captions;
        readonly SkyReadout _readout = new SkyReadout();
        readonly Matrix4x4[] _matrices = new Matrix4x4[MaxPuffs];
        readonly Matrix4x4[] _shadows = new Matrix4x4[MaxPuffs];
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
            _cloudMaterial.enableInstancing = true;

            font.RequestCharactersInTexture("0123456789:.,-/ abcdefghijklmnopqrstuvwxyz", 96);
            _textMaterial = new Material(textShader) { name = "Sky Captions", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_captions != null) Destroy(_captions.gameObject);
            if (_puff != null) Destroy(_puff);
            if (_shadow != null) Destroy(_shadow);
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
            count = Draw(_headline, _readout.Headline.Value, position, rotation, count);
            count = Draw(_left, _readout.Left.Value, position, rotation, count);
            count = Draw(_right, _readout.Right.Value, position, rotation, count);
            if (count == 0) return;

            var parameters = new RenderParams(_cloudMaterial)
            {
                layer = _scenery.Near.gameObject.layer,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(position, Vector3.one * headlineBlock * 60f),
            };
            Graphics.RenderMeshInstanced(parameters, _shadow, 0, _shadows, count);
            Graphics.RenderMeshInstanced(parameters, _puff, 0, _matrices, count);
        }

        int Draw(CloudLine line, string text, Vector3 position, Quaternion rotation, int count)
        {
            line.Cloud.SetText(text);
            line.Cloud.Update(Time.timeAsDouble, Time.deltaTime);
            foreach (var puff in line.Cloud.Puffs)
            {
                if (count >= MaxPuffs) break;
                var local = line.Offset + puff.Position.ToVector3() * line.Block;
                var radius = puff.Radius * line.Block;
                _matrices[count] = Matrix4x4.TRS(position + rotation * local, rotation, Vector3.one * radius);
                // Behind every body puff, a larger shaded one, a little low: together they draw
                // one shaded silhouette round the whole number, so it reads as a single cloud.
                var behind = local + new Vector3(0f, -0.1f * radius, line.Block);
                _shadows[count++] = Matrix4x4.TRS(position + rotation * behind, rotation, Vector3.one * (radius * 1.14f));
            }
            return count;
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
            _puff = PuffMesh(light, Color.Lerp(light, shade, 0.45f));
            _shadow = PuffMesh(shade, shade);

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
        }

        /// <summary>
        /// One painted puff, unit radius, facing -Z: <paramref name="light"/> in the middle and
        /// toward the top, easing to <paramref name="shade"/> underneath.
        /// </summary>
        static Mesh PuffMesh(Color light, Color shade)
        {
            const int sides = 24;
            var vertices = new Vector3[sides + 2];
            var colours = new Color[sides + 2];
            var triangles = new int[sides * 3];
            vertices[0] = new Vector3(0f, 0.15f, 0f);
            colours[0] = SkyDome.ToVertexColour(light);
            for (var s = 0; s <= sides; s++)
            {
                var a = 2f * Mathf.PI * s / sides;
                var y = Mathf.Sin(a);
                vertices[s + 1] = new Vector3(Mathf.Cos(a), y, 0f);
                colours[s + 1] = SkyDome.ToVertexColour(Color.Lerp(shade, light, Mathf.Clamp01(y * 0.6f + 0.6f)));
                if (s < sides)
                {
                    triangles[s * 3] = 0;
                    triangles[s * 3 + 1] = s + 1;
                    triangles[s * 3 + 2] = s + 2;
                }
            }
            var mesh = new Mesh { name = "Cloud Puff", vertices = vertices, colors = colours, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
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
