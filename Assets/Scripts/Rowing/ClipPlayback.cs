using System.IO;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Replays a recorded .rowclip through the rig in Play Mode, so tier 2 runs on real Quest
    /// hand tracking instead of hands driven from the keyboard. Keyboard motion is too jerky
    /// to read as a stroke; a recording from the rowing machine is exactly what the detector
    /// will see on device.
    ///
    /// Editor only. In a player build it disables itself, so leaving it in the scene cannot
    /// replace live tracking on the headset. Untick the component to use the XR Interaction
    /// Simulator instead.
    ///
    /// Each pass through the clip resets the detector and the readout's stroke count, so the
    /// count at the end of a pass can be checked against the count in the clip's note.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class ClipPlayback : MonoBehaviour
    {
        [Tooltip("Path to a .rowclip, relative to the repository root.")]
        [SerializeField] string clipPath =
            "tests/RowVR.Stroke.Tests/fixtures/real-erg-steady-12strokes-20260927.rowclip";

        [Tooltip("Start the clip again when it ends.")]
        [SerializeField] bool loop = true;

        [Tooltip("Draw the recorded palms, so the motion can be seen as well as the numbers.")]
        [SerializeField] bool showHands = true;

        [Tooltip("Report no pose where the recording lost tracking, as the headset does, " +
                 "instead of gliding across the gap. Recordings sample every ~14 ms.")]
        [SerializeField] bool keepTrackingGaps = true;

        /// <summary>A gap between samples longer than this, seconds, counts as lost tracking.</summary>
        const double TrackingGapSeconds = 0.1;

        RowingRig _rig;
        StrokeReadout _readout;
        HandClip _clip;
        Timeline _timeline;
        string _name;
        int _pass;
        Transform _leftMarker, _rightMarker;

        void Start()
        {
            if (!Application.isEditor)
            {
                enabled = false;
                return;
            }

            // Fixtures live outside Assets/, beside the tests that own them, so resolve from
            // the repository root rather than asking Unity to import them.
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", clipPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[RowVR] playback: no clip at {path}. Live tracking stays in use.");
                enabled = false;
                return;
            }

            _clip = HandClip.Load(path);
            if (_clip.Samples.Count < 2)
            {
                Debug.LogError($"[RowVR] playback: {clipPath} has fewer than two samples.");
                enabled = false;
                return;
            }

            _rig = GetComponent<RowingRig>();
            _readout = GetComponent<StrokeReadout>();
            _name = Path.GetFileNameWithoutExtension(path);
            var source = new ClipHandPoseSource(_clip);
            if (keepTrackingGaps) source.MaxGapSeconds = TrackingGapSeconds;
            _timeline = new Timeline(source);

            if (showHands)
            {
                _leftMarker = CreateMarker("Playback Left Palm");
                _rightMarker = CreateMarker("Playback Right Palm");
            }

            Debug.Log($"[RowVR] playback: {_name}, {_timeline.Duration:F1}s. Note: {_clip.Note}");
            StartPass();
        }

        void Update()
        {
            if (_timeline.ClipTime(Time.timeAsDouble) <= _timeline.EndTime) return;

            if (loop)
            {
                StartPass();
            }
            else
            {
                Debug.Log($"[RowVR] playback: {_name} finished.");
                enabled = false;
            }
        }

        void LateUpdate()
        {
            if (_leftMarker == null || !_rig.IsTracking) return;
            _leftMarker.position = _rig.LastSample.LeftHand.ToVector3();
            _rightMarker.position = _rig.LastSample.RightHand.ToVector3();
        }

        void StartPass()
        {
            _pass++;
            _timeline.StartAt(Time.timeAsDouble);
            // SetSource resets the detector, which the backward jump in clip time needs.
            _rig.SetSource(_timeline, $"replaying {_name}, pass {_pass}");
            if (_readout != null) _readout.ResetStrokeCount();
        }

        static Transform CreateMarker(string name)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.localScale = Vector3.one * 0.08f;
            Destroy(marker.GetComponent<Collider>());
            return marker.transform;
        }

        /// <summary>
        /// Maps Play Mode time onto the clip's own timeline, which starts wherever the
        /// recording started.
        /// </summary>
        sealed class Timeline : IHandPoseSource
        {
            readonly ClipHandPoseSource _clip;
            double _offset;

            public Timeline(ClipHandPoseSource clip) => _clip = clip;

            public double EndTime => _clip.EndTime;
            public double Duration => _clip.EndTime - _clip.StartTime;

            public void StartAt(double now)
            {
                _offset = _clip.StartTime - now;
                _clip.Rewind();
            }

            public double ClipTime(double now) => now + _offset;

            public bool TryGetPose(double timeSeconds, out HandSample sample) =>
                _clip.TryGetPose(ClipTime(timeSeconds), out sample);
        }
    }
}
