using System;
using System.Collections.Generic;
using RowVR.Stroke;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RowVR.Rowing
{
    /// <summary>
    /// The hands-only menu (REQ-0008, ADR-0013): a card low and in front of the rower, within
    /// reach once they let go of the handle, with the workout, the machine's resistance dial,
    /// and the scene.
    ///
    /// It is only live while the rower is resting: it appears after a few seconds without a
    /// stroke and vanishes, inert, at the first stroke, so rowing can never press it. Buttons
    /// are pressed by pushing an index fingertip into them from the front (PressDetector,
    /// engine-free and tested), with a visible push and a soft tick. Choices persist
    /// (RowerSettings). Choosing the other scene loads it; both scenes carry this card.
    /// </summary>
    [RequireComponent(typeof(RowingRig), typeof(BoatFrame), typeof(BoatMotion))]
    public sealed class MenuCard : MonoBehaviour
    {
        [Tooltip("Patrick Hand. Set by scene setup.")]
        [SerializeField] Font font;

        [Tooltip("RowVR/Text. Set by scene setup; a scene reference keeps it in the build.")]
        [SerializeField] Shader textShader;

        [Tooltip("Seconds without a stroke before the menu appears.")]
        [SerializeField] float restBeforeShowing = 3f;

        [Header("Placement, in the boat (metres)")]
        [SerializeField] float ahead = 0.5f;
        [SerializeField] float toRight = -0.12f;
        [Tooltip("Below the rower's average eye height: chest height, in reach.")]
        [SerializeField] float belowEyes = 0.5f;

        const float CardWidth = 0.46f;
        const float CardHeight = 0.3f;
        const float ButtonWidth = 0.095f;
        const float ButtonHeight = 0.05f;

        sealed class Button
        {
            public Transform Frame;      // unscaled, at the button's face, +Z toward the card
            public Transform Face;       // the visible block, pushed in on press
            public Renderer Renderer;
            public TextMesh Label;
            public PressDetector Detector;
            public Action OnPress;
            public Func<bool> Selected;
            public float Pushed;
        }

        RowingRig _rig;
        BoatFrame _frame;
        BoatMotion _motion;
        SceneryLayers _scenery;
        Transform _card;
        Material _textMaterial, _buttonMaterial, _selectedMaterial;
        TextMesh _resistanceValue;
        readonly List<Button> _buttons = new List<Button>();
        AudioSource _tick;
        float _restingFor;
        float _shown;
        float _eyeHeight = float.NaN;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _frame = GetComponent<BoatFrame>();
            _motion = GetComponent<BoatMotion>();
            _scenery = FindFirstObjectByType<SceneryLayers>();
            if (font == null || textShader == null)
            {
                Debug.LogWarning("[RowVR] MenuCard has no font or text shader.");
                enabled = false;
                return;
            }
            font.RequestCharactersInTexture("0123456789:.,-+/x abcdefghijklmnopqrstuvwxyz", 96);
            _textMaterial = new Material(textShader) { name = "Menu Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;

            _tick = new GameObject("Menu Tick").AddComponent<AudioSource>();
            _tick.playOnAwake = false;
            _tick.spatialBlend = 0.6f;
            var tick = SoundSynth.Tick();
            _tick.clip = AudioClip.Create("Tick", tick.Length, 1, SoundSynth.SampleRate, false);
            _tick.clip.SetData(tick, 0);

            Build();
            // Stays readable over the room when passthrough fades in (ADR-0014).
            PassthroughFade.KeepVisible(_card);
            PassthroughFade.KeepVisible(_buttonMaterial);
            PassthroughFade.KeepVisible(_selectedMaterial);
            _card.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_card != null) Destroy(_card.gameObject);
            if (_tick != null) Destroy(_tick.gameObject);
        }

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

            // Live only while resting: no stroke for a few seconds. A stroke hides it at once.
            var rowing = _rig.IsTracking && _rig.State.Phase != StrokePhase.Unknown;
            _restingFor = rowing ? 0f : _restingFor + Time.deltaTime;
            var live = _restingFor >= restBeforeShowing;
            _shown = rowing ? 0f : Mathf.MoveTowards(_shown, live ? 1f : 0f, Time.deltaTime * 3f);

            var visible = _shown > 0.01f;
            if (_card.gameObject.activeSelf != visible) _card.gameObject.SetActive(visible);
            if (!visible)
            {
                foreach (var b in _buttons) b.Detector.Lost();
                return;
            }

            var position = _frame.Point(ahead, toRight, 0f);
            position.y = _eyeHeight - belowEyes;
            var eyes = _frame.Point(-0.3f, 0f, 0f);
            eyes.y = _eyeHeight;
            _card.SetPositionAndRotation(position, Quaternion.LookRotation((position - eyes).normalized, Vector3.up));
            _card.localScale = Vector3.one * Mathf.SmoothStep(0.6f, 1f, _shown);

            if (live && _shown > 0.95f) Press();
            Refresh();
        }

        void Press()
        {
            var fingers = new List<Vector3>(2);
            if (_rig.TryGetFingertip(true, out var left)) fingers.Add(left);
            if (_rig.TryGetFingertip(false, out var right)) fingers.Add(right);
            var t = Time.timeAsDouble;

            foreach (var button in _buttons)
            {
                if (fingers.Count == 0)
                {
                    button.Detector.Lost();
                    continue;
                }
                // The nearest fingertip, in the button's frame: X, Y across its face, Z in
                // front of it (the card's -Z faces the rower, so flip it).
                var best = Vector3.positiveInfinity;
                foreach (var f in fingers)
                {
                    var local = button.Frame.InverseTransformPoint(f);
                    if (local.sqrMagnitude < best.sqrMagnitude) best = local;
                }
                if (button.Detector.Update(t, new Vec3(best.x, best.y, -best.z)))
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
                b.Pushed = Mathf.MoveTowards(b.Pushed, b.Detector.Down ? 1f : 0f, Time.deltaTime * 8f);
                b.Face.localPosition = new Vector3(0f, 0f, 0.008f + 0.008f * b.Pushed);
                var material = b.Selected != null && b.Selected() ? _selectedMaterial : _buttonMaterial;
                if (b.Renderer.sharedMaterial != material) b.Renderer.sharedMaterial = material;
            }
            var r = _motion.Resistance.ToString();
            if (_resistanceValue.text != r) _resistanceValue.text = r;
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var paper = palette != null ? palette.paper : new Color(0.97f, 0.94f, 0.86f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);
            var soft = Color.Lerp(ink, paper, 0.45f);
            var buttonColour = Color.Lerp(paper, palette != null ? palette.waterLight : Color.cyan, 0.35f);
            var selectedColour = palette != null ? palette.blade : new Color(0.84f, 0.36f, 0.26f);

            _buttonMaterial = IllustratedMaterials.Create(_scenery, "Menu Button", buttonColour, true, 0.002f)
                              ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = buttonColour };
            _selectedMaterial = IllustratedMaterials.Create(_scenery, "Menu Button Selected", selectedColour, true, 0.002f)
                                ?? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = selectedColour };

            _card = new GameObject("Menu Card").transform;
            if (_scenery != null) _card.SetParent(_scenery.Near, false);

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "Card";
            Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(_card, false);
            sheet.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            sheet.transform.localScale = new Vector3(CardWidth, CardHeight, 0.006f);
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, sheet, paper, true, 0.0025f);
            else
                sheet.GetComponent<Renderer>().material.color = paper;

            var top = CardHeight * 0.5f;
            Text("let go of the handle, then poke to choose", new Vector3(CardWidth * 0.5f - 0.02f, top - 0.035f, -0.004f),
                 0.0017f, TextAnchor.MiddleRight, soft);

            // Workouts.
            var y = top - 0.09f;
            Text("workout", new Vector3(-CardWidth * 0.5f + 0.02f, y + 0.042f, -0.004f), 0.0019f, TextAnchor.MiddleLeft, soft);
            var presets = Workout.Presets;
            for (var i = 0; i < presets.Count; i++)
            {
                var index = i;
                var x = -CardWidth * 0.5f + 0.02f + ButtonWidth * 0.5f + i * (ButtonWidth + 0.012f);
                AddButton(presets[i].Name, new Vector3(x, y, 0f), ink,
                          () => RowerSettings.WorkoutIndex = index, () => RowerSettings.WorkoutIndex == index);
            }

            // Resistance on the left, scene on the right, each laid out explicitly so the two
            // groups can never overlap on the 46 cm card.
            y -= 0.085f;
            const float small = 0.05f;
            Text("resistance", new Vector3(-0.21f, y + 0.042f, -0.004f), 0.0019f, TextAnchor.MiddleLeft, soft);
            AddButton("-", new Vector3(-0.185f, y, 0f), ink, () => SetResistance(_motion.Resistance - 1), null, small);
            _resistanceValue = Text("5", new Vector3(-0.13f, y, -0.004f), 0.0045f, TextAnchor.MiddleCenter, ink);
            AddButton("+", new Vector3(-0.075f, y, 0f), ink, () => SetResistance(_motion.Resistance + 1), null, small);

            Text("scene", new Vector3(0.0225f, y + 0.042f, -0.004f), 0.0019f, TextAnchor.MiddleLeft, soft);
            var current = SceneManager.GetActiveScene().name;
            AddButton("lake", new Vector3(0.07f, y, 0f), ink, () => LoadScene(RowerSettings.LakeScene),
                      () => current == RowerSettings.LakeScene);
            AddButton("rapids", new Vector3(0.175f, y, 0f), ink, () => LoadScene(RowerSettings.RapidsScene),
                      () => current == RowerSettings.RapidsScene);
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

        void AddButton(string label, Vector3 position, Color ink, Action onPress, Func<bool> selected, float width = ButtonWidth)
        {
            var frame = new GameObject("Button " + label).transform;
            frame.SetParent(_card, false);
            // The frame sits at the button's front face (toward the rower, -Z of the card).
            frame.localPosition = position + new Vector3(0f, 0f, -0.018f);

            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Face";
            Destroy(face.GetComponent<Collider>());
            face.transform.SetParent(frame, false);
            face.transform.localScale = new Vector3(width, ButtonHeight, 0.016f);
            var renderer = face.GetComponent<Renderer>();
            renderer.sharedMaterial = _buttonMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var text = Text(label, Vector3.zero, label.Length > 4 ? 0.0021f : 0.003f, TextAnchor.MiddleCenter, ink);
            text.transform.SetParent(face.transform, false);
            text.transform.localPosition = new Vector3(0f, 0f, -0.52f);
            text.transform.localScale = new Vector3(1f / width, 1f / ButtonHeight, 1f / 0.016f);

            _buttons.Add(new Button
            {
                Frame = frame,
                Face = face.transform,
                Renderer = renderer,
                Label = text,
                Detector = new PressDetector { HalfWidth = width * 0.5f, HalfHeight = ButtonHeight * 0.5f },
                OnPress = onPress,
                Selected = selected,
            });
        }

        TextMesh Text(string content, Vector3 position, float size, TextAnchor anchor, Color colour)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(_card, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            text.text = content;
            text.font = font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = colour;
            go.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            return text;
        }
    }
}
