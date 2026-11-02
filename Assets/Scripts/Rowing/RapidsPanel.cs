using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The rapids card (#30): which rapid, how much of it spent on the line, the run's
    /// time, and power against the target, the one thing the rower can do about the line.
    /// Between rapids it shows the last rapid's result; at the end, the run's summary.
    ///
    /// The same hand-lettered paper card as the lake's MetricsPanel, fixed in the raft ahead
    /// and to the right, since the rower faces downstream here.
    /// </summary>
    [RequireComponent(typeof(RapidsMotion), typeof(BoatFrame))]
    public sealed class RapidsPanel : MonoBehaviour
    {
        [Tooltip("Patrick Hand. Set by the rapids scene setup.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by the rapids scene setup; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [SerializeField] float ahead = 1.25f;
        [SerializeField] float toRight = 0.6f;
        [SerializeField] float belowEyes = 0.2f;

        const float CardWidth = 0.38f;
        const float CardHeight = 0.29f;

        RapidsMotion _rapids;
        BoatFrame _frame;
        RowingRig _rig;
        SceneryLayers _scenery;
        Transform _card;
        TextMesh[] _labels, _values;
        TextMesh _footer;
        Material _textMaterial;
        float _eyeHeight = float.NaN;

        void Awake()
        {
            _rapids = GetComponent<RapidsMotion>();
            _frame = GetComponent<BoatFrame>();
            _rig = GetComponent<RowingRig>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (font == null || textShader == null)
            {
                Debug.LogWarning("[RowVR] RapidsPanel has no font or text shader.");
                enabled = false;
                return;
            }
            font.RequestCharactersInTexture("0123456789:.,-/%W abcdefghijklmnopqrstuvwxyz", 96);
            _textMaterial = new Material(textShader) { name = "Rapids Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
            // Stays readable over the room when passthrough fades in (#45).
            PassthroughFade.KeepVisible(_card);
            _card.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_card != null) Destroy(_card.gameObject);
        }

        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == font && _textMaterial != null) _textMaterial.mainTexture = font.material.mainTexture;
        }

        void LateUpdate()
        {
            if (!_frame.HasFrame) return;
            if (_rig != null && _rig.IsTracking)
            {
                var eye = _rig.LastSample.HeadPosition.Y;
                _eyeHeight = float.IsNaN(_eyeHeight) ? eye : Mathf.Lerp(_eyeHeight, eye, 1f - Mathf.Exp(-Time.deltaTime / 4f));
            }
            if (float.IsNaN(_eyeHeight)) return;

            var position = _frame.Point(ahead, toRight, 0f);
            position.y = _eyeHeight - belowEyes;
            var eyes = _frame.Point(-0.3f, 0f, 0f);
            eyes.y = _eyeHeight;
            var facing = position - eyes;
            facing.y *= 0.5f;
            _card.SetPositionAndRotation(position, Quaternion.LookRotation(facing.normalized, Vector3.up));
            if (!_card.gameObject.activeSelf) _card.gameObject.SetActive(true);

            Show(_rapids.Run);
        }

        void Show(RapidsRun run)
        {
            if (run.Finished)
            {
                Set(0, "run", MetricsFormat.Time(run.Elapsed));
                var clean = "";
                foreach (var r in run.Results) clean += (clean.Length > 0 ? "  " : "") + Percent(r.Clean);
                Set(1, "on the line", clean);
                Set(2, "metres", MetricsFormat.Distance(run.TotalMetres));
                _footer.text = "run complete";
                return;
            }

            var where = !run.Started ? "top of the river"
                      : run.InRapid ? $"rapid {run.RapidNumber} of {run.RapidCount}"
                      : run.RapidNumber == 0 ? "calm water" : "pool";
            Set(0, where, MetricsFormat.Time(run.Elapsed));

            if (run.InRapid)
                Set(1, "on the line", Percent(run.CurrentClean));
            else if (run.Results.Count > 0)
                Set(1, "last rapid", Percent(run.Results[run.Results.Count - 1].Clean));
            else
                Set(1, "on the line", "-");

            var target = run.InRapid ? $" / {Mathf.RoundToInt(run.Section.TargetWatts)}" : "";
            Set(2, run.InRapid ? "power / target" : "power", $"{Mathf.RoundToInt(run.AveragePower)} W{target}");

            var toGo = run.TotalMetres - run.Distance;
            _footer.text = run.InRapid && !run.OnLine ? "rough water - pull harder" : $"{MetricsFormat.Distance(toGo)} m to go";
        }

        static string Percent(float fraction) => Mathf.RoundToInt(fraction * 100f) + "%";

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

            _card = new GameObject("Rapids Panel").transform;
            if (_scenery != null) _card.SetParent(_scenery.Near, false);

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "Card";
            Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(_card, false);
            sheet.transform.localScale = new Vector3(CardWidth, CardHeight, 0.006f);
            sheet.transform.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, sheet, paper, true, 0.0025f);
            else
                sheet.GetComponent<Renderer>().material.color = paper;

            _labels = new TextMesh[3];
            _values = new TextMesh[3];
            for (var row = 0; row < 3; row++)
            {
                var y = CardHeight * 0.5f - 0.05f - row * 0.068f;
                _labels[row] = Text(new Vector3(-CardWidth * 0.5f + 0.022f, y - 0.012f, -0.005f), 0.0024f, TextAnchor.LowerLeft, soft);
                _values[row] = Text(new Vector3(CardWidth * 0.5f - 0.02f, y - 0.018f, -0.005f), 0.0056f, TextAnchor.LowerRight, ink);
            }
            _footer = Text(new Vector3(0f, -CardHeight * 0.5f + 0.022f, -0.005f), 0.0024f, TextAnchor.LowerCenter, soft);
        }

        TextMesh Text(Vector3 position, float size, TextAnchor anchor, Color colour)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(_card, false);
            go.transform.localPosition = position;
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
