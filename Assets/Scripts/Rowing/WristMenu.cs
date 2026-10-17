using System;
using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RowVR.Rowing
{
    /// <summary>
    /// The wrist menu (#46, replacing #44's card): turn the left hand over, palm
    /// toward your face, and a fan of buttons opens above the palm. Poke them with the right
    /// index fingertip.
    ///
    /// Inner arc: resistance -, its value, +, and "back to the scene" while the room is
    /// showing (passthrough, #45). Outer arc: the workouts, then lake and rapids. Choices
    /// persist (RowerSettings).
    ///
    /// When it shows is WristMenuGate (engine-free, tested): a held palm-up, never while
    /// rowing. Pressing is PressDetector (tested). This only places, draws and wires them. The
    /// left hand holds the menu, so only the right hand can press it.
    /// </summary>
    [RequireComponent(typeof(RowingRig), typeof(BoatMotion))]
    public sealed class WristMenu : MonoBehaviour
    {
        [Tooltip("Patrick Hand. Set by scene setup.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by scene setup; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [Tooltip("How far above the palm the fan's centre floats, metres.")]
        [SerializeField] float abovePalm = 0.06f;

        [Tooltip("How far the fan is shifted from the palm toward the rower's right, metres. The left hand is held out to the left, so a fan centred on it puts its left buttons at the edge of hand tracking, where the right fingertip is lost.")]
        [SerializeField] float inward = 0.1f;

        /// <summary>
        /// Headless previews have no hands: when set, the menu shows in front of the head so its
        /// layout can be rendered (make preview).
        /// </summary>
        public static bool PreviewInFrontOfHead;

        const float InnerRadius = 0.085f;
        const float OuterRadius = 0.155f;
        const float ButtonWidth = 0.05f;
        const float ButtonHeight = 0.028f;

        sealed class Button
        {
            public Transform Frame;
            public Transform Face;
            public Renderer Renderer;
            public PressDetector Detector;
            public Action OnPress;
            public Func<bool> Selected;
            public Func<bool> Shown;
            public float Pushed;
        }

        RowingRig _rig;
        BoatMotion _motion;
        PassthroughFade _passthrough;
        SceneryLayers _scenery;
        Transform _menu;
        Material _textMaterial, _buttonMaterial, _selectedMaterial;
        TextMesh _resistanceValue;
        readonly List<Button> _buttons = new List<Button>();
        readonly WristMenuGate _gate = new WristMenuGate();
        AudioSource _tick;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _motion = GetComponent<BoatMotion>();
            _passthrough = GetComponent<PassthroughFade>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (font == null || textShader == null)
            {
                Debug.LogWarning("[RowVR] WristMenu has no font or text shader.");
                enabled = false;
                return;
            }
            font.RequestCharactersInTexture("0123456789:.,-+/x abcdefghijklmnopqrstuvwxyz", 96);
            _textMaterial = new Material(textShader) { name = "Wrist Menu Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;

            _tick = new GameObject("Wrist Menu Tick").AddComponent<AudioSource>();
            _tick.playOnAwake = false;
            _tick.spatialBlend = 0.6f;
            var tick = SoundSynth.Tick();
            _tick.clip = AudioClip.Create("Tick", tick.Length, 1, SoundSynth.SampleRate, false);
            _tick.clip.SetData(tick, 0);

            Build();
            // Stays readable over the room when passthrough fades in (#45).
            PassthroughFade.KeepVisible(_menu);
            PassthroughFade.KeepVisible(_buttonMaterial);
            PassthroughFade.KeepVisible(_selectedMaterial);
            _menu.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_menu != null) Destroy(_menu.gameObject);
            if (_tick != null) Destroy(_tick.gameObject);
        }

        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == font && _textMaterial != null) _textMaterial.mainTexture = font.material.mainTexture;
        }

        void LateUpdate()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;

            Vector3 centre;
            var tracked = _rig.TryGetPalmPose(true, out var palm);
            var facing = 0f;
            Vector3 eyes = head.position;
            if (PreviewInFrontOfHead)
            {
                // From the recorded head the preview camera shoots from, not the editor's XR
                // camera, so the render shows it where a rower would hold it.
                var sample = _rig.LastSample;
                eyes = sample.HeadPosition.ToVector3();
                tracked = true;
                facing = 1f;
                centre = eyes + Level(sample.HeadForward.ToVector3()) * 0.38f + Vector3.down * 0.28f;
            }
            else
            {
                // The palm faces -up in XR Hands' joint frame.
                var palmNormal = -(palm.rotation * Vector3.up);
                if (tracked) facing = Vector3.Dot(palmNormal, (head.position - palm.position).normalized);
                centre = palm.position + palmNormal * abovePalm;
                // Toward the rower's right as they see it: inside the view, within the right hand's reach.
                centre += Vector3.Cross(Vector3.up, Level(centre - eyes)) * inward;
            }

            _gate.Update(Time.timeAsDouble, tracked, facing, _rig.IsTracking ? _rig.State : default);
            var appear = PreviewInFrontOfHead ? 1f : _gate.Appear;
            var visible = appear > 0.01f;
            if (_menu.gameObject.activeSelf != visible) _menu.gameObject.SetActive(visible);
            if (!visible)
            {
                foreach (var b in _buttons) b.Detector.Lost();
                return;
            }

            if (tracked)
            {
                // Upright and facing the eyes, so it reads like a held card, whatever the wrist does.
                var toEyes = eyes - centre;
                _menu.SetPositionAndRotation(centre, Quaternion.LookRotation(-toEyes.normalized, Vector3.up));
            }
            _menu.localScale = Vector3.one * Mathf.SmoothStep(0.5f, 1f, appear);

            if (appear > 0.95f) Press();
            Refresh();
        }

        static Vector3 Level(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        void Press()
        {
            var hasFinger = _rig.TryGetFingertip(false, out var finger);
            var t = Time.timeAsDouble;
            foreach (var button in _buttons)
            {
                var shown = button.Shown == null || button.Shown();
                if (!hasFinger || !shown)
                {
                    button.Detector.Lost();
                    continue;
                }
                // Button frame: X, Y across its face, Z in front (the menu's -Z faces the rower).
                var local = button.Frame.InverseTransformPoint(finger);
                if (button.Detector.Update(t, new Vec3(local.x, local.y, -local.z)))
                {
                    button.OnPress();
                    button.Pushed = 1f;
                    _tick.transform.position = button.Frame.position;
                    _tick.Play();
                }
            }
        }

        void Refresh()
        {
            foreach (var b in _buttons)
            {
                var shown = b.Shown == null || b.Shown();
                if (b.Frame.gameObject.activeSelf != shown) b.Frame.gameObject.SetActive(shown);
                b.Pushed = Mathf.MoveTowards(b.Pushed, b.Detector.Down ? 1f : 0f, Time.deltaTime * 8f);
                b.Face.localPosition = new Vector3(0f, 0f, 0.006f + 0.006f * b.Pushed);
                var material = b.Selected != null && b.Selected() ? _selectedMaterial : _buttonMaterial;
                if (b.Renderer.sharedMaterial != material) b.Renderer.sharedMaterial = material;
            }
            var r = "res " + _motion.Resistance;
            if (_resistanceValue.text != r) _resistanceValue.text = r;
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var paper = palette != null ? palette.paper : new Color(0.97f, 0.94f, 0.86f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);
            var buttonColour = Color.Lerp(paper, palette != null ? palette.waterLight : Color.cyan, 0.35f);
            var selectedColour = palette != null ? palette.blade : new Color(0.84f, 0.36f, 0.26f);

            _buttonMaterial = IllustratedMaterials.Create(_scenery, "Wrist Button", buttonColour, true, 0.0015f)
                              ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = buttonColour };
            _selectedMaterial = IllustratedMaterials.Create(_scenery, "Wrist Button Selected", selectedColour, true, 0.0015f)
                                ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = selectedColour };

            _menu = new GameObject("Wrist Menu").transform;
            if (_scenery != null) _menu.SetParent(_scenery.Near, false);

            // A paper half-disc behind the fan, inked at the edge.
            var fan = new ShapeBuilder();
            const int segments = 24;
            var radius = OuterRadius + 0.04f;
            for (var s = 0; s < segments; s++)
            {
                var a0 = Mathf.PI * s / segments;
                var a1 = Mathf.PI * (s + 1) / segments;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius - 0.03f, 0.012f);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius - 0.03f, 0.012f);
                var c = new Vector3(0f, -0.03f, 0.012f);
                fan.Quad(c, c, p1, p0, paper);
            }
            var backing = new GameObject("Backing");
            backing.transform.SetParent(_menu, false);
            backing.AddComponent<MeshFilter>().sharedMesh = fan.Build("Wrist Menu Backing");
            var backingRenderer = backing.AddComponent<MeshRenderer>();
            var backingMaterial = IllustratedMaterials.Create(_scenery, "Wrist Backing", Color.white, true, 0.002f, vertexColour: true)
                                  ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = paper };
            backingMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            backingRenderer.sharedMaterial = backingMaterial;

            // Inner arc.
            Vector3 Arc(float radiusAt, float degrees) =>
                new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad) * radiusAt, Mathf.Sin(degrees * Mathf.Deg2Rad) * radiusAt - 0.03f, 0f);
            AddButton("-", Arc(InnerRadius, 155f), ink, () => SetResistance(_motion.Resistance - 1));
            _resistanceValue = Text("res 5", Arc(InnerRadius, 122f) + new Vector3(0f, 0f, -0.004f), 0.0022f, ink);
            AddButton("+", Arc(InnerRadius, 90f), ink, () => SetResistance(_motion.Resistance + 1));
            AddButton("back to scene", Arc(InnerRadius, 40f), ink, () => _passthrough?.Dismiss(),
                      shown: () => _passthrough != null && _passthrough.Fade > 0.05f, width: 0.075f);

            // Outer arc: workouts, then scenes.
            var presets = Workout.Presets;
            var labels = new List<(string Label, Action Press, Func<bool> Selected)>();
            for (var i = 0; i < presets.Count; i++)
            {
                var index = i;
                labels.Add((presets[i].Name, () => RowerSettings.WorkoutIndex = index, () => RowerSettings.WorkoutIndex == index));
            }
            var current = SceneManager.GetActiveScene().name;
            labels.Add(("lake", () => LoadScene(RowerSettings.LakeScene), () => current == RowerSettings.LakeScene));
            labels.Add(("rapids", () => LoadScene(RowerSettings.RapidsScene), () => current == RowerSettings.RapidsScene));
            for (var i = 0; i < labels.Count; i++)
            {
                var degrees = Mathf.Lerp(168f, 12f, (float)i / (labels.Count - 1));
                AddButton(labels[i].Label, Arc(OuterRadius, degrees), ink, labels[i].Press, labels[i].Selected);
            }
        }

        void SetResistance(int value)
        {
            _motion.Resistance = value;
            RowerSettings.Resistance = _motion.Resistance;
        }

        static void LoadScene(string name)
        {
            if (SceneManager.GetActiveScene().name == name) return;
            SceneManager.LoadScene(name);
        }

        void AddButton(string label, Vector3 position, Color ink, Action onPress,
                       Func<bool> selected = null, Func<bool> shown = null, float width = ButtonWidth)
        {
            var frame = new GameObject("Button " + label).transform;
            frame.SetParent(_menu, false);
            frame.localPosition = position + new Vector3(0f, 0f, -0.012f);

            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Face";
            Destroy(face.GetComponent<Collider>());
            face.transform.SetParent(frame, false);
            face.transform.localScale = new Vector3(width, ButtonHeight, 0.012f);
            var renderer = face.GetComponent<Renderer>();
            renderer.sharedMaterial = _buttonMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var text = Text(label, Vector3.zero, label.Length > 6 ? 0.0014f : 0.0019f, ink);
            text.transform.SetParent(face.transform, false);
            text.transform.localPosition = new Vector3(0f, 0f, -0.52f);
            text.transform.localScale = new Vector3(1f / width, 1f / ButtonHeight, 1f / 0.012f);

            _buttons.Add(new Button
            {
                Frame = frame,
                Face = face.transform,
                Renderer = renderer,
                // A little more forgiving than the face: fingertip tracking wobbles a few mm.
                Detector = new PressDetector { HalfWidth = width * 0.5f + 0.004f, HalfHeight = ButtonHeight * 0.5f + 0.004f, PressDepth = 0.01f },
                OnPress = onPress,
                Selected = selected,
                Shown = shown,
            });
        }

        TextMesh Text(string content, Vector3 position, float size, Color colour)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(_menu, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            text.text = content;
            text.font = font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = colour;
            go.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            return text;
        }
    }
}
