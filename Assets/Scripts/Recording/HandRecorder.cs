using System;
using System.IO;
using RowVR.Rowing;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Recording
{
    /// <summary>
    /// Records tracked hand and head poses on the headset and writes them as a .rowclip.
    ///
    /// This is the component that makes the fast loop possible. On Linux there is no Quest
    /// Link and no Meta XR Simulator, so every question about real tracking data would
    /// otherwise cost a full IL2CPP build and a headset session. Record the motion once,
    /// pull it with `make pull-recordings`, promote it with `make promote`, and from then
    /// on stroke detection can be developed against real data in about two seconds with the
    /// headset on the shelf.
    ///
    /// Clips land in Application.persistentDataPath, which on Quest is
    /// /sdcard/Android/data/&lt;package&gt;/files - the path tools/pull-recordings.sh reads.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class HandRecorder : MonoBehaviour
    {
        [Tooltip("Stop recording automatically after this many seconds. Keeps a forgotten recorder from filling storage.")]
        [SerializeField] float maxDurationSeconds = 300f;

        [Tooltip("Written into the clip's header to say what was being rowed.")]
        [SerializeField] string note = "";

        [Tooltip("Start and stop recording when a trigger file appears in / disappears from " +
                 "persistentDataPath. This is how takes are controlled on device: the app is " +
                 "hands-only, so there is no button to press. See tools/record.sh.")]
        [SerializeField] bool remoteControl = true;

        /// <summary>
        /// Trigger file, relative to persistentDataPath. Kept outside recordings/ so
        /// `make pull-recordings` never pulls it, and so the recordings directory is always
        /// created by the app itself - a directory created by adb's shell user is not
        /// writable by the app.
        /// </summary>
        public const string TriggerFileName = "record.cmd";

        const float TriggerPollSeconds = 0.25f;

        public bool IsRecording { get; private set; }
        public int SampleCount => _clip?.Samples.Count ?? 0;
        public float ElapsedSeconds => IsRecording ? (float)(Time.timeAsDouble - _startTime) : 0f;

        /// <summary>Raised when a clip is written, with its full path.</summary>
        public event Action<string> Saved;

        HandClip _clip;
        double _startTime;
        RowingRig _rig;
        StrokeReadout _readout;
        string _takeNote;
        bool _startedRemotely;
        float _nextTriggerPoll;

        // Set when a remotely started take hits the duration cap. The trigger file is still
        // present at that point, and without this the next poll would silently start a new
        // take - which is how one session of four sets became one 2-minute clip plus a
        // 2-second fragment, with no boundary between the sets.
        bool _cappedWhileTriggered;

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            _readout = GetComponent<StrokeReadout>();
        }

        public void StartRecording() => StartRecording(null);

        /// <param name="takeNote">
        /// Overrides the inspector note for this take. The remote trigger passes the take's
        /// name here, so each clip is labelled at the moment it is recorded rather than
        /// reconstructed from a timestamp afterwards.
        /// </param>
        public void StartRecording(string takeNote)
        {
            if (IsRecording) return;

            _takeNote = !string.IsNullOrWhiteSpace(takeNote) ? takeNote.Trim()
                      : !string.IsNullOrEmpty(note) ? note
                      : "recorded on device";

            _clip = new HandClip
            {
                Note = _takeNote,
                RecordedAtIso = DateTime.UtcNow.ToString("o"),
            };
            _startTime = Time.timeAsDouble;
            IsRecording = true;

            Debug.Log($"[RowVR] Recording started: {_takeNote}");
        }

        void Update()
        {
            if (_readout != null)
            {
                _readout.RecordingStatus = IsRecording
                    ? $"REC {ElapsedSeconds:F0}s  {SampleCount} samples  {_takeNote}"
                    : null;
            }

            if (!remoteControl || Time.unscaledTime < _nextTriggerPoll) return;
            _nextTriggerPoll = Time.unscaledTime + TriggerPollSeconds;

            var trigger = Path.Combine(Application.persistentDataPath, TriggerFileName);
            bool present;
            try { present = File.Exists(trigger); }
            catch (IOException) { return; }

            if (!present) _cappedWhileTriggered = false;

            if (present && !IsRecording && !_cappedWhileTriggered)
            {
                string takeNote = null;
                try { takeNote = File.ReadAllText(trigger); }
                catch (IOException e) { Debug.LogWarning($"[RowVR] Could not read {trigger}: {e.Message}"); }

                StartRecording(takeNote);
                _startedRemotely = true;
            }
            else if (!present && IsRecording && _startedRemotely)
            {
                _startedRemotely = false;
                StopRecording();
            }
        }

        /// <summary>Stops recording and writes the clip. Returns the path, or null if nothing was captured.</summary>
        public string StopRecording()
        {
            if (!IsRecording) return null;
            IsRecording = false;
            _startedRemotely = false;

            if (_clip == null || _clip.Samples.Count == 0)
            {
                Debug.LogWarning("[RowVR] Recording stopped with no samples - were the hands tracked?");
                return null;
            }

            var directory = Path.Combine(Application.persistentDataPath, "recordings");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"stroke-{DateTime.Now:yyyyMMdd-HHmmss}.rowclip");

            try
            {
                _clip.Save(path);
            }
            catch (IOException e)
            {
                Debug.LogError($"[RowVR] Could not write clip to {path}: {e.Message}");
                return null;
            }

            Debug.Log($"[RowVR] Wrote {_clip.Samples.Count} samples ({_clip.DurationSeconds:F1}s) to {path}");
            Debug.Log("[RowVR] Retrieve it with: make pull-recordings");
            Saved?.Invoke(path);
            return path;
        }

        public void Toggle()
        {
            if (IsRecording) StopRecording();
            else StartRecording();
        }

        void OnEnable()
        {
            if (_rig != null) _rig.Tick += OnTick;
        }

        void OnDisable()
        {
            if (_rig != null) _rig.Tick -= OnTick;

            // Losing the scene mid-take should not lose the take.
            if (IsRecording) StopRecording();
        }

        void OnTick(StrokeState _)
        {
            if (!IsRecording) return;

            if (ElapsedSeconds >= maxDurationSeconds)
            {
                Debug.Log($"[RowVR] Reached the {maxDurationSeconds:F0}s limit; stopping. " +
                          "Run 'make record-stop' then 'make record' for the next take.");
                _cappedWhileTriggered = _startedRemotely;
                StopRecording();
                return;
            }

            // Record the raw pose the rig actually used, not anything the detector derived
            // from it. The clip's whole purpose is to let detection be rewritten and retuned
            // later, and a clip of already-processed values could not support that.
            //
            // Taken from the rig rather than polled independently: polling the live
            // subsystem a second time would give a subtly different pose, and the clip would
            // no longer be a record of what the rower actually saw.
            _clip.Add(_rig.LastSample);
        }
    }
}
