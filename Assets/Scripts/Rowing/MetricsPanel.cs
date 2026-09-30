using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The rower's numbers (REQ-0006-FUNC): a hand-lettered card fixed in the boat, off to the
    /// rower's right, like a monitor clipped to the rigger. It does not follow the head, so it
    /// is somewhere to glance at, not something in the way.
    ///
    /// While rowing it shows time, pace per 500 m, distance, and a line with rate, strokes
    /// and average pace. When a session ends it becomes the session's summary until the next
    /// stroke. The numbers come from BoatMotion's engine-free models; formatting is
    /// MetricsFormat, both tested.
    ///
    /// Lettering is Patrick Hand (SIL OFL, Assets/Fonts), drawn with RowVR/Text so it renders
    /// in both eyes under single-pass stereo. The card is RowVR/Illustrated paper with ink.
    /// </summary>
    [RequireComponent(typeof(BoatMotion), typeof(BoatFrame))]
    public sealed class MetricsPanel : MonoBehaviour
    {
        [Tooltip("Patrick Hand. Set by RowVR > Add Scenery.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by RowVR > Add Scenery; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [Header("Placement, in the boat (metres)")]
        [SerializeField] float towardStern = 1.15f;
        [SerializeField] float toRight = 0.62f;
        [Tooltip("Height relative to the rower's average eye height.")]
        [SerializeField] float belowEyes = 0.18f;

        const float CardWidth = 0.36f;
        const float CardHeight = 0.28f;

        BoatMotion _motion;
        BoatFrame _frame;
        RowingRig _rig;
        SceneryLayers _scenery;
        Transform _card;
        TextMesh[] _labels;
        TextMesh[] _values;
        TextMesh _footer;
        Material _textMaterial;
        float _eyeHeight = float.NaN;

        void Awake()
        {
            _motion = GetComponent<BoatMotion>();
            _frame = GetComponent<BoatFrame>();
            _rig = GetComponent<RowingRig>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (font == null || textShader == null)
            {
                Debug.LogWarning("[RowVR] MetricsPanel has no font or text shader. Run RowVR > Add Scenery.");
                enabled = false;
                return;
            }

            font.RequestCharactersInTexture("0123456789:.,-/ abcdefghijklmnopqrstuvwxyzAPMS", 96);
            _textMaterial = new Material(textShader) { name = "Panel Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
            _card.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_card != null) Destroy(_card.gameObject);
        }

        // A dynamic font re-packs its atlas when it needs new glyphs; point at the new one.
        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == font && _textMaterial != null) _textMaterial.mainTexture = font.material.mainTexture;
        }

        void LateUpdate()
        {
            if (!_frame.HasFrame) return;

            if (_rig.IsTracking)
            {
                var eye = _rig.LastSample.HeadPosition.Y;
                _eyeHeight = float.IsNaN(_eyeHeight) ? eye : Mathf.Lerp(_eyeHeight, eye, 1f - Mathf.Exp(-Time.deltaTime / 4f));
            }
            if (float.IsNaN(_eyeHeight)) return;

            // Fixed in the boat; turned once toward where the rower's eyes usually are.
            var position = _frame.Point(towardStern, toRight, 0f);
            position.y = _eyeHeight - belowEyes;
            var eyes = _frame.Point(-0.3f, 0f, 0f);
            eyes.y = _eyeHeight;
            var facing = position - eyes;
            facing.y *= 0.5f;
            _card.SetPositionAndRotation(position, Quaternion.LookRotation(facing.normalized, Vector3.up));
            if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);

            Show(_motion.Session);
        }

        void Show(SessionStats session)
        {
            if (!session.Active && session.LastSummary.HasValue)
            {
                var s = session.LastSummary.Value;
                Set(0, "session", MetricsFormat.Time(s.ElapsedSeconds));
                Set(1, "avg /500m", MetricsFormat.Split(s.AverageSplitSeconds));
                Set(2, "metres", MetricsFormat.Distance(s.DistanceMetres));
                _footer.text = $"{s.Strokes} strokes  ·  avg rate {MetricsFormat.Rate(s.AverageRate)}";
                return;
            }

            var workout = _motion.Workout;
            var time = MetricsFormat.Time(session.ElapsedSeconds);
            var distance = MetricsFormat.Distance(session.DistanceMetres);
            Set(0, "time", time);
            Set(1, "/500m", MetricsFormat.Split(session.StrokeSplitSeconds));
            Set(2, "metres", distance);

            // The chosen workout (REQ-0008) takes over the lines it counts down.
            if (session.Active && workout != null && workout.Workout.Kind != WorkoutKind.JustRow)
            {
                if (workout.Finished)
                {
                    _footer.text = "workout complete" + IntervalSummary(workout);
                    return;
                }
                switch (workout.Workout.Kind)
                {
                    case WorkoutKind.Timed:
                        Set(0, "time left", MetricsFormat.Time(workout.SecondsLeft));
                        break;
                    case WorkoutKind.Distance:
                        Set(2, "metres left", MetricsFormat.Distance(workout.MetresLeft));
                        break;
                    case WorkoutKind.Intervals:
                        Set(0, $"{(workout.Resting ? "rest" : "work")} {workout.Interval} of {workout.Workout.Repeats}",
                            MetricsFormat.Time(workout.SecondsLeft));
                        break;
                }
            }

            _footer.text = session.Active
                ? $"rate {MetricsFormat.Rate(session.Rate)}  ·  {session.Strokes} strokes  ·  avg {MetricsFormat.Split(session.AverageSplitSeconds)}"
                : $"{RowerSettings.Workout.Name}  ·  take a stroke to begin";
        }

        static string IntervalSummary(WorkoutProgress workout)
        {
            if (workout.IntervalMetres.Count == 0) return "";
            var parts = "";
            foreach (var m in workout.IntervalMetres) parts += (parts.Length > 0 ? " " : "") + MetricsFormat.Distance(m);
            return "  ·  " + parts + " m";
        }

        void Set(int row, string label, string value)
        {
            if (_labels[row].text != label) _labels[row].text = label;
            if (_values[row].text != value) _values[row].text = value;
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var paper = palette != null ? palette.paper : new Color(0.97f, 0.94f, 0.86f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);
            var soft = Color.Lerp(ink, paper, 0.45f);

            _card = new GameObject("Metrics Panel").transform;
            if (_scenery != null) _card.SetParent(_scenery.Near, false);

            // The card: a sheet of paper with an inked edge, a touch off-square as if pinned
            // up by hand. Its front faces the rower (-Z of the card faces them).
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "Card";
            Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(_card, false);
            sheet.transform.localScale = new Vector3(CardWidth, CardHeight, 0.006f);
            sheet.transform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, sheet, paper, true, 0.0025f);
            else
                sheet.GetComponent<Renderer>().material.color = paper;

            _labels = new TextMesh[3];
            _values = new TextMesh[3];
            for (var row = 0; row < 3; row++)
            {
                var y = CardHeight * 0.5f - 0.05f - row * 0.068f;
                _labels[row] = Text("Label", new Vector3(-CardWidth * 0.5f + 0.022f, y - 0.012f, -0.005f), 0.0024f, TextAnchor.LowerLeft, soft);
                _values[row] = Text("Value", new Vector3(CardWidth * 0.5f - 0.02f, y - 0.018f, -0.005f), 0.0062f, TextAnchor.LowerRight, ink);
            }
            _footer = Text("Footer", new Vector3(0f, -CardHeight * 0.5f + 0.022f, -0.005f), 0.0024f, TextAnchor.LowerCenter, soft);
        }

        TextMesh Text(string name, Vector3 position, float size, TextAnchor anchor, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_card, false);
            go.transform.localPosition = position;
            // TextMesh faces -Z; turn it to read from the rower's side of the card.
            go.transform.localRotation = Quaternion.identity;
            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = anchor;
            text.alignment = anchor == TextAnchor.LowerRight ? TextAlignment.Right
                           : anchor == TextAnchor.LowerCenter ? TextAlignment.Center : TextAlignment.Left;
            text.color = colour;
            go.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            return text;
        }
    }
}
