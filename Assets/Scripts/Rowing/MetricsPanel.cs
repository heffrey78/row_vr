using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The rower's numbers (#29), as big block digits high in the sky (#99). It used to be a
    /// small hand-lettered card in the boat, which the owner found small and in the line of
    /// sight. Now pace per 500 m is the headline, with time to its lower left and distance to
    /// its lower right, each labelled, and a footer line for rate, strokes and average pace.
    ///
    /// The board sits far out along the boat's heading, above the horizon. Like the sky dome,
    /// it follows the head's position and only the boat's heading, so it holds a steady place
    /// in the sky and never swims with the stroke. What it shows comes from SkyReadout, and the
    /// block lettering from BlockFont, both engine-free and tested.
    ///
    /// Each glyph is one mesh built once; each character slot swaps between them, so changing
    /// numbers allocate nothing. Blocks are RowVR/Illustrated paper with ink; labels are Patrick
    /// Hand (SIL OFL, Assets/Fonts) drawn with RowVR/Text so they render in both eyes under
    /// single-pass stereo.
    ///
    /// It belongs to the virtual scene: when passthrough fades the room in, it fades with it.
    /// The class keeps its old name so the scenes' wiring holds without regenerating them.
    /// </summary>
    [RequireComponent(typeof(BoatMotion), typeof(BoatFrame))]
    public sealed class MetricsPanel : MonoBehaviour
    {
        [Tooltip("Patrick Hand. Set by RowVR > Add Scenery.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by RowVR > Add Scenery; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [Header("Placement")]
        [Tooltip("Distance from the eyes to the board, metres. Far enough to sit at optical infinity.")]
        [SerializeField] float distance = 160f;

        [Tooltip("Degrees above the horizon of the headline's middle.")]
        [SerializeField] float elevation = 20f;

        [Tooltip("Size of one block of the headline, metres. Five blocks make a digit's height.")]
        [SerializeField] float headlineBlock = 3.2f;

        [Tooltip("Share of the scene's fog the board takes, so it reads as far away.")]
        [Range(0f, 1f)]
        [SerializeField] float fog = 0.35f;

        const string Characters = "0123456789:.-, ";

        BoatMotion _motion;
        BoatFrame _frame;
        SceneryLayers _scenery;
        Transform _board;
        Material _textMaterial;
        readonly Dictionary<char, Mesh> _glyphs = new Dictionary<char, Mesh>();
        readonly SkyReadout _readout = new SkyReadout();
        BlockLine _headline, _left, _right;
        TextMesh _headlineLabel, _leftLabel, _rightLabel, _footer;

        void Awake()
        {
            _motion = GetComponent<BoatMotion>();
            _frame = GetComponent<BoatFrame>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (font == null || textShader == null)
            {
                Debug.LogWarning("[RowVR] MetricsPanel has no font or text shader. Run RowVR > Add Scenery.");
                enabled = false;
                return;
            }

            font.RequestCharactersInTexture("0123456789:.,-/ abcdefghijklmnopqrstuvwxyzAPMS·", 96);
            _textMaterial = new Material(textShader) { name = "Sky Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
            _board.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_board != null) Destroy(_board.gameObject);
            foreach (var mesh in _glyphs.Values) Destroy(mesh);
        }

        // A dynamic font re-packs its atlas when it needs new glyphs; point at the new one.
        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == font && _textMaterial != null) _textMaterial.mainTexture = font.material.mainTexture;
        }

        void LateUpdate()
        {
            var head = _scenery != null ? _scenery.Head : null;
            if (!_frame.HasFrame || head == null) return;

            // Out along the boat's heading and up; turned to face the eyes, never rolled.
            var forward = _frame.Forward;
            var direction = Quaternion.AngleAxis(-elevation, Vector3.Cross(Vector3.up, forward)) * forward;
            _board.SetPositionAndRotation(head.position + direction * distance, Quaternion.LookRotation(direction, Vector3.up));
            if (!_board.gameObject.activeSelf) _board.gameObject.SetActive(true);

            _readout.Update(_motion.Session, _motion.Workout, RowerSettings.Workout.Name);
            Show(_headline, _headlineLabel, _readout.Headline);
            Show(_left, _leftLabel, _readout.Left);
            Show(_right, _rightLabel, _readout.Right);
            if (_footer.text != _readout.Footer) _footer.text = _readout.Footer;
        }

        static void Show(BlockLine line, TextMesh label, ReadoutLine value)
        {
            line.SetText(value.Value);
            if (label.text != value.Label) label.text = value.Label;
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var paper = palette != null ? palette.paper : new Color(0.97f, 0.94f, 0.86f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);

            _board = new GameObject("Sky Numbers").transform;
            if (_scenery != null) _board.SetParent(_scenery.Near, false);

            var blocks = IllustratedMaterials.Create(_scenery, "Sky Number Blocks", paper, true, headlineBlock * 0.07f)
                         ?? new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = paper };
            if (blocks.HasProperty("_FogAmount")) blocks.SetFloat("_FogAmount", fog);
            // Blank glyphs (space, the thousands gap) get no mesh; their slots stay off.
            foreach (var c in Characters)
                if (HasBlocks(c)) _glyphs[c] = BuildGlyph(c);

            // Board space, metres: +x to the rower's right, +y up, the headline's middle at 0.
            // The side numbers are half the headline's size, under its outer edges.
            var side = headlineBlock * 0.5f;
            var sideRow = -BlockFont.Rows * headlineBlock * 0.5f - headlineBlock * 2.2f - BlockFont.Rows * side * 0.5f;
            var sideOffset = headlineBlock * 9f;
            _headline = new BlockLine(_board, "Headline", Vector3.zero, headlineBlock, _glyphs, blocks);
            _left = new BlockLine(_board, "Left", new Vector3(-sideOffset, sideRow, 0f), side, _glyphs, blocks);
            _right = new BlockLine(_board, "Right", new Vector3(sideOffset, sideRow, 0f), side, _glyphs, blocks);

            var labelGap = headlineBlock * 0.45f;
            _headlineLabel = Text("Headline Label", new Vector3(0f, -BlockFont.Rows * headlineBlock * 0.5f - labelGap, 0f), headlineBlock * 0.11f, ink);
            _leftLabel = Text("Left Label", new Vector3(-sideOffset, sideRow - BlockFont.Rows * side * 0.5f - labelGap, 0f), headlineBlock * 0.08f, ink);
            _rightLabel = Text("Right Label", new Vector3(sideOffset, sideRow - BlockFont.Rows * side * 0.5f - labelGap, 0f), headlineBlock * 0.08f, ink);
            _footer = Text("Footer", new Vector3(0f, sideRow - BlockFont.Rows * side * 0.5f - labelGap * 4.5f, 0f), headlineBlock * 0.08f, ink);
        }

        /// <summary>
        /// One glyph at one block per metre, its bottom-left at the origin: a box per filled
        /// cell, a little smaller than the cell and a little off-true, like stacked paper blocks.
        /// </summary>
        static Mesh BuildGlyph(char c)
        {
            var shape = new ShapeBuilder();
            var random = new System.Random(c * 7919);
            for (var row = 0; row < BlockFont.Rows; row++)
            for (var column = 0; column < BlockFont.Width(c); column++)
            {
                if (!BlockFont.Filled(c, column, row)) continue;
                var centre = new Vector3(column + 0.5f, BlockFont.Rows - row - 0.5f, 0f)
                             + new Vector3(Jitter(random, 0.04f), Jitter(random, 0.04f), Jitter(random, 0.08f));
                var size = new Vector3(0.88f + Jitter(random, 0.04f), 0.88f + Jitter(random, 0.04f), 0.6f);
                Box(shape, centre, size, Quaternion.Euler(0f, 0f, Jitter(random, 3f)), Color.white);
            }
            return shape.Build("Sky Glyph " + c);
        }

        static bool HasBlocks(char c)
        {
            for (var row = 0; row < BlockFont.Rows; row++)
                for (var column = 0; column < BlockFont.Width(c); column++)
                    if (BlockFont.Filled(c, column, row)) return true;
            return false;
        }

        static float Jitter(System.Random random, float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;

        static void Box(ShapeBuilder shape, Vector3 centre, Vector3 size, Quaternion rotation, Color colour)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            // Wound to face outward; the front (-z) is what the rower sees.
            shape.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), colour);
            shape.Quad(P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), colour);
            shape.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), colour);
            shape.Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), colour);
            shape.Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), colour);
            shape.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), colour);
        }

        TextMesh Text(string name, Vector3 position, float size, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_board, false);
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

        /// <summary>A line of block characters, centred on its origin, its middle at the origin's height.</summary>
        sealed class BlockLine
        {
            readonly Transform _root;
            readonly float _block;
            readonly IReadOnlyDictionary<char, Mesh> _glyphs;
            readonly Material _material;
            readonly List<MeshFilter> _slots = new List<MeshFilter>();
            string _text;

            public BlockLine(Transform parent, string name, Vector3 position, float block,
                             IReadOnlyDictionary<char, Mesh> glyphs, Material material)
            {
                _root = new GameObject(name).transform;
                _root.SetParent(parent, false);
                _root.localPosition = position;
                _block = block;
                _glyphs = glyphs;
                _material = material;
            }

            public void SetText(string text)
            {
                if (text == _text) return;
                _text = text;

                var x = -BlockFont.Measure(text) * 0.5f;
                for (var i = 0; i < text.Length; i++)
                {
                    var c = text[i];
                    var slot = Slot(i);
                    var drawn = _glyphs.TryGetValue(c, out var mesh);
                    slot.gameObject.SetActive(drawn);
                    if (drawn)
                    {
                        slot.sharedMesh = mesh;
                        slot.transform.localPosition = new Vector3(x, -BlockFont.Rows * 0.5f, 0f) * _block;
                    }
                    x += BlockFont.Width(c) + BlockFont.Gap;
                }
                for (var i = text.Length; i < _slots.Count; i++) _slots[i].gameObject.SetActive(false);
            }

            MeshFilter Slot(int index)
            {
                while (_slots.Count <= index)
                {
                    var go = new GameObject("Slot " + _slots.Count);
                    go.transform.SetParent(_root, false);
                    go.transform.localScale = Vector3.one * _block;
                    // Each slot leans its own way, by a hair, so repeated digits are not stamped.
                    go.transform.localRotation = Quaternion.Euler(0f, 0f, ((_slots.Count * 37) % 7 - 3) * 0.6f);
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = _material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    _slots.Add(go.AddComponent<MeshFilter>());
                }
                return _slots[index];
            }
        }
    }
}
