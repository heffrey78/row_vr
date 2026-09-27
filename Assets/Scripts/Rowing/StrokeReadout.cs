using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Makes the detector's output observable, which is the difference between "the scene
    /// runs" and "the scene works".
    ///
    /// Deliberately uses Debug.Log and IMGUI rather than a world-space canvas: logs reach
    /// the desktop through `make logs` on device and the Console in Play Mode, so the same
    /// component reports in tier 2 and tier 3 with nothing to wire up and no font, TMP or
    /// URP material to go wrong. A proper in-headset HUD is a gameplay concern; this is
    /// instrumentation.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class StrokeReadout : MonoBehaviour
    {
        [Tooltip("Log catches and finishes. On device these surface through 'make logs'.")]
        [SerializeField] bool logToConsole = true;

        [Tooltip("Draw a small IMGUI panel. Game view only - IMGUI does NOT render into the " +
                 "headset, so this is useless on device.")]
        [SerializeField] bool drawOnScreen = true;

        [Tooltip("Build a world-space panel in front of the rower. This is the only readout " +
                 "that is actually visible in the headset.")]
        [SerializeField] bool drawInWorld = true;

        [Tooltip("Where the world-space panel sits relative to the head, in metres.")]
        [SerializeField] Vector3 worldPanelOffset = new Vector3(0f, -0.2f, 1.5f);

        /// <summary>
        /// Set by a recorder on the same object while a take is running, and shown on the
        /// panel. A string rather than a reference so this assembly does not have to know
        /// the recording assembly exists.
        /// </summary>
        public string RecordingStatus { get; set; }

        /// <summary>
        /// Starts the stroke count again from zero. The count otherwise runs for as long as the
        /// app does - and on Quest the app survives the headset sleeping - so a take rowed an
        /// hour after an earlier session carried on from that session's total (12 strokes
        /// read as 23).
        /// </summary>
        public void ResetStrokeCount()
        {
            _strokes = 0;
            if (logToConsole) Debug.Log("[RowVR] stroke count reset");
        }

        RowingRig _rig;
        StrokeState _state;
        int _strokes;
        TextMesh _worldText;
        string _lastStatus;
        float _lastStatusLogTime = float.NegativeInfinity;

        void OnEnable()
        {
            _rig = GetComponent<RowingRig>();
            if (drawInWorld && _worldText == null) BuildWorldPanel();
            _rig.Tick += OnTick;
            _rig.Catch += OnCatch;
            _rig.Finish += OnFinish;
            _rig.CatchWithdrawn += OnCatchWithdrawn;
        }

        void OnDisable()
        {
            if (_rig == null) return;
            _rig.Tick -= OnTick;
            _rig.Catch -= OnCatch;
            _rig.Finish -= OnFinish;
            _rig.CatchWithdrawn -= OnCatchWithdrawn;
        }

        void OnCatchWithdrawn(StrokeMetrics metrics)
        {
            if (_strokes > 0) _strokes--;
            if (logToConsole)
                Debug.Log($"[RowVR] catch withdrawn - hands left the handle; strokes back to {_strokes}");
        }

        void OnTick(StrokeState state) => _state = state;

        /// <summary>
        /// A world-space TextMesh rather than an IMGUI panel or a UI Canvas. IMGUI never
        /// reaches the headset at all, and a Canvas needs a font asset and an event system
        /// to behave. A TextMesh with Unity's built-in font is the fewest moving parts that
        /// is actually visible in VR.
        /// </summary>
        void BuildWorldPanel()
        {
            var panel = new GameObject("Stroke Readout (world)");
            panel.transform.SetParent(transform, false);

            _worldText = panel.AddComponent<TextMesh>();
            _worldText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _worldText.fontSize = 96;
            // Small on purpose: the diagnostic line runs to ~70 characters, and at 0.012 the panel
            // was metres wide and felt pressed against your face.
            _worldText.characterSize = 0.004f;
            _worldText.anchor = TextAnchor.MiddleCenter;
            _worldText.alignment = TextAlignment.Left;
            _worldText.color = Color.white;
            _worldText.richText = true;   // RecordingStatus uses <color> to stand out.

            var renderer = panel.GetComponent<MeshRenderer>();
            if (renderer != null && _worldText.font != null)
                renderer.sharedMaterial = _worldText.font.material;
        }

        void LateUpdate()
        {
            if (_worldText == null) return;

            // Follow the head so the panel is always readable. Parented to the rig rather
            // than the camera so it does not jitter with every tracking update.
            var head = Camera.main;
            if (head != null)
            {
                _worldText.transform.position =
                    head.transform.position + head.transform.TransformVector(worldPanelOffset);
                _worldText.transform.rotation =
                    Quaternion.LookRotation(_worldText.transform.position - head.transform.position);
            }

            var tracking = _rig != null && _rig.IsTracking ? "tracking" : "NO HANDS";
            var why = _rig != null ? _rig.SourceStatus : "no rig";

            // Log changes, at most once a second. The status can carry a live counter, so
            // logging every change would be every frame; never logging would mean the reason
            // is only ever visible to whoever is wearing the headset.
            if (why != _lastStatus && Time.unscaledTime - _lastStatusLogTime >= 1f)
            {
                _lastStatus = why;
                _lastStatusLogTime = Time.unscaledTime;
                if (logToConsole) Debug.Log($"[RowVR] hand source: {why}");
            }
            _worldText.text =
                (string.IsNullOrEmpty(RecordingStatus) ? string.Empty : RecordingStatus + "\n") +
                $"phase    {_state.Phase}\n" +
                $"strokes  {_strokes}\n" +
                $"rate     {_state.Metrics.StrokesPerMinute:F1} spm\n" +
                $"handle   {_state.HandlePosition:F2} m\n" +
                $"hands    {tracking}\n" +
                $"why      {why}";
        }

        void OnCatch(StrokeMetrics metrics)
        {
            _strokes++;
            if (logToConsole)
            {
                Debug.Log($"[RowVR] catch  #{_strokes}  {metrics.StrokesPerMinute:F1} spm  " +
                          $"length {metrics.StrokeLengthMetres:F2} m");
            }
        }

        void OnFinish(StrokeMetrics metrics)
        {
            if (logToConsole)
            {
                Debug.Log($"[RowVR] finish        drive {metrics.DriveSeconds:F2}s  " +
                          $"ratio {metrics.RecoveryToDriveRatio:F2}");
            }
        }

        void OnGUI()
        {
            if (!drawOnScreen) return;

            // Tracking state is worth showing on its own line. "No strokes" and "no hands"
            // look identical from the numbers alone, and they have completely different
            // causes - see the XRHandsPoseSource note in docs/dev-loop.md.
            var tracking = _rig != null && _rig.IsTracking ? "tracking" : "NO HANDS";

            GUILayout.BeginArea(new Rect(12f, 12f, 300f, 130f), GUI.skin.box);
            GUILayout.Label($"phase     {_state.Phase}");
            GUILayout.Label($"strokes   {_strokes}");
            GUILayout.Label($"rate      {_state.Metrics.StrokesPerMinute:F1} spm");
            GUILayout.Label($"handle    {_state.HandlePosition:F2} m  ({_state.NormalizedPosition:F2})");
            GUILayout.Label($"hands     {tracking}");
            GUILayout.EndArea();
        }
    }
}
