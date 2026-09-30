using System;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// Drives <see cref="StrokeDetector"/> from whichever pose source is attached and raises
    /// events the rest of the game can hang audio, haptics and boat physics off.
    ///
    /// The source is an interface on purpose: the same component runs against live hand
    /// tracking in the headset and against a recorded clip in the Editor, so tier 2 of the
    /// dev loop can iterate on feel without a headset. See docs/dev-loop.md.
    /// </summary>
    public sealed class RowingRig : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("The XR Origin transform. Hand joints are reported in tracking space and need this to reach world space.")]
        [SerializeField] Transform trackingOrigin;

        [Tooltip("The head/camera transform. Establishes the drive axis and the seat reference.")]
        [SerializeField] Transform head;

        [Header("Tuning")]
        [Tooltip("Leave empty to use the defaults, which were tuned against recorded motion.")]
        [SerializeField] bool overrideDefaults;
        [SerializeField] float velocityThreshold = 0.15f;
        [SerializeField] float minStrokeLengthMetres = 0.25f;

        /// <summary>Raised on the frame the blades enter the water.</summary>
        public event Action<StrokeMetrics> Catch;

        /// <summary>Raised on the frame the drive ends.</summary>
        public event Action<StrokeMetrics> Finish;

        /// <summary>
        /// Raised when the most recent catch is taken back: the hands left the handle right
        /// after it and the seat never moved, so it was the handle being released, not a
        /// stroke. Anything that counted the Catch must undo it. Can arrive up to ~1.5s after
        /// the catch, or after a tracking gap. Carries the metrics as restored.
        /// </summary>
        public event Action<StrokeMetrics> CatchWithdrawn;

        /// <summary>Raised every frame a pose was available.</summary>
        public event Action<StrokeState> Tick;

        public StrokeState State { get; private set; }

        /// <summary>
        /// The raw pose that produced the current <see cref="State"/>. Exposed so a recorder
        /// can capture exactly what the detector saw, rather than polling the pose source a
        /// second time and getting a slightly different answer.
        /// </summary>
        public HandSample LastSample { get; private set; }
        public bool IsTracking => _source is XRHandsPoseSource live ? live.IsTracking : _hasPose;

        /// <summary>
        /// Why the source has no pose, for on-device diagnosis. Live tracking explains
        /// itself; any other source reports the label it was given in <see cref="SetSource"/>.
        /// </summary>
        public string SourceStatus =>
            _source is XRHandsPoseSource live ? live.Status
            : _source == null ? "no source"
            : _hasPose ? _sourceLabel
            : _sourceLabel + ", no pose";

        StrokeDetector _detector;
        IHandPoseSource _source;
        XRHandsPoseSource _live;

        /// <summary>
        /// An index fingertip from live hand tracking, for the hands-only menu (ADR-0013). Always
        /// the real hands, even while a recording is replayed: recordings carry palms only.
        /// </summary>
        public bool TryGetFingertip(bool left, out Vector3 worldPosition)
        {
            worldPosition = default;
            return _live != null && _live.TryGetFingertip(left, out worldPosition);
        }
        string _sourceLabel;
        bool _hasPose;

        /// <summary>
        /// Swaps the pose source at runtime. Used by the recorder's playback mode and by
        /// Editor tooling; the game itself normally leaves the live source in place.
        /// </summary>
        public void SetSource(IHandPoseSource source, string label = null)
        {
            _source = source;
            _sourceLabel = label ?? "custom source";
            _hasPose = false;
            _detector?.Reset();
        }

        void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;

            var settings = new StrokeDetectorSettings();
            if (overrideDefaults)
            {
                settings.VelocityThreshold = velocityThreshold;
                settings.MinStrokeLengthMetres = minStrokeLengthMetres;
            }

            _detector = new StrokeDetector(settings);
            _live = new XRHandsPoseSource(trackingOrigin, head);
            _source = _live;
        }

        void Update()
        {
            if (_source == null || !_source.TryGetPose(Time.timeAsDouble, out var sample))
            {
                _hasPose = false;
                return;
            }
            _hasPose = true;

            LastSample = sample;
            State = _detector.Update(
                sample.TimeSeconds,
                sample.LeftHand,
                sample.RightHand,
                sample.HeadPosition,
                sample.HeadForward);

            Tick?.Invoke(State);
            if (State.CatchWithdrawn) CatchWithdrawn?.Invoke(State.Metrics);

            // Catch and Finish surface for exactly one update each, so this needs no edge
            // detection of its own.
            switch (State.Phase)
            {
                case StrokePhase.Catch:
                    Catch?.Invoke(State.Metrics);
                    break;
                case StrokePhase.Finish:
                    Finish?.Invoke(State.Metrics);
                    break;
            }
        }
    }
}
