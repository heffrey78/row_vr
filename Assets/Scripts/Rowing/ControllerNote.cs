using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace RowVR.Rowing
{
    /// <summary>
    /// Tells a rower holding the Quest's controllers what is going on (#103). Picking them up
    /// stops hand tracking, the app reads that as letting go and shows the room (#28), and
    /// nothing said why or how to get back (owner, 2026-11-02: "not a great UX"). The wrist
    /// menu's "back to scene" needs hands, so it could not help either.
    ///
    /// While a controller is tracked, a hand-lettered note floats in front of the rower, drawn
    /// over the room: set the controllers down, take the handle and row. It follows the head's
    /// turn slowly, never its tilt, so it can be read without chasing it.
    ///
    /// Added at runtime by WristMenu, which hands over its lettering, so the scenes need no
    /// regeneration.
    /// </summary>
    public sealed class ControllerNote : MonoBehaviour
    {
        const float Distance = 1.1f;
        const float BelowEyes = 0.12f;
        const float FollowSeconds = 0.6f;
        const float ShowAfterSeconds = 0.4f;

        static readonly List<InputDevice> Devices = new List<InputDevice>();

        /// <summary>Editor preview only (make preview NOTE=1): show the note with no controllers.</summary>
        public static bool PreviewShown;

        Font _font;
        SceneryLayers _scenery;
        Transform _note;
        Material _textMaterial;
        float _heldFor;
        float _yaw = float.NaN;

        /// <summary>Builds the note in Patrick Hand on RowVR/Text.</summary>
        public void Init(Font font, Shader textShader)
        {
            _font = font;
            _scenery = FindFirstObjectByType<SceneryLayers>();
            _textMaterial = new Material(textShader) { name = "Controller Note Lettering", mainTexture = font.material.mainTexture };
            Font.textureRebuilt += OnFontRebuilt;
            Build();
            _note.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            Font.textureRebuilt -= OnFontRebuilt;
            if (_note != null) Destroy(_note.gameObject);
        }

        void OnFontRebuilt(Font rebuilt)
        {
            if (rebuilt == _font && _textMaterial != null) _textMaterial.mainTexture = _font.material.mainTexture;
        }

        void LateUpdate()
        {
            if (_note == null) return;
            // A moment's grace, so a controller waking briefly does not flash the note.
            _heldFor = PreviewShown || ControllerHeld() ? _heldFor + Time.deltaTime : 0f;
            var show = _heldFor > ShowAfterSeconds;
            if (_note.gameObject.activeSelf != show)
            {
                _note.gameObject.SetActive(show);
                if (show) _yaw = float.NaN;
                Debug.Log(show ? "[RowVR] controllers in hand: showing the note" : "[RowVR] controllers down");
            }

            var head = _scenery != null ? _scenery.Head : Camera.main != null ? Camera.main.transform : null;
            if (!show || head == null) return;

            // Level, and easing toward where the head points, so it settles in view.
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = _note.forward;
            var target = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            _yaw = float.IsNaN(_yaw) ? target
                 : Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-Time.deltaTime / FollowSeconds));
            var facing = Quaternion.Euler(0f, _yaw, 0f);
            _note.SetPositionAndRotation(head.position + facing * new Vector3(0f, -BelowEyes, Distance), facing);
        }

        static bool ControllerHeld()
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, Devices);
            foreach (var device in Devices)
            {
                // Quest reports a controller set down as untracked; only a held one counts.
                if (!device.isValid) continue;
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && tracked) return true;
            }
            return false;
        }

        void Build()
        {
            var palette = _scenery != null ? _scenery.Palette : null;
            var paper = palette != null ? palette.paper : new Color(0.97f, 0.94f, 0.86f);
            var ink = palette != null ? palette.ink : new Color(0.17f, 0.14f, 0.16f);

            _note = new GameObject("Controller Note").transform;

            var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sheet.name = "Card";
            Destroy(sheet.GetComponent<Collider>());
            sheet.transform.SetParent(_note, false);
            sheet.transform.localScale = new Vector3(0.5f, 0.22f, 0.006f);
            sheet.transform.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
            if (_scenery != null && _scenery.IllustratedShader != null)
                IllustratedMaterials.Apply(_scenery, sheet, paper, true, 0.003f);
            else
                sheet.GetComponent<Renderer>().material.color = paper;

            Text("this is rowed with your hands", new Vector3(0f, 0.068f, -0.005f), 0.004f, ink);
            Text("set the controllers down,\ntake the handle and row\nto get back on the water", new Vector3(0f, -0.025f, -0.005f), 0.0032f, ink);

            // Drawn over the room: this is exactly when the room is showing.
            PassthroughFade.KeepVisible(_note);
        }

        void Text(string words, Vector3 position, float size, Color colour)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(_note, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = colour;
            text.text = words;
            go.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
        }
    }
}
