using System;
using RowVR.Stroke;
using UnityEngine;

namespace RowVR.Rowing
{
    /// <summary>
    /// The blade going in and coming out, as sound (#25). Hands-only means no
    /// controller haptics, so this is what gives each stroke a physical beat.
    ///
    /// Plays on <see cref="RowingRig.Catch"/> and <see cref="RowingRig.Finish"/> on the frame
    /// the detector reports them. Android's audio output latency comes on top of that and is
    /// measured on the device (#66); the build asks for the smallest DSP buffer
    /// (ProjectBootstrap) to keep it down.
    ///
    /// A catch can be withdrawn up to ~1.5 s later when it turns out the hands let go of the
    /// handle (#38). Its catch sound has already played by then, and delaying every
    /// catch to wait would ruin the timing, so that is accepted. What is suppressed is the
    /// finish: one only sounds for a drive whose catch was heard and not taken back.
    ///
    /// The sounds are recordings of a paddle in a lake (#68): several takes of the
    /// entry for the catch and of a soft slosh for the finish, never the same take twice
    /// running. They live in Resources/Audio/Stroke, where SOURCES.md records their licence
    /// (CC0), and tools/stroke-sounds.py rebuilds them. If a folder is empty, the takes
    /// SoundSynth synthesises (engine-free, tested) stand in. They play from either side of
    /// the rower, roughly where the blades are, so they are spatialised.
    /// </summary>
    [RequireComponent(typeof(RowingRig))]
    public sealed class StrokeAudio : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float catchVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] float finishVolume = 0.45f;

        [Tooltip("Where the sounds come from, relative to the head: metres to each side, and below.")]
        [SerializeField] float sideOffset = 1.2f;
        [SerializeField] float belowHead = 0.6f;

        [Tooltip("Log each sound, for checking sound counts against the recordings in Play Mode.")]
        [SerializeField] bool logSounds;

        RowingRig _rig;
        AudioSource _left, _right;
        AudioClip[] _catchClips, _finishClips;
        readonly System.Random _variety = new System.Random(11);
        int _lastCatch = -1, _lastFinish = -1;

        const string CatchRecordings = "Audio/Stroke/Catch";
        const string FinishRecordings = "Audio/Stroke/Finish";
        bool _catchOpen;

        /// <summary>Sounds played since startup, for tests and diagnostics.</summary>
        public int CatchSounds { get; private set; }
        public int FinishSounds { get; private set; }

        void Awake()
        {
            _rig = GetComponent<RowingRig>();
            // Several takes of each, so no two strokes in a row sound identical.
            _catchClips = Takes(CatchRecordings, i => Clip("Catch " + i, SoundSynth.Catch(100 + i)));
            _finishClips = Takes(FinishRecordings, i => Clip("Finish " + i, SoundSynth.Finish(200 + i)));
            _left = CreateSource("Stroke Audio Left");
            _right = CreateSource("Stroke Audio Right");
        }

        void OnEnable()
        {
            _rig.Catch += OnCatch;
            _rig.Finish += OnFinish;
            _rig.CatchWithdrawn += OnCatchWithdrawn;
        }

        void OnDisable()
        {
            _rig.Catch -= OnCatch;
            _rig.Finish -= OnFinish;
            _rig.CatchWithdrawn -= OnCatchWithdrawn;
        }

        void OnDestroy()
        {
            if (_left != null) Destroy(_left.gameObject);
            if (_right != null) Destroy(_right.gameObject);
        }

        void LateUpdate()
        {
            if (!_rig.IsTracking) return;
            var sample = _rig.LastSample;
            var head = sample.HeadPosition.ToVector3();
            var forward = sample.HeadForward.ToVector3();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            var right = Vector3.Cross(Vector3.up, forward.normalized);
            var low = head + Vector3.down * belowHead;
            _left.transform.position = low - right * sideOffset;
            _right.transform.position = low + right * sideOffset;
        }

        void OnCatch(StrokeMetrics metrics)
        {
            _catchOpen = true;
            Play(Pick(_catchClips, ref _lastCatch), catchVolume);
            CatchSounds++;
            if (logSounds) Debug.Log($"[RowVR] sound: catch #{CatchSounds}");
        }

        void OnFinish(StrokeMetrics metrics)
        {
            if (!_catchOpen) return;
            _catchOpen = false;
            Play(Pick(_finishClips, ref _lastFinish), finishVolume);
            FinishSounds++;
            if (logSounds) Debug.Log($"[RowVR] sound: finish #{FinishSounds}");
        }

        void OnCatchWithdrawn(StrokeMetrics metrics)
        {
            _catchOpen = false;
            if (logSounds) Debug.Log("[RowVR] sound: catch withdrawn, no finish will sound");
        }

        /// <summary>The recorded takes in a Resources folder, or four synthesised ones if it has none.</summary>
        static AudioClip[] Takes(string folder, Func<int, AudioClip> synthesise)
        {
            var recorded = Resources.LoadAll<AudioClip>(folder);
            if (recorded.Length > 0) return recorded;
            Debug.LogWarning($"[RowVR] No recordings in Resources/{folder}; using synthesised stroke sounds.");
            var takes = new AudioClip[4];
            for (var i = 0; i < takes.Length; i++) takes[i] = synthesise(i);
            return takes;
        }

        /// <summary>A take at random, but never the one that played last.</summary>
        AudioClip Pick(AudioClip[] takes, ref int last)
        {
            var index = _variety.Next(takes.Length);
            if (index == last && takes.Length > 1) index = (index + 1 + _variety.Next(takes.Length - 1)) % takes.Length;
            last = index;
            return takes[index];
        }

        void Play(AudioClip clip, float volume)
        {
            _left.PlayOneShot(clip, volume);
            _right.PlayOneShot(clip, volume);
        }

        static AudioSource CreateSource(string name)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 20f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            return source;
        }

        static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
